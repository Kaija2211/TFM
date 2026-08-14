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

// Reproducible editor-only diagnostic. It proves the generated lineup profile spread
// before any player-derived value is allowed to alter live match odds.
public static class PlayerDerivedStrengthAudit
{
    private const int AuditSeed = 221104;
    private const int AuditWorlds = 100;

    [MenuItem("TFM/Audits/Player-Derived Strength Profiles")]
    public static void RunFromMenu() => Run();

    public static void Run()
    {
        string rawDirectory = Path.Combine(Application.dataPath, "Data", "Raw", "OpenFootball");
        string[] trainingFiles = Directory.GetFiles(rawDirectory, "premierleague_20*.txt")
            .Where(path => !Path.GetFileName(path).Contains("2025_26"))
            .OrderBy(path => path)
            .ToArray();
        string currentSeasonPath = Path.Combine(rawDirectory, "premierleague_2025_26.txt");
        if (trainingFiles.Length == 0 || !File.Exists(currentSeasonPath))
            throw new FileNotFoundException("Player strength audit could not find the vendored Premier League training/current-season files.");

        List<OpenFootballMatch> trainingMatches = new();
        foreach (string path in trainingFiles)
            trainingMatches.AddRange(OpenFootballTextParser.ParseSeasonFile(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path)));

        StatisticalModel model = new();
        model.Train(trainingMatches);
        List<string> teams = OpenFootballTextParser
            .ParseSeasonFile(File.ReadAllText(currentSeasonPath), Path.GetFileNameWithoutExtension(currentSeasonPath))
            .SelectMany(match => new[] { match.HomeTeam, match.AwayTeam })
            .Distinct()
            .OrderBy(name => name)
            .ToList();

        Dictionary<string, List<ManagerPlayerDerivedStrength.Profile>> profilesByTeam = teams
            .ToDictionary(team => team, _ => new List<ManagerPlayerDerivedStrength.Profile>(AuditWorlds));
        for (int world = 0; world < AuditWorlds; world++)
        {
            Random.InitState(AuditSeed + world);
            AgentSquadGenerator generator = new();
            foreach (string teamName in teams)
            {
                StatisticalModel.TeamStrength legacy = model.GetTeamStrength(teamName);
                AgentTeam squad = generator.GenerateSquad(teamName, legacy.AttackStrength, legacy.DefenceStrength);
                profilesByTeam[teamName].Add(ManagerPlayerDerivedStrength.Calculate(
                    squad,
                    generator.GetStartingPositions(squad.Formation)));
            }
        }

        List<(string team, float legacyAttack, float legacyDefence, float control, float creation, float threat, float defence, float goalkeeping, float depth, float overall, float overallStdDev, float minimum, float maximum)> rows = new();
        foreach (string teamName in teams)
        {
            List<ManagerPlayerDerivedStrength.Profile> profiles = profilesByTeam[teamName];
            float overall = profiles.Average(profile => profile.DisplayOverall);
            float variance = profiles.Average(profile => (profile.DisplayOverall - overall) * (profile.DisplayOverall - overall));
            StatisticalModel.TeamStrength legacy = model.GetTeamStrength(teamName);
            rows.Add((
                teamName, legacy.AttackStrength, legacy.DefenceStrength,
                profiles.Average(profile => profile.Control),
                profiles.Average(profile => profile.ChanceCreation),
                profiles.Average(profile => profile.GoalThreat),
                profiles.Average(profile => profile.DefensiveResistance),
                profiles.Average(profile => profile.Goalkeeping),
                profiles.Average(profile => profile.Depth),
                overall, Mathf.Sqrt(variance),
                profiles.Min(profile => profile.DisplayOverall),
                profiles.Max(profile => profile.DisplayOverall)));
        }

        rows.Sort((left, right) => right.overall.CompareTo(left.overall));
        StringBuilder csv = new("Rank,Team,Worlds,LegacyAttack,LegacyDefence,MeanControl,MeanCreation,MeanThreat,MeanDefence,MeanGoalkeeping,MeanDepth,MeanDisplayOverall,OverallStdDev,MinimumOverall,MaximumOverall\n");
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            csv.Append(index + 1).Append(',').Append(EscapeCsv(row.team)).Append(',').Append(AuditWorlds).Append(',')
                .Append(Number(row.legacyAttack)).Append(',').Append(Number(row.legacyDefence)).Append(',')
                .Append(Number(row.control)).Append(',').Append(Number(row.creation)).Append(',')
                .Append(Number(row.threat)).Append(',').Append(Number(row.defence)).Append(',')
                .Append(Number(row.goalkeeping)).Append(',').Append(Number(row.depth)).Append(',')
                .Append(Number(row.overall)).Append(',').Append(Number(row.overallStdDev)).Append(',')
                .Append(Number(row.minimum)).Append(',').Append(Number(row.maximum)).Append('\n');
        }

        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "PlayerDerivedStrengthAudit.csv"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, csv.ToString());

        float strongest = rows.First().overall;
        float weakest = rows.Last().overall;
        Debug.Log($"Player-derived strength audit wrote {AuditWorlds} worlds / {rows.Count} clubs to {output}. " +
                  $"Mean display spread {strongest:F2}-{weakest:F2} ({strongest - weakest:F2}).");
    }

    private static string Number(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
