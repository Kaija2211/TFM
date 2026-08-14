using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Data
{
    public static class SimulationStatistics
    {
        public static float CalculateMedian(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0f;
            }

            int middleIndex = values.Count / 2;

            if (values.Count % 2 == 1)
            {
                return values[middleIndex];
            }

            return (values[middleIndex - 1] + values[middleIndex]) / 2f;
        }

        public static float CalculateStandardDeviation(List<float> values, float mean)
        {
            if (values == null || values.Count == 0)
            {
                return 0f;
            }

            float totalSquaredDifference = 0f;

            foreach (float value in values)
            {
                float difference = value - mean;
                totalSquaredDifference += difference * difference;
            }

            float variance = totalSquaredDifference / values.Count;

            return Mathf.Sqrt(variance);
        }

        public static float CalculatePointsMAE(LeagueTable actualTable, LeagueTable simulatedTable)
        {
            List<LeagueTable.Entry> actualEntries = actualTable.Sorted();
            List<LeagueTable.Entry> simulatedEntries = simulatedTable.Sorted();

            Dictionary<int, LeagueTable.Entry> simulatedByTeamId = new();

            foreach (LeagueTable.Entry simulatedEntry in simulatedEntries)
            {
                simulatedByTeamId[simulatedEntry.TeamId] = simulatedEntry;
            }

            float totalAbsoluteError = 0f;
            int comparedTeams = 0;

            foreach (LeagueTable.Entry actualEntry in actualEntries)
            {
                if (!simulatedByTeamId.ContainsKey(actualEntry.TeamId))
                {
                    continue;
                }

                LeagueTable.Entry simulatedEntry = simulatedByTeamId[actualEntry.TeamId];

                int absoluteError = Mathf.Abs(actualEntry.Points - simulatedEntry.Points);

                totalAbsoluteError += absoluteError;
                comparedTeams++;
            }

            if (comparedTeams == 0)
            {
                Debug.LogWarning("No teams could be compared when calculating Points MAE.");
                return 0f;
            }

            return totalAbsoluteError / comparedTeams;
        }
    }
}
