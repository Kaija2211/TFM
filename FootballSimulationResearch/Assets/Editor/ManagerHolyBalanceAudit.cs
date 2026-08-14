using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Data;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

// Exercises the same generated players, formation-fit layer and Manager Mode match
// simulator used by a fresh career. Unlike the target-only importer audit, every goal
// here is produced by real PlayerAgent attributes and AgentMatchSimulator events.
public static class ManagerHolyBalanceAudit
{
    private const int Worlds = 10;
    private const int SeasonsPerWorld = 20;
    private const int Seed = 221104;
    private const float HomeGoalBase = 1.45f;
    private const float AwayGoalBase = 1.20f;
    private const float RatingToLogStrength = 0.10f;

    private sealed class TableRow
    {
        public int Points;
        public int GoalsFor;
        public int GoalsAgainst;
        public int GoalDifference => GoalsFor - GoalsAgainst;
    }

    [MenuItem("TFM/Audits/Manager Holy Balance")]
    public static void Run()
    {
        TextAsset historyAsset = Resources.Load<TextAsset>("World/football_world_history");
        if (historyAsset == null) throw new FileNotFoundException("Runtime world history resource was not found.");
        FootballWorldHistory history = FootballWorldHistory.FromTextAsset(historyAsset);
        List<ClubWorldGenerationProfileRecord> clubs = history.Data.WorldGenerationProfiles
            .Where(profile => profile.CountryCode == "eng" && profile.Level == 1)
            .GroupBy(profile => profile.ReferenceSeason)
            .OrderByDescending(group => group.Key)
            .First()
            .OrderBy(profile => profile.ClubId)
            .ToList();
        if (clubs.Count != 20) throw new InvalidDataException($"Expected 20 latest English top-flight profiles, found {clubs.Count}.");

        float leagueMean = clubs.Average(profile => (float)profile.FirstTeamOverall);
        List<float> goalsPerGame = new();
        List<float> championPoints = new();
        List<float> bottomPoints = new();
        List<float> bestGoalDifference = new();
        List<float> worstGoalDifference = new();
        int homeWins = 0;
        int draws = 0;
        int awayWins = 0;
        Dictionary<string, int> titles = clubs.ToDictionary(profile => profile.ClubName, _ => 0);

        for (int world = 0; world < Worlds; world++)
        {
            Random.InitState(Seed + world);
            AgentSquadGenerator generator = new();
            Manager.AgentMatchSimulator simulator = new();
            Dictionary<string, AgentTeam> teams = new();
            foreach (ClubWorldGenerationProfileRecord club in clubs)
            {
                SquadQualityTarget target = new((float)club.FirstTeamOverall, (float)club.BenchOverall, (float)club.ReserveOverall);
                teams[club.ClubName] = generator.GenerateSquad(club.ClubName, target);
            }

            for (int season = 0; season < SeasonsPerWorld; season++)
            {
                Dictionary<string, TableRow> table = clubs.ToDictionary(club => club.ClubName, _ => new TableRow());
                int seasonGoals = 0;
                int seasonMatches = 0;
                foreach (ClubWorldGenerationProfileRecord home in clubs)
                {
                    foreach (ClubWorldGenerationProfileRecord away in clubs)
                    {
                        if (home.ClubId == away.ClubId) continue;
                        float homeAttack = Mathf.Exp(((float)home.FirstTeamOverall - leagueMean) * RatingToLogStrength);
                        float homeDefence = Mathf.Exp(-((float)home.FirstTeamOverall - leagueMean) * RatingToLogStrength);
                        float awayAttack = Mathf.Exp(((float)away.FirstTeamOverall - leagueMean) * RatingToLogStrength);
                        float awayDefence = Mathf.Exp(-((float)away.FirstTeamOverall - leagueMean) * RatingToLogStrength);
                        AgentTeam homeAdjusted = ManagerFormationFit.BuildFitAdjustedTeam(teams[home.ClubName], generator.GetStartingPositions(teams[home.ClubName].Formation));
                        AgentTeam awayAdjusted = ManagerFormationFit.BuildFitAdjustedTeam(teams[away.ClubName], generator.GetStartingPositions(teams[away.ClubName].Formation));
                        Manager.AgentMatchSimulator.AgentMatchResult result = simulator.SimulateMatch(
                            homeAdjusted, awayAdjusted,
                            HomeGoalBase * homeAttack * awayDefence,
                            AwayGoalBase * awayAttack * homeDefence);

                        TableRow homeRow = table[home.ClubName];
                        TableRow awayRow = table[away.ClubName];
                        homeRow.GoalsFor += result.HomeGoals;
                        homeRow.GoalsAgainst += result.AwayGoals;
                        awayRow.GoalsFor += result.AwayGoals;
                        awayRow.GoalsAgainst += result.HomeGoals;
                        if (result.HomeGoals > result.AwayGoals)
                        {
                            homeRow.Points += 3;
                            homeWins++;
                        }
                        else if (result.HomeGoals < result.AwayGoals)
                        {
                            awayRow.Points += 3;
                            awayWins++;
                        }
                        else
                        {
                            homeRow.Points++;
                            awayRow.Points++;
                            draws++;
                        }
                        seasonGoals += result.HomeGoals + result.AwayGoals;
                        seasonMatches++;
                    }
                }

                KeyValuePair<string, TableRow>[] ordered = table.OrderByDescending(pair => pair.Value.Points)
                    .ThenByDescending(pair => pair.Value.GoalDifference)
                    .ThenByDescending(pair => pair.Value.GoalsFor).ToArray();
                goalsPerGame.Add(seasonGoals / (float)seasonMatches);
                championPoints.Add(ordered[0].Value.Points);
                bottomPoints.Add(ordered[^1].Value.Points);
                bestGoalDifference.Add(ordered[0].Value.GoalDifference);
                worstGoalDifference.Add(ordered[^1].Value.GoalDifference);
                titles[ordered[0].Key]++;
            }
        }

        int totalMatches = Worlds * SeasonsPerWorld * clubs.Count * (clubs.Count - 1);
        float meanGoals = goalsPerGame.Average();
        float meanChampion = championPoints.Average();
        float meanBottom = bottomPoints.Average();
        float meanBestGd = bestGoalDifference.Average();
        float meanWorstGd = worstGoalDifference.Average();
        float maxTitleShare = titles.Values.Max() / (float)(Worlds * SeasonsPerWorld);
        bool pass = meanGoals >= 2.40f && meanGoals <= 3.20f &&
                    meanChampion >= 72f && meanChampion <= 95f &&
                    meanBottom >= 18f && meanBottom <= 38f &&
                    meanBestGd >= 25f && meanBestGd <= 65f &&
                    meanWorstGd <= -20f && meanWorstGd >= -55f &&
                    maxTitleShare <= 0.45f;

        StringBuilder report = new();
        report.AppendLine("# Manager Mode holy-balance audit").AppendLine();
        report.AppendLine($"Actual generated-player and Manager match-engine audit: {Worlds} worlds × {SeasonsPerWorld} seasons ({totalMatches:N0} matches).").AppendLine();
        report.AppendLine($"- Status: **{(pass ? "PASS" : "REVIEW")}**");
        report.AppendLine($"- Goals/game: {meanGoals:F3}");
        report.AppendLine($"- Champion points: {meanChampion:F1}");
        report.AppendLine($"- Bottom points: {meanBottom:F1}");
        report.AppendLine($"- Best goal difference: {meanBestGd:F1}");
        report.AppendLine($"- Worst goal difference: {meanWorstGd:F1}");
        report.AppendLine($"- Home/draw/away: {homeWins / (float)totalMatches:P1} / {draws / (float)totalMatches:P1} / {awayWins / (float)totalMatches:P1}");
        report.AppendLine($"- Highest title share: {maxTitleShare:P1}").AppendLine();
        report.AppendLine("| Club | Titles | Share |");
        report.AppendLine("|---|---:|---:|");
        foreach (KeyValuePair<string, int> club in titles.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
            report.AppendLine($"| {club.Key} | {club.Value} | {club.Value / (float)(Worlds * SeasonsPerWorld):P1} |");

        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ManagerHolyBalanceAudit.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, report.ToString());
        Debug.Log($"Manager holy-balance audit {(pass ? "PASSED" : "requires review")}: GPG {meanGoals:F3}, champion {meanChampion:F1}, bottom {meanBottom:F1}, best GD {meanBestGd:F1}, worst GD {meanWorstGd:F1}, max title share {maxTitleShare:P1}. Report: {output}");
    }
}
