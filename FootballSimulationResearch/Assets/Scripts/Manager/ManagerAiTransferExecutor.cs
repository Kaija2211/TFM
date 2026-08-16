using System.Collections.Generic;
using System.Linq;
using Sim;

namespace Manager
{
    // AI transaction layer (roadmap: the first AI-club work that actually completes a
    // transfer, building on ManagerAiSquadDepthEvaluator + ManagerAiTransferTargetSearch
    // + ManagerClubFinance). Deliberately AI-to-AI only in this first slice - never
    // touches the managed team's squad, transfer market, or budget, so it can't yet
    // compete with the human for a target or otherwise reach into human-visible state.
    // That's a real future design decision (should AI clubs be able to outbid the
    // human?), not something to answer silently inside this first pass.
    public static class ManagerAiTransferExecutor
    {
        // Mirrors ManagerAiSquadDepthEvaluator's own scoring - a club only goes
        // shopping if it has a genuine problem, not a marginal one. Calibrated
        // against that service's own statistical audit findings, not guessed: across
        // a real generated Premier League, weakest-position NeedScore is usually
        // exactly 0 (a genuinely balanced squad) with only occasional small positive
        // spikes (observed example: 1.28) - a naive round-number threshold like 10
        // would never fire at all against real generated squads, confirmed the hard
        // way when this audit's own full-league pass completed zero transfers at
        // that value. Most clubs most seasons will still do nothing, which is the
        // intended, realistic behaviour - this only needed to stop screening out
        // every real gap that actually exists.
        private const float MinimumNeedScoreToShop = 0.5f;

        // A sale must leave the selling club with at least this many other adequately-
        // fit players at the sold player's own primary position - a basic "don't gut
        // your own squad" guard, not a full simulation of selling reluctance (a real
        // club might also refuse to sell its best player even with backup cover -
        // future refinement, see BACKLOG).
        private const int MinimumSellerCoverAfterSale = 1;
        private const float MinimumFitForSellerCover = 0.80f;

        public readonly struct CompletedTransfer
        {
            public readonly PlayerAgent Player;
            public readonly string SellingClubName;
            public readonly string BuyingClubName;
            public readonly float Fee;

            public CompletedTransfer(PlayerAgent player, string sellingClubName, string buyingClubName, float fee)
            {
                Player = player;
                SellingClubName = sellingClubName;
                BuyingClubName = buyingClubName;
                Fee = fee;
            }
        }

        // otherClubs should already exclude the managed team and the buying club
        // itself. Tries each candidate target in ranked order (best suitability
        // first) until one is actually affordable and sellable, rather than only ever
        // considering the single top-ranked target - a club priced out of its first
        // choice still looks at its second, same as real recruitment.
        public static CompletedTransfer? TryExecuteTransfer(
            AgentTeam buyingClub,
            List<PlayerPosition> buyingClubRelevantPositions,
            List<AgentTeam> otherClubs,
            ManagerClubFinance finance)
        {
            ManagerAiSquadDepthEvaluator.SquadDepthReport depthReport = ManagerAiSquadDepthEvaluator.Evaluate(buyingClub, buyingClubRelevantPositions);
            ManagerAiSquadDepthEvaluator.PositionDepth weakest = depthReport.Positions.First(p => p.Position == depthReport.WeakestPosition);
            if (weakest.NeedScore < MinimumNeedScoreToShop)
            {
                return null;
            }

            List<ManagerAiTransferTargetSearch.TransferTarget> targets = ManagerAiTransferTargetSearch.FindTargets(
                depthReport.WeakestPosition, weakest.BestOverall, otherClubs, maxResults: 10);

            float buyerBudget = finance.GetBudget(buyingClub.TeamName);

            foreach (ManagerAiTransferTargetSearch.TransferTarget candidate in targets)
            {
                AgentTeam sellingClub = otherClubs.FirstOrDefault(c => c.TeamName == candidate.CurrentClubName);
                if (sellingClub == null)
                {
                    continue;
                }

                // First slice deliberately only ever sells bench/reserve depth, never
                // a current starter - AgentTeam.RemovePlayer shrinks StartingEleven in
                // place, which would break the formation-slot index alignment
                // ChangeFormation/ManagerAiSquadRotation rely on. Real star-player
                // sales are a future refinement, not this slice (see BACKLOG).
                if (sellingClub.StartingEleven.Contains(candidate.Player))
                {
                    continue;
                }

                float fee = ManagerClubFinance.GetMarketValue(candidate.Player);
                if (buyerBudget < fee)
                {
                    continue;
                }

                if (!SellingClubRetainsAdequateCover(sellingClub, candidate.Player))
                {
                    continue;
                }

                sellingClub.RemovePlayer(candidate.Player);
                buyingClub.AddSquadPlayer(candidate.Player);
                finance.AdjustBudget(buyingClub.TeamName, -fee);
                finance.AdjustBudget(sellingClub.TeamName, fee);

                return new CompletedTransfer(candidate.Player, sellingClub.TeamName, buyingClub.TeamName, fee);
            }

            return null;
        }

        private static bool SellingClubRetainsAdequateCover(AgentTeam sellingClub, PlayerAgent playerBeingSold)
        {
            int remainingCover = 0;
            foreach (PlayerAgent player in sellingClub.Players)
            {
                if (player == playerBeingSold)
                {
                    continue;
                }

                if (player.GetPositionFit(playerBeingSold.PrimaryPosition) >= MinimumFitForSellerCover)
                {
                    remainingCover++;
                }
            }

            return remainingCover >= MinimumSellerCoverAfterSale;
        }
    }
}
