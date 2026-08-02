using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Sim;

namespace Data
{
    // Owns the Statistical-vs-Agent-Based research evaluation pipeline: training,
    // simulating, comparing against held-out results, and printing/exporting evidence.
    // Deliberately plain C# (not a MonoBehaviour) so it can run outside Unity's
    // lifecycle and stays separate from the manager-facing gameplay wrapper.
    public class ResearchEvaluationRunner
    {
        private readonly TeamRegistry teamRegistry;
        private readonly EvidenceExporter evidenceExporter;

        public ResearchEvaluationRunner(TeamRegistry teamRegistry, EvidenceExporter evidenceExporter)
        {
            this.teamRegistry = teamRegistry;
            this.evidenceExporter = evidenceExporter;
        }

        public void Run(
            List<OpenFootballMatch> trainingMatches,
            List<OpenFootballMatch> evaluationMatches)
        {
            StatisticalModel statisticalModel = new StatisticalModel();
            statisticalModel.Train(trainingMatches);
            statisticalModel.PrintTeamStrengths(10);

            PrintGeneratedAgentSquad(statisticalModel, "Liverpool");

            statisticalModel.PrintExpectedGoalsSamples(evaluationMatches, 10);
            statisticalModel.PrintSimulatedMatchSamples(evaluationMatches, 10);

            List<StatisticalModel.SimulatedMatchResult> simulatedResults =
                statisticalModel.SimulateSeason(evaluationMatches);

            LeagueTable simulatedTable = BuildSimulatedLeagueTable(simulatedResults, verbose: true);
            LeagueTable actualTable = BuildActualEvaluationTable(evaluationMatches);

            CompareTablesWithPointsMAE(actualTable, simulatedTable);

            RunRepeatedStatisticalEvaluation(
                statisticalModel,
                evaluationMatches,
                actualTable,
                100
            );

            PrintSampleAgentBasedMatch(
                statisticalModel,
                "Liverpool",
                "AFC Bournemouth"
            );

            LeagueTable agentBasedTable = SimulateAgentBasedEvaluationSeason(
                statisticalModel,
                evaluationMatches
            );

            float agentBasedMae = SimulationStatistics.CalculatePointsMAE(actualTable, agentBasedTable);
            Debug.Log($"Agent-Based Model Points MAE: {agentBasedMae:F2}");

            PrintGoalsPerMatchComparison(evaluationMatches, agentBasedTable);

            RunRepeatedAgentBasedEvaluation(
                statisticalModel,
                evaluationMatches,
                actualTable,
                100
            );
        }

        private void PrintGeneratedAgentSquad(
            StatisticalModel statisticalModel,
            string teamName)
        {
            StatisticalModel.TeamStrength strength =
                statisticalModel.GetTeamStrength(teamName);

            AgentSquadGenerator generator = new AgentSquadGenerator();

            AgentTeam squad = generator.GenerateSquad(
                teamName,
                strength.AttackStrength,
                strength.DefenceStrength
            );

            Debug.Log($"Generated ABM squad for {teamName}:");
            Debug.Log($"Formation: {squad.Formation}");

            Debug.Log("Starting XI:");

            foreach (PlayerAgent player in squad.StartingEleven)
            {
                Debug.Log(player.ToString());
            }

            Debug.Log("Bench:");

            foreach (PlayerAgent player in squad.Bench)
            {
                Debug.Log(player.ToString());
            }
        }

        private void PrintSampleAgentBasedMatch(
            StatisticalModel statisticalModel,
            string homeTeamName,
            string awayTeamName)
        {
            StatisticalModel.TeamStrength homeStrength =
                statisticalModel.GetTeamStrength(homeTeamName);

            StatisticalModel.TeamStrength awayStrength =
                statisticalModel.GetTeamStrength(awayTeamName);

            AgentSquadGenerator squadGenerator = new AgentSquadGenerator();

            AgentTeam homeTeam = squadGenerator.GenerateSquad(
                homeTeamName,
                homeStrength.AttackStrength,
                homeStrength.DefenceStrength
            );

            AgentTeam awayTeam = squadGenerator.GenerateSquad(
                awayTeamName,
                awayStrength.AttackStrength,
                awayStrength.DefenceStrength
            );

            OpenFootballMatch sampleFixture = new OpenFootballMatch
            {
                HomeTeam = homeTeamName,
                AwayTeam = awayTeamName,
                HomeGoals = 0,
                AwayGoals = 0,
                Season = "sample"
            };

            StatisticalModel.ExpectedGoalsPrediction prediction =
                statisticalModel.PredictExpectedGoals(sampleFixture);

            AgentMatchSimulator matchSimulator = new AgentMatchSimulator();

            AgentMatchSimulator.AgentMatchResult result =
                matchSimulator.SimulateMatch(
                    homeTeam,
                    awayTeam,
                    prediction.ExpectedHomeGoals,
                    prediction.ExpectedAwayGoals
                );

            Debug.Log(
                $"ABM sample match result: " +
                $"{result.HomeTeamName} {result.HomeGoals}-{result.AwayGoals} {result.AwayTeamName}"
            );

            Debug.Log("ABM sample match events:");

            foreach (AgentMatchSimulator.AgentMatchEvent matchEvent in result.Events)
            {
                Debug.Log($"{matchEvent.Minute}' {matchEvent.Description}");
            }
        }

        private LeagueTable SimulateAgentBasedEvaluationSeason(
            StatisticalModel statisticalModel,
            List<OpenFootballMatch> evaluationMatches,
            bool verbose = true)
        {
            Dictionary<string, AgentTeam> squadsByTeamName = new();

            AgentSquadGenerator squadGenerator = new AgentSquadGenerator();
            AgentMatchSimulator matchSimulator = new AgentMatchSimulator();

            LeagueTable abmTable = new LeagueTable();

            foreach (OpenFootballMatch fixture in evaluationMatches)
            {
                AgentTeam homeTeam = GetOrCreateAgentTeam(
                    fixture.HomeTeam,
                    statisticalModel,
                    squadGenerator,
                    squadsByTeamName
                );

                AgentTeam awayTeam = GetOrCreateAgentTeam(
                    fixture.AwayTeam,
                    statisticalModel,
                    squadGenerator,
                    squadsByTeamName
                );

                StatisticalModel.ExpectedGoalsPrediction prediction =
                    statisticalModel.PredictExpectedGoals(fixture);

                AgentMatchSimulator.AgentMatchResult result =
                    matchSimulator.SimulateMatch(
                        homeTeam,
                        awayTeam,
                        prediction.ExpectedHomeGoals,
                        prediction.ExpectedAwayGoals
                    );

                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = teamRegistry.GetTeamId(result.HomeTeamName),
                    AwayTeamId = teamRegistry.GetTeamId(result.AwayTeamName),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                abmTable.Apply(record);
            }

            if (verbose)
            {
                Debug.Log($"Simulated {evaluationMatches.Count} ABM matches.");
                Debug.Log($"Generated {squadsByTeamName.Count} ABM squads.");

                PrintAgentBasedLeagueTable(abmTable);
            }

            return abmTable;
        }

        private AgentTeam GetOrCreateAgentTeam(
            string teamName,
            StatisticalModel statisticalModel,
            AgentSquadGenerator squadGenerator,
            Dictionary<string, AgentTeam> squadsByTeamName)
        {
            if (squadsByTeamName.TryGetValue(teamName, out AgentTeam existingTeam))
            {
                return existingTeam;
            }

            StatisticalModel.TeamStrength teamStrength =
                statisticalModel.GetTeamStrength(teamName);

            AgentTeam newTeam = squadGenerator.GenerateSquad(
                teamName,
                teamStrength.AttackStrength,
                teamStrength.DefenceStrength
            );

            squadsByTeamName[teamName] = newTeam;

            return newTeam;
        }

        private void PrintAgentBasedLeagueTable(LeagueTable table)
        {
            List<LeagueTable.Entry> sortedTable = table.Sorted();

            Debug.Log("Agent-Based Model simulated evaluation table:");

            for (int i = 0; i < sortedTable.Count; i++)
            {
                LeagueTable.Entry entry = sortedTable[i];

                string teamName = teamRegistry.GetTeamName(entry.TeamId);

                Debug.Log(
                    $"{i + 1}. {teamName} " +
                    $"Pts:{entry.Points} " +
                    $"P:{entry.Played} " +
                    $"W:{entry.Wins} " +
                    $"D:{entry.Draws} " +
                    $"L:{entry.Losses} " +
                    $"GF:{entry.GoalsFor} " +
                    $"GA:{entry.GoalsAgainst} " +
                    $"GD:{entry.GoalsFor - entry.GoalsAgainst}"
                );
            }
        }

        private void PrintGoalsPerMatchComparison(
            List<OpenFootballMatch> evaluationMatches,
            LeagueTable abmTable)
        {
            int actualGoals = 0;

            foreach (OpenFootballMatch match in evaluationMatches)
            {
                actualGoals += match.HomeGoals + match.AwayGoals;
            }

            int abmGoals = 0;

            foreach (LeagueTable.Entry entry in abmTable.Sorted())
            {
                abmGoals += entry.GoalsFor;
            }

            float actualGoalsPerMatch = (float)actualGoals / evaluationMatches.Count;
            float abmGoalsPerMatch = (float)abmGoals / evaluationMatches.Count;

            Debug.Log("Goals per match comparison:");
            Debug.Log($"Actual goals per match: {actualGoalsPerMatch:F2}");
            Debug.Log($"ABM goals per match: {abmGoalsPerMatch:F2}");
        }

        private void RunRepeatedAgentBasedEvaluation(
            StatisticalModel statisticalModel,
            List<OpenFootballMatch> evaluationMatches,
            LeagueTable actualTable,
            int runs)
        {
            List<float> maeValues = new();

            float totalMae = 0f;
            float bestMae = float.MaxValue;
            float worstMae = float.MinValue;

            Dictionary<int, int> titleWinsByTeamId = new();

            Dictionary<int, float> totalPointsByTeamId = new();
            Dictionary<int, float> totalPositionByTeamId = new();
            Dictionary<int, int> appearancesByTeamId = new();

            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < runs; i++)
            {
                LeagueTable agentBasedTable = SimulateAgentBasedEvaluationSeason(
                    statisticalModel,
                    evaluationMatches,
                    verbose: false
                );

                float mae = SimulationStatistics.CalculatePointsMAE(actualTable, agentBasedTable);

                maeValues.Add(mae);
                totalMae += mae;

                if (mae < bestMae)
                {
                    bestMae = mae;
                }

                if (mae > worstMae)
                {
                    worstMae = mae;
                }

                List<LeagueTable.Entry> sortedTable = agentBasedTable.Sorted();

                if (sortedTable.Count > 0)
                {
                    int championTeamId = sortedTable[0].TeamId;

                    if (!titleWinsByTeamId.ContainsKey(championTeamId))
                    {
                        titleWinsByTeamId[championTeamId] = 0;
                    }

                    titleWinsByTeamId[championTeamId]++;
                }

                for (int positionIndex = 0; positionIndex < sortedTable.Count; positionIndex++)
                {
                    LeagueTable.Entry entry = sortedTable[positionIndex];

                    if (!totalPointsByTeamId.ContainsKey(entry.TeamId))
                    {
                        totalPointsByTeamId[entry.TeamId] = 0f;
                        totalPositionByTeamId[entry.TeamId] = 0f;
                        appearancesByTeamId[entry.TeamId] = 0;
                    }

                    totalPointsByTeamId[entry.TeamId] += entry.Points;
                    totalPositionByTeamId[entry.TeamId] += positionIndex + 1;
                    appearancesByTeamId[entry.TeamId]++;
                }
            }

            stopwatch.Stop();

            maeValues.Sort();

            float averageMae = totalMae / runs;
            float medianMae = SimulationStatistics.CalculateMedian(maeValues);
            float maeStandardDeviation = SimulationStatistics.CalculateStandardDeviation(maeValues, averageMae);

            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            double simulationsPerMinute = runs / elapsedSeconds * 60.0;

            Debug.Log("========================================");
            Debug.Log($"Agent-Based repeated evaluation over {runs} runs:");
            Debug.Log("----------------------------------------");
            Debug.Log($"Average Points MAE: {averageMae:F2}");
            Debug.Log($"Median Points MAE: {medianMae:F2}");
            Debug.Log($"MAE Standard Deviation: {maeStandardDeviation:F2}");
            Debug.Log($"Best Points MAE: {bestMae:F2}");
            Debug.Log($"Worst Points MAE: {worstMae:F2}");
            Debug.Log($"Execution time: {elapsedSeconds:F4} seconds");
            Debug.Log($"Simulations per minute: {simulationsPerMinute:F2}");
            Debug.Log("========================================");

            Debug.Log("ABM league title winners over repeated simulations:");

            List<KeyValuePair<int, int>> titleWins = new(titleWinsByTeamId);

            titleWins.Sort((a, b) => b.Value.CompareTo(a.Value));

            StringBuilder summary = new StringBuilder();

            summary.AppendLine($"Agent-Based repeated evaluation over {runs} runs");
            summary.AppendLine("----------------------------------------");
            summary.AppendLine($"Average Points MAE: {averageMae:F2}");
            summary.AppendLine($"Median Points MAE: {medianMae:F2}");
            summary.AppendLine($"MAE Standard Deviation: {maeStandardDeviation:F2}");
            summary.AppendLine($"Best Points MAE: {bestMae:F2}");
            summary.AppendLine($"Worst Points MAE: {worstMae:F2}");
            summary.AppendLine($"Execution time: {elapsedSeconds:F4} seconds");
            summary.AppendLine($"Simulations per minute: {simulationsPerMinute:F2}");
            summary.AppendLine();
            summary.AppendLine("ABM league title winners over repeated simulations:");

            foreach (KeyValuePair<int, int> pair in titleWins)
            {
                string teamName = teamRegistry.GetTeamName(pair.Key);

                float percentage = (float)pair.Value / runs * 100f;

                string titleLine = $"{teamName}: {pair.Value} titles out of {runs} ({percentage:F2}%)";

                Debug.Log(titleLine);
                summary.AppendLine(titleLine);
            }

            evidenceExporter.ExportTextEvidence(
                $"abm_repeated_summary_{runs}_runs.txt",
                summary.ToString()
            );

            PrintAverageAgentBasedTable(
                actualTable,
                totalPointsByTeamId,
                totalPositionByTeamId,
                appearancesByTeamId,
                runs
            );
        }

        private void PrintAverageAgentBasedTable(
            LeagueTable actualTable,
            Dictionary<int, float> totalPointsByTeamId,
            Dictionary<int, float> totalPositionByTeamId,
            Dictionary<int, int> appearancesByTeamId,
            int runs)
        {
            List<AverageTeamResult> averageResults = new();

            List<LeagueTable.Entry> actualEntries = actualTable.Sorted();

            Dictionary<int, LeagueTable.Entry> actualByTeamId = new();
            Dictionary<int, int> actualPositionByTeamId = new();

            for (int i = 0; i < actualEntries.Count; i++)
            {
                LeagueTable.Entry actualEntry = actualEntries[i];

                actualByTeamId[actualEntry.TeamId] = actualEntry;
                actualPositionByTeamId[actualEntry.TeamId] = i + 1;
            }

            foreach (KeyValuePair<int, float> pair in totalPointsByTeamId)
            {
                int teamId = pair.Key;

                if (!appearancesByTeamId.ContainsKey(teamId) || appearancesByTeamId[teamId] == 0)
                {
                    continue;
                }

                float averagePoints = totalPointsByTeamId[teamId] / appearancesByTeamId[teamId];
                float averagePosition = totalPositionByTeamId[teamId] / appearancesByTeamId[teamId];

                int actualPoints = 0;
                int actualPosition = 0;

                if (actualByTeamId.ContainsKey(teamId))
                {
                    actualPoints = actualByTeamId[teamId].Points;
                }

                if (actualPositionByTeamId.ContainsKey(teamId))
                {
                    actualPosition = actualPositionByTeamId[teamId];
                }

                averageResults.Add(new AverageTeamResult
                {
                    TeamId = teamId,
                    AveragePoints = averagePoints,
                    AveragePosition = averagePosition,
                    ActualPosition = actualPosition,
                    ActualPoints = actualPoints,
                    PointsError = Mathf.Abs(actualPoints - averagePoints)
                });
            }

            averageResults.Sort((a, b) =>
            {
                int pointsCompare = b.AveragePoints.CompareTo(a.AveragePoints);

                if (pointsCompare != 0)
                {
                    return pointsCompare;
                }

                return a.AveragePosition.CompareTo(b.AveragePosition);
            });

            Debug.Log("========================================");
            Debug.Log($"ABM average simulated table over {runs} repeated runs:");
            Debug.Log("----------------------------------------");

            for (int i = 0; i < averageResults.Count; i++)
            {
                AverageTeamResult result = averageResults[i];

                string teamName = teamRegistry.GetTeamName(result.TeamId);

                Debug.Log(
                    $"{i + 1}. {teamName} " +
                    $"AvgPts:{result.AveragePoints:F2} " +
                    $"AvgPos:{result.AveragePosition:F2} " +
                    $"ActualPos:{result.ActualPosition} " +
                    $"ActualPts:{result.ActualPoints} " +
                    $"Error:{result.PointsError:F2}"
                );
            }

            Debug.Log("========================================");

            evidenceExporter.ExportAverageTableCsv(
                $"abm_average_table_{runs}_runs.csv",
                averageResults
            );
        }

        private LeagueTable BuildSimulatedLeagueTable(
            List<StatisticalModel.SimulatedMatchResult> simulatedResults,
            bool verbose = false)
        {
            LeagueTable simulatedTable = new LeagueTable();

            foreach (StatisticalModel.SimulatedMatchResult result in simulatedResults)
            {
                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = teamRegistry.GetTeamId(result.HomeTeam),
                    AwayTeamId = teamRegistry.GetTeamId(result.AwayTeam),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                simulatedTable.Apply(record);
            }

            if (verbose)
            {
                List<LeagueTable.Entry> sortedTable = simulatedTable.Sorted();

                Debug.Log("Statistical model simulated evaluation table:");

                for (int i = 0; i < sortedTable.Count; i++)
                {
                    LeagueTable.Entry entry = sortedTable[i];

                    string teamName = teamRegistry.GetTeamName(entry.TeamId);

                    Debug.Log(
                        $"{i + 1}. {teamName} " +
                        $"Pts:{entry.Points} " +
                        $"P:{entry.Played} " +
                        $"W:{entry.Wins} " +
                        $"D:{entry.Draws} " +
                        $"L:{entry.Losses} " +
                        $"GF:{entry.GoalsFor} " +
                        $"GA:{entry.GoalsAgainst} " +
                        $"GD:{entry.GoalsFor - entry.GoalsAgainst}"
                    );
                }
            }

            return simulatedTable;
        }

        private void RunRepeatedStatisticalEvaluation(
            StatisticalModel statisticalModel,
            List<OpenFootballMatch> evaluationMatches,
            LeagueTable actualTable,
            int runs)
        {
            float totalMae = 0f;
            float bestMae = float.MaxValue;
            float worstMae = float.MinValue;

            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < runs; i++)
            {
                List<StatisticalModel.SimulatedMatchResult> simulatedResults =
                    statisticalModel.SimulateSeason(evaluationMatches, false);

                LeagueTable simulatedTable = BuildSimulatedLeagueTable(simulatedResults);

                float mae = SimulationStatistics.CalculatePointsMAE(actualTable, simulatedTable);

                totalMae += mae;

                if (mae < bestMae)
                {
                    bestMae = mae;
                }

                if (mae > worstMae)
                {
                    worstMae = mae;
                }
            }

            stopwatch.Stop();

            float averageMae = totalMae / runs;
            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            double simulationsPerMinute = runs / elapsedSeconds * 60.0;

            Debug.Log($"Statistical repeated evaluation over {runs} runs:");
            Debug.Log($"Average Points MAE: {averageMae:F2}");
            Debug.Log($"Best Points MAE: {bestMae:F2}");
            Debug.Log($"Worst Points MAE: {worstMae:F2}");
            Debug.Log($"Execution time: {elapsedSeconds:F4} seconds");
            Debug.Log($"Simulations per minute: {simulationsPerMinute:F2}");

            StringBuilder summary = new StringBuilder();

            summary.AppendLine($"Statistical repeated evaluation over {runs} runs");
            summary.AppendLine("----------------------------------------");
            summary.AppendLine($"Average Points MAE: {averageMae:F2}");
            summary.AppendLine($"Best Points MAE: {bestMae:F2}");
            summary.AppendLine($"Worst Points MAE: {worstMae:F2}");
            summary.AppendLine($"Execution time: {elapsedSeconds:F4} seconds");
            summary.AppendLine($"Simulations per minute: {simulationsPerMinute:F2}");

            evidenceExporter.ExportTextEvidence(
                $"statistical_repeated_summary_{runs}_runs.txt",
                summary.ToString()
            );
        }

        private void CompareTablesWithPointsMAE(LeagueTable actualTable, LeagueTable simulatedTable)
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

            Debug.Log("Actual vs simulated points comparison:");

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

                string teamName = teamRegistry.GetTeamName(actualEntry.TeamId);

                Debug.Log(
                    $"{teamName} " +
                    $"Actual:{actualEntry.Points} " +
                    $"Simulated:{simulatedEntry.Points} " +
                    $"Error:{absoluteError}"
                );
            }

            if (comparedTeams == 0)
            {
                Debug.LogWarning("No teams could be compared when comparing actual and simulated points.");
                return;
            }

            float mae = totalAbsoluteError / comparedTeams;

            Debug.Log($"Points MAE: {mae:F2}");
        }

        private LeagueTable BuildActualEvaluationTable(List<OpenFootballMatch> evaluationMatches)
        {
            LeagueTable actualTable = new LeagueTable();

            foreach (OpenFootballMatch match in evaluationMatches)
            {
                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = teamRegistry.GetTeamId(match.HomeTeam),
                    AwayTeamId = teamRegistry.GetTeamId(match.AwayTeam),
                    HomeGoals = match.HomeGoals,
                    AwayGoals = match.AwayGoals
                };

                actualTable.Apply(record);
            }

            return actualTable;
        }
    }
}
