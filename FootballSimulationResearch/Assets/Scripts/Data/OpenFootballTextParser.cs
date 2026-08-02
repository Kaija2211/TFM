using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Data
{
    public static class OpenFootballTextParser
    {
        public static List<OpenFootballMatch> ParseSeasonFile(string fileText, string seasonName)
        {
            List<OpenFootballMatch> matches = new();

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

            return matches;
        }

        private static OpenFootballMatch? ParseLine(string line, string seasonName)
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

        private static string NormaliseTeamName(string teamName)
        {
            teamName = teamName.Trim();

            // Merge "Manchester City" and "Manchester City FC"
            if (teamName.EndsWith(" FC"))
            {
                teamName = teamName.Substring(0, teamName.Length - 3);
            }

            return teamName;
        }
    }
}
