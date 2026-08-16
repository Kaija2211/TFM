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

// Generates real player agents from the historical quality targets and verifies that
// squad calibration survives the position-specific attribute generator. It remains an
// editor audit until live new-save bootstrapping is ready to consume stable club IDs.
public static class WorldGenerationProfileAudit
{
    private const int AuditWorlds = 25;
    private const int AuditSeed = 221104;

    [MenuItem("TFM/Audits/World Generation Profiles")]
    public static void Run()
    {
        string historyPath = Path.Combine(Application.dataPath, "Data", "Generated", "football_world_history.json.txt");
        if (!File.Exists(historyPath)) throw new FileNotFoundException("Generated football world history was not found.", historyPath);
        FootballWorldHistory history = FootballWorldHistory.FromJson(File.ReadAllText(historyPath));
        List<ClubWorldGenerationProfileRecord> targets = history.Data.WorldGenerationProfiles
            .Where(profile => profile.Level == 1)
            .GroupBy(profile => profile.CountryCode)
            .SelectMany(group =>
            {
                string latest = group.Max(profile => profile.ReferenceSeason);
                return group.Where(profile => profile.ReferenceSeason == latest);
            })
            .OrderBy(profile => profile.CountryCode).ThenBy(profile => profile.ClubId)
            .ToList();

        Dictionary<string, List<(float starters, float bench, float reserves, float display, float starterValue, float benchValue, float reserveValue)>> results = targets
            .ToDictionary(target => target.ClubId, _ => new List<(float, float, float, float, float, float, float)>(AuditWorlds));
        for (int world = 0; world < AuditWorlds; world++)
        {
            Random.InitState(AuditSeed + world);
            AgentSquadGenerator generator = new();
            foreach (ClubWorldGenerationProfileRecord target in targets)
            {
                SquadQualityTarget quality = new((float)target.FirstTeamOverall, (float)target.BenchOverall, (float)target.ReserveOverall);
                AgentTeam team = generator.GenerateSquad(target.ClubName, quality);
                ManagerPlayerDerivedStrength.Profile profile = ManagerPlayerDerivedStrength.Calculate(team, generator.GetStartingPositions(team.Formation));
                results[target.ClubId].Add((
                    team.StartingEleven.Average(player => player.GetOverallRating()),
                    team.Bench.Average(player => player.GetOverallRating()),
                    team.Reserves.Average(player => player.GetOverallRating()),
                    profile.DisplayOverall,
                    team.StartingEleven.Average(ManagerClubFinance.GetMarketValue),
                    team.Bench.Average(ManagerClubFinance.GetMarketValue),
                    team.Reserves.Average(ManagerClubFinance.GetMarketValue)));
            }
        }

        StringBuilder csv = new("Country,ClubId,Club,Worlds,TargetFirstTeam,ActualFirstTeam,TargetBench,ActualBench,ActualReserves,MeanPlayerDerivedDisplay,MeanStarterValueM,MeanBenchValueM,MeanReserveValueM\n");
        foreach (ClubWorldGenerationProfileRecord target in targets)
        {
            List<(float starters, float bench, float reserves, float display, float starterValue, float benchValue, float reserveValue)> rows = results[target.ClubId];
            csv.Append(target.CountryCode).Append(',').Append(Escape(target.ClubId)).Append(',').Append(Escape(target.ClubName)).Append(',')
                .Append(AuditWorlds).Append(',').Append(Number(target.FirstTeamOverall)).Append(',').Append(Number(rows.Average(row => row.starters))).Append(',')
                .Append(Number(target.BenchOverall)).Append(',').Append(Number(rows.Average(row => row.bench))).Append(',')
                .Append(Number(rows.Average(row => row.reserves))).Append(',')
                .Append(Number(rows.Average(row => row.display))).Append(',')
                .Append(Number(rows.Average(row => row.starterValue))).Append(',')
                .Append(Number(rows.Average(row => row.benchValue))).Append(',')
                .Append(Number(rows.Average(row => row.reserveValue))).Append('\n');
        }

        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "WorldGenerationProfileAudit.csv"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, csv.ToString());
        float worstCalibrationError = targets.Max(target =>
            Mathf.Abs((float)target.FirstTeamOverall - results[target.ClubId].Average(row => row.starters)));
        Debug.Log($"World generation profile audit wrote {AuditWorlds} worlds / {targets.Count} clubs to {output}. " +
                  $"Worst mean first-team calibration error: {worstCalibrationError:F3}.");
    }

    private static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
