using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Sim;

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

                LoadMatches(file.text);

                int loadedFromFile = matches.Count - beforeCount;

                Debug.Log($"Loaded {loadedFromFile} matches from {file.name}.");
            }

            Debug.Log($"Loaded {matches.Count} total matches from {seasonFiles.Length} season files.");

            List<MatchRecord> records = ConvertToMatchRecords();

            LeagueTable table = new LeagueTable();

            foreach (MatchRecord record in records)
            {
                table.Apply(record);
            }

            PrintLeagueTable(table);
        }

        private void LoadMatches(string fileText)
        {
            string[] lines = fileText.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#"))
                    continue;

                OpenFootballMatch? parsedMatch = ParseLine(line);

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

        private OpenFootballMatch? ParseLine(string line)
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
                    AwayGoals = int.Parse(newerFormat.Groups[4].Value)
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
                    AwayGoals = int.Parse(olderFormat.Groups[3].Value)
                };
            }

            return null;
        }

        private List<MatchRecord> ConvertToMatchRecords()
        {
            List<MatchRecord> records = new();

            foreach (OpenFootballMatch match in matches)
            {
                records.Add(new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = GetTeamId(match.HomeTeam),
                    AwayTeamId = GetTeamId(match.AwayTeam),
                    HomeGoals = match.HomeGoals,
                    AwayGoals = match.AwayGoals
                });
            }

            return records;
        }

        private void PrintLeagueTable(LeagueTable table)
        {
            List<LeagueTable.Entry> sortedTable = table.Sorted();

            Debug.Log("Combined league table from loaded seasons:");

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
    }
}