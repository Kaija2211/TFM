using System;
using Sim;

namespace Manager
{
    public static class ManagerAiTacticalPlanner
    {
        public static ManagerTacticalSliders Choose(string aiName, Formation aiFormation, string opponentName, Formation opponentFormation, bool aiIsHome)
        {
            ManagerTacticalSliders best = new();
            float bestScore = float.MinValue;
            foreach (WidthSetting width in Enum.GetValues(typeof(WidthSetting)))
            foreach (DefensiveDepthSetting depth in Enum.GetValues(typeof(DefensiveDepthSetting)))
            foreach (TempoSetting tempo in Enum.GetValues(typeof(TempoSetting)))
            {
                ManagerTacticalSliders candidate = new() { Width = width, DefensiveDepth = depth, Tempo = tempo };
                // First-generation AI makes one deliberate matchup adjustment rather
                // than discovering and spamming a universally extreme three-slider preset.
                if (CountExtremes(candidate) > 1) continue;
                ManagerTacticalShape.Matchup matchup = aiIsHome
                    ? ManagerTacticalShape.BuildMatchup(aiName, aiFormation, candidate, opponentName, opponentFormation, null)
                    : ManagerTacticalShape.BuildMatchup(opponentName, opponentFormation, null, aiName, aiFormation, candidate);
                float score = RouteUtility(matchup.GetAttackEffect(aiName)) -
                    RouteUtility(matchup.GetAttackEffect(opponentName)) * 0.90f - ExtremityPenalty(candidate);
                if (score > bestScore + 0.0001f) { bestScore = score; best = candidate; }
            }
            return best;
        }

        private static float Strongest(ManagerTacticalShape.RouteEffect effect)
        {
            float strongest = float.MinValue;
            foreach (float value in effect.All) strongest = Math.Max(strongest, value);
            return strongest;
        }

        private static float RouteUtility(ManagerTacticalShape.RouteEffect effect)
        {
            float total = 0f;
            int count = 0;
            foreach (float value in effect.All) { total += value; count++; }
            return total / count + Strongest(effect) * 0.20f;
        }

        private static float ExtremityPenalty(ManagerTacticalSliders tactics)
        {
            return CountExtremes(tactics) * 0.004f;
        }

        private static int CountExtremes(ManagerTacticalSliders tactics) =>
            (tactics.Width == WidthSetting.Balanced ? 0 : 1) +
            (tactics.DefensiveDepth == DefensiveDepthSetting.Balanced ? 0 : 1) +
            (tactics.Tempo == TempoSetting.Balanced ? 0 : 1);
    }
}
