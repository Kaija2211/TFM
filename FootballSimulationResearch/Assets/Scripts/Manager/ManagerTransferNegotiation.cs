using System;
using System.Collections.Generic;
using UnityEngine;
using Manager.Save;
using Sim;

namespace Manager
{
    // Transfer bid/negotiation system (session 13) - replaces the old instant-buy
    // (OnBuyRowClicked used to move a player straight onto the squad in one click, see
    // HANDOFF session 12). Design locked in with Thomas before building: scout a
    // target first (own scout pool, separate from ManagerScouting's World Scouting/
    // Academy allowance - a deliberate choice, not sharing slots), pick a bid amount
    // from a shown range rather than a single fixed ask, the selling club's own squad
    // depth at that position feeds how reluctant they are to sell, the bid amount is
    // escrowed (deducted immediately, refunded on decline) rather than only leaving the
    // budget on acceptance, and an accepted bid needs a separate manual Sign action
    // (not an automatic transfer) so the manager gets a real "confirm and sign" beat -
    // Thomas's own phrasing for the flow he wanted.
    public class ManagerTransferNegotiation
    {
        public const int MaxConcurrentBids = 3;
        public const int MaxConcurrentTransferScouts = 2;

        // Session 13, second pass - Thomas's playtest surfaced a real bug: an accepted
        // bid left AwaitingSignature had no expiry, so the source player kept
        // developing/declining on their real club for as long as the deal sat unsigned
        // (proved live: 4 unsigned seasons moved a target's Overall by several points on
        // its own) - almost certainly the cause of a separately-reported "paid £100m,
        // player arrived 60-rated" bug. Same fix shape as ManagerScouting's poach timer:
        // sign within a window or the deal falls through and you're refunded.
        public const int MatchdaysUntilSignatureExpires = 3;

        public enum BidStatus { PendingResponse, AwaitingSignature }

        public class PendingBid
        {
            public PlayerAgent Player;
            public float BidAmount;
            public int ResolveMatchday;
            public string SourceTeamName;
            public BidStatus Status;

            // Set the matchday Status flips to AwaitingSignature - the deadline for
            // TrySign is AcceptedMatchday + MatchdaysUntilSignatureExpires, not
            // ResolveMatchday (which only ever meant "when does the accept/decline roll
            // happen").
            public int AcceptedMatchday;
        }

        private readonly Dictionary<PlayerAgent, PendingBid> bidsByPlayer = new();

        // Separate from ManagerScouting's own scoutedPlayers/assignmentResolveMatchday -
        // Thomas's explicit call (session 13 design questions) was a separate allowance
        // rather than sharing World Scouting/Academy's MaxConcurrentAssignments=2 pool,
        // so scouting a transfer target never blocks scouting a youth prospect or vice
        // versa. Only ever used for regular AI-squad players - scouted prospects already
        // go through ManagerScouting's own gate (a prospect only even appears in the Buy
        // list once ManagerScouting.IsScouted is true, see RefreshTransferMarketBuyList),
        // so this pool would have nothing left to gate for them.
        private readonly HashSet<PlayerAgent> transferScoutedPlayers = new();
        private readonly Dictionary<PlayerAgent, int> transferScoutAssignmentResolveMatchday = new();

        // Called from ApplySaveData alongside squadRolesByTeamName.Clear()/academy.
        // Clear()/etc. - every AI squad regenerates fresh on load (see ManagerSaveData's
        // own comment), so any PlayerAgent reference held here from before the load is
        // already dangling. The escrowed money itself is refunded separately via
        // ManagerSaveData.PendingBidRefundOnLoad, not by this method.
        public void Clear()
        {
            bidsByPlayer.Clear();
            transferScoutedPlayers.Clear();
            transferScoutAssignmentResolveMatchday.Clear();
        }

        public bool HasPendingBid(PlayerAgent player) => bidsByPlayer.ContainsKey(player);
        public PendingBid GetPendingBid(PlayerAgent player) => bidsByPlayer.TryGetValue(player, out PendingBid bid) ? bid : null;
        public int PendingBidCount => bidsByPlayer.Count;

        public bool IsTransferScouted(PlayerAgent player) => transferScoutedPlayers.Contains(player);
        public bool IsTransferScoutAssigned(PlayerAgent player) => transferScoutAssignmentResolveMatchday.ContainsKey(player);
        public int ActiveTransferScoutAssignmentCount => transferScoutAssignmentResolveMatchday.Count;

        // Total £m currently tied up in escrow across every pending/awaiting-signature
        // bid - used both for the Transfer Market header ("£Xm committed") and to
        // compute ManagerSaveData.PendingBidRefundOnLoad at save time.
        public float GetTotalEscrowed()
        {
            float total = 0f;
            foreach (PendingBid bid in bidsByPlayer.Values) total += bid.BidAmount;
            return total;
        }

        public bool TryAssignTransferScout(PlayerAgent target, int currentMatchdayIndex)
        {
            if (transferScoutedPlayers.Contains(target) || transferScoutAssignmentResolveMatchday.ContainsKey(target))
            {
                return false;
            }

            if (transferScoutAssignmentResolveMatchday.Count >= MaxConcurrentTransferScouts)
            {
                return false;
            }

            // Resolves one matchday later - same cadence as ManagerScouting.
            // TryAssignScout, a familiar established pattern rather than a new one.
            transferScoutAssignmentResolveMatchday[target] = currentMatchdayIndex + 1;
            return true;
        }

        // Undo for an in-progress (not yet resolved) transfer scout assignment - Thomas,
        // session 13: "I accidentally started scouting a player I didn't want and
        // couldn't undo it." No cost was ever paid to assign a scout (only the slot
        // itself is the resource), so cancelling is a pure no-cost free the slot.
        public bool CancelTransferScout(PlayerAgent target)
        {
            return transferScoutAssignmentResolveMatchday.Remove(target);
        }

        // Called from the same matchday-tick hooks as ManagerScouting.
        // ResolveDueAssignments (both the single-match Continue path and the auto-
        // resolved Simulate Season loop). getSellingTeam looks up the target's current
        // club so the scouting report can quote a real recommended bid - a Func rather
        // than a held reference, matching ManagerScouting taking AgentSquadGenerator as
        // a parameter instead of caching one.
        public void ResolveDueTransferScoutAssignments(int currentMatchdayIndex, ManagerInbox inbox, Func<PlayerAgent, AgentTeam> getSellingTeam)
        {
            List<PlayerAgent> resolved = new List<PlayerAgent>();

            foreach (KeyValuePair<PlayerAgent, int> entry in transferScoutAssignmentResolveMatchday)
            {
                if (currentMatchdayIndex >= entry.Value) resolved.Add(entry.Key);
            }

            foreach (PlayerAgent player in resolved)
            {
                transferScoutAssignmentResolveMatchday.Remove(player);
                transferScoutedPlayers.Add(player);

                AgentTeam sourceTeam = getSellingTeam?.Invoke(player);
                float recommendedBid = GetRecommendedBid(player, sourceTeam);

                string body = $"Scouting report on {player.Name} ({player.PrimaryPosition}, age {player.Age}) is in. " +
                    $"True Overall {Mathf.RoundToInt(player.GetOverallRating())}. " +
                    $"Our scout reckons a bid around £{recommendedBid:F1}m gives you a real shot - head to the Transfer Market to make an offer.";

                inbox.Add(InboxMessageType.ScoutingReport, $"Scouting Report: {player.Name}", body, currentMatchdayIndex);
            }
        }

        // A club is much harder to buy from if the target is clearly their best option
        // at that position (few/no close replacements in their own squad), easier if
        // they've got real depth there. sourceTeam null (no selling club resolvable)
        // reads as zero reluctance - this class only ever bids on regular AI-squad
        // players now (session 13 Youth rework routes every scouted prospect through
        // the Academy instead, never through here), so null is effectively unreachable
        // in practice, just handled defensively.
        private static float ComputeDepthReluctance(PlayerAgent target, AgentTeam sourceTeam)
        {
            if (sourceTeam == null) return 0f;

            float targetOverall = target.GetOverallRating();
            float bestReplacementOverall = float.MinValue;

            foreach (PlayerAgent p in sourceTeam.Players)
            {
                if (p == target || !IsSamePositionGroup(p.PrimaryPosition, target.PrimaryPosition)) continue;

                float ovr = p.GetOverallRating();
                if (ovr > bestReplacementOverall) bestReplacementOverall = ovr;
            }

            if (bestReplacementOverall <= float.MinValue + 1f)
            {
                // No same-position replacement in the whole squad - the hardest case.
                return 1f;
            }

            // gap <= 0 (an equal-or-better replacement already on the books): minimal
            // reluctance. gap >= 15 (nobody close): near-maximum reluctance. 15 chosen
            // as roughly "a squad player vs. a genuine star" gap on this project's own
            // 0-99 Overall scale (see the session 12 attribute-overhaul calibration).
            float gap = targetOverall - bestReplacementOverall;
            return Mathf.Clamp01(gap / 15f);
        }

        // Widened from a strict PrimaryPosition tag match (session 16 playtest finding -
        // a club with exactly one player tagged "DM" showed maximum reluctance even with
        // several covering CMs/CBs on the books, badly inflating price for anyone in a
        // thinly-tagged exact slot). Grouped by the same back/midfield/attack banding a
        // manager would actually think in, not exact-position realism.
        private static bool IsSamePositionGroup(PlayerPosition a, PlayerPosition b)
        {
            if (a == b) return true;

            bool IsDefense(PlayerPosition p) =>
                p == PlayerPosition.RB || p == PlayerPosition.CB || p == PlayerPosition.LB ||
                p == PlayerPosition.RWB || p == PlayerPosition.LWB;

            bool IsMidfield(PlayerPosition p) =>
                p == PlayerPosition.DM || p == PlayerPosition.CM || p == PlayerPosition.AM ||
                p == PlayerPosition.RM || p == PlayerPosition.LM;

            bool IsAttack(PlayerPosition p) =>
                p == PlayerPosition.RW || p == PlayerPosition.LW || p == PlayerPosition.ST;

            return (IsDefense(a) && IsDefense(b)) ||
                   (IsMidfield(a) && IsMidfield(b)) ||
                   (IsAttack(a) && IsAttack(b));
        }

        // Unscouted AI-squad players show a fuzzy Overall band on the Buy list rather
        // than the exact number - deterministic per player (seeded from PlayerId, via
        // System.Random, never the shared UnityEngine.Random stream) so the band stays
        // stable across UI refreshes instead of re-rolling every redraw. Same shape as
        // ManagerScouting.GetDisplayPotential, duplicated rather than shared since it
        // fuzzes a different stat (Overall, not Potential) for a different pool
        // (regular AI-squad players, not the youth scouting pool).
        public static string GetDisplayOverallBand(PlayerAgent player)
        {
            float trueOverall = player.GetOverallRating();

            System.Random fuzzRandom = new System.Random(player.PlayerId.GetHashCode());
            float noise = (float)(fuzzRandom.NextDouble() * 16f) - 8f;
            float fuzzyCenter = trueOverall + noise;

            int lowerBand = Mathf.Clamp(Mathf.FloorToInt((fuzzyCenter - 7f) / 5f) * 5, 1, 95);
            int upperBand = Mathf.Clamp(lowerBand + 15, lowerBand + 5, 99);

            return $"{lowerBand}-{upperBand}";
        }

        // Middle-of-the-road suggestion, not a guarantee - the real accept threshold at
        // resolve time is still randomised per bid (see RollAcceptance). Used both for
        // the scouting report's "recommended bid" line and as the default-selected
        // option in the bid-amount picker.
        public static float GetRecommendedBid(PlayerAgent target, AgentTeam sourceTeam)
        {
            float reluctance = ComputeDepthReluctance(target, sourceTeam);
            float marketValue = ManagerClubFinance.GetMarketValue(target);
            return marketValue * (1.0f + reluctance * 0.6f);
        }

        // Even a generous bid can be turned down outright sometimes, more so the more
        // reluctant the seller is. Above that flat refusal roll, the accept threshold
        // itself scales up with reluctance too - a reluctant seller needs to be paid
        // meaningfully over market value, not just avoid an outright "no." Mirrors the
        // shape of the old ManagerClubFinance.TryResolveBid, extended with the new
        // depth-reluctance term.
        private static bool RollAcceptance(PlayerAgent target, float bidAmount, float reluctance)
        {
            float flatRefusalChance = 0.05f + reluctance * 0.30f;
            if (UnityEngine.Random.value < flatRefusalChance) return false;

            float marketValue = ManagerClubFinance.GetMarketValue(target);
            float thresholdMultiplier = UnityEngine.Random.Range(0.85f, 1.2f) * (1f + reluctance * 0.6f);
            float acceptThreshold = marketValue * thresholdMultiplier;

            return bidAmount >= acceptThreshold;
        }

        // Escrows the bid amount immediately (deducted from budget on placement,
        // refunded on decline/walk-away, converted to a recorded spend on Sign) rather
        // than only touching the budget once resolved - Thomas's explicit call, so you
        // can't out-bid what you can actually afford across several pending targets.
        public bool TryPlaceBid(PlayerAgent target, float amount, string sourceTeamName, int currentMatchdayIndex, ManagerClubFinance finance, string managedTeamName)
        {
            if (bidsByPlayer.ContainsKey(target) || bidsByPlayer.Count >= MaxConcurrentBids)
            {
                return false;
            }

            if (finance.GetBudget(managedTeamName) < amount)
            {
                return false;
            }

            finance.AdjustBudget(managedTeamName, -amount);

            bidsByPlayer[target] = new PendingBid
            {
                Player = target,
                BidAmount = amount,
                ResolveMatchday = currentMatchdayIndex + 1,
                SourceTeamName = sourceTeamName,
                Status = BidStatus.PendingResponse
            };

            return true;
        }

        // Called from the same matchday-tick hooks as ManagerScouting.
        // ResolveMatchdayTick. getSellingTeam returns null if the AI club can no longer
        // be resolved, which ComputeDepthReluctance already treats as "no depth
        // information available" (reluctance 0).
        // Once a selling club's bench drops to this many players (from a starting 9),
        // they refuse every further sale outright regardless of position or price -
        // session 16, Thomas: "if they are at the point where they only have 5 bench
        // players, they also won't sell." A blanket depth floor on top of
        // WouldLeaveSquadTooThin's per-position check, since repeated sales that each
        // individually still leave "one player" at a position can still hollow out a
        // squad's overall depth long before any single position hits zero.
        private const int MinBenchDepthBeforeRefusingAllSales = 5;

        public void ResolveDueBids(int currentMatchdayIndex, ManagerClubFinance finance, string managedTeamName, ManagerInbox inbox, Func<PlayerAgent, AgentTeam> getSellingTeam)
        {
            List<PlayerAgent> due = new List<PlayerAgent>();

            foreach (KeyValuePair<PlayerAgent, PendingBid> entry in bidsByPlayer)
            {
                if (entry.Value.Status == BidStatus.PendingResponse && currentMatchdayIndex >= entry.Value.ResolveMatchday)
                {
                    due.Add(entry.Key);
                }
            }

            foreach (PlayerAgent player in due)
            {
                PendingBid bid = bidsByPlayer[player];
                AgentTeam sourceTeam = getSellingTeam?.Invoke(player);

                // Session 16 - a hard "not for sale" refusal, checked before the normal
                // price/reluctance roll even runs. No replacement generation exists for
                // a transfer-out (unlike retirement, see ApplyRetirementsForTeam), so
                // without this a club could genuinely be bid down to zero players at a
                // position (a real crash risk - see PickGoalkeeper's unguarded
                // team.StartingEleven[0] fallback) or hollowed out entirely over a long
                // career of repeated poaching.
                bool tooThinToSell = WouldLeaveSquadTooThin(player, sourceTeam);
                bool accepted = !tooThinToSell && RollAcceptance(player, bid.BidAmount, ComputeDepthReluctance(player, sourceTeam));

                if (accepted)
                {
                    bid.Status = BidStatus.AwaitingSignature;
                    bid.AcceptedMatchday = currentMatchdayIndex;

                    string body = $"{bid.SourceTeamName} have accepted your £{bid.BidAmount:F1}m bid for {player.Name}. " +
                        $"Confirm to sign them within {MatchdaysUntilSignatureExpires} matchdays, or walk away and get your money back.";

                    inbox.Add(InboxMessageType.BidAccepted, $"Bid Accepted: {player.Name}", body, currentMatchdayIndex, actionPlayer: player);
                }
                else
                {
                    finance.AdjustBudget(managedTeamName, bid.BidAmount);
                    bidsByPlayer.Remove(player);

                    string body = tooThinToSell
                        ? $"{bid.SourceTeamName} won't even discuss selling {player.Name} - they don't have the squad depth to let them go. Your £{bid.BidAmount:F1}m has been refunded."
                        : $"{bid.SourceTeamName} have turned down your £{bid.BidAmount:F1}m bid for {player.Name}. " +
                          $"Your £{bid.BidAmount:F1}m has been refunded - you're free to try again.";

                    inbox.Add(InboxMessageType.BidDeclined, $"Bid Declined: {player.Name}", body, currentMatchdayIndex);
                }
            }
        }

        // sourceTeam null (unresolvable club) reads as "not too thin" - same
        // can't-check-so-don't-block posture ComputeDepthReluctance already takes,
        // and per its own comment this is effectively unreachable in practice anyway
        // (every bid target today is a regular AI-squad player).
        private static bool WouldLeaveSquadTooThin(PlayerAgent target, AgentTeam sourceTeam)
        {
            if (sourceTeam == null) return false;

            bool hasAnotherAtExactPosition = sourceTeam.Players.Exists(p => p != target && p.PrimaryPosition == target.PrimaryPosition);
            if (!hasAnotherAtExactPosition) return true;

            return sourceTeam.Bench.Count <= MinBenchDepthBeforeRefusingAllSales;
        }

        // Finalizes an accepted bid - the escrowed amount was already deducted at
        // TryPlaceBid time, so this only needs to record it as a real spend (Career
        // screen's Finance tab lifetime total) and free the pending-bid slot. Moving the
        // player onto the squad itself stays in ManagerPrototypeController (same
        // "remove from whichever source they actually came from" logic the old
        // OnBuyRowClicked used), since this class deliberately has no direct access to
        // squadsByTeamName/the scouting pools.
        public bool TrySign(PlayerAgent player, ManagerClubFinance finance, string managedTeamName, out PendingBid resolvedBid)
        {
            if (!bidsByPlayer.TryGetValue(player, out PendingBid bid) || bid.Status != BidStatus.AwaitingSignature)
            {
                resolvedBid = null;
                return false;
            }

            finance.RecordTransferSpend(managedTeamName, bid.BidAmount);
            bidsByPlayer.Remove(player);
            resolvedBid = bid;
            return true;
        }

        public bool TryWalkAway(PlayerAgent player, ManagerClubFinance finance, string managedTeamName)
        {
            if (!bidsByPlayer.TryGetValue(player, out PendingBid bid) || bid.Status != BidStatus.AwaitingSignature)
            {
                return false;
            }

            finance.AdjustBudget(managedTeamName, bid.BidAmount);
            bidsByPlayer.Remove(player);
            return true;
        }

        // Called from the same matchday-tick hooks as ResolveDueBids - an accepted deal
        // left unsigned for too long falls through on its own (refunded), rather than
        // sitting indefinitely while the source player keeps living/developing on their
        // real club underneath the deal (see MatchdaysUntilSignatureExpires' own
        // comment for the real bug this fixes).
        public void ResolveExpiredSignatures(int currentMatchdayIndex, ManagerClubFinance finance, string managedTeamName, ManagerInbox inbox)
        {
            List<PlayerAgent> expired = new List<PlayerAgent>();

            foreach (KeyValuePair<PlayerAgent, PendingBid> entry in bidsByPlayer)
            {
                if (entry.Value.Status == BidStatus.AwaitingSignature &&
                    currentMatchdayIndex - entry.Value.AcceptedMatchday >= MatchdaysUntilSignatureExpires)
                {
                    expired.Add(entry.Key);
                }
            }

            foreach (PlayerAgent player in expired)
            {
                PendingBid bid = bidsByPlayer[player];
                finance.AdjustBudget(managedTeamName, bid.BidAmount);
                bidsByPlayer.Remove(player);

                string body = $"{bid.SourceTeamName} have pulled out of the {player.Name} deal - you took too long to confirm. " +
                    $"Your £{bid.BidAmount:F1}m has been refunded.";

                inbox.Add(InboxMessageType.BidDeclined, $"Deal Fell Through: {player.Name}", body, currentMatchdayIndex);
            }
        }

        // Season rollover resets currentFixtureIndex back to 0, which would otherwise
        // strand a bid/scout assignment made near the end of a season with a now-
        // unreachable resolve matchday - mirrors ManagerScouting.ForceResolveAllPending
        // exactly (the report/response simply comes in over the off-season instead).
        // Also force-expires any still-AwaitingSignature deal rather than letting it
        // dangle across the reset (its AcceptedMatchday would otherwise be measured
        // against the NEW season's matchday 0 onward, silently granting it a fresh,
        // unintended signing window instead of actually expiring on schedule).
        public void ForceResolveAllPending(ManagerClubFinance finance, string managedTeamName, ManagerInbox inbox, Func<PlayerAgent, AgentTeam> getSellingTeam, int currentMatchdayIndex)
        {
            foreach (PlayerAgent player in transferScoutAssignmentResolveMatchday.Keys)
            {
                transferScoutedPlayers.Add(player);
            }
            transferScoutAssignmentResolveMatchday.Clear();

            List<PlayerAgent> pendingResponse = new List<PlayerAgent>();
            List<PlayerAgent> awaitingSignature = new List<PlayerAgent>();
            foreach (KeyValuePair<PlayerAgent, PendingBid> entry in bidsByPlayer)
            {
                if (entry.Value.Status == BidStatus.PendingResponse) pendingResponse.Add(entry.Key);
                else awaitingSignature.Add(entry.Key);
            }

            foreach (PlayerAgent player in pendingResponse)
            {
                bidsByPlayer[player].ResolveMatchday = currentMatchdayIndex;
            }

            foreach (PlayerAgent player in awaitingSignature)
            {
                bidsByPlayer[player].AcceptedMatchday = currentMatchdayIndex - MatchdaysUntilSignatureExpires;
            }

            ResolveDueBids(currentMatchdayIndex, finance, managedTeamName, inbox, getSellingTeam);
            ResolveExpiredSignatures(currentMatchdayIndex, finance, managedTeamName, inbox);
        }
    }
}
