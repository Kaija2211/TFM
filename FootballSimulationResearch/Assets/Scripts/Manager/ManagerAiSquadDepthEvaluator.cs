using System.Collections.Generic;
using System.Linq;
using Sim;

namespace Manager
{
    // AI squad depth/need evaluation (roadmap: "Evaluate positional depth, squad
    // quality and age profile" - the second stage of the Intelligent AI Clubs epic,
    // building on ManagerAiSquadRotation's Condition/injury-aware selection). Pure
    // analysis with no side effects and no transfer-market wiring yet - answers "how
    // well covered is each position and which one most needs reinforcement," which a
    // later recruitment-targeting stage will consume. Deliberately a plain,
    // inspectable scoring formula rather than a black box, matching the epic's own
    // "readable evidence for debugging why an AI club made each major decision" goal.
    public static class ManagerAiSquadDepthEvaluator
    {
        // Same 0.80 "adjacent or better" fit tier the pitch-slot selector UI and squad
        // auto-pick already treat as genuinely usable cover, rather than an emergency
        // mismatch (see PlayerAgent.GetPositionFit's own tiers).
        private const float AdequateCoverFitThreshold = 0.80f;

        // A position with 0-1 adequate options is a real depth gap; 2+ is treated as
        // sufficiently covered - not chasing perfect strength-in-depth everywhere.
        private const int SufficientDepthCount = 2;
        private const float MissingCoverPenaltyPerSlot = 15f;

        // "Succession concern" - the best option is the wrong side of 30 (same
        // threshold ManagerMatchdayCondition's own injury-risk age curve starts
        // ramping at) with no comparable-quality backup ready to replace them.
        private const int SuccessionAgeThreshold = 31;
        private const float SuccessionOverallGap = 8f;
        private const float SuccessionPenalty = 10f;

        public readonly struct PositionDepth
        {
            public readonly PlayerPosition Position;
            public readonly int AdequateCoverCount;
            public readonly PlayerAgent BestPlayer;
            public readonly float BestOverall;
            public readonly float SecondBestOverall;
            public readonly bool SuccessionConcern;
            public readonly float NeedScore;

            public PositionDepth(PlayerPosition position, int adequateCoverCount, PlayerAgent bestPlayer,
                float bestOverall, float secondBestOverall, bool successionConcern, float needScore)
            {
                Position = position;
                AdequateCoverCount = adequateCoverCount;
                BestPlayer = bestPlayer;
                BestOverall = bestOverall;
                SecondBestOverall = secondBestOverall;
                SuccessionConcern = successionConcern;
                NeedScore = needScore;
            }
        }

        public readonly struct SquadDepthReport
        {
            public readonly List<PositionDepth> Positions;
            public readonly PlayerPosition WeakestPosition;
            public readonly float WeakestPositionNeedScore;

            public SquadDepthReport(List<PositionDepth> positions, PlayerPosition weakestPosition, float weakestPositionNeedScore)
            {
                Positions = positions;
                WeakestPosition = weakestPosition;
                WeakestPositionNeedScore = weakestPositionNeedScore;
            }
        }

        // positionsToEvaluate: deliberately not "all 14 PlayerPosition values" - only
        // some formations actually field a wing-back slot (RWB/LWB), for example, so
        // judging every club against every canonical position regardless of its own
        // formation made an under-modelled, formation-irrelevant position (nobody's
        // formation uses it, so nobody has dedicated cover there) look like the
        // "weakest position" for almost every club, which isn't a genuine, club-
        // specific signal - caught by this service's own statistical audit before
        // shipping. Callers should pass the club's own formation's starting slots
        // (see AgentSquadGenerator.GetStartingPositions), deduplicated.
        public static SquadDepthReport Evaluate(AgentTeam team, List<PlayerPosition> positionsToEvaluate)
        {
            // Deliberately the Starting Eleven's own average, not the whole 30-man
            // squad's - AgentSquadGenerator generates bench/reserves at intentionally
            // lower SquadQualityTarget tiers than the XI, so comparing a position's
            // best option against the whole-squad average made the quality-penalty
            // term almost always zero regardless of position or club strength (a
            // position's best cover is very likely to already beat a reserve-dragged-
            // down squad average) - caught by this service's own statistical audit,
            // which found even deliberately weak generated clubs showing zero
            // positional weakness under the original formula.
            float startingElevenAverageOverall = team.StartingEleven.Count > 0
                ? team.StartingEleven.Average(p => p.GetOverallRating())
                : team.Players.Count > 0 ? team.Players.Average(p => p.GetOverallRating()) : 0f;

            List<PositionDepth> positions = new List<PositionDepth>();
            foreach (PlayerPosition position in positionsToEvaluate.Distinct())
            {
                positions.Add(EvaluatePosition(team, position, startingElevenAverageOverall));
            }

            PositionDepth weakest = positions.OrderByDescending(p => p.NeedScore).First();
            return new SquadDepthReport(positions, weakest.Position, weakest.NeedScore);
        }

        private static PositionDepth EvaluatePosition(AgentTeam team, PlayerPosition position, float startingElevenAverageOverall)
        {
            List<PlayerAgent> adequate = team.Players
                .Where(p => p.GetPositionFit(position) >= AdequateCoverFitThreshold)
                .OrderByDescending(p => p.GetOverallRating())
                .ToList();

            PlayerAgent best = adequate.Count > 0 ? adequate[0] : null;
            PlayerAgent secondBest = adequate.Count > 1 ? adequate[1] : null;
            float bestOverall = best?.GetOverallRating() ?? 0f;
            float secondBestOverall = secondBest?.GetOverallRating() ?? 0f;

            bool successionConcern = best != null && best.Age >= SuccessionAgeThreshold
                && (secondBest == null || secondBestOverall < bestOverall - SuccessionOverallGap);

            // Three independently-readable penalty terms rather than one opaque
            // formula - a bounded, explainable "why does this position need
            // reinforcement" signal, not a claim of perfect optimisation.
            float depthPenalty = System.Math.Max(0, SufficientDepthCount - adequate.Count) * MissingCoverPenaltyPerSlot;
            float qualityPenalty = System.Math.Max(0f, startingElevenAverageOverall - bestOverall);
            float successionPenalty = successionConcern ? SuccessionPenalty : 0f;
            float needScore = depthPenalty + qualityPenalty + successionPenalty;

            return new PositionDepth(position, adequate.Count, best, bestOverall, secondBestOverall, successionConcern, needScore);
        }
    }
}
