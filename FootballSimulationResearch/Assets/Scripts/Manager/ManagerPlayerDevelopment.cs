using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Sim;

namespace Manager
{
    // Season-rollover attribute growth/decline toward Potential, plus retirement -
    // Phase 1 of the career arc (session 8: progression/scouting/transfers/incentives).
    // Mutates existing PlayerAgent instance FIELD VALUES only, on objects Manager Mode
    // already owns (never shared with Research Mode's own separately-generated
    // instances) - no PlayerAgent.cs logic touched, same reasoning as every other
    // Manager-only system in this codebase.
    //
    // Deliberately doesn't try to replicate PlayerAgent.GetOverallRating()'s per-
    // position weight tables exactly (those are private and there are twelve of them) -
    // instead spreads growth/decline across the same broad technical/mental/physical
    // pool every position's Overall actually reads from, at different relative rates
    // (physical erodes fastest with age, "reading the game" barely erodes at all - see
    // DeclineOutfieldAttributes). Good enough to move Overall in the right direction
    // realistically without hand-maintaining twelve duplicate weight tables here.
    public static class ManagerPlayerDevelopment
    {
        private const int VeteranRetirementAge = 35;

        // Growth glide-path targets (session 9 fix - see HANDOFF) - by PeakDevelopmentAge,
        // a linear "remaining headroom / remaining seasons" schedule guarantees closure
        // regardless of playing time, instead of the old youthFactor-tapering-to-zero-at-
        // 24 shape, which could (and did, confirmed by live simulation) leave a real
        // wonderkid permanently several points short of their own Potential once
        // youthFactor hit exactly zero. GrowthEligibleUntilAge matches where
        // veteranFactor first turns nonzero below, so growth and decline hand off
        // cleanly with no age gap where neither applies.
        private const float PeakDevelopmentAge = 26f;
        private const int GrowthEligibleUntilAge = 30;

        // Elite aging curve extension (session 9 - Thomas: "I'd say Harry Kane has only
        // gotten better since turning 30" - a genuine elite player's whole career
        // timeline should shift later, not just decline on the same schedule as an
        // average pro). Scales with CURRENT Overall, not Potential - Potential can now
        // shrink from neglect erosion, so it no longer cleanly reflects genuine talent
        // tier the way current ability does. A player at Overall<=80 gets zero
        // extension (identical to every formula below as it was before this change); a
        // genuine 95+ Overall superstar gets the full +5 years tacked onto every age
        // threshold - still growing until ~35, peaking around 31, not declining until
        // ~34, instead of the flat 29/30 cutoff everyone used to share.
        private const float EliteAgingExtensionYears = 5f;

        private static float GetAgingCurveOffset(PlayerAgent player)
        {
            float eliteFactor = Mathf.Clamp01((player.GetOverallRating() - 80f) / 15f);
            return eliteFactor * EliteAgingExtensionYears;
        }

        private static float GetGrowthEligibleUntilAge(PlayerAgent player)
        {
            return GrowthEligibleUntilAge + GetAgingCurveOffset(player);
        }

        private static float GetPeakDevelopmentAge(PlayerAgent player)
        {
            return PeakDevelopmentAge + GetAgingCurveOffset(player);
        }

        // Same 8-year decline ramp shape as before, just with the onset age (was a flat
        // 29 for everyone) shifted later for elite players.
        private static float GetVeteranFactor(PlayerAgent player)
        {
            float declineOnsetAge = 29f + GetAgingCurveOffset(player);
            return Mathf.Clamp01((player.Age - declineOnsetAge) / 8f);
        }

        // Potential erosion (session 9 - explicit design call from Thomas: a wonderkid
        // genuinely starved of game time for years shouldn't just develop slower, they
        // should permanently never become what they once could have - real stakes for
        // neglecting a prospect instead of loaning them out or giving them minutes).
        // 0.3 == roughly 8 appearances in a season (playingTimeFactor is appearances/25,
        // see ManagerPrototypeController's season-rollover loop) - confirmed with Thomas
        // as the right bar: normal squad rotation/cup games clear it easily, only a
        // player frozen out of the matchday squad all season triggers erosion. Above
        // this playing-time factor, no erosion at all.
        private const float NeglectPlayingTimeThreshold = 0.3f;
        private const float MaxPotentialErosionPerSeason = 1.5f;

        // Delta display (career arc backlog item, session 9) - keyed by PlayerAgent
        // reference rather than a PlayerAgent field, following the same "new Manager-
        // only per-player state lives in a Manager-namespace class, never on
        // PlayerAgent" pattern as ManagerScouting's scoutedPlayers/assignmentResolveMatchday
        // (see PROJECT_CONTEXT_FOR_AI.md). Not persisted through save/load - a fresh
        // "no change yet" state after loading a career is an acceptable, already-
        // precedented scope limit (Condition/injuries/appearances reset the same way).
        // Stored in DISPLAY-rating terms (the stretched value the UI actually shows next
        // to a player's OVR), not raw GetOverallRating() - duplicates ManagerPrototype
        // Controller.GetDisplayRating's tiny stretch formula rather than sharing it,
        // same precedent as ClampAllAttributes below mirroring AgentSquadGenerator's
        // clamp logic instead of exposing it.
        private static readonly Dictionary<PlayerAgent, int> lastSeasonOverallDelta = new();

        public static int GetLastSeasonOverallDelta(PlayerAgent player)
        {
            return lastSeasonOverallDelta.TryGetValue(player, out int delta) ? delta : 0;
        }

        // Per-matchday ticks (session 9 backlog item - Thomas: spread growth across the
        // season instead of one lump at rollover). Only the managed squad gets ticked
        // this way (see ApplyMatchdayConditionAndInjuries, the same per-matchday hook
        // Condition already uses) - AI clubs/reserves/youth pools have no real per-
        // matchday signal, so they keep going through the original once-a-season
        // ApplySeasonProgression below, completely unchanged.
        //
        // Since ticking decouples "when growth happens" from "when the season-delta
        // badge gets measured," delta tracking is split into its own snapshot/finalize
        // pair (SnapshotSeasonStart/FinalizeSeasonDelta) rather than living inside the
        // growth call itself - the managed team's season-rollover loop calls Finalize
        // (closing out the season that just ended) then Snapshot (opening the next one)
        // for every player, regardless of whether their growth came from ticks or a
        // lump sum.
        private const int AssumedMatchdaysPerSeason = 38;
        private static readonly Dictionary<PlayerAgent, int> seasonStartDisplayRating = new();

        public static void SnapshotSeasonStart(PlayerAgent player)
        {
            seasonStartDisplayRating[player] = GetDisplayRating(player.GetOverallRating());
        }

        public static void FinalizeSeasonDelta(PlayerAgent player)
        {
            int before = seasonStartDisplayRating.TryGetValue(player, out int b) ? b : GetDisplayRating(player.GetOverallRating());
            lastSeasonOverallDelta[player] = GetDisplayRating(player.GetOverallRating()) - before;
        }

        // Live in-season delta (Thomas: "a visual next to their OVR delta badge...
        // in real time" - the mid-season progress gap called out in HANDOFF). Unlike
        // GetLastSeasonOverallDelta (frozen at whatever it read at the last rollover,
        // i.e. stale for the entire following season), this reads straight off the
        // same seasonStartDisplayRating snapshot FinalizeSeasonDelta uses, computed
        // fresh on every call instead of cached - so it climbs as matchday growth
        // ticks land, and drops back to 0 the instant SnapshotSeasonStart re-baselines
        // for the new season (Finalize then Snapshot run back-to-back at rollover).
        public static int GetCurrentSeasonOverallDelta(PlayerAgent player)
        {
            int before = seasonStartDisplayRating.TryGetValue(player, out int b) ? b : GetDisplayRating(player.GetOverallRating());
            return GetDisplayRating(player.GetOverallRating()) - before;
        }

        // Growth/decline only - NOT erosion. Erosion (ApplySeasonEndErosion below) stays
        // a season-END-only calculation using the season's true aggregate playing time,
        // deliberately not translated to a per-matchday check: a player who starts 20 of
        // 38 games has 18 "unplayed" matchdays, and a naive per-matchday erosion check
        // would treat every one of those as neglect, savaging a normally-rotated
        // player's Potential for no real reason. Growth/decline don't have that failure
        // mode - decline was already playing-time-INDEPENDENT even in the original
        // formula, and growth's playing-time multiplier only ever swings between 0.7x-
        // 1.0x (the same floor-protected shape as the season version), so ticking it off
        // a single match's played/not-played signal is safe.
        // moraleGrowthMultiplier (session 10 - Thomas: morale shouldn't touch match
        // performance, it should touch development) - optional and defaulted to 1f
        // (no effect) so this stays a pure no-op for any caller that doesn't have real
        // morale data to pass; only the managed team's own per-matchday tick (the only
        // caller of this method - see ApplyMatchdayConditionAndInjuries) ever passes a
        // real value, from ManagerSquadRoles.GetMoraleGrowthMultiplier. Only applied to
        // the GROWTH branch below, never decline - see GetMoraleGrowthMultiplier's own
        // comment for why.
        // focusAttributes (session 16 - academy prospects moved from a once-a-season
        // lump (ApplySeasonProgression) to this same per-matchday tick, so their
        // focus-stat doubling needed to carry over too, exactly like ApplySeasonProgression
        // already threads it through to Grow*Attributes). Optional and defaulted to null
        // so the managed team's own matchday tick (which never had a focus set) is
        // unaffected.
        public static void ApplyMatchdayProgression(PlayerAgent player, bool playedThisMatchday, float moraleGrowthMultiplier = 1f, IReadOnlyCollection<string> focusAttributes = null)
        {
            float headroom = player.Potential - player.GetOverallRating();
            bool isGoalkeeper = player.PrimaryPosition == PlayerPosition.GK;

            if (headroom > 0f && player.Age < GetGrowthEligibleUntilAge(player))
            {
                float matchdayPlayingTimeFactor = playedThisMatchday ? 1f : 0f;
                float seasonsRemainingToPeak = Mathf.Max(1f, GetPeakDevelopmentAge(player) - player.Age + 1f);
                float seasonGrowth = (headroom / seasonsRemainingToPeak) * (0.7f + matchdayPlayingTimeFactor * 0.3f);
                float growth = (seasonGrowth / AssumedMatchdaysPerSeason) * moraleGrowthMultiplier;

                if (isGoalkeeper) GrowGoalkeeperAttributes(player, growth, focusAttributes);
                else GrowOutfieldAttributes(player, growth, focusAttributes);
            }
            else
            {
                float veteranFactor = GetVeteranFactor(player);

                if (veteranFactor > 0f)
                {
                    float seasonDecline = 3f + veteranFactor * 5f;
                    float decline = seasonDecline / AssumedMatchdaysPerSeason;

                    if (isGoalkeeper) DeclineGoalkeeperAttributes(player, decline, veteranFactor);
                    else DeclineOutfieldAttributes(player, decline, veteranFactor);
                }

                // Prime-age noise deliberately NOT ticked here - a ±1.5 random nudge
                // applied 38 times a season would just add variance, not a meaningful
                // signal. Still applied once at season rollover instead, see
                // ApplySeasonEndNoiseIfPrimeAge.
            }

            ClampAllAttributes(player);
        }

        public enum MatchFormOutcome { Win, Draw, Loss }

        // Cheap match-performance proxy (session 9 backlog item - Thomas: scale growth
        // speed with match *performance*, not just playing time). No live in-match
        // rating system exists yet, so this uses the two signals the match sim already
        // produces: goals scored (AgentMatchEvent.ScorerName) and the team result while
        // the player was on the pitch - not the fuller "goals/assists/team result"
        // wishlist, since assists aren't tracked anywhere in the sim and adding that
        // felt like a bigger, separate change from just wiring up what already exists.
        //
        // A small ADDITIVE nudge on top of whatever ApplyMatchdayProgression already
        // ticked for this same matchday (called separately, post-match, from
        // ApplyFixtureResult - the pre-match tick can't know the result yet). Deliberately
        // small relative to the base weekly tick (a hat-trick in a win is roughly a
        // strong week, not a whole extra season) and floor-clamped at zero - a bad
        // result never actively erodes progress on top of a loss, it just withholds the
        // bonus. Only applies to players still in their growth window, so a veteran's
        // hot streak doesn't reverse their decline.
        public static void ApplyMatchFormBonus(PlayerAgent player, int goalsThisMatch, MatchFormOutcome outcome)
        {
            float headroom = player.Potential - player.GetOverallRating();

            if (headroom <= 0f || player.Age >= GetGrowthEligibleUntilAge(player))
            {
                return;
            }

            float bonus = goalsThisMatch * 0.03f;
            bonus += outcome == MatchFormOutcome.Win ? 0.02f : outcome == MatchFormOutcome.Loss ? -0.015f : 0f;
            bonus = Mathf.Max(0f, bonus);

            if (bonus <= 0f)
            {
                return;
            }

            if (player.PrimaryPosition == PlayerPosition.GK) GrowGoalkeeperAttributes(player, bonus);
            else GrowOutfieldAttributes(player, bonus);

            ClampAllAttributes(player);
        }

        // Extracted from the erosion block that used to live inside ApplySeasonProgression
        // - same exact formula, called once per season at rollover for the managed team
        // (which now gets growth via matchday ticks instead) using the real final
        // seasonPlayingTimeFactor, instead of every matchday off a binary signal (see
        // ApplyMatchdayProgression's comment for why that would be wrong).
        public static void ApplySeasonEndErosion(PlayerAgent player, float seasonPlayingTimeFactor)
        {
            seasonPlayingTimeFactor = Mathf.Clamp01(seasonPlayingTimeFactor);

            if (seasonPlayingTimeFactor < NeglectPlayingTimeThreshold && player.Age < GetGrowthEligibleUntilAge(player) && player.Potential > player.GetOverallRating())
            {
                float neglectFactor = (NeglectPlayingTimeThreshold - seasonPlayingTimeFactor) / NeglectPlayingTimeThreshold;
                player.Potential = Mathf.Max(player.GetOverallRating(), player.Potential - neglectFactor * MaxPotentialErosionPerSeason);
            }
        }

        // Mirrors ApplySeasonProgression's own "else" branch exactly - a managed-team
        // player who's already closed their headroom (or aged out of the growth window)
        // but isn't yet in decline gets the same once-a-season noise nudge as everyone
        // else, since matchday ticks deliberately skip it (see ApplyMatchdayProgression).
        public static void ApplySeasonEndNoiseIfPrimeAge(PlayerAgent player)
        {
            float headroom = player.Potential - player.GetOverallRating();
            bool stillGrowing = headroom > 0f && player.Age < GetGrowthEligibleUntilAge(player);

            if (stillGrowing)
            {
                return;
            }

            float veteranFactor = GetVeteranFactor(player);

            if (veteranFactor > 0f)
            {
                return;
            }

            ApplySmallPrimeAgeNoise(player, player.PrimaryPosition == PlayerPosition.GK);
            ClampAllAttributes(player);
        }

        // playingTimeFactor is 0-1, supplied by the caller since only the managed
        // team's appearances are actually tracked (see ManagerSquadRoles) - callers pass
        // a real per-player value for the managed squad and a flat assumed value for
        // everyone else (AI clubs' first team vs. uncalled reserves), rather than this
        // method needing to know which kind of player it's looking at.
        // focusAttributes (backlog item, session 10) - up to 3 attribute names (see
        // ManagerAcademy.GetFocusableAttributes) that grow at double rate this season.
        // Optional and defaulted to null so every non-academy caller (AI first team,
        // uncalled reserves, unsigned scouting youth) is completely unaffected - only
        // ManagerAcademy's own prospects ever have a real focus set to pass in.
        //
        // exemptFromErosion (bug fix, session 13 - caught while answering an unrelated
        // question about academy growth rate): the neglect-erosion block below exists
        // to punish a SENIOR player who could be given minutes or loaned out but isn't
        // (see its own comment). Academy/unclaimed-youth-prospect callers pass a low
        // playingTimeFactor purely as a growth-rate throttle (these players structurally
        // can't have real senior appearances at their age), but that same low value was
        // ALSO tripping the erosion threshold every single season - a 14-year-old
        // academy kid was having their Potential permanently ground down every year
        // just for existing in the pool, the exact opposite of what a development
        // pipeline should do. Defaults false so every other caller (AI first team,
        // uncalled reserves, loan returns) keeps the original, intentional behavior.
        public static void ApplySeasonProgression(PlayerAgent player, float playingTimeFactor, IReadOnlyCollection<string> focusAttributes = null, bool exemptFromErosion = false)
        {
            playingTimeFactor = Mathf.Clamp01(playingTimeFactor);

            int displayedOverallBefore = GetDisplayRating(player.GetOverallRating());

            float veteranFactor = GetVeteranFactor(player);

            // Erode Potential itself before computing this season's headroom - real
            // neglect (bench-warming, never loaned out) permanently shrinks the ceiling,
            // it doesn't just slow the approach to it. Scales with how far below the
            // threshold playing time was (near-zero minutes erodes fastest); stops
            // entirely once real minutes resume, or once there's no headroom left to
            // erode. Confirmed live: 10 seasons of zero playing time eroded a 90
            // Potential down to 75 while Overall only reached 74.6 - a real, permanent
            // cost, not a cosmetic slowdown (see HANDOFF).
            if (!exemptFromErosion && playingTimeFactor < NeglectPlayingTimeThreshold && player.Age < GetGrowthEligibleUntilAge(player) && player.Potential > player.GetOverallRating())
            {
                float neglectFactor = (NeglectPlayingTimeThreshold - playingTimeFactor) / NeglectPlayingTimeThreshold;
                player.Potential = Mathf.Max(player.GetOverallRating(), player.Potential - neglectFactor * MaxPotentialErosionPerSeason);
            }

            float headroom = player.Potential - player.GetOverallRating();

            bool isGoalkeeper = player.PrimaryPosition == PlayerPosition.GK;

            if (headroom > 0f && player.Age < GetGrowthEligibleUntilAge(player))
            {
                // Linear glide-path: divide remaining headroom by remaining seasons to
                // PeakDevelopmentAge, so the required rate ramps up automatically if
                // early seasons under-delivered, converging on full closure of WHATEVER
                // headroom remains instead of decaying toward it forever. Playing time
                // still shapes how much of that season's target actually lands (0.7-1.0x)
                // on top of the erosion above - a neglected player still grows toward
                // their (now-shrunk) ceiling, just doesn't get to keep the original one.
                float seasonsRemainingToPeak = Mathf.Max(1f, GetPeakDevelopmentAge(player) - player.Age + 1f);
                float growth = (headroom / seasonsRemainingToPeak) * (0.7f + playingTimeFactor * 0.3f);

                if (isGoalkeeper) GrowGoalkeeperAttributes(player, growth, focusAttributes);
                else GrowOutfieldAttributes(player, growth, focusAttributes);
            }
            else if (veteranFactor > 0f)
            {
                float decline = 3f + veteranFactor * 5f;

                if (isGoalkeeper) DeclineGoalkeeperAttributes(player, decline, veteranFactor);
                else DeclineOutfieldAttributes(player, decline, veteranFactor);
            }
            else
            {
                ApplySmallPrimeAgeNoise(player, isGoalkeeper);
            }

            ClampAllAttributes(player);

            lastSeasonOverallDelta[player] = GetDisplayRating(player.GetOverallRating()) - displayedOverallBefore;
        }

        // Mirrors ManagerPrototypeController.GetDisplayRating exactly (same midpoint/
        // stretch) - duplicated rather than shared since that method is UI-layer private,
        // and the delta above needs to be in the same display terms as the OVR number
        // it's shown next to, not raw GetOverallRating() terms.
        private static int GetDisplayRating(float trueRating)
        {
            const float midpoint = 50f;
            const float stretch = 1.15f;

            float displayed = midpoint + (trueRating - midpoint) * stretch;

            return Mathf.RoundToInt(Mathf.Clamp(displayed, 1f, 99f));
        }

        // Age-scaled chance, starting small right at the threshold and climbing toward
        // roughly a coin flip for a genuinely ancient outfield veteran - a 35-year-old
        // playing on is common in real football, a 45-year-old isn't.
        public static bool RollRetirement(PlayerAgent player)
        {
            if (player.Age < VeteranRetirementAge)
            {
                return false;
            }

            float ageFactor = Mathf.Clamp01((player.Age - VeteranRetirementAge) / 10f);
            float chance = 0.03f + ageFactor * 0.5f;
            return Random.value < chance;
        }

        // GetOverallRating() is a WEIGHTED AVERAGE of a position-specific subset of
        // these attributes, not a sum - adding the same `amount` to every attribute in
        // the pool raises that weighted average by (very close to) `amount` itself,
        // since almost every touched stat carries real weight in every position's
        // formula. Diluting `amount` across the pool first (an earlier version of this
        // method divided by attribute count) made Overall barely move at all - the
        // weighted-average math absorbed nearly all of it. Confirmed live: a tracked
        // 18-year-old only gained +0.8 Overall over 7 simulated seasons with the
        // diluted version; this version is the fix.
        // focusAttributes doubles the per-attribute amount for whichever names are in
        // the set (see Focused below) - optional and null for every caller except
        // ApplySeasonProgression's academy path, so ApplyMatchdayProgression/
        // ApplyMatchFormBonus (the managed team's own growth ticks) are completely
        // unaffected by this parameter's addition.
        private static void GrowOutfieldAttributes(PlayerAgent player, float amount, IReadOnlyCollection<string> focusAttributes = null)
        {
            player.Finishing += Focused(amount, "Finishing", focusAttributes);
            player.Passing += Focused(amount, "Passing", focusAttributes);
            player.Dribbling += Focused(amount, "Dribbling", focusAttributes);
            player.Crossing += Focused(amount, "Crossing", focusAttributes);
            player.Heading += Focused(amount, "Heading", focusAttributes);
            player.LongShots += Focused(amount, "LongShots", focusAttributes);
            player.ThroughBalls += Focused(amount, "ThroughBalls", focusAttributes);
            player.Creativity += Focused(amount, "Creativity", focusAttributes);
            player.Positioning += Focused(amount, "Positioning", focusAttributes);
            player.Composure += Focused(amount, "Composure", focusAttributes);
            player.OffTheBall += Focused(amount, "OffTheBall", focusAttributes);
            player.Defending += Focused(amount, "Defending", focusAttributes);
            player.Tackling += Focused(amount, "Tackling", focusAttributes);
            player.Marking += Focused(amount, "Marking", focusAttributes);

            // Physical attributes develop more slowly than technical/mental as a young
            // player matures - the body was already closer to its ceiling than the
            // footballing skillset was. Focus doubling still applies on top of that
            // reduced base rate, not the full unreduced amount.
            player.Pace += Focused(amount * 0.5f, "Pace", focusAttributes);
            player.Strength += Focused(amount * 0.6f, "Strength", focusAttributes);
            player.Stamina += Focused(amount * 0.5f, "Stamina", focusAttributes);
            player.Aerial += Focused(amount * 0.5f, "Aerial", focusAttributes);
        }

        private static float Focused(float baseAmount, string attributeName, IReadOnlyCollection<string> focusAttributes)
        {
            return focusAttributes != null && focusAttributes.Contains(attributeName) ? baseAmount * 2f : baseAmount;
        }

        private static void DeclineOutfieldAttributes(PlayerAgent player, float amount, float veteranFactor)
        {
            // Physical erodes fastest and first - the real aging curve, legs go before
            // the footballing brain does.
            player.Pace -= amount * 1.4f;
            player.Stamina -= amount * 1.2f;
            player.Strength -= amount * 0.8f;
            player.Aerial -= amount * 0.6f;

            float technicalDecline = amount * 0.4f * veteranFactor;
            player.Finishing -= technicalDecline;
            player.Passing -= technicalDecline;
            player.Dribbling -= technicalDecline;
            player.Crossing -= technicalDecline;
            player.Defending -= technicalDecline * 0.5f;
            player.Tackling -= technicalDecline * 0.5f;

            // "Reading the game" is the one thing that doesn't decline with age in real
            // football - experience keeps this roughly flat or even nudging up.
            player.Composure += amount * 0.15f;
            player.Positioning += amount * 0.1f;
        }

        private static void GrowGoalkeeperAttributes(PlayerAgent player, float amount, IReadOnlyCollection<string> focusAttributes = null)
        {
            player.Goalkeeping += Focused(amount * 1.4f, "Goalkeeping", focusAttributes);
            player.Reflexes += Focused(amount * 1.3f, "Reflexes", focusAttributes);
            player.Positioning += Focused(amount, "Positioning", focusAttributes);
            player.Composure += Focused(amount, "Composure", focusAttributes);
            player.Passing += Focused(amount * 0.6f, "Passing", focusAttributes);
        }

        private static void DeclineGoalkeeperAttributes(PlayerAgent player, float amount, float veteranFactor)
        {
            // Reflexes are goalkeeping's "pace" - the first and sharpest thing to go.
            player.Reflexes -= amount * 1.3f;
            player.Goalkeeping -= amount * 0.5f * veteranFactor;

            // Shot-stopping composure/positioning from experience holds up well.
            player.Composure += amount * 0.1f;
        }

        // Prime-age (roughly 24-30) players aren't static, just not trending strongly
        // either way - a small two-sided nudge rather than zero change.
        private static void ApplySmallPrimeAgeNoise(PlayerAgent player, bool isGoalkeeper)
        {
            float noise = Random.Range(-1.5f, 1.5f);

            if (isGoalkeeper)
            {
                player.Goalkeeping += noise;
                player.Reflexes += noise;
            }
            else
            {
                player.Composure += noise;
                player.Positioning += noise;
            }
        }

        // Mirrors AgentSquadGenerator.ClampAttributes's 1-100 wall - duplicated rather
        // than shared, since that method is private to a protected Sim file and this is
        // a separate Manager-only concern touching the same public fields from outside.
        private static void ClampAllAttributes(PlayerAgent player)
        {
            player.Finishing = Clamp(player.Finishing);
            player.Passing = Clamp(player.Passing);
            player.Dribbling = Clamp(player.Dribbling);
            player.Crossing = Clamp(player.Crossing);
            player.Heading = Clamp(player.Heading);
            player.LongShots = Clamp(player.LongShots);
            player.ThroughBalls = Clamp(player.ThroughBalls);
            player.FreeKicks = Clamp(player.FreeKicks);

            player.Creativity = Clamp(player.Creativity);
            player.Positioning = Clamp(player.Positioning);
            player.Composure = Clamp(player.Composure);
            player.OffTheBall = Clamp(player.OffTheBall);
            player.Leadership = Clamp(player.Leadership);

            player.Defending = Clamp(player.Defending);
            player.Tackling = Clamp(player.Tackling);
            player.Marking = Clamp(player.Marking);

            player.Pace = Clamp(player.Pace);
            player.Strength = Clamp(player.Strength);
            player.Stamina = Clamp(player.Stamina);
            player.Aerial = Clamp(player.Aerial);

            player.Goalkeeping = Clamp(player.Goalkeeping);
            player.Reflexes = Clamp(player.Reflexes);

            player.WeakFoot = Clamp(player.WeakFoot);
        }

        private static float Clamp(float value)
        {
            return Mathf.Clamp(value, 1f, 100f);
        }
    }
}
