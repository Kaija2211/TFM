using System;
using System.Collections.Generic;
using Sim;

namespace Manager
{
    // Read-only diagnostic profile for the player-derived strength overhaul. This does
    // not currently feed match odds: first we need league-wide distributions and holy-
    // balance comparisons, otherwise using these values on top of the existing event
    // resolver would double-count the same player attributes again.
    public static class ManagerPlayerDerivedStrength
    {
        public sealed class MatchupPrediction
        {
            public float ExpectedHomeGoals;
            public float ExpectedAwayGoals;
        }

        private const float HomeGoalBase = 1.32f;
        private const float AwayGoalBase = 1.08f;
        private const float NeutralAttackDefenceOffset = 5.5f;
        private const float MatchupScale = 16f;

        public sealed class Profile
        {
            public float Control;
            public float ChanceCreation;
            public float GoalThreat;
            public float DefensiveResistance;
            public float Goalkeeping;
            public float Depth;

            public float DisplayOverall =>
                Control * 0.20f +
                ChanceCreation * 0.20f +
                GoalThreat * 0.20f +
                DefensiveResistance * 0.25f +
                Goalkeeping * 0.15f;
        }

        private readonly struct PhaseWeights
        {
            public readonly float Control;
            public readonly float Creation;
            public readonly float Threat;
            public readonly float Defence;
            public readonly float Goalkeeping;

            public PhaseWeights(float control, float creation, float threat, float defence, float goalkeeping = 0f)
            {
                Control = control;
                Creation = creation;
                Threat = threat;
                Defence = defence;
                Goalkeeping = goalkeeping;
            }
        }

        public static Profile Calculate(
            AgentTeam team,
            IReadOnlyList<PlayerPosition> formationSlots,
            Func<PlayerAgent, float> conditionLookup = null)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            if (formationSlots == null) throw new ArgumentNullException(nameof(formationSlots));

            float control = 0f, controlWeight = 0f;
            float creation = 0f, creationWeight = 0f;
            float threat = 0f, threatWeight = 0f;
            float defence = 0f, defenceWeight = 0f;
            float goalkeeping = 0f, goalkeepingWeight = 0f;

            for (int index = 0; index < team.StartingEleven.Count; index++)
            {
                PlayerAgent player = team.StartingEleven[index];
                PlayerPosition slot = index < formationSlots.Count ? formationSlots[index] : player.PrimaryPosition;
                float availability = player.GetPositionFit(slot) * Clamp01(conditionLookup?.Invoke(player) ?? 1f);
                PhaseWeights weights = GetPhaseWeights(slot);

                AddWeighted(ref control, ref controlWeight, GetControlScore(player) * availability, weights.Control);
                AddWeighted(ref creation, ref creationWeight, GetCreationScore(player) * availability, weights.Creation);
                AddWeighted(ref threat, ref threatWeight, GetThreatScore(player) * availability, weights.Threat);
                AddWeighted(ref defence, ref defenceWeight, GetDefenceScore(player) * availability, weights.Defence);
                AddWeighted(ref goalkeeping, ref goalkeepingWeight, GetGoalkeepingScore(player) * availability, weights.Goalkeeping);
            }

            return new Profile
            {
                Control = DivideOrZero(control, controlWeight),
                ChanceCreation = DivideOrZero(creation, creationWeight),
                GoalThreat = DivideOrZero(threat, threatWeight),
                DefensiveResistance = DivideOrZero(defence, defenceWeight),
                Goalkeeping = DivideOrZero(goalkeeping, goalkeepingWeight),
                Depth = CalculateDepth(team.Bench)
            };
        }

        public static MatchupPrediction PredictMatchup(Profile home, Profile away)
        {
            if (home == null) throw new ArgumentNullException(nameof(home));
            if (away == null) throw new ArgumentNullException(nameof(away));

            float homeEdge = GetAttackIndex(home) - GetResistanceIndex(away) + NeutralAttackDefenceOffset;
            float awayEdge = GetAttackIndex(away) - GetResistanceIndex(home) + NeutralAttackDefenceOffset;

            return new MatchupPrediction
            {
                ExpectedHomeGoals = HomeGoalBase * ExpClamped(homeEdge / MatchupScale),
                ExpectedAwayGoals = AwayGoalBase * ExpClamped(awayEdge / MatchupScale)
            };
        }

        private static float GetAttackIndex(Profile profile) =>
            profile.Control * 0.20f + profile.ChanceCreation * 0.35f + profile.GoalThreat * 0.45f;

        private static float GetResistanceIndex(Profile profile) =>
            profile.DefensiveResistance * 0.72f + profile.Goalkeeping * 0.28f;

        private static float ExpClamped(float exponent)
        {
            exponent = exponent < -0.65f ? -0.65f : exponent > 0.65f ? 0.65f : exponent;
            return (float)Math.Exp(exponent);
        }

        private static float GetControlScore(PlayerAgent player) => WeightedAverage(
            (player.Passing, 0.20f), (player.FirstTouch, 0.16f), (player.Decisions, 0.15f),
            (player.Composure, 0.14f), (player.Technique, 0.11f), (player.Stamina, 0.09f),
            (player.WorkRate, 0.07f), (player.Dribbling, 0.05f), (player.WeakFoot, 0.03f));

        private static float GetCreationScore(PlayerAgent player) => WeightedAverage(
            (player.Vision, 0.22f), (player.Passing, 0.18f), (player.Decisions, 0.14f),
            (player.Technique, 0.12f), (player.Dribbling, 0.11f), (player.Crossing, 0.09f),
            (player.OffTheBall, 0.08f), (player.FirstTouch, 0.06f));

        private static float GetThreatScore(PlayerAgent player) => WeightedAverage(
            (player.Finishing, 0.22f), (player.OffTheBall, 0.16f), (player.Anticipation, 0.13f),
            (player.Composure, 0.12f), (player.FirstTouch, 0.09f), (player.Heading, 0.08f),
            (player.LongShots, 0.07f), (player.Acceleration, 0.06f), (player.Pace, 0.04f),
            (player.JumpingReach, 0.03f));

        private static float GetDefenceScore(PlayerAgent player) => WeightedAverage(
            (player.DefensivePositioning, 0.22f), (player.Marking, 0.18f), (player.Tackling, 0.17f),
            (player.Anticipation, 0.13f), (player.Decisions, 0.09f), (player.JumpingReach, 0.06f),
            (player.Strength, 0.06f), (player.Pace, 0.04f), (player.WorkRate, 0.03f),
            (player.Composure, 0.02f));

        private static float GetGoalkeepingScore(PlayerAgent player) => WeightedAverage(
            (player.Handling, 0.22f), (player.Reflexes, 0.21f), (player.OneOnOnes, 0.17f),
            (player.GoalkeeperPositioning, 0.15f), (player.AerialCommand, 0.10f),
            (player.Distribution, 0.06f), (player.Decisions, 0.05f), (player.Composure, 0.04f));

        private static PhaseWeights GetPhaseWeights(PlayerPosition slot)
        {
            switch (slot)
            {
                case PlayerPosition.GK: return new PhaseWeights(0.30f, 0.05f, 0f, 0.15f, 1f);
                case PlayerPosition.CB: return new PhaseWeights(0.45f, 0.20f, 0.08f, 1f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return new PhaseWeights(0.60f, 0.62f, 0.30f, 0.78f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return new PhaseWeights(0.72f, 0.78f, 0.55f, 0.62f);
                case PlayerPosition.DM: return new PhaseWeights(0.88f, 0.62f, 0.25f, 0.90f);
                case PlayerPosition.CM: return new PhaseWeights(1f, 0.88f, 0.52f, 0.62f);
                case PlayerPosition.AM: return new PhaseWeights(0.88f, 1f, 0.88f, 0.28f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return new PhaseWeights(0.82f, 0.90f, 0.72f, 0.52f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return new PhaseWeights(0.72f, 0.95f, 0.98f, 0.22f);
                case PlayerPosition.ST: return new PhaseWeights(0.42f, 0.58f, 1f, 0.16f);
                default: return new PhaseWeights(0.5f, 0.5f, 0.5f, 0.5f);
            }
        }

        private static float CalculateDepth(List<PlayerAgent> bench)
        {
            if (bench == null || bench.Count == 0) return 0f;
            float total = 0f;
            foreach (PlayerAgent player in bench) total += player.GetOverallRating();
            return total / bench.Count;
        }

        private static float WeightedAverage(params (float value, float weight)[] terms)
        {
            float value = 0f, weight = 0f;
            foreach ((float termValue, float termWeight) in terms)
            {
                value += termValue * termWeight;
                weight += termWeight;
            }
            return DivideOrZero(value, weight);
        }

        private static void AddWeighted(ref float total, ref float totalWeight, float value, float weight)
        {
            if (weight <= 0f) return;
            total += value * weight;
            totalWeight += weight;
        }

        private static float DivideOrZero(float value, float divisor) => divisor > 0f ? value / divisor : 0f;
        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
