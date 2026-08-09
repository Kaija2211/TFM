using UnityEngine;
using Sim;

namespace Manager
{
    // Gives captaincy a real mechanical consequence without touching AgentMatchSimulator.
    // SimulateMatch itself - same pre-match, pre-processing pattern as
    // ManagerMentalityModifier, applied to the same expected-goals inputs before
    // SimulateMatch ever runs. Deliberately downside-only: a well-chosen captain (older,
    // good Leadership/Composure) costs nothing, a poorly-chosen one (young, low
    // Leadership) costs real expected goals - "don't pick a bad captain" rather than
    // "maximize this stat by picking the highest-Leadership player regardless of fit."
    // A no-op for any team with no captain designated (every AI-controlled team, and the
    // managed team until Thomas actually assigns one via Player Detail) - see
    // ManagerSquadRoles.
    public static class ManagerCaptaincyModifier
    {
        private const float SuitabilityThreshold = 45f;
        private const float MaxPenalty = 0.12f;

        public static void Apply(PlayerAgent captain, ref float teamExpectedGoals)
        {
            if (captain == null)
            {
                return;
            }

            float suitability = GetCaptaincySuitability(captain);

            if (suitability >= SuitabilityThreshold)
            {
                return;
            }

            float deficit = (SuitabilityThreshold - suitability) / SuitabilityThreshold;
            teamExpectedGoals *= 1f - (deficit * MaxPenalty);
        }

        // Leadership carries most of the weight; Composure represents big-game
        // temperament under pressure; Age gets its own explicit term on top of the
        // veteran/youth nudge Leadership already receives at generation time (see
        // AgentSquadGenerator.GenerateNewerAttributes) - a real teenage captain should
        // read as risky even on the rare roll where their Leadership came out high.
        private static float GetCaptaincySuitability(PlayerAgent captain)
        {
            float ageFactor = Mathf.Clamp01((captain.Age - 18f) / 10f) * 100f;
            return captain.Leadership * 0.55f + captain.Composure * 0.20f + ageFactor * 0.25f;
        }
    }
}
