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

        // playingTimeFactor is 0-1, supplied by the caller since only the managed
        // team's appearances are actually tracked (see ManagerSquadRoles) - callers pass
        // a real per-player value for the managed squad and a flat assumed value for
        // everyone else (AI clubs' first team vs. uncalled reserves), rather than this
        // method needing to know which kind of player it's looking at.
        public static void ApplySeasonProgression(PlayerAgent player, float playingTimeFactor)
        {
            playingTimeFactor = Mathf.Clamp01(playingTimeFactor);

            float youthFactor = Mathf.Clamp01((24f - player.Age) / 6f);
            float veteranFactor = Mathf.Clamp01((player.Age - 29f) / 8f);
            float headroom = player.Potential - player.GetOverallRating();

            bool isGoalkeeper = player.PrimaryPosition == PlayerPosition.GK;

            if (youthFactor > 0f && headroom > 0f)
            {
                // Capped at half the remaining headroom per season, even for a perfect
                // storm of youth/minutes/huge ceiling - "reaching potential" should read
                // as a multi-season arc, not something that can happen in one.
                float growth = Mathf.Min(headroom * 0.5f, 6f) * youthFactor * (0.4f + playingTimeFactor * 0.6f);

                if (isGoalkeeper) GrowGoalkeeperAttributes(player, growth);
                else GrowOutfieldAttributes(player, growth);
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
        private static void GrowOutfieldAttributes(PlayerAgent player, float amount)
        {
            player.Finishing += amount;
            player.Passing += amount;
            player.Dribbling += amount;
            player.Crossing += amount;
            player.Heading += amount;
            player.LongShots += amount;
            player.ThroughBalls += amount;
            player.Creativity += amount;
            player.Positioning += amount;
            player.Composure += amount;
            player.OffTheBall += amount;
            player.Defending += amount;
            player.Tackling += amount;
            player.Marking += amount;

            // Physical attributes develop more slowly than technical/mental as a young
            // player matures - the body was already closer to its ceiling than the
            // footballing skillset was.
            player.Pace += amount * 0.5f;
            player.Strength += amount * 0.6f;
            player.Stamina += amount * 0.5f;
            player.Aerial += amount * 0.5f;
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

        private static void GrowGoalkeeperAttributes(PlayerAgent player, float amount)
        {
            player.Goalkeeping += amount * 1.4f;
            player.Reflexes += amount * 1.3f;
            player.Positioning += amount;
            player.Composure += amount;
            player.Passing += amount * 0.6f;
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
