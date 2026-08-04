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

        [Header("Title Screen")]
        [SerializeField] private GameObject titlePanel;
        [SerializeField] private RectTransform titleContentContainer;

        [Header("Team Select UI")]
        [SerializeField] private GameObject teamSelectPanel;
        [SerializeField] private TMP_InputField managerNameInput;
        [SerializeField] private RectTransform teamGridContainer;
        [SerializeField] private Button teamSelectBackButton;
        [SerializeField] private Button confirmTeamButton; // relabeled "Start Career"

        [Header("Season Hub UI")]
        [SerializeField] private GameObject seasonHubPanel;
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text nextFixtureText;
        [SerializeField] private TMP_Text tacticText;
        [SerializeField] private TMP_Text leagueTableText;
        [SerializeField] private Button playNextMatchButton;
        [SerializeField] private Button simulateSeasonButton;
        [SerializeField] private Button viewSquadButton;
        [SerializeField] private Button transfersButton; // disabled placeholder - no transfer system exists yet
        [SerializeField] private Button exitToTitleButton;

        // Tactic buttons, Make Subs, and the subs counter are NOT declared here - they're
        // the same attackingButton/balancedButton/defensiveButton/makeSubsButton/
        // subsStatusText fields further down, reparented in the Editor from the Hub onto
        // this screen. Same C# references, they just live under a different panel now.
        [Header("Matchday Prep UI")]
        [SerializeField] private GameObject matchdayPrepPanel;
        [SerializeField] private RectTransform matchdayPrepContentContainer;
        [SerializeField] private SquadListView opponentSquadListView;
        [SerializeField] private Button simulateMatchButton;
        [SerializeField] private Button matchdayPrepBackButton;

        [Header("Player Inspect UI")]
        [SerializeField] private GameObject playerInspectPanel;
        [SerializeField] private RectTransform playerInspectContentContainer;
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
        [SerializeField] private Button squadSortButton; // disabled placeholder - no sort logic built yet
        [SerializeField] private Button squadFilterButton; // disabled placeholder - no filter logic built yet

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

        // In-memory only - there is no save system, so this never persists across sessions.
        private string managerName = "Manager";
        private bool titleScreenBuilt;
        private bool teamGridBuilt;
        private List<Button> teamGridButtons = new();

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

        private bool matchdayPrepChromeBuilt;
        private TextMeshProUGUI matchdayPrepTitleLabel;
        private TextMeshProUGUI matchdayPrepSubtitleLabel;

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

        // Distinguishes the two non-in-match callers of the player list panel (Squad
        // browse from the Hub vs. pre-match subs from Matchday Prep), so Back/cancel and
        // sub-completion return to whichever screen actually opened the panel.
        private bool playerListReturnsToMatchdayPrep;
        private bool pendingSubApplied;
        private int subsUsedThisMatch;

        // Raised by the in-match "Make Sub" button; the replay coroutine only acts on it
        // at a minute boundary, and resumes via subSelectionConfirmed once a pick is made
        // (or cancelled) so the coroutine's own WaitUntil can proceed.
        private bool inMatchSubRequested;
        private bool subSelectionConfirmed;

        private void Start()
        {
            if (playNextMatchButton != null) playNextMatchButton.onClick.AddListener(OnNextMatchdayClicked);
            if (simulateMatchButton != null) simulateMatchButton.onClick.AddListener(OnSimulateMatchClicked);
            if (matchdayPrepBackButton != null) matchdayPrepBackButton.onClick.AddListener(OnMatchdayPrepBackClicked);
            if (simulateSeasonButton != null) simulateSeasonButton.onClick.AddListener(OnSimulateSeasonClicked);
            if (viewSquadButton != null) viewSquadButton.onClick.AddListener(OnViewSquadClicked);
            if (inspectPreviousButton != null) inspectPreviousButton.onClick.AddListener(OnInspectPreviousClicked);
            if (inspectNextButton != null) inspectNextButton.onClick.AddListener(OnInspectNextClicked);
            if (inspectBackButton != null) inspectBackButton.onClick.AddListener(OnInspectBackClicked);
            if (skipToResultsButton != null) skipToResultsButton.onClick.AddListener(OnSkipToResultsClicked);
            if (fullTimeContinueButton != null) fullTimeContinueButton.onClick.AddListener(OnFullTimeContinueClicked);
            if (attackingButton != null) attackingButton.onClick.AddListener(SelectAttackingTactic);
            if (balancedButton != null) balancedButton.onClick.AddListener(SelectBalancedTactic);
            if (defensiveButton != null) defensiveButton.onClick.AddListener(SelectDefensiveTactic);
            if (confirmTeamButton != null) confirmTeamButton.onClick.AddListener(OnConfirmTeamClicked);
            if (teamSelectBackButton != null) teamSelectBackButton.onClick.AddListener(OnTeamSelectBackClicked);
            if (playerListBackButton != null) playerListBackButton.onClick.AddListener(OnPlayerListBackClicked);
            if (makeSubsButton != null) makeSubsButton.onClick.AddListener(OnMakeSubsClicked);
            if (makeSubButton != null) makeSubButton.onClick.AddListener(OnMakeSubDuringMatchClicked);
            if (exitToTitleButton != null) exitToTitleButton.onClick.AddListener(OnExitToTitleClicked);

            ApplyManagerUITheme();
            SetTactic(selectedTactic);

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

            ShowTitleScreen();
        }

        // Recolors the already-placed Hub buttons/text to the reskinned palette using the
        // references already wired in the Inspector - no new Editor layout work needed for
        // elements that are already correctly positioned, just their colors/fonts.
        private void ApplyManagerUITheme()
        {
            ManagerUITheme.ApplyPanelBackground(seasonHubPanel);
            ManagerUITheme.ApplyPanelBackground(matchdayPanel);
            ManagerUITheme.ApplyPanelBackground(teamSelectPanel);
            ManagerUITheme.ApplyPanelBackground(playerInspectPanel);
            ManagerUITheme.ApplyPanelBackground(playerListPanel);
            ManagerUITheme.ApplyPanelBackground(matchdayPrepPanel);

            StyleHubActionButton(playNextMatchButton);
            StyleHubActionButton(simulateSeasonButton);
            StyleHubActionButton(viewSquadButton);
            StyleHubActionButton(makeSubsButton);
            StyleHubActionButton(skipToResultsButton);
            StyleHubActionButton(makeSubButton);
            StyleHubActionButton(matchdayPrepBackButton);

            if (simulateMatchButton != null && simulateMatchButton.TryGetComponent(out Image simulateMatchImage))
            {
                simulateMatchImage.color = ManagerUITheme.Accent;
                ManagerUITheme.NormalizeButtonLabel(simulateMatchButton, "SIMULATE MATCH", ManagerUITheme.OnAccent, 15);
            }

            if (fullTimeContinueButton != null && fullTimeContinueButton.TryGetComponent(out Image continueImage))
            {
                continueImage.color = ManagerUITheme.Accent;
                TextMeshProUGUI continueLabel = fullTimeContinueButton.GetComponentInChildren<TextMeshProUGUI>();
                if (continueLabel != null) continueLabel.color = ManagerUITheme.OnAccent;
            }

            if (headerText != null) headerText.color = ManagerUITheme.TextPrimary;
            if (nextFixtureText != null) nextFixtureText.color = ManagerUITheme.TextMuted;
            if (tacticText != null) tacticText.color = ManagerUITheme.TextMuted;
            if (leagueTableText != null) leagueTableText.color = ManagerUITheme.TextBody;
            if (subsStatusText != null)
            {
                subsStatusText.color = ManagerUITheme.TextMuted;
                subsStatusText.fontSize = 14;
                subsStatusText.textWrappingMode = TextWrappingModes.NoWrap;
                subsStatusText.overflowMode = TextOverflowModes.Truncate;
            }

            if (playerListTitleText != null)
            {
                playerListTitleText.color = ManagerUITheme.TextPrimary;
                playerListTitleText.fontSize = 20;
                playerListTitleText.textWrappingMode = TextWrappingModes.NoWrap;
                playerListTitleText.overflowMode = TextOverflowModes.Truncate;
            }
            if (fixtureTitleText != null) fixtureTitleText.color = ManagerUITheme.TextPrimary;
            if (clockText != null) clockText.color = ManagerUITheme.Accent;
            if (scoreText != null) scoreText.color = ManagerUITheme.TextPrimary;
            if (eventFeedText != null) eventFeedText.color = ManagerUITheme.TextBody;
            if (matchStatsText != null) matchStatsText.color = ManagerUITheme.TextBody;

            if (transfersButton != null)
            {
                ManagerUITheme.SetDisabledPlaceholder(transfersButton, "TRANSFERS");
            }

            if (exitToTitleButton != null && exitToTitleButton.TryGetComponent(out Image exitImage))
            {
                exitImage.color = ManagerUITheme.PanelDark;
                ManagerUITheme.NormalizeButtonLabel(exitToTitleButton, "SAVE & EXIT TO TITLE", ManagerUITheme.TextMuted, 14);
            }

            if (confirmTeamButton != null && confirmTeamButton.TryGetComponent(out Image confirmImage))
            {
                confirmImage.color = ManagerUITheme.Accent;
                ManagerUITheme.NormalizeButtonLabel(confirmTeamButton, "START CAREER", ManagerUITheme.OnAccent, 15);
            }

            StyleHubActionButton(teamSelectBackButton);
        }

        private static void StyleHubActionButton(Button button)
        {
            if (button == null || !button.TryGetComponent(out Image image))
            {
                return;
            }

            image.color = ManagerUITheme.CardNeutral;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = ManagerUITheme.TextBody;
                label.fontSize = 15;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Truncate;
            }
        }

        public void OnExitToTitleClicked()
        {
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);

            ShowTitleScreen();
        }

        // --- Title Screen ---

        private void ShowTitleScreen()
        {
            if (!titleScreenBuilt)
            {
                BuildTitleScreenContent();
                titleScreenBuilt = true;
            }

            if (titlePanel != null) titlePanel.SetActive(true);
            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);
        }

        // Built once at runtime rather than hand-placed in the Editor - logo mark,
        // wordmark, and the New Career / Load Career / Settings / Exit list. Load Career
        // and Settings are visible-but-disabled placeholders: there is no save system and
        // no settings screen yet, so pretending they work would be worse than being honest
        // about what's not built.
        private void BuildTitleScreenContent()
        {
            if (titleContentContainer == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(titlePanel);

            GameObject logo = new GameObject("LogoMark", typeof(RectTransform), typeof(Image));
            logo.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(logo, 60f, 96f, 96f);
            logo.GetComponent<Image>().color = ManagerUITheme.Accent;
            ManagerUITheme.BuildLabel(logo.transform, "M", 32, ManagerUITheme.OnAccent, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject wordmark = new GameObject("Wordmark", typeof(RectTransform));
            wordmark.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(wordmark, 180f, 600f, 56f);
            ManagerUITheme.BuildLabel(wordmark.transform, "MATCHDAY MANAGER", 34, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject subtitle = new GameObject("Subtitle", typeof(RectTransform));
            subtitle.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(subtitle, 232f, 600f, 30f);
            ManagerUITheme.BuildLabel(subtitle.transform, "THE ENGLISH PREMIER LEAGUE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            const float buttonWidth = 340f;
            const float buttonHeight = 52f;
            const float spacing = 12f;
            const float startY = 300f;

            GameObject newCareerObj = new GameObject("NewCareerButton", typeof(RectTransform), typeof(Image), typeof(Button));
            newCareerObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(newCareerObj, startY, buttonWidth, buttonHeight);
            newCareerObj.GetComponent<Image>().color = ManagerUITheme.Accent;
            Button newCareerButton = newCareerObj.GetComponent<Button>();
            newCareerButton.targetGraphic = newCareerObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(newCareerObj.transform, "NEW CAREER", 17, ManagerUITheme.OnAccent, TextAlignmentOptions.Center, FontStyles.Bold);
            newCareerButton.onClick.AddListener(OnTitleNewCareerClicked);

            GameObject loadCareerObj = new GameObject("LoadCareerButton", typeof(RectTransform), typeof(Image), typeof(Button));
            loadCareerObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(loadCareerObj, startY + buttonHeight + spacing, buttonWidth, buttonHeight);
            loadCareerObj.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            Button loadCareerButton = loadCareerObj.GetComponent<Button>();
            loadCareerButton.targetGraphic = loadCareerObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(loadCareerObj.transform, "LOAD CAREER", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
            ManagerUITheme.SetDisabledPlaceholder(loadCareerButton, "LOAD CAREER");

            GameObject settingsObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(settingsObj, startY + 2 * (buttonHeight + spacing), buttonWidth, buttonHeight);
            settingsObj.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            Button settingsButton = settingsObj.GetComponent<Button>();
            settingsButton.targetGraphic = settingsObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(settingsObj.transform, "SETTINGS", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
            ManagerUITheme.SetDisabledPlaceholder(settingsButton, "SETTINGS");

            GameObject exitObj = new GameObject("ExitButton", typeof(RectTransform), typeof(Image), typeof(Button));
            exitObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(exitObj, startY + 3 * (buttonHeight + spacing) + 8f, buttonWidth, 40f);
            exitObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;
            Button exitButton = exitObj.GetComponent<Button>();
            exitButton.targetGraphic = exitObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(exitObj.transform, "EXIT", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
            exitButton.onClick.AddListener(OnTitleExitClicked);
        }

        public void OnTitleNewCareerClicked()
        {
            if (titlePanel != null) titlePanel.SetActive(false);

            ShowTeamSelect();
        }

        public void OnTitleExitClicked()
        {
            Application.Quit();
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
            if (!teamGridBuilt)
            {
                BuildTeamSelectChrome();
                BuildTeamSelectGrid();
                teamGridBuilt = true;
            }

            if (teamSelectPanel != null) teamSelectPanel.SetActive(true);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshTeamSelectUI();
        }

        // Header/footer bands (see ManagerUITheme.BuildAccentBand) plus the two captions
        // above the name field and the grid. Both bands are 90px tall - TeamGridContainer
        // and ManagerNameInput need matching Top/Pos Y offsets in the Editor so their
        // content doesn't sit underneath these.
        private void BuildTeamSelectChrome()
        {
            if (teamSelectPanel == null)
            {
                return;
            }

            const float bandHeight = 90f;

            GameObject header = ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-44f, 30f);
            titleRect.anchoredPosition = new Vector2(24f, -18f);
            ManagerUITheme.BuildLabel(titleObj.transform, "NEW CAREER", 22, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-44f, 20f);
            subtitleRect.anchoredPosition = new Vector2(24f, -52f);
            ManagerUITheme.BuildLabel(subtitleObj.transform, "Step 1 of 1 — Manager & Club", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: false, height: bandHeight);

            GameObject nameCaption = new GameObject("ManagerNameCaption", typeof(RectTransform));
            nameCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform nameCaptionRect = nameCaption.GetComponent<RectTransform>();
            nameCaptionRect.anchorMin = new Vector2(0f, 1f);
            nameCaptionRect.anchorMax = new Vector2(0f, 1f);
            nameCaptionRect.pivot = new Vector2(0f, 1f);
            nameCaptionRect.sizeDelta = new Vector2(180f, 18f);
            nameCaptionRect.anchoredPosition = new Vector2(24f, -(bandHeight + 6f));
            ManagerUITheme.BuildLabel(nameCaption.transform, "MANAGER NAME", 11, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nameCaption.transform.SetAsFirstSibling();

            GameObject clubCaption = new GameObject("SelectClubCaption", typeof(RectTransform));
            clubCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform clubCaptionRect = clubCaption.GetComponent<RectTransform>();
            clubCaptionRect.anchorMin = new Vector2(0f, 1f);
            clubCaptionRect.anchorMax = new Vector2(0f, 1f);
            clubCaptionRect.pivot = new Vector2(0f, 1f);
            clubCaptionRect.sizeDelta = new Vector2(400f, 18f);
            clubCaptionRect.anchoredPosition = new Vector2(220f, -(bandHeight + 6f));
            ManagerUITheme.BuildLabel(clubCaption.transform, "SELECT CLUB — PREMIER LEAGUE", 11, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            clubCaption.transform.SetAsFirstSibling();
        }

        // Real 20-club grid (5 columns), built once at runtime from availableTeamNames -
        // the actual clubs in the season file, not a hand-authored/fictional list.
        private void BuildTeamSelectGrid()
        {
            if (teamGridContainer == null)
            {
                return;
            }

            const int columns = 5;
            int rows = Mathf.Max(1, Mathf.CeilToInt(availableTeamNames.Count / (float)columns));

            teamGridButtons.Clear();

            for (int i = 0; i < availableTeamNames.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                GameObject cell = new GameObject($"Club_{availableTeamNames[i]}", typeof(RectTransform), typeof(Image), typeof(Button));
                cell.transform.SetParent(teamGridContainer, false);

                RectTransform cellRect = cell.GetComponent<RectTransform>();
                float colWidth = 1f / columns;
                float rowHeight = 1f / rows;
                cellRect.anchorMin = new Vector2(col * colWidth, 1f - (row + 1) * rowHeight);
                cellRect.anchorMax = new Vector2((col + 1) * colWidth, 1f - row * rowHeight);
                cellRect.offsetMin = new Vector2(4f, 4f);
                cellRect.offsetMax = new Vector2(-4f, -4f);

                Image image = cell.GetComponent<Image>();
                image.color = ManagerUITheme.CardNeutralAlt;

                Button button = cell.GetComponent<Button>();
                button.targetGraphic = image;

                ManagerUITheme.BuildLabel(cell.transform, availableTeamNames[i].ToUpperInvariant(), 12, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);

                int capturedIndex = i;
                button.onClick.AddListener(() => OnTeamGridTileClicked(capturedIndex));

                teamGridButtons.Add(button);
            }
        }

        private void OnTeamGridTileClicked(int index)
        {
            selectedTeamIndex = index;
            RefreshTeamSelectUI();
        }

        private void RefreshTeamSelectUI()
        {
            for (int i = 0; i < teamGridButtons.Count; i++)
            {
                Button button = teamGridButtons[i];

                if (button == null || !button.TryGetComponent(out Image image))
                {
                    continue;
                }

                bool selected = i == selectedTeamIndex;
                image.color = selected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutralAlt;

                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.color = selected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
                }
            }
        }

        public void OnTeamSelectBackClicked()
        {
            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

            ShowTitleScreen();
        }

        public void OnConfirmTeamClicked()
        {
            if (availableTeamNames.Count > 0)
            {
                managedTeamName = availableTeamNames[selectedTeamIndex];
            }

            if (managerNameInput != null && !string.IsNullOrWhiteSpace(managerNameInput.text))
            {
                managerName = managerNameInput.text.Trim();
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

            HighlightSelectedTacticButton(attackingButton, tactic == ManagerTactic.Attacking);
            HighlightSelectedTacticButton(balancedButton, tactic == ManagerTactic.Balanced);
            HighlightSelectedTacticButton(defensiveButton, tactic == ManagerTactic.Defensive);
        }

        private static void HighlightSelectedTacticButton(Button button, bool selected)
        {
            if (button == null || !button.TryGetComponent(out Image image))
            {
                return;
            }

            image.color = selected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = selected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
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
                headerText.text = $"Manager {managerName} — {managedTeamName}";
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

        // Shared by the Hub (informational) and Matchday Prep (where subs are actually
        // made) - both keep the Make Subs button/counter in sync with the current cap.
        private void RefreshSubsStatusUI()
        {
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

            playerListReturnsToMatchdayPrep = false;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (playerListPanel != null) playerListPanel.SetActive(true);
            if (playerListTitleText != null) playerListTitleText.text = $"{managedTeamName} Squad";

            if (squadListView != null)
            {
                squadListView.Clear();
                squadListView.AddSectionHeader("Starting XI");

                foreach (PlayerAgent player in team.StartingEleven)
                {
                    squadListView.AddPlayerRow(player, DescribePlayer(player), OnSquadBrowseRowClicked, GetRatingPercent(player));
                }

                squadListView.AddSectionHeader($"Bench ({team.Bench.Count})");

                foreach (PlayerAgent player in team.Bench)
                {
                    squadListView.AddPlayerRow(player, DescribePlayer(player), OnSquadBrowseRowClicked, GetRatingPercent(player));
                }
            }

            if (squadSortButton != null) ManagerUITheme.SetDisabledPlaceholder(squadSortButton, "SORT: POSITION");
            if (squadFilterButton != null) ManagerUITheme.SetDisabledPlaceholder(squadFilterButton, "FILTER");
        }

        private static float GetRatingPercent(PlayerAgent player)
        {
            return GetDisplayRating(player.GetOverallRating()) / 99f;
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
                squadListView.Populate(players, DescribePlayer, onRowClicked, ratingSelector: GetRatingPercent);
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
            else if (playerListReturnsToMatchdayPrep)
            {
                ShowMatchdayPrep();
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
            playerListReturnsToMatchdayPrep = true;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);

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
            else if (playerListReturnsToMatchdayPrep)
            {
                ShowMatchdayPrep();
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

        // --- Player Inspect (Prev/Next once inside; entry point jumps straight to a
        // specific player from the squad browse list - no standalone Hub entry point) ---

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

        private readonly List<GameObject> spawnedInspectElements = new();

        // Rebuilt in full each time (unlike Title/Team Select, which build once) since the
        // content changes per player. Only uses PlayerAgent fields that actually exist -
        // no invented Age or descriptive role titles like "Ball-Playing Defender", since
        // this data doesn't track either.
        private void RefreshPlayerInspectUI()
        {
            if (playerInspectContentContainer == null || inspectSquadPlayers.Count == 0)
            {
                return;
            }

            foreach (GameObject element in spawnedInspectElements)
            {
                if (element != null)
                {
                    Destroy(element);
                }
            }

            spawnedInspectElements.Clear();

            PlayerAgent player = inspectSquadPlayers[inspectPlayerIndex];
            string squadStatus = player.IsStartingEleven ? "Starting XI" : "Bench";

            GameObject headerBand = new GameObject("HeaderBand", typeof(RectTransform), typeof(Image));
            headerBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(headerBand, 0f, 130f);
            headerBand.GetComponent<Image>().color = ManagerUITheme.PanelDark;
            spawnedInspectElements.Add(headerBand);

            GameObject photo = new GameObject("PhotoPlaceholder", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(headerBand.transform, false);
            RectTransform photoRect = photo.GetComponent<RectTransform>();
            photoRect.anchorMin = new Vector2(0f, 1f);
            photoRect.anchorMax = new Vector2(0f, 1f);
            photoRect.pivot = new Vector2(0f, 1f);
            photoRect.sizeDelta = new Vector2(76f, 76f);
            photoRect.anchoredPosition = new Vector2(24f, -24f);
            photo.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject nameLabel = new GameObject("Name", typeof(RectTransform));
            nameLabel.transform.SetParent(headerBand.transform, false);
            RectTransform nameRect = nameLabel.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.sizeDelta = new Vector2(-220f, 30f);
            nameRect.anchoredPosition = new Vector2(116f, -22f);
            ManagerUITheme.BuildLabel(nameLabel.transform, player.Name.ToUpperInvariant(), 22, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject metaLabel = new GameObject("Meta", typeof(RectTransform));
            metaLabel.transform.SetParent(headerBand.transform, false);
            RectTransform metaRect = metaLabel.GetComponent<RectTransform>();
            metaRect.anchorMin = new Vector2(0f, 1f);
            metaRect.anchorMax = new Vector2(1f, 1f);
            metaRect.pivot = new Vector2(0f, 1f);
            metaRect.sizeDelta = new Vector2(-220f, 22f);
            metaRect.anchoredPosition = new Vector2(116f, -54f);
            string metaText = $"{player.Role}  ·  Weak Foot: {BuildStarRating(player.WeakFoot)}  ·  Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})";
            ManagerUITheme.BuildLabel(metaLabel.transform, metaText, 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            float badgeX = 116f;
            AddPositionBadge(headerBand.transform, player.PrimaryPosition.ToString(), badgeX, true);
            badgeX += 56f;

            foreach (PlayerPosition secondary in player.SecondaryPositions)
            {
                AddPositionBadge(headerBand.transform, secondary.ToString(), badgeX, false);
                badgeX += 56f;
            }

            int displayRating = GetDisplayRating(player.GetOverallRating());

            GameObject ovrValue = new GameObject("OvrValue", typeof(RectTransform));
            ovrValue.transform.SetParent(headerBand.transform, false);
            RectTransform ovrRect = ovrValue.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(1f, 1f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.pivot = new Vector2(1f, 1f);
            ovrRect.sizeDelta = new Vector2(90f, 44f);
            ovrRect.anchoredPosition = new Vector2(-24f, -20f);
            ManagerUITheme.BuildLabel(ovrValue.transform, displayRating.ToString(), 34, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject ovrCaption = new GameObject("OvrCaption", typeof(RectTransform));
            ovrCaption.transform.SetParent(headerBand.transform, false);
            RectTransform ovrCaptionRect = ovrCaption.GetComponent<RectTransform>();
            ovrCaptionRect.anchorMin = new Vector2(1f, 1f);
            ovrCaptionRect.anchorMax = new Vector2(1f, 1f);
            ovrCaptionRect.pivot = new Vector2(1f, 1f);
            ovrCaptionRect.sizeDelta = new Vector2(140f, 16f);
            ovrCaptionRect.anchoredPosition = new Vector2(-24f, -60f);
            ManagerUITheme.BuildLabel(ovrCaption.transform, $"OVERALL ({player.PrimaryPosition})", 10, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            GameObject attributeGrid = new GameObject("AttributeGrid", typeof(RectTransform));
            attributeGrid.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(attributeGrid, 150f, 220f, 20f);
            spawnedInspectElements.Add(attributeGrid);

            RectTransform attributeGridRect = attributeGrid.GetComponent<RectTransform>();

            BuildAttributeColumn(attributeGridRect, 0, 4, "Technical", new (string, float)[]
            {
                ("Finishing", player.Finishing), ("Passing", player.Passing), ("Dribbling", player.Dribbling),
                ("Crossing", player.Crossing), ("Heading", player.Heading)
            });

            BuildAttributeColumn(attributeGridRect, 1, 4, "Mental", new (string, float)[]
            {
                ("Creativity", player.Creativity), ("Positioning", player.Positioning), ("Composure", player.Composure)
            });

            BuildAttributeColumn(attributeGridRect, 2, 4, "Defensive", new (string, float)[]
            {
                ("Defending", player.Defending), ("Tackling", player.Tackling)
            });

            BuildAttributeColumn(attributeGridRect, 3, 4, "Physical", new (string, float)[]
            {
                ("Pace", player.Pace), ("Strength", player.Strength), ("Stamina", player.Stamina), ("Aerial", player.Aerial)
            });
        }

        private void AddPositionBadge(Transform parent, string label, float x, bool primary)
        {
            GameObject badge = new GameObject($"Badge_{label}", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);

            RectTransform rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(48f, 22f);
            rect.anchoredPosition = new Vector2(x, -88f);

            badge.GetComponent<Image>().color = primary ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            ManagerUITheme.BuildLabel(
                badge.transform,
                label,
                11,
                primary ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody,
                TextAlignmentOptions.Center,
                FontStyles.Bold);
        }

        private static void BuildAttributeColumn(RectTransform parent, int columnIndex, int totalColumns, string title, (string label, float value)[] attributes)
        {
            GameObject column = new GameObject($"Column_{title}", typeof(RectTransform));
            column.transform.SetParent(parent, false);

            RectTransform columnRect = column.GetComponent<RectTransform>();
            float colWidth = 1f / totalColumns;
            columnRect.anchorMin = new Vector2(columnIndex * colWidth, 0f);
            columnRect.anchorMax = new Vector2((columnIndex + 1) * colWidth, 1f);
            columnRect.offsetMin = new Vector2(6f, 0f);
            columnRect.offsetMax = new Vector2(-6f, 0f);

            GameObject titleObj = new GameObject("ColumnTitle", typeof(RectTransform));
            titleObj.transform.SetParent(column.transform, false);
            ManagerUITheme.AnchorTopStretch(titleObj, 0f, 20f);
            ManagerUITheme.BuildLabel(titleObj.transform, title.ToUpperInvariant(), 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            float offset = 28f;

            foreach ((string label, float value) in attributes)
            {
                offset = BuildAttributeRow(column.transform, offset, label, value);
            }
        }

        private static float BuildAttributeRow(Transform parent, float topOffset, string label, float value)
        {
            GameObject labelRow = new GameObject($"AttrLabel_{label}", typeof(RectTransform));
            labelRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelRow, topOffset, 16f);

            GameObject nameText = new GameObject("Name", typeof(RectTransform));
            nameText.transform.SetParent(labelRow.transform, false);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(0.7f, 1f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(nameText.transform, label, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            GameObject valueText = new GameObject("Value", typeof(RectTransform));
            valueText.transform.SetParent(labelRow.transform, false);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.7f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(valueText.transform, Mathf.RoundToInt(value).ToString(), 13, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight);

            GameObject barRow = new GameObject($"AttrBar_{label}", typeof(RectTransform));
            barRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(barRow, topOffset + 18f, 5f);
            ManagerUITheme.BuildBar(barRow.transform, value / 100f, ManagerUITheme.RatingColor(value), 5f);

            return topOffset + 34f;
        }

        private static string BuildStarRating(float rawValue)
        {
            int stars = Mathf.Clamp(Mathf.RoundToInt(rawValue / 20f), 1, 5);
            return new string('★', stars) + new string('☆', 5 - stars);
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

        // --- Matchday Prep (opponent scouting, Tactic, pre-match Subs - shown before
        // every match instead of simulating straight from the Hub) ---

        public void OnNextMatchdayClicked()
        {
            if (currentFixtureIndex >= managedTeamFixtures.Count)
            {
                return;
            }

            currentFixture = managedTeamFixtures[currentFixtureIndex];

            ShowMatchdayPrep();
        }

        private void ShowMatchdayPrep()
        {
            if (!matchdayPrepChromeBuilt)
            {
                BuildMatchdayPrepChrome();
                matchdayPrepChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(true);

            RefreshMatchdayPrepUI();
        }

        // Header/footer accent bands + the two title labels, built once. The labels'
        // actual text is filled in per-fixture by RefreshMatchdayPrepUI.
        private void BuildMatchdayPrepChrome()
        {
            if (matchdayPrepContentContainer == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(matchdayPrepPanel);

            const float bandHeight = 90f;

            GameObject header = ManagerUITheme.BuildAccentBand(matchdayPrepContentContainer, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-44f, 30f);
            titleRect.anchoredPosition = new Vector2(24f, -18f);
            matchdayPrepTitleLabel = ManagerUITheme.BuildLabel(titleObj.transform, "", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-44f, 20f);
            subtitleRect.anchoredPosition = new Vector2(24f, -52f);
            matchdayPrepSubtitleLabel = ManagerUITheme.BuildLabel(subtitleObj.transform, "", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            ManagerUITheme.BuildAccentBand(matchdayPrepContentContainer, topBand: false, height: bandHeight);
        }

        private void RefreshMatchdayPrepUI()
        {
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            string opponentName = managedIsHome ? currentFixture.AwayTeam : currentFixture.HomeTeam;
            AgentTeam opponentTeam = GetOrCreateAgentTeam(opponentName);

            if (matchdayPrepTitleLabel != null)
            {
                matchdayPrepTitleLabel.text = managedIsHome
                    ? $"{managedTeamName} vs {opponentName} (Home)"
                    : $"{managedTeamName} vs {opponentName} (Away)";
            }

            if (matchdayPrepSubtitleLabel != null)
            {
                matchdayPrepSubtitleLabel.text = $"Matchday {currentFixture.Matchday}   ·   Opponent Formation: {opponentTeam.Formation}";
            }

            if (opponentSquadListView != null)
            {
                opponentSquadListView.Clear();
                opponentSquadListView.AddSectionHeader("Starting XI");

                foreach (PlayerAgent player in opponentTeam.StartingEleven)
                {
                    opponentSquadListView.AddPlayerRow(player, DescribePlayer(player), _ => { }, GetRatingPercent(player));
                }

                opponentSquadListView.AddSectionHeader($"Bench ({opponentTeam.Bench.Count})");

                foreach (PlayerAgent player in opponentTeam.Bench)
                {
                    opponentSquadListView.AddPlayerRow(player, DescribePlayer(player), _ => { }, GetRatingPercent(player));
                }
            }

            HighlightSelectedTacticButton(attackingButton, selectedTactic == ManagerTactic.Attacking);
            HighlightSelectedTacticButton(balancedButton, selectedTactic == ManagerTactic.Balanced);
            HighlightSelectedTacticButton(defensiveButton, selectedTactic == ManagerTactic.Defensive);

            RefreshSubsStatusUI();
        }

        public void OnMatchdayPrepBackClicked()
        {
            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);

            ShowSeasonHub();
        }

        public void OnSimulateMatchClicked()
        {
            tacticUsedForCurrentMatch = selectedTactic;

            AgentMatchSimulator.AgentMatchResult result = SimulateFixture(currentFixture);

            lastSimulatedResult = result;

            SimulateOtherFixturesInMatchday(currentFixture.Matchday);

            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);
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
                int homeShots = 0;
                int awayShots = 0;

                foreach (AgentMatchSimulator.AgentMatchEvent matchEvent in result.Events)
                {
                    if (!matchEvent.IsShot)
                    {
                        continue;
                    }

                    if (matchEvent.HomeTeamAttacking) homeShots++; else awayShots++;
                }

                matchStatsText.text =
                    "FULL-TIME STATS\n" +
                    $"{currentFixture.HomeTeam} {result.HomeGoals} - {result.AwayGoals} {currentFixture.AwayTeam}\n" +
                    $"Tactic Used: {tacticUsedForCurrentMatch}\n" +
                    $"Shots: {homeShots} / {awayShots}\n" +
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
