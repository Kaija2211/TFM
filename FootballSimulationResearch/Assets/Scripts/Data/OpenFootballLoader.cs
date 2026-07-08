using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Data
{
    public class OpenFootballLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset seasonFile;

        private readonly List<OpenFootballMatch> matches = new();

        private void Start()
        {
            if (seasonFile == null)
            {
                Debug.LogError("No OpenFootball season file assigned.");
                return;
            }

            LoadMatches(seasonFile.text);

            Debug.Log($"Loaded {matches.Count} matches from {seasonFile.name}.");

            foreach (OpenFootballMatch match in matches.GetRange(0, Mathf.Min(5, matches.Count)))
            {
                Debug.Log($"{match.HomeTeam} {match.HomeGoals}-{match.AwayGoals} {match.AwayTeam}");
            }
        }

        private void LoadMatches(string fileText)
        {
            matches.Clear();

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
else if (line.Contains(" v "))
{
    Debug.LogWarning($"Could not parse match line: {line}");
}
            }
        }

        private OpenFootballMatch? ParseLine(string line)
        {
            // Removes optional kickoff time at start, e.g. "20:00 "
            line = Regex.Replace(line, @"^\d{1,2}:\d{2}\s+", "");

            // Example:
            // Manchester United FC v Fulham FC 1-0 (0-0)
            Match match = Regex.Match(
    line,
    @"^(.*?)\s+v\s+(.*?)\s+(\d+)-(\d+)(?:\s+\(\d+-\d+\))?.*$"
);

            if (!match.Success)
                return null;

            return new OpenFootballMatch
            {
                HomeTeam = match.Groups[1].Value.Trim(),
                AwayTeam = match.Groups[2].Value.Trim(),
                HomeGoals = int.Parse(match.Groups[3].Value),
                AwayGoals = int.Parse(match.Groups[4].Value)
            };
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