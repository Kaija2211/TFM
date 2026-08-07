using System;
using System.Collections;
using System.Collections.Generic;
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

        // headerText/nextFixtureText/tacticText/leagueTableText from the original restyle
        // pass are retired - the Hub's visual layout now matches the newer mockup (crest,
        // club name/byline, two-column body, styled table) built entirely in code by
        // BuildHubChrome/RefreshHubUI. The five buttons below are unchanged Editor
        // references, just repositioned by code now instead of by hand.
        [Header("Season Hub UI")]
        [SerializeField] private GameObject seasonHubPanel;
        [SerializeField] private Button playNextMatchButton;
        [SerializeField] private Button simulateSeasonButton;
        [SerializeField] private Button viewSquadButton;
        [SerializeField] private Button transfersButton; // disabled placeholder - no transfer system exists yet
        [SerializeField] private Button exitToTitleButton;
        [SerializeField] private LeagueTableView leagueTableView;

        // Tactic buttons are NOT declared here - they're the same
        // attackingButton/balancedButton/defensiveButton fields further down,
        // reparented in the Editor from the Hub onto this screen. Same C# references,
        // they just live under a different panel now.
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

        [Header("Substitutions")]
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

        // Pause and Tactics/Subs are built entirely in code (BuildMatchdayChrome) rather
        // than as Editor-placed [SerializeField] refs - there was nothing pre-existing in
        // the scene to wire them to, so this skips the usual "create it by hand, then drag
        // it into the Inspector" round trip entirely.
        private bool matchdayChromeBuilt;
        private bool matchPaused;
        private Button pauseButton;
        private bool playerInspectChromeBuilt;
        private TextMeshProUGUI matchHomeNameLabel;
        private TextMeshProUGUI matchAwayNameLabel;
        private GameObject[] matchLiveOnlyElements;
        private GameObject matchFullTimeCaptionGroup;
        private TextMeshProUGUI matchFullTimeCaptionLabel;
        private RectTransform matchStatsBarsContainer;
        private TextMeshProUGUI matchStatsCaptionLabel;
        private RectTransform matchStatsCaptionRect;
        private RectTransform matchKeyMomentsCaptionRect;
        private GameObject matchLogGroup;
        private TextMeshProUGUI matchSubsStatusLabel;
        private TextMeshProUGUI matchHomeScorersLabel;
        private TextMeshProUGUI matchAwayScorersLabel;
        private Button viewMatchEventsButton;
        private List<AgentMatchSimulator.AgentMatchEvent> lastMatchEvents;
        private List<GameObject> matchFullTimeOnlyElements;

        private bool matchEventsChromeBuilt;
        private GameObject matchEventsPanel;
        private TextMeshProUGUI matchEventsScoreText;
        private TextMeshProUGUI matchEventsHomeNameLabel;
        private TextMeshProUGUI matchEventsAwayNameLabel;
        private RectTransform matchEventsListContainer;

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

        // TMP Sprite Assets (Assets/Resources/Manager/*.asset) - loaded once here rather
        // than per-build-call. star-filled has star-empty wired as its fallback sprite
        // asset (see the .asset itself), so a single <sprite name="..."> tag in text
        // assigned this as its spriteAsset can resolve either glyph.
        private TMP_SpriteAsset weakFootStarSpriteAsset;
        private TMP_SpriteAsset footballIconSpriteAsset;

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

        private bool hubChromeBuilt;
        private TextMeshProUGUI hubClubNameLabel;
        private TextMeshProUGUI hubBylineLabel;
        private Coroutine hubBylineRecreateCoroutine;

        // --- Squad: Tactics Board (pitch view, drag-to-sub, formation switching) ---
        private bool tacticsBoardChromeBuilt;
        private GameObject tacticsBoardPanel;
        private RectTransform tacticsBoardPitchContainer;
        private RectTransform tacticsBoardBenchContent;
        private Button tacticsBoardFormationButton;
        private GameObject tacticsBoardFormationDropdown;

        // Player Inspect always hides seasonHubPanel and its Back button always returns
        // to it - true for every OTHER entry point, but Tactics Board is a second panel
        // that also needs hiding/restoring, so this flag distinguishes the two callers.
        private bool playerInspectReturnsToTacticsBoard;

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
            weakFootStarSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/star-filled");
            footballIconSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/football-icon");

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

            // Seed every club into the table at 0 played/0 points so the Hub shows the
            // full 20-team league from Matchday 1, not just teams that have played -
            // otherwise Sorted() returns nothing until the first result comes in.
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

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
            // seasonHubPanel's background, and playNextMatchButton/simulateSeasonButton/
            // viewSquadButton's styling, are handled by BuildHubChrome instead (which also
            // repositions them) - styling them here too would just be redundant, since
            // BuildHubChrome runs later and wins.
            ManagerUITheme.ApplyPanelBackground(matchdayPanel);
            ManagerUITheme.ApplyPanelBackground(teamSelectPanel);
            ManagerUITheme.ApplyPanelBackground(playerInspectPanel);
            ManagerUITheme.ApplyPanelBackground(playerListPanel);
            ManagerUITheme.ApplyPanelBackground(matchdayPrepPanel);

            // squadListView's ScrollView background was never recolored (same gap as the
            // Hub's league table before it was fixed) - invisible whenever the list has
            // enough rows to cover it, but exposed as a gray box on shorter lists like the
            // Starting-XI-only substitution picker.
            if (squadListView != null && squadListView.TryGetComponent(out Image squadListImage))
            {
                squadListImage.color = ManagerUITheme.PanelDark;
            }

            StyleHubActionButton(skipToResultsButton);
            StyleHubActionButton(makeSubButton);
            StyleHubActionButton(matchdayPrepBackButton);
            // StyleHubActionButton only recolors - it never sets label text, since the other
            // three buttons it's used on kept their correct hand-authored Editor text. This
            // one was freshly created and still had Unity's literal default "Button" label.
            ManagerUITheme.NormalizeButtonLabel(matchdayPrepBackButton, "BACK TO HUB", ManagerUITheme.TextBody, 15);

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

            // These are all pre-existing Editor TMP_Text fields, styled directly here
            // rather than through ManagerUITheme.BuildLabel - none of them ever had
            // .font set anywhere, so they kept whatever font they were originally
            // created with regardless of the project's current default.
            TMP_FontAsset themeFont = TMP_Settings.defaultFontAsset;

            if (subsStatusText != null)
            {
                subsStatusText.color = ManagerUITheme.TextMuted;
                subsStatusText.fontSize = 14;
                subsStatusText.textWrappingMode = TextWrappingModes.NoWrap;
                subsStatusText.overflowMode = TextOverflowModes.Truncate;
                if (themeFont != null) subsStatusText.font = themeFont;
            }

            if (playerListTitleText != null)
            {
                playerListTitleText.color = ManagerUITheme.TextPrimary;
                playerListTitleText.fontSize = 20;
                playerListTitleText.textWrappingMode = TextWrappingModes.NoWrap;
                playerListTitleText.overflowMode = TextOverflowModes.Truncate;
                if (themeFont != null) playerListTitleText.font = themeFont;
            }
            if (fixtureTitleText != null) fixtureTitleText.color = ManagerUITheme.TextPrimary;
            if (clockText != null) { clockText.color = ManagerUITheme.Accent; if (themeFont != null) clockText.font = themeFont; }
            if (scoreText != null) { scoreText.color = ManagerUITheme.TextPrimary; if (themeFont != null) scoreText.font = themeFont; }
            if (eventFeedText != null) { eventFeedText.color = ManagerUITheme.TextBody; if (themeFont != null) eventFeedText.font = themeFont; }
            if (matchStatsText != null) matchStatsText.color = ManagerUITheme.TextBody;

            // transfersButton and exitToTitleButton are also handled by BuildHubChrome,
            // same reasoning as above.

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
                label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Truncate;

                // This function is separate from ManagerUITheme.BuildLabel/
                // NormalizeButtonLabel (which already force the theme font) - buttons
                // ONLY ever touched by this one (several of the earliest ones from before
                // this reskin) kept whatever font they were originally created with.
                if (TMP_Settings.defaultFontAsset != null)
                {
                    label.font = TMP_Settings.defaultFontAsset;
                }
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

            // titleContentContainer had several hand-set values baked into the scene from
            // an earlier manual Editor edit - a m_LocalScale of (2,2,1), plus a shrunk
            // (0.5,0.5)-(0.5,0.5) point anchor shoved 500 units above center instead of
            // stretching to fill the panel. Every child below is positioned via
            // AnchorTopCenter/point-anchors relative to this container's own rect, so if
            // it isn't actually a full-stretch rect at (0,0), none of those positions
            // mean what they're supposed to - which is exactly what pushed the whole
            // screen (including the New Career button) above the visible viewport.
            // Reset defensively so this can't silently break again from a stray edit.
            titleContentContainer.localScale = Vector3.one;
            titleContentContainer.anchorMin = Vector2.zero;
            titleContentContainer.anchorMax = Vector2.one;
            titleContentContainer.anchoredPosition = Vector2.zero;
            titleContentContainer.sizeDelta = Vector2.zero;

            // The old shield-icon + "MATCHDAY MANAGER" text combo is replaced by the
            // single wordmark ("TF" + accent-green "M") - the club crest badge on the Hub
            // is a separate thing (the managed club's crest) and is untouched.
            GameObject wordmark = new GameObject("Wordmark", typeof(RectTransform));
            wordmark.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(wordmark, 70f, 400f, 90f);
            TextMeshProUGUI wordmarkLabel = ManagerUITheme.BuildLabel(wordmark.transform, "TF<color=#3ddc84>M</color>", 64, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            wordmarkLabel.characterSpacing = 4f;

            // The wordmark is the very first TextMeshProUGUI created in the whole play
            // session (Title is always shown first), and on a genuinely fresh session it
            // can silently fail to generate any mesh at all - texts built moments later
            // (Subtitle, buttons) using the exact same font asset render fine, and
            // ForceMeshUpdate/SetText/toggling .enabled done in the SAME frame don't
            // recover it either (confirmed live), because whatever TMP/font-asset
            // initialization it's racing hasn't finished within that first frame yet.
            // Waiting a frame before checking gives that initialization time to complete.
            StartCoroutine(RecoverBlankLabelNextFrame(wordmarkLabel));

            GameObject subtitle = new GameObject("Subtitle", typeof(RectTransform));
            subtitle.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(subtitle, 175f, 600f, 30f);
            ManagerUITheme.BuildLabel(subtitle.transform, "THE ENGLISH PREMIER LEAGUE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            const float buttonWidth = 340f;
            const float buttonHeight = 52f;
            const float spacing = 12f;
            const float startY = 250f;

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

        // See BuildTitleScreenContent's call site - recovers a label that came out of
        // creation with zero generated characters despite non-empty text. Originally seen
        // only on the very first TMP label built each session (the Title wordmark); later
        // also seen on a label rebuilt via rapid destroy/recreate churn (Player Inspect's
        // OvrValue - see RecoverBlankLabelsNextFrame below), so it isn't actually limited
        // to "first ever" - something about TMP's mesh generation is more generally
        // flaky than that. Destroying and recreating the component is the only thing
        // that's been found to recover it, and that only works once at least one real
        // frame has passed.
        private IEnumerator RecoverBlankLabelNextFrame(TextMeshProUGUI label)
        {
            yield return null;

            if (label == null || string.IsNullOrEmpty(label.text))
            {
                yield break;
            }

            label.ForceMeshUpdate();

            if (label.textInfo.characterCount > 0)
            {
                yield break;
            }

            GameObject labelObject = label.gameObject;
            string text = label.text;
            float fontSize = label.fontSize;
            Color color = label.color;
            TextAlignmentOptions alignment = label.alignment;
            FontStyles fontStyle = label.fontStyle;
            float characterSpacing = label.characterSpacing;
            TMP_FontAsset font = label.font;

            // Must be DestroyImmediate, not Destroy - AddComponent on the same GameObject
            // in the same frame silently returns null while the old component is still
            // pending removal from a deferred Destroy().
            DestroyImmediate(label);

            TextMeshProUGUI fresh = labelObject.AddComponent<TextMeshProUGUI>();
            fresh.font = font;
            fresh.text = text;
            fresh.fontSize = fontSize;
            fresh.color = color;
            fresh.alignment = alignment;
            fresh.fontStyle = fontStyle;
            fresh.characterSpacing = characterSpacing;
            fresh.raycastTarget = false;
            fresh.ForceMeshUpdate();
        }

        // General-purpose version of the recovery above: sweeps every TextMeshProUGUI
        // under a root and recovers any that came out blank, rather than checking one
        // specific label. Used by screens (like Player Inspect) that destroy and rebuild
        // their whole label set on every refresh, where any of them could be the one that
        // happens to hit the same TMP mesh-generation failure.
        private IEnumerator RecoverBlankLabelsNextFrame(Transform root)
        {
            yield return null;

            if (root == null)
            {
                yield break;
            }

            foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label == null || string.IsNullOrEmpty(label.text))
                {
                    continue;
                }

                label.ForceMeshUpdate();

                if (label.textInfo.characterCount > 0)
                {
                    continue;
                }

                GameObject labelObject = label.gameObject;
                string text = label.text;
                float fontSize = label.fontSize;
                Color color = label.color;
                TextAlignmentOptions alignment = label.alignment;
                FontStyles fontStyle = label.fontStyle;
                float characterSpacing = label.characterSpacing;
                TMP_FontAsset font = label.font;

                DestroyImmediate(label);

                TextMeshProUGUI fresh = labelObject.AddComponent<TextMeshProUGUI>();
                fresh.font = font;
                fresh.text = text;
                fresh.fontSize = fontSize;
                fresh.color = color;
                fresh.alignment = alignment;
                fresh.fontStyle = fontStyle;
                fresh.characterSpacing = characterSpacing;
                fresh.raycastTarget = false;
                fresh.ForceMeshUpdate();
            }
        }

        // See RefreshHubUI's call site. hubBylineLabel is a class field (reassigned
        // repeatedly across the session, unlike the one-shot local labels the other
        // recovery coroutines handle), so this reassigns it to the fresh component
        // rather than leaving the field pointing at the destroyed one.
        // WaitForEndOfFrame rather than a plain "yield return null" - the coroutine-
        // race fix (StopCoroutine before restarting) cut this down a lot but didn't
        // fully eliminate a fainter version of the same overlap (confirmed live).
        // "yield return null" resumes at the START of the next Update, before that
        // frame's render pass - if the old label's draw call was already queued for
        // THIS frame by the time the destroy/recreate runs, the stale glyph can still
        // get composited once more. Waiting for the actual end of the frame instead
        // means the destroy/recreate always happens strictly after a render, so the
        // freshly rebuilt mesh has a full frame to settle before it's ever drawn.
        private IEnumerator RecreateHubBylineLabelNextFrame()
        {
            yield return new WaitForEndOfFrame();

            if (hubBylineLabel == null)
            {
                yield break;
            }

            GameObject labelObject = hubBylineLabel.gameObject;
            string text = hubBylineLabel.text;
            float fontSize = hubBylineLabel.fontSize;
            Color color = hubBylineLabel.color;
            TextAlignmentOptions alignment = hubBylineLabel.alignment;
            FontStyles fontStyle = hubBylineLabel.fontStyle;
            float characterSpacing = hubBylineLabel.characterSpacing;
            TMP_FontAsset font = hubBylineLabel.font;

            DestroyImmediate(hubBylineLabel);

            TextMeshProUGUI fresh = labelObject.AddComponent<TextMeshProUGUI>();
            fresh.font = font;
            fresh.text = text;
            fresh.fontSize = fontSize;
            fresh.color = color;
            fresh.alignment = alignment;
            fresh.fontStyle = fontStyle;
            fresh.characterSpacing = characterSpacing;
            fresh.raycastTarget = false;
            fresh.ForceMeshUpdate();

            hubBylineLabel = fresh;
            hubBylineRecreateCoroutine = null;
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
            ManagerUITheme.BuildLabel(subtitleObj.transform, "Step 1 of 1 · Manager & Club", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

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
            ManagerUITheme.BuildLabel(clubCaption.transform, "SELECT CLUB · PREMIER LEAGUE", 11, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            clubCaption.transform.SetAsFirstSibling();

            // managerNameInput and teamGridContainer are Editor-placed objects (an
            // InputField and a Scroll/Grid layout aren't worth rebuilding from scratch
            // in code), but their position/size/color was left to hand-dragging instead
            // of being set here - the exact failure mode this file's other screens
            // deliberately avoid. Margins below match the design mockup's proportions
            // (header-to-caption and caption-to-content gaps, not just a token few px).
            const float captionTop = bandHeight + 40f;
            const float captionHeight = 18f;
            float contentTop = captionTop + captionHeight + 32f;

            nameCaptionRect.anchoredPosition = new Vector2(24f, -captionTop);
            clubCaptionRect.anchoredPosition = new Vector2(220f, -captionTop);

            if (managerNameInput != null)
            {
                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();
                ManagerUITheme.SetPointAnchor(inputRect, new Vector2(0f, 1f), new Vector2(24f, -contentTop), new Vector2(200f, 48f));

                if (managerNameInput.TryGetComponent(out Image inputImage))
                {
                    inputImage.color = ManagerUITheme.PanelDark;
                }

                // The typed-text color was never set (only the box background was),
                // so it was still whatever Unity's default TMP Input Field text color
                // is - too dim to read against a dark box. Font was never set either.
                if (managerNameInput.textComponent != null)
                {
                    managerNameInput.textComponent.color = ManagerUITheme.TextPrimary;
                    if (TMP_Settings.defaultFontAsset != null) managerNameInput.textComponent.font = TMP_Settings.defaultFontAsset;
                }

                if (managerNameInput.placeholder is TextMeshProUGUI placeholderLabel)
                {
                    placeholderLabel.color = ManagerUITheme.TextMuted;
                    if (TMP_Settings.defaultFontAsset != null) placeholderLabel.font = TMP_Settings.defaultFontAsset;
                }

                GameObject inputAccent = new GameObject("LeftAccent", typeof(RectTransform), typeof(Image));
                inputAccent.transform.SetParent(inputRect, false);
                RectTransform inputAccentRect = inputAccent.GetComponent<RectTransform>();
                inputAccentRect.anchorMin = new Vector2(0f, 0f);
                inputAccentRect.anchorMax = new Vector2(0f, 1f);
                inputAccentRect.pivot = new Vector2(0f, 0.5f);
                inputAccentRect.sizeDelta = new Vector2(3f, 0f);
                inputAccentRect.anchoredPosition = Vector2.zero;
                inputAccent.GetComponent<Image>().color = ManagerUITheme.Accent;
            }

            if (teamGridContainer != null)
            {
                RectTransform gridRect = teamGridContainer.GetComponent<RectTransform>();
                gridRect.anchorMin = new Vector2(0f, 0f);
                gridRect.anchorMax = new Vector2(1f, 1f);
                gridRect.offsetMin = new Vector2(220f, bandHeight + 47f);
                gridRect.offsetMax = new Vector2(-40f, -contentTop);
            }
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
            if (!hubChromeBuilt)
            {
                BuildHubChrome();
                hubChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(true);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshHubUI();
        }

        // Header (crest, club name/byline, Simulate Season top-right) and the two-column
        // body (menu left, league table right), built once. The five reused buttons keep
        // their existing Editor wiring/onClick listeners - only their RectTransforms get
        // set here, via ManagerUITheme.SetPointAnchor, instead of being hand-dragged.
        private void BuildHubChrome()
        {
            if (seasonHubPanel == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(seasonHubPanel);

            // Left accent bar - this mockup uses a thin vertical edge bar instead of the
            // top/bottom bands used on Team Select/Matchday Prep/Squad/Player Detail.
            GameObject leftBar = new GameObject("LeftAccentBar", typeof(RectTransform), typeof(Image));
            leftBar.transform.SetParent(seasonHubPanel.transform, false);
            leftBar.transform.SetAsFirstSibling();
            RectTransform leftBarRect = leftBar.GetComponent<RectTransform>();
            leftBarRect.anchorMin = new Vector2(0f, 0f);
            leftBarRect.anchorMax = new Vector2(0f, 1f);
            leftBarRect.pivot = new Vector2(0f, 0.5f);
            leftBarRect.sizeDelta = new Vector2(6f, 0f);
            leftBarRect.anchoredPosition = Vector2.zero;
            leftBar.GetComponent<Image>().color = ManagerUITheme.Accent;

            // Crest badge - a colored initials badge, not the mockup's exact pentagon
            // shape (that needs real crest artwork or a custom mesh, neither of which
            // exist here).
            GameObject crest = new GameObject("CrestBadge", typeof(RectTransform), typeof(Image));
            crest.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(crest.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(52f, 52f));
            crest.GetComponent<Image>().color = ManagerUITheme.Accent;
            ManagerUITheme.BuildLabel(crest.transform, GetClubInitials(managedTeamName), 16, ManagerUITheme.OnAccent, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject nameObj = new GameObject("ClubName", typeof(RectTransform));
            nameObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(nameObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(98f, -24f), new Vector2(500f, 32f));
            hubClubNameLabel = ManagerUITheme.BuildLabel(nameObj.transform, "", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(98f, -58f), new Vector2(500f, 20f));
            hubBylineLabel = ManagerUITheme.BuildLabel(bylineObj.transform, "", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            if (simulateSeasonButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateSeasonButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-30f, -24f), new Vector2(200f, 36f));
                if (simulateSeasonButton.TryGetComponent(out Image simulateSeasonImage))
                {
                    simulateSeasonImage.color = ManagerUITheme.CardNeutral;
                }
                ManagerUITheme.NormalizeButtonLabel(simulateSeasonButton, "SIMULATE SEASON", ManagerUITheme.TextBody, 13);
            }

            // Left column (menu): Next Matchday / Squad / Transfers / Settings / Save & Exit.
            if (playNextMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(playNextMatchButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -130f), new Vector2(430f, 55f));
                if (playNextMatchButton.TryGetComponent(out Image playNextImage))
                {
                    playNextImage.color = ManagerUITheme.Accent;
                }
                ManagerUITheme.NormalizeButtonLabel(playNextMatchButton, "NEXT MATCHDAY", ManagerUITheme.OnAccent, 16);
            }

            if (viewSquadButton != null)
            {
                ManagerUITheme.SetPointAnchor(viewSquadButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -197f), new Vector2(430f, 50f));
                StyleHubActionButton(viewSquadButton);
                ManagerUITheme.NormalizeButtonLabel(viewSquadButton, "SQUAD", ManagerUITheme.TextBody, 15);
            }

            if (transfersButton != null)
            {
                ManagerUITheme.SetPointAnchor(transfersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -259f), new Vector2(430f, 50f));
                ManagerUITheme.SetDisabledPlaceholder(transfersButton, "TRANSFERS");
            }

            GameObject settingsObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(settingsObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -321f), new Vector2(430f, 50f));
            Button settingsButton = settingsObj.GetComponent<Button>();
            settingsButton.targetGraphic = settingsObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(settingsObj.transform, "SETTINGS", 15, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            ManagerUITheme.SetDisabledPlaceholder(settingsButton, "SETTINGS");

            if (exitToTitleButton != null)
            {
                // Anchored to the bottom of the panel (not the top, unlike the buttons
                // above) so it stays visible regardless of canvas height - the previous
                // y=-980 top-anchored offset assumed a canvas far taller than this one
                // actually renders at, pushing it off-screen entirely.
                ManagerUITheme.SetPointAnchor(exitToTitleButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(30f, 24f), new Vector2(430f, 44f));
                if (exitToTitleButton.TryGetComponent(out Image exitImage))
                {
                    exitImage.color = ManagerUITheme.PanelDark;
                }
                ManagerUITheme.NormalizeButtonLabel(exitToTitleButton, "SAVE & EXIT TO TITLE", ManagerUITheme.TextMuted, 14);
            }

            // Right column: league table caption. The Scroll View itself is an Editor
            // object (leagueTableView) - positioned to occupy the rest of this column.
            GameObject tableCaption = new GameObject("TableCaption", typeof(RectTransform));
            tableCaption.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(tableCaption.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(500f, -130f), new Vector2(430f, 22f));
            ManagerUITheme.BuildLabel(tableCaption.transform, "PREMIER LEAGUE · TABLE", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        }

        // First letter of each of the first two words (e.g. "Manchester City" -> "MC"),
        // or the first two letters of a single-word name. An approximation, not a real
        // club abbreviation - there's no crest artwork/data source to draw a real one from.
        private static string GetClubInitials(string clubName)
        {
            if (string.IsNullOrWhiteSpace(clubName))
            {
                return "?";
            }

            string[] words = clubName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length >= 2)
            {
                return $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
            }

            return clubName.Length >= 2 ? clubName.Substring(0, 2).ToUpperInvariant() : clubName.ToUpperInvariant();
        }

        private void RefreshHubUI()
        {
            if (hubClubNameLabel != null)
            {
                hubClubNameLabel.text = managedTeamName.ToUpperInvariant();
            }

            if (hubBylineLabel != null)
            {
                hubBylineLabel.text = $"Manager {managerName}   ·   Matchday {currentFixtureIndex + 1}";

                // Same family of TMP mesh-generation flakiness as the blank-label bug
                // (see RecoverBlankLabelNextFrame) - here it showed up as the OLD glyph
                // mesh not being cleared, so old and new text render overlapped on top
                // of each other (confirmed live, after the first matchday). Unlike the
                // blank case there's no reliable characterCount check to detect it, so
                // this just unconditionally destroys+recreates the label every refresh
                // rather than trying to detect the failure first - cheap, since this
                // only runs when returning to the Hub, not on a hot path.
                // Must stop any coroutine already in flight from a PREVIOUS RefreshHubUI
                // call first - without this, returning to the Hub again before the prior
                // one-frame wait elapsed (confirmed live to happen by matchday 5) started
                // a second recreate on top of the first, and the two destroy/recreate
                // cycles racing each other is exactly what produced the overlapping text,
                // not a one-off fluke.
                if (hubBylineRecreateCoroutine != null)
                {
                    StopCoroutine(hubBylineRecreateCoroutine);
                }

                hubBylineRecreateCoroutine = StartCoroutine(RecreateHubBylineLabelNextFrame());
            }

            bool hasNextFixture = currentFixtureIndex < managedTeamFixtures.Count;

            if (playNextMatchButton != null)
            {
                playNextMatchButton.interactable = hasNextFixture;
            }

            if (simulateSeasonButton != null)
            {
                simulateSeasonButton.interactable = hasNextFixture;
            }

            if (leagueTableView != null)
            {
                int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
                leagueTableView.Populate(playableTable.Sorted(), teamRegistry.GetTeamName, managedTeamId);
            }
        }

        // --- Squad: Tactics Board (pitch view, position-pinned starters, drag a bench
        // card onto a pin to substitute, switch formation from the header dropdown - no
        // Editor-placed panel to wire, built entirely in code the first time it's
        // opened, same precedent as Match Events). Replaces the old scrollview-based
        // squad browse entirely; playerListPanel/squadListView stay alive below for the
        // in-match sub picker only. ---

        public void OnViewSquadClicked()
        {
            if (!tacticsBoardChromeBuilt)
            {
                BuildTacticsBoardChrome();
                tacticsBoardChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);

            RefreshTacticsBoardUI();
        }

        public void OnTacticsBoardBackClicked()
        {
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            CloseTacticsBoardFormationDropdown();
            CleanupStrayDragGhosts();

            ShowSeasonHub();
        }

        // Belt-and-suspenders on top of TacticsBoardPlayerCard's own drag-cleanup fixes -
        // a drag ghost is parented directly to the root Canvas (so it can float above
        // everything while dragging), which means it survives a screen change even if
        // something upstream left it undestroyed. Cheap no-op when nothing's stray;
        // called on every way of leaving the Tactics Board so one can never linger onto
        // whatever screen comes next (confirmed live: a click firing mid-drag navigated
        // to Player Inspect with the ghost still floating on top of it).
        private void CleanupStrayDragGhosts()
        {
            if (tacticsBoardPanel == null)
            {
                return;
            }

            Transform canvasTransform = tacticsBoardPanel.transform.parent;

            if (canvasTransform == null)
            {
                return;
            }

            for (int i = canvasTransform.childCount - 1; i >= 0; i--)
            {
                Transform child = canvasTransform.GetChild(i);

                if (child.name == "DragGhost")
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void BuildTacticsBoardChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            // Shorter than other screens' header/footer bands deliberately - the pitch
            // needs as much of this 540-tall canvas as it can get. The mockup's own pin
            // percentages were authored against an ~600px-tall pitch region (960x820
            // panel); squeezed into what's left here even after shrinking these two,
            // pins still need a vertical compression factor - see BuildTacticsBoardPin.
            const float headerHeight = 64f;
            const float benchHeight = 96f;

            tacticsBoardPanel = new GameObject("TacticsBoardPanel", typeof(RectTransform));
            tacticsBoardPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = tacticsBoardPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(tacticsBoardPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(tacticsBoardPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(30f, -18f), new Vector2(300f, 32f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SQUAD", 22, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-30f, -16f), new Vector2(150f, 36f));
            backButton.onClick.AddListener(OnTacticsBoardBackClicked);

            tacticsBoardFormationButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "FORMATION", ManagerUITheme.CardNeutral, ManagerUITheme.TextPrimary, 14);
            ManagerUITheme.SetPointAnchor(tacticsBoardFormationButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-196f, -16f), new Vector2(200f, 36f));
            tacticsBoardFormationButton.onClick.AddListener(ToggleTacticsBoardFormationDropdown);

            // Pitch: flat rectangles for the halfway line/penalty boxes (no sprites in
            // this project, same convention as everywhere else) - without them the pins
            // are just numbers scattered on a plain rectangle, with nothing anchoring the
            // eye to "this is a football formation" or explaining why the goalkeeper
            // sits close behind the back line. Pin positions come from TacticsBoardLayout.
            GameObject pitchObj = new GameObject("Pitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(tacticsBoardPanel.transform, false);
            tacticsBoardPitchContainer = pitchObj.GetComponent<RectTransform>();

            // Constrained to a fixed aspect ratio (1130:700, matching design's Tactics
            // Board mockup) instead of stretching to fill the panel's full width - the
            // panel is far wider than a pitch region is tall, so a full-width pitch read
            // as a smeared rectangle with no visible formation shape. Width-fit against
            // the available height (the tighter dimension here), centered, leaving empty
            // margin on either side by design. Height budget/positioning unchanged from
            // before, so the pin vertical-compression factor in BuildTacticsBoardPin
            // still applies.
            const float pitchAspectRatio = 1130f / 700f;
            float availableWidth = panelRect.rect.width - 80f;
            float availableHeight = panelRect.rect.height - (headerHeight + 20f) - (benchHeight + 20f);
            float pitchWidth = Mathf.Min(availableWidth, availableHeight * pitchAspectRatio);
            float pitchHeight = pitchWidth / pitchAspectRatio;

            tacticsBoardPitchContainer.anchorMin = new Vector2(0.5f, 0f);
            tacticsBoardPitchContainer.anchorMax = new Vector2(0.5f, 0f);
            tacticsBoardPitchContainer.pivot = new Vector2(0.5f, 0f);
            tacticsBoardPitchContainer.anchoredPosition = new Vector2(0f, benchHeight + 20f);
            tacticsBoardPitchContainer.sizeDelta = new Vector2(pitchWidth, pitchHeight);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(tacticsBoardPitchContainer);

            // Positioned at benchHeight-12 (not -24) so its bottom edge clears the scroll
            // view's top edge (at y=16+64=80) with a few px of breathing room, instead of
            // overlapping it - confirmed live, the two were almost touching. The extra
            // headroom this eats into is the 20px gap already reserved between the bench
            // band and the pitch above it, so this doesn't touch pitch geometry.
            GameObject benchCaptionObj = new GameObject("BenchCaption", typeof(RectTransform));
            benchCaptionObj.transform.SetParent(tacticsBoardPanel.transform, false);
            ManagerUITheme.SetPointAnchor(benchCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(40f, benchHeight - 12f), new Vector2(600f, 20f));
            ManagerUITheme.BuildLabel(benchCaptionObj.transform, "BENCH · DRAG A PLAYER ONTO THE PITCH TO SUBSTITUTE", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Horizontal scroll row: same ScrollRect+Viewport+Content pattern as the
            // vertical lists elsewhere (SquadListView/MatchEventsListContainer), just
            // rotated - HorizontalLayoutGroup instead of Vertical, horizontal scroll only.
            GameObject scrollViewObj = new GameObject("BenchScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 0f);
            scrollViewRect.pivot = new Vector2(0.5f, 0f);
            scrollViewRect.anchoredPosition = new Vector2(0f, 16f);
            scrollViewRect.sizeDelta = new Vector2(-80f, 64f);

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            tacticsBoardBenchContent = contentObj.GetComponent<RectTransform>();
            tacticsBoardBenchContent.anchorMin = new Vector2(0f, 0.5f);
            tacticsBoardBenchContent.anchorMax = new Vector2(0f, 0.5f);
            tacticsBoardBenchContent.pivot = new Vector2(0f, 0.5f);
            tacticsBoardBenchContent.anchoredPosition = Vector2.zero;
            // Height must be explicit, not zero - childForceExpandHeight below stretches
            // every card to fill THIS rect's own height, so a zero-height Content
            // silently squashed every bench card to zero height too (invisible despite
            // existing, with correct width/position - confirmed live).
            tacticsBoardBenchContent.sizeDelta = new Vector2(0f, 56f);

            HorizontalLayoutGroup layoutGroup = contentObj.AddComponent<HorizontalLayoutGroup>();
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.spacing = 10f;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.viewport = viewportRect;
            scrollRect.content = tacticsBoardBenchContent;

            // Slim scrollbar in the 16px gap below the card row - the bench row itself was
            // already a working horizontal ScrollRect (drag or mouse-wheel scrolls it,
            // confirmed live), but with more bench players than fit in one screen's width
            // and no visible affordance, it read as broken/missing subs rather than "there's
            // more, scroll for it". This is purely a discoverability fix, not a functional one.
            GameObject scrollbarObj = new GameObject("BenchScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(0f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 0f);
            scrollbarRect.pivot = new Vector2(0.5f, 0f);
            scrollbarRect.anchoredPosition = new Vector2(0f, 4f);
            scrollbarRect.sizeDelta = new Vector2(-80f, 6f);
            scrollbarObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject handleAreaObj = new GameObject("SlidingArea", typeof(RectTransform));
            handleAreaObj.transform.SetParent(scrollbarObj.transform, false);
            RectTransform handleAreaRect = handleAreaObj.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObj.transform.SetParent(handleAreaObj.transform, false);
            RectTransform handleRect = handleObj.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = new Vector2(0.3f, 1f);
            // Must be zeroed explicitly - a fresh RectTransform's default sizeDelta is
            // (100,100), which under stretched anchors ADDS 100px to the computed size
            // rather than being ignored, blowing the handle up far past this 6px-tall bar
            // (confirmed live: rendered as a huge block covering the bench row).
            handleRect.sizeDelta = Vector2.zero;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.horizontalScrollbar = scrollbar;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            BuildTacticsBoardFormationDropdown();
        }

        // Halfway line + both penalty boxes, all built from thin flat-color rectangles
        // (no sprite/mesh assets in this project - a true circle for the center circle
        // isn't practical the same way, so it's skipped; the boxes alone are enough to
        // read as "this is a pitch" and to explain why the goalkeeper sits close behind
        // the back line). Static per formation - built once, not part of the per-refresh
        // pin rebuild.
        private void BuildPitchMarkings(RectTransform pitch)
        {
            Color lineColor = new Color(1f, 1f, 1f, 0.10f);

            GameObject halfwayLine = new GameObject("HalfwayLine", typeof(RectTransform), typeof(Image));
            halfwayLine.transform.SetParent(pitch, false);
            RectTransform halfwayRect = halfwayLine.GetComponent<RectTransform>();
            halfwayRect.anchorMin = new Vector2(0f, 0.5f);
            halfwayRect.anchorMax = new Vector2(1f, 0.5f);
            halfwayRect.pivot = new Vector2(0.5f, 0.5f);
            halfwayRect.anchoredPosition = Vector2.zero;
            halfwayRect.sizeDelta = new Vector2(0f, 1f);
            halfwayLine.GetComponent<Image>().color = lineColor;

            BuildPenaltyBox(pitch, atTop: true, lineColor);
            BuildPenaltyBox(pitch, atTop: false, lineColor);
        }

        // An open-ended rectangle (three sides, no side facing the halfway line) built
        // from three thin Image strips - top/bottom edge plus two verticals, same
        // "no sprites, flat rectangles" approach as everywhere else.
        private void BuildPenaltyBox(RectTransform pitch, bool atTop, Color lineColor)
        {
            const float boxWidthPercent = 0.30f;
            const float boxDepthPercent = 0.16f;

            float edgeY = atTop ? 1f : 0f;
            float innerY = atTop ? 1f - boxDepthPercent : boxDepthPercent;

            GameObject edgeLine = new GameObject(atTop ? "TopBoxLine" : "BottomBoxLine", typeof(RectTransform), typeof(Image));
            edgeLine.transform.SetParent(pitch, false);
            RectTransform edgeRect = edgeLine.GetComponent<RectTransform>();
            edgeRect.anchorMin = new Vector2(0.5f - boxWidthPercent, innerY);
            edgeRect.anchorMax = new Vector2(0.5f + boxWidthPercent, innerY);
            edgeRect.pivot = new Vector2(0.5f, 0.5f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = new Vector2(0f, 1f);
            edgeLine.GetComponent<Image>().color = lineColor;

            foreach (float xPercent in new[] { 0.5f - boxWidthPercent, 0.5f + boxWidthPercent })
            {
                GameObject sideLine = new GameObject(atTop ? "TopBoxSide" : "BottomBoxSide", typeof(RectTransform), typeof(Image));
                sideLine.transform.SetParent(pitch, false);
                RectTransform sideRect = sideLine.GetComponent<RectTransform>();
                sideRect.anchorMin = new Vector2(xPercent, Mathf.Min(edgeY, innerY));
                sideRect.anchorMax = new Vector2(xPercent, Mathf.Max(edgeY, innerY));
                sideRect.pivot = new Vector2(0.5f, 0.5f);
                sideRect.anchoredPosition = Vector2.zero;
                sideRect.sizeDelta = new Vector2(1f, 0f);
                sideLine.GetComponent<Image>().color = lineColor;
            }
        }

        private void BuildTacticsBoardFormationDropdown()
        {
            tacticsBoardFormationDropdown = new GameObject("FormationDropdown", typeof(RectTransform), typeof(Image));
            tacticsBoardFormationDropdown.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform dropdownRect = tacticsBoardFormationDropdown.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(1f, 1f);
            dropdownRect.anchorMax = new Vector2(1f, 1f);
            dropdownRect.pivot = new Vector2(1f, 1f);
            dropdownRect.anchoredPosition = new Vector2(-30f, -58f);
            dropdownRect.sizeDelta = new Vector2(200f, 6 * 34f);
            tacticsBoardFormationDropdown.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            VerticalLayoutGroup layoutGroup = tacticsBoardFormationDropdown.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;

            Formation[] formations =
            {
                Formation.FourThreeThree, Formation.FourTwoThreeOne, Formation.FourFourTwo,
                Formation.ThreeFiveTwo, Formation.ThreeFourThree, Formation.ThreeFourTwoOne
            };

            foreach (Formation formation in formations)
            {
                Button optionButton = ManagerUITheme.BuildButton(tacticsBoardFormationDropdown.transform, TacticsBoardLayout.FormatFormation(formation), ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                optionButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
                optionButton.onClick.AddListener(() => OnFormationSelected(formation));
            }

            tacticsBoardFormationDropdown.SetActive(false);
        }

        private void ToggleTacticsBoardFormationDropdown()
        {
            if (tacticsBoardFormationDropdown != null)
            {
                tacticsBoardFormationDropdown.SetActive(!tacticsBoardFormationDropdown.activeSelf);
            }
        }

        private void CloseTacticsBoardFormationDropdown()
        {
            if (tacticsBoardFormationDropdown != null) tacticsBoardFormationDropdown.SetActive(false);
        }

        // Greedy best-fit reassignment: for each slot in the new formation (in order),
        // pick the best remaining player from the full squad by
        // GetOverallRating() * GetPositionFit(slot) - a CB played at CB scores at full
        // rating, the same CB pressed into an ST slot scores at 60% of it. Applies
        // instantly, same immediacy as the drag-substitute mechanic on this same screen.
        private void OnFormationSelected(Formation formation)
        {
            CloseTacticsBoardFormationDropdown();

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (team.Formation == formation)
            {
                return;
            }

            List<PlayerPosition> newSlots = squadGenerator.GetStartingPositions(formation);
            List<PlayerAgent> pool = new List<PlayerAgent>(team.Players);
            List<PlayerAgent> newStartingEleven = new List<PlayerAgent>();

            foreach (PlayerPosition slot in newSlots)
            {
                PlayerAgent best = null;
                float bestScore = float.MinValue;

                foreach (PlayerAgent candidate in pool)
                {
                    float score = candidate.GetOverallRating() * candidate.GetPositionFit(slot);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    break;
                }

                newStartingEleven.Add(best);
                pool.Remove(best);
            }

            team.ChangeFormation(formation, newStartingEleven);
            RefreshTacticsBoardUI();
        }

        private void RefreshTacticsBoardUI()
        {
            if (tacticsBoardPitchContainer == null || tacticsBoardBenchContent == null)
            {
                return;
            }

            CloseTacticsBoardFormationDropdown();

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            // Only clear pins, not the pitch markings built once in BuildPitchMarkings -
            // those are siblings in the same container and would otherwise get destroyed
            // right along with the pins on the very first refresh (confirmed live: the
            // pitch had zero marking children left after OnViewSquadClicked's own first
            // RefreshTacticsBoardUI call).
            for (int i = tacticsBoardPitchContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = tacticsBoardPitchContainer.GetChild(i);

                if (child.name.StartsWith("Pin_"))
                {
                    Destroy(child.gameObject);
                }
            }

            foreach (Transform child in tacticsBoardBenchContent)
            {
                Destroy(child.gameObject);
            }

            if (tacticsBoardFormationButton != null)
            {
                // "v" not the mockup's ▾ glyph - Oswald has no symbol glyphs at all (same
                // reason "·" replaced the design's bullet/star/emoji elsewhere).
                ManagerUITheme.NormalizeButtonLabel(tacticsBoardFormationButton, $"Formation: {TacticsBoardLayout.FormatFormation(team.Formation)} v", ManagerUITheme.TextPrimary, 14);
            }

            IReadOnlyList<Vector2> pins = TacticsBoardLayout.GetPins(team.Formation);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            for (int i = 0; i < team.StartingEleven.Count && i < pins.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slotPosition = i < slots.Count ? slots[i] : player.PrimaryPosition;
                BuildTacticsBoardPin(player, slotPosition, pins[i]);
            }

            foreach (PlayerAgent player in team.Bench)
            {
                BuildTacticsBoardBenchCard(player);
            }
        }

        private void BuildTacticsBoardPin(PlayerAgent player, PlayerPosition slotPosition, Vector2 pinPercent)
        {
            GameObject pinObj = new GameObject($"Pin_{player.Name}", typeof(RectTransform), typeof(Image));
            pinObj.transform.SetParent(tacticsBoardPitchContainer, false);

            RectTransform pinRect = pinObj.GetComponent<RectTransform>();

            // Compresses pinPercent.y toward the vertical center before mapping to an
            // anchor. The mockup's pin percentages were authored against a pitch region
            // roughly twice as tall as the one this 540-tall canvas has room for once
            // the header/bench bands are accounted for - used verbatim, formations with
            // a player stacked close behind another on the same flank (e.g. a back-three
            // formation's GK sitting right above its central CB) visibly overlap here,
            // even though they had enough room in the source design's taller pitch.
            // Raised from 0.66 - that value still left GK/CB pins overlapping in
            // back-three formations (confirmed live), and there's more headroom to work
            // with than 0.66 assumed (checked against every formation's most extreme pin
            // percentages, 0.85 keeps all of them comfortably inside the pitch box).
            const float verticalCompression = 0.85f;
            float compressedTopPercent = 0.5f + (pinPercent.y - 0.5f) * verticalCompression;

            Vector2 anchor = new Vector2(pinPercent.x, 1f - compressedTopPercent);
            pinRect.anchorMin = anchor;
            pinRect.anchorMax = anchor;
            pinRect.pivot = new Vector2(0.5f, 0.5f);
            pinRect.anchoredPosition = Vector2.zero;
            pinRect.sizeDelta = new Vector2(74f, 44f);

            // Transparent - exists only so the pin has a Graphic to raycast against
            // (IDropHandler needs one), not for the visible badge below.
            pinObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            // Two-layer "border" (accent square behind, dark square inset on top) -
            // a stand-in for the mockup's colored circle ring, since true circles
            // need a sprite this project doesn't have (same flat-rectangles-only
            // constraint as the pitch markings above).
            GameObject badgeBorderObj = new GameObject("BadgeBorder", typeof(RectTransform), typeof(Image));
            badgeBorderObj.transform.SetParent(pinObj.transform, false);
            RectTransform badgeBorderRect = badgeBorderObj.GetComponent<RectTransform>();
            badgeBorderRect.anchorMin = new Vector2(0.5f, 1f);
            badgeBorderRect.anchorMax = new Vector2(0.5f, 1f);
            badgeBorderRect.pivot = new Vector2(0.5f, 1f);
            badgeBorderRect.anchoredPosition = Vector2.zero;
            badgeBorderRect.sizeDelta = new Vector2(30f, 30f);
            badgeBorderObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            GameObject badgeObj = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeObj.transform.SetParent(badgeBorderObj.transform, false);
            RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.zero;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.offsetMin = new Vector2(2f, 2f);
            badgeRect.offsetMax = new Vector2(-2f, -2f);
            badgeObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;
            ManagerUITheme.BuildLabel(badgeObj.transform, GetDisplayRating(player.GetOverallRating()).ToString(), 10, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(pinObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(130f, 14f);
            ManagerUITheme.BuildLabel(labelObj.transform, $"{player.Name} · {slotPosition}", 8, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center);

            TacticsBoardPlayerCard card = pinObj.AddComponent<TacticsBoardPlayerCard>();
            card.Configure(player, isDraggable: false, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin);
        }

        private void BuildTacticsBoardBenchCard(PlayerAgent player)
        {
            GameObject cardObj = new GameObject($"Bench_{player.Name}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cardObj.transform.SetParent(tacticsBoardBenchContent, false);

            LayoutElement layoutElement = cardObj.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 150f;
            layoutElement.preferredHeight = 46f;

            cardObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject nameObj = new GameObject("Name", typeof(RectTransform));
            nameObj.transform.SetParent(cardObj.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(10f, 0f);
            nameRect.offsetMax = new Vector2(-10f, -2f);
            ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 13, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject ovrObj = new GameObject("OVR", typeof(RectTransform));
            ovrObj.transform.SetParent(cardObj.transform, false);
            RectTransform ovrRect = ovrObj.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(0f, 0.5f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.offsetMin = new Vector2(10f, 0f);
            ovrRect.offsetMax = new Vector2(-10f, -2f);
            ManagerUITheme.BuildLabel(ovrObj.transform, GetDisplayRating(player.GetOverallRating()).ToString(), 13, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject posObj = new GameObject("Position", typeof(RectTransform));
            posObj.transform.SetParent(cardObj.transform, false);
            RectTransform posRect = posObj.GetComponent<RectTransform>();
            posRect.anchorMin = new Vector2(0f, 0f);
            posRect.anchorMax = new Vector2(1f, 0.5f);
            posRect.offsetMin = new Vector2(10f, 2f);
            posRect.offsetMax = new Vector2(-10f, 0f);
            ManagerUITheme.BuildLabel(posObj.transform, player.PrimaryPosition.ToString(), 11, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            TacticsBoardPlayerCard card = cardObj.AddComponent<TacticsBoardPlayerCard>();
            card.Configure(player, isDraggable: true, isDropTarget: false, OnTacticsBoardPlayerTapped, null);
        }

        private void OnTacticsBoardPlayerTapped(PlayerAgent player)
        {
            playerInspectReturnsToTacticsBoard = true;
            OpenPlayerInspect(player);
        }

        private void OnBenchPlayerDroppedOnPin(PlayerAgent benchPlayer, PlayerAgent pinPlayer)
        {
            if (benchPlayer == pinPlayer)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            team.SubstitutePlayer(pinPlayer, benchPlayer);

            RefreshTacticsBoardUI();
        }

        private static float GetRatingPercent(PlayerAgent player)
        {
            return GetDisplayRating(player.GetOverallRating()) / 99f;
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
            else
            {
                ShowSeasonHub();
            }
        }

        // --- Substitutions: pre-match subs happen on the Tactics Board (drag a bench
        // card onto a pin - see OnBenchPlayerDroppedOnPin), unlimited, same as real
        // football before kickoff. This off-then-on picker flow exists only for the
        // in-match case now, which IS capped (see OnMakeSubDuringMatchClicked). Both
        // ultimately mutate the same persistent AgentTeam instance for managedTeamName,
        // so a sub made either way carries forward into future matches unless changed
        // again. ---

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

            ClosePlayerListPanel();

            if (subFlowIsInMatch)
            {
                // Only in-match subs count against the 5-per-match cap - this branch is
                // the only place subsUsedThisMatch increments.
                if (pendingSubApplied)
                {
                    subsUsedThisMatch++;
                }

                subFlowIsInMatch = false;

                if (matchdayPanel != null) matchdayPanel.SetActive(true);
                if (makeSubButton != null) makeSubButton.interactable = subsUsedThisMatch < MaxSubsPerMatch;
                RefreshMatchSubsStatus();

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

        // --- Player Inspect (Prev/Next once inside; entry point jumps straight to a
        // specific player from the squad browse list - no standalone Hub entry point) ---

        private void OpenPlayerInspect(PlayerAgent preselected)
        {
            CleanupStrayDragGhosts();

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            inspectSquadPlayers = new List<PlayerAgent>(team.StartingEleven);
            inspectSquadPlayers.AddRange(team.Bench);

            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            int preselectedIndex = preselected != null ? inspectSquadPlayers.IndexOf(preselected) : -1;
            inspectPlayerIndex = preselectedIndex >= 0 ? preselectedIndex : 0;

            if (!playerInspectChromeBuilt)
            {
                BuildPlayerInspectChrome();
                playerInspectChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            if (playerInspectPanel != null) playerInspectPanel.SetActive(true);

            RefreshPlayerInspectUI();
        }

        // Footer band + the three nav buttons (Prev/Next/Back), which were only ever
        // wired to click handlers, never positioned or styled - same "wired but
        // untouched" gap as TransfersButton/ExitToTitleButton originally were.
        private void BuildPlayerInspectChrome()
        {
            if (playerInspectPanel == null)
            {
                return;
            }

            const float footerHeight = 90f;
            ManagerUITheme.BuildAccentBand(playerInspectPanel.transform, topBand: false, height: footerHeight);

            // Positioned relative to the panel itself (not reparented into the footer
            // band). SetPointAnchor forces pivot == anchor, and these use a bottom
            // anchor (y=0), so the Y offset here is the button's BOTTOM edge, not its
            // center - true vertical centering needs (footerHeight - buttonHeight) / 2,
            // not footerHeight / 2 (that was the earlier bug: it centered as if pivot.y
            // were 0.5, pushing every button up and out the top of the band).
            const float navButtonHeight = 48f;
            float navButtonY = (footerHeight - navButtonHeight) / 2f;

            if (inspectBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-30f, navButtonY), new Vector2(220f, navButtonHeight));
                if (inspectBackButton.TryGetComponent(out Image backImage))
                {
                    backImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectBackButton, "BACK TO SQUAD", ManagerUITheme.TextBody, 15);
            }

            if (inspectPreviousButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectPreviousButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(30f, navButtonY), new Vector2(140f, navButtonHeight));
                if (inspectPreviousButton.TryGetComponent(out Image prevImage))
                {
                    prevImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectPreviousButton, "< PREV", ManagerUITheme.TextBody, 14);
            }

            if (inspectNextButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectNextButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(186f, navButtonY), new Vector2(140f, navButtonHeight));
                if (inspectNextButton.TryGetComponent(out Image nextImage))
                {
                    nextImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectNextButton, "NEXT >", ManagerUITheme.TextBody, 13);
            }
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

            if (playerInspectReturnsToTacticsBoard)
            {
                playerInspectReturnsToTacticsBoard = false;
                OnViewSquadClicked();
            }
            else
            {
                ShowSeasonHub();
            }
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
            string metaText = $"{player.Role}  ·  Weak Foot: {BuildFootRating(player.WeakFoot)}  ·  Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})";
            TextMeshProUGUI metaTMP = ManagerUITheme.BuildLabel(metaLabel.transform, metaText, 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            if (weakFootStarSpriteAsset != null) metaTMP.spriteAsset = weakFootStarSpriteAsset;

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
            spawnedInspectElements.Add(attributeGrid);

            // Full stretch down to the footer (not a fixed height) - the old fixed 220px
            // height left most of the panel as dead empty space below the grid.
            RectTransform attributeGridRect = attributeGrid.GetComponent<RectTransform>();
            attributeGridRect.anchorMin = new Vector2(0f, 0f);
            attributeGridRect.anchorMax = new Vector2(1f, 1f);
            attributeGridRect.offsetMin = new Vector2(20f, 110f);
            attributeGridRect.offsetMax = new Vector2(-20f, -150f);

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

            // Player Inspect fully destroys and rebuilds every label on every refresh
            // (see spawnedInspectElements above) - that rapid churn turns out to trigger
            // the same TMP mesh-generation failure the Title wordmark hit once (see
            // RecoverBlankLabelNextFrame): a label with correct text/color/position but
            // characterCount stuck at 0 forever, invisible despite everything else about
            // it checking out. Confirmed live on OvrValue (the big number next to
            // "OVERALL (GK)") - blank on screen, structurally perfect otherwise. This is
            // a general sweep rather than a fix targeted at that one label, since nothing
            // about the failure is specific to it.
            StartCoroutine(RecoverBlankLabelsNextFrame(playerInspectContentContainer));
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
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(0.8f, 1f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(nameText.transform, label, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            GameObject valueText = new GameObject("Value", typeof(RectTransform));
            valueText.transform.SetParent(labelRow.transform, false);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.8f, 0f);
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(valueText.transform, Mathf.RoundToInt(value).ToString(), 13, ManagerUITheme.RatingColor(value), TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject barRow = new GameObject($"AttrBar_{label}", typeof(RectTransform));
            barRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(barRow, topOffset + 18f, 5f);
            ManagerUITheme.BuildBar(barRow.transform, value / 100f, ManagerUITheme.RatingColor(value), 5f);

            return topOffset + 34f;
        }

        // Weak foot uses a star rating rather than a raw number - unlike the attribute
        // rows above (which do show their numeric value), weak foot is intentionally
        // kept as a qualitative 1-5 rating instead.
        // Star icons rather than the old "|||--" ASCII bars - relies on the caller
        // assigning weakFootStarSpriteAsset to the label's spriteAsset (star-empty is
        // wired as its fallback, so both glyphs resolve from a single sprite tag).
        private static string BuildFootRating(float rawValue)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(rawValue / 20f), 1, 5);
            const string filledTag = "<sprite name=\"star-filled\">";
            const string emptyTag = "<sprite name=\"star-empty\">";

            // <sprite> has no "size" attribute in TMP - an earlier attempt at
            // size=60% directly on the tag silently failed to parse and printed the
            // tag text literally (confirmed live). <size=X%>...</size> is the
            // real, documented way to scale inline content.
            // <voffset> nudges the sprite block down to sit on the surrounding text's
            // baseline instead of its own glyph-metric default (confirmed live: without
            // it the stars sat high and crowded right up against "Weak Foot:" with no
            // visible gap despite the space already in the caller's format string).
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(" <voffset=-0.15em><size=60%>");
            for (int i = 0; i < filled; i++) sb.Append(filledTag);
            for (int i = filled; i < 5; i++) sb.Append(emptyTag);
            sb.Append("</size></voffset>");
            return sb.ToString();
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

            // Tactic pills belong to live Match Day now, not scouting - but they're the
            // same shared Button instances Match Day reparents into its own footer, and
            // that reparenting only happens lazily the first time BuildMatchdayChrome runs
            // (first Simulate Match click). Until then they're still sitting wherever they
            // started (originally hand-placed under MatchdayPrepPanel), so explicitly hide
            // them here rather than relying on that lazy reparent to have already happened.
            if (attackingButton != null) attackingButton.gameObject.SetActive(false);
            if (balancedButton != null) balancedButton.gameObject.SetActive(false);
            if (defensiveButton != null) defensiveButton.gameObject.SetActive(false);

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

            // Footer action pair, right-aligned per the design mockup ("Back to Hub" /
            // "Simulate Match ->"). These two were never positioned - both sat stacked at
            // (0,0), so the unstyled Back button (still showing its default Editor label)
            // rendered on top of and completely hid the correctly-styled Simulate Match button.
            if (simulateMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateMatchButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-40f, 20f), new Vector2(220f, 50f));
            }

            if (matchdayPrepBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(matchdayPrepBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-272f, 20f), new Vector2(170f, 50f));
            }

            // Tactic and Substitutions moved to the live Match Day screen (see
            // BuildMatchdayChrome) - Matchday Prep is scouting-only now, so the opponent
            // squad list gets the full width instead of sharing it with a right column.
            if (opponentSquadListView != null)
            {
                RectTransform opponentListRect = opponentSquadListView.GetComponent<RectTransform>();
                opponentListRect.anchorMin = new Vector2(0f, 0f);
                opponentListRect.anchorMax = new Vector2(1f, 1f);
                opponentListRect.offsetMin = new Vector2(40f, bandHeight + 24f);
                opponentListRect.offsetMax = new Vector2(-40f, -(bandHeight + 24f));
            }
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
                matchdayPrepSubtitleLabel.text = $"Matchday {currentFixture.Matchday}   ·   Opponent Formation: {TacticsBoardLayout.FormatFormation(opponentTeam.Formation)}";
            }

            // Opponent scouting list is retired here - once the tactics board lands, this
            // screen will show the opposition's formation on the board itself instead of a
            // flat Starting XI/Bench list, so there's no point populating (or fixing the
            // layout of) a list that's about to be replaced.
            if (opponentSquadListView != null)
            {
                opponentSquadListView.Clear();
                opponentSquadListView.gameObject.SetActive(false);
            }

        }

        public void OnMatchdayPrepBackClicked()
        {
            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Header (toolbar + score flanked by team names), footer (tactic readout +
        // Continue), and the Key Moments/Match Stats body columns - built once. The old
        // fixtureTitleText/matchStatsText free-text fields are retired in favor of this
        // (kept assigned in the Inspector, just hidden - see HeaderText/etc. precedent
        // on the Hub). No possession stat: this project never implemented one (no real
        // data source for it - see HANDOFF.md), so unlike the design mockup, only shots
        // (a real tracked stat) get a bar here.
        private void BuildMatchdayChrome()
        {
            if (matchdayPanel == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(matchdayPanel);

            const float headerHeight = 110f;
            const float footerHeight = 90f;

            ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: true, height: headerHeight);
            GameObject footerBand = ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: false, height: footerHeight);

            if (fixtureTitleText != null) fixtureTitleText.gameObject.SetActive(false);
            if (matchStatsText != null) matchStatsText.gameObject.SetActive(false);

            // --- Toolbar: Skip to Results (existing, repositioned) / Pause ---
            // No more "Tactics / Subs" placeholder here - real, working Tactic pills and
            // a Substitutions section are now directly on this screen (see below), so a
            // separate disabled button pointing at the same functionality would just be
            // redundant/confusing.
            if (skipToResultsButton != null)
            {
                ManagerUITheme.SetPointAnchor(skipToResultsButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-30f, -14f), new Vector2(150f, 30f));
                StyleHubActionButton(skipToResultsButton);
                ManagerUITheme.NormalizeButtonLabel(skipToResultsButton, "SKIP TO RESULTS", ManagerUITheme.TextBody, 12);
            }

            pauseButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "PAUSE", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-188f, -14f), new Vector2(90f, 30f));
            pauseButton.onClick.AddListener(OnPauseClicked);

            // --- Score row: team names flank a centered score + minute/LIVE tag ---
            // Moved up from -64 (and team names from -72, scorers from -108 below) -
            // the score/team-name row was crowding the very top of the panel while the
            // full-time scorer row right below it was crammed almost flush against the
            // header's bottom divider, with barely any gap between the two (confirmed
            // live). This frees up room right below the divider for the scorer row.
            if (scoreText != null)
            {
                ManagerUITheme.SetPointAnchor(scoreText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(220f, 44f));
                scoreText.fontSize = 32;
                scoreText.alignment = TextAlignmentOptions.Center;
                scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            }

            if (clockText != null)
            {
                ManagerUITheme.SetPointAnchor(clockText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(220f, 20f));
                clockText.alignment = TextAlignmentOptions.Center;
                clockText.fontSize = 13;
            }

            GameObject homeNameObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform homeNameRect = homeNameObj.GetComponent<RectTransform>();
            homeNameRect.anchorMin = new Vector2(0.5f, 1f);
            homeNameRect.anchorMax = new Vector2(0.5f, 1f);
            homeNameRect.pivot = new Vector2(1f, 1f);
            homeNameRect.anchoredPosition = new Vector2(-120f, -64f);
            homeNameRect.sizeDelta = new Vector2(260f, 32f);
            matchHomeNameLabel = ManagerUITheme.BuildLabel(homeNameObj.transform, "", 18, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayNameObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform awayNameRect = awayNameObj.GetComponent<RectTransform>();
            awayNameRect.anchorMin = new Vector2(0.5f, 1f);
            awayNameRect.anchorMax = new Vector2(0.5f, 1f);
            awayNameRect.pivot = new Vector2(0f, 1f);
            awayNameRect.anchoredPosition = new Vector2(120f, -64f);
            awayNameRect.sizeDelta = new Vector2(260f, 32f);
            matchAwayNameLabel = ManagerUITheme.BuildLabel(awayNameObj.transform, "", 18, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject fullTimeCaptionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            fullTimeCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(fullTimeCaptionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 20f));
            matchFullTimeCaptionLabel = ManagerUITheme.BuildLabel(fullTimeCaptionObj.transform, "FULL TIME", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            matchFullTimeCaptionGroup = fullTimeCaptionObj;
            matchFullTimeCaptionGroup.SetActive(false);

            // Goal-scorer lists, flanking the score under the team names - full-time only,
            // built from the real ScorerName on each goal event (see AgentMatchSimulator),
            // not fabricated or parsed out of the free-text event description.
            // Width (220) is deliberately less than double the offset (120) so the two
            // boxes' inner edges can't cross at center - at the old 260 width they
            // overlapped by 20px there regardless of text, invisible with short scorer
            // names/few goals but a real collision once a name or scorer count pushed
            // right up against that boundary (confirmed live).
            // Y moved from -108 to -128 - at -108 the text (top-aligned within this box)
            // rendered right on top of the header's bottom divider with no visible gap
            // (confirmed live); the header band itself isn't a mask, so this box is free
            // to sit further down past the divider into the body's dark background.
            GameObject homeScorersObj = new GameObject("HomeScorers", typeof(RectTransform));
            homeScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(homeScorersObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(-120f, -128f), new Vector2(220f, 44f));
            matchHomeScorersLabel = ManagerUITheme.BuildLabel(homeScorersObj.transform, "", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.TopRight, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchHomeScorersLabel.spriteAsset = footballIconSpriteAsset;
            // A one-sided scoreline (hat-tricks etc.) can need more lines than this box's
            // fixed 44px height allows - autosizing shrinks the font to fit rather than
            // overflowing into whatever sits below (Substitutions/Match Stats).
            matchHomeScorersLabel.enableAutoSizing = true;
            matchHomeScorersLabel.fontSizeMin = 7;
            matchHomeScorersLabel.fontSizeMax = 12;

            GameObject awayScorersObj = new GameObject("AwayScorers", typeof(RectTransform));
            awayScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(awayScorersObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(120f, -128f), new Vector2(220f, 44f));
            matchAwayScorersLabel = ManagerUITheme.BuildLabel(awayScorersObj.transform, "", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchAwayScorersLabel.spriteAsset = footballIconSpriteAsset;
            matchAwayScorersLabel.enableAutoSizing = true;
            matchAwayScorersLabel.fontSizeMin = 7;
            matchAwayScorersLabel.fontSizeMax = 12;

            matchFullTimeOnlyElements = new List<GameObject> { homeScorersObj, awayScorersObj };
            homeScorersObj.SetActive(false);
            awayScorersObj.SetActive(false);

            matchLiveOnlyElements = new[] { pauseButton.gameObject, skipToResultsButton != null ? skipToResultsButton.gameObject : null, clockText != null ? clockText.gameObject : null };

            // --- Body: Key Moments (left) / Match Stats (right) ---
            GameObject keyMomentsCaptionObj = new GameObject("MatchLogCaption", typeof(RectTransform));
            keyMomentsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            matchKeyMomentsCaptionRect = keyMomentsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(matchKeyMomentsCaptionRect, new Vector2(0f, 1f), new Vector2(40f, -(headerHeight + 28f)), new Vector2(400f, 20f));
            ManagerUITheme.BuildLabel(keyMomentsCaptionObj.transform, "MATCH LOG", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            if (eventFeedText != null)
            {
                // RectMask2D only clips CHILDREN of the GameObject it's on, not a
                // Graphic on that same object - putting it directly on eventFeedText
                // (as a previous pass here did) clips nothing. It needs to be a real
                // parent, with eventFeedText reparented inside it and stretched to fill.
                GameObject maskObj = new GameObject("EventFeedMask", typeof(RectTransform), typeof(RectMask2D));
                maskObj.transform.SetParent(matchdayPanel.transform, false);
                RectTransform maskRect = maskObj.GetComponent<RectTransform>();
                maskRect.anchorMin = new Vector2(0f, 0f);
                maskRect.anchorMax = new Vector2(0.55f, 1f);
                maskRect.offsetMin = new Vector2(40f, footerHeight + 24f);
                maskRect.offsetMax = new Vector2(-20f, -(headerHeight + 56f));

                eventFeedText.transform.SetParent(maskRect, false);
                RectTransform eventRect = eventFeedText.GetComponent<RectTransform>();
                eventRect.anchorMin = Vector2.zero;
                eventRect.anchorMax = Vector2.one;
                eventRect.offsetMin = Vector2.zero;
                eventRect.offsetMax = Vector2.zero;
                eventFeedText.fontSize = 15;

                // Hidden entirely at full-time - the design moves the full event list to
                // its own separate "Match Events" screen instead of showing it inline here.
                matchLogGroup = maskObj;
            }

            viewMatchEventsButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "VIEW MATCH EVENTS", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            viewMatchEventsButton.onClick.AddListener(OnViewMatchEventsClicked);
            viewMatchEventsButton.gameObject.SetActive(false);
            matchFullTimeOnlyElements.Add(viewMatchEventsButton.gameObject);

            // --- Right column: Substitutions (top) then Match Stats (below) ---
            // SetPointAnchor always sets pivot == anchor, so anchor.x=0.55 (meant as a
            // left-edge reference point for this column) was also making these elements'
            // own pivot sit 55% across THEMSELVES - not left-aligned at that point at
            // all, but straddling the panel center either side of it (which is exactly
            // what looked like everything "floating in the middle"/illegible overlap).
            // Explicit pivot.x=0 after each call fixes it: anchor point stays at the
            // column's left edge, and the element now actually starts there.
            GameObject subsCaptionObj = new GameObject("SubstitutionsCaption", typeof(RectTransform));
            subsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsCaptionRect = subsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsCaptionRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 28f)), new Vector2(360f, 20f));
            subsCaptionRect.pivot = new Vector2(0f, 1f);
            ManagerUITheme.BuildLabel(subsCaptionObj.transform, "SUBSTITUTIONS", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subsStatusObj = new GameObject("SubsStatus", typeof(RectTransform));
            subsStatusObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsStatusRect = subsStatusObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsStatusRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 54f)), new Vector2(360f, 22f));
            subsStatusRect.pivot = new Vector2(0f, 1f);
            matchSubsStatusLabel = ManagerUITheme.BuildLabel(subsStatusObj.transform, "", 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            // Reuses the existing, already-working in-match sub picker (OnMakeSubDuringMatchClicked)
            // as its click handler - restyled/repositioned/shown here instead of floating
            // unstyled mid-screen over the match log, which is what it did before.
            if (makeSubButton != null)
            {
                makeSubButton.gameObject.SetActive(true);
                RectTransform makeSubRect = makeSubButton.GetComponent<RectTransform>();
                ManagerUITheme.SetPointAnchor(makeSubRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 88f)), new Vector2(300f, 42f));
                makeSubRect.pivot = new Vector2(0f, 1f);
                if (makeSubButton.TryGetComponent(out Image subButtonImage))
                {
                    subButtonImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(makeSubButton, "+ ADD SUBSTITUTION", ManagerUITheme.TextBody, 13);
            }

            // Substitutions are a live-match-only concept (see also OnMakeSubDuringMatchClicked)
            // - the design's Full-Time Summary has no Substitutions section at all, so this
            // whole column needs to disappear at full-time exactly like the tactic pills do.
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsStatusObj;
            if (makeSubButton != null)
            {
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = makeSubButton.gameObject;
            }

            GameObject statsCaptionObj = new GameObject("MatchStatsCaption", typeof(RectTransform));
            statsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform statsCaptionRect2 = statsCaptionObj.GetComponent<RectTransform>();
            matchStatsCaptionRect = statsCaptionRect2;
            ManagerUITheme.SetPointAnchor(statsCaptionRect2, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 152f)), new Vector2(360f, 20f));
            statsCaptionRect2.pivot = new Vector2(0f, 1f);
            matchStatsCaptionLabel = ManagerUITheme.BuildLabel(statsCaptionObj.transform, "MATCH STATS", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject statsBarsObj = new GameObject("MatchStatsBars", typeof(RectTransform));
            statsBarsObj.transform.SetParent(matchdayPanel.transform, false);
            matchStatsBarsContainer = statsBarsObj.GetComponent<RectTransform>();
            matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
            matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 180f));
            matchStatsBarsContainer.sizeDelta = new Vector2(360f, 140f);

            // --- Footer: live Tactic pills (left, real - reused from Matchday Prep, which
            // no longer needs them since it's scouting-only now) + Continue (right) ---
            GameObject tacticLabelObj = new GameObject("TacticFooterCaption", typeof(RectTransform));
            tacticLabelObj.transform.SetParent(footerBand.transform, false);
            RectTransform tacticLabelRect = tacticLabelObj.GetComponent<RectTransform>();
            tacticLabelRect.anchorMin = new Vector2(0f, 0.5f);
            tacticLabelRect.anchorMax = new Vector2(0f, 0.5f);
            tacticLabelRect.pivot = new Vector2(0f, 0.5f);
            tacticLabelRect.anchoredPosition = new Vector2(40f, 0f);
            tacticLabelRect.sizeDelta = new Vector2(70f, 26f);
            ManagerUITheme.BuildLabel(tacticLabelObj.transform, "TACTIC", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = tacticLabelObj;

            // Repositioning alone isn't enough - these three are still children of
            // MatchdayPrepPanel (their original parent from before Matchday Prep was
            // simplified), so their visibility was still tied to THAT panel's active
            // state regardless of where their anchors pointed: visible on Matchday Prep
            // (wrong), invisible on Match Day (also wrong). Reparenting to footerBand
            // (matching tacticLabelObj above, so the anchor(0,0.5) math means the same
            // thing for all four) fixes both at once.
            if (attackingButton != null)
            {
                attackingButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(attackingButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(120f, 0f), new Vector2(120f, 44f));
                // Hand-placed Editor buttons, never routed through BuildButton - their
                // labels kept the Editor's original alignment/font/weight until now
                // (confirmed live: top-left aligned, non-bold, visibly different from
                // every other button in the app).
                ManagerUITheme.NormalizeButtonLabel(attackingButton, "ATTACKING", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = attackingButton.gameObject;
            }

            if (balancedButton != null)
            {
                balancedButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(balancedButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(250f, 0f), new Vector2(120f, 44f));
                ManagerUITheme.NormalizeButtonLabel(balancedButton, "BALANCED", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = balancedButton.gameObject;
            }

            if (defensiveButton != null)
            {
                defensiveButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(defensiveButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(380f, 0f), new Vector2(120f, 44f));
                ManagerUITheme.NormalizeButtonLabel(defensiveButton, "DEFENSIVE", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = defensiveButton.gameObject;
            }

            if (fullTimeContinueButton != null)
            {
                ManagerUITheme.SetPointAnchor(fullTimeContinueButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-40f, 20f), new Vector2(220f, 50f));
                if (fullTimeContinueButton.TryGetComponent(out Image continueImage))
                {
                    continueImage.color = ManagerUITheme.Accent;
                }

                ManagerUITheme.NormalizeButtonLabel(fullTimeContinueButton, "CONTINUE", ManagerUITheme.OnAccent, 15);
            }
        }

        public void OnPauseClicked()
        {
            matchPaused = !matchPaused;
            Time.timeScale = matchPaused ? 0f : 1f;

            if (pauseButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(pauseButton, matchPaused ? "RESUME" : "PAUSE", ManagerUITheme.TextBody, 12);
            }
        }

        private void RefreshMatchSubsStatus()
        {
            if (matchSubsStatusLabel != null)
            {
                matchSubsStatusLabel.text = $"Subs used: {subsUsedThisMatch}/{MaxSubsPerMatch}";
            }
        }

        // Single proportional bar showing the home team's share of total shots (no
        // possession bar - see BuildMatchdayChrome comment on why).
        private void RefreshLiveMatchStats(int homeShots, int awayShots)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            int totalShots = homeShots + awayShots;
            float homeSharePct = totalShots > 0 ? homeShots / (float)totalShots : 0.5f;

            GameObject row = new GameObject("ShotsRow", typeof(RectTransform));
            row.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = Vector2.zero;
            rowRect.sizeDelta = new Vector2(0f, 40f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.6f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            label.text = $"SHOTS   {homeShots} / {awayShots}";
            label.fontSize = 14;
            label.color = ManagerUITheme.TextBody;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject barObj = new GameObject("Bar", typeof(RectTransform));
            barObj.transform.SetParent(row.transform, false);
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildBar(barRect, homeSharePct, ManagerUITheme.Accent, 6f);
        }

        // Decorative equal-split bars (matching the design - the numbers carry the real
        // information, the bar underneath is just a visual accent) for shots and goals,
        // plus the tactic actually used, once the match has finished.
        private void ShowFullTimeMatchStats(int homeShots, int awayShots, int homeGoals, int awayGoals)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            float y = 0f;
            y = BuildFullTimeStatRow("SHOTS", homeShots, awayShots, y);
            y = BuildFullTimeStatRow("GOALS", homeGoals, awayGoals, y);

            GameObject tacticLineObj = new GameObject("TacticUsedLine", typeof(RectTransform));
            tacticLineObj.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform tacticLineRect = tacticLineObj.GetComponent<RectTransform>();
            tacticLineRect.anchorMin = new Vector2(0f, 1f);
            tacticLineRect.anchorMax = new Vector2(1f, 1f);
            tacticLineRect.pivot = new Vector2(0f, 1f);
            tacticLineRect.anchoredPosition = new Vector2(0f, -y - 8f);
            tacticLineRect.sizeDelta = new Vector2(0f, 22f);
            // Centered, matching the design's Full-Time Summary board (it centers this
            // line under the stat bars rather than left-aligning it).
            ManagerUITheme.BuildLabel(tacticLineObj.transform, $"Tactic used: {tacticUsedForCurrentMatch}", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
        }

        private float BuildFullTimeStatRow(string label, int homeValue, int awayValue, float y)
        {
            GameObject row = new GameObject($"{label}Row", typeof(RectTransform));
            row.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -y);
            rowRect.sizeDelta = new Vector2(0f, 40f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.6f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
            text.text = $"{homeValue}   {label}   {awayValue}";
            text.fontSize = 14;
            text.color = ManagerUITheme.TextBody;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject barsObj = new GameObject("Bars", typeof(RectTransform));
            barsObj.transform.SetParent(row.transform, false);
            RectTransform barsRect = barsObj.GetComponent<RectTransform>();
            barsRect.anchorMin = new Vector2(0f, 0f);
            barsRect.anchorMax = new Vector2(1f, 0.5f);
            barsRect.offsetMin = Vector2.zero;
            barsRect.offsetMax = Vector2.zero;

            ManagerUITheme.BuildBar(barsRect, 1f, ManagerUITheme.Accent, 6f);

            return y + 44f;
        }

        public void OnSimulateMatchClicked()
        {
            tacticUsedForCurrentMatch = selectedTactic;

            AgentMatchSimulator.AgentMatchResult result = SimulateFixture(currentFixture);

            lastSimulatedResult = result;

            SimulateOtherFixturesInMatchday(currentFixture.Matchday);

            if (!matchdayChromeBuilt)
            {
                BuildMatchdayChrome();
                matchdayChromeBuilt = true;
            }

            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(true);

            matchPaused = false;
            Time.timeScale = 1f;

            if (matchHomeNameLabel != null) matchHomeNameLabel.text = currentFixture.HomeTeam.ToUpperInvariant();
            if (matchAwayNameLabel != null) matchAwayNameLabel.text = currentFixture.AwayTeam.ToUpperInvariant();
            SetTactic(selectedTactic); // re-highlights the correct footer pill for this screen
            RefreshMatchSubsStatus();
            if (matchFullTimeCaptionGroup != null) matchFullTimeCaptionGroup.SetActive(false);

            // Undo everything ShowFullTimeResults did to these shared elements for the
            // previous match - without this, the second matchday inherited the first
            // match's full-time layout (centered/repositioned stats panel, hidden match
            // log, full-time-only scorer lists and View Match Events button still
            // visible) and rendered as an overlapping mess (confirmed live: fine on
            // matchday 1, badly broken on matchday 2).
            if (matchKeyMomentsCaptionRect != null) matchKeyMomentsCaptionRect.gameObject.SetActive(true);
            if (matchLogGroup != null) matchLogGroup.SetActive(true);
            if (matchFullTimeOnlyElements != null)
            {
                foreach (GameObject fullTimeElement in matchFullTimeOnlyElements)
                {
                    if (fullTimeElement != null) fullTimeElement.SetActive(false);
                }
            }
            ResetMatchStatsPanelToLiveLayout();

            foreach (GameObject liveElement in matchLiveOnlyElements)
            {
                if (liveElement != null) liveElement.SetActive(true);
            }

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = false;
            }

            StartCoroutine(ReplayMatchCoroutine(result));
        }

        // Mirrors the anchor/position/size BuildMatchdayChrome originally gave these two
        // elements for the live two-column layout - must match those values exactly,
        // since ShowFullTimeResults overwrites them in place (same RectTransforms,
        // reused rather than rebuilt) to get the full-time centered layout.
        private void ResetMatchStatsPanelToLiveLayout()
        {
            const float headerHeight = 110f;

            if (matchStatsCaptionRect != null)
            {
                matchStatsCaptionRect.anchorMin = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.anchorMax = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.pivot = new Vector2(0f, 1f);
                matchStatsCaptionRect.anchoredPosition = new Vector2(20f, -(headerHeight + 152f));
                matchStatsCaptionRect.sizeDelta = new Vector2(360f, 20f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 180f));
                matchStatsBarsContainer.sizeDelta = new Vector2(360f, 140f);
            }
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
            if (scoreText != null) scoreText.text = "0 - 0";
            if (clockText != null) clockText.text = "0' LIVE";

            RefreshLiveMatchStats(0, 0);

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            AgentTeam homeTeamAgent = GetOrCreateAgentTeam(currentFixture.HomeTeam);
            AgentTeam awayTeamAgent = GetOrCreateAgentTeam(currentFixture.AwayTeam);
            AgentTeam managedAgentTeam = currentFixture.HomeTeam == managedTeamName ? homeTeamAgent : awayTeamAgent;

            int homeGoals = 0;
            int awayGoals = 0;
            int homeShots = 0;
            int awayShots = 0;
            int eventIndex = 0;

            for (int minute = 1; minute <= 90; minute++)
            {
                if (!skipToResultsRequested)
                {
                    // Was a single blocking WaitForSeconds(secondsPerMinute) - since that's
                    // scaled-time, it (correctly) freezes solid while paused (timeScale=0),
                    // but that also meant a sub requested while paused couldn't be noticed
                    // until this wait naturally elapsed, which can only happen after
                    // resuming - the picker only ever popped open right when you hit
                    // Resume, not when you actually pressed the sub button (confirmed
                    // live). Polling per-frame with an early-exit lets a paused sub
                    // request be handled immediately without changing the normal
                    // (unpaused) per-minute pacing at all.
                    float elapsed = 0f;

                    while (elapsed < secondsPerMinute)
                    {
                        // skipToResultsRequested alone (not gated on matchPaused like the
                        // sub check) - same frozen-wait trap while paused (confirmed live:
                        // Skip to Results silently did nothing until Resume), and breaking
                        // early here costs nothing in the unpaused case either, since the
                        // very next loop iteration's "if (!skipToResultsRequested)" would
                        // have skipped the wait anyway.
                        if ((inMatchSubRequested && matchPaused) || skipToResultsRequested)
                        {
                            break;
                        }

                        yield return null;
                        elapsed += Time.deltaTime;
                    }
                }

                if (clockText != null) clockText.text = $"{minute}' LIVE";

                while (eventIndex < result.Events.Count && result.Events[eventIndex].Minute == minute)
                {
                    AgentMatchSimulator.AgentMatchEvent matchEvent = result.Events[eventIndex];
                    eventIndex++;

                    if (matchEvent.IsGoal)
                    {
                        if (matchEvent.HomeTeamScored) homeGoals++; else awayGoals++;

                        if (scoreText != null)
                        {
                            scoreText.text = $"{homeGoals} - {awayGoals}";
                        }
                    }

                    if (matchEvent.IsShot)
                    {
                        if (matchEvent.HomeTeamAttacking) homeShots++; else awayShots++;

                        RefreshLiveMatchStats(homeShots, awayShots);
                    }

                    if (eventFeedText != null)
                    {
                        // Goal lines get bolded and colored via TMP rich text (richText is
                        // on by default) to match the design's treatment - everything else
                        // stays the plain default color.
                        string line = matchEvent.IsGoal
                            ? $"<b><color=#3ddc84>{minute}' GOAL</color></b> · {matchEvent.Description}"
                            : $"{minute}' {matchEvent.Description}";

                        recentEventLines.Enqueue(line);

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

            // Switch from the live layout to the full-time one: hide the toolbar/clock/
            // tactic readout, show the "FULL TIME" caption, enlarge the score, and swap
            // the stats panel from the live single shots bar to the full-time breakdown.
            foreach (GameObject liveElement in matchLiveOnlyElements)
            {
                if (liveElement != null) liveElement.SetActive(false);
            }

            if (matchFullTimeCaptionGroup != null) matchFullTimeCaptionGroup.SetActive(true);

            if (scoreText != null)
            {
                scoreText.fontSize = 44;
                scoreText.text = $"{result.HomeGoals} - {result.AwayGoals}";
            }

            // Match Log is removed entirely at full-time (moved to its own "Match
            // Events" screen, see OnViewMatchEventsClicked) rather than staying visible
            // inline - Match Stats gets the freed-up space and is centered instead of
            // sharing a column with the log.
            if (matchKeyMomentsCaptionRect != null) matchKeyMomentsCaptionRect.gameObject.SetActive(false);
            if (matchLogGroup != null) matchLogGroup.SetActive(false);

            foreach (GameObject fullTimeElement in matchFullTimeOnlyElements)
            {
                if (fullTimeElement != null) fullTimeElement.SetActive(true);
            }

            PopulateGoalScorerLists(result);
            lastMatchEvents = new List<AgentMatchSimulator.AgentMatchEvent>(result.Events);

            // Recenter the stats panel into a single centered 520-wide column (matching
            // the design) now that it doesn't need to share the row with anything else.
            // Vertically centered in the space between the header and footer too (the
            // mockup's own Full-Time Summary board vertically centers this block) -
            // previously just pinned near the top, leaving a large dead gap below it
            // before the footer (confirmed live).
            if (matchStatsCaptionRect != null)
            {
                // Must be matchStatsCaptionRect (the "MatchStatsCaption" container that's
                // actually parented to the canvas), not matchStatsCaptionLabel.rectTransform
                // (BuildLabel's inner "Label" child, whose anchors/position are relative to
                // that container instead) - repositioning the child left the container
                // behind at its original column position and produced a nonsense on-screen
                // spot for the text, nowhere near the intended centered position.
                RectTransform captionRect = matchStatsCaptionRect;
                captionRect.anchorMin = new Vector2(0.5f, 1f);
                captionRect.anchorMax = new Vector2(0.5f, 1f);
                captionRect.pivot = new Vector2(0f, 1f);
                captionRect.anchoredPosition = new Vector2(-260f, -183f);
                captionRect.sizeDelta = new Vector2(240f, 20f);
            }

            if (viewMatchEventsButton != null)
            {
                RectTransform viewEventsRect = viewMatchEventsButton.GetComponent<RectTransform>();
                viewEventsRect.anchorMin = new Vector2(0.5f, 1f);
                viewEventsRect.anchorMax = new Vector2(0.5f, 1f);
                viewEventsRect.pivot = new Vector2(1f, 1f);
                viewEventsRect.anchoredPosition = new Vector2(260f, -179f);
                viewEventsRect.sizeDelta = new Vector2(220f, 32f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(-260f, -215f);
                matchStatsBarsContainer.sizeDelta = new Vector2(520f, 150f);
            }

            ShowFullTimeMatchStats(homeShots, awayShots, result.HomeGoals, result.AwayGoals);

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = true;
            }
        }

        // Real scorer names/minutes from AgentMatchEvent.ScorerName (see AgentMatchSimulator) -
        // not fabricated. Newest-first isn't required by the design, so kept in match order.
        private void PopulateGoalScorerLists(AgentMatchSimulator.AgentMatchResult result)
        {
            string homeList = "";
            string awayList = "";

            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal || string.IsNullOrEmpty(evt.ScorerName))
                {
                    continue;
                }

                // Football icon TMP Sprite Asset, not the "·" placeholder this used before
                // real art existed - matchHomeScorersLabel/matchAwayScorersLabel have
                // footballIconSpriteAsset assigned where they're built.
                string line = $"<size=60%><sprite name=\"football-icon\"></size> {evt.ScorerName} {evt.Minute}'\n";

                if (evt.HomeTeamScored)
                {
                    homeList += line;
                }
                else
                {
                    awayList += line;
                }
            }

            if (matchHomeScorersLabel != null) matchHomeScorersLabel.text = homeList.TrimEnd('\n');
            if (matchAwayScorersLabel != null) matchAwayScorersLabel.text = awayList.TrimEnd('\n');
        }

        public void OnFullTimeContinueClicked()
        {
            ApplyFixtureResult(currentFixture, lastSimulatedResult);

            currentFixtureIndex++;
            subsUsedThisMatch = 0;

            matchPaused = false;
            Time.timeScale = 1f;

            // The Match Events screen has its own Continue button wired to this same
            // method - without hiding it here, clicking Continue from there did switch
            // to the Hub underneath, but this still-active panel stayed on top and
            // masked the change completely, making the button look dead from that
            // screen (confirmed live).
            if (matchEventsPanel != null) matchEventsPanel.SetActive(false);

            ShowSeasonHub();
        }

        // --- Full-Time Summary -> Match Events (new screen, no Editor-placed panel to
        // wire - built entirely in code the first time it's opened, same as everything
        // else this reskin builds fresh). ---

        public void OnViewMatchEventsClicked()
        {
            if (!matchEventsChromeBuilt)
            {
                BuildMatchEventsPanel();
                matchEventsChromeBuilt = true;
            }

            if (matchEventsHomeNameLabel != null) matchEventsHomeNameLabel.text = currentFixture.HomeTeam.ToUpperInvariant();
            if (matchEventsAwayNameLabel != null) matchEventsAwayNameLabel.text = currentFixture.AwayTeam.ToUpperInvariant();
            if (matchEventsScoreText != null && lastSimulatedResult != null)
            {
                matchEventsScoreText.text = $"{lastSimulatedResult.HomeGoals} - {lastSimulatedResult.AwayGoals}";
            }

            PopulateMatchEventsList();

            if (matchdayPanel != null) matchdayPanel.SetActive(false);
            if (matchEventsPanel != null) matchEventsPanel.SetActive(true);
        }

        public void OnBackToSummaryClicked()
        {
            if (matchEventsPanel != null) matchEventsPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(true);
        }

        private void BuildMatchEventsPanel()
        {
            if (matchdayPanel == null || matchdayPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;
            const float footerHeight = 90f;

            matchEventsPanel = new GameObject("MatchEventsPanel", typeof(RectTransform));
            matchEventsPanel.transform.SetParent(matchdayPanel.transform.parent, false);
            RectTransform panelRect = matchEventsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(matchEventsPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(matchEventsPanel.transform, topBand: true, height: headerHeight);
            GameObject footer = ManagerUITheme.BuildAccentBand(matchEventsPanel.transform, topBand: false, height: footerHeight);

            GameObject captionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            captionObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(captionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(300f, 18f));
            ManagerUITheme.BuildLabel(captionObj.transform, "FULL TIME", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject scoreObj = new GameObject("Score", typeof(RectTransform));
            scoreObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(scoreObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(160f, 32f));
            matchEventsScoreText = ManagerUITheme.BuildLabel(scoreObj.transform, "", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject homeObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeObj.transform.SetParent(header.transform, false);
            RectTransform homeRect = homeObj.GetComponent<RectTransform>();
            homeRect.anchorMin = new Vector2(0.5f, 1f);
            homeRect.anchorMax = new Vector2(0.5f, 1f);
            homeRect.pivot = new Vector2(1f, 1f);
            homeRect.anchoredPosition = new Vector2(-90f, -50f);
            homeRect.sizeDelta = new Vector2(220f, 24f);
            matchEventsHomeNameLabel = ManagerUITheme.BuildLabel(homeObj.transform, "", 14, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayObj.transform.SetParent(header.transform, false);
            RectTransform awayRect = awayObj.GetComponent<RectTransform>();
            awayRect.anchorMin = new Vector2(0.5f, 1f);
            awayRect.anchorMax = new Vector2(0.5f, 1f);
            awayRect.pivot = new Vector2(0f, 1f);
            awayRect.anchoredPosition = new Vector2(90f, -50f);
            awayRect.sizeDelta = new Vector2(220f, 24f);
            matchEventsAwayNameLabel = ManagerUITheme.BuildLabel(awayObj.transform, "", 14, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject eventsCaptionObj = new GameObject("MatchEventsCaption", typeof(RectTransform));
            eventsCaptionObj.transform.SetParent(matchEventsPanel.transform, false);
            ManagerUITheme.SetPointAnchor(eventsCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(40f, -(headerHeight + 24f)), new Vector2(300f, 20f));
            ManagerUITheme.BuildLabel(eventsCaptionObj.transform, "MATCH EVENTS", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(matchEventsPanel.transform, "BACK TO SUMMARY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-40f, -(headerHeight + 18f)), new Vector2(200f, 32f));
            backButton.onClick.AddListener(OnBackToSummaryClicked);

            // Scrollable list: ScrollRect + masked Viewport + Content (VerticalLayoutGroup
            // + ContentSizeFitter), same pattern as SquadListView/LeagueTableView.
            GameObject scrollViewObj = new GameObject("MatchEventsScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(matchEventsPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(40f, footerHeight + 24f);
            // Right margin widened from 40 to 56 to make room for the scrollbar added
            // below - same lesson as the Tactics Board bench row earlier: the list was
            // already genuinely scrollable (mouse wheel confirmed working), but with no
            // visible affordance it read as "broken/missing events" rather than
            // "scroll for more" (confirmed live).
            scrollViewRect.offsetMax = new Vector2(-56f, -(headerHeight + 56f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            matchEventsListContainer = contentObj.GetComponent<RectTransform>();
            matchEventsListContainer.anchorMin = new Vector2(0f, 1f);
            matchEventsListContainer.anchorMax = new Vector2(1f, 1f);
            matchEventsListContainer.pivot = new Vector2(0.5f, 1f);
            matchEventsListContainer.anchoredPosition = Vector2.zero;
            matchEventsListContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.spacing = 2f;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = matchEventsListContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Negated from the default +1 - mouse wheel felt backwards (scrolling down
            // revealed earlier events instead of later ones, confirmed live) with
            // Unity's stock sign convention on this setup.
            scrollRect.scrollSensitivity = -1f;

            // Slim vertical scrollbar in the 16px gap freed up above - see the comment
            // on scrollViewRect.offsetMax.
            GameObject scrollbarObj = new GameObject("MatchEventsScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(matchEventsPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            // offsetMin/offsetMax, not sizeDelta - mirrors scrollViewRect's own margins
            // exactly (footerHeight+24 bottom, headerHeight+56 top) so the two line up,
            // and avoids the sizeDelta-under-stretched-anchors trap entirely.
            scrollbarRect.offsetMin = new Vector2(-46f, footerHeight + 24f);
            scrollbarRect.offsetMax = new Vector2(-40f, -(headerHeight + 56f));
            scrollbarObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject scrollHandleAreaObj = new GameObject("SlidingArea", typeof(RectTransform));
            scrollHandleAreaObj.transform.SetParent(scrollbarObj.transform, false);
            RectTransform scrollHandleAreaRect = scrollHandleAreaObj.GetComponent<RectTransform>();
            scrollHandleAreaRect.anchorMin = Vector2.zero;
            scrollHandleAreaRect.anchorMax = Vector2.one;
            scrollHandleAreaRect.offsetMin = Vector2.zero;
            scrollHandleAreaRect.offsetMax = Vector2.zero;

            GameObject scrollHandleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            scrollHandleObj.transform.SetParent(scrollHandleAreaObj.transform, false);
            RectTransform scrollHandleRect = scrollHandleObj.GetComponent<RectTransform>();
            scrollHandleRect.anchorMin = Vector2.zero;
            scrollHandleRect.anchorMax = new Vector2(1f, 0.3f);
            // Must be zeroed explicitly - a fresh RectTransform's default sizeDelta is
            // (100,100), which under stretched anchors ADDS 100px to the computed size
            // rather than being ignored (confirmed live on the bench scrollbar earlier).
            scrollHandleRect.sizeDelta = Vector2.zero;
            scrollHandleRect.offsetMin = Vector2.zero;
            scrollHandleRect.offsetMax = Vector2.zero;
            scrollHandleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.TopToBottom;
            scrollbar.handleRect = scrollHandleRect;
            scrollbar.targetGraphic = scrollHandleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            Button continueButton = ManagerUITheme.BuildButton(footer.transform, "CONTINUE", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(continueButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(220f, 50f));
            continueButton.onClick.AddListener(OnFullTimeContinueClicked);
        }

        private void PopulateMatchEventsList()
        {
            if (matchEventsListContainer == null || lastMatchEvents == null)
            {
                return;
            }

            foreach (Transform child in matchEventsListContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (AgentMatchSimulator.AgentMatchEvent evt in lastMatchEvents)
            {
                GameObject row = new GameObject($"Event_{evt.Minute}", typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(matchEventsListContainer, false);

                LayoutElement layoutElement = row.GetComponent<LayoutElement>();
                layoutElement.preferredHeight = 30f;
                layoutElement.flexibleWidth = 1f;

                string text = evt.IsGoal
                    ? $"<b><color=#3ddc84>{evt.Minute}'</color></b>   <b><color=#3ddc84>{evt.Description}</color></b>"
                    : $"{evt.Minute}'   {evt.Description}";

                ManagerUITheme.BuildLabel(row.transform, text, 14, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
            }
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
