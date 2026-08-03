using System;
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

        [Header("Team Select UI")]
        [SerializeField] private GameObject teamSelectPanel;
        [SerializeField] private TMP_Text teamSelectNameText;
        [SerializeField] private Button previousTeamButton;
        [SerializeField] private Button nextTeamButton;
        [SerializeField] private Button confirmTeamButton;

        [Header("Season Hub UI")]
        [SerializeField] private GameObject seasonHubPanel;
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text nextFixtureText;
        [SerializeField] private TMP_Text tacticText;
        [SerializeField] private TMP_Text leagueTableText;
        [SerializeField] private Button playNextMatchButton;
        [SerializeField] private Button simulateSeasonButton;
        [SerializeField] private Button viewSquadButton;
        [SerializeField] private Button inspectPlayerButton;

        [Header("Player Inspect UI")]
        [SerializeField] private GameObject playerInspectPanel;
        [SerializeField] private TMP_Text playerInspectText;
        [SerializeField] private Button inspectPreviousButton;
        [SerializeField] private Button inspectNextButton;
        [SerializeField] private Button inspectBackButton;

        // Runtime-generated clickable list (see SquadListView) reused for squad browsing
        // and both substitution pickers, so none of them need Prev/Next cycling to reach
        // a specific player - only one of these three purposes is ever active at a time.
        [Header("Player List (squad browse / sub picker)")]
        [SerializeField] private GameObject playerListPanel;
        [SerializeField] private TMP_Text playerListTitleText;
        [SerializeField] private SquadListView squadListView;
        [SerializeField] private Button playerListBackButton;

        [Header("Substitutions")]
        [SerializeField] private Button makeSubsButton; // Season Hub, pre-match team sheet
        [SerializeField] private TMP_Text subsStatusText;
        [SerializeField] private Button makeSubButton; // Matchday panel, in-match

        // Tactic is chosen between matches on the Hub, not mid-match - no manager is
        // rethinking their approach to the next opponent while the current game is live.
        [SerializeField] private Button attackingButton;
        [SerializeField] private Button balancedButton;
        [SerializeField] private Button defensiveButton;

        [Header("Matchday UI")]
        [SerializeField] private GameObject matchdayPanel;
        [SerializeField] private TMP_Text fixtureTitleText;
        [SerializeField] private TMP_Text clockText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text eventFeedText;
        [SerializeField] private TMP_Text matchStatsText;
        [SerializeField] private Button skipToResultsButton;
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

        private List<OpenFootballMatch> allSeasonFixtures = new();
        private List<OpenFootballMatch> managedTeamFixtures = new();
        private int currentFixtureIndex;
        private ManagerTactic selectedTactic = ManagerTactic.Balanced;

        // Populated from the season file itself, so the list is always exactly the
        // clubs actually playing this season - no separately maintained team list.
        private List<string> availableTeamNames = new();
        private int selectedTeamIndex;

        // Tracks which matchdays have already had their non-managed-team fixtures
        // simulated, so a round's other 9 matches are only ever resolved once.
        private readonly HashSet<int> simulatedMatchdays = new();

        private OpenFootballMatch currentFixture;
        private ManagerTactic tacticUsedForCurrentMatch;
        private bool skipToResultsRequested;

        // Set inside SimulateFixture and reused if an in-match substitution requires
        // resimulating the remainder of the match with the same underlying prediction.
        private float lastExpectedHomeGoals;
        private float lastExpectedAwayGoals;

        // Starting XI followed by Bench, built fresh each time the inspect screen opens.
        private List<PlayerAgent> inspectSquadPlayers = new();
        private int inspectPlayerIndex;

        // --- Substitutions (pre-match team sheet + in-match interjection share one
        // picker flow and one 5-per-match cap, matching real football's sub limit) ---
        private const int MaxSubsPerMatch = 5;

        private PlayerAgent pendingSubOffPlayer;
        private bool subFlowIsInMatch;
        private bool pendingSubApplied;
        private int subsUsedThisMatch;

        // Raised by the in-match "Make Sub" button; the replay coroutine only acts on it
        // at a minute boundary, and resumes via subSelectionConfirmed once a pick is made
        // (or cancelled) so the coroutine's own WaitUntil can proceed.
        private bool inMatchSubRequested;
        private bool subSelectionConfirmed;

        private void Start()
        {
            if (playNextMatchButton != null) playNextMatchButton.onClick.AddListener(OnPlayNextMatchClicked);
            if (simulateSeasonButton != null) simulateSeasonButton.onClick.AddListener(OnSimulateSeasonClicked);
            if (viewSquadButton != null) viewSquadButton.onClick.AddListener(OnViewSquadClicked);
            if (inspectPlayerButton != null) inspectPlayerButton.onClick.AddListener(OnInspectPlayerClicked);
            if (inspectPreviousButton != null) inspectPreviousButton.onClick.AddListener(OnInspectPreviousClicked);
            if (inspectNextButton != null) inspectNextButton.onClick.AddListener(OnInspectNextClicked);
            if (inspectBackButton != null) inspectBackButton.onClick.AddListener(OnInspectBackClicked);
            if (skipToResultsButton != null) skipToResultsButton.onClick.AddListener(OnSkipToResultsClicked);
            if (fullTimeContinueButton != null) fullTimeContinueButton.onClick.AddListener(OnFullTimeContinueClicked);
            if (attackingButton != null) attackingButton.onClick.AddListener(SelectAttackingTactic);
            if (balancedButton != null) balancedButton.onClick.AddListener(SelectBalancedTactic);
            if (defensiveButton != null) defensiveButton.onClick.AddListener(SelectDefensiveTactic);
            if (previousTeamButton != null) previousTeamButton.onClick.AddListener(OnPreviousTeamClicked);
            if (nextTeamButton != null) nextTeamButton.onClick.AddListener(OnNextTeamClicked);
            if (confirmTeamButton != null) confirmTeamButton.onClick.AddListener(OnConfirmTeamClicked);
            if (playerListBackButton != null) playerListBackButton.onClick.AddListener(OnPlayerListBackClicked);
            if (makeSubsButton != null) makeSubsButton.onClick.AddListener(OnMakeSubsClicked);
            if (makeSubButton != null) makeSubButton.onClick.AddListener(OnMakeSubDuringMatchClicked);

            if (seasonFile == null)
            {
                Debug.LogError("ManagerPrototypeController: no season file assigned.");
                return;
            }

            allSeasonFixtures = OpenFootballTextParser.ParseSeasonFile(seasonFile.text, seasonFile.name);
            availableTeamNames = BuildAvailableTeamNames();

            int defaultIndex = availableTeamNames.IndexOf(managedTeamName);
            selectedTeamIndex = defaultIndex >= 0 ? defaultIndex : 0;

            TrainStatisticalModel();

            ShowTeamSelect();
        }

        // --- Team Select ---

        private List<string> BuildAvailableTeamNames()
        {
            SortedSet<string> names = new();

            foreach (OpenFootballMatch match in allSeasonFixtures)
            {
                names.Add(match.HomeTeam);
                names.Add(match.AwayTeam);
            }

            return new List<string>(names);
        }

        private void ShowTeamSelect()
        {
            if (teamSelectPanel != null) teamSelectPanel.SetActive(true);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshTeamSelectUI();
        }

        private void RefreshTeamSelectUI()
        {
            if (teamSelectNameText == null || availableTeamNames.Count == 0)
            {
                return;
            }

            teamSelectNameText.text = $"Choose your club:\n{availableTeamNames[selectedTeamIndex]}";
        }

        public void OnPreviousTeamClicked()
        {
            if (availableTeamNames.Count == 0)
            {
                return;
            }

            selectedTeamIndex = (selectedTeamIndex - 1 + availableTeamNames.Count) % availableTeamNames.Count;
            RefreshTeamSelectUI();
        }

        public void OnNextTeamClicked()
        {
            if (availableTeamNames.Count == 0)
            {
                return;
            }

            selectedTeamIndex = (selectedTeamIndex + 1) % availableTeamNames.Count;
            RefreshTeamSelectUI();
        }

        public void OnConfirmTeamClicked()
        {
            if (availableTeamNames.Count > 0)
            {
                managedTeamName = availableTeamNames[selectedTeamIndex];
            }

            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            if (managedTeamFixtures.Count == 0)
            {
                Debug.LogWarning($"ManagerPrototypeController: no fixtures found for '{managedTeamName}' in {seasonFile.name}.");
            }

            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

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

            bool subsAvailable = subsUsedThisMatch < MaxSubsPerMatch;

            if (makeSubsButton != null)
            {
                makeSubsButton.interactable = subsAvailable;
            }

            if (subsStatusText != null)
            {
                subsStatusText.text = $"Subs: {subsUsedThisMatch}/{MaxSubsPerMatch} used";
            }
        }

        // --- Squad browsing / player list panel (click a row, jump straight there -
        // no Prev/Next cycling needed to reach a specific player) ---

        public void OnViewSquadClicked()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            List<PlayerAgent> allPlayers = new List<PlayerAgent>(team.StartingEleven);
            allPlayers.AddRange(team.Bench);

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);

            ShowPlayerListPanel($"{managedTeamName} Squad", allPlayers, OnSquadBrowseRowClicked);
        }

        private void OnSquadBrowseRowClicked(PlayerAgent player)
        {
            ClosePlayerListPanel();
            OpenPlayerInspect(player);
        }

        private void ShowPlayerListPanel(string title, List<PlayerAgent> players, Action<PlayerAgent> onRowClicked)
        {
            if (playerListPanel != null) playerListPanel.SetActive(true);
            if (playerListTitleText != null) playerListTitleText.text = title;

            if (squadListView != null)
            {
                squadListView.Populate(players, DescribePlayer, onRowClicked);
            }
        }

        private void ClosePlayerListPanel()
        {
            if (playerListPanel != null) playerListPanel.SetActive(false);
            if (squadListView != null) squadListView.Clear();
        }

        public void OnPlayerListBackClicked()
        {
            ClosePlayerListPanel();

            if (subFlowIsInMatch)
            {
                // Cancelling an in-match sub attempt: resume the replay exactly as it
                // was, no swap applied (pendingSubApplied stays false).
                subFlowIsInMatch = false;
                pendingSubApplied = false;
                if (matchdayPanel != null) matchdayPanel.SetActive(true);
                subSelectionConfirmed = true;
            }
            else
            {
                ShowSeasonHub();
            }
        }

        // --- Substitutions: pre-match (Season Hub) and in-match share this same
        // off-then-on picker flow and the same 5-per-match cap. Both ultimately mutate
        // the same persistent AgentTeam instance for managedTeamName, so an in-match sub
        // also carries forward into future matches unless changed again. ---

        public void OnMakeSubsClicked()
        {
            if (subsUsedThisMatch >= MaxSubsPerMatch)
            {
                return;
            }

            subFlowIsInMatch = false;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);

            ShowPlayerListPanel("Substitute OFF (pick a starter)", new List<PlayerAgent>(team.StartingEleven), OnSubOffPicked);
        }

        public void OnMakeSubDuringMatchClicked()
        {
            if (subsUsedThisMatch >= MaxSubsPerMatch || inMatchSubRequested)
            {
                return;
            }

            // Only raises the flag - the replay coroutine opens the picker itself at the
            // next minute boundary, where it already has the match's team references.
            inMatchSubRequested = true;
        }

        private void OnSubOffPicked(PlayerAgent playerOff)
        {
            pendingSubOffPlayer = playerOff;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            ShowPlayerListPanel("Substitute ON (pick from bench)", new List<PlayerAgent>(team.Bench), OnSubOnPicked);
        }

        private void OnSubOnPicked(PlayerAgent playerOn)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            pendingSubApplied = team.SubstitutePlayer(pendingSubOffPlayer, playerOn);

            if (pendingSubApplied)
            {
                subsUsedThisMatch++;
            }

            ClosePlayerListPanel();

            if (subFlowIsInMatch)
            {
                subFlowIsInMatch = false;

                if (matchdayPanel != null) matchdayPanel.SetActive(true);
                if (makeSubButton != null) makeSubButton.interactable = subsUsedThisMatch < MaxSubsPerMatch;

                subSelectionConfirmed = true;
            }
            else
            {
                ShowSeasonHub();
            }
        }

        private string DescribePlayer(PlayerAgent player)
        {
            return $"{player.PrimaryPosition,-3} {player.Name,-20} OVR {GetDisplayRating(player.GetOverallRating())}";
        }

        // Cosmetic only: stretches the true weighted rating away from the midpoint so
        // strong squads read as clearly elite and weak squads read as clearly weak,
        // closer to how FIFA-style ratings feel. The underlying attributes, the true
        // GetOverallRating() value, and match simulation are all completely unaffected
        // by this - only the number printed here changes.
        private static int GetDisplayRating(float trueRating)
        {
            const float midpoint = 50f;
            const float stretch = 1.15f;

            float displayed = midpoint + (trueRating - midpoint) * stretch;

            return Mathf.RoundToInt(Mathf.Clamp(displayed, 1f, 99f));
        }

        // --- Player Inspect (Prev/Next once inside; entry point can also jump straight
        // to a specific player, e.g. from the squad browse list, instead of always
        // starting at index 0) ---

        public void OnInspectPlayerClicked()
        {
            OpenPlayerInspect(null);
        }

        private void OpenPlayerInspect(PlayerAgent preselected)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            inspectSquadPlayers = new List<PlayerAgent>(team.StartingEleven);
            inspectSquadPlayers.AddRange(team.Bench);

            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            int preselectedIndex = preselected != null ? inspectSquadPlayers.IndexOf(preselected) : -1;
            inspectPlayerIndex = preselectedIndex >= 0 ? preselectedIndex : 0;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (playerInspectPanel != null) playerInspectPanel.SetActive(true);

            RefreshPlayerInspectUI();
        }

        public void OnInspectPreviousClicked()
        {
            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            inspectPlayerIndex = (inspectPlayerIndex - 1 + inspectSquadPlayers.Count) % inspectSquadPlayers.Count;
            RefreshPlayerInspectUI();
        }

        public void OnInspectNextClicked()
        {
            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            inspectPlayerIndex = (inspectPlayerIndex + 1) % inspectSquadPlayers.Count;
            RefreshPlayerInspectUI();
        }

        public void OnInspectBackClicked()
        {
            if (playerInspectPanel != null) playerInspectPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void RefreshPlayerInspectUI()
        {
            if (playerInspectText == null || inspectSquadPlayers.Count == 0)
            {
                return;
            }

            PlayerAgent player = inspectSquadPlayers[inspectPlayerIndex];
            string squadStatus = player.IsStartingEleven ? "Starting XI" : "Bench";

            playerInspectText.text =
                $"Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})\n" +
                $"OVR {GetDisplayRating(player.GetOverallRating())}\n\n" +
                player.ToString();
        }

        private string DescribeFixture(OpenFootballMatch fixture)
        {
            bool managedIsHome = fixture.HomeTeam == managedTeamName;
            string opponent = managedIsHome ? fixture.AwayTeam : fixture.HomeTeam;
            return managedIsHome ? $"vs {opponent} (H)" : $"vs {opponent} (A)";
        }

        // A full division table: playing your own fixture also resolves every other
        // match in that matchday (see SimulateOtherFixturesInMatchday), so every club
        // stays in sync by games played rather than only showing teams you've faced.
        private string BuildSeasonTableSummary()
        {
            List<LeagueTable.Entry> sortedTable = playableTable.Sorted();

            if (sortedTable.Count == 0)
            {
                return "Premier League Table: no matches played yet.";
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Premier League Table:");

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
            tacticUsedForCurrentMatch = selectedTactic;

            AgentMatchSimulator.AgentMatchResult result = SimulateFixture(currentFixture);

            lastSimulatedResult = result;

            SimulateOtherFixturesInMatchday(currentFixture.Matchday);

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

                ApplyFixtureResult(fixture, SimulateFixture(fixture));
                SimulateOtherFixturesInMatchday(fixture.Matchday);

                currentFixtureIndex++;
            }

            RefreshHubUI();
        }

        // Simulates and applies every other fixture sharing the given matchday (i.e.
        // every match the managed club isn't part of), so the table reflects a full
        // division rather than just the managed club's own results. Guarded so each
        // matchday's other fixtures are only ever resolved once, however you reach it.
        private void SimulateOtherFixturesInMatchday(int matchday)
        {
            if (simulatedMatchdays.Contains(matchday))
            {
                return;
            }

            simulatedMatchdays.Add(matchday);

            foreach (OpenFootballMatch fixture in allSeasonFixtures)
            {
                if (fixture.Matchday != matchday)
                {
                    continue;
                }

                if (fixture.HomeTeam == managedTeamName || fixture.AwayTeam == managedTeamName)
                {
                    continue;
                }

                ApplyFixtureResult(fixture, SimulateFixture(fixture));
            }
        }

        private void ApplyFixtureResult(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            MatchRecord record = new MatchRecord
            {
                Matchday = fixture.Matchday,
                HomeTeamId = teamRegistry.GetTeamId(fixture.HomeTeam),
                AwayTeamId = teamRegistry.GetTeamId(fixture.AwayTeam),
                HomeGoals = result.HomeGoals,
                AwayGoals = result.AwayGoals
            };

            playableTable.Apply(record);
        }

        // Applies the tactic modifier only when the managed club is actually playing
        // in this fixture - other clubs' matches against each other use the plain
        // predicted expected goals with no modifier.
        private AgentMatchSimulator.AgentMatchResult SimulateFixture(OpenFootballMatch fixture)
        {
            AgentTeam homeTeam = GetOrCreateAgentTeam(fixture.HomeTeam);
            AgentTeam awayTeam = GetOrCreateAgentTeam(fixture.AwayTeam);

            StatisticalModel.ExpectedGoalsPrediction prediction = statisticalModel.PredictExpectedGoals(fixture);

            float expectedHomeGoals = prediction.ExpectedHomeGoals;
            float expectedAwayGoals = prediction.ExpectedAwayGoals;

            if (fixture.HomeTeam == managedTeamName)
            {
                ManagerTacticModifier.Apply(selectedTactic, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ManagerTacticModifier.Apply(selectedTactic, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;

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
            inMatchSubRequested = false;
            subFlowIsInMatch = false;
            pendingSubApplied = false;

            if (makeSubButton != null) makeSubButton.interactable = subsUsedThisMatch < MaxSubsPerMatch;

            if (eventFeedText != null) eventFeedText.text = "";
            if (matchStatsText != null) matchStatsText.text = "";
            if (scoreText != null) scoreText.text = $"{currentFixture.HomeTeam} 0 - 0 {currentFixture.AwayTeam}";
            if (clockText != null) clockText.text = "0'";

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            AgentTeam homeTeamAgent = GetOrCreateAgentTeam(currentFixture.HomeTeam);
            AgentTeam awayTeamAgent = GetOrCreateAgentTeam(currentFixture.AwayTeam);
            AgentTeam managedAgentTeam = currentFixture.HomeTeam == managedTeamName ? homeTeamAgent : awayTeamAgent;

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

                // Skipping to results ignores any pending sub request rather than
                // popping a picker mid-fast-forward - the request is simply dropped.
                if (inMatchSubRequested && !skipToResultsRequested)
                {
                    subSelectionConfirmed = false;
                    pendingSubApplied = false;
                    subFlowIsInMatch = true;

                    if (matchdayPanel != null) matchdayPanel.SetActive(false);

                    ShowPlayerListPanel("Substitute OFF (pick a starter)", new List<PlayerAgent>(managedAgentTeam.StartingEleven), OnSubOffPicked);

                    yield return new WaitUntil(() => subSelectionConfirmed);

                    inMatchSubRequested = false;

                    if (pendingSubApplied)
                    {
                        AgentMatchSimulator.AgentMatchResult tail = matchSimulator.SimulateFromMinute(
                            homeTeamAgent,
                            awayTeamAgent,
                            lastExpectedHomeGoals,
                            lastExpectedAwayGoals,
                            minute + 1,
                            homeGoals,
                            awayGoals);

                        result.Events.RemoveAll(e => e.Minute > minute);
                        result.Events.AddRange(tail.Events);
                        result.HomeGoals = tail.HomeGoals;
                        result.AwayGoals = tail.AwayGoals;
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
            ApplyFixtureResult(currentFixture, lastSimulatedResult);

            currentFixtureIndex++;
            subsUsedThisMatch = 0;

            ShowSeasonHub();
        }

        private AgentMatchSimulator.AgentMatchResult lastSimulatedResult;

        private AgentTeam GetOrCreateAgentTeam(string teamName)
        {
            if (squadsByTeamName.TryGetValue(teamName, out AgentTeam existingTeam))
            {
                return existingTeam;
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);

            AgentTeam newTeam = squadGenerator.GenerateSquad(teamName, strength.AttackStrength, strength.DefenceStrength);

            squadsByTeamName[teamName] = newTeam;

            return newTeam;
        }
    }
}
