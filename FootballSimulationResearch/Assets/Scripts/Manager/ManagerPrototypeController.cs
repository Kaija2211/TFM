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

        [Header("Substitutions")]
        [SerializeField] private TMP_Text subsStatusText;

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
        private RectTransform matchSubsLogContainer;
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

        // Standalone wordmark image (Title screen, Hub header) - unlike the two sprite
        // assets above, this is never used inline within a text string, so it's a plain
        // Sprite + Image rather than a TMP Sprite Asset.
        private Sprite tfmLogoSprite;

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
        private RectTransform matchdayPrepPitchContainer;

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

        // Which screen "Back to Squad" on Player Inspect actually returns to - three
        // possible entry points now that the Squad list screen exists alongside the
        // Tactics Board and the Hub (row-tapped-from-Squad-list vs pin-tapped-from-board
        // need different return screens, and Hub is the fallback for any other caller).
        private enum PlayerInspectReturnTarget { Hub, TacticsBoard, Squad }
        private PlayerInspectReturnTarget playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;

        // --- Squad list (read-only Pos/Player/OVR/Rating browse screen, reached from a
        // "List View" button on the Tactics Board header - Hub's own Squad button still
        // goes straight to the Tactics Board, unchanged) ---
        private bool squadBrowseChromeBuilt;
        private GameObject squadBrowsePanel;
        private TextMeshProUGUI squadBrowseByline;
        private SquadListView squadBrowseListView;

        // Set inside SimulateFixture and reused if an in-match substitution requires
        // resimulating the remainder of the match with the same underlying prediction.
        private float lastExpectedHomeGoals;
        private float lastExpectedAwayGoals;

        // Starting XI followed by Bench, built fresh each time the inspect screen opens.
        private List<PlayerAgent> inspectSquadPlayers = new();
        private int inspectPlayerIndex;

        // --- Substitutions: pre-match subs happen on the Tactics Board (drag a bench
        // card onto a pin - see OnBenchPlayerDroppedOnPin). Mid-match subs now reuse the
        // exact same drag-drop path via "Make Changes" (see
        // OnOpenTacticsBoardDuringMatchClicked) - uncapped, matching pre-match behaviour,
        // no separate off-then-on picker flow or per-match sub limit anymore. ---
        private bool tacticsBoardOpenedMidMatch;
        private readonly List<(string offName, string offPosition, string onName, string onPosition, int minute)> matchSubsLog = new();
        private int currentMatchMinute;
        private int liveHomeGoalsSoFar;
        private int liveAwayGoalsSoFar;

        private void Start()
        {
            weakFootStarSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/star-filled");
            footballIconSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/football-icon");
            tfmLogoSprite = Resources.Load<Sprite>("Manager/tfm-logo");

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
            ManagerUITheme.ApplyPanelBackground(matchdayPrepPanel);

            StyleHubActionButton(skipToResultsButton);
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
            ManagerUITheme.ApplyDiagonalGradientBackground(titlePanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

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

            // The old "TF"+accent-green-"M" text wordmark is replaced by the designer's
            // tfm-logo.png (native 700x220) - a standalone image, so a plain Image rather
            // than the TMP-inline-glyph sprite-asset pattern used for football-icon/stars.
            // Whole stack (logo/subtitle/buttons) is vertically centered in the 1080-tall
            // panel per the mockup's flex-centered layout, via fixed top offsets computed
            // from the stack's approximate total height (this file's existing top-anchored
            // pattern, not a real layout group - simplest fit for a screen this static).
            const float logoWidth = 420f;
            const float logoHeight = logoWidth * 220f / 700f;
            const float logoTop = 280f;

            GameObject wordmark = new GameObject("Wordmark", typeof(RectTransform));
            wordmark.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(wordmark, logoTop, logoWidth, logoHeight);

            if (tfmLogoSprite != null)
            {
                Image wordmarkImage = wordmark.AddComponent<Image>();
                wordmarkImage.sprite = tfmLogoSprite;
                wordmarkImage.preserveAspect = true;
                wordmarkImage.raycastTarget = false;
            }
            else
            {
                // Falls back to the old text wordmark if tfm-logo.png didn't come through
                // as a loadable Sprite - Unity's own TextureImporter has been failing to
                // read this specific asset ("File could not be read", reproduced against
                // multiple re-encoded copies of the file, so not a content problem) - an
                // Image with sprite == null renders as a solid white box, which reads as
                // more broken than the plain text mark it's meant to replace.
                TextMeshProUGUI wordmarkLabel = ManagerUITheme.BuildLabel(wordmark.transform, "TF<color=#3ddc84>M</color>", 64, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
                wordmarkLabel.characterSpacing = 4f;
                StartCoroutine(RecoverBlankLabelNextFrame(wordmarkLabel));
            }

            const float subtitleTop = logoTop + logoHeight + 32f;
            GameObject subtitle = new GameObject("Subtitle", typeof(RectTransform));
            subtitle.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(subtitle, subtitleTop, 600f, 30f);
            TextMeshProUGUI subtitleLabel = ManagerUITheme.BuildLabel(subtitle.transform, "THE ENGLISH PREMIER LEAGUE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            // Subtitle is now the very first TextMeshProUGUI created in the whole play
            // session (Title is always shown first, and the wordmark above is an Image,
            // not TMP anymore) - on a genuinely fresh session that first label can silently
            // fail to generate any mesh at all, texts built moments later (buttons) using
            // the exact same font asset render fine. Waiting a frame before checking gives
            // whatever TMP/font-asset initialization it's racing time to complete.
            StartCoroutine(RecoverBlankLabelNextFrame(subtitleLabel));

            const float buttonWidth = 340f;
            const float buttonHeight = 52f;
            const float spacing = 12f;
            const float startY = subtitleTop + 30f + 44f;

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
        // only on the very first TMP label built each session (the Title subtitle); later
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

            // Mockup's body is a max-width:1500px column centered in the 1920-wide panel
            // (`margin:0 auto`), not edge-to-edge - contentLeft/contentRight mark that
            // centered region's horizontal bounds, matching the panel's new width-wide
            // 1920x1080 canvas instead of the old 24px-from-edge layout tuned for 960x540.
            const float contentWidth = 1500f;
            const float contentLeft = (1920f - contentWidth) / 2f;
            const float contentRight = 1920f - contentLeft;
            const float nameColumnWidth = 240f;
            const float columnGap = 56f;
            const float clubColumnLeft = contentLeft + nameColumnWidth + columnGap;

            GameObject header = ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-2f * contentLeft, 34f);
            titleRect.anchoredPosition = new Vector2(contentLeft, -22f);
            ManagerUITheme.BuildLabel(titleObj.transform, "NEW CAREER", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-2f * contentLeft, 20f);
            subtitleRect.anchoredPosition = new Vector2(contentLeft, -58f);
            ManagerUITheme.BuildLabel(subtitleObj.transform, "Step 1 of 1 · Manager & Club", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: false, height: bandHeight);

            GameObject nameCaption = new GameObject("ManagerNameCaption", typeof(RectTransform));
            nameCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform nameCaptionRect = nameCaption.GetComponent<RectTransform>();
            nameCaptionRect.anchorMin = new Vector2(0f, 1f);
            nameCaptionRect.anchorMax = new Vector2(0f, 1f);
            nameCaptionRect.pivot = new Vector2(0f, 1f);
            nameCaptionRect.sizeDelta = new Vector2(nameColumnWidth, 18f);
            ManagerUITheme.BuildLabel(nameCaption.transform, "MANAGER NAME", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nameCaption.transform.SetAsFirstSibling();

            GameObject clubCaption = new GameObject("SelectClubCaption", typeof(RectTransform));
            clubCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform clubCaptionRect = clubCaption.GetComponent<RectTransform>();
            clubCaptionRect.anchorMin = new Vector2(0f, 1f);
            clubCaptionRect.anchorMax = new Vector2(0f, 1f);
            clubCaptionRect.pivot = new Vector2(0f, 1f);
            clubCaptionRect.sizeDelta = new Vector2(contentRight - clubColumnLeft, 18f);
            ManagerUITheme.BuildLabel(clubCaption.transform, "SELECT CLUB · PREMIER LEAGUE", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
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

            nameCaptionRect.anchoredPosition = new Vector2(contentLeft, -captionTop);
            clubCaptionRect.anchoredPosition = new Vector2(clubColumnLeft, -captionTop);

            if (managerNameInput != null)
            {
                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();
                ManagerUITheme.SetPointAnchor(inputRect, new Vector2(0f, 1f), new Vector2(contentLeft, -contentTop), new Vector2(nameColumnWidth, 48f));

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
                gridRect.offsetMin = new Vector2(clubColumnLeft, bandHeight + 47f);
                gridRect.offsetMax = new Vector2(-contentLeft, -contentTop);
            }

            // confirmTeamButton/teamSelectBackButton are Editor-placed and were never
            // explicitly positioned in code - their baked scene position was tuned
            // against the old 960x540 CanvasScaler reference resolution, so once that
            // changed to 1920x1080 their fixed pixel offset would have silently drifted
            // out of the mockup's intended footer position. Pinned explicitly here, same
            // pattern as every other screen's buttons.
            if (confirmTeamButton != null)
            {
                ManagerUITheme.SetPointAnchor(confirmTeamButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-contentLeft, 22f), new Vector2(200f, 48f));
            }

            if (teamSelectBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(teamSelectBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-contentLeft - 200f - 12f, 22f), new Vector2(140f, 48f));
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
            ManagerUITheme.ApplyDiagonalGradientBackground(seasonHubPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

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

            // Content is a max-width:1700px column centered in the 1920-wide panel
            // (mockup's `padding:48px 80px; max-width:1700px; margin:0 auto`), not
            // edge-to-edge against the panel's own corners like the old layout.
            const float outerLeft = (1920f - 1700f) / 2f;
            const float contentLeft = outerLeft + 80f;
            const float contentRight = 1920f - contentLeft;
            const float headerTop = 48f;
            const float logoHeight = 48f;
            const float logoWidth = logoHeight * 700f / 220f;

            // The mockup's Hub header has no separate club-crest badge - just the
            // tfm-logo mark directly beside the club name/byline block, so this replaces
            // the old colored-initials crest badge rather than sitting alongside it.
            // Skipped entirely (rather than showing a blank white box) if tfm-logo.png
            // didn't come through as a loadable Sprite - see the Title screen's wordmark
            // for the same fallback reasoning. Club name just falls back to sitting where
            // the logo would have started.
            float nameLeft = contentLeft;

            if (tfmLogoSprite != null)
            {
                GameObject logoObj = new GameObject("Logo", typeof(RectTransform), typeof(Image));
                logoObj.transform.SetParent(seasonHubPanel.transform, false);
                ManagerUITheme.SetPointAnchor(logoObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -headerTop), new Vector2(logoWidth, logoHeight));
                Image logoImage = logoObj.GetComponent<Image>();
                logoImage.sprite = tfmLogoSprite;
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;

                nameLeft = contentLeft + logoWidth + 20f;
            }

            GameObject nameObj = new GameObject("ClubName", typeof(RectTransform));
            nameObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(nameObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(nameLeft, -headerTop), new Vector2(600f, 36f));
            hubClubNameLabel = ManagerUITheme.BuildLabel(nameObj.transform, "", 32, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(nameLeft, -(headerTop + 38f)), new Vector2(600f, 20f));
            hubBylineLabel = ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            if (simulateSeasonButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateSeasonButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-contentLeft, -headerTop), new Vector2(220f, logoHeight));
                if (simulateSeasonButton.TryGetComponent(out Image simulateSeasonImage))
                {
                    simulateSeasonImage.color = ManagerUITheme.CardNeutral;
                }
                ManagerUITheme.NormalizeButtonLabel(simulateSeasonButton, "SIMULATE SEASON", ManagerUITheme.TextBody, 13);
            }

            // Left column (menu): Next Matchday / Squad / Transfers / Inbox / Settings /
            // Save & Exit. Row top offsets computed from the header block's own height
            // (headerTop + logoHeight + mockup's 40px margin-bottom below it) plus each
            // preceding row's own height and the mockup's 12px inter-row gap.
            const float menuWidth = 400f;
            const float menuTop = headerTop + logoHeight + 40f;
            const float rowGap = 12f;
            const float mainRowHeight = 64f;
            const float subRowHeight = 54f;

            if (playNextMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(playNextMatchButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -menuTop), new Vector2(menuWidth, mainRowHeight));
                if (playNextMatchButton.TryGetComponent(out Image playNextImage))
                {
                    playNextImage.color = ManagerUITheme.Accent;
                }
                ManagerUITheme.NormalizeButtonLabel(playNextMatchButton, "NEXT MATCHDAY", ManagerUITheme.OnAccent, 20);
            }

            float squadTop = menuTop + mainRowHeight + rowGap;

            if (viewSquadButton != null)
            {
                ManagerUITheme.SetPointAnchor(viewSquadButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -squadTop), new Vector2(menuWidth, subRowHeight));
                StyleHubActionButton(viewSquadButton);
                ManagerUITheme.NormalizeButtonLabel(viewSquadButton, "SQUAD", ManagerUITheme.TextBody, 17);
            }

            float transfersTop = squadTop + subRowHeight + rowGap;

            if (transfersButton != null)
            {
                ManagerUITheme.SetPointAnchor(transfersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -transfersTop), new Vector2(menuWidth, subRowHeight));
                ManagerUITheme.SetDisabledPlaceholder(transfersButton, "TRANSFERS");
            }

            float inboxTop = transfersTop + subRowHeight + rowGap;

            // "Inbox" is new copy from the mockup with no backing inbox system anywhere
            // in Manager Mode - treated as a disabled placeholder, same as
            // Transfers/Settings, rather than faking a feature that doesn't exist.
            GameObject inboxObj = new GameObject("InboxButton", typeof(RectTransform), typeof(Image), typeof(Button));
            inboxObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(inboxObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -inboxTop), new Vector2(menuWidth, subRowHeight));
            Button inboxButton = inboxObj.GetComponent<Button>();
            inboxButton.targetGraphic = inboxObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(inboxObj.transform, "INBOX", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            ManagerUITheme.SetDisabledPlaceholder(inboxButton, "INBOX");

            float settingsTop = inboxTop + subRowHeight + rowGap;

            GameObject settingsObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(settingsObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -settingsTop), new Vector2(menuWidth, subRowHeight));
            Button settingsButton = settingsObj.GetComponent<Button>();
            settingsButton.targetGraphic = settingsObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(settingsObj.transform, "SETTINGS", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            ManagerUITheme.SetDisabledPlaceholder(settingsButton, "SETTINGS");

            if (exitToTitleButton != null)
            {
                // Anchored to the bottom of the panel (not the top, unlike the buttons
                // above) so it stays visible regardless of canvas height.
                ManagerUITheme.SetPointAnchor(exitToTitleButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(contentLeft, 24f), new Vector2(menuWidth, 44f));
                if (exitToTitleButton.TryGetComponent(out Image exitImage))
                {
                    exitImage.color = ManagerUITheme.PanelDark;
                }
                ManagerUITheme.NormalizeButtonLabel(exitToTitleButton, "SAVE & EXIT TO TITLE", ManagerUITheme.TextMuted, 15);
            }

            // Right column: league table caption + the table itself. The Scroll View is
            // an Editor object (leagueTableView) - its RectTransform is repositioned here
            // (full-stretch anchors, explicit pixel offsets) rather than left at whatever
            // it was baked to in the scene, since that baked offset was tuned against the
            // old 960x540 CanvasScaler reference resolution and would silently drift once
            // the reference resolution changed to 1920x1080.
            float tableColumnLeft = contentLeft + menuWidth + 60f;

            GameObject tableCaption = new GameObject("TableCaption", typeof(RectTransform));
            tableCaption.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(tableCaption.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(tableColumnLeft, -menuTop), new Vector2(contentRight - tableColumnLeft, 22f));
            ManagerUITheme.BuildLabel(tableCaption.transform, "PREMIER LEAGUE · TABLE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            if (leagueTableView != null && leagueTableView.TryGetComponent(out RectTransform tableRect))
            {
                tableRect.anchorMin = new Vector2(0f, 0f);
                tableRect.anchorMax = new Vector2(1f, 1f);
                tableRect.offsetMin = new Vector2(tableColumnLeft, 48f);
                tableRect.offsetMax = new Vector2(-(1920f - contentRight), -(menuTop + 36f));
            }
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
                leagueTableView.Populate(playableTable.Sorted(), teamRegistry.GetTeamName, managedTeamId, GetRecentFormString);
            }
        }

        // --- Squad: Tactics Board (pitch view, position-pinned starters, drag a bench
        // card onto a pin to substitute, switch formation from the header dropdown - no
        // Editor-placed panel to wire, built entirely in code the first time it's
        // opened, same precedent as Match Events). Mid-match subs also go through this
        // same board now (see OnOpenTacticsBoardDuringMatchClicked) - the old in-match
        // off-then-on picker flow (playerListPanel/squadListView) is gone entirely. ---

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

            if (tacticsBoardOpenedMidMatch)
            {
                // Opened via "Make Changes" during a live match - return there instead of
                // the Hub, and don't auto-resume (matches every other manual pause/resume
                // flow in this file - the user hits Resume explicitly when ready).
                tacticsBoardOpenedMidMatch = false;
                if (matchdayPanel != null) matchdayPanel.SetActive(true);
            }
            else
            {
                ShowSeasonHub();
            }
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

            const float headerHeight = 90f;

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
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -28f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SQUAD", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(150f, 36f));
            backButton.onClick.AddListener(OnTacticsBoardBackClicked);

            tacticsBoardFormationButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "FORMATION", ManagerUITheme.CardNeutral, ManagerUITheme.TextPrimary, 14);
            ManagerUITheme.SetPointAnchor(tacticsBoardFormationButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-226f, -27f), new Vector2(200f, 36f));
            tacticsBoardFormationButton.onClick.AddListener(ToggleTacticsBoardFormationDropdown);

            // Not in the mockup (which assumed a separate Squad-list-first navigation) -
            // the user's chosen flow keeps the Tactics Board as the direct landing screen
            // from the Hub's Squad button, with this as the way to reach the read-only
            // Squad list instead of the other way around.
            Button listViewButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "LIST VIEW", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(listViewButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-442f, -27f), new Vector2(150f, 36f));
            listViewButton.onClick.AddListener(OnOpenSquadListClicked);

            // Body row: pitch (flex, capped at 1320px wide) beside a 300px vertical bench
            // rail, both filling the row band between the header and the panel's own
            // bottom margin - replaces the old bottom-anchored horizontal bench strip
            // beneath a fixed-1130:700-aspect-ratio pitch. Centered within a max-width
            // 1700px content region, matching the mockup's own centered body row (this
            // leaves a small unused margin after the bench rail when 1320+40+300 <
            // 1700, same as the mockup's own flex layout would - not a bug).
            const float outerContentWidth = 1700f;
            const float sideMargin = (1920f - outerContentWidth) / 2f;
            const float bodyPadding = 28f;
            const float benchRailWidth = 300f;
            const float columnGap = 40f;
            const float pitchMaxWidth = 1320f;

            float rowTop = headerHeight + bodyPadding;
            float rowHeight = panelRect.rect.height - headerHeight - bodyPadding * 2f;
            float availablePitchWidth = outerContentWidth - benchRailWidth - columnGap;
            float pitchWidth = Mathf.Min(pitchMaxWidth, availablePitchWidth);

            // Pitch: flat rectangles for the halfway line/penalty boxes (no sprites in
            // this project, same convention as everywhere else) - without them the pins
            // are just numbers scattered on a plain rectangle, with nothing anchoring the
            // eye to "this is a football formation" or explaining why the goalkeeper
            // sits close behind the back line. Pin positions come from TacticsBoardLayout.
            GameObject pitchObj = new GameObject("Pitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(tacticsBoardPanel.transform, false);
            tacticsBoardPitchContainer = pitchObj.GetComponent<RectTransform>();
            tacticsBoardPitchContainer.anchorMin = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.anchorMax = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.pivot = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.anchoredPosition = new Vector2(sideMargin, -rowTop);
            tacticsBoardPitchContainer.sizeDelta = new Vector2(pitchWidth, rowHeight);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(tacticsBoardPitchContainer);

            float benchRailLeft = sideMargin + pitchWidth + columnGap;

            GameObject benchCaptionObj = new GameObject("BenchCaption", typeof(RectTransform));
            benchCaptionObj.transform.SetParent(tacticsBoardPanel.transform, false);
            ManagerUITheme.SetPointAnchor(benchCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(benchRailLeft, -rowTop), new Vector2(benchRailWidth, 18f));
            ManagerUITheme.BuildLabel(benchCaptionObj.transform, "BENCH", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject benchSubCaptionObj = new GameObject("BenchSubCaption", typeof(RectTransform));
            benchSubCaptionObj.transform.SetParent(tacticsBoardPanel.transform, false);
            ManagerUITheme.SetPointAnchor(benchSubCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(benchRailLeft, -(rowTop + 24f)), new Vector2(benchRailWidth, 16f));
            ManagerUITheme.BuildLabel(benchSubCaptionObj.transform, "DRAG ONTO THE PITCH TO SUBSTITUTE", 11, ManagerUITheme.TextDim, TextAlignmentOptions.MidlineLeft);

            float benchListTop = rowTop + 24f + 24f;
            float benchListHeight = rowHeight - 24f - 24f;

            // Vertical scroll rail: same ScrollRect+Viewport+Content pattern as every
            // other list in this file (SquadListView/LeagueTableView/MatchEvents), just
            // a plain VerticalLayoutGroup instead of the bench's old rotated horizontal
            // one now that it's a right-side column instead of a bottom strip.
            GameObject scrollViewObj = new GameObject("BenchScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 1f);
            scrollViewRect.anchorMax = new Vector2(0f, 1f);
            scrollViewRect.pivot = new Vector2(0f, 1f);
            scrollViewRect.anchoredPosition = new Vector2(benchRailLeft, -benchListTop);
            scrollViewRect.sizeDelta = new Vector2(benchRailWidth - 16f, benchListHeight);

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
            tacticsBoardBenchContent.anchorMin = new Vector2(0f, 1f);
            tacticsBoardBenchContent.anchorMax = new Vector2(1f, 1f);
            tacticsBoardBenchContent.pivot = new Vector2(0.5f, 1f);
            tacticsBoardBenchContent.anchoredPosition = Vector2.zero;
            // Height must be explicit, not zero - childForceExpandHeight below stretches
            // every card to fill THIS rect's own height, so a zero-height Content
            // silently squashed every bench card to zero height too (invisible despite
            // existing, with correct width/position - confirmed live, on the old
            // horizontal version of this same rail).
            tacticsBoardBenchContent.sizeDelta = new Vector2(0f, 76f);

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.spacing = 10f;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = viewportRect;
            scrollRect.content = tacticsBoardBenchContent;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = -1f;

            // Slim scrollbar in the 16px gap to the right of the card column - the bench
            // rail itself is already a working vertical ScrollRect (drag or mouse-wheel
            // scrolls it), but with more bench players than fit in one screen's height
            // and no visible affordance, it reads as broken/missing subs rather than
            // "there's more, scroll for it" (same lesson as the old horizontal strip's
            // scrollbar, and Match Events' vertical one).
            GameObject scrollbarObj = new GameObject("BenchScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(0f, 1f);
            scrollbarRect.anchorMax = new Vector2(0f, 1f);
            scrollbarRect.pivot = new Vector2(0f, 1f);
            scrollbarRect.anchoredPosition = new Vector2(benchRailLeft + benchRailWidth - 10f, -benchListTop);
            scrollbarRect.sizeDelta = new Vector2(6f, benchListHeight);
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
            handleRect.anchorMax = new Vector2(1f, 0.3f);
            // Must be zeroed explicitly - a fresh RectTransform's default sizeDelta is
            // (100,100), which under stretched anchors ADDS 100px to the computed size
            // rather than being ignored (confirmed live on this exact scrollbar's
            // earlier horizontal incarnation).
            handleRect.sizeDelta = Vector2.zero;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.TopToBottom;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

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
            // No more vertical-compression fudge - that existed only to squeeze the
            // mockup's pin percentages into the old 960x540 canvas's much shorter pitch
            // region (see TacticsBoardLayout's own header comment). The 1920x1080 pitch
            // is close enough to the source design's own proportions that the raw
            // percentages should already read cleanly; re-verify live per formation
            // (including the un-mocked 4-3-3) and reintroduce a compression factor here
            // only if a specific formation still shows real overlap.
            Vector2 anchor = new Vector2(pinPercent.x, 1f - pinPercent.y);

            GameObject pinObj = ManagerUITheme.BuildPitchPinVisual(
                tacticsBoardPitchContainer,
                $"Pin_{player.Name}",
                anchor,
                circleSize: 68f,
                borderColor: ManagerUITheme.Accent,
                ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                ratingFontSize: 18,
                labelText: $"{player.Name} · {slotPosition}",
                labelFontSize: 14);

            pinObj.GetComponent<Image>().raycastTarget = true;

            TacticsBoardPlayerCard card = pinObj.AddComponent<TacticsBoardPlayerCard>();
            card.Configure(player, isDraggable: false, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin);
        }

        private void BuildTacticsBoardBenchCard(PlayerAgent player)
        {
            GameObject cardObj = new GameObject($"Bench_{player.Name}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cardObj.transform.SetParent(tacticsBoardBenchContent, false);

            LayoutElement layoutElement = cardObj.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 66f;

            cardObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject nameObj = new GameObject("Name", typeof(RectTransform));
            nameObj.transform.SetParent(cardObj.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(18f, 0f);
            nameRect.offsetMax = new Vector2(-18f, -2f);
            ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 17, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject ovrObj = new GameObject("OVR", typeof(RectTransform));
            ovrObj.transform.SetParent(cardObj.transform, false);
            RectTransform ovrRect = ovrObj.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(0f, 0.5f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.offsetMin = new Vector2(18f, 0f);
            ovrRect.offsetMax = new Vector2(-18f, -2f);
            ManagerUITheme.BuildLabel(ovrObj.transform, GetDisplayRating(player.GetOverallRating()).ToString(), 17, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject posObj = new GameObject("Position", typeof(RectTransform));
            posObj.transform.SetParent(cardObj.transform, false);
            RectTransform posRect = posObj.GetComponent<RectTransform>();
            posRect.anchorMin = new Vector2(0f, 0f);
            posRect.anchorMax = new Vector2(1f, 0.5f);
            posRect.offsetMin = new Vector2(18f, 2f);
            posRect.offsetMax = new Vector2(-18f, 0f);
            ManagerUITheme.BuildLabel(posObj.transform, player.PrimaryPosition.ToString(), 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            TacticsBoardPlayerCard card = cardObj.AddComponent<TacticsBoardPlayerCard>();
            card.Configure(player, isDraggable: true, isDropTarget: false, OnTacticsBoardPlayerTapped, null);
        }

        private void OnTacticsBoardPlayerTapped(PlayerAgent player)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.TacticsBoard;
            OpenPlayerInspect(player);
        }

        private void OnBenchPlayerDroppedOnPin(PlayerAgent benchPlayer, PlayerAgent pinPlayer)
        {
            if (benchPlayer == pinPlayer)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool applied = team.SubstitutePlayer(pinPlayer, benchPlayer);

            // Only log/show as a "Subs Made" entry - and only resimulate the rest of the
            // match - when this swap happens via the mid-match "Make Changes" flow;
            // pre-match team-sheet edits on this same board (reached via the Hub's Squad
            // button) aren't match events and have no match in progress to resimulate.
            if (applied && tacticsBoardOpenedMidMatch)
            {
                matchSubsLog.Add((pinPlayer.Name, pinPlayer.PrimaryPosition.ToString(), benchPlayer.Name, benchPlayer.PrimaryPosition.ToString(), currentMatchMinute));
                RefreshMatchSubsMadeList();
                TriggerMidMatchResimulation();
            }

            RefreshTacticsBoardUI();
        }

        // Regenerates the remainder of the currently-live match (from the minute after
        // the sub was made) against the same underlying prediction, so a mid-match sub
        // actually affects the rest of that match's events/result instead of only
        // taking effect from the *next* match onward. lastSimulatedResult is the same
        // object reference ReplayMatchCoroutine holds as its own "result" parameter, so
        // mutating it here is visible to that coroutine as soon as it resumes (it's
        // sitting frozen at Time.timeScale=0 while the Tactics Board is open, not
        // actively reading events right now).
        private void TriggerMidMatchResimulation()
        {
            if (lastSimulatedResult == null)
            {
                return;
            }

            AgentTeam homeTeamAgent = GetOrCreateAgentTeam(currentFixture.HomeTeam);
            AgentTeam awayTeamAgent = GetOrCreateAgentTeam(currentFixture.AwayTeam);

            AgentMatchSimulator.AgentMatchResult tail = matchSimulator.SimulateFromMinute(
                homeTeamAgent,
                awayTeamAgent,
                lastExpectedHomeGoals,
                lastExpectedAwayGoals,
                currentMatchMinute + 1,
                liveHomeGoalsSoFar,
                liveAwayGoalsSoFar);

            lastSimulatedResult.Events.RemoveAll(e => e.Minute > currentMatchMinute);
            lastSimulatedResult.Events.AddRange(tail.Events);
            lastSimulatedResult.HomeGoals = tail.HomeGoals;
            lastSimulatedResult.AwayGoals = tail.AwayGoals;
        }

        private static float GetRatingPercent(PlayerAgent player)
        {
            return GetDisplayRating(player.GetOverallRating()) / 99f;
        }

        // --- Squad list: read-only Pos/Player/OVR/Rating browse screen (Starting XI +
        // Bench), reached via the Tactics Board's "List View" button. Built entirely in
        // code the first time it's opened, same precedent as the Tactics Board and Match
        // Events panels - no Editor-placed panel to wire. ---

        public void OnOpenSquadListClicked()
        {
            if (!squadBrowseChromeBuilt)
            {
                BuildSquadChrome();
                squadBrowseChromeBuilt = true;
            }

            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(true);

            RefreshSquadUI();
        }

        public void OnSquadListBackClicked()
        {
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(false);

            OnViewSquadClicked();
        }

        private void BuildSquadChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            squadBrowsePanel = new GameObject("SquadBrowsePanel", typeof(RectTransform));
            squadBrowsePanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = squadBrowsePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(squadBrowsePanel);

            GameObject header = ManagerUITheme.BuildAccentBand(squadBrowsePanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SQUAD", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1000f, 20f));
            squadBrowseByline = ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO TACTICS BOARD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(230f, 36f));
            backButton.onClick.AddListener(OnSquadListBackClicked);

            Button sortButton = ManagerUITheme.BuildButton(header.transform, "SORT: POSITION", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(sortButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-306f, -27f), new Vector2(160f, 36f));
            ManagerUITheme.SetDisabledPlaceholder(sortButton, "SORT: POSITION");

            Button filterButton = ManagerUITheme.BuildButton(header.transform, "FILTER", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(filterButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-482f, -27f), new Vector2(150f, 36f));
            ManagerUITheme.SetDisabledPlaceholder(filterButton, "FILTER");

            // Centered max-width:1600 scrollable list - code-built ScrollRect+Viewport+
            // Content+Scrollbar, same pattern as BuildMatchEventsPanel.
            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("SquadScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(squadBrowsePanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(sideMargin, 40f);
            scrollViewRect.offsetMax = new Vector2(-(sideMargin + 20f), -(headerHeight + 40f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(SquadListView));
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            squadBrowseListView = contentObj.GetComponent<SquadListView>();
            squadBrowseListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = -1f;

            GameObject scrollbarObj = new GameObject("SquadScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(squadBrowsePanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-(sideMargin + 20f), 40f);
            scrollbarRect.offsetMax = new Vector2(-(sideMargin + 4f), -(headerHeight + 40f));
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
            handleRect.anchorMax = new Vector2(1f, 0.3f);
            handleRect.sizeDelta = Vector2.zero;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.TopToBottom;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private void RefreshSquadUI()
        {
            if (squadBrowseListView == null)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (squadBrowseByline != null)
            {
                string formationText = TacticsBoardLayout.FormatFormation(team.Formation);

                if (currentFixtureIndex < managedTeamFixtures.Count)
                {
                    OpenFootballMatch nextFixture = managedTeamFixtures[currentFixtureIndex];
                    bool managedIsHome = nextFixture.HomeTeam == managedTeamName;
                    string opponentName = managedIsHome ? nextFixture.AwayTeam : nextFixture.HomeTeam;
                    squadBrowseByline.text = $"Next: vs {opponentName} ({(managedIsHome ? "H" : "A")})   ·   Formation {formationText}   ·   Tactic: {selectedTactic}";
                }
                else
                {
                    squadBrowseByline.text = $"Season complete   ·   Formation {formationText}   ·   Tactic: {selectedTactic}";
                }
            }

            squadBrowseListView.Clear();
            squadBrowseListView.AddGridHeaderRow();
            squadBrowseListView.AddSectionHeader("Starting XI");

            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                squadBrowseListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked);
            }

            squadBrowseListView.AddSectionHeader($"Bench ({team.Bench.Count})");

            foreach (PlayerAgent player in team.Bench)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked);
            }
        }

        private void OnSquadRowClicked(PlayerAgent player)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.Squad;
            OpenPlayerInspect(player);
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
                ManagerUITheme.SetPointAnchor(inspectBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, navButtonY), new Vector2(220f, navButtonHeight));
                if (inspectBackButton.TryGetComponent(out Image backImage))
                {
                    backImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectBackButton, "BACK TO SQUAD", ManagerUITheme.TextBody, 15);
            }

            if (inspectPreviousButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectPreviousButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(60f, navButtonY), new Vector2(140f, navButtonHeight));
                if (inspectPreviousButton.TryGetComponent(out Image prevImage))
                {
                    prevImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectPreviousButton, "< PREV", ManagerUITheme.TextBody, 14);
            }

            if (inspectNextButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectNextButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(216f, navButtonY), new Vector2(140f, navButtonHeight));
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

            switch (playerInspectReturnTarget)
            {
                case PlayerInspectReturnTarget.TacticsBoard:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnViewSquadClicked();
                    break;
                case PlayerInspectReturnTarget.Squad:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenSquadListClicked();
                    break;
                default:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    ShowSeasonHub();
                    break;
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

            // Centered max-width:1600px content region within the full-stretch 1920-wide
            // container, matching the mockup's centered layout instead of edge-to-edge.
            const float contentMargin = (1920f - 1600f) / 2f;

            GameObject headerBand = new GameObject("HeaderBand", typeof(RectTransform), typeof(Image));
            headerBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(headerBand, 0f, 130f, contentMargin);
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
            ManagerUITheme.BuildLabel(nameLabel.transform, player.Name.ToUpperInvariant(), 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

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
            ManagerUITheme.BuildLabel(ovrValue.transform, displayRating.ToString(), 40, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject ovrCaption = new GameObject("OvrCaption", typeof(RectTransform));
            ovrCaption.transform.SetParent(headerBand.transform, false);
            RectTransform ovrCaptionRect = ovrCaption.GetComponent<RectTransform>();
            ovrCaptionRect.anchorMin = new Vector2(1f, 1f);
            ovrCaptionRect.anchorMax = new Vector2(1f, 1f);
            ovrCaptionRect.pivot = new Vector2(1f, 1f);
            ovrCaptionRect.sizeDelta = new Vector2(140f, 16f);
            ovrCaptionRect.anchoredPosition = new Vector2(-24f, -66f);
            ManagerUITheme.BuildLabel(ovrCaption.transform, $"OVERALL ({player.PrimaryPosition})", 11, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            GameObject attributeGrid = new GameObject("AttributeGrid", typeof(RectTransform));
            attributeGrid.transform.SetParent(playerInspectContentContainer, false);
            spawnedInspectElements.Add(attributeGrid);

            // Full stretch down to the footer (not a fixed height) - the old fixed 220px
            // height left most of the panel as dead empty space below the grid. Same
            // centered max-width:1600 region as the header band above.
            RectTransform attributeGridRect = attributeGrid.GetComponent<RectTransform>();
            attributeGridRect.anchorMin = new Vector2(0f, 0f);
            attributeGridRect.anchorMax = new Vector2(1f, 1f);
            attributeGridRect.offsetMin = new Vector2(contentMargin + 20f, 110f);
            attributeGridRect.offsetMax = new Vector2(-(contentMargin + 20f), -150f);

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
            titleRect.sizeDelta = new Vector2(-120f, 34f);
            titleRect.anchoredPosition = new Vector2(60f, -22f);
            matchdayPrepTitleLabel = ManagerUITheme.BuildLabel(titleObj.transform, "", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-120f, 20f);
            subtitleRect.anchoredPosition = new Vector2(60f, -58f);
            matchdayPrepSubtitleLabel = ManagerUITheme.BuildLabel(subtitleObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            ManagerUITheme.BuildAccentBand(matchdayPrepContentContainer, topBand: false, height: bandHeight);

            // Footer action pair, right-aligned per the design mockup ("Back to Hub" /
            // "Simulate Match ->"). These two were never positioned - both sat stacked at
            // (0,0), so the unstyled Back button (still showing its default Editor label)
            // rendered on top of and completely hid the correctly-styled Simulate Match button.
            if (simulateMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateMatchButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, 22f), new Vector2(220f, 50f));
            }

            if (matchdayPrepBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(matchdayPrepBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-292f, 22f), new Vector2(170f, 50f));
            }

            // Two-column body: opponent scout list (left, flexible width) beside a
            // read-only opponent-formation pitch (right, 620px, matching the mockup).
            // Both fill the row between the header/footer bands.
            const float sideMargin = 60f;
            const float columnGap = 48f;
            const float opponentPitchWidth = 620f;
            const float rowMargin = bandHeight + 24f;

            float scoutListRight = 1920f - sideMargin - opponentPitchWidth - columnGap;

            if (opponentSquadListView != null)
            {
                RectTransform opponentListRect = opponentSquadListView.GetComponent<RectTransform>();
                opponentListRect.anchorMin = new Vector2(0f, 0f);
                opponentListRect.anchorMax = new Vector2(1f, 1f);
                opponentListRect.offsetMin = new Vector2(sideMargin, rowMargin);
                opponentListRect.offsetMax = new Vector2(-(1920f - scoutListRight), -rowMargin);
                opponentSquadListView.gameObject.SetActive(true);
            }

            float pitchLeft = scoutListRight + columnGap;

            GameObject pitchColumnCaption = new GameObject("OpponentShapeCaption", typeof(RectTransform));
            pitchColumnCaption.transform.SetParent(matchdayPrepContentContainer, false);
            ManagerUITheme.SetPointAnchor(pitchColumnCaption.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(pitchLeft, -rowMargin), new Vector2(opponentPitchWidth, 20f));
            ManagerUITheme.BuildLabel(pitchColumnCaption.transform, "OPPONENT SHAPE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject pitchObj = new GameObject("OpponentPitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(matchdayPrepContentContainer, false);
            matchdayPrepPitchContainer = pitchObj.GetComponent<RectTransform>();
            matchdayPrepPitchContainer.anchorMin = new Vector2(0f, 1f);
            matchdayPrepPitchContainer.anchorMax = new Vector2(0f, 1f);
            matchdayPrepPitchContainer.pivot = new Vector2(0f, 1f);
            matchdayPrepPitchContainer.anchoredPosition = new Vector2(pitchLeft, -(rowMargin + 30f));
            matchdayPrepPitchContainer.sizeDelta = new Vector2(opponentPitchWidth, 1080f - (rowMargin + 30f) - rowMargin);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(matchdayPrepPitchContainer);
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

            // Read-only scouting list - Starting XI + Bench, no row click handler (null
            // onRowClicked means SquadListView.AddPlayerGridRow builds no Button at all).
            if (opponentSquadListView != null)
            {
                opponentSquadListView.Clear();
                opponentSquadListView.AddGridHeaderRow();
                opponentSquadListView.AddSectionHeader("Starting XI");

                List<PlayerPosition> slots = squadGenerator.GetStartingPositions(opponentTeam.Formation);

                for (int i = 0; i < opponentTeam.StartingEleven.Count; i++)
                {
                    PlayerAgent player = opponentTeam.StartingEleven[i];
                    PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                    opponentSquadListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), null);
                }

                opponentSquadListView.AddSectionHeader($"Bench ({opponentTeam.Bench.Count})");

                foreach (PlayerAgent player in opponentTeam.Bench)
                {
                    opponentSquadListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), null);
                }
            }

            RefreshMatchdayPrepOpponentPitch(opponentTeam);
        }

        // Read-only opponent-formation pitch - shares BuildPitchMarkings and the
        // BuildPitchPinVisual helper with the interactive Tactics Board, but never adds
        // a TacticsBoardPlayerCard (no drag, no drop, no tap), and uses the Danger red
        // border color instead of Accent green to read as "not yours to touch".
        private void RefreshMatchdayPrepOpponentPitch(AgentTeam opponentTeam)
        {
            if (matchdayPrepPitchContainer == null)
            {
                return;
            }

            for (int i = matchdayPrepPitchContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = matchdayPrepPitchContainer.GetChild(i);

                if (child.name.StartsWith("OpponentPin_"))
                {
                    Destroy(child.gameObject);
                }
            }

            IReadOnlyList<Vector2> pins = TacticsBoardLayout.GetPins(opponentTeam.Formation);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(opponentTeam.Formation);

            for (int i = 0; i < opponentTeam.StartingEleven.Count && i < pins.Count; i++)
            {
                PlayerAgent player = opponentTeam.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                Vector2 anchor = new Vector2(pins[i].x, 1f - pins[i].y);

                ManagerUITheme.BuildPitchPinVisual(
                    matchdayPrepPitchContainer,
                    $"OpponentPin_{player.Name}",
                    anchor,
                    circleSize: 48f,
                    borderColor: ManagerUITheme.Danger,
                    ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                    ratingFontSize: 12,
                    labelText: $"{player.Name} · {slot}",
                    labelFontSize: 10);
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
                ManagerUITheme.SetPointAnchor(scoreText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(260f, 60f));
                scoreText.fontSize = 52;
                scoreText.alignment = TextAlignmentOptions.Center;
                scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            }

            if (clockText != null)
            {
                ManagerUITheme.SetPointAnchor(clockText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(220f, 20f));
                clockText.alignment = TextAlignmentOptions.Center;
                clockText.fontSize = 14;
            }

            GameObject homeNameObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform homeNameRect = homeNameObj.GetComponent<RectTransform>();
            homeNameRect.anchorMin = new Vector2(0.5f, 1f);
            homeNameRect.anchorMax = new Vector2(0.5f, 1f);
            homeNameRect.pivot = new Vector2(1f, 1f);
            homeNameRect.anchoredPosition = new Vector2(-150f, -68f);
            homeNameRect.sizeDelta = new Vector2(300f, 36f);
            matchHomeNameLabel = ManagerUITheme.BuildLabel(homeNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayNameObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform awayNameRect = awayNameObj.GetComponent<RectTransform>();
            awayNameRect.anchorMin = new Vector2(0.5f, 1f);
            awayNameRect.anchorMax = new Vector2(0.5f, 1f);
            awayNameRect.pivot = new Vector2(0f, 1f);
            awayNameRect.anchoredPosition = new Vector2(150f, -68f);
            awayNameRect.sizeDelta = new Vector2(300f, 36f);
            matchAwayNameLabel = ManagerUITheme.BuildLabel(awayNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject fullTimeCaptionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            fullTimeCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(fullTimeCaptionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 20f));
            matchFullTimeCaptionLabel = ManagerUITheme.BuildLabel(fullTimeCaptionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
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
            GameObject subsCaptionObj = new GameObject("SubsMadeCaption", typeof(RectTransform));
            subsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsCaptionRect = subsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsCaptionRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 28f)), new Vector2(360f, 20f));
            subsCaptionRect.pivot = new Vector2(0f, 1f);
            ManagerUITheme.BuildLabel(subsCaptionObj.transform, "SUBS MADE  ·  MANAGE VIA TACTICS BOARD", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Read-only log of subs made this match (see matchSubsLog) - populated by
            // RefreshMatchSubsMadeList, one row per entry. Subs themselves happen on the
            // Tactics Board via "Make Changes" below, not here - no picker on this screen.
            GameObject subsLogObj = new GameObject("SubsLog", typeof(RectTransform));
            subsLogObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsLogRect = subsLogObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsLogRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 54f)), new Vector2(360f, 76f));
            subsLogRect.pivot = new Vector2(0f, 1f);
            matchSubsLogContainer = subsLogRect;

            VerticalLayoutGroup subsLogLayout = subsLogObj.AddComponent<VerticalLayoutGroup>();
            subsLogLayout.childForceExpandWidth = true;
            subsLogLayout.childForceExpandHeight = false;
            subsLogLayout.childControlWidth = true;
            subsLogLayout.childControlHeight = true;
            subsLogLayout.spacing = 6f;

            ContentSizeFitter subsLogFitter = subsLogObj.AddComponent<ContentSizeFitter>();
            subsLogFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Button makeChangesButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextPrimary, 13);
            RectTransform makeChangesRect = makeChangesButton.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(makeChangesRect, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 148f)), new Vector2(300f, 42f));
            makeChangesRect.pivot = new Vector2(0f, 1f);
            makeChangesButton.onClick.AddListener(OnOpenTacticsBoardDuringMatchClicked);

            // Subs Made is a live-match-only concept - the design's Full-Time Summary has
            // no equivalent section at all, so this whole column needs to disappear at
            // full-time exactly like the tactic pills do.
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsLogObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = makeChangesButton.gameObject;

            GameObject statsCaptionObj = new GameObject("MatchStatsCaption", typeof(RectTransform));
            statsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform statsCaptionRect2 = statsCaptionObj.GetComponent<RectTransform>();
            matchStatsCaptionRect = statsCaptionRect2;
            ManagerUITheme.SetPointAnchor(statsCaptionRect2, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 210f)), new Vector2(360f, 20f));
            statsCaptionRect2.pivot = new Vector2(0f, 1f);
            matchStatsCaptionLabel = ManagerUITheme.BuildLabel(statsCaptionObj.transform, "MATCH STATS", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject statsBarsObj = new GameObject("MatchStatsBars", typeof(RectTransform));
            statsBarsObj.transform.SetParent(matchdayPanel.transform, false);
            matchStatsBarsContainer = statsBarsObj.GetComponent<RectTransform>();
            matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
            matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 238f));
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

        // Rebuilds the "Subs Made" list from matchSubsLog - one row per entry, oldest
        // first. Called after ReplayMatchCoroutine starts (empty log) and again whenever
        // a sub lands via the Tactics Board mid-match (see
        // OnOpenTacticsBoardDuringMatchClicked/OnBenchPlayerDroppedOnPin).
        private void RefreshMatchSubsMadeList()
        {
            if (matchSubsLogContainer == null)
            {
                return;
            }

            foreach (Transform child in matchSubsLogContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (var entry in matchSubsLog)
            {
                GameObject row = new GameObject("SubRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                row.transform.SetParent(matchSubsLogContainer, false);

                LayoutElement layoutElement = row.GetComponent<LayoutElement>();
                layoutElement.preferredHeight = 32f;
                layoutElement.flexibleWidth = 1f;

                row.GetComponent<Image>().color = ManagerUITheme.PanelDark;

                string rowText = $"OUT {entry.offName} ({entry.offPosition})  →  IN {entry.onName} ({entry.onPosition})  {entry.minute}'";
                GameObject labelObj = new GameObject("Label", typeof(RectTransform));
                labelObj.transform.SetParent(row.transform, false);
                RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(labelObj.transform, rowText, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            }
        }

        // Opens the Tactics Board mid-match so subs can be made via the same drag-drop
        // path used pre-match, uncapped (no MaxSubsPerMatch-style limit here, matching
        // pre-match behaviour) - replaces the old separate off-then-on picker flow.
        // Auto-pauses via the existing Pause toggle rather than a new mechanism.
        public void OnOpenTacticsBoardDuringMatchClicked()
        {
            if (!matchPaused)
            {
                OnPauseClicked();
            }

            if (!tacticsBoardChromeBuilt)
            {
                BuildTacticsBoardChrome();
                tacticsBoardChromeBuilt = true;
            }

            if (matchdayPanel != null) matchdayPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);

            tacticsBoardOpenedMidMatch = true;

            RefreshTacticsBoardUI();
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
            ManagerUITheme.BuildLabel(tacticLineObj.transform, $"Tactic used: {tacticUsedForCurrentMatch}", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
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
            text.fontSize = 18;
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

            if (matchHomeNameLabel != null) { matchHomeNameLabel.text = currentFixture.HomeTeam.ToUpperInvariant(); matchHomeNameLabel.fontSize = 24; }
            if (matchAwayNameLabel != null) { matchAwayNameLabel.text = currentFixture.AwayTeam.ToUpperInvariant(); matchAwayNameLabel.fontSize = 24; }
            // scoreText.fontSize isn't reset by ResetMatchStatsPanelToLiveLayout below (that
            // only touches the stats panel's position/size) - without resetting it here too,
            // matchday 2+ would inherit the full-time-sized 56pt score from the previous
            // match instead of the live view's 52pt, same class of bug that motivated
            // ResetMatchStatsPanelToLiveLayout in the first place.
            if (scoreText != null) scoreText.fontSize = 52;
            SetTactic(selectedTactic); // re-highlights the correct footer pill for this screen
            matchSubsLog.Clear();
            RefreshMatchSubsMadeList();
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
            int homeTeamId = teamRegistry.GetTeamId(fixture.HomeTeam);
            int awayTeamId = teamRegistry.GetTeamId(fixture.AwayTeam);

            MatchRecord record = new MatchRecord
            {
                Matchday = fixture.Matchday,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                HomeGoals = result.HomeGoals,
                AwayGoals = result.AwayGoals
            };

            playableTable.Apply(record);

            if (result.HomeGoals > result.AwayGoals)
            {
                RecordFormResult(homeTeamId, 'W');
                RecordFormResult(awayTeamId, 'L');
            }
            else if (result.HomeGoals < result.AwayGoals)
            {
                RecordFormResult(homeTeamId, 'L');
                RecordFormResult(awayTeamId, 'W');
            }
            else
            {
                RecordFormResult(homeTeamId, 'D');
                RecordFormResult(awayTeamId, 'D');
            }
        }

        // Manager-Mode-only last-5-results tracker backing the Hub league table's Form
        // column (e.g. "W D W W D") - deliberately NOT part of LeagueTable.Entry, which
        // stays untouched and is still what Research Mode's evaluation output reads.
        // Oldest result first, newest last (capped at 5), matching how "form" reads
        // left-to-right in real football coverage.
        private readonly Dictionary<int, List<char>> recentFormByTeamId = new();

        private void RecordFormResult(int teamId, char result)
        {
            if (!recentFormByTeamId.TryGetValue(teamId, out List<char> history))
            {
                history = new List<char>();
                recentFormByTeamId[teamId] = history;
            }

            history.Add(result);

            if (history.Count > 5)
            {
                history.RemoveAt(0);
            }
        }

        private string GetRecentFormString(int teamId)
        {
            if (!recentFormByTeamId.TryGetValue(teamId, out List<char> history) || history.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(" ", history);
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
            tacticsBoardOpenedMidMatch = false;
            currentMatchMinute = 0;
            liveHomeGoalsSoFar = 0;
            liveAwayGoalsSoFar = 0;
            matchSubsLog.Clear();
            RefreshMatchSubsMadeList();

            if (eventFeedText != null) eventFeedText.text = "";
            if (scoreText != null) scoreText.text = "0 - 0";
            if (clockText != null) clockText.text = "0' LIVE";

            RefreshLiveMatchStats(0, 0);

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            int homeShots = 0;
            int awayShots = 0;
            int eventIndex = 0;

            for (int minute = 1; minute <= 90; minute++)
            {
                // Tracked on a field (not just the loop variable) so a mid-match
                // substitution made via the Tactics Board - which happens entirely
                // outside this coroutine while it's frozen at Time.timeScale=0 - knows
                // which minute to resimulate from and log against.
                currentMatchMinute = minute;

                if (!skipToResultsRequested)
                {
                    // Was a single blocking WaitForSeconds(secondsPerMinute) - since
                    // that's scaled-time, it (correctly) freezes solid while paused
                    // (timeScale=0). Polling per-frame instead lets Skip to Results
                    // interrupt immediately even while paused, without changing the
                    // normal (unpaused) per-minute pacing at all. The Tactics Board
                    // itself needs no special handling here - opening it mid-match just
                    // pauses (Time.timeScale=0), which freezes this wait solid exactly
                    // like a manual Pause does, and it resumes correctly on its own once
                    // timeScale goes back to 1.
                    float elapsed = 0f;

                    while (elapsed < secondsPerMinute)
                    {
                        if (skipToResultsRequested)
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
                        if (matchEvent.HomeTeamScored) liveHomeGoalsSoFar++; else liveAwayGoalsSoFar++;

                        if (scoreText != null)
                        {
                            scoreText.text = $"{liveHomeGoalsSoFar} - {liveAwayGoalsSoFar}";
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
                scoreText.fontSize = 56;
                scoreText.text = $"{result.HomeGoals} - {result.AwayGoals}";
            }

            if (matchHomeNameLabel != null) matchHomeNameLabel.fontSize = 30;
            if (matchAwayNameLabel != null) matchAwayNameLabel.fontSize = 30;

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
            ManagerUITheme.BuildLabel(captionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);

            // Score/team-name sizes match the Full-Time Summary header exactly - the
            // mockup uses the identical header block for both screens.
            GameObject scoreObj = new GameObject("Score", typeof(RectTransform));
            scoreObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(scoreObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(200f, 60f));
            matchEventsScoreText = ManagerUITheme.BuildLabel(scoreObj.transform, "", 56, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject homeObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeObj.transform.SetParent(header.transform, false);
            RectTransform homeRect = homeObj.GetComponent<RectTransform>();
            homeRect.anchorMin = new Vector2(0.5f, 1f);
            homeRect.anchorMax = new Vector2(0.5f, 1f);
            homeRect.pivot = new Vector2(1f, 1f);
            homeRect.anchoredPosition = new Vector2(-110f, -58f);
            homeRect.sizeDelta = new Vector2(260f, 32f);
            matchEventsHomeNameLabel = ManagerUITheme.BuildLabel(homeObj.transform, "", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayObj.transform.SetParent(header.transform, false);
            RectTransform awayRect = awayObj.GetComponent<RectTransform>();
            awayRect.anchorMin = new Vector2(0.5f, 1f);
            awayRect.anchorMax = new Vector2(0.5f, 1f);
            awayRect.pivot = new Vector2(0f, 1f);
            awayRect.anchoredPosition = new Vector2(110f, -58f);
            awayRect.sizeDelta = new Vector2(260f, 32f);
            matchEventsAwayNameLabel = ManagerUITheme.BuildLabel(awayObj.transform, "", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

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
                layoutElement.preferredHeight = 38f;
                layoutElement.flexibleWidth = 1f;

                string text = evt.IsGoal
                    ? $"<b><color=#3ddc84>{evt.Minute}'</color></b>   <b><color=#3ddc84>{evt.Description}</color></b>"
                    : $"{evt.Minute}'   {evt.Description}";

                ManagerUITheme.BuildLabel(row.transform, text, 19, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
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
