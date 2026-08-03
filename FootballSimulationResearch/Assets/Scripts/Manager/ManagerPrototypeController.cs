using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Data;
using Sim;

namespace Manager
{
    // Playable text-first manager wrapper around the existing ABM systems.
    // Fully separate from OpenFootballLoader/ResearchEvaluationRunner: it loads its
    // own copy of a season file and keeps its own TeamRegistry/LeagueTable, so it
    // cannot affect the research evaluation flow or the Statistical-vs-ABM metrics.
    public class ManagerPrototypeController : MonoBehaviour
    {
        [Header("Season Data")]
        [SerializeField] private TextAsset seasonFile;
        [SerializeField] private TextAsset[] trainingSeasonFiles;
        [SerializeField] private string managedTeamName = "Liverpool";
        [SerializeField] private float matchReplayDurationSeconds = 45f;
        [SerializeField] private int maxVisibleEventLines = 12;

        [Header("Season Hub UI")]
        [SerializeField] private GameObject seasonHubPanel;
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text nextFixtureText;
        [SerializeField] private TMP_Text tacticText;
        [SerializeField] private TMP_Text leagueTableText;
        [SerializeField] private Button playNextMatchButton;
        [SerializeField] private Button simulateSeasonButton;

        [Header("Matchday UI")]
        [SerializeField] private GameObject matchdayPanel;
        [SerializeField] private TMP_Text fixtureTitleText;
        [SerializeField] private TMP_Text clockText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text eventFeedText;
        [SerializeField] private TMP_Text matchStatsText;
        [SerializeField] private Button skipToResultsButton;
        [SerializeField] private Button attackingButton;
        [SerializeField] private Button balancedButton;
        [SerializeField] private Button defensiveButton;
        [SerializeField] private Button fullTimeContinueButton;

        private readonly TeamRegistry teamRegistry = new();
        private readonly LeagueTable playableTable = new();
        private readonly Dictionary<string, AgentTeam> squadsByTeamName = new();
        private readonly AgentSquadGenerator squadGenerator = new();
        private readonly AgentMatchSimulator matchSimulator = new();

        // Own StatisticalModel instance, trained on trainingSeasonFiles only. Completely
        // separate from ResearchEvaluationRunner's own StatisticalModel instance, so
        // nothing here can affect the research evaluation flow or its metrics.
        private readonly StatisticalModel statisticalModel = new();

        private List<OpenFootballMatch> managedTeamFixtures = new();
        private int currentFixtureIndex;
        private ManagerTactic selectedTactic = ManagerTactic.Balanced;

        private OpenFootballMatch currentFixture;
        private bool currentFixtureManagedIsHome;
        private ManagerTactic tacticUsedForCurrentMatch;
        private bool skipToResultsRequested;

        private void Start()
        {
            if (playNextMatchButton != null) playNextMatchButton.onClick.AddListener(OnPlayNextMatchClicked);
            if (simulateSeasonButton != null) simulateSeasonButton.onClick.AddListener(OnSimulateSeasonClicked);
            if (skipToResultsButton != null) skipToResultsButton.onClick.AddListener(OnSkipToResultsClicked);
            if (fullTimeContinueButton != null) fullTimeContinueButton.onClick.AddListener(OnFullTimeContinueClicked);
            if (attackingButton != null) attackingButton.onClick.AddListener(SelectAttackingTactic);
            if (balancedButton != null) balancedButton.onClick.AddListener(SelectBalancedTactic);
            if (defensiveButton != null) defensiveButton.onClick.AddListener(SelectDefensiveTactic);

            if (seasonFile == null)
            {
                Debug.LogError("ManagerPrototypeController: no season file assigned.");
                return;
            }

            List<OpenFootballMatch> seasonMatches = OpenFootballTextParser.ParseSeasonFile(seasonFile.text, seasonFile.name);

            managedTeamFixtures = seasonMatches.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            if (managedTeamFixtures.Count == 0)
            {
                Debug.LogWarning($"ManagerPrototypeController: no fixtures found for '{managedTeamName}' in {seasonFile.name}.");
            }

            TrainStatisticalModel();

            ShowSeasonHub();
        }

        private void TrainStatisticalModel()
        {
            if (trainingSeasonFiles == null || trainingSeasonFiles.Length == 0)
            {
                Debug.LogError("ManagerPrototypeController: no training season files assigned — expected goals predictions will be degenerate until this is fixed.");
                return;
            }

            List<OpenFootballMatch> trainingMatches = new();

            foreach (TextAsset file in trainingSeasonFiles)
            {
                if (file == null)
                {
                    continue;
                }

                trainingMatches.AddRange(OpenFootballTextParser.ParseSeasonFile(file.text, file.name));
            }

            if (trainingMatches.Count == 0)
            {
                Debug.LogError("ManagerPrototypeController: training season files produced no matches — expected goals predictions will be degenerate until this is fixed.");
                return;
            }

            statisticalModel.Train(trainingMatches);
        }

        // --- Tactic selection (Balanced default: no modifier applied) ---

        public void SelectAttackingTactic() => SetTactic(ManagerTactic.Attacking);
        public void SelectBalancedTactic() => SetTactic(ManagerTactic.Balanced);
        public void SelectDefensiveTactic() => SetTactic(ManagerTactic.Defensive);

        private void SetTactic(ManagerTactic tactic)
        {
            selectedTactic = tactic;

            if (tacticText != null)
            {
                tacticText.text = $"Tactic: {selectedTactic}";
            }
        }

        // --- Season Hub ---

        private void ShowSeasonHub()
        {
            if (seasonHubPanel != null) seasonHubPanel.SetActive(true);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshHubUI();
        }

        private void RefreshHubUI()
        {
            if (headerText != null)
            {
                headerText.text = $"Managing: {managedTeamName}";
            }

            if (tacticText != null)
            {
                tacticText.text = $"Tactic: {selectedTactic}";
            }

            bool hasNextFixture = currentFixtureIndex < managedTeamFixtures.Count;

            if (nextFixtureText != null)
            {
                nextFixtureText.text = hasNextFixture
                    ? $"Next Fixture: {DescribeFixture(managedTeamFixtures[currentFixtureIndex])}"
                    : "Season complete.";
            }

            if (playNextMatchButton != null)
            {
                playNextMatchButton.interactable = hasNextFixture;
            }

            if (simulateSeasonButton != null)
            {
                simulateSeasonButton.interactable = hasNextFixture;
            }

            if (leagueTableText != null)
            {
                leagueTableText.text = BuildSeasonTableSummary();
            }
        }

        private string DescribeFixture(OpenFootballMatch fixture)
        {
            bool managedIsHome = fixture.HomeTeam == managedTeamName;
            string opponent = managedIsHome ? fixture.AwayTeam : fixture.HomeTeam;
            return managedIsHome ? $"vs {opponent} (H)" : $"vs {opponent} (A)";
        }

        // Only ever contains results for matches the managed club has actually played,
        // since Manager Mode doesn't simulate fixtures between other clubs.
        private string BuildSeasonTableSummary()
        {
            List<LeagueTable.Entry> sortedTable = playableTable.Sorted();

            if (sortedTable.Count == 0)
            {
                return "Your Season So Far: no matches played yet.";
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Your Season So Far:");

            for (int i = 0; i < sortedTable.Count; i++)
            {
                LeagueTable.Entry entry = sortedTable[i];
                string teamName = teamRegistry.GetTeamName(entry.TeamId);

                summary.AppendLine(
                    $"{i + 1}. {teamName} " +
                    $"Pts:{entry.Points} P:{entry.Played} " +
                    $"W:{entry.Wins} D:{entry.Draws} L:{entry.Losses} " +
                    $"GF:{entry.GoalsFor} GA:{entry.GoalsAgainst}"
                );
            }

            return summary.ToString();
        }

        // --- Matchday ---

        public void OnPlayNextMatchClicked()
        {
            if (currentFixtureIndex >= managedTeamFixtures.Count)
            {
                return;
            }

            currentFixture = managedTeamFixtures[currentFixtureIndex];
            currentFixtureManagedIsHome = currentFixture.HomeTeam == managedTeamName;
            tacticUsedForCurrentMatch = selectedTactic;

            AgentMatchSimulator.AgentMatchResult result = SimulateFixture(currentFixture, currentFixtureManagedIsHome);

            lastSimulatedResult = result;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(true);

            if (fixtureTitleText != null)
            {
                fixtureTitleText.text = $"{currentFixture.HomeTeam} vs {currentFixture.AwayTeam}";
            }

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = false;
            }

            StartCoroutine(ReplayMatchCoroutine(result));
        }

        // Instantly plays out every remaining fixture with no matchday replay, applying
        // each result straight to the season table. Uses whichever tactic is currently
        // selected for every remaining match, since there's no per-match UI step here.
        public void OnSimulateSeasonClicked()
        {
            while (currentFixtureIndex < managedTeamFixtures.Count)
            {
                OpenFootballMatch fixture = managedTeamFixtures[currentFixtureIndex];
                bool managedIsHome = fixture.HomeTeam == managedTeamName;

                AgentMatchSimulator.AgentMatchResult result = SimulateFixture(fixture, managedIsHome);

                MatchRecord record = new MatchRecord
                {
                    Matchday = 0,
                    HomeTeamId = teamRegistry.GetTeamId(fixture.HomeTeam),
                    AwayTeamId = teamRegistry.GetTeamId(fixture.AwayTeam),
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals
                };

                playableTable.Apply(record);

                currentFixtureIndex++;
            }

            RefreshHubUI();
        }

        private AgentMatchSimulator.AgentMatchResult SimulateFixture(OpenFootballMatch fixture, bool managedIsHome)
        {
            AgentTeam homeTeam = GetOrCreateAgentTeam(fixture.HomeTeam);
            AgentTeam awayTeam = GetOrCreateAgentTeam(fixture.AwayTeam);

            StatisticalModel.ExpectedGoalsPrediction prediction = statisticalModel.PredictExpectedGoals(fixture);

            float expectedHomeGoals = prediction.ExpectedHomeGoals;
            float expectedAwayGoals = prediction.ExpectedAwayGoals;

            if (managedIsHome)
            {
                ManagerTacticModifier.Apply(selectedTactic, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else
            {
                ManagerTacticModifier.Apply(selectedTactic, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            return matchSimulator.SimulateMatch(homeTeam, awayTeam, expectedHomeGoals, expectedAwayGoals);
        }

        // Lets the running replay coroutine finish out its remaining minutes without
        // waiting between them, so it lands on the same full-time state almost
        // instantly instead of skipping/discarding any of the match.
        public void OnSkipToResultsClicked()
        {
            skipToResultsRequested = true;
        }

        // Simulates the full match instantly, then replays the pre-computed events
        // against an accelerated clock so it reads as if live. Tactic buttons stay
        // interactable during replay, but only affect the *next* match (scaffolded
        // mid-match control, per the v1 scope) — this match is already fully resolved.
        private IEnumerator ReplayMatchCoroutine(AgentMatchSimulator.AgentMatchResult result)
        {
            Queue<string> recentEventLines = new();

            skipToResultsRequested = false;

            if (eventFeedText != null) eventFeedText.text = "";
            if (matchStatsText != null) matchStatsText.text = "";
            if (scoreText != null) scoreText.text = $"{currentFixture.HomeTeam} 0 - 0 {currentFixture.AwayTeam}";
            if (clockText != null) clockText.text = "0'";

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            int homeGoals = 0;
            int awayGoals = 0;
            int eventIndex = 0;

            for (int minute = 1; minute <= 90; minute++)
            {
                if (!skipToResultsRequested)
                {
                    yield return new WaitForSeconds(secondsPerMinute);
                }

                if (clockText != null) clockText.text = $"{minute}'";

                while (eventIndex < result.Events.Count && result.Events[eventIndex].Minute == minute)
                {
                    AgentMatchSimulator.AgentMatchEvent matchEvent = result.Events[eventIndex];
                    eventIndex++;

                    if (matchEvent.IsGoal)
                    {
                        if (matchEvent.HomeTeamScored) homeGoals++; else awayGoals++;

                        if (scoreText != null)
                        {
                            scoreText.text = $"{currentFixture.HomeTeam} {homeGoals} - {awayGoals} {currentFixture.AwayTeam}";
                        }
                    }

                    if (eventFeedText != null)
                    {
                        recentEventLines.Enqueue($"{minute}' {matchEvent.Description}");

                        while (recentEventLines.Count > maxVisibleEventLines)
                        {
                            recentEventLines.Dequeue();
                        }

                        eventFeedText.text = string.Join("\n", recentEventLines);
                    }
                }
            }

            if (matchStatsText != null)
            {
                int totalShots = 0;

                foreach (AgentMatchSimulator.AgentMatchEvent matchEvent in result.Events)
                {
                    if (matchEvent.IsShot)
                    {
                        totalShots++;
                    }
                }

                matchStatsText.text =
                    "Full-Time Stats\n" +
                    $"{currentFixture.HomeTeam} {result.HomeGoals} - {result.AwayGoals} {currentFixture.AwayTeam}\n" +
                    $"Tactic Used: {tacticUsedForCurrentMatch}\n" +
                    $"Total Events: {result.Events.Count}\n" +
                    $"Shots: {totalShots}\n" +
                    $"Goals — {currentFixture.HomeTeam}: {result.HomeGoals}   {currentFixture.AwayTeam}: {result.AwayGoals}";
            }

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = true;
            }
        }

        public void OnFullTimeContinueClicked()
        {
            MatchRecord record = new MatchRecord
            {
                Matchday = 0,
                HomeTeamId = teamRegistry.GetTeamId(currentFixture.HomeTeam),
                AwayTeamId = teamRegistry.GetTeamId(currentFixture.AwayTeam),
                HomeGoals = lastSimulatedResult.HomeGoals,
                AwayGoals = lastSimulatedResult.AwayGoals
            };

            playableTable.Apply(record);

            currentFixtureIndex++;

            ShowSeasonHub();
        }

        private AgentMatchSimulator.AgentMatchResult lastSimulatedResult;

        private AgentTeam GetOrCreateAgentTeam(string teamName)
        {
            if (squadsByTeamName.TryGetValue(teamName, out AgentTeam existingTeam))
            {
                return existingTeam;
            }

            // v1 uses flat, undifferentiated squad strength for every club — every
            // match plays out with the same baseline quality regardless of opponent.
            // A believable next step is deriving attack/defence strength per team the
            // same way ResearchEvaluationRunner does, without sharing its instance.
            AgentTeam newTeam = squadGenerator.GenerateSquad(teamName, 1f, 1f);

            squadsByTeamName[teamName] = newTeam;

            return newTeam;
        }
    }
}
