using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Data
{
    public class OpenFootballLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset[] seasonFiles;

        private readonly List<OpenFootballMatch> matches = new();

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

            TeamRegistry teamRegistry = new TeamRegistry();
            EvidenceExporter evidenceExporter = new EvidenceExporter(teamRegistry);
            ResearchEvaluationRunner evaluationRunner = new ResearchEvaluationRunner(teamRegistry, evidenceExporter);

            evaluationRunner.Run(trainingMatches, evaluationMatches);
        }

        private void LoadAllSeasonFiles()
        {
            matches.Clear();

            foreach (TextAsset file in seasonFiles)
            {
                if (file == null)
                {
                    Debug.LogWarning("An assigned season file slot is empty.");
                    continue;
                }

                List<OpenFootballMatch> loadedMatches = OpenFootballTextParser.ParseSeasonFile(file.text, file.name);

                matches.AddRange(loadedMatches);

                Debug.Log($"Loaded {loadedMatches.Count} matches from {file.name}.");
            }

            Debug.Log($"Loaded {matches.Count} total matches from {seasonFiles.Length} season files.");
        }
    }

    public struct OpenFootballMatch
    {
        public string HomeTeam;
        public string AwayTeam;
        public int HomeGoals;
        public int AwayGoals;
        public string Season;
        public int Matchday;
    }
}
