using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Sim;
using System.IO;
using System.Text;

namespace Data
{
    public class OpenFootballLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset[] seasonFiles;

        private readonly List<OpenFootballMatch> matches = new();

        private readonly Dictionary<string, int> teamIds = new();
        private readonly Dictionary<int, string> teamNames = new();
        private int nextTeamId = 1;

        private void Start()
        {
            if (seasonFiles == null || seasonFiles.Length == 0)
            {
                Debug.LogError("No OpenFootball season files assigned.");
                return;
            }

            LoadAllSeasonFiles();

            List<OpenFootballMatch> trainingMatches = matches.FindAll(m => !m.Season.Contains("2025_26"));
            List<OpenFootballMatch> evaluationMatches = matches.FindAll(m => m.Season.Contains("2025_26"));

            Debug.Log($"Training matches: {trainingMatches.Count}");
            Debug.Log($"Evaluation matches: {evaluationMatches.Count}");

            StatisticalModel statisticalModel = new StatisticalModel();
            statisticalModel.Train(trainingMatches);
            statisticalModel.PrintTeamStrengths(10);

            PrintGeneratedAgentSquad(statisticalModel, "Liverpool");

            statisticalModel.PrintExpectedGoalsSamples(evaluationMatches, 10);
            statisticalModel.PrintSimulatedMatchSamples(evaluationMatches, 10);

            List<StatisticalModel.SimulatedMatchResult> simulatedResults =
                statisticalModel.SimulateSeason(evaluationMatches);

            LeagueTable simulatedTable = PrintSimulatedLeagueTable(simulatedResults);
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

            float agentBasedMae = CalculatePointsMAE(actualTable, agentBasedTable);
            Debug.Log($"Agent-Based Model Points MAE: {agentBasedMae:F2}");

            PrintGoalsPerMatchComparison(evaluationMatches, agentBasedTable);

            RunRepeatedAgentBasedEvaluation(
                statisticalModel,
                evaluationMatches,
                actualTable,
                100
            );
        }

        private void LoadAllSeasonFiles()
        {
            matches.Clear();
            teamIds.Clear();
            teamNames.Clear();
            nextTeamId = 1;

            foreach (TextAsset file in seasonFiles)
            {
                if (file == null)
                {
                    Debug.LogWarning("An assigned season file slot is empty.");
                    continue;
                }

                int beforeCount = matches.Count;

                LoadMatches(file.text, file.name);

                int loadedFromFile = matches.Count - beforeCount;

                Debug.Log($"Loaded {loadedFromFile} matches from {file.name}.");
            }

            Debug.Log($"Loaded {matches.Count} total matches from {seasonFiles.Length} season files.");
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

        private string GetEvidenceOutputFolder()
        {
            string folderPath = Path.Combine(Application.persistentDataPath, "EvidenceExports");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        private void ExportTextEvidence(
    string fileName,
    string content)
        {
            string folderPath = GetEvidenceOutputFolder();
            string filePath = Path.Combine(folderPath, fileName);

            File.WriteAllText(filePath, content);

            Debug.Log($"Evidence text exported to: {filePath}");
        }

        private void ExportAverageTableCsv(
    string fileName,
    List<AverageTeamResult> averageResults)
        {
            string folderPath = GetEvidenceOutputFolder();
            string filePath = Path.Combine(folderPath, fileName);

            StringBuilder csv = new StringBuilder();

            csv.AppendLine("Position,Team,AveragePoints,AveragePosition,ActualPosition,ActualPoints,PointsError");

            for (int i = 0; i < averageResults.Count; i++)
            {
                AverageTeamResult result = averageResults[i];

                string teamName = teamNames.ContainsKey(result.TeamId)
                    ? teamNames[result.TeamId]
                    : $"Team {result.TeamId}";

                csv.AppendLine(
                    $"{i + 1}," +
                    $"{EscapeCsv(teamName)}," +
                    $"{result.AveragePoints:F2}," +
                    $"{result.AveragePosition:F2}," +
                    $"{result.ActualPosition}," +
                    $"{result.ActualPoints}," +
                    $"{result.PointsError:F2}"
                );
            }

            File.WriteAllText(filePath, csv.ToString());

            Debug.Log($"Average table CSV exported to: {filePath}");
        }

        private string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private LeagueTable SimulateAgentBasedEvaluationSeason(
            StatisticalModel statisticalModel,
            List<OpenFootballMatch> evaluationMatches)
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
                    HomeTeamId = GetTeamId(result.HomeTeamName),
                    AwayTeamId = GetTeamId(result.AwayTeamName),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                abmTable.Apply(record);
            }

            Debug.Log($"Simulated {evaluationMatches.Count} ABM matches.");
            Debug.Log($"Generated {squadsByTeamName.Count} ABM squads.");

            PrintAgentBasedLeagueTable(abmTable);

            return abmTable;
        }

        private LeagueTable SimulateAgentBasedEvaluationSeasonQuiet(
            StatisticalModel statisticalModel,
            List<OpenFootballMatch> evaluationMatches)
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
                    HomeTeamId = GetTeamId(result.HomeTeamName),
                    AwayTeamId = GetTeamId(result.AwayTeamName),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                abmTable.Apply(record);
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

                string teamName = teamNames.ContainsKey(entry.TeamId)
                    ? teamNames[entry.TeamId]
                    : $"Team {entry.TeamId}";

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
                LeagueTable agentBasedTable = SimulateAgentBasedEvaluationSeasonQuiet(
                    statisticalModel,
                    evaluationMatches
                );

                float mae = CalculatePointsMAE(actualTable, agentBasedTable);

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
            float medianMae = CalculateMedian(maeValues);
            float maeStandardDeviation = CalculateStandardDeviation(maeValues, averageMae);

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
                string teamName = teamNames.ContainsKey(pair.Key)
                    ? teamNames[pair.Key]
                    : $"Team {pair.Key}";

                float percentage = (float)pair.Value / runs * 100f;

                string titleLine = $"{teamName}: {pair.Value} titles out of {runs} ({percentage:F2}%)";

                Debug.Log(titleLine);
                summary.AppendLine(titleLine);
            }

            ExportTextEvidence(
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

         

            summary.AppendLine($"Statistical repeated evaluation over {runs} runs");
            summary.AppendLine("----------------------------------------");
            summary.AppendLine($"Average Points MAE: {averageMae:F2}");
            summary.AppendLine($"Best Points MAE: {bestMae:F2}");
            summary.AppendLine($"Worst Points MAE: {worstMae:F2}");
            summary.AppendLine($"Execution time: {elapsedSeconds:F4} seconds");
            summary.AppendLine($"Simulations per minute: {simulationsPerMinute:F2}");

            ExportTextEvidence(
                $"statistical_repeated_summary_{runs}_runs.txt",
                summary.ToString()
            );


        }

        private float CalculateMedian(List<float> values)
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

        private float CalculateStandardDeviation(List<float> values, float mean)
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

                string teamName = teamNames.ContainsKey(result.TeamId)
                    ? teamNames[result.TeamId]
                    : $"Team {result.TeamId}";

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

            ExportAverageTableCsv(
    $"abm_average_table_{runs}_runs.csv",
    averageResults
);

        }

        private class AverageTeamResult
        {
            public int TeamId;
            public float AveragePoints;
            public float AveragePosition;
            public int ActualPosition;
            public int ActualPoints;
            public float PointsError;
        }


        private float CalculatePointsMAE(LeagueTable actualTable, LeagueTable simulatedTable)
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

        private LeagueTable PrintSimulatedLeagueTable(List<StatisticalModel.SimulatedMatchResult> simulatedResults)
        {
            LeagueTable simulatedTable = new LeagueTable();

            foreach (StatisticalModel.SimulatedMatchResult result in simulatedResults)
            {
                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = GetTeamId(result.HomeTeam),
                    AwayTeamId = GetTeamId(result.AwayTeam),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                simulatedTable.Apply(record);
            }

            List<LeagueTable.Entry> sortedTable = simulatedTable.Sorted();

            Debug.Log("Statistical model simulated evaluation table:");

            for (int i = 0; i < sortedTable.Count; i++)
            {
                LeagueTable.Entry entry = sortedTable[i];

                string teamName = teamNames.ContainsKey(entry.TeamId)
                    ? teamNames[entry.TeamId]
                    : $"Team {entry.TeamId}";

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

            return simulatedTable;
        }

        private LeagueTable BuildSimulatedLeagueTable(List<StatisticalModel.SimulatedMatchResult> simulatedResults)
        {
            LeagueTable simulatedTable = new LeagueTable();

            foreach (StatisticalModel.SimulatedMatchResult result in simulatedResults)
            {
                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = GetTeamId(result.HomeTeam),
                    AwayTeamId = GetTeamId(result.AwayTeam),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                simulatedTable.Apply(record);
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

                float mae = CalculatePointsMAE(actualTable, simulatedTable);

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

                string teamName = teamNames.ContainsKey(actualEntry.TeamId)
                    ? teamNames[actualEntry.TeamId]
                    : $"Team {actualEntry.TeamId}";

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

        private void LoadMatches(string fileText, string seasonName)
        {
            string[] lines = fileText.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("#"))
                {
                    continue;
                }

                OpenFootballMatch? parsedMatch = ParseLine(line, seasonName);

                if (parsedMatch.HasValue)
                {
                    matches.Add(parsedMatch.Value);
                }
                else if (Regex.IsMatch(line, @"\d+-\d+"))
                {
                    Debug.LogWarning($"Could not parse match line: {line}");
                }
            }
        }

        private OpenFootballMatch? ParseLine(string line, string seasonName)
        {
            // Removes optional kickoff time at start, e.g. "20:00 "
            line = Regex.Replace(line, @"^\d{1,2}:\d{2}\s+", "");

            // Newer format:
            // Manchester United v Fulham 1-0 (0-0)
            Match newerFormat = Regex.Match(
                line,
                @"^(.+?)\s+v\s+(.+?)\s+(\d+)-(\d+)(?:\s+\(\d+-\d+\))?\s*$"
            );

            if (newerFormat.Success)
            {
                return new OpenFootballMatch
                {
                    HomeTeam = NormaliseTeamName(newerFormat.Groups[1].Value),
                    AwayTeam = NormaliseTeamName(newerFormat.Groups[2].Value),
                    HomeGoals = int.Parse(newerFormat.Groups[3].Value),
                    AwayGoals = int.Parse(newerFormat.Groups[4].Value),
                    Season = seasonName,
                };
            }

            // Older format:
            // Arsenal 4-3 (2-2) Leicester City
            // Arsenal 4-3 Leicester City
            Match olderFormat = Regex.Match(
                line,
                @"^(.+?)\s+(\d+)-(\d+)(?:\s+\(\d+-\d+\))?\s+(.+?)\s*$"
            );

            if (olderFormat.Success)
            {
                return new OpenFootballMatch
                {
                    HomeTeam = NormaliseTeamName(olderFormat.Groups[1].Value),
                    AwayTeam = NormaliseTeamName(olderFormat.Groups[4].Value),
                    HomeGoals = int.Parse(olderFormat.Groups[2].Value),
                    AwayGoals = int.Parse(olderFormat.Groups[3].Value),
                    Season = seasonName,
                };
            }

            return null;
        }

        private LeagueTable BuildActualEvaluationTable(List<OpenFootballMatch> evaluationMatches)
        {
            LeagueTable actualTable = new LeagueTable();

            foreach (OpenFootballMatch match in evaluationMatches)
            {
                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = GetTeamId(match.HomeTeam),
                    AwayTeamId = GetTeamId(match.AwayTeam),
                    HomeGoals = match.HomeGoals,
                    AwayGoals = match.AwayGoals
                };

                actualTable.Apply(record);
            }

            return actualTable;
        }

        private string NormaliseTeamName(string teamName)
        {
            teamName = teamName.Trim();

            // Merge "Manchester City" and "Manchester City FC"
            if (teamName.EndsWith(" FC"))
            {
                teamName = teamName.Substring(0, teamName.Length - 3);
            }

            return teamName;
        }

        private int GetTeamId(string teamName)
        {
            if (!teamIds.ContainsKey(teamName))
            {
                int id = nextTeamId;

                teamIds[teamName] = id;
                teamNames[id] = teamName;

                nextTeamId++;
            }

            return teamIds[teamName];
        }
    }

    public struct OpenFootballMatch
    {
        public string HomeTeam;
        public string AwayTeam;
        public int HomeGoals;
        public int AwayGoals;
        public string Season;
    }
}