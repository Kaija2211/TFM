using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Data;
using Sim;
using Manager.Save;

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
        // 45s (0.5s/simulated minute) read as too fast once Match Log lines got more
        // varied in phrasing/length (see PickVariant in the ManagerSim fork) - bumped to
        // give each event more real time on screen before the next one lands.
        [SerializeField] private float matchReplayDurationSeconds = 60f;
        [SerializeField] private int maxVisibleEventLines = 12;

        [Header("Title Screen")]
        [SerializeField] private GameObject titlePanel;
        [SerializeField] private RectTransform titleContentContainer;

        [Header("Team Select UI")]
        [SerializeField] private GameObject teamSelectPanel;
        [SerializeField] private TMP_InputField managerNameInput;
        [SerializeField] private RectTransform teamGridContainer;
        [SerializeField] private Button teamSelectBackButton;
        [SerializeField] private Button confirmTeamButton; // relabeled "Start Career"/"Continue" depending on teamSelectStep

        // 1 = manager name entry, 2 = club select. Split into two steps so the name
        // field can be a real centered "type your name" screen instead of a squeezed
        // side column, and so a name can actually be required before moving on (it
        // wasn't enforced at all when this was a single combined screen).
        private int teamSelectStep = 1;
        // GameObject, not a cached TextMeshProUGUI - RecoverBlankLabelsNextFrame
        // (see BuildTeamSelectChrome's call to it) can destroy+recreate the TMP
        // component on TMP mesh-generation failure, which would silently orphan a
        // cached component reference. Re-fetching via GetComponentInChildren on this
        // parent GameObject each time sidesteps that, since the parent itself is never
        // destroyed/recreated, only its TMP child component sometimes is.
        private GameObject teamSelectSubtitleObj;
        private GameObject teamSelectNameCaption;
        private GameObject teamSelectClubCaption;

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

        // Mentality buttons are NOT declared here - they're the same
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

        // Mentality can now be changed mid-match too (see SetMentality), not just
        // between matches on the Hub - these same three buttons are reused on the live
        // Match Day screen for that.
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
        // Row-based live event feed (matches the mockup's per-line border-bottom divider)
        // - replaces the old single eventFeedText block, which is still built/reparented
        // by chrome but no longer has its .text touched; this container sits alongside it
        // in the same masked area instead.
        private RectTransform matchEventFeedContainer;
        private readonly Queue<GameObject> matchEventFeedRows = new();
        private RectTransform matchSubsLogContainer;
        private TextMeshProUGUI matchHomeScorersLabel;
        private TextMeshProUGUI matchAwayScorersLabel;
        private RectTransform matchGoalTimelineContainer;
        private float matchGoalTimelineWidth;
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
        private readonly Dictionary<string, ManagerSquadRoles> squadRolesByTeamName = new();
        private readonly ManagerScouting scouting = new();
        private readonly ManagerAcademy academy = new();
        private readonly ManagerLoanTracker loanTracker = new();
        private readonly ManagerClubFinance finance = new();
        private readonly ManagerCareerHistory careerHistory = new();
        private SeasonRecord lastSeasonRecord;
        private bool seasonEndRewardsAppliedForCurrentSeason;
        private readonly AgentSquadGenerator squadGenerator = new();
        private readonly AgentMatchSimulator matchSimulator = new();

        // Own StatisticalModel instance, trained on trainingSeasonFiles only. Completely
        // separate from ResearchEvaluationRunner's own StatisticalModel instance, so
        // nothing here can affect the research evaluation flow or its metrics.
        private readonly StatisticalModel statisticalModel = new();

        private List<OpenFootballMatch> allSeasonFixtures = new();
        private List<OpenFootballMatch> managedTeamFixtures = new();
        private int currentFixtureIndex;
        private ManagerMentality selectedMentality = ManagerMentality.Balanced;
        private readonly ManagerTacticalSliders tacticalSliders = new();

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

        // Developer easter egg - see ApplyDeveloperEasterEggPlayer. A real portrait, only
        // ever shown on this one specific player's Player Detail screen.
        private Sprite hiddePortraitSprite;

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
        private ManagerMentality mentalityUsedForCurrentMatch;
        private bool skipToResultsRequested;

        private bool matchdayPrepChromeBuilt;
        // GameObject, not TextMeshProUGUI - both labels start with text="" (see
        // BuildMatchdayPrepChrome), a prime target for the TMP mesh-generation failure
        // this project has hit before (New Career's subtitle, session 5): the
        // destroy/recreate recovery sweep (RecoverBlankLabelsNextFrame) can silently swap
        // out a label's TextMeshProUGUI component, orphaning any cached reference to it.
        // Caching the parent GameObject and re-fetching GetComponentInChildren fresh each
        // refresh (same fix as teamSelectSubtitleObj) avoids writing to a dead reference
        // while the real on-screen label never updates again - confirmed live as the
        // cause of "matchday prep always shows the very first fixture" (Thomas, 2026-08-09).
        private GameObject matchdayPrepTitleLabel;
        private GameObject matchdayPrepSubtitleLabel;
        private RectTransform matchdayPrepPitchContainer;

        private bool hubChromeBuilt;

        // --- Season loop (career-arc addition, replaces the old dead end where fixtures
        // just ran out and Next Matchday/Simulate Season quietly disabled forever) ---
        private int currentSeason = 1;
        private bool endOfSeasonChromeBuilt;
        private GameObject endOfSeasonPanel;
        private RectTransform endOfSeasonContentContainer;
        private readonly List<GameObject> spawnedEndOfSeasonElements = new();

        // --- Squad: Tactics Board (pitch view, drag-to-sub, formation switching) ---
        private bool tacticsBoardChromeBuilt;
        private GameObject tacticsBoardPanel;
        private RectTransform tacticsBoardPitchContainer;
        private RectTransform tacticsBoardBenchContent;
        private Button tacticsBoardFormationButton;
        private GameObject tacticsBoardFormationDropdown;

        // Injury block warning (backlog item, session 10) - the icon alone (session 9)
        // only made an injured starter visible, it never stopped a manager from
        // dragging one onto the pitch in the first place. Reuses the header band rather
        // than adding a whole new toast system - flat red text that fades in, sits for
        // a few seconds, then clears itself.
        private TextMeshProUGUI tacticsBoardWarningLabel;
        private Coroutine tacticsBoardWarningCoroutine;

        // Tactics screen (session 7) - reached from the Tactics Board via a new TACTICS
        // button beside FORMATION. Centralizes captaincy/set-piece-taker assignment
        // (moved off Player Detail) alongside the new tactical sliders.
        private bool tacticsScreenChromeBuilt;
        private GameObject tacticsScreenPanel;
        private readonly List<GameObject> spawnedTacticsScreenElements = new();
        private readonly List<GameObject> tacticsScreenOpenDropdowns = new();

        // Which screen "Back to Squad" on Player Inspect actually returns to - three
        // possible entry points now that the Squad list screen exists alongside the
        // Tactics Board and the Hub (row-tapped-from-Squad-list vs pin-tapped-from-board
        // need different return screens, and Hub is the fallback for any other caller).
        private enum PlayerInspectReturnTarget { Hub, TacticsBoard, Squad, Scouting, TransferMarket }
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

        // Pre-mentality prediction, kept separately from the two fields above (which
        // already have the current mentality's multiplier baked in) so a mid-match
        // mentality change can recompute cleanly from the original baseline instead of
        // compounding a second modifier on top of the first one's already-adjusted
        // numbers. See SetMentality.
        private float lastRawExpectedHomeGoals;
        private float lastRawExpectedAwayGoals;

        // Starting XI followed by Bench, built fresh each time the inspect screen opens.
        // Overridden to an arbitrary browse list (Scouting/Transfer Market) via
        // OpenPlayerInspect's browseList param - see its comment.
        private List<PlayerAgent> inspectSquadPlayers = new();
        private int inspectPlayerIndex;
        private bool inspectIsOwnSquad = true;

        // Academy focus stats (session 10) - distinct from inspectIsOwnSquad (an
        // academy prospect is never "your own squad" either, same as a Scouting/
        // Transfer target), needed to tell those apart since only an academy prospect
        // gets a focus-stats picker instead of the generic "NOT ON YOUR SQUAD" notice.
        private bool inspectIsAcademyProspect;

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

        // True only for the actual duration of ReplayMatchCoroutine (kickoff to full-
        // time) - see ApplyLiveMentalityChangeIfMatchInProgress, which needs an
        // unambiguous "is a match genuinely in progress right now" signal rather than
        // inferring it from panel active-states (which can be misleading mid-transition,
        // e.g. during OnSimulateMatchClicked's own setup for the *next* match).
        private bool isMatchCurrentlyLive;

        private void Start()
        {
            weakFootStarSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/star-filled");
            footballIconSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/football-icon");
            tfmLogoSprite = Resources.Load<Sprite>("Manager/tfm-logo");
            hiddePortraitSprite = Resources.Load<Sprite>("Manager/hidde_playerportrait");

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
            if (attackingButton != null) attackingButton.onClick.AddListener(SelectAttackingMentality);
            if (balancedButton != null) balancedButton.onClick.AddListener(SelectBalancedMentality);
            if (defensiveButton != null) defensiveButton.onClick.AddListener(SelectDefensiveMentality);
            if (confirmTeamButton != null) confirmTeamButton.onClick.AddListener(OnConfirmTeamClicked);
            if (teamSelectBackButton != null) teamSelectBackButton.onClick.AddListener(OnTeamSelectBackClicked);
            // Live-validates the Continue button as the manager types, rather than only
            // checking on click - see RefreshTeamSelectStepUI.
            if (managerNameInput != null) managerNameInput.onValueChanged.AddListener(_ => RefreshTeamSelectStepUI());
            if (exitToTitleButton != null) exitToTitleButton.onClick.AddListener(OnExitToTitleClicked);

            ApplyManagerUITheme();
            SetMentality(selectedMentality);

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
            // Real now (career-arc addition, session 8, Phase 5) - this button used to
            // have "nothing to actually commit" (see the old comment on the Tactics
            // screen's own SAVE button, same era). Only reachable from the Hub, which
            // means a career is always genuinely in progress here - no guard needed.
            ManagerSaveService.Save(BuildSaveData());

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

            const float subtitleTop = logoTop + logoHeight + 12f;
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

            // Real now (career-arc addition, session 8, Phase 5) - only enabled when a
            // save file actually exists, same "don't fake a feature that doesn't have
            // anything behind it yet" reasoning that kept it disabled before.
            // BuildLabel must run in BOTH branches - NormalizeButtonLabel (which
            // SetDisabledPlaceholder calls internally) only ever UPDATES an existing
            // label via GetComponentInChildren, it never creates one. Splitting this
            // into an if/else and only keeping BuildLabel in the "has save" branch left
            // the common "no save yet" case with a completely unlabeled button - the
            // exact bug Thomas spotted live.
            ManagerUITheme.BuildLabel(loadCareerObj.transform, "LOAD CAREER", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);

            if (ManagerSaveService.HasSaveFile())
            {
                loadCareerButton.onClick.AddListener(OnLoadCareerClicked);
            }
            else
            {
                ManagerUITheme.SetDisabledPlaceholder(loadCareerButton, "LOAD CAREER");
            }

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

        // Same TMP mesh-generation failure as RecoverBlankLabelNextFrame above, but hit
        // here via a fontSize change (live->full-time bump) on labels that were already
        // rendering fine, rather than at creation - confirmed live (characterCount=0 with
        // the correct text still assigned). Needs its own variant instead of just calling
        // the generic helper on matchHomeNameLabel/matchAwayNameLabel because those two
        // fields get their .text reassigned again at the start of every subsequent match
        // (OnSimulateMatchClicked) - the generic helper's destroy/recreate would leave the
        // field pointing at a destroyed component, breaking the *next* match's team names
        // too. This one reassigns the fields to the recreated component instead.
        private IEnumerator RecoverBlankMatchTeamNameLabelsNextFrame()
        {
            yield return null;

            if (matchHomeNameLabel != null && !string.IsNullOrEmpty(matchHomeNameLabel.text))
            {
                matchHomeNameLabel.ForceMeshUpdate();

                if (matchHomeNameLabel.textInfo.characterCount == 0)
                {
                    matchHomeNameLabel = RecreateBlankLabel(matchHomeNameLabel);
                }
            }

            if (matchAwayNameLabel != null && !string.IsNullOrEmpty(matchAwayNameLabel.text))
            {
                matchAwayNameLabel.ForceMeshUpdate();

                if (matchAwayNameLabel.textInfo.characterCount == 0)
                {
                    matchAwayNameLabel = RecreateBlankLabel(matchAwayNameLabel);
                }
            }
        }

        // Destroy/recreate step shared by RecoverBlankMatchTeamNameLabelsNextFrame - pulled
        // out so it can hand back the fresh component for the field reassignment its two
        // call sites each need (see RecoverBlankLabelNextFrame above for why DestroyImmediate
        // specifically is required here).
        private TextMeshProUGUI RecreateBlankLabel(TextMeshProUGUI label)
        {
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

            return fresh;
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

            teamSelectStep = 1;

            if (teamSelectPanel != null) teamSelectPanel.SetActive(true);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshTeamSelectUI();
            RefreshTeamSelectStepUI();
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
            // (`margin:0 auto`), not edge-to-edge - contentLeft marks that centered
            // region's left bound, matching the panel's new width-wide 1920x1080 canvas
            // instead of the old 24px-from-edge layout tuned for 960x540.
            const float contentWidth = 1700f;
            const float contentLeft = (1920f - contentWidth) / 2f;
            const float nameColumnWidth = 340f;

            GameObject header = ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-2f * contentLeft, 40f);
            titleRect.anchoredPosition = new Vector2(contentLeft, -22f);
            ManagerUITheme.BuildLabel(titleObj.transform, "NEW CAREER", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-2f * contentLeft, 24f);
            subtitleRect.anchoredPosition = new Vector2(contentLeft, -60f);
            // Placeholder text - RefreshTeamSelectStepUI overwrites this immediately
            // (ShowTeamSelect calls it right after this method) with the real per-step
            // "Step 1 of 2"/"Step 2 of 2" text.
            ManagerUITheme.BuildLabel(subtitleObj.transform, "Step 1 of 2 · Manager Name", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            teamSelectSubtitleObj = subtitleObj;

            ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: false, height: bandHeight);

            GameObject nameCaption = new GameObject("ManagerNameCaption", typeof(RectTransform));
            nameCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform nameCaptionRect = nameCaption.GetComponent<RectTransform>();
            nameCaptionRect.anchorMin = new Vector2(0f, 1f);
            nameCaptionRect.anchorMax = new Vector2(0f, 1f);
            nameCaptionRect.pivot = new Vector2(0f, 1f);
            nameCaptionRect.sizeDelta = new Vector2(nameColumnWidth, 22f);
            ManagerUITheme.BuildLabel(nameCaption.transform, "MANAGER NAME", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nameCaption.transform.SetAsFirstSibling();
            teamSelectNameCaption = nameCaption;

            GameObject clubCaption = new GameObject("SelectClubCaption", typeof(RectTransform));
            clubCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform clubCaptionRect = clubCaption.GetComponent<RectTransform>();
            clubCaptionRect.anchorMin = new Vector2(0f, 1f);
            clubCaptionRect.anchorMax = new Vector2(0f, 1f);
            clubCaptionRect.pivot = new Vector2(0f, 1f);
            // Full content width, not just the old clubColumnLeft..contentRight span -
            // on step 2 the grid no longer shares the row with a name column, so the
            // caption above it shouldn't either.
            clubCaptionRect.sizeDelta = new Vector2(contentWidth, 18f);
            ManagerUITheme.BuildLabel(clubCaption.transform, "SELECT CLUB · PREMIER LEAGUE", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            clubCaption.transform.SetAsFirstSibling();
            teamSelectClubCaption = clubCaption;

            // managerNameInput and teamGridContainer are Editor-placed objects (an
            // InputField and a Scroll/Grid layout aren't worth rebuilding from scratch
            // in code), but their position/size/color was left to hand-dragging instead
            // of being set here - the exact failure mode this file's other screens
            // deliberately avoid. Margins below match the design mockup's proportions
            // (header-to-caption and caption-to-content gaps, not just a token few px).
            const float captionTop = bandHeight + 40f;

            nameCaptionRect.anchoredPosition = new Vector2(contentLeft, -captionTop);
            clubCaptionRect.anchoredPosition = new Vector2(contentLeft, -captionTop);

            if (managerNameInput != null)
            {
                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();
                // Positioned per-step by RefreshTeamSelectStepUI instead of fixed here -
                // step 1 wants it big and centered, step 2 hides it entirely.

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
                    managerNameInput.textComponent.fontSize = 18;
                    if (TMP_Settings.defaultFontAsset != null) managerNameInput.textComponent.font = TMP_Settings.defaultFontAsset;
                }

                if (managerNameInput.placeholder is TextMeshProUGUI placeholderLabel)
                {
                    placeholderLabel.color = ManagerUITheme.TextMuted;
                    placeholderLabel.fontSize = 18;
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

            // teamGridContainer's position is set per-step by RefreshTeamSelectStepUI
            // instead of fixed here - step 2 stretches it to the full content width now
            // that it no longer shares the row with the name column.

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

            // Same TMP mesh-generation flakiness documented on the Title screen's
            // subtitle/Player Inspect's labels - a freshly AddComponent'd
            // TextMeshProUGUI can come out with correct text/color/position but zero
            // generated characters (confirmed live on this exact screen's "NEW CAREER"
            // title: characterCount=0 despite everything else about it being correct).
            // Not limited to any one label, so this sweeps everything under the panel
            // rather than guessing which one might be affected this time.
            StartCoroutine(RecoverBlankLabelsNextFrame(teamSelectPanel.transform));
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

                ManagerUITheme.BuildLabel(cell.transform, availableTeamNames[i].ToUpperInvariant(), 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);

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

        // Drives the two-step New Career wizard: step 1 is a big centered manager name
        // field (blocks progression until non-empty - this is also what makes a manager
        // name required, which the old single-screen version never enforced at all),
        // step 2 is the club grid stretched to the full content width now that it isn't
        // sharing the row with a name column. Called on every step change and on every
        // keystroke in the name field (see the onValueChanged listener), so it has to
        // stay cheap - just RectTransform/active-state/text updates, no rebuilding.
        private void RefreshTeamSelectStepUI()
        {
            const float bandHeight = 90f;
            const float contentWidth = 1700f;
            const float contentLeft = (1920f - contentWidth) / 2f;
            const float nameColumnWidth = 340f;
            const float captionTop = bandHeight + 40f;
            const float captionHeight = 18f;
            const float contentTop = captionTop + captionHeight + 32f;

            bool isNameStep = teamSelectStep == 1;

            if (teamSelectSubtitleObj != null)
            {
                TextMeshProUGUI subtitleLabel = teamSelectSubtitleObj.GetComponentInChildren<TextMeshProUGUI>();
                if (subtitleLabel != null)
                {
                    subtitleLabel.text = isNameStep
                        ? "Step 1 of 2 · Manager Name"
                        : "Step 2 of 2 · Select Club";
                }
            }

            if (teamSelectClubCaption != null) teamSelectClubCaption.SetActive(!isNameStep);
            if (teamGridContainer != null) teamGridContainer.gameObject.SetActive(!isNameStep);

            if (teamSelectNameCaption != null)
            {
                teamSelectNameCaption.SetActive(isNameStep);

                if (isNameStep)
                {
                    // Re-anchored to sit centered directly above the big centered input
                    // box below, instead of its original top-left chrome position (which
                    // reads as orphaned once the input it labels is no longer nearby).
                    RectTransform captionRect = teamSelectNameCaption.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(captionRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(640f, 24f));

                    TextMeshProUGUI captionLabel = teamSelectNameCaption.GetComponentInChildren<TextMeshProUGUI>();
                    if (captionLabel != null) captionLabel.alignment = TextAlignmentOptions.Center;
                }
            }

            if (managerNameInput != null)
            {
                managerNameInput.gameObject.SetActive(isNameStep);

                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();

                if (isNameStep)
                {
                    // Big and centered in the body area between the header/footer bands -
                    // "a big text input thing in the middle", not squeezed into the old
                    // 340px side column that only existed to share space with the grid.
                    const float bigInputWidth = 640f;
                    const float bigInputHeight = 72f;
                    ManagerUITheme.SetPointAnchor(
                        inputRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(bigInputWidth, bigInputHeight));

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 28;
                    if (managerNameInput.placeholder is TextMeshProUGUI bigPlaceholder) bigPlaceholder.fontSize = 28;
                }
                else
                {
                    ManagerUITheme.SetPointAnchor(
                        inputRect, new Vector2(0f, 1f), new Vector2(contentLeft, -contentTop), new Vector2(nameColumnWidth, 56f));

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 18;
                    if (managerNameInput.placeholder is TextMeshProUGUI smallPlaceholder) smallPlaceholder.fontSize = 18;
                }
            }

            if (teamGridContainer != null && !isNameStep)
            {
                RectTransform gridRect = teamGridContainer.GetComponent<RectTransform>();
                gridRect.anchorMin = new Vector2(0f, 0f);
                gridRect.anchorMax = new Vector2(1f, 1f);
                gridRect.offsetMin = new Vector2(contentLeft, bandHeight + 47f);
                gridRect.offsetMax = new Vector2(-contentLeft, -contentTop);
            }

            if (confirmTeamButton != null)
            {
                TextMeshProUGUI confirmLabel = confirmTeamButton.GetComponentInChildren<TextMeshProUGUI>();
                if (confirmLabel != null) confirmLabel.text = isNameStep ? "CONTINUE" : "START CAREER";

                bool nameFilled = managerNameInput != null && !string.IsNullOrWhiteSpace(managerNameInput.text);
                confirmTeamButton.interactable = !isNameStep || nameFilled;
            }
        }

        public void OnTeamSelectBackClicked()
        {
            if (teamSelectStep == 2)
            {
                teamSelectStep = 1;
                RefreshTeamSelectStepUI();
                return;
            }

            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

            ShowTitleScreen();
        }

        public void OnConfirmTeamClicked()
        {
            if (teamSelectStep == 1)
            {
                if (managerNameInput == null || string.IsNullOrWhiteSpace(managerNameInput.text))
                {
                    return;
                }

                managerName = managerNameInput.text.Trim();
                teamSelectStep = 2;
                RefreshTeamSelectStepUI();
                return;
            }

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

        // --- Mentality selection (Balanced default: no modifier applied). Renamed from
        // "Tactic" - mentality is the real football term for this attacking/balanced/
        // defensive spectrum; "tactic" more naturally implies formation/shape, which
        // this has nothing to do with (see the Tactics Board for that, a completely
        // separate screen). Selectable both pre-match (Hub/Matchday Prep) and now live
        // during a match too - see ApplyLiveMentalityChangeIfMatchInProgress. ---

        public void SelectAttackingMentality() => SetMentality(ManagerMentality.Attacking);
        public void SelectBalancedMentality() => SetMentality(ManagerMentality.Balanced);
        public void SelectDefensiveMentality() => SetMentality(ManagerMentality.Defensive);

        private void SetMentality(ManagerMentality mentality)
        {
            selectedMentality = mentality;

            HighlightSelectedMentalityButton(attackingButton, mentality == ManagerMentality.Attacking);
            HighlightSelectedMentalityButton(balancedButton, mentality == ManagerMentality.Balanced);
            HighlightSelectedMentalityButton(defensiveButton, mentality == ManagerMentality.Defensive);

            ApplyLiveMentalityChangeIfMatchInProgress();
        }

        // A mentality click during a live match now genuinely changes the rest of that
        // match instead of silently only affecting the *next* one (the old "scaffolded
        // mid-match control, v1 scope" limitation) - reuses the exact same resimulation
        // path substitutions already use (TriggerMidMatchResimulation). Recomputed from
        // the stored pre-mentality baseline (lastRawExpectedHomeGoals/AwayGoals, set in
        // SimulateFixture) rather than re-applying the modifier on top of
        // lastExpectedHomeGoals/AwayGoals, which already has whatever mentality was
        // selected at kickoff baked in - reapplying on top of that would compound two
        // modifiers instead of replacing one with the other.
        private void ApplyLiveMentalityChangeIfMatchInProgress()
        {
            // currentFixture is a struct (OpenFootballMatch), always populated by the
            // time isMatchCurrentlyLive can be true - both OnNextMatchdayClicked and
            // OnSimulateMatchClicked set it before a match ever starts - so no separate
            // null check is needed or possible here.
            if (!isMatchCurrentlyLive || lastSimulatedResult == null)
            {
                return;
            }

            float expectedHomeGoals = lastRawExpectedHomeGoals;
            float expectedAwayGoals = lastRawExpectedAwayGoals;

            if (currentFixture.HomeTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else if (currentFixture.AwayTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;
            mentalityUsedForCurrentMatch = selectedMentality;

            TriggerMidMatchResimulation();
        }

        private static void HighlightSelectedMentalityButton(Button button, bool selected)
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
            ManagerUITheme.BuildLabel(nameObj.transform, "", 32, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(nameLeft, -(headerTop + 38f)), new Vector2(600f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

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
                // Real now (career-arc addition, session 8, Phase 3) - was a disabled
                // placeholder with no backing system; StyleHubActionButton/
                // NormalizeButtonLabel match viewSquadButton's own normal (non-disabled)
                // styling instead of SetDisabledPlaceholder.
                ManagerUITheme.SetPointAnchor(transfersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -transfersTop), new Vector2(menuWidth, subRowHeight));
                StyleHubActionButton(transfersButton);
                ManagerUITheme.NormalizeButtonLabel(transfersButton, "TRANSFERS", ManagerUITheme.TextBody, 17);
                transfersButton.onClick.AddListener(OnOpenTransferMarketClicked);
            }

            // SCOUTING (career-arc addition, session 8, Phase 2) - real, unlike the
            // placeholders around it, so built with the same normal-button styling as
            // viewSquadButton rather than SetDisabledPlaceholder.
            float scoutingTop = transfersTop + subRowHeight + rowGap;

            GameObject scoutingObj = new GameObject("ScoutingButton", typeof(RectTransform), typeof(Image), typeof(Button));
            scoutingObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(scoutingObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -scoutingTop), new Vector2(menuWidth, subRowHeight));
            Button scoutingButton = scoutingObj.GetComponent<Button>();
            scoutingButton.targetGraphic = scoutingObj.GetComponent<Image>();
            // BuildLabel first - StyleHubActionButton/NormalizeButtonLabel only ever
            // UPDATE an existing label via GetComponentInChildren, they never create
            // one, unlike viewSquadButton/transfersButton which already had an
            // Editor-placed label to update. A brand-new code-built button has nothing
            // for them to find, so it rendered with no text at all.
            ManagerUITheme.BuildLabel(scoutingObj.transform, "SCOUTING", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(scoutingButton);
            ManagerUITheme.NormalizeButtonLabel(scoutingButton, "SCOUTING", ManagerUITheme.TextBody, 17);
            scoutingButton.onClick.AddListener(OnOpenScoutingClicked);

            // TROPHY ROOM (career-arc addition, session 8, Phase 4) - real, same styling
            // as Squad/Transfers/Scouting rather than a disabled placeholder.
            float trophyRoomTop = scoutingTop + subRowHeight + rowGap;

            GameObject trophyRoomObj = new GameObject("TrophyRoomButton", typeof(RectTransform), typeof(Image), typeof(Button));
            trophyRoomObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(trophyRoomObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -trophyRoomTop), new Vector2(menuWidth, subRowHeight));
            Button trophyRoomButton = trophyRoomObj.GetComponent<Button>();
            trophyRoomButton.targetGraphic = trophyRoomObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(trophyRoomObj.transform, "TROPHY ROOM", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(trophyRoomButton);
            ManagerUITheme.NormalizeButtonLabel(trophyRoomButton, "TROPHY ROOM", ManagerUITheme.TextBody, 17);
            trophyRoomButton.onClick.AddListener(OnOpenTrophyRoomClicked);

            float inboxTop = trophyRoomTop + subRowHeight + rowGap;

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

            // See BuildTeamSelectChrome's identical call for why - the same TMP
            // mesh-generation flakiness can hit any freshly-created label on this
            // screen too.
            StartCoroutine(RecoverBlankLabelsNextFrame(seasonHubPanel.transform));
        }

        // Session 9 bug fix: hubClubNameLabel/hubBylineLabel used to be cached fields,
        // assigned once in BuildHubChrome and reused here on every refresh. Confirmed
        // live to break permanently after the very first matchday - the byline got
        // stuck reading "Matchday 1" forever while the league table correctly advanced
        // to 21+ games played. Root cause: RecoverBlankLabelsNextFrame(seasonHubPanel.
        // transform), the general blank-label recovery sweep also called from this
        // screen, silently destroys and recreates any TMP label under the Hub panel
        // that came out blank (a real, if rare, TMP mesh-generation glitch - see that
        // method's own header comment) without knowing to update either cached field
        // to point at the new component. Once that happened, hubBylineLabel != null
        // simply failed forever after, silently skipping the update block entirely -
        // confirmed via a diagnostic log showing hubBylineLabel null on the very next
        // refresh after the first one. A previous fix attempt (an unconditional async
        // destroy+recreate coroutine every refresh, specifically to keep this field
        // valid) had exactly the failure this replaced: it just moved the same "who
        // recreates it last" race somewhere else instead of removing it. Looking these
        // two up fresh by path every refresh - cheap, this isn't a hot path - sidesteps
        // the whole "stale cached reference to something another mechanism can destroy"
        // problem class entirely, no coroutine required.
        private void RefreshHubUI()
        {
            TextMeshProUGUI clubNameLabel = seasonHubPanel != null
                ? seasonHubPanel.transform.Find("ClubName/Label")?.GetComponent<TextMeshProUGUI>()
                : null;
            TextMeshProUGUI bylineLabel = seasonHubPanel != null
                ? seasonHubPanel.transform.Find("Byline/Label")?.GetComponent<TextMeshProUGUI>()
                : null;

            if (clubNameLabel != null)
            {
                clubNameLabel.text = managedTeamName.ToUpperInvariant();
            }

            if (bylineLabel != null)
            {
                bylineLabel.text = $"Manager {managerName}   ·   Matchday {currentFixtureIndex + 1}";
                bylineLabel.ForceMeshUpdate();
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

            if (!hasNextFixture)
            {
                ShowEndOfSeasonPanel();
                return;
            }

            if (leagueTableView != null)
            {
                int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
                leagueTableView.Populate(playableTable.Sorted(), teamRegistry.GetTeamName, managedTeamId, GetRecentFormString);

                // Rows are cleared and rebuilt fresh every refresh (every return to the
                // Hub) - same rapid destroy/recreate churn as the Tactics Board's
                // pins/bench, same TMP mesh-generation flakiness risk.
                StartCoroutine(RecoverBlankLabelsNextFrame(leagueTableView.transform));
            }
        }

        // --- End of Season (career-arc addition): shown automatically once
        // managedTeamFixtures runs out, in place of the old dead end where Next
        // Matchday/Simulate Season just quietly disabled forever. "Start New Season"
        // performs the full rollover - see OnStartNewSeasonClicked. Built the same
        // code-built-panel/chrome-built-guard/Refresh pattern as the Tactics screen. ---

        private void ShowEndOfSeasonPanel()
        {
            if (!endOfSeasonChromeBuilt)
            {
                BuildEndOfSeasonChrome();
                endOfSeasonChromeBuilt = true;
            }

            // Guarded so re-entering this panel (e.g. RefreshHubUI firing again before
            // Start New Season is clicked) can't pay out prize money/board boost twice
            // for the same season - applied exactly once, the first time the season
            // actually ends.
            if (!seasonEndRewardsAppliedForCurrentSeason)
            {
                ApplySeasonEndRewards();
                seasonEndRewardsAppliedForCurrentSeason = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (endOfSeasonPanel != null) endOfSeasonPanel.SetActive(true);

            RefreshEndOfSeasonUI();
        }

        // Career-arc addition, Phase 4 (session 8) - league finish prize money and a
        // separate board confidence budget boost, both position-scaled (see
        // ManagerCareerHistory), both land in the same transfer budget Phase 3 spends
        // from. Recorded as a SeasonRecord for the Trophy Room regardless of amount -
        // even a poor season's minimal prize money is worth a row in the history.
        private void ApplySeasonEndRewards()
        {
            List<LeagueTable.Entry> finalTable = playableTable.Sorted();
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            int finalPosition = finalTable.Count;

            for (int i = 0; i < finalTable.Count; i++)
            {
                if (finalTable[i].TeamId == managedTeamId)
                {
                    finalPosition = i + 1;
                    break;
                }
            }

            float prizeMoney = ManagerCareerHistory.GetPrizeMoney(finalPosition);
            float boardBoost = ManagerCareerHistory.GetBoardBoost(finalPosition);

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            finance.AdjustBudget(managedTeamName, prizeMoney + boardBoost);

            lastSeasonRecord = new SeasonRecord
            {
                Season = currentSeason,
                FinalPosition = finalPosition,
                IsChampion = finalPosition == 1,
                PrizeMoney = prizeMoney,
                BoardBoost = boardBoost
            };

            careerHistory.AddRecord(lastSeasonRecord);
        }

        private void BuildEndOfSeasonChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            endOfSeasonPanel = new GameObject("EndOfSeasonPanel", typeof(RectTransform));
            endOfSeasonPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = endOfSeasonPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(endOfSeasonPanel);
            ManagerUITheme.ApplyDiagonalGradientBackground(endOfSeasonPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

            GameObject header = ManagerUITheme.BuildAccentBand(endOfSeasonPanel.transform, topBand: true, height: 100f);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(700f, 40f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SEASON COMPLETE", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(endOfSeasonPanel.transform, false);
            endOfSeasonContentContainer = contentObj.GetComponent<RectTransform>();
            endOfSeasonContentContainer.anchorMin = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.anchorMax = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.pivot = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.sizeDelta = new Vector2(700f, 400f);
            endOfSeasonContentContainer.anchoredPosition = new Vector2(0f, 40f);

            ManagerUITheme.BuildAccentBand(endOfSeasonPanel.transform, topBand: false, height: 110f);

            Button startNewSeasonButton = ManagerUITheme.BuildButton(endOfSeasonPanel.transform, "START NEW SEASON", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 17);
            ManagerUITheme.SetPointAnchor(startNewSeasonButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(320f, 52f));
            startNewSeasonButton.onClick.AddListener(OnStartNewSeasonClicked);

            endOfSeasonPanel.SetActive(false);
        }

        // Content (final position, and from Phase 4 onward prize money/board boost/
        // trophy) is rebuilt fresh every time this screen opens, same destroy/recreate
        // pattern as the Tactics screen's dropdown rows - cheap here since it only runs
        // once per season, not on a hot path.
        private void RefreshEndOfSeasonUI()
        {
            if (endOfSeasonContentContainer == null)
            {
                return;
            }

            foreach (GameObject element in spawnedEndOfSeasonElements)
            {
                if (element != null) Destroy(element);
            }
            spawnedEndOfSeasonElements.Clear();

            List<LeagueTable.Entry> finalTable = playableTable.Sorted();
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            int finalPosition = 0;
            for (int i = 0; i < finalTable.Count; i++)
            {
                if (finalTable[i].TeamId == managedTeamId)
                {
                    finalPosition = i + 1;
                    break;
                }
            }

            GameObject seasonLabelObj = new GameObject("SeasonLabel", typeof(RectTransform));
            seasonLabelObj.transform.SetParent(endOfSeasonContentContainer, false);
            RectTransform seasonLabelRect = seasonLabelObj.GetComponent<RectTransform>();
            seasonLabelRect.anchorMin = new Vector2(0.5f, 1f);
            seasonLabelRect.anchorMax = new Vector2(0.5f, 1f);
            seasonLabelRect.pivot = new Vector2(0.5f, 1f);
            seasonLabelRect.sizeDelta = new Vector2(700f, 30f);
            seasonLabelRect.anchoredPosition = Vector2.zero;
            ManagerUITheme.BuildLabel(seasonLabelObj.transform, $"SEASON {currentSeason}", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            spawnedEndOfSeasonElements.Add(seasonLabelObj);

            string positionSuffix = GetOrdinalSuffix(finalPosition);
            GameObject positionObj = new GameObject("Position", typeof(RectTransform));
            positionObj.transform.SetParent(endOfSeasonContentContainer, false);
            RectTransform positionRect = positionObj.GetComponent<RectTransform>();
            positionRect.anchorMin = new Vector2(0.5f, 1f);
            positionRect.anchorMax = new Vector2(0.5f, 1f);
            positionRect.pivot = new Vector2(0.5f, 1f);
            positionRect.sizeDelta = new Vector2(700f, 90f);
            positionRect.anchoredPosition = new Vector2(0f, -46f);
            Color positionColor = finalPosition == 1 ? ManagerUITheme.Accent : ManagerUITheme.TextPrimary;
            string positionText = finalPosition == 1
                ? $"CHAMPIONS! {managedTeamName.ToUpperInvariant()} WIN THE LEAGUE"
                : $"{managedTeamName.ToUpperInvariant()} FINISHED {finalPosition}{positionSuffix}";
            ManagerUITheme.BuildLabel(positionObj.transform, positionText, 26, positionColor, TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);
            spawnedEndOfSeasonElements.Add(positionObj);

            // Prize money and board boost (career-arc addition, Phase 4) - kept as two
            // explicitly separate lines, matching how Thomas framed these as distinct
            // mechanisms even though both land in the same transfer budget.
            if (lastSeasonRecord != null && lastSeasonRecord.Season == currentSeason)
            {
                GameObject prizeObj = new GameObject("PrizeMoney", typeof(RectTransform));
                prizeObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform prizeRect = prizeObj.GetComponent<RectTransform>();
                prizeRect.anchorMin = new Vector2(0.5f, 1f);
                prizeRect.anchorMax = new Vector2(0.5f, 1f);
                prizeRect.pivot = new Vector2(0.5f, 1f);
                prizeRect.sizeDelta = new Vector2(700f, 26f);
                prizeRect.anchoredPosition = new Vector2(0f, -130f);
                ManagerUITheme.BuildLabel(prizeObj.transform, $"Prize money:  £{lastSeasonRecord.PrizeMoney:F1}m", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
                spawnedEndOfSeasonElements.Add(prizeObj);

                GameObject boostObj = new GameObject("BoardBoost", typeof(RectTransform));
                boostObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform boostRect = boostObj.GetComponent<RectTransform>();
                boostRect.anchorMin = new Vector2(0.5f, 1f);
                boostRect.anchorMax = new Vector2(0.5f, 1f);
                boostRect.pivot = new Vector2(0.5f, 1f);
                boostRect.sizeDelta = new Vector2(700f, 26f);
                boostRect.anchoredPosition = new Vector2(0f, -160f);
                string boostText = lastSeasonRecord.BoardBoost > 0f
                    ? $"Board have boosted your transfer budget:  £{lastSeasonRecord.BoardBoost:F1}m"
                    : "Board: no additional backing this season";
                ManagerUITheme.BuildLabel(boostObj.transform, boostText, 17, lastSeasonRecord.BoardBoost > 0f ? ManagerUITheme.Accent : ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
                spawnedEndOfSeasonElements.Add(boostObj);

                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
                float budget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

                GameObject budgetObj = new GameObject("BudgetTotal", typeof(RectTransform));
                budgetObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform budgetRect = budgetObj.GetComponent<RectTransform>();
                budgetRect.anchorMin = new Vector2(0.5f, 1f);
                budgetRect.anchorMax = new Vector2(0.5f, 1f);
                budgetRect.pivot = new Vector2(0.5f, 1f);
                budgetRect.sizeDelta = new Vector2(700f, 26f);
                budgetRect.anchoredPosition = new Vector2(0f, -196f);
                ManagerUITheme.BuildLabel(budgetObj.transform, $"Transfer budget:  £{budget:F1}m", 19, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
                spawnedEndOfSeasonElements.Add(budgetObj);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(endOfSeasonContentContainer));
        }

        private static string GetOrdinalSuffix(int n)
        {
            int lastTwo = n % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return "TH";
            }

            switch (n % 10)
            {
                case 1: return "ST";
                case 2: return "ND";
                case 3: return "RD";
                default: return "TH";
            }
        }

        public void OnStartNewSeasonClicked()
        {
            currentSeason++;
            seasonEndRewardsAppliedForCurrentSeason = false;

            AgeAndReloadFixturesForNewSeason();

            // Reads this season's now-final appearance counts (managed team only) before
            // ResetForNewSeason wipes them below - order matters here.
            ApplyPlayerDevelopmentAndRetirements();

            // Loan system (session 9) - fixed-duration loans (per Thomas: until end of
            // season, no manual recall), so every active loan returns right here.
            ReturnLoanedPlayersForNewSeason();

            DeductManagedTeamWageBill();

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            // Bug fix (session 9, live bug report): playableTable.Reset() above clears
            // PL/GD/PTS for the new season, but recentFormByTeamId was never cleared
            // alongside it - a club continuing into the new season kept showing last
            // season's Form strip (slowly overwritten 5 results at a time) while any
            // club new to this season's fixture file correctly showed blank, making the
            // mismatch obvious side-by-side.
            recentFormByTeamId.Clear();

            currentFixtureIndex = 0;
            simulatedMatchdays.Clear();
            scouting.ForceResolveAllPending();

            foreach (ManagerSquadRoles roles in squadRolesByTeamName.Values)
            {
                roles.ResetForNewSeason();
            }

            if (endOfSeasonPanel != null) endOfSeasonPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Ages every already-generated player (squads and reserve pools alike) by one
        // year, then reloads next season's real fixture calendar - cycling through
        // seasonFile + trainingSeasonFiles (both already real Premier League season
        // files used elsewhere in this controller) rather than replaying the exact same
        // 380 fixtures every year. Falls back to the career's original seasonFile if no
        // pool candidate actually features managedTeamName (a genuine historical season
        // it wasn't in the top flight for), since an empty managedTeamFixtures would
        // otherwise silently break the whole matchday loop.
        private void AgeAndReloadFixturesForNewSeason()
        {
            foreach (AgentTeam team in squadsByTeamName.Values)
            {
                foreach (PlayerAgent player in team.StartingEleven) player.Age += 1;
                foreach (PlayerAgent player in team.Bench) player.Age += 1;
            }

            foreach (List<PlayerAgent> reservePool in reservePoolByTeamName.Values)
            {
                foreach (PlayerAgent player in reservePool) player.Age += 1;
            }

            // Unsigned youth prospects keep developing whether or not you've scouted
            // them yet - procrastinate and a hidden wonderkid becomes obviously great
            // (and obviously expensive) by the time you finally look, real tension for
            // the "discover them early" fantasy. AgeAndExpireProspects (session 10)
            // folds in expiry/refresh on the same tick - a prospect who ages out
            // unbought gets swapped for a fresh 16-19-year-old instead of just getting
            // older forever (see its own comment in ManagerScouting).
            foreach (string region in scouting.GetPoolRegions())
            {
                scouting.GetOrCreateYouthPool(region, squadGenerator);
                scouting.AgeAndExpireProspects(region, squadGenerator);
            }

            // Youth academy (session 9) - same "keeps developing whether or not you're
            // watching" reasoning as the scouting pool above.
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                player.Age += 1;
            }

            List<TextAsset> seasonFilePool = new List<TextAsset>();
            if (seasonFile != null) seasonFilePool.Add(seasonFile);

            if (trainingSeasonFiles != null)
            {
                foreach (TextAsset file in trainingSeasonFiles)
                {
                    if (file == null) continue;

                    bool alreadyInPool = false;
                    foreach (TextAsset existing in seasonFilePool)
                    {
                        if (existing.name == file.name) { alreadyInPool = true; break; }
                    }

                    if (!alreadyInPool) seasonFilePool.Add(file);
                }
            }

            for (int attempt = 0; attempt < seasonFilePool.Count; attempt++)
            {
                int index = (currentSeason - 1 + attempt) % seasonFilePool.Count;
                TextAsset candidate = seasonFilePool[index];
                List<OpenFootballMatch> candidateFixtures = OpenFootballTextParser.ParseSeasonFile(candidate.text, candidate.name);
                List<OpenFootballMatch> candidateManagedFixtures = candidateFixtures.FindAll(m =>
                    m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

                if (candidateManagedFixtures.Count > 0)
                {
                    allSeasonFixtures = candidateFixtures;
                    managedTeamFixtures = candidateManagedFixtures;
                    availableTeamNames = BuildAvailableTeamNames();
                    return;
                }
            }

            allSeasonFixtures = OpenFootballTextParser.ParseSeasonFile(seasonFile.text, seasonFile.name);
            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);
            availableTeamNames = BuildAvailableTeamNames();
        }

        // Applied league-wide (every already-generated club, not just managedTeamName) -
        // otherwise only the user's own squad would ever improve and the league would
        // go static, the opposite of how real football ages. Only the managed team's
        // appearances are actually tracked (see ManagerSquadRoles), so everyone else
        // gets a flat assumed playing-time factor rather than real per-player data -
        // still moves Overall in a realistic direction, just without the extra
        // precision that data doesn't exist for.
        private const float AssumedPlayingTimeFactorAiFirstTeam = 0.65f;
        private const float AssumedPlayingTimeFactorUncalledReserve = 0.15f;
        private const float AssumedPlayingTimeFactorYouthProspect = 0.1f;

        // Higher than the AI-first-team assumption above - the whole point of a loan
        // (session 9) is escaping a bench role for regular minutes elsewhere, which
        // this game doesn't simulate match-by-match, so the assumption reflects that
        // intent directly.
        private const float AssumedPlayingTimeFactorOnLoan = 0.8f;

        private void ApplyPlayerDevelopmentAndRetirements()
        {
            foreach (KeyValuePair<string, AgentTeam> entry in squadsByTeamName)
            {
                string teamName = entry.Key;
                AgentTeam team = entry.Value;
                bool isManagedTeam = teamName == managedTeamName;
                ManagerSquadRoles roles = isManagedTeam ? GetOrCreateSquadRoles(teamName) : null;

                foreach (PlayerAgent player in team.Players)
                {
                    if (isManagedTeam)
                    {
                        // Growth/decline already happened via ApplyMatchdayProgression
                        // ticks all season (see ApplyMatchdayConditionAndInjuries) - only
                        // erosion (a real season-end verdict, not something to tick per
                        // match, see ApplySeasonEndErosion's comment) and the delta-badge
                        // snapshot/finalize pair still happen here.
                        float seasonPlayingTimeFactor = Mathf.Clamp01(roles.GetAppearancesThisSeason(player) / 25f);

                        ManagerPlayerDevelopment.FinalizeSeasonDelta(player);
                        ManagerPlayerDevelopment.ApplySeasonEndErosion(player, seasonPlayingTimeFactor);
                        ManagerPlayerDevelopment.ApplySeasonEndNoiseIfPrimeAge(player);
                        ManagerPlayerDevelopment.SnapshotSeasonStart(player);
                    }
                    else
                    {
                        ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorAiFirstTeam);
                    }
                }

                ApplyRetirementsForTeam(teamName, team);
            }

            foreach (KeyValuePair<string, List<PlayerAgent>> entry in reservePoolByTeamName)
            {
                foreach (PlayerAgent player in entry.Value)
                {
                    ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorUncalledReserve);
                }
            }

            // Unsigned youth prospects (session 8, Phase 2) - no real matches at all, so
            // the lowest playing-time assumption of any pool.
            foreach (string region in scouting.GetPoolRegions())
            {
                foreach (PlayerAgent player in scouting.GetOrCreateYouthPool(region, squadGenerator))
                {
                    ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorYouthProspect);
                }
            }

            // Youth academy (session 9) - same reasoning/rate as unsigned youth
            // prospects above; reuses ManagerPlayerDevelopment's existing Potential/
            // growth system completely unchanged, exactly as agreed when this was
            // first floated, so academy kids visibly grow before they're even
            // promotion-eligible. Focus stats (session 10) ride along on the same call -
            // GetFocusAttributes returns an empty list for a prospect nobody's picked
            // anything for yet, which ApplySeasonProgression already treats as "no
            // doubling" the same as a null set.
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorYouthProspect, academy.GetFocusAttributes(player));
            }
        }

        // Loan system (session 9) - a loaned player isn't in ANY team's Players list
        // right now (removed from the squad entirely by OnLoanOutClicked), so they're
        // untouched by AgeAndReloadFixturesForNewSeason and the per-team loop above -
        // aged and developed here instead, then handed back to their origin squad's
        // Bench (not Starting XI - they need to earn that back, same as any other
        // returning/newly-available player).
        private void ReturnLoanedPlayersForNewSeason()
        {
            List<ManagerLoanTracker.LoanRecord> returned = loanTracker.ReturnAllLoansForNewSeason();

            foreach (ManagerLoanTracker.LoanRecord loan in returned)
            {
                loan.Player.Age += 1;
                ManagerPlayerDevelopment.ApplySeasonProgression(loan.Player, AssumedPlayingTimeFactorOnLoan);

                if (squadsByTeamName.TryGetValue(loan.OriginTeamName, out AgentTeam originTeam))
                {
                    originTeam.AddBenchPlayer(loan.Player);
                }
            }
        }

        // Replaces any retiree in place (whichever list/index they were in - starter,
        // bench, or Players) with a freshly generated player at the same position and
        // current team strength, rather than removing and leaving a hole. Preserves
        // StartingEleven slot order, which the Tactics Board relies on (see AgentTeam.
        // SubstitutePlayer's own comment on the same constraint).
        private void ApplyRetirementsForTeam(string teamName, AgentTeam team)
        {
            List<PlayerAgent> retirees = new List<PlayerAgent>();

            foreach (PlayerAgent player in team.Players)
            {
                if (ManagerPlayerDevelopment.RollRetirement(player))
                {
                    retirees.Add(player);
                }
            }

            if (retirees.Count == 0)
            {
                return;
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);

            foreach (PlayerAgent retiree in retirees)
            {
                PlayerAgent replacement = squadGenerator.GenerateReservePlayer(retiree.PrimaryPosition, strength.AttackStrength, strength.DefenceStrength);

                int startingIndex = team.StartingEleven.IndexOf(retiree);
                int benchIndex = team.Bench.IndexOf(retiree);
                int playersIndex = team.Players.IndexOf(retiree);

                if (startingIndex >= 0)
                {
                    team.StartingEleven[startingIndex] = replacement;
                    replacement.IsStartingEleven = true;
                }
                else if (benchIndex >= 0)
                {
                    team.Bench[benchIndex] = replacement;
                    replacement.IsStartingEleven = false;
                }

                if (playersIndex >= 0)
                {
                    team.Players[playersIndex] = replacement;
                }
            }
        }

        // Only the managed team's budget is ever spent or displayed (see the Transfer
        // Market screen below) - AI clubs never buy or sell anything (explicit scope
        // boundary, see HANDOFF), so there's no point maintaining an accurate wage bill
        // for squads nobody ever checks the finances of.
        private void DeductManagedTeamWageBill()
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team))
            {
                return;
            }

            float totalWage = 0f;
            foreach (PlayerAgent player in team.Players)
            {
                totalWage += ManagerClubFinance.GetAnnualWage(player);
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            finance.AdjustBudget(managedTeamName, -totalWage);
        }

        // --- Save / load (career-arc addition, session 8, Phase 5) - see
        // Manager/Save/ManagerSaveData.cs for the deliberate scope limits (managed team
        // only; condition/injuries/appearances reset). BuildSaveData/ApplySaveData are
        // the only places that translate between live state and the DTOs - everywhere
        // else in this file is untouched by save/load existing at all. ---

        private ManagerSaveData BuildSaveData()
        {
            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            float budget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

            ManagerSaveData data = new ManagerSaveData
            {
                ManagerName = managerName,
                ManagedTeamName = managedTeamName,
                CurrentSeason = currentSeason,
                CurrentFixtureIndex = currentFixtureIndex,
                ActiveSeasonFileName = allSeasonFixtures.Count > 0 ? allSeasonFixtures[0].Season : seasonFile.name,
                ManagedSquad = AgentTeamSaveData.FromTeam(managedTeam),
                ManagedBudget = budget,
                ManagedRoles = new ManagerSquadRolesSaveData
                {
                    CaptainId = roles.Captain?.PlayerId,
                    ViceCaptainId = roles.ViceCaptain?.PlayerId,
                    PenaltyTakerId = roles.PenaltyTaker?.PlayerId,
                    FreeKickTakerId = roles.FreeKickTaker?.PlayerId,
                    LeftCornerTakerId = roles.LeftCornerTaker?.PlayerId,
                    RightCornerTakerId = roles.RightCornerTaker?.PlayerId
                }
            };

            foreach (PlayerAgent player in managedTeam.Players)
            {
                AttackDefendRole role = roles.GetRole(player);
                if (role == AttackDefendRole.Attacking) data.ManagedRoles.AttackingRolePlayerIds.Add(player.PlayerId);
                else if (role == AttackDefendRole.Defensive) data.ManagedRoles.DefensiveRolePlayerIds.Add(player.PlayerId);
            }

            if (reservePoolByTeamName.TryGetValue(managedTeamName, out List<PlayerAgent> reserves))
            {
                foreach (PlayerAgent p in reserves) data.ManagedReservePool.Add(PlayerAgentSaveData.FromPlayer(p));
            }

            // Loan system (session 9) - see ManagerSaveData.LoanedOutPlayers' comment.
            // Only the managed team ever loans a player out in this scope, so no other
            // club's loans need saving.
            foreach (ManagerLoanTracker.LoanRecord loan in loanTracker.ActiveLoans)
            {
                if (loan.OriginTeamName == managedTeamName)
                {
                    data.LoanedOutPlayers.Add(PlayerAgentSaveData.FromPlayer(loan.Player));
                }
            }

            // Youth academy (session 9) - see ManagerSaveData.AcademyPool's comment.
            foreach (PlayerAgent academyProspect in academy.GetAcademyPoolForAging())
            {
                data.AcademyPool.Add(PlayerAgentSaveData.FromPlayer(academyProspect));
            }

            foreach (LeagueTable.Entry entry in playableTable.Sorted())
            {
                data.TableEntries.Add(new LeagueTableEntrySaveData
                {
                    TeamId = entry.TeamId,
                    Played = entry.Played,
                    Wins = entry.Wins,
                    Draws = entry.Draws,
                    Losses = entry.Losses,
                    GoalsFor = entry.GoalsFor,
                    GoalsAgainst = entry.GoalsAgainst,
                    Points = entry.Points
                });
            }

            foreach (SeasonRecord record in careerHistory.Records)
            {
                data.CareerHistory.Add(new SeasonRecordSaveData
                {
                    Season = record.Season,
                    FinalPosition = record.FinalPosition,
                    IsChampion = record.IsChampion,
                    PrizeMoney = record.PrizeMoney,
                    BoardBoost = record.BoardBoost
                });
            }

            foreach (string region in scouting.GetPoolRegions())
            {
                YouthPoolSaveData poolData = new YouthPoolSaveData { Region = region };

                foreach (PlayerAgent prospect in scouting.GetOrCreateYouthPool(region, squadGenerator))
                {
                    poolData.Prospects.Add(PlayerAgentSaveData.FromPlayer(prospect));

                    if (scouting.IsScouted(prospect))
                    {
                        data.ScoutedPlayerIds.Add(prospect.PlayerId);
                    }
                }

                data.YouthPools.Add(poolData);
            }

            return data;
        }

        // Rebuilds every piece of state BuildSaveData captured, then jumps straight to
        // the Season Hub - a loaded career resumes exactly where Save & Exit left it,
        // not back at team select.
        private void ApplySaveData(ManagerSaveData data)
        {
            managerName = data.ManagerName;
            managedTeamName = data.ManagedTeamName;
            currentSeason = data.CurrentSeason;
            currentFixtureIndex = data.CurrentFixtureIndex;

            TextAsset activeFile = FindSeasonFileAssetByName(data.ActiveSeasonFileName) ?? seasonFile;
            allSeasonFixtures = OpenFootballTextParser.ParseSeasonFile(activeFile.text, activeFile.name);
            availableTeamNames = BuildAvailableTeamNames();
            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            // recentFormByTeamId isn't part of the save DTO (same documented scope limit
            // as condition/injuries/appearances not persisting), so clear it here too -
            // otherwise a loaded career could show pre-save Form strips that no longer
            // correspond to anything in the restored fixture list.
            recentFormByTeamId.Clear();
            foreach (LeagueTableEntrySaveData entry in data.TableEntries)
            {
                playableTable.SetEntry(entry.TeamId, entry.Played, entry.Wins, entry.Draws, entry.Losses, entry.GoalsFor, entry.GoalsAgainst, entry.Points);
            }

            squadsByTeamName.Clear();
            reservePoolByTeamName.Clear();
            squadRolesByTeamName.Clear();
            simulatedMatchdays.Clear();
            loanTracker.Clear();
            academy.Clear();

            AgentTeam managedTeam = data.ManagedSquad.ToTeam();
            squadsByTeamName[managedTeamName] = managedTeam;

            Dictionary<string, PlayerAgent> managedPlayersById = new();
            foreach (PlayerAgent p in managedTeam.Players) managedPlayersById[p.PlayerId] = p;

            List<PlayerAgent> restoredReserves = new();
            foreach (PlayerAgentSaveData dto in data.ManagedReservePool) restoredReserves.Add(dto.ToPlayer());
            reservePoolByTeamName[managedTeamName] = restoredReserves;

            // Loan system (session 9) - re-register each restored player as on loan
            // (SendOnLoan rolls a fresh destination flavor name, harmless since it was
            // never saved - cosmetic only) rather than adding them back to
            // managedTeam.Players, since they're still out on loan in the loaded save.
            foreach (PlayerAgentSaveData dto in data.LoanedOutPlayers)
            {
                loanTracker.SendOnLoan(dto.ToPlayer(), managedTeamName);
            }

            // Youth academy (session 9) - only restore if the pool was actually
            // generated before saving (data.AcademyPool.Count > 0). If the player never
            // opened the Academy tab this career, nothing was ever generated to save -
            // restoring an EMPTY list here would still mark the pool as "already
            // created" (GetOrCreateAcademyPool's null-check would never trigger again),
            // permanently freezing it at zero prospects instead of lazily generating
            // fresh ones the first time it's actually opened after loading.
            if (data.AcademyPool.Count > 0)
            {
                List<PlayerAgent> restoredAcademy = new();
                foreach (PlayerAgentSaveData dto in data.AcademyPool) restoredAcademy.Add(dto.ToPlayer());
                academy.RestoreAcademyPool(restoredAcademy);
            }

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            ManagerSquadRolesSaveData rolesData = data.ManagedRoles;

            if (rolesData != null)
            {
                roles.Captain = ResolvePlayerById(managedPlayersById, rolesData.CaptainId);
                roles.ViceCaptain = ResolvePlayerById(managedPlayersById, rolesData.ViceCaptainId);
                roles.PenaltyTaker = ResolvePlayerById(managedPlayersById, rolesData.PenaltyTakerId);
                roles.FreeKickTaker = ResolvePlayerById(managedPlayersById, rolesData.FreeKickTakerId);
                roles.LeftCornerTaker = ResolvePlayerById(managedPlayersById, rolesData.LeftCornerTakerId);
                roles.RightCornerTaker = ResolvePlayerById(managedPlayersById, rolesData.RightCornerTakerId);

                foreach (string id in rolesData.AttackingRolePlayerIds)
                {
                    PlayerAgent p = ResolvePlayerById(managedPlayersById, id);
                    if (p != null) roles.SetRole(p, AttackDefendRole.Attacking);
                }

                foreach (string id in rolesData.DefensiveRolePlayerIds)
                {
                    PlayerAgent p = ResolvePlayerById(managedPlayersById, id);
                    if (p != null) roles.SetRole(p, AttackDefendRole.Defensive);
                }
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            finance.AdjustBudget(managedTeamName, data.ManagedBudget - finance.GetBudget(managedTeamName));

            foreach (SeasonRecordSaveData recordData in data.CareerHistory)
            {
                careerHistory.AddRecord(new SeasonRecord
                {
                    Season = recordData.Season,
                    FinalPosition = recordData.FinalPosition,
                    IsChampion = recordData.IsChampion,
                    PrizeMoney = recordData.PrizeMoney,
                    BoardBoost = recordData.BoardBoost
                });
            }

            HashSet<string> scoutedIds = new HashSet<string>(data.ScoutedPlayerIds);

            foreach (YouthPoolSaveData poolData in data.YouthPools)
            {
                List<PlayerAgent> pool = new List<PlayerAgent>();

                foreach (PlayerAgentSaveData dto in poolData.Prospects)
                {
                    PlayerAgent prospect = dto.ToPlayer();
                    pool.Add(prospect);

                    if (scoutedIds.Contains(prospect.PlayerId))
                    {
                        scouting.RestoreScoutedPlayer(prospect);
                    }
                }

                scouting.RestoreYouthPool(poolData.Region, pool);
            }

            seasonEndRewardsAppliedForCurrentSeason = true;

            ShowSeasonHub();
        }

        private static PlayerAgent ResolvePlayerById(Dictionary<string, PlayerAgent> playersById, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            return playersById.TryGetValue(playerId, out PlayerAgent p) ? p : null;
        }

        private TextAsset FindSeasonFileAssetByName(string fileName)
        {
            if (seasonFile != null && seasonFile.name == fileName) return seasonFile;

            if (trainingSeasonFiles != null)
            {
                foreach (TextAsset file in trainingSeasonFiles)
                {
                    if (file != null && file.name == fileName) return file;
                }
            }

            return null;
        }

        public void OnLoadCareerClicked()
        {
            ManagerSaveData data = ManagerSaveService.Load();
            if (data == null)
            {
                return;
            }

            if (titlePanel != null) titlePanel.SetActive(false);

            ApplySaveData(data);
        }

        // --- Scouting (career-arc addition, session 8, Phase 2): browse every club's
        // hidden youth-prospect pool, assign a scout to reveal a specific player's real
        // Potential (fuzzy range until then). Same code-built-panel/scroll-view pattern
        // as the Squad screen (BuildSquadChrome), reusing SquadListView's flat
        // Populate rather than the grid variant since the label here is a custom
        // composite (name/position/age/club/potential/status) rather than fixed columns. ---

        private bool scoutingChromeBuilt;
        private GameObject scoutingPanel;
        private SquadListView scoutingListView;
        // GameObject, not TextMeshProUGUI - see matchdayPrepTitleLabel's comment. This
        // label starts with text="" at build time (populated later by
        // RefreshScoutingUI), which is exactly the shape that trips the blank-label
        // recovery sweep into destroying/recreating it - a cached TextMeshProUGUI
        // reference would silently start writing to the dead original. Confirmed live
        // (Thomas: byline stuck at "0/2" while the per-row status text updated fine).
        private GameObject scoutingBylineObj;

        // Sortable columns (session 9 - Thomas: "click OVR to sort high to low").
        // -1 = no explicit sort (original generation order). First click on any column
        // defaults to descending (matches "high to low" as the expected first click for
        // a numeric column); clicking the same column again toggles direction.
        private int scoutingSortColumn = -1;
        private bool scoutingSortDescending = true;

        // Youth academy tab (session 9) - shares this screen/list with World Scouting.
        private Button scoutingAcademyTabButton;
        private Button scoutingWorldTabButton;
        private bool scoutingShowingAcademyTab;

        public void OnOpenScoutingClicked()
        {
            if (!scoutingChromeBuilt)
            {
                BuildScoutingChrome();
                scoutingChromeBuilt = true;
            }

            scoutingShowingAcademyTab = false;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (scoutingPanel != null) scoutingPanel.SetActive(true);

            RefreshScoutingUI();
        }

        private void OnScoutingWorldTabClicked()
        {
            scoutingShowingAcademyTab = false;
            RefreshScoutingUI();
        }

        private void OnScoutingAcademyTabClicked()
        {
            scoutingShowingAcademyTab = true;
            RefreshScoutingUI();
        }

        public void OnScoutingBackClicked()
        {
            if (scoutingPanel != null) scoutingPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void BuildScoutingChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            scoutingPanel = new GameObject("ScoutingPanel", typeof(RectTransform));
            scoutingPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = scoutingPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(scoutingPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(scoutingPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SCOUTING", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            scoutingBylineObj = bylineObj;

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnScoutingBackClicked);

            // Youth academy (session 9) - same tab-toggle pattern as Transfer Market's
            // Buy/Sell (see BuildTransferMarketChrome), sharing this one screen/list
            // rather than building an entire second panel from scratch for what's
            // thematically the same "discover/develop young players" concern.
            scoutingAcademyTabButton = ManagerUITheme.BuildButton(header.transform, "ACADEMY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(scoutingAcademyTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            scoutingAcademyTabButton.onClick.AddListener(OnScoutingAcademyTabClicked);

            scoutingWorldTabButton = ManagerUITheme.BuildButton(header.transform, "WORLD SCOUTING", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(scoutingWorldTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-406f, -27f), new Vector2(170f, 36f));
            scoutingWorldTabButton.onClick.AddListener(OnScoutingWorldTabClicked);

            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("ScoutingScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(scoutingPanel.transform, false);
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

            scoutingListView = contentObj.GetComponent<SquadListView>();
            scoutingListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 1f;

            GameObject scrollbarObj = new GameObject("ScoutingScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(scoutingPanel.transform, false);
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
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            StartCoroutine(RecoverBlankLabelsNextFrame(scoutingPanel.transform));
        }

        // Generates every club's youth pool the first time this screen is opened (cheap -
        // object allocation only, no RNG-safety concern this deep into live play) so the
        // list has full league breadth rather than only clubs already encountered via
        // fixtures. Rebuilt fresh every open, same destroy/recreate pattern as every
        // other dynamic list in this file.
        private void RefreshScoutingUI()
        {
            if (scoutingListView == null)
            {
                return;
            }

            if (scoutingWorldTabButton != null && scoutingWorldTabButton.TryGetComponent(out Image worldImage))
            {
                worldImage.color = !scoutingShowingAcademyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(scoutingWorldTabButton, "WORLD SCOUTING", !scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (scoutingAcademyTabButton != null && scoutingAcademyTabButton.TryGetComponent(out Image academyImage))
            {
                academyImage.color = scoutingShowingAcademyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(scoutingAcademyTabButton, "ACADEMY", scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            scoutingListView.Clear();

            if (scoutingShowingAcademyTab)
            {
                RefreshAcademyUI();
            }
            else
            {
                RefreshWorldScoutingUI();
            }
        }

        private void RefreshWorldScoutingUI()
        {
            List<PlayerAgent> allProspects = new List<PlayerAgent>();

            // World-scattered rework (session 9) - unaffiliated free agents pooled by
            // region (see ManagerScouting), not tied to any real Premier League club.
            foreach (string region in ManagerPlayerNationality.AllRegions)
            {
                List<PlayerAgent> pool = scouting.GetOrCreateYouthPool(region, squadGenerator);
                allProspects.AddRange(pool);
            }

            if (scoutingSortColumn >= 0)
            {
                allProspects.Sort((a, b) => CompareScoutingColumn(a, b, scoutingSortColumn, scoutingSortDescending));
            }

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    bylineTMP.text = $"{scouting.ActiveAssignmentCount}/{ManagerScouting.MaxConcurrentAssignments} scouts assigned   ·   reports land after one matchday   ·   click a prospect to assign";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(ScoutingColumnHeaders, ScoutingColumnFractions, OnScoutingColumnHeaderClicked, scoutingSortColumn, scoutingSortDescending);

            foreach (PlayerAgent prospect in allProspects)
            {
                string nation = ManagerPlayerNationality.GetNationality(prospect).Name;
                string status = scouting.IsScouted(prospect) ? "<color=#3ddc84>SCOUTED</color>"
                    : scouting.IsAssigned(prospect) ? "<color=#e8c547>SCOUTING...</color>"
                    : "";

                string[] cells =
                {
                    prospect.Name,
                    prospect.PrimaryPosition.ToString(),
                    prospect.Age.ToString(),
                    nation,
                    prospect.GetOverallRating().ToString("F0"),
                    scouting.GetDisplayPotential(prospect),
                    status
                };

                scoutingListView.AddCustomGridRow(prospect, cells, ScoutingColumnFractions, OnScoutingProspectClicked,
                    onNameClicked: p => OpenScoutedProspectDetail(p, allProspects));
            }
        }

        private static readonly string[] ScoutingColumnHeaders = { "PROSPECT", "POS", "AGE", "NATION", "OVR", "POTENTIAL", "STATUS" };
        private static readonly float[] ScoutingColumnFractions = { 0.20f, 0.07f, 0.07f, 0.22f, 0.09f, 0.14f, 0.21f };

        // Youth academy (session 9) - "grew them myself," complementary to World
        // Scouting's "found them abroad" (see ManagerAcademy). Deliberately no sortable
        // headers here (only 5 slots - sorting adds little for a list that short) and
        // no NATION column (they're your own kids, not a scouted discovery).
        private void RefreshAcademyUI()
        {
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            List<PlayerAgent> pool = academy.GetOrCreateAcademyPool(squadGenerator, strength.AttackStrength, strength.DefenceStrength);

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    bylineTMP.text = $"{ManagerAcademy.AcademySlots} academy prospects   ·   promotable to reserves at age {ManagerAcademy.PromotionAge}   ·   click a promotable prospect to promote";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(AcademyColumnHeaders, AcademyColumnFractions);

            foreach (PlayerAgent prospect in pool)
            {
                bool promotable = academy.CanPromote(prospect);
                string status = promotable ? "<color=#3ddc84>PROMOTABLE</color>" : "DEVELOPING";

                string[] cells =
                {
                    prospect.Name,
                    prospect.PrimaryPosition.ToString(),
                    prospect.Age.ToString(),
                    prospect.GetOverallRating().ToString("F0"),
                    scouting.GetDisplayPotential(prospect),
                    status
                };

                scoutingListView.AddCustomGridRow(prospect, cells, AcademyColumnFractions, OnAcademyProspectClicked,
                    onNameClicked: p => OpenAcademyProspectDetail(p, pool));
            }
        }

        private static readonly string[] AcademyColumnHeaders = { "PROSPECT", "POS", "AGE", "OVR", "POTENTIAL", "STATUS" };
        private static readonly float[] AcademyColumnFractions = { 0.24f, 0.10f, 0.10f, 0.12f, 0.18f, 0.26f };

        private void OnAcademyProspectClicked(PlayerAgent prospect)
        {
            if (academy.TryPromoteToReserves(prospect))
            {
                if (!reservePoolByTeamName.TryGetValue(managedTeamName, out List<PlayerAgent> reserves))
                {
                    reserves = new List<PlayerAgent>();
                    reservePoolByTeamName[managedTeamName] = reserves;
                }

                reserves.Add(prospect);
            }

            RefreshScoutingUI();
        }

        private void OpenAcademyProspectDetail(PlayerAgent prospect, List<PlayerAgent> browseList)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.Scouting;
            OpenPlayerInspect(prospect, browseList, ownSquad: false, isAcademyProspect: true);
        }

        private void OnScoutingColumnHeaderClicked(int column)
        {
            if (scoutingSortColumn == column)
            {
                scoutingSortDescending = !scoutingSortDescending;
            }
            else
            {
                scoutingSortColumn = column;
                scoutingSortDescending = true;
            }

            RefreshScoutingUI();
        }

        // Column indices match ScoutingColumnHeaders. Potential sorts by the same
        // fuzzy-band display string an unscouted prospect already shows (see
        // ManagerScouting.GetDisplayPotential) rather than the true hidden value -
        // sorting shouldn't leak information scouting itself hasn't revealed yet.
        private int CompareScoutingColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    string nationA = ManagerPlayerNationality.GetNationality(a).Name;
                    string nationB = ManagerPlayerNationality.GetNationality(b).Name;
                    result = string.Compare(nationA, nationB, StringComparison.OrdinalIgnoreCase);
                    break;
                case 4:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = GetScoutingPotentialSortKey(a).CompareTo(GetScoutingPotentialSortKey(b));
                    break;
                case 6:
                    result = GetScoutingStatusSortKey(a).CompareTo(GetScoutingStatusSortKey(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private float GetScoutingPotentialSortKey(PlayerAgent prospect)
        {
            string display = scouting.GetDisplayPotential(prospect);
            string firstPart = display.Split('-')[0];
            return float.TryParse(firstPart, out float value) ? value : 0f;
        }

        private int GetScoutingStatusSortKey(PlayerAgent prospect)
        {
            if (scouting.IsScouted(prospect)) return 2;
            if (scouting.IsAssigned(prospect)) return 1;
            return 0;
        }

        private void OnScoutingProspectClicked(PlayerAgent prospect)
        {
            scouting.TryAssignScout(prospect, currentFixtureIndex);
            RefreshScoutingUI();
        }

        // Session 9 - Thomas: "click a prospect's name to see detailed stats" instead of
        // buying/scouting blind off just Age/OVR. browseList is the exact same list
        // (allProspects) Prev/Next will cycle through - browsing every scouted prospect
        // without going back to the list each time. ownSquad:false hides the roles band
        // (captaincy/set-piece/attack-defend) in RefreshPlayerInspectUI - none of that
        // applies to a prospect you don't own yet.
        private void OpenScoutedProspectDetail(PlayerAgent prospect, List<PlayerAgent> browseList)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.Scouting;
            OpenPlayerInspect(prospect, browseList, ownSquad: false);
        }

        // --- Transfer Market (career-arc addition, session 8, Phase 3): Buy tab browses
        // every other club's squad plus already-scouted youth prospects, one-click bid at
        // a competitive 1.15x MarketValue; Sell tab lists only your own Bench (Starting
        // XI deliberately excluded - selling your best XI by a misclick is the one
        // mistake this screen shouldn't let you make casually), one-click sell at 0.9x
        // MarketValue. No AI-vs-AI transfer activity (explicit scope boundary, see
        // HANDOFF) - rival squads only change via progression/retirement, never trading
        // amongst themselves. Same code-built-panel/scroll-view pattern as Squad/
        // Scouting. ---

        private bool transferMarketChromeBuilt;
        private GameObject transferMarketPanel;
        private SquadListView transferMarketListView;
        // GameObject, not TextMeshProUGUI - same reasoning/gotcha as
        // scoutingBylineObj: both start with text="" at build time, which trips the
        // blank-label recovery sweep into destroying/recreating them.
        private GameObject transferMarketBylineObj;
        private GameObject transferMarketStatusLabelObj;
        private Button transferMarketBuyTabButton;
        private Button transferMarketSellTabButton;
        private bool transferMarketShowingBuyTab = true;
        private const float TransferBidMultiplier = 1.15f;

        public void OnOpenTransferMarketClicked()
        {
            if (!transferMarketChromeBuilt)
            {
                BuildTransferMarketChrome();
                transferMarketChromeBuilt = true;
            }

            transferMarketShowingBuyTab = true;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (transferMarketPanel != null) transferMarketPanel.SetActive(true);

            RefreshTransferMarketUI();
        }

        public void OnTransferMarketBackClicked()
        {
            if (transferMarketPanel != null) transferMarketPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void OnTransferMarketBuyTabClicked()
        {
            transferMarketShowingBuyTab = true;
            RefreshTransferMarketUI();
        }

        private void OnTransferMarketSellTabClicked()
        {
            transferMarketShowingBuyTab = false;
            RefreshTransferMarketUI();
        }

        private void BuildTransferMarketChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 110f;

            transferMarketPanel = new GameObject("TransferMarketPanel", typeof(RectTransform));
            transferMarketPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = transferMarketPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(transferMarketPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(transferMarketPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "TRANSFERS", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            transferMarketBylineObj = bylineObj;

            GameObject statusObj = new GameObject("StatusLabel", typeof(RectTransform));
            statusObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(statusObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -84f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(statusObj.transform, "", 14, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft);
            transferMarketStatusLabelObj = statusObj;

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnTransferMarketBackClicked);

            transferMarketBuyTabButton = ManagerUITheme.BuildButton(header.transform, "BUY", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(transferMarketBuyTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            transferMarketBuyTabButton.onClick.AddListener(OnTransferMarketBuyTabClicked);

            transferMarketSellTabButton = ManagerUITheme.BuildButton(header.transform, "SELL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(transferMarketSellTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-406f, -27f), new Vector2(120f, 36f));
            transferMarketSellTabButton.onClick.AddListener(OnTransferMarketSellTabClicked);

            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("TransferScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(transferMarketPanel.transform, false);
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

            transferMarketListView = contentObj.GetComponent<SquadListView>();
            transferMarketListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 1f;

            GameObject scrollbarObj = new GameObject("TransferScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(transferMarketPanel.transform, false);
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
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            StartCoroutine(RecoverBlankLabelsNextFrame(transferMarketPanel.transform));
        }

        private readonly Dictionary<PlayerAgent, string> transferMarketRowClubs = new();

        // Sortable columns (session 9 - Thomas: "click OVR to sort high to low"), same
        // pattern as scoutingSortColumn/scoutingSortDescending above. Separate state per
        // tab since Buy and Sell have different column layouts.
        private int transferBuySortColumn = -1;
        private bool transferBuySortDescending = true;
        private int transferSellSortColumn = -1;
        private bool transferSellSortDescending = true;

        private void RefreshTransferMarketUI()
        {
            if (transferMarketListView == null)
            {
                return;
            }

            StatisticalModel.TeamStrength managedStrength = statisticalModel.GetTeamStrength(managedTeamName);
            float budget = finance.GetOrSeedBudget(managedTeamName, managedStrength.AttackStrength, managedStrength.DefenceStrength);

            if (transferMarketBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = transferMarketBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    bylineTMP.text = $"Transfer budget: £{budget:F1}m";
                }
            }

            if (transferMarketBuyTabButton != null && transferMarketBuyTabButton.TryGetComponent(out Image buyImage))
            {
                buyImage.color = transferMarketShowingBuyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(transferMarketBuyTabButton, "BUY", transferMarketShowingBuyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (transferMarketSellTabButton != null && transferMarketSellTabButton.TryGetComponent(out Image sellImage))
            {
                sellImage.color = !transferMarketShowingBuyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(transferMarketSellTabButton, "SELL", !transferMarketShowingBuyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            transferMarketListView.Clear();
            transferMarketRowClubs.Clear();

            if (transferMarketShowingBuyTab)
            {
                RefreshTransferMarketBuyList(budget);
            }
            else
            {
                RefreshTransferMarketSellList();
            }
        }

        // Grid-column layout (same AddCustomGridRow/AddCustomGridHeaderRow technique
        // already shipped for Scouting - see RefreshScoutingUI) replacing the old flat
        // concatenated-string label, which didn't align into columns since name lengths
        // vary (backlog item, see HANDOFF).
        private static readonly string[] TransferBuyColumnHeaders = { "PLAYER", "POS", "AGE", "CLUB/NATION", "OVR", "BID" };
        private static readonly float[] TransferBuyColumnFractions = { 0.28f, 0.10f, 0.08f, 0.24f, 0.10f, 0.20f };
        private static readonly string[] TransferSellColumnHeaders = { "PLAYER", "POS", "AGE", "OVR", "SELL FOR" };
        private static readonly float[] TransferSellColumnFractions = { 0.34f, 0.14f, 0.12f, 0.14f, 0.26f };

        private void RefreshTransferMarketBuyList(float budget)
        {
            List<PlayerAgent> players = new List<PlayerAgent>();

            foreach (string teamName in availableTeamNames)
            {
                if (teamName == managedTeamName)
                {
                    continue;
                }

                AgentTeam team = GetOrCreateAgentTeam(teamName);

                foreach (PlayerAgent player in team.Players)
                {
                    transferMarketRowClubs[player] = teamName;
                    players.Add(player);
                }
            }

            // World-scattered rework (session 9) - scouted prospects are unaffiliated
            // free agents now, so the shared "CLUB" column shows their nation instead
            // for these rows specifically (same transferMarketRowClubs dictionary, just
            // a different kind of string stored in it - every reader of that dictionary
            // already just displays/sorts whatever string is there, so no other call
            // site needed to change).
            foreach (string region in ManagerPlayerNationality.AllRegions)
            {
                foreach (PlayerAgent prospect in scouting.GetOrCreateYouthPool(region, squadGenerator))
                {
                    if (!scouting.IsScouted(prospect))
                    {
                        continue;
                    }

                    transferMarketRowClubs[prospect] = ManagerPlayerNationality.GetNationality(prospect).Name;
                    players.Add(prospect);
                }
            }

            if (transferBuySortColumn >= 0)
            {
                players.Sort((a, b) => CompareTransferBuyColumn(a, b, transferBuySortColumn, transferBuySortDescending));
            }

            transferMarketListView.AddCustomGridHeaderRow(TransferBuyColumnHeaders, TransferBuyColumnFractions, OnTransferBuyColumnHeaderClicked, transferBuySortColumn, transferBuySortDescending);

            foreach (PlayerAgent player in players)
            {
                string teamName = transferMarketRowClubs.TryGetValue(player, out string t) ? t : "?";
                AddBuyRow(player, teamName, budget, players);
            }
        }

        private void OnTransferBuyColumnHeaderClicked(int column)
        {
            if (transferBuySortColumn == column)
            {
                transferBuySortDescending = !transferBuySortDescending;
            }
            else
            {
                transferBuySortColumn = column;
                transferBuySortDescending = true;
            }

            RefreshTransferMarketUI();
        }

        // Column indices match TransferBuyColumnHeaders.
        private int CompareTransferBuyColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    string clubA = transferMarketRowClubs.TryGetValue(a, out string ca) ? ca : "";
                    string clubB = transferMarketRowClubs.TryGetValue(b, out string cb) ? cb : "";
                    result = string.Compare(clubA, clubB, StringComparison.OrdinalIgnoreCase);
                    break;
                case 4:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = GetTransferAskingPrice(a).CompareTo(GetTransferAskingPrice(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private float GetTransferAskingPrice(PlayerAgent player)
        {
            return ManagerClubFinance.GetMarketValue(player) * TransferBidMultiplier;
        }

        private void AddBuyRow(PlayerAgent player, string teamName, float budget, List<PlayerAgent> browseList)
        {
            float value = ManagerClubFinance.GetMarketValue(player);
            float askingPrice = value * TransferBidMultiplier;
            string bidCell = askingPrice <= budget
                ? $"£{askingPrice:F1}m"
                : $"£{askingPrice:F1}m  <color=#e05a5a>(over budget)</color>";

            string[] cells =
            {
                player.Name,
                player.PrimaryPosition.ToString(),
                player.Age.ToString(),
                teamName,
                player.GetOverallRating().ToString("F0"),
                bidCell
            };

            // Session 9 - Thomas: "click a name to see detailed stats" instead of buying
            // blind off just Age/OVR. See OpenScoutedProspectDetail's comment - same
            // pattern, ownSquad:false since these are other clubs' players.
            transferMarketListView.AddCustomGridRow(player, cells, TransferBuyColumnFractions, OnBuyRowClicked,
                onNameClicked: p => OpenTransferTargetDetail(p, browseList));
        }

        private void OpenTransferTargetDetail(PlayerAgent player, List<PlayerAgent> browseList)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.TransferMarket;
            OpenPlayerInspect(player, browseList, ownSquad: false);
        }

        private void RefreshTransferMarketSellList()
        {
            // GetOrCreateAgentTeam, not a TryGetValue no-op - the managed team's squad
            // may genuinely not exist yet if Transfers is opened before ever viewing
            // Squad or playing a match (squads generate lazily), which would otherwise
            // silently show an empty Sell list instead of your real bench.
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            List<PlayerAgent> players = new List<PlayerAgent>(team.Bench);

            if (transferSellSortColumn >= 0)
            {
                players.Sort((a, b) => CompareTransferSellColumn(a, b, transferSellSortColumn, transferSellSortDescending));
            }

            transferMarketListView.AddCustomGridHeaderRow(TransferSellColumnHeaders, TransferSellColumnFractions, OnTransferSellColumnHeaderClicked, transferSellSortColumn, transferSellSortDescending);

            foreach (PlayerAgent player in players)
            {
                float sellPrice = ManagerClubFinance.GetSellPrice(player);
                string[] cells =
                {
                    player.Name,
                    player.PrimaryPosition.ToString(),
                    player.Age.ToString(),
                    player.GetOverallRating().ToString("F0"),
                    $"£{sellPrice:F1}m"
                };

                // Session 9 - unlike Buy/Scouting, a Sell-list player IS on your own
                // squad, so this opens the normal full Player Detail (roles band and
                // all) rather than the read-only external mode - just returning to
                // Transfers instead of the Hub.
                transferMarketListView.AddCustomGridRow(player, cells, TransferSellColumnFractions, OnSellRowClicked,
                    onNameClicked: p => OpenOwnSquadDetailFromTransferMarket(p, players));
            }
        }

        private void OpenOwnSquadDetailFromTransferMarket(PlayerAgent player, List<PlayerAgent> browseList)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.TransferMarket;
            OpenPlayerInspect(player, browseList, ownSquad: true);
        }

        private void OnTransferSellColumnHeaderClicked(int column)
        {
            if (transferSellSortColumn == column)
            {
                transferSellSortDescending = !transferSellSortDescending;
            }
            else
            {
                transferSellSortColumn = column;
                transferSellSortDescending = true;
            }

            RefreshTransferMarketUI();
        }

        // Column indices match TransferSellColumnHeaders.
        private int CompareTransferSellColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 4:
                    result = ManagerClubFinance.GetSellPrice(a).CompareTo(ManagerClubFinance.GetSellPrice(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private void OnBuyRowClicked(PlayerAgent target)
        {
            string sourceTeam = transferMarketRowClubs.TryGetValue(target, out string t) ? t : null;

            StatisticalModel.TeamStrength managedStrength = statisticalModel.GetTeamStrength(managedTeamName);
            float budget = finance.GetOrSeedBudget(managedTeamName, managedStrength.AttackStrength, managedStrength.DefenceStrength);

            float askingPrice = ManagerClubFinance.GetMarketValue(target) * TransferBidMultiplier;

            if (askingPrice > budget)
            {
                SetTransferMarketStatus($"Can't afford {target.Name} - £{askingPrice:F1}m bid exceeds your £{budget:F1}m budget.");
                return;
            }

            if (!ManagerClubFinance.TryResolveBid(target, askingPrice))
            {
                SetTransferMarketStatus($"{target.Name}'s club rejected your £{askingPrice:F1}m bid.");
                return;
            }

            // Move the player onto the managed squad. Scouted youth prospects live in
            // the scouting pool, not squadsByTeamName - remove from whichever source
            // they actually came from. World-scattered rework (session 9) - sourceTeam
            // is now a NATION name for a scouted prospect (see RefreshTransferMarketBuyList),
            // not a pool key, so which pool to remove from is looked up via the
            // prospect's own REGION instead of trying to reuse that string directly.
            string prospectRegion = ManagerPlayerNationality.GetNationality(target).Region;
            bool wasProspect = scouting.GetOrCreateYouthPool(prospectRegion, squadGenerator).Contains(target);

            if (wasProspect)
            {
                scouting.GetOrCreateYouthPool(prospectRegion, squadGenerator).Remove(target);
            }
            else if (sourceTeam != null && squadsByTeamName.TryGetValue(sourceTeam, out AgentTeam sourceSquad))
            {
                sourceSquad.StartingEleven.Remove(target);
                sourceSquad.Bench.Remove(target);
                sourceSquad.Players.Remove(target);
            }

            finance.AdjustBudget(managedTeamName, -askingPrice);
            GetOrCreateAgentTeam(managedTeamName).AddBenchPlayer(target);

            SetTransferMarketStatus($"Signed {target.Name} for £{askingPrice:F1}m!");
            RefreshTransferMarketUI();
        }

        private void OnSellRowClicked(PlayerAgent target)
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team) || !team.Bench.Contains(target))
            {
                return;
            }

            float sellPrice = ManagerClubFinance.GetSellPrice(target);

            team.Bench.Remove(target);
            team.Players.Remove(target);
            finance.AdjustBudget(managedTeamName, sellPrice);

            SetTransferMarketStatus($"Sold {target.Name} for £{sellPrice:F1}m.");
            RefreshTransferMarketUI();
        }

        private void SetTransferMarketStatus(string message)
        {
            if (transferMarketStatusLabelObj == null)
            {
                return;
            }

            TextMeshProUGUI statusTMP = transferMarketStatusLabelObj.GetComponentInChildren<TextMeshProUGUI>();
            if (statusTMP != null)
            {
                statusTMP.text = message;
            }
        }

        // --- Trophy Room (career-arc addition, session 8, Phase 4): season-by-season
        // history - final position, prize money, board boost, champion highlight. Same
        // code-built-panel/scroll-view pattern as Squad/Scouting/Transfers, but rows are
        // plain labels built directly (SquadListView is PlayerAgent-typed, not
        // applicable to SeasonRecord) rather than via that shared component. ---

        private bool trophyRoomChromeBuilt;
        private GameObject trophyRoomPanel;
        private RectTransform trophyRoomContentContainer;
        private readonly List<GameObject> spawnedTrophyRoomRows = new();

        public void OnOpenTrophyRoomClicked()
        {
            if (!trophyRoomChromeBuilt)
            {
                BuildTrophyRoomChrome();
                trophyRoomChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(true);

            RefreshTrophyRoomUI();
        }

        public void OnTrophyRoomBackClicked()
        {
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void BuildTrophyRoomChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            trophyRoomPanel = new GameObject("TrophyRoomPanel", typeof(RectTransform));
            trophyRoomPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = trophyRoomPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(trophyRoomPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(trophyRoomPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "TROPHY ROOM", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnTrophyRoomBackClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("TrophyScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(trophyRoomPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(sideMargin, 40f);
            scrollViewRect.offsetMax = new Vector2(-sideMargin, -(headerHeight + 40f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(viewportObj.transform, false);
            trophyRoomContentContainer = contentObj.GetComponent<RectTransform>();
            trophyRoomContentContainer.anchorMin = new Vector2(0f, 1f);
            trophyRoomContentContainer.anchorMax = new Vector2(1f, 1f);
            trophyRoomContentContainer.pivot = new Vector2(0.5f, 1f);
            trophyRoomContentContainer.anchoredPosition = Vector2.zero;
            trophyRoomContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 4f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = trophyRoomContentContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 1f;

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomPanel.transform));
        }

        private void RefreshTrophyRoomUI()
        {
            if (trophyRoomContentContainer == null)
            {
                return;
            }

            foreach (GameObject row in spawnedTrophyRoomRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedTrophyRoomRows.Clear();

            spawnedTrophyRoomRows.Add(BuildTrophyRoomHeaderRow());

            if (careerHistory.Records.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(trophyRoomContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "No seasons completed yet - finish your first season to start the history.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedTrophyRoomRows.Add(emptyObj);
            }
            else
            {
                // Most recent season first.
                for (int i = careerHistory.Records.Count - 1; i >= 0; i--)
                {
                    spawnedTrophyRoomRows.Add(BuildTrophyRoomRow(careerHistory.Records[i]));
                }
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomContentContainer));
        }

        private static readonly float[] TrophyRoomColumnFractions = { 0.14f, 0.20f, 0.22f, 0.24f, 0.20f };

        private GameObject BuildTrophyRoomHeaderRow()
        {
            GameObject row = new GameObject("TrophyHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 30f;

            string[] headers = { "SEASON", "POSITION", "PRIZE MONEY", "BOARD BOOST", "" };
            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                GameObject cell = new GameObject($"Header_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + TrophyRoomColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, headers[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += TrophyRoomColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildTrophyRoomRow(SeasonRecord record)
        {
            GameObject row = new GameObject($"Season_{record.Season}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = record.IsChampion ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            string[] values =
            {
                $"Season {record.Season}",
                $"{record.FinalPosition}{GetOrdinalSuffix(record.FinalPosition)}",
                $"£{record.PrizeMoney:F1}m",
                record.BoardBoost > 0f ? $"£{record.BoardBoost:F1}m" : "-",
                record.IsChampion ? "CHAMPIONS" : ""
            };

            Color textColor = record.IsChampion ? ManagerUITheme.Accent : ManagerUITheme.TextBody;
            FontStyles style = record.IsChampion ? FontStyles.Bold : FontStyles.Normal;

            float x = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + TrophyRoomColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, textColor, TextAlignmentOptions.MidlineLeft, style);
                x += TrophyRoomColumnFractions[i];
            }

            return row;
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

            // Just "BACK" rather than "BACK TO HUB" - this same button/label is reused
            // for the mid-match "Make Changes" flow (see OnTacticsBoardBackClicked),
            // where it actually returns to the live match, not the Hub.
            Button backButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "BACK", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
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

            // Session 7 - sliders + captaincy/set-piece-taker assignment, centralized
            // here instead of scattered across each player's own detail page.
            Button tacticsScreenButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "TACTICS", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(tacticsScreenButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-598f, -27f), new Vector2(150f, 36f));
            tacticsScreenButton.onClick.AddListener(OnOpenTacticsScreenClicked);

            // Injury block warning (session 10) - centered under the header row, empty
            // by default (BuildLabel with empty text still reserves the space so it pops
            // in without shifting anything else when a blocked drop fills it).
            GameObject warningObj = new GameObject("InjuryWarning", typeof(RectTransform));
            warningObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform warningRect = warningObj.GetComponent<RectTransform>();
            warningRect.anchorMin = new Vector2(0.5f, 1f);
            warningRect.anchorMax = new Vector2(0.5f, 1f);
            warningRect.pivot = new Vector2(0.5f, 1f);
            warningRect.sizeDelta = new Vector2(700f, 24f);
            warningRect.anchoredPosition = new Vector2(0f, -headerHeight - 14f);
            tacticsBoardWarningLabel = ManagerUITheme.BuildLabel(warningObj.transform, "", 15, ManagerUITheme.Danger, TextAlignmentOptions.Center, FontStyles.Bold);

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
            // BottomToTop, not the seemingly-obvious TopToBottom - ScrollRect's
            // verticalNormalizedPosition convention is 1=viewing the top of the content,
            // 0=viewing the bottom, and it drives the linked Scrollbar's .value directly.
            // Confirmed empirically (not guessed): with TopToBottom, value=1 (viewing the
            // list's top) rendered the handle at the BOTTOM of the track and vice versa -
            // exactly backwards, matching the reported "scroll to the bottom of the
            // scrollbar to see the top of the list" symptom.
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            BuildTacticsBoardFormationDropdown();

            // See BuildTeamSelectChrome's identical call for why. This only catches the
            // static chrome built here (title/buttons/captions) - pins and bench cards
            // are rebuilt fresh on every RefreshTacticsBoardUI call, so that method gets
            // its own sweep too.
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardPanel.transform));
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
            // 2px, not 1px - these lines are declared at the 1920x1080 reference
            // resolution, and CanvasScaler downscales that reference pixel to LESS than
            // one real screen pixel whenever the actual window is smaller (not maximized/
            // fullscreen) - a sub-pixel-wide line at only 10% opacity anti-aliases down to
            // essentially invisible (confirmed live: almost the entire pitch marking set
            // vanished in a windowed, non-maximized Game view). 2px keeps a visible line
            // down to roughly half the reference resolution.
            halfwayRect.sizeDelta = new Vector2(0f, 2f);
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
            // See BuildPitchMarkings' identical comment for why 2px, not 1px.
            edgeRect.sizeDelta = new Vector2(0f, 2f);
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
                sideRect.sizeDelta = new Vector2(2f, 0f);
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
            // Right edge aligned with the Formation button's own right edge (-226, same
            // anchor/pivot), sitting just below its bottom edge (button top -27, height
            // 36, so bottom is -63) - was left at the button's old pre-rework position
            // (-30,-58), which no longer lines up now that the header also has the List
            // View button squeezed in next to it.
            dropdownRect.anchoredPosition = new Vector2(-226f, -66f);
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

        // --- Tactics screen (session 7): sliders + captaincy/set-piece-taker assignment,
        // reached from the Tactics Board. Two independently right/left-edge-anchored
        // columns (not a fixed-width-assumption layout) - see
        // feedback_snapshot_anchor_drift_pattern for why that distinction matters after
        // the Matchday Prep pitch bug found earlier this session.

        public void OnOpenTacticsScreenClicked()
        {
            if (!tacticsScreenChromeBuilt)
            {
                BuildTacticsScreenChrome();
                tacticsScreenChromeBuilt = true;
            }

            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            if (tacticsScreenPanel != null) tacticsScreenPanel.SetActive(true);

            RefreshTacticsScreenUI();
        }

        public void OnTacticsScreenBackClicked()
        {
            if (tacticsScreenPanel != null) tacticsScreenPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);
        }

        private void BuildTacticsScreenChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            tacticsScreenPanel = new GameObject("TacticsScreenPanel", typeof(RectTransform));
            tacticsScreenPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = tacticsScreenPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(tacticsScreenPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(tacticsScreenPanel.transform, topBand: true, height: TacticsScreenHeaderHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -28f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "TACTICS", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(tacticsScreenPanel.transform, "BACK TO TACTICS BOARD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(240f, 36f));
            backButton.onClick.AddListener(OnTacticsScreenBackClicked);

            ManagerUITheme.BuildAccentBand(tacticsScreenPanel.transform, topBand: false, height: TacticsScreenFooterHeight);

            // Everything here is already live the moment you pick it (same immediate-
            // apply pattern as every other assignment this session) - SAVE has nothing
            // to actually commit, it's just a clearly-labeled way back matching the
            // mockup's own footer.
            Button saveButton = ManagerUITheme.BuildButton(tacticsScreenPanel.transform, "SAVE", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(saveButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, 22f), new Vector2(180f, 46f));
            saveButton.onClick.AddListener(OnTacticsScreenBackClicked);

            tacticsScreenPanel.SetActive(false);
        }

        private const float TacticsScreenHeaderHeight = 90f;
        private const float TacticsScreenFooterHeight = 90f;

        private void RefreshTacticsScreenUI()
        {
            if (tacticsScreenPanel == null)
            {
                return;
            }

            foreach (GameObject element in spawnedTacticsScreenElements)
            {
                if (element != null) Destroy(element);
            }

            spawnedTacticsScreenElements.Clear();
            tacticsScreenOpenDropdowns.Clear();

            const float columnTopMargin = 30f;

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            List<PlayerAgent> squadPlayers = new List<PlayerAgent>(team.StartingEleven);
            squadPlayers.AddRange(team.Bench);

            // Matches the actual design mockup (DesignSync, "Football Manager UI
            // Concepts.dc.html", TACTICS frame) exactly: a centered fixed-width row -
            // 520px left column + 80px gap + 2px partition + 80px gap + 820px right
            // column = 1502px total - rather than the earlier version's two
            // independently-edge-anchored fraction-width columns, which was drift-safe
            // but left the columns reading as "far apart" on a real 1920-wide canvas.
            // Anchored from the CENTER (0.5) with fixed pixel offsets on either side of
            // it, not from an edge assuming a literal total canvas width - the center
            // point itself is always correct regardless of true canvas width/aspect
            // ratio, so this is just as immune to the drift bug as the fraction version
            // was, without the oversized gap.
            const float leftColumnWidth = 520f;
            const float rightColumnWidth = 820f;
            const float columnRowGap = 80f;
            const float partitionWidth = 2f;
            const float partitionVerticalInset = 60f;

            float halfTotalWidth = (leftColumnWidth + columnRowGap + partitionWidth + columnRowGap + rightColumnWidth) / 2f;
            float leftColumnLeft = -halfTotalWidth;
            float leftColumnRight = leftColumnLeft + leftColumnWidth;
            float partitionLeft = leftColumnRight + columnRowGap;
            float partitionRight = partitionLeft + partitionWidth;
            float rightColumnLeft = partitionRight + columnRowGap;

            GameObject leftColumn = new GameObject("ShapeApproachColumn", typeof(RectTransform));
            leftColumn.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform leftColumnRect = leftColumn.GetComponent<RectTransform>();
            leftColumnRect.anchorMin = new Vector2(0.5f, 0f);
            leftColumnRect.anchorMax = new Vector2(0.5f, 1f);
            leftColumnRect.offsetMin = new Vector2(leftColumnLeft, TacticsScreenFooterHeight);
            leftColumnRect.offsetMax = new Vector2(leftColumnRight, -(TacticsScreenHeaderHeight + columnTopMargin));
            spawnedTacticsScreenElements.Add(leftColumn);

            GameObject partition = new GameObject("Partition", typeof(RectTransform), typeof(Image));
            partition.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform partitionRect = partition.GetComponent<RectTransform>();
            partitionRect.anchorMin = new Vector2(0.5f, 0f);
            partitionRect.anchorMax = new Vector2(0.5f, 1f);
            partitionRect.offsetMin = new Vector2(partitionLeft, TacticsScreenFooterHeight + partitionVerticalInset);
            partitionRect.offsetMax = new Vector2(partitionRight, -(TacticsScreenHeaderHeight + columnTopMargin + partitionVerticalInset));
            partition.GetComponent<Image>().color = ManagerUITheme.BarTrack;
            spawnedTacticsScreenElements.Add(partition);

            GameObject shapeCaption = new GameObject("Caption", typeof(RectTransform));
            shapeCaption.transform.SetParent(leftColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(shapeCaption, 0f, 20f, 0f);
            ManagerUITheme.BuildLabel(shapeCaption.transform, "SHAPE & APPROACH", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            float sliderTop = 40f;
            sliderTop = BuildSliderRow(leftColumn.transform, "WIDTH", sliderTop,
                new[] { "NARROW", "BALANCED", "WIDE" }, (int)tacticalSliders.Width,
                index => { tacticalSliders.Width = (WidthSetting)index; RefreshTacticsScreenUI(); });

            sliderTop = BuildSliderRow(leftColumn.transform, "DEFENSIVE DEPTH", sliderTop,
                new[] { "DEEP", "BALANCED", "HIGH LINE" }, (int)tacticalSliders.DefensiveDepth,
                index => { tacticalSliders.DefensiveDepth = (DefensiveDepthSetting)index; RefreshTacticsScreenUI(); });

            BuildSliderRow(leftColumn.transform, "TEMPO", sliderTop,
                new[] { "SLOW", "BALANCED", "FAST" }, (int)tacticalSliders.Tempo,
                index => { tacticalSliders.Tempo = (TempoSetting)index; RefreshTacticsScreenUI(); });

            GameObject rightColumn = new GameObject("RoleAssignmentColumn", typeof(RectTransform));
            rightColumn.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform rightColumnRect = rightColumn.GetComponent<RectTransform>();
            rightColumnRect.anchorMin = new Vector2(0.5f, 0f);
            rightColumnRect.anchorMax = new Vector2(0.5f, 1f);
            rightColumnRect.offsetMin = new Vector2(rightColumnLeft, TacticsScreenFooterHeight);
            rightColumnRect.offsetMax = new Vector2(rightColumnLeft + rightColumnWidth, -(TacticsScreenHeaderHeight + columnTopMargin));
            spawnedTacticsScreenElements.Add(rightColumn);

            GameObject leadershipCaption = new GameObject("Caption", typeof(RectTransform));
            leadershipCaption.transform.SetParent(rightColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(leadershipCaption, 0f, 20f, 0f);
            ManagerUITheme.BuildLabel(leadershipCaption.transform, "LEADERSHIP", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Each role shows whichever stats actually matter for it - Leadership/
            // Composure/Age for captaincy (the exact inputs ManagerCaptaincyModifier's
            // suitability score reads), Crossing/Creativity for corners (the exact
            // PickCreatorForChance formula a designated corner taker's stats drive),
            // Free Kicks/Finishing+Composure for the other two, organizational for now
            // but still the honest "this is what a real free-kick/penalty taker needs"
            // proxy - rather than picking blind by name alone.
            static string[] CaptaincySummary(PlayerAgent p) => new[] { $"LDR {p.Leadership:F0}", $"COMP {p.Composure:F0}", $"AGE {p.Age}" };
            static string[] PenaltySummary(PlayerAgent p) => new[] { $"FIN {p.Finishing:F0}", $"COMP {p.Composure:F0}" };
            static string[] FreeKickSummary(PlayerAgent p) => new[] { $"FK {p.FreeKicks:F0}" };
            static string[] CornerSummary(PlayerAgent p) => new[] { $"CRS {p.Crossing:F0}", $"CRE {p.Creativity:F0}" };

            float roleTop = 40f;
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "CAPTAIN", roleTop, roles.Captain, squadPlayers,
                player => AssignRole(SquadRoleSlot.Captain, player), CaptaincySummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "VICE-CAPTAIN", roleTop, roles.ViceCaptain, squadPlayers,
                player => AssignRole(SquadRoleSlot.ViceCaptain, player), CaptaincySummary);

            roleTop += 30f;

            GameObject setPiecesCaption = new GameObject("Caption", typeof(RectTransform));
            setPiecesCaption.transform.SetParent(rightColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(setPiecesCaption, roleTop, 20f, 0f);
            ManagerUITheme.BuildLabel(setPiecesCaption.transform, "SET PIECES", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            roleTop += 30f;

            roleTop = BuildRoleDropdownRow(rightColumn.transform, "PENALTY TAKER", roleTop, roles.PenaltyTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.PenaltyTaker, player), PenaltySummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "FREE-KICK TAKER", roleTop, roles.FreeKickTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.FreeKickTaker, player), FreeKickSummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "LEFT CORNER TAKER", roleTop, roles.LeftCornerTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.LeftCornerTaker, player), CornerSummary);
            BuildRoleDropdownRow(rightColumn.transform, "RIGHT CORNER TAKER", roleTop, roles.RightCornerTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.RightCornerTaker, player), CornerSummary);

            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsScreenPanel.transform));
        }

        // One row: a left-aligned label plus a 3-way toggle-button group, same
        // BuildRoleToggleButton control used for the attack/defend leaning on Player
        // Detail - deliberately not a literal drag Slider widget (the mockup that
        // inspired this screen was a layout suggestion, not a pixel spec) since the
        // backend is three discrete settings either way. Returns the top offset the next
        // row should start at.
        private float BuildSliderRow(Transform parent, string label, float top, string[] optionLabels, int currentIndex, Action<int> onSelect)
        {
            const float labelHeight = 22f;
            const float labelGap = 10f;
            const float buttonHeight = 40f;
            const float rowGap = 30f;

            GameObject labelObj = new GameObject("SliderLabel", typeof(RectTransform));
            labelObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelObj, top, labelHeight, 0f);
            ManagerUITheme.BuildLabel(labelObj.transform, label, 16, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject buttonRow = new GameObject("SliderButtons", typeof(RectTransform));
            buttonRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(buttonRow, top + labelHeight + labelGap, buttonHeight, 0f);

            float x = 0f;

            for (int i = 0; i < optionLabels.Length; i++)
            {
                int capturedIndex = i;
                x = BuildRoleToggleButton(buttonRow.transform, optionLabels[i], x, currentIndex == i, () => onSelect(capturedIndex));
            }

            return top + labelHeight + labelGap + buttonHeight + rowGap;
        }

        // One row: a left-aligned label, a button showing the current holder's name (or
        // "- None -") that toggles a scrollable list of every squad player to pick from.
        // statSummary formats whichever stats actually matter for this specific role
        // (e.g. Leadership/Composure/Age for captaincy, Crossing/Creativity for corners -
        // the same attributes the real formula/mechanism for that role reads, where one
        // exists) - Thomas's point: picking blind by name alone doesn't work for
        // generated players nobody already knows by heart. Returns the top offset the
        // next row should start at.
        private float BuildRoleDropdownRow(Transform parent, string label, float top, PlayerAgent currentValue, List<PlayerAgent> options, Action<PlayerAgent> onSelect, Func<PlayerAgent, string[]> statColumns)
        {
            const float rowHeight = 44f;
            const float rowGap = 14f;

            GameObject rowObj = new GameObject("RoleRow", typeof(RectTransform));
            rowObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(rowObj, top, rowHeight, 0f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(rowObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.42f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelObj.transform, label, 14, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject dropdownButtonObj = new GameObject("DropdownButton", typeof(RectTransform), typeof(Image), typeof(Button));
            dropdownButtonObj.transform.SetParent(rowObj.transform, false);
            RectTransform dropdownButtonRect = dropdownButtonObj.GetComponent<RectTransform>();
            dropdownButtonRect.anchorMin = new Vector2(0.44f, 0f);
            dropdownButtonRect.anchorMax = new Vector2(1f, 1f);
            dropdownButtonRect.offsetMin = Vector2.zero;
            dropdownButtonRect.offsetMax = Vector2.zero;
            Image dropdownButtonImage = dropdownButtonObj.GetComponent<Image>();
            dropdownButtonImage.color = ManagerUITheme.CardNeutral;
            Button dropdownButton = dropdownButtonObj.GetComponent<Button>();
            dropdownButton.targetGraphic = dropdownButtonImage;

            // "v" not "▾" - Oswald SDF has no symbol glyphs at all (same reason the
            // Tactics Board's own formation dropdown uses "v", see its comment). This
            // spot was missed when that fix was applied elsewhere, and kept spamming
            // "character not found" warnings every time this row rebuilt.
            string currentLabel = (currentValue != null ? currentValue.Name : "— None —") + "  v";
            ManagerUITheme.BuildLabel(dropdownButtonObj.transform, currentLabel, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            // Two real bugs, found live and fixed together: (1) the dropdown used to be
            // nested inside its own row's button - Unity draws UI children in sibling
            // order, so it always rendered BEHIND every later row regardless of being
            // "open" (the garbled overlapping list Thomas saw). Building it as a sibling
            // of the row instead (parented to the column) and calling SetAsLastSibling()
            // on open fixes this by construction. (2) the option buttons used to be
            // built eagerly while the panel was still inactive - TMP labels built inside
            // an inactive hierarchy can permanently fail mesh generation (see
            // feedback_tmp_cached_label_reference_gotcha), which is why some rows showed
            // no names at all. Populating them only at the moment the panel actually
            // becomes active sidesteps the bug rather than trying to detect/repair it.
            GameObject dropdownPanel = BuildEmptyDropdownScaffold(parent, options.Count);
            RectTransform dropdownPanelRect = dropdownPanel.GetComponent<RectTransform>();
            dropdownPanelRect.anchorMin = new Vector2(0.44f, 1f);
            dropdownPanelRect.anchorMax = new Vector2(1f, 1f);
            dropdownPanelRect.pivot = new Vector2(0.5f, 1f);
            dropdownPanelRect.anchoredPosition = new Vector2(0f, -(top + rowHeight + 4f));

            Transform dropdownContent = dropdownPanel.transform.Find("Viewport/Content");
            tacticsScreenOpenDropdowns.Add(dropdownPanel);

            dropdownButton.onClick.AddListener(() =>
            {
                bool wasOpen = dropdownPanel.activeSelf;
                CloseAllTacticsDropdowns();

                if (!wasOpen)
                {
                    PopulateDropdownOptions(dropdownContent, options, onSelect, statColumns);
                    dropdownPanel.transform.SetAsLastSibling();
                    dropdownPanel.SetActive(true);
                }
            });

            return top + rowHeight + rowGap;
        }

        private void CloseAllTacticsDropdowns()
        {
            foreach (GameObject dropdown in tacticsScreenOpenDropdowns)
            {
                if (dropdown != null) dropdown.SetActive(false);
            }
        }

        // Scrollable option list scaffold (ScrollRect+Viewport+RectMask2D+Content, same
        // shape as the Tactics Board's own bench rail) rather than a plain unclipped
        // VerticalLayoutGroup - with up to 20 squad players plus "- None -" to choose
        // from, an unclipped list could easily run past the bottom of the screen for
        // whichever role row happens to sit lowest, the same class of overflow bug
        // fixed earlier this session on Matchday Prep's pitch. Deliberately empty - see
        // PopulateDropdownOptions, called only once this actually becomes active.
        private GameObject BuildEmptyDropdownScaffold(Transform parent, int optionCount)
        {
            const float optionHeight = 30f;
            const float maxVisibleHeight = 220f;

            GameObject dropdownPanel = new GameObject("DropdownOptions", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            dropdownPanel.transform.SetParent(parent, false);
            RectTransform dropdownPanelRect = dropdownPanel.GetComponent<RectTransform>();
            dropdownPanelRect.sizeDelta = new Vector2(0f, Mathf.Min(maxVisibleHeight, (optionCount + 1) * optionHeight));
            dropdownPanel.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(dropdownPanel.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, optionHeight);

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = dropdownPanel.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            dropdownPanel.SetActive(false);
            return dropdownPanel;
        }

        // Called only once the dropdown panel is actually active (see
        // BuildRoleDropdownRow's click handler) - building these TMP-labeled buttons
        // while active avoids the inactive-hierarchy mesh generation bug entirely.
        // Clears any previously-populated options first, since a dropdown can be opened
        // more than once across a single Tactics screen visit.
        private static void PopulateDropdownOptions(Transform content, List<PlayerAgent> options, Action<PlayerAgent> onSelect, Func<PlayerAgent, string[]> statColumns)
        {
            const float optionHeight = 30f;

            foreach (Transform child in content)
            {
                Destroy(child.gameObject);
            }

            Button noneOption = ManagerUITheme.BuildButton(content, "— None —", ManagerUITheme.CardNeutral, ManagerUITheme.TextDim, 12);
            noneOption.gameObject.AddComponent<LayoutElement>().preferredHeight = optionHeight;
            noneOption.onClick.AddListener(() => onSelect(null));

            foreach (PlayerAgent option in options)
            {
                BuildOptionRow(content, option.Name, statColumns(option), optionHeight, () => onSelect(option));
            }
        }

        // A real grid row (name cell + up to 3 fixed-width stat cells, same column-
        // fraction technique SquadListView.BuildPlayerGridRow already uses) rather than
        // one concatenated label - Thomas's point: with a single label, the stat values
        // start at a different X per row depending on how long each player's name is, so
        // they never actually line up into columns.
        private static readonly float[] OptionRowColumnFractions = { 0.46f, 0.18f, 0.18f, 0.18f };

        private static void BuildOptionRow(Transform parent, string name, string[] statColumns, float rowHeight, Action onClick)
        {
            GameObject row = new GameObject($"Option_{name}", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = rowHeight;

            Image background = row.GetComponent<Image>();
            background.color = ManagerUITheme.CardNeutral;

            Button button = row.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onClick());

            float x = 0f;
            BuildOptionCell(row.transform, x, OptionRowColumnFractions[0], name, ManagerUITheme.TextBody, FontStyles.Normal);
            x += OptionRowColumnFractions[0];

            for (int i = 0; i < 3; i++)
            {
                string cellText = statColumns != null && i < statColumns.Length ? statColumns[i] : string.Empty;
                BuildOptionCell(row.transform, x, OptionRowColumnFractions[i + 1], cellText, ManagerUITheme.TextMuted, FontStyles.Normal);
                x += OptionRowColumnFractions[i + 1];
            }
        }

        private static void BuildOptionCell(Transform parent, float x, float widthFraction, string text, Color color, FontStyles style)
        {
            GameObject cell = new GameObject("Cell", typeof(RectTransform));
            cell.transform.SetParent(parent, false);

            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(x, 0f);
            cellRect.anchorMax = new Vector2(x + widthFraction, 1f);
            cellRect.offsetMin = new Vector2(8f, 0f);
            cellRect.offsetMax = new Vector2(-4f, 0f);

            ManagerUITheme.BuildLabel(cell.transform, text, 12, color, TextAlignmentOptions.MidlineLeft, style);
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

            // Pins/bench cards are destroyed and rebuilt fresh every time this runs
            // (every formation change, every substitution, every time the board opens) -
            // exactly the kind of rapid destroy/recreate churn that triggers the TMP
            // mesh-generation flakiness documented on Player Inspect's own equivalent
            // sweep.
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardPitchContainer));
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardBenchContent));
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

            // Three tiers now, matching PlayerAgent.GetPositionFit: 1.00 primary or 0.85
            // listed secondary both read as comfortable (plain slot label, no color) -
            // 0.80 "adjacent but never rolled as an actual secondary" (e.g. an LW never
            // got LM) reads as a lenient orange warning - anything below that is a
            // genuinely foreign position, flagged red. Both warning tiers show the
            // player's own true PrimaryPosition rather than the slot's position label -
            // showing "DM" in red for a misplaced ST just relabels the empty slot, not
            // where the manager actually needs to move him; showing "ST" makes that
            // unambiguous. This is purely a visual flag - see ManagerFormationFit for
            // the actual gameplay consequence (which reads the same GetPositionFit
            // value, so the color tier and the real penalty always agree).
            float positionFit = player.GetPositionFit(slotPosition);
            string slotLabel;

            if (positionFit >= 0.85f)
            {
                slotLabel = slotPosition.ToString();
            }
            else if (positionFit >= 0.80f)
            {
                slotLabel = $"<color=#{ColorUtility.ToHtmlStringRGB(ManagerUITheme.Warning)}>{player.PrimaryPosition}</color>";
            }
            else
            {
                slotLabel = $"<color=#{ColorUtility.ToHtmlStringRGB(ManagerUITheme.Danger)}>{player.PrimaryPosition}</color>";
            }

            // Live condition, not just a static Stamina number on Player Detail - reads
            // the exact same GetFatigueMultiplier the sim itself plays the match against
            // (made public in the ManagerSim fork for this). Tints the pin's border
            // (previously always a flat Accent green, purely decorative) instead of
            // adding new pin real estate, which the position-mismatch text already uses
            // for its own separate signal.
            //
            // Gated on isMatchCurrentlyLive rather than assuming currentMatchMinute is 0
            // whenever no match is live - it isn't; ReplayMatchCoroutine only resets it
            // at kickoff, so it's left sitting at ~90 between full-time and the next
            // match's kickoff. Without this gate, players read as still gassed from the
            // *previous* match on the Tactics Board right up until the next one starts
            // (confirmed live - reported as "not sure if this is by design", it wasn't).
            float condition = isMatchCurrentlyLive
                ? matchSimulator.GetFatigueMultiplier(player, currentMatchMinute)
                : 1f;
            Color conditionColor = condition >= 0.95f
                ? ManagerUITheme.Accent
                : condition >= 0.85f
                    ? ManagerUITheme.Warning
                    : ManagerUITheme.Danger;

            // Injury cross (session 9) - the Tactics screen previously had zero injury
            // awareness at all (see feedback in HANDOFF), so a manager could plan a
            // lineup around a player who's silently benched at kickoff. Doesn't block
            // selection yet, just makes it visible where the lineup is actually built.
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            bool isInjured = roles.IsInjured(player, currentFixtureIndex);

            GameObject pinObj = ManagerUITheme.BuildPitchPinVisual(
                tacticsBoardPitchContainer,
                $"Pin_{player.Name}",
                anchor,
                circleSize: 68f,
                borderColor: conditionColor,
                ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                ratingFontSize: 18,
                labelText: $"{player.Name} · {slotLabel}",
                labelFontSize: 14,
                showInjuryIcon: isInjured);

            pinObj.GetComponent<Image>().raycastTarget = true;

            TacticsBoardPlayerCard card = pinObj.AddComponent<TacticsBoardPlayerCard>();
            // isDraggable: true now (was false) - lets a pin be dragged onto another
            // pin to swap their positions, not just a bench card dragged onto a pin.
            card.Configure(player, isDraggable: true, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin, OnPinPlayersSwapped);
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

            // Block, don't just decorate (session 10 - the injury cross icon from
            // session 9 only made an injured starter visible, it never stopped one being
            // dragged into the XI). Only checked here, not in OnPinPlayersSwapped - a
            // pin-to-pin swap never adds anyone to the starting XI who wasn't already in
            // it, so there's nothing new to block there.
            ManagerSquadRoles blockRoles = GetOrCreateSquadRoles(managedTeamName);
            if (blockRoles.IsInjured(benchPlayer, currentFixtureIndex))
            {
                ShowTacticsBoardWarning($"{benchPlayer.Name} is injured and can't start");
                RefreshTacticsBoardUI();
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

                // Fresh legs, fresh fatigue clock - see AgentMatchSimulator.
                // GetFatigueMultiplier's own comment on why this was missing before.
                matchSimulator.RegisterSubstitution(benchPlayer, currentMatchMinute);
                TriggerMidMatchResimulation();
            }

            RefreshTacticsBoardUI();
        }

        // A pin dragged onto another pin - e.g. after a formation change scatters the
        // ST onto the LM spot and vice versa, dragging the ST back onto the ST pin.
        // Both players stay in the starting XI (unlike OnBenchPlayerDroppedOnPin, this
        // never touches the Bench), so it's not logged as a "Subs Made" entry - no sub
        // was used, nobody came off. Still resimulates the rest of a live match though,
        // same as a real substitution would - position genuinely affects the sim now
        // (see ManagerFormationFit), so repositioning players mid-match should too.
        private void OnPinPlayersSwapped(PlayerAgent draggedPlayer, PlayerAgent targetPlayer)
        {
            if (draggedPlayer == targetPlayer)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool applied = team.SwapStartingPositions(draggedPlayer, targetPlayer);

            if (applied && tacticsBoardOpenedMidMatch)
            {
                TriggerMidMatchResimulation();
            }

            RefreshTacticsBoardUI();
        }

        private void ShowTacticsBoardWarning(string message)
        {
            if (tacticsBoardWarningLabel == null)
            {
                return;
            }

            tacticsBoardWarningLabel.text = message;

            if (tacticsBoardWarningCoroutine != null)
            {
                StopCoroutine(tacticsBoardWarningCoroutine);
            }

            tacticsBoardWarningCoroutine = StartCoroutine(ClearTacticsBoardWarningAfterDelay());
        }

        private IEnumerator ClearTacticsBoardWarningAfterDelay()
        {
            yield return new WaitForSeconds(3f);

            if (tacticsBoardWarningLabel != null)
            {
                tacticsBoardWarningLabel.text = "";
            }

            tacticsBoardWarningCoroutine = null;
        }

        // Regenerates the remainder of the currently-live match (from the minute after
        // the change was made) against the current prediction, so a mid-match sub or
        // mentality change (see ApplyLiveMentalityChangeIfMatchInProgress) actually
        // affects the rest of that match's events/result instead of only taking effect
        // from the *next* match onward. lastSimulatedResult is the same object
        // reference ReplayMatchCoroutine holds as its own "result" parameter, so
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
            // See BuildMatchEventsPanel's identical comment for how this was verified -
            // +1 is Unity's own default and is confirmed (via simulated scroll input,
            // not guessed) to move content the correct direction.
            scrollRect.scrollSensitivity = 1f;

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
            // BottomToTop, not the seemingly-obvious TopToBottom - ScrollRect's
            // verticalNormalizedPosition convention is 1=viewing the top of the content,
            // 0=viewing the bottom, and it drives the linked Scrollbar's .value directly.
            // Confirmed empirically (not guessed): with TopToBottom, value=1 (viewing the
            // list's top) rendered the handle at the BOTTOM of the track and vice versa -
            // exactly backwards, matching the reported "scroll to the bottom of the
            // scrollbar to see the top of the list" symptom.
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(squadBrowsePanel.transform));
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
                    squadBrowseByline.text = $"Next: vs {opponentName} ({(managedIsHome ? "H" : "A")})   ·   Formation {formationText}   ·   Mentality: {selectedMentality}";
                }
                else
                {
                    squadBrowseByline.text = $"Season complete   ·   Formation {formationText}   ·   Mentality: {selectedMentality}";
                }
            }

            ManagerSquadRoles squadRoles = GetOrCreateSquadRoles(managedTeamName);

            squadBrowseListView.Clear();
            squadBrowseListView.AddGridHeaderRow();
            squadBrowseListView.AddSectionHeader("Starting XI");

            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                squadBrowseListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles) + BuildFitnessBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, currentFixtureIndex));
            }

            squadBrowseListView.AddSectionHeader($"Bench ({team.Bench.Count})");

            foreach (PlayerAgent player in team.Bench)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles) + BuildFitnessBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, currentFixtureIndex));
            }

            // Rows are cleared and rebuilt fresh every refresh - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(squadBrowsePanel.transform));
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

        // Compact role indicators for the Squad screen's PLAYER cell - "C"/"VC" for
        // captaincy, "PK"/"FK"/"CK" for set-piece takers. Assignment itself happens on
        // The Tactics screen (see BuildTacticsScreenChrome); this is read-only.
        private static string BuildRoleBadgeSuffix(PlayerAgent player, ManagerSquadRoles roles)
        {
            List<string> badges = new();

            if (roles.Captain == player) badges.Add("C");
            if (roles.ViceCaptain == player) badges.Add("VC");
            if (roles.PenaltyTaker == player) badges.Add("PK");
            if (roles.FreeKickTaker == player) badges.Add("FK");
            if (roles.LeftCornerTaker == player) badges.Add("CK-L");
            if (roles.RightCornerTaker == player) badges.Add("CK-R");

            if (badges.Count == 0)
            {
                return string.Empty;
            }

            string accentHex = ColorUtility.ToHtmlStringRGB(ManagerUITheme.Accent);
            return $"  <size=80%><color=#{accentHex}>{string.Join(" ", badges)}</color></size>";
        }

        // Injured takes priority over a plain Condition readout - no point showing a
        // fitness number next to a player who's actually out.
        //
        // Always-visible Condition (backlog item, session 10) - this used to only
        // appear once Condition dropped below 60%, staying an empty string otherwise.
        // Condition genuinely persists matchday-to-matchday, but hiding the number above
        // that threshold meant a manager had no way to see it trending down before it
        // was already a crisis - the whole point of tracking it per-matchday in the
        // first place. Always shown now; color grading (Accent/Warning/Danger by band)
        // keeps a fully-fit player's number calm rather than loud, without hiding it.
        private string BuildFitnessBadgeSuffix(PlayerAgent player, ManagerSquadRoles roles)
        {
            if (roles.IsInjured(player, currentFixtureIndex))
            {
                // No leading "INJ" text anymore - the injury cross icon (see
                // ManagerUITheme.BuildInjuryCrossIcon) already says that visually now;
                // this just adds the one piece of info the icon alone can't carry.
                int returnMatchday = roles.GetInjuryReturnMatchday(player);
                string dangerHex = ColorUtility.ToHtmlStringRGB(ManagerUITheme.Danger);
                return $"  <size=80%><color=#{dangerHex}>(Ret. MD{returnMatchday + 1})</color></size>";
            }

            float condition = roles.GetCondition(player);
            Color conditionColor = condition >= 85f
                ? ManagerUITheme.Accent
                : condition >= 60f
                    ? ManagerUITheme.Warning
                    : ManagerUITheme.Danger;
            string conditionHex = ColorUtility.ToHtmlStringRGB(conditionColor);
            return $"  <size=80%><color=#{conditionHex}>FIT {condition:F0}%</color></size>";
        }

        // --- Player Inspect (Prev/Next once inside; entry point jumps straight to a
        // specific player from the squad browse list - no standalone Hub entry point) ---

        // browseList/ownSquad (session 9 - Thomas: "we need to be able to click on
        // [a Transfer/Scouting target's] name to see detailed stats") let Player Detail
        // browse an arbitrary list instead of always the managed squad - e.g. Prev/Next
        // cycles through the exact Scouting or Transfer Market list you clicked from.
        // Every pre-existing call site omits both and keeps browsing the managed squad
        // exactly as before. ownSquad also gates the roles band in RefreshPlayerInspectUI
        // - captaincy/set-piece/attack-defend assignment only makes sense for a player
        // you actually manage, not someone else's player you're scouting or bidding on.
        private void OpenPlayerInspect(PlayerAgent preselected, List<PlayerAgent> browseList = null, bool ownSquad = true, bool isAcademyProspect = false)
        {
            CleanupStrayDragGhosts();

            if (browseList != null)
            {
                inspectSquadPlayers = browseList;
            }
            else
            {
                AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
                inspectSquadPlayers = new List<PlayerAgent>(team.StartingEleven);
                inspectSquadPlayers.AddRange(team.Bench);
            }

            inspectIsAcademyProspect = isAcademyProspect;

            inspectIsOwnSquad = ownSquad;

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
            // squadBrowsePanel didn't exist when this method was first written - missing
            // here meant opening Player Detail from the Squad list left that screen
            // active underneath (or on top of, depending on sibling order) Player
            // Detail instead of actually navigating away from it.
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(false);
            // Same gap, hit again (session 9 live bug report): scoutingPanel/
            // transferMarketPanel didn't exist when this method was first written
            // either, so clicking a name on either screen correctly opened Player
            // Detail underneath, but the still-active source panel stayed on top and
            // visually hid it - confirmed live: it only appeared after pressing that
            // screen's own Back button, which hid the panel actually covering it.
            if (scoutingPanel != null) scoutingPanel.SetActive(false);
            if (transferMarketPanel != null) transferMarketPanel.SetActive(false);
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
                case PlayerInspectReturnTarget.Scouting:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenScoutingClicked();
                    break;
                case PlayerInspectReturnTarget.TransferMarket:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenTransferMarketClicked();
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
        // no invented descriptive role titles like "Ball-Playing Defender", since this
        // data doesn't track that (Age/Height do exist, see the meta line below).
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
            // player.IsStartingEleven/Bench membership is meaningless for a browsed
            // Scouting/Transfer target that isn't part of the managed squad at all -
            // would otherwise misleadingly default to "Bench" for everyone.
            string squadStatus = !inspectIsOwnSquad
                ? (playerInspectReturnTarget == PlayerInspectReturnTarget.Scouting ? "Scouting Target" : "Transfer Target")
                : player.IsStartingEleven ? "Starting XI" : "Bench";

            // Centered max-width:1600px content region within the full-stretch 1920-wide
            // container, matching the mockup's centered layout instead of edge-to-edge.
            const float contentMargin = (1920f - 1600f) / 2f;

            // Bumped from 130 to 240 - centering the stat columns below (see
            // BuildAttributeColumn) moved the empty space that used to sit below the
            // stats to between the header and the stats instead, since the grid area
            // shrank but the header didn't grow to compensate (confirmed live: a large
            // gap opened up between the header band and "TECHNICAL"). Growing the banner
            // itself - bigger photo/name/meta - eats into that gap directly rather than
            // just relocating it.
            // Bumped again to 300 (2026-08-09) to fit the bigger photo below (220px, was
            // 140) with matching top/bottom margins - user feedback wanted the photo to
            // fill more of its area, twice ("fill in the red borders", then "bigger
            // actually").
            const float headerBandHeight = 300f;

            // A new strip between the header band and the attribute grid for role
            // assignment (captaincy, set-piece takers, attack/defend leaning) - see
            // RolesBand below. Kept as its own band rather than crammed into the header,
            // which already took two rounds of tuning to fit the bigger photo. Taller for
            // an academy prospect (session 10) - the focus-stats picker needs room for a
            // caption line plus a wrapped 2-row chip grid (up to 18 outfield attributes),
            // not just the single row of role toggles/LOAN OUT this band was sized for.
            // Not a const anymore since it now varies, but everything downstream
            // (attributeGridRect below) already reads it as a variable, so the rest of
            // the layout adjusts automatically.
            float rolesBandHeight = inspectIsAcademyProspect ? 130f : 56f;

            // Full-width (no contentMargin) unlike the centered stat grid below it - the
            // margined header looked like it wasn't filling the screen, with visible
            // background peeking on both sides (confirmed live). The name/meta/badges
            // stay left-anchored at their existing offsets, so widening this just gives
            // the right-anchored OVR number more room out toward the true screen edge.
            GameObject headerBand = new GameObject("HeaderBand", typeof(RectTransform), typeof(Image));
            headerBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(headerBand, 0f, headerBandHeight, 0f);
            headerBand.GetComponent<Image>().color = ManagerUITheme.PanelDark;
            spawnedInspectElements.Add(headerBand);

            GameObject photo = new GameObject("PhotoPlaceholder", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(headerBand.transform, false);
            RectTransform photoRect = photo.GetComponent<RectTransform>();
            photoRect.anchorMin = new Vector2(0f, 1f);
            photoRect.anchorMax = new Vector2(0f, 1f);
            photoRect.pivot = new Vector2(0f, 1f);
            photoRect.sizeDelta = new Vector2(220f, 220f);
            photoRect.anchoredPosition = new Vector2(36f, -40f);

            // Developer easter egg (see ApplyDeveloperEasterEggPlayer) - a real portrait
            // for this one specific player, everyone else keeps the plain placeholder
            // color since there's no actual photo pipeline for generated players.
            Image photoImage = photo.GetComponent<Image>();
            if (player.Name == "Hidde Rietberg" && hiddePortraitSprite != null)
            {
                photoImage.sprite = hiddePortraitSprite;
                photoImage.color = Color.white;
                photoImage.preserveAspect = true;
            }
            else
            {
                photoImage.color = ManagerUITheme.CardNeutralAlt;
            }

            // Start-x and sizeDelta shrink both bumped from 200/-320 to 300/-420 to clear
            // the wider photo (220px, was 140) with the same ~44px gap after it.
            GameObject nameLabel = new GameObject("Name", typeof(RectTransform));
            nameLabel.transform.SetParent(headerBand.transform, false);
            RectTransform nameRect = nameLabel.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.sizeDelta = new Vector2(-420f, 40f);
            nameRect.anchoredPosition = new Vector2(300f, -60f);
            ManagerUITheme.BuildLabel(nameLabel.transform, player.Name.ToUpperInvariant(), 32, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject metaLabel = new GameObject("Meta", typeof(RectTransform));
            metaLabel.transform.SetParent(headerBand.transform, false);
            RectTransform metaRect = metaLabel.GetComponent<RectTransform>();
            metaRect.anchorMin = new Vector2(0f, 1f);
            metaRect.anchorMax = new Vector2(1f, 1f);
            metaRect.pivot = new Vector2(0f, 1f);
            metaRect.sizeDelta = new Vector2(-420f, 34f);
            metaRect.anchoredPosition = new Vector2(300f, -116f);
            string nationalityName = ManagerPlayerNationality.GetNationality(player).Name;
            string metaText = $"{player.Role}  ·  {nationalityName}  ·  {player.Age} yrs  ·  {player.Height:F0}cm  ·  Weak Foot: {BuildFootRating(player.WeakFoot)}  ·  Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})";
            TextMeshProUGUI metaTMP = ManagerUITheme.BuildLabel(metaLabel.transform, metaText, 21, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            if (weakFootStarSpriteAsset != null) metaTMP.spriteAsset = weakFootStarSpriteAsset;

            float badgeX = 300f;
            AddPositionBadge(headerBand.transform, player.PrimaryPosition.ToString(), badgeX, true);
            badgeX += 68f;

            foreach (PlayerPosition secondary in player.SecondaryPositions)
            {
                AddPositionBadge(headerBand.transform, secondary.ToString(), badgeX, false);
                badgeX += 68f;
            }

            int displayRating = GetDisplayRating(player.GetOverallRating());

            GameObject ovrValue = new GameObject("OvrValue", typeof(RectTransform));
            ovrValue.transform.SetParent(headerBand.transform, false);
            RectTransform ovrRect = ovrValue.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(1f, 1f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.pivot = new Vector2(1f, 1f);
            ovrRect.sizeDelta = new Vector2(120f, 64f);
            ovrRect.anchoredPosition = new Vector2(-36f, -36f);
            ManagerUITheme.BuildLabel(ovrValue.transform, displayRating.ToString(), 56, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject ovrCaption = new GameObject("OvrCaption", typeof(RectTransform));
            ovrCaption.transform.SetParent(headerBand.transform, false);
            RectTransform ovrCaptionRect = ovrCaption.GetComponent<RectTransform>();
            ovrCaptionRect.anchorMin = new Vector2(1f, 1f);
            ovrCaptionRect.anchorMax = new Vector2(1f, 1f);
            ovrCaptionRect.pivot = new Vector2(1f, 1f);
            ovrCaptionRect.sizeDelta = new Vector2(180f, 18f);
            ovrCaptionRect.anchoredPosition = new Vector2(-36f, -106f);
            ManagerUITheme.BuildLabel(ovrCaption.transform, $"OVERALL ({player.PrimaryPosition})", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            // Always-visible Condition (backlog item, session 10) - Squad Browse's own
            // "FIT XX%" suffix (see BuildFitnessBadgeSuffix) used to be the only place
            // Condition showed at all, and only once it dropped below 60% - Player
            // Detail had no raw number anywhere. Gated on inspectIsOwnSquad like the
            // roles band below: Condition isn't tracked for browsed Scouting/Transfer
            // targets or other clubs' players (ApplyMatchdayConditionAndInjuries only
            // ticks the managed squad), so GetCondition would just silently read back
            // its 100f default for anyone else - showing that as a real number would be
            // misleading rather than merely absent.
            if (inspectIsOwnSquad)
            {
                float condition = GetOrCreateSquadRoles(managedTeamName).GetCondition(player);
                Color conditionColor = condition >= 85f
                    ? ManagerUITheme.Accent
                    : condition >= 60f
                        ? ManagerUITheme.Warning
                        : ManagerUITheme.Danger;

                GameObject conditionCaption = new GameObject("ConditionCaption", typeof(RectTransform));
                conditionCaption.transform.SetParent(headerBand.transform, false);
                RectTransform conditionRect = conditionCaption.GetComponent<RectTransform>();
                conditionRect.anchorMin = new Vector2(1f, 1f);
                conditionRect.anchorMax = new Vector2(1f, 1f);
                conditionRect.pivot = new Vector2(1f, 1f);
                conditionRect.sizeDelta = new Vector2(180f, 18f);
                conditionRect.anchoredPosition = new Vector2(-36f, -126f);
                ManagerUITheme.BuildLabel(conditionCaption.transform, $"CONDITION {condition:F0}%", 13, conditionColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            }

            // In-season delta (career arc backlog item, session 9/10) - a small badge
            // tucked into the top-right corner of the OVR number itself (live feedback:
            // a full "+3 LAST SEASON" text line read as too heavy - just the signed
            // number, right where you're already looking, reads faster). Switched from
            // GetLastSeasonOverallDelta to GetCurrentSeasonOverallDelta (session 10) -
            // the old one only updated at rollover, so it sat frozen showing last
            // season's final number for the entire following season even though growth
            // now ticks per matchday. Live version climbs in real time as ticks land and
            // resets to 0 right at rollover. Hidden entirely rather than showing "+0",
            // since a brand-new player (just scouted/signed/promoted) genuinely has no
            // season-start snapshot to compare against yet, and "+0" would misleadingly
            // read as "no growth this season" instead.
            int overallDelta = ManagerPlayerDevelopment.GetCurrentSeasonOverallDelta(player);
            if (overallDelta != 0)
            {
                GameObject ovrDelta = new GameObject("OvrDelta", typeof(RectTransform));
                ovrDelta.transform.SetParent(headerBand.transform, false);
                RectTransform ovrDeltaRect = ovrDelta.GetComponent<RectTransform>();
                ovrDeltaRect.anchorMin = new Vector2(1f, 1f);
                ovrDeltaRect.anchorMax = new Vector2(1f, 1f);
                ovrDeltaRect.pivot = new Vector2(1f, 1f);
                ovrDeltaRect.sizeDelta = new Vector2(44f, 22f);
                ovrDeltaRect.anchoredPosition = new Vector2(-8f, -18f);
                string deltaSign = overallDelta > 0 ? "+" : "";
                Color deltaColor = overallDelta > 0 ? ManagerUITheme.Accent : ManagerUITheme.Danger;
                ManagerUITheme.BuildLabel(ovrDelta.transform, $"{deltaSign}{overallDelta}", 17, deltaColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            }

            // Captain/vice-captain/penalty/free-kick/corner-taker assignment moved to the
            // Tactics screen (see BuildTacticsScreenChrome) - a centralized dropdown-
            // picker layout reads better than clicking into each individual player's own
            // page to toggle their role. Per-player attack/defend leaning stays here
            // though, since it's inherently about this one specific player rather than a
            // single-holder-per-team assignment, and wasn't part of that redesign.
            GameObject rolesBand = new GameObject("AttackDefendBand", typeof(RectTransform));
            rolesBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(rolesBand, headerBandHeight, rolesBandHeight, contentMargin);
            spawnedInspectElements.Add(rolesBand);

            // Captaincy/set-piece/attack-defend assignment only makes sense for a player
            // you actually manage - a Scouting/Transfer target browsed via
            // OpenPlayerInspect's browseList (session 9) isn't part of the managed squad
            // at all, so ManagerSquadRoles has no real state for them and toggling a role
            // here would incorrectly start tracking one. Same band height reserved either
            // way (see attributeGridRect below) so the layout doesn't shift.
            if (inspectIsOwnSquad)
            {
                ManagerSquadRoles squadRoles = GetOrCreateSquadRoles(managedTeamName);
                AttackDefendRole currentAttackDefendRole = squadRoles.GetRole(player);

                // Which leanings even make tactical sense varies by position - a winger
                // "defending" or a centre-back "attacking" isn't a real football
                // instruction the way it is for a fullback or a central midfielder.
                // Restricted per position rather than offering all three everywhere;
                // goalkeepers don't get the control at all, since it doesn't apply to
                // them.
                AttackDefendRole[] allowedRoles = GetAllowedAttackDefendRoles(player.PrimaryPosition);
                float roleX = 0f;

                foreach (AttackDefendRole allowedRole in allowedRoles)
                {
                    roleX = BuildRoleToggleButton(rolesBand.transform, allowedRole.ToString().ToUpperInvariant(), roleX, currentAttackDefendRole == allowedRole, () => SetAttackDefendRole(player, allowedRole));
                }

                // Loan system (session 9) - right-anchored so it sits at the far edge of
                // the band regardless of how many attack/defend toggles are on the left
                // (goalkeepers get none at all - see GetAllowedAttackDefendRoles).
                Button loanButton = ManagerUITheme.BuildButton(rolesBand.transform, "LOAN OUT", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(loanButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(130f, 40f));
                loanButton.onClick.AddListener(() => OnLoanOutClicked(player));
            }
            else if (inspectIsAcademyProspect)
            {
                BuildFocusStatsPicker(rolesBand.transform, player);
            }
            else
            {
                ManagerUITheme.BuildLabel(rolesBand.transform, "NOT ON YOUR SQUAD - VIEW ONLY", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            }

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
            attributeGridRect.offsetMax = new Vector2(-(contentMargin + 20f), -(headerBandHeight + rolesBandHeight + 20f));

            if (player.PrimaryPosition == PlayerPosition.GK)
            {
                // GKs never roll meaningful Finishing/Dribbling/Crossing/Heading/Creativity/
                // Tackling values (see AgentSquadGenerator.GenerateGoalkeeper - those stay at
                // low dump-stat floors), so showing them here was always a bit dishonest.
                // Goalkeeping/Reflexes are the two stats actually generated for and used by
                // a keeper (AgentMatchSimulator's shot-stopping resolution) but were never
                // surfaced anywhere in the UI until now.
                BuildAttributeColumn(attributeGridRect, 0, 4, "Goalkeeping", new (string, float)[]
                {
                    ("Goalkeeping", player.Goalkeeping), ("Reflexes", player.Reflexes)
                });

                BuildAttributeColumn(attributeGridRect, 1, 4, "Mental", new (string, float)[]
                {
                    ("Positioning", player.Positioning), ("Composure", player.Composure), ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 4, "Distribution", new (string, float)[]
                {
                    ("Passing", player.Passing)
                });

                BuildAttributeColumn(attributeGridRect, 3, 4, "Physical", new (string, float)[]
                {
                    ("Pace", player.Pace), ("Strength", player.Strength), ("Stamina", player.Stamina), ("Aerial", player.Aerial)
                });
            }
            else
            {
                BuildAttributeColumn(attributeGridRect, 0, 4, "Technical", new (string, float)[]
                {
                    ("Finishing", player.Finishing), ("Passing", player.Passing), ("Dribbling", player.Dribbling),
                    ("Crossing", player.Crossing), ("Heading", player.Heading), ("Long Shots", player.LongShots),
                    ("Through Balls", player.ThroughBalls), ("Free Kicks", player.FreeKicks)
                });

                BuildAttributeColumn(attributeGridRect, 1, 4, "Mental", new (string, float)[]
                {
                    ("Creativity", player.Creativity), ("Positioning", player.Positioning), ("Composure", player.Composure),
                    ("Off The Ball", player.OffTheBall), ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 4, "Defensive", new (string, float)[]
                {
                    ("Defending", player.Defending), ("Tackling", player.Tackling), ("Marking", player.Marking)
                });

                BuildAttributeColumn(attributeGridRect, 3, 4, "Physical", new (string, float)[]
                {
                    ("Pace", player.Pace), ("Strength", player.Strength), ("Stamina", player.Stamina), ("Aerial", player.Aerial)
                });
            }

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
            rect.sizeDelta = new Vector2(60f, 28f);
            rect.anchoredPosition = new Vector2(x, -150f);

            badge.GetComponent<Image>().color = primary ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            ManagerUITheme.BuildLabel(
                badge.transform,
                label,
                14,
                primary ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody,
                TextAlignmentOptions.Center,
                FontStyles.Bold);
        }

        // Which single-holder-per-team slot a RolesBand button toggles - see
        // AssignRole. Attack/defend role isn't here since it's per-player rather than
        // single-holder (see SetAttackDefendRole).
        private enum SquadRoleSlot
        {
            Captain,
            ViceCaptain,
            PenaltyTaker,
            FreeKickTaker,
            LeftCornerTaker,
            RightCornerTaker
        }

        // Directly assigns (or clears, if player is null) whoever holds a given role -
        // the Tactics screen's dropdown pickers call this after a selection, rather than
        // the old Player Detail "click a player to toggle their own role" interaction
        // this replaced. Captain and vice-captain stay mutually exclusive - assigning one
        // clears the other if the same player held it.
        private void AssignRole(SquadRoleSlot slot, PlayerAgent player)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            switch (slot)
            {
                case SquadRoleSlot.Captain:
                    roles.Captain = player;
                    if (player != null && roles.ViceCaptain == player) roles.ViceCaptain = null;
                    break;
                case SquadRoleSlot.ViceCaptain:
                    roles.ViceCaptain = player;
                    if (player != null && roles.Captain == player) roles.Captain = null;
                    break;
                case SquadRoleSlot.PenaltyTaker:
                    roles.PenaltyTaker = player;
                    break;
                case SquadRoleSlot.FreeKickTaker:
                    roles.FreeKickTaker = player;
                    break;
                case SquadRoleSlot.LeftCornerTaker:
                    roles.LeftCornerTaker = player;
                    break;
                case SquadRoleSlot.RightCornerTaker:
                    roles.RightCornerTaker = player;
                    break;
            }

            RefreshTacticsScreenUI();
        }

        private void SetAttackDefendRole(PlayerAgent player, AttackDefendRole role)
        {
            GetOrCreateSquadRoles(managedTeamName).SetRole(player, role);
            RefreshPlayerInspectUI();
        }

        // Which AttackDefendRole values are even offered for a given position - a real
        // manager wouldn't tell a winger to "defend" or a centre-back to "attack" the way
        // they would a fullback or central midfielder, whose whole job varies by
        // instruction. Empty for GK - the leaning doesn't apply to a goalkeeper at all.
        private static AttackDefendRole[] GetAllowedAttackDefendRoles(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK:
                    return Array.Empty<AttackDefendRole>();

                case PlayerPosition.CB:
                case PlayerPosition.DM:
                    return new[] { AttackDefendRole.Defensive, AttackDefendRole.Balanced };

                case PlayerPosition.RB:
                case PlayerPosition.LB:
                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                case PlayerPosition.CM:
                    return new[] { AttackDefendRole.Defensive, AttackDefendRole.Balanced, AttackDefendRole.Attacking };

                default: // AM, RW, LW, RM, LM, ST
                    return new[] { AttackDefendRole.Balanced, AttackDefendRole.Attacking };
            }
        }

        // Small pill-style toggle button for RolesBand - active state mirrors
        // HighlightSelectedMentalityButton's Accent/CardNeutral treatment for the
        // existing mentality selector, so the two read as the same kind of control.
        // Returns the x position the next button in the row should start at.
        private float BuildRoleToggleButton(Transform parent, string label, float x, bool active, Action onClick)
        {
            const float buttonWidth = 130f;
            const float buttonHeight = 40f;
            const float gap = 8f;

            GameObject buttonObject = new GameObject($"RoleButton_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            rect.anchoredPosition = new Vector2(x, 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = active ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            ManagerUITheme.BuildLabel(
                buttonObject.transform,
                label,
                13,
                active ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody,
                TextAlignmentOptions.Center,
                FontStyles.Bold);

            return x + buttonWidth + gap;
        }

        // Academy focus stats picker (session 10) - up to 3 attributes per prospect,
        // doubling their growth rate for as long as they stay in the academy (see
        // ManagerAcademy.ToggleFocusAttribute / ManagerPlayerDevelopment's Focused
        // helper). Reuses the same RolesBand slot the attack/defend toggles occupy for
        // an owned-squad player - mutually exclusive with that content (a player is
        // never both an academy prospect and on your own squad), so no extra layout
        // region is needed beyond the taller rolesBandHeight already reserved for this
        // case in RefreshPlayerInspectUI.
        private void BuildFocusStatsPicker(Transform parent, PlayerAgent prospect)
        {
            IReadOnlyList<string> selected = academy.GetFocusAttributes(prospect);

            GameObject captionObj = new GameObject("FocusCaption", typeof(RectTransform));
            captionObj.transform.SetParent(parent, false);
            RectTransform captionRect = captionObj.GetComponent<RectTransform>();
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0f, 1f);
            captionRect.sizeDelta = new Vector2(0f, 20f);
            ManagerUITheme.BuildLabel(captionObj.transform, $"FOCUS STATS - {selected.Count}/3 SELECTED (2x GROWTH)", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            string[] focusable = ManagerAcademy.GetFocusableAttributes(prospect.PrimaryPosition);

            const float chipWidth = 140f;
            const float chipHeight = 30f;
            const float gapX = 8f;
            const float gapY = 6f;
            const int chipsPerRow = 9;

            for (int i = 0; i < focusable.Length; i++)
            {
                string attributeName = focusable[i];
                bool isSelected = selected.Contains(attributeName);

                int row = i / chipsPerRow;
                int col = i % chipsPerRow;
                float x = col * (chipWidth + gapX);
                float y = -28f - row * (chipHeight + gapY);

                GameObject chip = new GameObject($"FocusChip_{attributeName}", typeof(RectTransform), typeof(Image), typeof(Button));
                chip.transform.SetParent(parent, false);
                RectTransform chipRect = chip.GetComponent<RectTransform>();
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(0f, 1f);
                chipRect.pivot = new Vector2(0f, 1f);
                chipRect.sizeDelta = new Vector2(chipWidth, chipHeight);
                chipRect.anchoredPosition = new Vector2(x, y);

                Image chipImage = chip.GetComponent<Image>();
                chipImage.color = isSelected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

                Button chipButton = chip.GetComponent<Button>();
                chipButton.targetGraphic = chipImage;
                chipButton.onClick.AddListener(() => OnFocusAttributeToggled(prospect, attributeName));

                Color textColor = isSelected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
                ManagerUITheme.BuildLabel(chip.transform, AbbreviateAttributeName(attributeName), 12, textColor, TextAlignmentOptions.Center, FontStyles.Bold);
            }
        }

        private void OnFocusAttributeToggled(PlayerAgent prospect, string attributeName)
        {
            academy.ToggleFocusAttribute(prospect, attributeName);
            RefreshPlayerInspectUI();
        }

        // Short display labels for the focus-stat chips - full attribute names
        // ("ThroughBalls", "OffTheBall") don't fit a 140px chip at a readable size,
        // same abbreviation instinct as the existing role/set-piece badges
        // (BuildRoleBadgeSuffix's "PK"/"FK"/"CK-L").
        private static string AbbreviateAttributeName(string attributeName)
        {
            switch (attributeName)
            {
                case "Finishing": return "FIN";
                case "Passing": return "PAS";
                case "Dribbling": return "DRI";
                case "Crossing": return "CRO";
                case "Heading": return "HEA";
                case "LongShots": return "L.SHOT";
                case "ThroughBalls": return "T.BALL";
                case "Creativity": return "CREA";
                case "Positioning": return "POS";
                case "Composure": return "COMP";
                case "OffTheBall": return "OTB";
                case "Defending": return "DEF";
                case "Tackling": return "TACK";
                case "Marking": return "MARK";
                case "Pace": return "PACE";
                case "Strength": return "STR";
                case "Stamina": return "STAM";
                case "Aerial": return "AER";
                case "Goalkeeping": return "GK";
                case "Reflexes": return "REFL";
                default: return attributeName.ToUpperInvariant();
            }
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

            const float titleHeight = 22f;
            const float titleGap = 14f;
            const float rowSpacing = 42f;
            float contentHeight = titleHeight + titleGap + attributes.Length * rowSpacing;

            // Top-aligned (matches the "PLAYER DETAIL" mockup's align-items:start), not
            // vertically centered. Centering was tried first, but with columns holding
            // different row counts (2-5) each one centers independently, so column titles
            // land at different heights depending on how many stats that column has - the
            // "why are Technical and Defensive's titles not in line" bug. Pinning every
            // stack to the top keeps titles level across all columns regardless of length,
            // and reads as using the grid's space top-down rather than floating in the
            // middle of a tall, mostly-empty area.
            GameObject stack = new GameObject("Stack", typeof(RectTransform));
            stack.transform.SetParent(column.transform, false);
            RectTransform stackRect = stack.GetComponent<RectTransform>();
            stackRect.anchorMin = new Vector2(0f, 1f);
            stackRect.anchorMax = new Vector2(1f, 1f);
            stackRect.pivot = new Vector2(0.5f, 1f);
            stackRect.sizeDelta = new Vector2(0f, contentHeight);
            stackRect.anchoredPosition = Vector2.zero;

            GameObject titleObj = new GameObject("ColumnTitle", typeof(RectTransform));
            titleObj.transform.SetParent(stack.transform, false);
            ManagerUITheme.AnchorTopStretch(titleObj, 0f, titleHeight);
            ManagerUITheme.BuildLabel(titleObj.transform, title.ToUpperInvariant(), 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            float offset = titleHeight + titleGap;

            foreach ((string label, float value) in attributes)
            {
                offset = BuildAttributeRow(stack.transform, offset, label, value);
            }
        }

        private static float BuildAttributeRow(Transform parent, float topOffset, string label, float value)
        {
            GameObject labelRow = new GameObject($"AttrLabel_{label}", typeof(RectTransform));
            labelRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelRow, topOffset, 18f);

            GameObject nameText = new GameObject("Name", typeof(RectTransform));
            nameText.transform.SetParent(labelRow.transform, false);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(0.8f, 1f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(nameText.transform, label, 15, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            GameObject valueText = new GameObject("Value", typeof(RectTransform));
            valueText.transform.SetParent(labelRow.transform, false);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.8f, 0f);
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(valueText.transform, Mathf.RoundToInt(value).ToString(), 15, ManagerUITheme.RatingColor(value), TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject barRow = new GameObject($"AttrBar_{label}", typeof(RectTransform));
            barRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(barRow, topOffset + 21f, 7f);
            ManagerUITheme.BuildBar(barRow.transform, value / 100f, ManagerUITheme.RatingColor(value), 7f);

            return topOffset + 42f;
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
            // <voffset> nudges the sprite block onto the surrounding text's visual
            // center. Three earlier attempts (-0.15em, -0.06em, -0.02em) were each eyeballed
            // off zoomed screenshots and still read as low - the star sprite's own artwork
            // sits well below its reported baseline, so baseline-matching was never going
            // to be enough. This value is derived, not eyeballed: queried
            // TMP_TextInfo.characterInfo live for 'o' (a plain x-height glyph, unlike the
            // ascenders 'k'/'F' checked earlier) vs the star sprite's own bounds at the old
            // -0.02em - centers were 1.31 and -5.18 respectively, a 6.49-unit gap at
            // fontSize 21 (6.49/21 ≈ 0.31em), added on top of the old -0.02em.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(" <voffset=0.29em><size=60%>");
            for (int i = 0; i < filled; i++) sb.Append(filledTag);
            for (int i = filled; i < 5; i++) sb.Append(emptyTag);
            sb.Append("</size></voffset>");
            return sb.ToString();
        }

        // --- Matchday Prep (opponent scouting, Mentality, pre-match Subs - shown before
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

            // Mentality pills belong to live Match Day now, not scouting - but they're the
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
            ManagerUITheme.BuildLabel(titleObj.transform, "", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            matchdayPrepTitleLabel = titleObj;

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-120f, 20f);
            subtitleRect.anchoredPosition = new Vector2(60f, -58f);
            ManagerUITheme.BuildLabel(subtitleObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            matchdayPrepSubtitleLabel = subtitleObj;

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

            if (opponentSquadListView != null)
            {
                RectTransform opponentListRect = opponentSquadListView.GetComponent<RectTransform>();
                opponentListRect.anchorMin = new Vector2(0f, 0f);
                opponentListRect.anchorMax = new Vector2(1f, 1f);
                opponentListRect.offsetMin = new Vector2(sideMargin, rowMargin);
                // Expressed as a fixed offset from the RIGHT anchor (reserving exactly
                // "pitch + gap + margin" worth of space) rather than a literal 1920-based
                // left position - see the pitch's own comment below for why that
                // distinction actually matters here, not just style.
                opponentListRect.offsetMax = new Vector2(-(opponentPitchWidth + columnGap + sideMargin), -rowMargin);
                opponentSquadListView.gameObject.SetActive(true);

                // This ScrollView's own background Image was never recolored - it was
                // always retired/hidden before now, so its default Unity light-grey
                // "Background" sprite was never actually visible on screen. Now that
                // it's shown for the first time, that unstyled default shows through as
                // a plain grey/white box behind the rows.
                if (opponentSquadListView.TryGetComponent(out Image opponentListImage))
                {
                    opponentListImage.color = ManagerUITheme.PanelDark;
                }
            }

            // Right-anchored (point anchor at the container's top-right corner, pivot to
            // match) rather than a fixed left-relative "pitchLeft" computed from a
            // literal 1920 container width. CanvasScaler's actual effective canvas width
            // only equals the 1920 reference when the window's aspect ratio is exactly
            // 16:9 - in any other window size/aspect (i.e. not maximized/fullscreen) the
            // real container came out 2117 units wide in one live measurement, not 1920.
            // The scout list's own offsetMax above is expressed relative to the RIGHT
            // anchor, so it already scales correctly with the container's true width -
            // but this pitch was anchored from the LEFT at a fixed literal-1920-derived
            // offset, which does NOT scale, so the two drifted apart and the list
            // visibly overlapped the pitch's left edge (confirmed live, exactly the
            // "tactic board is behind the list view" report - not a z-order/sibling-index
            // bug as originally assumed, a genuine position mismatch that just happened
            // to look like a z-order issue).
            GameObject pitchColumnCaption = new GameObject("OpponentShapeCaption", typeof(RectTransform));
            pitchColumnCaption.transform.SetParent(matchdayPrepContentContainer, false);
            RectTransform pitchCaptionRect = pitchColumnCaption.GetComponent<RectTransform>();
            pitchCaptionRect.anchorMin = new Vector2(1f, 1f);
            pitchCaptionRect.anchorMax = new Vector2(1f, 1f);
            pitchCaptionRect.pivot = new Vector2(1f, 1f);
            pitchCaptionRect.anchoredPosition = new Vector2(-sideMargin, -rowMargin);
            pitchCaptionRect.sizeDelta = new Vector2(opponentPitchWidth, 20f);
            ManagerUITheme.BuildLabel(pitchColumnCaption.transform, "OPPONENT SHAPE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject pitchObj = new GameObject("OpponentPitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(matchdayPrepContentContainer, false);
            matchdayPrepPitchContainer = pitchObj.GetComponent<RectTransform>();
            // Vertically stretched (anchorMin/Max.y = 0/1) with top/bottom offsets,
            // exactly like opponentListRect right above - NOT a point anchor with a
            // manually-snapshotted sizeDelta.height (what this used to be). That snapshot
            // was computed once, from matchdayPrepContentContainer.rect.height, at chrome-
            // build time only - correct for whatever window size was active the very
            // first time Matchday Prep was ever shown, but frozen forever after and wrong
            // for any other window size, including the same window resized later in the
            // same session (confirmed live, 2026-08-09 session 7: fine on Matchday 1,
            // visibly elongated past the footer buttons by Matchday 2 after a window
            // resize in between - real recurrence of the exact drift class already
            // documented in the comment above for width/position, this time for height).
            // Stretch anchors recompute automatically on every layout pass, so this can't
            // go stale again regardless of when or how many times the window resizes.
            matchdayPrepPitchContainer.anchorMin = new Vector2(1f, 0f);
            matchdayPrepPitchContainer.anchorMax = new Vector2(1f, 1f);
            matchdayPrepPitchContainer.pivot = new Vector2(1f, 1f);
            matchdayPrepPitchContainer.offsetMax = new Vector2(-sideMargin, -(rowMargin + 30f));
            matchdayPrepPitchContainer.offsetMin = new Vector2(-(sideMargin + opponentPitchWidth), rowMargin);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(matchdayPrepPitchContainer);

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPrepContentContainer));
        }

        private void RefreshMatchdayPrepUI()
        {
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            string opponentName = managedIsHome ? currentFixture.AwayTeam : currentFixture.HomeTeam;
            AgentTeam opponentTeam = GetOrCreateAgentTeam(opponentName);

            if (matchdayPrepTitleLabel != null)
            {
                TextMeshProUGUI titleTMP = matchdayPrepTitleLabel.GetComponentInChildren<TextMeshProUGUI>();
                if (titleTMP != null)
                {
                    titleTMP.text = managedIsHome
                        ? $"{managedTeamName} vs {opponentName} (Home)"
                        : $"{managedTeamName} vs {opponentName} (Away)";
                }
            }

            if (matchdayPrepSubtitleLabel != null)
            {
                TextMeshProUGUI subtitleTMP = matchdayPrepSubtitleLabel.GetComponentInChildren<TextMeshProUGUI>();
                if (subtitleTMP != null)
                {
                    subtitleTMP.text = $"Matchday {currentFixture.Matchday}   ·   Opponent Formation: {TacticsBoardLayout.FormatFormation(opponentTeam.Formation)}";
                }
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

            // Scout list rows and opponent pitch pins are both cleared and rebuilt
            // fresh every refresh - same rapid destroy/recreate churn as the Tactics
            // Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPrepContentContainer));
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

                // labelFontSize bumped 10->12 - below that read as genuinely too small to
                // make out a name at a glance (confirmed live, Thomas couldn't read them).
                // Tactics Board's own pins use circleSize 68/labelFontSize 14 for
                // comparison - this pitch is deliberately smaller (shares the screen with
                // the OVR/Rating list), so it gets a smaller but still legible size rather
                // than matching exactly.
                ManagerUITheme.BuildPitchPinVisual(
                    matchdayPrepPitchContainer,
                    $"OpponentPin_{player.Name}",
                    anchor,
                    // circleSize stays at the original 48 (not bumped alongside the font
                    // sizes) - it directly drives labelWidth (circleSize + 70), and a
                    // wider box was enough to tip two closely-spaced CM pins into visual
                    // overlap in some formations (confirmed live). Keeping the same box
                    // width and only growing the text inside it gets the readability win
                    // without the collision risk.
                    circleSize: 48f,
                    borderColor: ManagerUITheme.Danger,
                    ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                    ratingFontSize: 14,
                    labelText: $"{player.Name} · {slot}",
                    labelFontSize: 14);
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

            // Grown from 110 to 170 - at 110 the enlarged score/clock (52pt/16pt, up from
            // the pre-1920x1080-pass 32pt/13pt) no longer fit inside the band at all; the
            // clock specifically rendered ~30px below the header's own bottom divider,
            // spilling into the body content underneath it (confirmed by the numbers:
            // old clock offset -122 with height 20 spans down to -142, well past a
            // 110-tall band). 170 gives the mockup's own header proportions room to
            // breathe (its own header is a content-sized flex column, not a fixed-height
            // band, but works out to roughly this tall once padding/gaps are accounted
            // for).
            const float headerHeight = 170f;
            const float footerHeight = 90f;

            ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: true, height: headerHeight);
            GameObject footerBand = ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: false, height: footerHeight);

            if (fixtureTitleText != null) fixtureTitleText.gameObject.SetActive(false);
            if (matchStatsText != null) matchStatsText.gameObject.SetActive(false);

            // --- Toolbar: Skip to Results (existing, repositioned) / Pause ---
            // No more "Tactics / Subs" placeholder here - real, working Mentality pills and
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
            // Vertical rhythm redone against the new 170px header: names/score both
            // start around the same top offset so their centers roughly line up (the
            // score box is taller to fit its much bigger digits), clock sits in the gap
            // below the score, all comfortably inside the band now instead of spilling
            // past its bottom edge.
            if (scoreText != null)
            {
                ManagerUITheme.SetPointAnchor(scoreText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(260f, 68f));
                scoreText.fontSize = 52;
                scoreText.alignment = TextAlignmentOptions.Center;
                scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            }

            // Bumped from 14 to 16 ("the ticker could be bigger") and repositioned to
            // sit directly under the score within the taller header.
            if (clockText != null)
            {
                ManagerUITheme.SetPointAnchor(clockText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(220f, 26f));
                clockText.alignment = TextAlignmentOptions.Center;
                clockText.fontSize = 16;
            }

            GameObject homeNameObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform homeNameRect = homeNameObj.GetComponent<RectTransform>();
            homeNameRect.anchorMin = new Vector2(0.5f, 1f);
            homeNameRect.anchorMax = new Vector2(0.5f, 1f);
            homeNameRect.pivot = new Vector2(1f, 1f);
            homeNameRect.anchoredPosition = new Vector2(-150f, -64f);
            homeNameRect.sizeDelta = new Vector2(300f, 40f);
            matchHomeNameLabel = ManagerUITheme.BuildLabel(homeNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayNameObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform awayNameRect = awayNameObj.GetComponent<RectTransform>();
            awayNameRect.anchorMin = new Vector2(0.5f, 1f);
            awayNameRect.anchorMax = new Vector2(0.5f, 1f);
            awayNameRect.pivot = new Vector2(0f, 1f);
            awayNameRect.anchoredPosition = new Vector2(150f, -64f);
            awayNameRect.sizeDelta = new Vector2(300f, 40f);
            matchAwayNameLabel = ManagerUITheme.BuildLabel(awayNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject fullTimeCaptionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            fullTimeCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(fullTimeCaptionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 20f));
            matchFullTimeCaptionLabel = ManagerUITheme.BuildLabel(fullTimeCaptionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            matchFullTimeCaptionGroup = fullTimeCaptionObj;
            matchFullTimeCaptionGroup.SetActive(false);

            // Goals: full-time only, built from the real ScorerName on each goal event (see
            // AgentMatchSimulator), never fabricated or parsed out of the free-text event
            // description. Previously a tiny pair of labels (12pt, autosizing down to a 7pt
            // floor) flanking dead-center below the header - illegible at a glance
            // (confirmed live, user feedback). Redesigned again after the first left/right-
            // half pass left a large empty band below both halves for a typical (non-8-goal)
            // scoreline (confirmed live, user feedback, screenshot) - compact team-labeled
            // scorer lists stay up top, and a large, full-width goal timeline (spanning the
            // whole screen, not just the left half) with minute labels on each marker now
            // fills that space instead of sitting cramped and small next to it.
            const float goalsBlockTop = -(headerHeight + 20f);
            const float halfMargin = 40f;
            // Left half is anchor x 0-0.5 (960px at 1920-wide); this is its usable width
            // with halfMargin on both the true left edge and the center-side, matching the
            // right half's own rightHalfWidth in the full-time repositioning block below.
            const float scorersWidth = 880f;

            GameObject goalsCaptionObj = new GameObject("GoalsCaption", typeof(RectTransform));
            goalsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(goalsCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, goalsBlockTop), new Vector2(400f, 20f));
            goalsCaptionObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            ManagerUITheme.BuildLabel(goalsCaptionObj.transform, "GOALS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Side-by-side columns (not stacked) - each list is self-labeled with its own
            // team name (see PopulateGoalScorerLists) since position alone doesn't imply
            // which team the way left-vs-right flanking across the whole screen used to.
            // Kept compact (not the full available height) now that the big timeline below
            // is the section's main visual - this is scannable detail, not the centerpiece.
            const float scorersTop = goalsBlockTop - 34f;
            const float scorersColumnGap = 40f;
            const float scorersColumnWidth = (scorersWidth - scorersColumnGap) / 2f;
            const float scorersBlockHeight = 140f;

            GameObject homeScorersObj = new GameObject("HomeScorers", typeof(RectTransform));
            homeScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(homeScorersObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, scorersTop), new Vector2(scorersColumnWidth, scorersBlockHeight));
            homeScorersObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            matchHomeScorersLabel = ManagerUITheme.BuildLabel(homeScorersObj.transform, "", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchHomeScorersLabel.spriteAsset = footballIconSpriteAsset;
            // An unusually long one-sided scoreline can still need more lines than this box
            // allows - autosizing shrinks the font to fit rather than overflowing into the
            // timeline below.
            matchHomeScorersLabel.enableAutoSizing = true;
            matchHomeScorersLabel.fontSizeMin = 13;
            matchHomeScorersLabel.fontSizeMax = 20;

            GameObject awayScorersObj = new GameObject("AwayScorers", typeof(RectTransform));
            awayScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(awayScorersObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin + scorersColumnWidth + scorersColumnGap, scorersTop), new Vector2(scorersColumnWidth, scorersBlockHeight));
            awayScorersObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            matchAwayScorersLabel = ManagerUITheme.BuildLabel(awayScorersObj.transform, "", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchAwayScorersLabel.spriteAsset = footballIconSpriteAsset;
            matchAwayScorersLabel.enableAutoSizing = true;
            matchAwayScorersLabel.fontSizeMin = 13;
            matchAwayScorersLabel.fontSizeMax = 20;

            // Big full-width timeline - starts below BOTH the scorer lists above (left
            // half) and Match Stats (right half, see the full-time repositioning block
            // below, which ends around -550) so it never runs underneath either at any
            // x-position, then has the whole rest of the panel down to the footer to work
            // with. Bigger markers (26px, was 14) and a minute label on each one now that
            // there's room, plus it spans corner-to-corner instead of being confined to the
            // left half.
            const float bigTimelineWidth = 1840f; // full 1920 width minus halfMargin each side
            const float bigTimelineY = -730f;
            matchGoalTimelineWidth = bigTimelineWidth;

            GameObject timelineTrackObj = new GameObject("GoalTimelineTrack", typeof(RectTransform), typeof(Image));
            timelineTrackObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(timelineTrackObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, bigTimelineY), new Vector2(bigTimelineWidth, 4f));
            timelineTrackObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            timelineTrackObj.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            GameObject timelineMarkersObj = new GameObject("GoalTimelineMarkers", typeof(RectTransform));
            timelineMarkersObj.transform.SetParent(matchdayPanel.transform, false);
            matchGoalTimelineContainer = timelineMarkersObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(matchGoalTimelineContainer, new Vector2(0f, 1f), new Vector2(halfMargin, bigTimelineY), new Vector2(bigTimelineWidth, 4f));
            matchGoalTimelineContainer.pivot = new Vector2(0f, 0.5f);

            matchFullTimeOnlyElements = new List<GameObject> { goalsCaptionObj, timelineTrackObj, timelineMarkersObj, homeScorersObj, awayScorersObj };
            goalsCaptionObj.SetActive(false);
            timelineTrackObj.SetActive(false);
            timelineMarkersObj.SetActive(false);
            homeScorersObj.SetActive(false);
            awayScorersObj.SetActive(false);

            matchLiveOnlyElements = new[] { pauseButton.gameObject, skipToResultsButton != null ? skipToResultsButton.gameObject : null, clockText != null ? clockText.gameObject : null };

            // --- Body: Key Moments (left) / Match Stats (right) ---
            GameObject keyMomentsCaptionObj = new GameObject("MatchLogCaption", typeof(RectTransform));
            keyMomentsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            matchKeyMomentsCaptionRect = keyMomentsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(matchKeyMomentsCaptionRect, new Vector2(0f, 1f), new Vector2(40f, -(headerHeight + 28f)), new Vector2(400f, 20f));
            ManagerUITheme.BuildLabel(keyMomentsCaptionObj.transform, "MATCH LOG", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

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
                // Bumped from 15 with added line spacing - the log's own mask has
                // plenty of vertical room (footerHeight+24 to headerHeight+56, hundreds
                // of px), so there's no risk of maxVisibleEventLines worth of lines
                // overflowing the hard RectMask2D clip at this size.
                eventFeedText.fontSize = 19;
                eventFeedText.lineSpacing = 14f;

                // Live feed is now row-based (see AppendMatchEventRow) so each event gets
                // its own bottom divider, matching the mockup's per-line
                // "border-bottom:1px solid #1e2a3d" - eventFeedText itself is a
                // pre-existing Inspector-wired SerializeField, kept in the hierarchy
                // (still reparented/sized above) but disabled and never given text again,
                // rather than touching its scene wiring.
                eventFeedText.gameObject.SetActive(false);

                GameObject feedRowsObj = new GameObject("EventFeedRows", typeof(RectTransform));
                feedRowsObj.transform.SetParent(maskRect, false);
                matchEventFeedContainer = feedRowsObj.GetComponent<RectTransform>();
                matchEventFeedContainer.anchorMin = Vector2.zero;
                matchEventFeedContainer.anchorMax = Vector2.one;
                matchEventFeedContainer.offsetMin = Vector2.zero;
                matchEventFeedContainer.offsetMax = Vector2.zero;

                VerticalLayoutGroup feedLayout = feedRowsObj.AddComponent<VerticalLayoutGroup>();
                feedLayout.childForceExpandWidth = true;
                feedLayout.childForceExpandHeight = false;
                feedLayout.childControlWidth = true;
                feedLayout.childControlHeight = true;
                feedLayout.spacing = 0f;

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

            Button makeChangesButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextPrimary, 15);
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
            matchStatsCaptionLabel = ManagerUITheme.BuildLabel(statsCaptionObj.transform, "MATCH STATS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject statsBarsObj = new GameObject("MatchStatsBars", typeof(RectTransform));
            statsBarsObj.transform.SetParent(matchdayPanel.transform, false);
            matchStatsBarsContainer = statsBarsObj.GetComponent<RectTransform>();
            matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
            matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 238f));
            // Grown from 140 (1 row: Shots) to fit 4 rows (Possession/Chances Created/
            // Shots/Shots on Target) at 36px pitch each.
            matchStatsBarsContainer.sizeDelta = new Vector2(360f, 190f);

            // --- Footer: live Mentality pills (left, real - reused from Matchday Prep,
            // which no longer needs them since it's scouting-only now; now genuinely
            // live too, see ApplyLiveMentalityChangeIfMatchInProgress) + Continue (right) ---
            GameObject mentalityLabelObj = new GameObject("MentalityFooterCaption", typeof(RectTransform));
            mentalityLabelObj.transform.SetParent(footerBand.transform, false);
            RectTransform mentalityLabelRect = mentalityLabelObj.GetComponent<RectTransform>();
            mentalityLabelRect.anchorMin = new Vector2(0f, 0.5f);
            mentalityLabelRect.anchorMax = new Vector2(0f, 0.5f);
            mentalityLabelRect.pivot = new Vector2(0f, 0.5f);
            mentalityLabelRect.anchoredPosition = new Vector2(40f, 0f);
            mentalityLabelRect.sizeDelta = new Vector2(90f, 26f);
            ManagerUITheme.BuildLabel(mentalityLabelObj.transform, "MENTALITY", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = mentalityLabelObj;

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

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPanel.transform));
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

                // Oswald SDF has no arrow glyph (same reason ">" already stands in for
                // it elsewhere in this file, e.g. "NEXT >") - a literal "→" here just
                // silently prints as a blank space instead of erroring.
                string rowText = $"OUT {entry.offName} ({entry.offPosition})  >  IN {entry.onName} ({entry.onPosition})  {entry.minute}'";
                GameObject labelObj = new GameObject("Label", typeof(RectTransform));
                labelObj.transform.SetParent(row.transform, false);
                RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(labelObj.transform, rowText, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            }

            // Rows are cleared and rebuilt fresh every time this runs - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchSubsLogContainer));
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
        // Expanded from a single live Shots row to the same four real, derived stats the
        // full-time panel shows (see ShowFullTimeMatchStats) - Possession/Chances
        // Created update on every event now (not just shots), matching how
        // HomeTeamAttacking is set on every event in the ManagerSim fork.
        private void RefreshLiveMatchStats(
            int homeShots,
            int awayShots,
            int homeShotsOnTarget,
            int awayShotsOnTarget,
            int homeAttackEvents,
            int awayAttackEvents)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            int totalAttackEvents = homeAttackEvents + awayAttackEvents;
            int homePossessionPct = totalAttackEvents > 0
                ? Mathf.RoundToInt(100f * homeAttackEvents / totalAttackEvents)
                : 50;
            int awayPossessionPct = 100 - homePossessionPct;

            float y = 0f;
            y = BuildLiveStatRow("POSSESSION", homePossessionPct, awayPossessionPct, y, "%");
            y = BuildLiveStatRow("CHANCES CREATED", homeAttackEvents, awayAttackEvents, y);
            y = BuildLiveStatRow("SHOTS", homeShots, awayShots, y);
            BuildLiveStatRow("SHOTS ON TARGET", homeShotsOnTarget, awayShotsOnTarget, y);
        }

        private float BuildLiveStatRow(string label, int homeValue, int awayValue, float y, string valueSuffix = "")
        {
            int total = homeValue + awayValue;
            float homeSharePct = total > 0 ? homeValue / (float)total : 0.5f;

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
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = $"{label}   {homeValue}{valueSuffix} / {awayValue}{valueSuffix}";
            labelText.fontSize = 16;
            labelText.color = ManagerUITheme.TextBody;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject barObj = new GameObject("Bar", typeof(RectTransform));
            barObj.transform.SetParent(row.transform, false);
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            // Real two-team comparison (green/red split at the home team's actual share),
            // not just a single-color fill - managed-team-relative like the rest of this
            // session's coloring work, not simply home=green/away=red.
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            Color homeColor = managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger;
            Color awayColor = managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent;
            ManagerUITheme.BuildSplitBar(barRect, homeSharePct, homeColor, awayColor, 6f);

            return y + 36f;
        }

        // Decorative equal-split bars (matching the design - the numbers carry the real
        // information, the bar underneath is just a visual accent) for shots and goals,
        // plus the tactic actually used, once the match has finished.
        //
        // Possession/Chances Created/Shots on Target are all real, derived numbers, not
        // invented ones - see the ManagerSim fork of AgentMatchSimulator for how. They
        // needed a genuine model change (an on/off-target split, and setting
        // HomeTeamAttacking on every event instead of just shots) that the protected
        // Sim.AgentMatchSimulator can't take, hence the fork.
        private void ShowFullTimeMatchStats(
            int homeShots,
            int awayShots,
            int homeShotsOnTarget,
            int awayShotsOnTarget,
            int homeAttackEvents,
            int awayAttackEvents,
            int homeGoals,
            int awayGoals)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            int totalAttackEvents = homeAttackEvents + awayAttackEvents;
            int homePossessionPct = totalAttackEvents > 0
                ? Mathf.RoundToInt(100f * homeAttackEvents / totalAttackEvents)
                : 50;
            int awayPossessionPct = 100 - homePossessionPct;

            float y = 0f;
            y = BuildFullTimeStatRow("POSSESSION", homePossessionPct, awayPossessionPct, y, "%");
            y = BuildFullTimeStatRow("CHANCES CREATED", homeAttackEvents, awayAttackEvents, y);
            y = BuildFullTimeStatRow("SHOTS", homeShots, awayShots, y);
            y = BuildFullTimeStatRow("SHOTS ON TARGET", homeShotsOnTarget, awayShotsOnTarget, y);
            y = BuildFullTimeStatRow("GOALS", homeGoals, awayGoals, y);

            GameObject mentalityLineObj = new GameObject("MentalityUsedLine", typeof(RectTransform));
            mentalityLineObj.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform mentalityLineRect = mentalityLineObj.GetComponent<RectTransform>();
            mentalityLineRect.anchorMin = new Vector2(0f, 1f);
            mentalityLineRect.anchorMax = new Vector2(1f, 1f);
            mentalityLineRect.pivot = new Vector2(0f, 1f);
            mentalityLineRect.anchoredPosition = new Vector2(0f, -y - 8f);
            mentalityLineRect.sizeDelta = new Vector2(0f, 22f);
            // Centered, matching the design's Full-Time Summary board (it centers this
            // line under the stat bars rather than left-aligning it).
            ManagerUITheme.BuildLabel(mentalityLineObj.transform, $"Mentality used: {mentalityUsedForCurrentMatch}", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
        }

        private float BuildFullTimeStatRow(string label, int homeValue, int awayValue, float y, string valueSuffix = "")
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
            text.text = $"{homeValue}{valueSuffix}   {label}   {awayValue}{valueSuffix}";
            text.fontSize = 19;
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

            // Real two-team comparison (e.g. 60/40 possession -> a 60% green / 40% red
            // split, not a decorative always-full bar) - was previously hardcoded to
            // pct=1f regardless of the actual values (confirmed live, user feedback).
            // Managed-team-relative like the rest of this session's coloring work, not
            // simply home=green/away=red.
            int total = homeValue + awayValue;
            float homeSharePct = total > 0 ? homeValue / (float)total : 0.5f;
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            Color homeColor = managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger;
            Color awayColor = managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent;
            ManagerUITheme.BuildSplitBar(barsRect, homeSharePct, homeColor, awayColor, 6f);

            return y + 44f;
        }

        public void OnSimulateMatchClicked()
        {
            mentalityUsedForCurrentMatch = selectedMentality;

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
            SetMentality(selectedMentality); // re-highlights the correct footer pill for this screen
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
            // Must match BuildMatchdayChrome's own headerHeight/offsets for these two
            // exactly - this was left at stale pre-redesign values (110/152/180) after
            // this session's header rework (170/210/238), which meant every match
            // silently reset the stats panel to the wrong position immediately after
            // BuildMatchdayChrome had just built it correctly, since this runs
            // unconditionally in OnSimulateMatchClicked on every match, not just the
            // first one.
            const float headerHeight = 170f;

            if (matchStatsCaptionRect != null)
            {
                matchStatsCaptionRect.anchorMin = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.anchorMax = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.pivot = new Vector2(0f, 1f);
                matchStatsCaptionRect.anchoredPosition = new Vector2(20f, -(headerHeight + 210f));
                matchStatsCaptionRect.sizeDelta = new Vector2(360f, 20f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 238f));
                matchStatsBarsContainer.sizeDelta = new Vector2(360f, 190f);
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
                scouting.ResolveDueAssignments(currentFixtureIndex);
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

            ApplyMatchFormBonusForManagedTeam(fixture, result);
        }

        // Form-based development bonus (session 9 backlog item) - has to live here,
        // post-match, rather than in ApplyMatchdayConditionAndInjuries (which runs
        // pre-match, before the result exists at all - see SimulateFixture's ordering).
        // Managed team only, same scope limit as every other per-player development
        // signal this session (AI clubs only get a flat assumed playing-time factor,
        // no real per-match tracking). Matches scorer names against the managed
        // Starting XI specifically (not the whole league) to minimise the same-name
        // collision risk that AgentMatchEvent.ScorerName already carries everywhere
        // else it's used (see the match log's own goal list) - a small squad is a much
        // narrower name-collision surface than 20 clubs' worth of players.
        private void ApplyMatchFormBonusForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            bool isManagedHome = fixture.HomeTeam == managedTeamName;
            bool isManagedAway = fixture.AwayTeam == managedTeamName;

            if (!isManagedHome && !isManagedAway)
            {
                return;
            }

            int managedGoals = isManagedHome ? result.HomeGoals : result.AwayGoals;
            int opponentGoals = isManagedHome ? result.AwayGoals : result.HomeGoals;

            ManagerPlayerDevelopment.MatchFormOutcome outcome = managedGoals > opponentGoals
                ? ManagerPlayerDevelopment.MatchFormOutcome.Win
                : managedGoals < opponentGoals
                    ? ManagerPlayerDevelopment.MatchFormOutcome.Loss
                    : ManagerPlayerDevelopment.MatchFormOutcome.Draw;

            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            HashSet<PlayerAgent> playedThisMatch = new HashSet<PlayerAgent>(managedTeam.StartingEleven);

            Dictionary<string, int> goalsByScorerName = new Dictionary<string, int>();
            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal || string.IsNullOrEmpty(evt.ScorerName))
                {
                    continue;
                }

                goalsByScorerName[evt.ScorerName] = goalsByScorerName.TryGetValue(evt.ScorerName, out int count) ? count + 1 : 1;
            }

            foreach (PlayerAgent player in playedThisMatch)
            {
                int goalsThisMatch = goalsByScorerName.TryGetValue(player.Name, out int goals) ? goals : 0;
                ManagerPlayerDevelopment.ApplyMatchFormBonus(player, goalsThisMatch, outcome);
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

            // TMP's <mspace> forces every character (letters and the join space alike) to
            // advance by the same fixed width - without it, "W" is visibly wider than "L"
            // or "D" in a proportional font, so consecutive results didn't line up at a
            // consistent rhythm (confirmed live: "WWDWW" reads noticeably tighter/looser
            // than "LWLWL" despite both being 5 characters).
            return $"<mspace=1.4em>{string.Join(" ", history)}</mspace>";
        }

        // Applies the mentality modifier only when the managed club is actually playing
        // in this fixture - other clubs' matches against each other use the plain
        // predicted expected goals with no modifier.
        private AgentMatchSimulator.AgentMatchResult SimulateFixture(OpenFootballMatch fixture)
        {
            AgentTeam homeTeam = GetOrCreateAgentTeam(fixture.HomeTeam);
            AgentTeam awayTeam = GetOrCreateAgentTeam(fixture.AwayTeam);

            // Swap any still-injured starter for the best fit bench cover (or call up a
            // reserve if the bench has none - see CallUpReservePlayer) before this match's
            // XI is finalized. Managed team only - AI opponents have no injury tracking.
            if (fixture.HomeTeam == managedTeamName)
            {
                EnsureNoInjuredStarters(homeTeam, fixture.HomeTeam);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                EnsureNoInjuredStarters(awayTeam, fixture.AwayTeam);
            }

            // Throwaway fit-adjusted clones, not the real squad data - see
            // ManagerFormationFit. A no-op for AI teams (never touched by the user, so
            // every starter is already a perfect fit for their slot, and conditionLookup
            // stays null since AI teams have no Condition tracking); only matters once
            // the managed team's XI has anyone out of position or under-conditioned.
            Func<PlayerAgent, float> homeConditionLookup = fixture.HomeTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p))
                : null;
            Func<PlayerAgent, float> awayConditionLookup = fixture.AwayTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p))
                : null;

            AgentTeam fitAdjustedHomeTeam = ManagerFormationFit.BuildFitAdjustedTeam(homeTeam, squadGenerator.GetStartingPositions(homeTeam.Formation), homeConditionLookup);
            AgentTeam fitAdjustedAwayTeam = ManagerFormationFit.BuildFitAdjustedTeam(awayTeam, squadGenerator.GetStartingPositions(awayTeam.Formation), awayConditionLookup);

            // Condition decay/recovery + injury rolls - managed team only (see
            // ManagerSquadRoles). Snapshotting team.StartingEleven here, before
            // SimulateMatch/replay ever runs, captures exactly the pre-kickoff XI -
            // substitutions during replay mutate StartingEleven/Bench in place, so
            // capturing any later would blur "who actually started" with "who's on at
            // full-time." Subs who come on mid-match aren't counted as "played" for
            // fatigue purposes in this pass - a deliberate v1 simplification, not an
            // oversight (see HANDOFF).
            if (fixture.HomeTeam == managedTeamName)
            {
                ApplyMatchdayConditionAndInjuries(homeTeam);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ApplyMatchdayConditionAndInjuries(awayTeam);
            }

            StatisticalModel.ExpectedGoalsPrediction prediction = statisticalModel.PredictExpectedGoals(fixture);

            // Kept before the mentality modifier touches anything - see
            // ApplyLiveMentalityChangeIfMatchInProgress, which needs this exact
            // pre-mentality baseline to recompute cleanly from if mentality changes
            // again mid-match.
            lastRawExpectedHomeGoals = prediction.ExpectedHomeGoals;
            lastRawExpectedAwayGoals = prediction.ExpectedAwayGoals;

            float expectedHomeGoals = prediction.ExpectedHomeGoals;
            float expectedAwayGoals = prediction.ExpectedAwayGoals;

            if (fixture.HomeTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            // Naturally a no-op for AI-controlled teams - only the managed team's squad
            // roles are ever populated via Player Detail, so an opponent's Captain is
            // always null here.
            ManagerCaptaincyModifier.Apply(GetOrCreateSquadRoles(fixture.HomeTeam).Captain, ref expectedHomeGoals);
            ManagerCaptaincyModifier.Apply(GetOrCreateSquadRoles(fixture.AwayTeam).Captain, ref expectedAwayGoals);

            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;

            ManagerSquadRoles homeRoles = GetOrCreateSquadRoles(fixture.HomeTeam);
            ManagerSquadRoles awayRoles = GetOrCreateSquadRoles(fixture.AwayTeam);
            matchSimulator.CornerTakerNamesByTeamName[fixture.HomeTeam] = (homeRoles.LeftCornerTaker?.Name, homeRoles.RightCornerTaker?.Name);
            matchSimulator.CornerTakerNamesByTeamName[fixture.AwayTeam] = (awayRoles.LeftCornerTaker?.Name, awayRoles.RightCornerTaker?.Name);

            matchSimulator.ManagedTeamName = managedTeamName;
            matchSimulator.ManagedTeamTacticalSliders = tacticalSliders;

            // Fresh match, fresh substitution clock - see AgentMatchSimulator.
            // ClearSubstitutions' own comment. SimulateFixture runs exactly once per
            // match (mid-match resimulation calls SimulateFromMinute directly, never
            // this method again), so this is the one correct place to reset it.
            matchSimulator.ClearSubstitutions();

            return matchSimulator.SimulateMatch(fitAdjustedHomeTeam, fitAdjustedAwayTeam, expectedHomeGoals, expectedAwayGoals);
        }

        private void EnsureNoInjuredStarters(AgentTeam team, string teamName)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(teamName);

            // Snapshot before iterating - SubstitutePlayer mutates StartingEleven in
            // place, so walking the live list while swapping into it would skip entries.
            foreach (PlayerAgent starter in new List<PlayerAgent>(team.StartingEleven))
            {
                if (!roles.IsInjured(starter, currentFixtureIndex))
                {
                    continue;
                }

                PlayerAgent replacement = FindFitBenchReplacement(team, roles, starter.PrimaryPosition)
                    ?? CallUpReservePlayer(teamName, starter.PrimaryPosition);

                if (replacement != null)
                {
                    team.SubstitutePlayer(starter, replacement);
                }

                // If replacement is still null here, the bench and reserve pool are both
                // out of fit cover for this position - a real, visible squad crisis
                // rather than one silently papered over. The injured starter plays
                // anyway (better than fielding ten men).
            }
        }

        private PlayerAgent FindFitBenchReplacement(AgentTeam team, ManagerSquadRoles roles, PlayerPosition neededPosition)
        {
            PlayerAgent best = null;
            float bestFit = -1f;

            foreach (PlayerAgent candidate in team.Bench)
            {
                if (roles.IsInjured(candidate, currentFixtureIndex))
                {
                    continue;
                }

                float fit = candidate.GetPositionFit(neededPosition);

                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            return best;
        }

        // Loan system (session 9) - "any squad player" per Thomas's own answer, so a
        // starter can be loaned out too. If they were starting, backfills the slot the
        // same way an injury already does (FindFitBenchReplacement, falling back to
        // CallUpReservePlayer) rather than leaving a hole in the XI - a genuine squad
        // crisis only if no cover exists anywhere, same as the injury path.
        private void OnLoanOutClicked(PlayerAgent player)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool wasStarting = team.StartingEleven.Contains(player);

            if (wasStarting)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                PlayerAgent replacement = FindFitBenchReplacement(team, roles, player.PrimaryPosition)
                    ?? CallUpReservePlayer(managedTeamName, player.PrimaryPosition);

                if (replacement != null)
                {
                    team.SubstitutePlayer(player, replacement);
                    team.Bench.Remove(player);
                }
                else
                {
                    team.StartingEleven.Remove(player);
                }
            }
            else
            {
                team.Bench.Remove(player);
            }

            team.Players.Remove(player);

            // No cross-screen status label to report into here (Player Detail is
            // reachable from several different origin screens, each with its own
            // status mechanism or none at all) - the player disappearing from the
            // squad list on return is the confirmation for this first version. A
            // proper toast/confirmation would be a follow-up polish item.
            loanTracker.SendOnLoan(player, managedTeamName);

            OnInspectBackClicked();
        }

        private void ApplyMatchdayConditionAndInjuries(AgentTeam team)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            List<PlayerAgent> fullSquad = new List<PlayerAgent>(team.StartingEleven);
            fullSquad.AddRange(team.Bench);

            foreach (PlayerAgent player in fullSquad)
            {
                float minutesPlayed = ComputeMinutesPlayed(player, team);
                bool played = minutesPlayed > 0f;
                float preMatchCondition = roles.GetCondition(player);

                roles.ApplyPostMatchCondition(player, minutesPlayed, player.Age, player.Stamina);

                if (played)
                {
                    roles.RecordAppearance(player);
                    TryRollInjury(roles, player, preMatchCondition);
                }

                // Per-matchday development tick (session 9 backlog item) - same hook
                // Condition already uses, same played/not-played signal computed above.
                // Whole squad, not just starters - a benched player still ticks (at the
                // 0.7x floor rate), same as the old season-lump version's playing-time
                // floor. Deliberately still the binary `played` flag here, not
                // minutesPlayed - growth ticks were never the reported issue, only
                // Condition was, so left unchanged to keep this fix minimal.
                ManagerPlayerDevelopment.ApplyMatchdayProgression(player, played);
            }
        }

        // Real per-player minutes for this match (session 10 fix, see
        // ManagerSquadRoles.ApplyPostMatchCondition's own comment for the bug this
        // replaces). matchSubsLog only ever gets an entry for a genuine MID-match
        // substitution (see OnBenchPlayerDroppedOnPin's tacticsBoardOpenedMidMatch
        // gate) - a pre-match team-sheet edit made before kickoff isn't logged at all,
        // but doesn't need to be: team.StartingEleven by kickoff already reflects
        // whoever the manager actually chose to start, so anyone not touched by a
        // mid-match sub either played the full 90 (if they started) or didn't feature
        // at all (if they didn't). Doesn't handle a player being subbed on and then
        // subbed off again in the same match - the UI has no path to re-introduce a
        // player who's already come off, so that combination can't happen today.
        private float ComputeMinutesPlayed(PlayerAgent player, AgentTeam team)
        {
            const float matchLengthMinutes = 90f;

            foreach (var sub in matchSubsLog)
            {
                if (sub.onName == player.Name) return matchLengthMinutes - sub.minute;
                if (sub.offName == player.Name) return sub.minute;
            }

            return team.StartingEleven.Contains(player) ? matchLengthMinutes : 0f;
        }

        // Injury risk scales sharply as pre-match Condition drops - a manager who never
        // rests a player is directly trading long-term injury risk for short-term
        // selection convenience, which was the whole point of this system. Age adds a
        // smaller, realistic aging-curve bump on top. Recovery duration is a rough bell
        // curve (two averaged Random.Range rolls, same cheap-Gaussian-ish trick
        // GenerateAge uses) - mostly short knocks, occasional longer absences, matching
        // the "bell curve not hard range" preference used everywhere else stats/ages/
        // heights are generated.
        private void TryRollInjury(ManagerSquadRoles roles, PlayerAgent player, float preMatchCondition)
        {
            float fatigueRisk = Mathf.Clamp01((70f - preMatchCondition) / 70f);
            float ageRisk = Mathf.Clamp01((player.Age - 30f) / 15f);

            float injuryChance = 0.015f + (fatigueRisk * 0.09f) + (ageRisk * 0.02f);

            if (UnityEngine.Random.value >= injuryChance)
            {
                return;
            }

            int duration = Mathf.Clamp(Mathf.RoundToInt((UnityEngine.Random.Range(1f, 6f) + UnityEngine.Random.Range(1f, 6f)) / 2f), 1, 8);
            roles.SetInjured(player, currentFixtureIndex + duration + 1);
        }

        // Lets the running replay coroutine finish out its remaining minutes without
        // waiting between them, so it lands on the same full-time state almost
        // instantly instead of skipping/discarding any of the match.
        public void OnSkipToResultsClicked()
        {
            skipToResultsRequested = true;
        }

        // Simulates the full match instantly, then replays the pre-computed events
        // against an accelerated clock so it reads as if live. Mentality buttons stay
        // interactable during replay and now genuinely affect the match in progress -
        // see ApplyLiveMentalityChangeIfMatchInProgress and isMatchCurrentlyLive below.
        private IEnumerator ReplayMatchCoroutine(AgentMatchSimulator.AgentMatchResult result)
        {
            skipToResultsRequested = false;
            tacticsBoardOpenedMidMatch = false;
            currentMatchMinute = 0;
            liveHomeGoalsSoFar = 0;
            liveAwayGoalsSoFar = 0;
            matchSubsLog.Clear();
            RefreshMatchSubsMadeList();

            // Explicit flag rather than inferring "live" from panel active-states -
            // SetMentality also gets called during match *setup* (OnSimulateMatchClicked,
            // purely to re-highlight the footer pill) before this coroutine has even
            // reset currentMatchMinute to 0, so a state-inferred check could fire a bogus
            // resimulation against stale leftover data from the previous match.
            isMatchCurrentlyLive = true;

            if (matchEventFeedContainer != null)
            {
                foreach (Transform child in matchEventFeedContainer)
                {
                    Destroy(child.gameObject);
                }

                matchEventFeedRows.Clear();
            }

            if (scoreText != null) scoreText.text = "0 - 0";
            if (clockText != null) clockText.text = "0' LIVE";

            RefreshLiveMatchStats(0, 0, 0, 0, 0, 0);

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            int homeShots = 0;
            int awayShots = 0;
            int homeShotsOnTarget = 0;
            int awayShotsOnTarget = 0;
            int homeAttackEvents = 0;
            int awayAttackEvents = 0;
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

                    // Every event now carries HomeTeamAttacking (see the ManagerSim fork -
                    // the protected original only set it on shots), so "chances created"/
                    // possession share can come straight from the full event list instead
                    // of needing separate running totals threaded through the mid-match
                    // resimulation splice.
                    if (matchEvent.HomeTeamAttacking) homeAttackEvents++; else awayAttackEvents++;

                    if (matchEvent.IsShot)
                    {
                        if (matchEvent.HomeTeamAttacking) homeShots++; else awayShots++;
                        if (matchEvent.IsOnTarget)
                        {
                            if (matchEvent.HomeTeamAttacking) homeShotsOnTarget++; else awayShotsOnTarget++;
                        }
                    }

                    // Refreshed on every event, not just shots - Possession/Chances
                    // Created should tick up on a stopped-before-shot attack too.
                    RefreshLiveMatchStats(homeShots, awayShots, homeShotsOnTarget, awayShotsOnTarget, homeAttackEvents, awayAttackEvents);

                    AppendMatchEventRow(minute, matchEvent);
                }
            }

            // Match is resolved - any further mentality clicks should only affect the
            // *next* match again, not trigger a resimulation against a finished match.
            isMatchCurrentlyLive = false;

            // Switch from the live layout to the full-time one: hide the toolbar/clock/
            // mentality readout, show the "FULL TIME" caption, enlarge the score, and
            // swap the stats panel from the live single shots bar to the full-time
            // breakdown.
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

            // The fontSize bump above hits the same TMP mesh-generation failure as fresh
            // label creation (confirmed live: characterCount=0 with the text still
            // correctly assigned, silently rendering neither team name at full-time).
            StartCoroutine(RecoverBlankMatchTeamNameLabelsNextFrame());

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
            PopulateGoalTimeline(result);
            lastMatchEvents = new List<AgentMatchSimulator.AgentMatchEvent>(result.Events);

            // Match Stats + View Match Events move to the right half now that the goal
            // timeline/scorer lists (see BuildMatchdayChrome) own the whole left half -
            // previously both were recentered into one narrow column sharing the same
            // dead-center space the (then-tiny) scorer labels also used. Right half starts
            // right under the header since nothing else occupies that half above it anymore.
            const float rightHalfMargin = 40f;
            const float rightHalfTop = -(170f + 20f); // matches BuildMatchdayChrome's headerHeight (170) + goalsBlockTop's own offset
            // Right half is anchor x 0.5-1.0 (960px at 1920-wide); this is its usable width
            // with rightHalfMargin on both the center-side and the true right edge.
            const float rightHalfWidth = 880f;
            // Button's right edge lines up with the stats bars' own right edge below it.
            const float rightEdgeOffset = rightHalfMargin + rightHalfWidth;

            if (viewMatchEventsButton != null)
            {
                RectTransform viewEventsRect = viewMatchEventsButton.GetComponent<RectTransform>();
                viewEventsRect.anchorMin = new Vector2(0.5f, 1f);
                viewEventsRect.anchorMax = new Vector2(0.5f, 1f);
                viewEventsRect.pivot = new Vector2(1f, 1f);
                viewEventsRect.anchoredPosition = new Vector2(rightEdgeOffset, rightHalfTop);
                viewEventsRect.sizeDelta = new Vector2(220f, 32f);
            }

            if (matchStatsCaptionRect != null)
            {
                // Must be matchStatsCaptionRect (the "MatchStatsCaption" container that's
                // actually parented to the canvas), not matchStatsCaptionLabel.rectTransform
                // (BuildLabel's inner "Label" child, whose anchors/position are relative to
                // that container instead) - repositioning the child left the container
                // behind at its original column position and produced a nonsense on-screen
                // spot for the text, nowhere near the intended position.
                RectTransform captionRect = matchStatsCaptionRect;
                captionRect.anchorMin = new Vector2(0.5f, 1f);
                captionRect.anchorMax = new Vector2(0.5f, 1f);
                captionRect.pivot = new Vector2(0f, 1f);
                captionRect.anchoredPosition = new Vector2(rightHalfMargin, rightHalfTop - 50f);
                captionRect.sizeDelta = new Vector2(360f, 20f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(rightHalfMargin, rightHalfTop - 80f);
                // Widened from 520 to the right half's real available width now that it's
                // not sharing a centered column - BuildFullTimeStatRow's rows anchor
                // (0,1)-(1,1) within this container, so they scale with it automatically,
                // no other change needed. Height fits 5 rows (Possession/Chances Created/
                // Shots/Shots on Target/Goals) at 44px each.
                matchStatsBarsContainer.sizeDelta = new Vector2(rightHalfWidth, 280f);
            }

            ShowFullTimeMatchStats(
                homeShots,
                awayShots,
                homeShotsOnTarget,
                awayShotsOnTarget,
                homeAttackEvents,
                awayAttackEvents,
                result.HomeGoals,
                result.AwayGoals);

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = true;
            }
        }

        // One row per live event, each with its own bottom divider - matches the mockup's
        // Match Log treatment (per-line "border-bottom:1px solid #1e2a3d") instead of the
        // old single text block with only line-spacing between events. Oldest row is
        // dropped once matchEventFeedRows exceeds maxVisibleEventLines, same cap the old
        // text-line queue used - since row count never exceeds that cap, total content
        // height never exceeds what the feed's mask was already sized to fit, so newest is
        // always visible without needing any scrolling logic.
        private void AppendMatchEventRow(int minute, AgentMatchSimulator.AgentMatchEvent matchEvent)
        {
            if (matchEventFeedContainer == null)
            {
                return;
            }

            // Only the "N' GOAL" prefix is green for a goal - the description itself
            // never mentions "goal" (see BuildGoalEventText), so an inline <color> tag
            // around just the prefix, not the row's own base color, keeps the rest of the
            // line in normal text color instead of washing the whole row green.
            string line = matchEvent.IsGoal
                ? $"<b><color=#3ddc84>{minute}' GOAL</color></b> · {matchEvent.Description}"
                : $"{minute}' {matchEvent.Description}";

            GameObject row = new GameObject("EventRow", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(matchEventFeedContainer, false);

            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.preferredHeight = 44f;
            rowLayout.flexibleWidth = 1f;

            TextMeshProUGUI rowLabel = ManagerUITheme.BuildLabel(row.transform, line, 19, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
            RectTransform rowLabelRect = rowLabel.GetComponent<RectTransform>();
            rowLabelRect.offsetMin = new Vector2(0f, 6f);
            rowLabelRect.offsetMax = Vector2.zero;

            GameObject divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(row.transform, false);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.sizeDelta = new Vector2(0f, 1f);
            dividerRect.anchoredPosition = Vector2.zero;
            divider.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            matchEventFeedRows.Enqueue(row);

            while (matchEventFeedRows.Count > maxVisibleEventLines)
            {
                GameObject oldRow = matchEventFeedRows.Dequeue();
                if (oldRow != null) Destroy(oldRow);
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
                string line = $"<size=60%><sprite name=\"football-icon\"></size> {evt.ScorerName}  {evt.Minute}'\n";

                if (evt.HomeTeamScored)
                {
                    homeList += line;
                }
                else
                {
                    awayList += line;
                }
            }

            // Each block is now self-labeled with its own team name - now that the two
            // lists are stacked (not flanking left/right of center), position alone no
            // longer tells you which team is which. A team with no goals gets no block at
            // all rather than an empty header, rather than inventing a "no goals" line.
            // Header color is managed-team-relative (green) vs opponent (red), not simply
            // home/away - same `currentFixture.HomeTeam == managedTeamName` check used
            // elsewhere (e.g. OnConfirmTeamClicked, matchStatsBarsContainer's possession
            // math) so it stays correct on the (roughly half the time) away fixtures too.
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            string homeTeamName = matchHomeNameLabel != null ? matchHomeNameLabel.text : "";
            string awayTeamName = matchAwayNameLabel != null ? matchAwayNameLabel.text : "";
            string homeHex = ColorUtility.ToHtmlStringRGB(managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger);
            string awayHex = ColorUtility.ToHtmlStringRGB(managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent);

            if (matchHomeScorersLabel != null)
            {
                matchHomeScorersLabel.text = homeList.Length > 0
                    ? $"<b><color=#{homeHex}>{homeTeamName}</color></b>\n{homeList.TrimEnd('\n')}"
                    : "";
            }

            if (matchAwayScorersLabel != null)
            {
                matchAwayScorersLabel.text = awayList.Length > 0
                    ? $"<b><color=#{awayHex}>{awayTeamName}</color></b>\n{awayList.TrimEnd('\n')}"
                    : "";
            }
        }

        // Rebuilt per match (goal count varies) into the chrome-built matchGoalTimelineContainer
        // (see BuildMatchdayChrome) - a marker per goal placed proportionally along the 0-90'
        // track by real Minute, home above the line / away below it (mirrors the old
        // left/right-flanking convention for scorer lists, just turned 90 degrees), now with
        // the real minute printed next to each marker - the timeline is big enough for that to
        // read cleanly since it moved to its own full-width band below everything else.
        private void PopulateGoalTimeline(AgentMatchSimulator.AgentMatchResult result)
        {
            if (matchGoalTimelineContainer == null)
            {
                return;
            }

            for (int i = matchGoalTimelineContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(matchGoalTimelineContainer.GetChild(i).gameObject);
            }

            const float markerSize = 26f;
            // Marker sits a bit off the line, label sits further out beyond the marker -
            // same above(home)/below(away) split as the marker itself.
            const float markerOffset = 26f;
            const float labelOffset = 54f;
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;

            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal)
                {
                    continue;
                }

                float minuteFraction = Mathf.Clamp01(evt.Minute / 90f);
                float x = minuteFraction * matchGoalTimelineWidth;
                float sign = evt.HomeTeamScored ? 1f : -1f;

                // Green for the managed team's own goals, red for the opponent's - same
                // managed-team-relative convention as PopulateGoalScorerLists' headers,
                // not simply home=green/away=red.
                bool scoredByManagedTeam = evt.HomeTeamScored == managedIsHome;
                Color markerColor = scoredByManagedTeam ? ManagerUITheme.Accent : ManagerUITheme.Danger;

                GameObject marker = new GameObject($"GoalMarker_{evt.Minute}", typeof(RectTransform), typeof(Image));
                marker.transform.SetParent(matchGoalTimelineContainer, false);

                RectTransform markerRect = marker.GetComponent<RectTransform>();
                markerRect.anchorMin = new Vector2(0f, 0.5f);
                markerRect.anchorMax = new Vector2(0f, 0.5f);
                markerRect.pivot = new Vector2(0.5f, 0.5f);
                markerRect.sizeDelta = new Vector2(markerSize, markerSize);
                markerRect.anchoredPosition = new Vector2(x, sign * markerOffset);
                marker.GetComponent<Image>().color = markerColor;

                GameObject label = new GameObject($"GoalMarkerMinute_{evt.Minute}", typeof(RectTransform));
                label.transform.SetParent(matchGoalTimelineContainer, false);

                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.5f);
                labelRect.anchorMax = new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = new Vector2(70f, 24f);
                labelRect.anchoredPosition = new Vector2(x, sign * labelOffset);
                ManagerUITheme.BuildLabel(label.transform, $"{evt.Minute}'", 15, markerColor, TextAlignmentOptions.Center, FontStyles.Bold);
            }
        }

        public void OnFullTimeContinueClicked()
        {
            ApplyFixtureResult(currentFixture, lastSimulatedResult);

            currentFixtureIndex++;
            scouting.ResolveDueAssignments(currentFixtureIndex);

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

            // Bumped from 90 to 170, matching Match Day/Full-Time Summary's own header -
            // this screen already deliberately uses their 56/30pt score/name sizing ("the
            // mockup uses the identical header block for both screens", below), which
            // needs the same taller band; at 90 the caption sat only 16px from the very
            // top of the screen with almost no headroom (confirmed live: "on the border").
            const float headerHeight = 170f;
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
            ManagerUITheme.SetPointAnchor(captionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 18f));
            ManagerUITheme.BuildLabel(captionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);

            // Score/team-name sizes match the Full-Time Summary header exactly - the
            // mockup uses the identical header block for both screens.
            GameObject scoreObj = new GameObject("Score", typeof(RectTransform));
            scoreObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(scoreObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(200f, 68f));
            matchEventsScoreText = ManagerUITheme.BuildLabel(scoreObj.transform, "", 56, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject homeObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeObj.transform.SetParent(header.transform, false);
            RectTransform homeRect = homeObj.GetComponent<RectTransform>();
            homeRect.anchorMin = new Vector2(0.5f, 1f);
            homeRect.anchorMax = new Vector2(0.5f, 1f);
            homeRect.pivot = new Vector2(1f, 1f);
            homeRect.anchoredPosition = new Vector2(-110f, -64f);
            homeRect.sizeDelta = new Vector2(260f, 32f);
            matchEventsHomeNameLabel = ManagerUITheme.BuildLabel(homeObj.transform, "", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayObj.transform.SetParent(header.transform, false);
            RectTransform awayRect = awayObj.GetComponent<RectTransform>();
            awayRect.anchorMin = new Vector2(0.5f, 1f);
            awayRect.anchorMax = new Vector2(0.5f, 1f);
            awayRect.pivot = new Vector2(0f, 1f);
            awayRect.anchoredPosition = new Vector2(110f, -64f);
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
            // Settled by direct measurement rather than another guess: simulated a wheel
            // event via ExecuteEvents and read scrollRect.verticalNormalizedPosition
            // before/after. At sensitivity=-1, a simulated "scroll down" event tried to
            // move the list toward its already-at-the-top position (wrong direction,
            // clamped with no visible movement); at sensitivity=+1 (Unity's own default)
            // the same event correctly moved it toward later content. Every prior report
            // that +1 "still felt backwards" was against a continuously-running Play
            // Mode session that started before that build's fix was ever compiled in -
            // this screen's chrome is only built once per session (see the
            // matchEventsChromeBuilt-style guard), so a session that predates a fix will
            // never show it no matter how long it keeps running.
            scrollRect.scrollSensitivity = 1f;

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
            // BottomToTop, not the seemingly-obvious TopToBottom - ScrollRect's
            // verticalNormalizedPosition convention is 1=viewing the top of the content,
            // 0=viewing the bottom, and it drives the linked Scrollbar's .value directly.
            // Confirmed empirically (not guessed): with TopToBottom, value=1 (viewing the
            // list's top) rendered the handle at the BOTTOM of the track and vice versa -
            // exactly backwards, matching the reported "scroll to the bottom of the
            // scrollbar to see the top of the list" symptom.
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = scrollHandleRect;
            scrollbar.targetGraphic = scrollHandleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            Button continueButton = ManagerUITheme.BuildButton(footer.transform, "CONTINUE", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(continueButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(220f, 50f));
            continueButton.onClick.AddListener(OnFullTimeContinueClicked);

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchEventsPanel.transform));
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

                // Only the minute prefix is green for a goal - the description itself
                // never mentions "goal" (see BuildGoalEventText), so wrapping the whole
                // line in green read as over-highlighted (same fix as AppendMatchEventRow).
                string text = evt.IsGoal
                    ? $"<b><color=#3ddc84>{evt.Minute}'</color></b>   {evt.Description}"
                    : $"{evt.Minute}'   {evt.Description}";

                ManagerUITheme.BuildLabel(row.transform, text, 19, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
            }

            // Rows are cleared and rebuilt fresh every time this runs - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchEventsListContainer));
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
            ApplyDeveloperEasterEggPlayer(newTeam);

            squadsByTeamName[teamName] = newTeam;

            return newTeam;
        }

        // Reserve pool depth (session 7, injuries phase) - a safety net beneath the real
        // 20-man matchday squad (11 starters + 9 bench, the bench number deliberately
        // matching the real Premier League matchday-squad rule - never inflated to make
        // room for this). Only ever generated/consulted for managedTeamName - AI opponents
        // never run out of a position since there's no injury tracking for them at all
        // (see ManagerSquadRoles), so generating reserves for all 19 of them would be pure
        // waste. Deliberately generated at a softened team strength (0.85x) - a reserve
        // being a clear step down from the first team is the point, not a bug; there's
        // still enough RollAttribute variance for an occasional promising one.
        private readonly Dictionary<string, List<PlayerAgent>> reservePoolByTeamName = new();

        private static readonly PlayerPosition[] ReservePoolPositions =
        {
            PlayerPosition.GK,
            PlayerPosition.CB, PlayerPosition.CB,
            PlayerPosition.RB, PlayerPosition.LB,
            PlayerPosition.CM, PlayerPosition.CM,
            PlayerPosition.AM,
            PlayerPosition.RW, PlayerPosition.LW,
            PlayerPosition.ST
        };

        private List<PlayerAgent> GetOrCreateReservePool(string teamName)
        {
            if (reservePoolByTeamName.TryGetValue(teamName, out List<PlayerAgent> pool))
            {
                return pool;
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
            pool = new List<PlayerAgent>();

            // DefenceStrength is inverted in AgentSquadGenerator (defenceMultiplier =
            // 1/defenceStrength - lower DefenceStrength means fewer goals conceded, i.e.
            // a BETTER defence), so a genuine discount divides it rather than multiplying
            // like AttackStrength does. Multiplying it (the old code) accidentally made
            // reserve-pool defenders progressively BETTER the harder they were meant to
            // be discounted - confirmed live (see HANDOFF): discounting to 0.5x pushed a
            // CB's average Defending from 72.5 to 95.7, not down.
            foreach (PlayerPosition position in ReservePoolPositions)
            {
                pool.Add(squadGenerator.GenerateReservePlayer(position, strength.AttackStrength * 0.85f, strength.DefenceStrength / 0.85f));
            }

            reservePoolByTeamName[teamName] = pool;
            return pool;
        }

        // Promotes the best-fitting available reserve straight onto the real matchday
        // bench (AddBenchPlayer is already public on AgentTeam - no protected-file change
        // needed to do this) so they immediately show up everywhere the rest of the squad
        // does (Squad screen, Tactics Board substitute picker). Prefers an exact position
        // match; falls back to the reserve with the best position fit for the needed slot
        // (PlayerAgent.GetPositionFit, the same adjacency judgement formation-fit already
        // uses) rather than leaving a position with zero cover. Returns null if the pool
        // is completely exhausted - a real, visible squad crisis rather than silently
        // conjuring an infinite bench.
        private PlayerAgent CallUpReservePlayer(string teamName, PlayerPosition neededPosition)
        {
            List<PlayerAgent> pool = GetOrCreateReservePool(teamName);

            if (pool.Count == 0)
            {
                return null;
            }

            PlayerAgent best = pool[0];
            float bestFit = best.GetPositionFit(neededPosition);

            foreach (PlayerAgent candidate in pool)
            {
                float fit = candidate.GetPositionFit(neededPosition);
                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            pool.Remove(best);
            GetOrCreateAgentTeam(teamName).AddBenchPlayer(best);

            return best;
        }

        // Manager Mode-only side table (captaincy, set-piece takers, attack/defend role) -
        // see ManagerSquadRoles. Keyed by team name alongside squadsByTeamName; a team's
        // ManagerSquadRoles is created empty on first access and persists for the rest of
        // the play session, same lifetime as the AgentTeam it applies to.
        private ManagerSquadRoles GetOrCreateSquadRoles(string teamName)
        {
            if (!squadRolesByTeamName.TryGetValue(teamName, out ManagerSquadRoles roles))
            {
                roles = new ManagerSquadRoles();
                squadRolesByTeamName[teamName] = roles;
            }

            return roles;
        }

        // Developer easter egg (Manager Mode only, purely cosmetic) - deliberately kept
        // out of AgentSquadGenerator.cs entirely, since that generator is shared with
        // Research Mode's ResearchEvaluationRunner. Special-casing anything inside the
        // generation loop itself would shift the RNG draw sequence and silently change
        // every other generated player's stats too (the same risk flagged when GK stats
        // were discussed earlier this session). Applied strictly *after* GenerateSquad
        // returns, overwriting only Name/Age/Height on one already-generated player -
        // attributes/Overall are whatever normal generation rolled, untouched.
        private void ApplyDeveloperEasterEggPlayer(AgentTeam team)
        {
            if (team.TeamName != "Arsenal")
            {
                return;
            }

            PlayerAgent target = team.StartingEleven.Find(p => p.PrimaryPosition == PlayerPosition.ST)
                ?? team.Bench.Find(p => p.PrimaryPosition == PlayerPosition.ST);

            if (target == null)
            {
                return;
            }

            target.Name = "Hidde Rietberg";
            target.Age = 25;
            target.Height = 183f;
        }
    }
}
