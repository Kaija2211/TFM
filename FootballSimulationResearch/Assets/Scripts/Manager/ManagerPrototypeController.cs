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
    public partial class ManagerPrototypeController : MonoBehaviour
    {
        private const float UiScrollSensitivity = 70f;
        private const float UiCompactDropdownScrollSensitivity = 12f;

        // Playtest report (2026-08-16): the ONLY save call site anywhere in the project
        // was OnExitToTitleClicked - closing the game any other way (window close,
        // Alt+F4, task kill) silently discarded everything since the last explicit
        // "Exit to Title" click, which is what actually explained the reported "academy
        // player stats don't persist" bug (not a save-format gap - see OnApplicationQuit
        // below). Set true the first time ShowSeasonHub runs, whether from a brand-new
        // career or a loaded one - false the whole time on Splash/Title/Team Select/Save
        // Browser, so a quit from there correctly saves nothing.
        private bool careerLoadedThisSession;
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

        // Save Name field (session 15, multi-save support) - code-built via
        // ManagerUITheme.BuildInputField, not Editor-placed like managerNameInput above,
        // since there's no spare Editor-authored input field to reuse for it. Built once
        // inside BuildTeamSelectChrome and captioned the same way the Manager Name field
        // already is.
        private TMP_InputField saveNameInput;
        private GameObject teamSelectSaveNameCaption;

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
        private GameObject halfTimePanel;
        private TextMeshProUGUI halfTimeScoreLabel;
        private TextMeshProUGUI halfTimeStatsLabel;
        private bool waitingAtHalfTime;

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

        // Live team strength (session 16) - see RecalculateLiveTeamStrength for how
        // these get used. baselineAverageOverallByTeam is captured once per team in
        // GetOrCreateAgentTeam (or on load, for the managed team - see
        // RestoreCareerFromSaveData), the moment that team's squad first exists this
        // session. originalAttackStrengthByTeam/originalDefenceStrengthByTeam are a
        // separate, one-time-ever snapshot taken immediately after training (see Start)
        // - deliberately NOT read from the live statisticalModel.GetTeamStrength at
        // whatever later moment a team's squad happens to be generated, since a mid-
        // session Exit to Title -> Load a different save of the SAME club would
        // otherwise baseline the newly-loaded career against the abandoned one's
        // already-drifted numbers instead of the true historical value.
        private readonly Dictionary<string, float> baselineAverageOverallByTeam = new();
        private readonly Dictionary<string, float> originalAttackStrengthByTeam = new();
        private readonly Dictionary<string, float> originalDefenceStrengthByTeam = new();
        private readonly ManagerScouting scouting = new();
        private readonly ManagerAcademy academy = new();
        private readonly ManagerLoanTracker loanTracker = new();
        private readonly ManagerClubFinance finance = new();
        private readonly ManagerCareerHistory careerHistory = new();
        private readonly ManagerTransferNegotiation transferNegotiation = new();
        private readonly ManagerInbox inbox = new();
        private SeasonRecord lastSeasonRecord;
        private bool seasonEndRewardsAppliedForCurrentSeason;
        private readonly AgentSquadGenerator squadGenerator = new();
        private readonly AgentMatchSimulator matchSimulator = new();

        // Own StatisticalModel instance, trained on trainingSeasonFiles only. Completely
        // separate from ResearchEvaluationRunner's own StatisticalModel instance, so
        // nothing here can affect the research evaluation flow or its metrics.
        private readonly StatisticalModel statisticalModel = new();
        private WorldClubGenerationService worldGenerationService;
        private bool usesWorldGeneration;
        private float worldLeagueMeanOverall;
        private float worldLeagueMaxPositiveDelta;

        private List<OpenFootballMatch> allSeasonFixtures = new();
        private List<OpenFootballMatch> managedTeamFixtures = new();
        private int currentFixtureIndex;
        private readonly ManagerCareerCalendar careerCalendar = new();
        private const int FirstCareerSeasonStartYear = 2026;
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

        // Developer easter eggs - see ApplyDeveloperEasterEggPlayer. Real portraits, only
        // ever shown on these specific players' Player Detail screens. All four source
        // PNGs are pre-cropped to shoulders-and-above (same framing) - portrait display
        // (RefreshPlayerInspectUI) just needs a name-to-sprite lookup, not any cropping
        // logic of its own.
        private Sprite hiddePortraitSprite;
        private Sprite thomasPortraitSprite;
        private Sprite charliePortraitSprite;
        private Sprite victorPortraitSprite;

        // Splash screen logo (backlog item 12, session 11) - studio name "Eucna".
        private Sprite eucnaLogoSprite;

        private string managerName = "Manager";
        private bool titleScreenBuilt;

        // Title screen CONTINUE/LOAD CAREER visibility (session 15) - Thomas: these two
        // should only be there once a save actually exists, not shown-disabled like the
        // old placeholder convention. Built once in BuildTitleScreenContent, then shown/
        // hidden (and everything below re-flowed to close the gap) by
        // RefreshTitleScreenButtons every time the Title screen is shown - HasAnySaves()
        // can change mid-session (a brand new career gets its first save on Exit to Hub),
        // so this can't just be decided once at build time.
        private GameObject titleContinueButtonObj;
        private GameObject titleLoadCareerButtonObj;
        private GameObject titleSettingsButtonObj;
        private GameObject titleExitButtonObj;
        private float titleButtonsStartY;

        // Multi-save support (session 15) - identifies which on-disk save file this
        // session's career is tied to. currentSaveId is a GUID, generated fresh on a
        // new career (see OnConfirmTeamClicked) or copied from whichever save was
        // loaded (see ApplySaveData) - every OnExitToTitleClicked save this session
        // writes to that same file (ManagerSaveService.Save assigns one automatically
        // if still blank, but it should never be blank by the time a save actually
        // happens). currentSaveName is just the player-facing label shown in the Load
        // Career browser, captured from the new Save Name input field.
        private string currentSaveId;
        private string currentSaveName;

        // Splash screen (backlog item 12, session 11) - shown once before Title on
        // launch. splashAdvanced guards against both the timed auto-advance and a
        // click-to-skip firing twice (e.g. a click landing in the same frame the timer
        // was already about to fire).
        private GameObject splashPanel;
        private bool splashScreenBuilt;
        private bool splashAdvanced;
        private CanvasGroup splashCanvasGroup;
        private CanvasGroup titleCanvasGroup;
        private Coroutine splashSequenceCoroutine;
        private bool teamGridBuilt;
        private List<Button> teamGridButtons = new();

        // Settings screen (backlog item, session 12) - reachable from both Title and Hub
        // (each had its own disabled SETTINGS placeholder), so Back returns to whichever
        // one it was actually opened from rather than a fixed screen.
        private GameObject settingsPanel;
        private bool settingsScreenBuilt;
        private GameObject settingsReturnPanel;
        private readonly List<GameObject> spawnedSettingsRows = new();

        // Multiplier-framed (Thomas: "x1, x1.5, x2... and the other direction as well") -
        // labels are honest multipliers of matchReplayDurationSeconds' own ACTUAL current
        // value. Corrected 2026-08-11 (session 12, live bug report - "match speed is on
        // x0.5 on default... should be on x1 no?"): the field's C# declaration defaults to
        // 60f, but the scene's own serialized SerializeField value overrides that to 45 -
        // the true value every session has actually been running Match Day at, which the
        // first build of this array never checked for and assumed was 60. x1 now
        // correctly anchors on the real 45s; the fastest option still respects Thomas's
        // explicit "30 seconds max" cap (45/1.5 = 30 exactly, so x2 no longer fits under
        // that cap and was dropped down to x1.5).
        private static readonly float[] MatchSpeedSecondsOptions = { 90f, 60f, 45f, 30f };
        private static readonly string[] MatchSpeedLabels = { "x0.5", "x0.75", "x1", "x1.5" };
        private const int MatchSpeedDefaultIndex = 2;

        // Populated from the season file itself, so the list is always exactly the
        // clubs actually playing this season - no separately maintained team list.
        private List<string> availableTeamNames = new();
        private int selectedTeamIndex;

        // Tracks which matchdays have already had their non-managed-team fixtures
        // simulated, so a round's other 9 matches are only ever resolved once.
        private readonly HashSet<int> simulatedMatchdays = new();

        private OpenFootballMatch currentFixture;
        private OpenFootballMatch activeMatchFixture;
        private bool hasActiveMatchFixture;
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
        private Button inboxButton; // code-built (real now, session 13 - see BuildHubChrome), stored so RefreshHubUI can show an unread-count badge

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
        // Playtest backlog (session 14) - hidden mid-match (see RefreshTacticsBoardUI),
        // since replacing the whole XI wholesale mid-game reads as a very different
        // action from a considered pre-match pick and isn't something a real manager
        // would reach for once a match is underway.
        private Button autoPickButton;

        // Per-match tactical override (new feature suggestion, session 14) - Thomas:
        // formation/lineup changes that apply to just the next fixture without touching
        // the persistent Tactics default. Additive/opt-in, not a default-behavior
        // change - a manager who never touches this button gets the exact same
        // "edits are the new default" behavior the Tactics Board has always had.
        // Mentality already worked this way (selectedMentality resets to Balanced
        // before every kickoff, see OnNextMatchdayClicked/OnSimulateSeasonClicked), so
        // this closes the gap for formation/lineup specifically. Snapshot is
        // (Formation, StartingEleven) at the moment the toggle is armed - restored via
        // AgentTeam.ChangeFormation right after that one fixture resolves (see
        // ResolveNextMatchOnlyOverride, called from the same two matchday-advance sites
        // as ResolveMatchdayInboxTicks).
        private Button nextMatchOnlyButton;
        private bool nextMatchOnlyOverrideActive;
        private Formation nextMatchOverrideDefaultFormation;
        private List<PlayerAgent> nextMatchOverrideDefaultStartingEleven;
        private GameObject tacticsBoardFormationDropdown;

        // Injury block warning (backlog item, session 10) - the icon alone (session 9)
        // only made an injured starter visible, it never stopped a manager from
        // dragging one onto the pitch in the first place. Reuses the header band rather
        // than adding a whole new toast system - flat red text that fades in, sits for
        // a few seconds, then clears itself.
        private TextMeshProUGUI tacticsBoardWarningLabel;
        private Coroutine tacticsBoardWarningCoroutine;
        private GameObject positionSelectionDialog;

        // Tactics screen (session 7) - reached from the Tactics Board via a new TACTICS
        // button beside FORMATION. Centralizes captaincy/set-piece-taker assignment
        // (moved off Player Detail) alongside the new tactical sliders.
        private bool tacticsScreenChromeBuilt;
        private GameObject tacticsScreenPanel;
        private Button tacticsScreenBackButton;
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

        // Sortable Squad columns (backlog item, session 12, Thomas: "sortable by
        // Overall/Age/Transfer Value") - same -1/descending-first convention as
        // scoutingSortColumn. Sorts Starting XI and Bench independently rather than
        // flattening them into one list, since the section split itself is meaningful
        // (who's actually selected vs who isn't).
        private int squadSortColumn = -1;
        private bool squadSortDescending = true;

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

        private const int MaxSubsPerMatch = 5;
        private bool tacticsBoardOpenedMidMatch;
        private readonly List<(string offName, string offPosition, string onName, string onPosition, int minute)> matchSubsLog = new();
        private Formation midMatchDraftFormation;
        private List<PlayerAgent> midMatchDraftStartingEleven;
        private List<PlayerAgent> midMatchDraftBench;
        private List<PlayerAgent> midMatchDraftReserves;
        private Formation preMatchFormation;
        private List<PlayerAgent> preMatchStartingEleven;
        private List<PlayerAgent> preMatchBench;
        private List<PlayerAgent> preMatchReserves;
        private Dictionary<PlayerAgent, AttackDefendRole> preMatchAttackDefendRoles;
        private WidthSetting preMatchWidth;
        private DefensiveDepthSetting preMatchDefensiveDepth;
        private TempoSetting preMatchTempo;


        // Real football doesn't let a substituted-off player return - tracks who's
        // actually left the pitch via a genuine mid-match sub this match (session 10
        // fix: OnBenchPlayerDroppedOnPin used to allow cycling the same two players
        // back and forth indefinitely, resetting fatigue to fresh each time and
        // spamming duplicate "Subs Made" entries). Cleared alongside matchSubsLog.
        private readonly HashSet<PlayerAgent> playersSubbedOffThisMatch = new();

        // Live in-match player ratings (session 10) - managed team only, reset at
        // kickoff (see OnSimulateMatchClicked) and applied one event at a time in sync
        // with the event feed reveal in ReplayMatchCoroutine, so the ratings grid ticks
        // at the same pace the user is already watching the match log update.
        private readonly ManagerMatchRatings matchRatings = new();
        private RectTransform matchRatingsGridContainer;

        private int currentMatchMinute;
        private int liveHomeGoalsSoFar;
        private int liveAwayGoalsSoFar;

        // True only for the actual duration of ReplayMatchCoroutine (kickoff to full-
        // time) - see ApplyLiveMentalityChangeIfMatchInProgress, which needs an
        // unambiguous "is a match genuinely in progress right now" signal rather than
        // inferring it from panel active-states (which can be misleading mid-transition,
        // e.g. during OnSimulateMatchClicked's own setup for the *next* match).
        private bool isMatchCurrentlyLive;

        // Playtest report (2026-08-16): closing the game any way other than the Hub's
        // "Exit to Title" button (which is the only other save call site) previously
        // lost everything since the last explicit save - a Windows standalone build has
        // no other quit signal to hook, so this is the safety net. Guarded by
        // careerLoadedThisSession so a quit from Splash/Title/Team Select/Save Browser
        // (before any career state exists in memory) doesn't write a stale/default save.
        private void OnApplicationQuit()
        {
            if (!careerLoadedThisSession)
            {
                return;
            }

            ManagerSaveService.Save(BuildSaveData());
        }

        private void Start()
        {
            PurgeOrphanedRuntimePanels();

            weakFootStarSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/star-filled");
            footballIconSpriteAsset = Resources.Load<TMP_SpriteAsset>("Manager/football-icon");
            tfmLogoSprite = Resources.Load<Sprite>("Manager/tfm-logo");
            hiddePortraitSprite = Resources.Load<Sprite>("Manager/hidde_playerportrait");
            thomasPortraitSprite = Resources.Load<Sprite>("Manager/thomas_playerportrait");
            charliePortraitSprite = Resources.Load<Sprite>("Manager/charlie_playerportrait");
            victorPortraitSprite = Resources.Load<Sprite>("Manager/victor_playerportrait");
            eucnaLogoSprite = Resources.Load<Sprite>("Manager/eucna_logo_2");

            if (playNextMatchButton != null) playNextMatchButton.onClick.AddListener(OnNextMatchdayClicked);
            // Backlog items 13/15 (session 11) - both buttons now go through a wrapper
            // that may show a confirm dialog first; the real simulate logic (unchanged)
            // only runs once that's resolved. See OnSimulateMatchButtonClicked/
            // OnSimulateSeasonButtonClicked.
            if (simulateMatchButton != null) simulateMatchButton.onClick.AddListener(OnSimulateMatchButtonClicked);
            if (matchdayPrepBackButton != null) matchdayPrepBackButton.onClick.AddListener(OnMatchdayPrepBackClicked);
            if (simulateSeasonButton != null) simulateSeasonButton.onClick.AddListener(OnSimulateSeasonButtonClicked);
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

            // Click SFX (backlog item 11, session 11) - these are the Editor-placed
            // buttons wired above; every code-built button (the overwhelming majority)
            // already gets the same listener from ManagerUITheme.BuildButton itself.
            ManagerAudio.Initialize(gameObject);
            Button[] editorPlacedButtons =
            {
                playNextMatchButton, simulateMatchButton, matchdayPrepBackButton, simulateSeasonButton,
                viewSquadButton, inspectPreviousButton, inspectNextButton, inspectBackButton,
                skipToResultsButton, fullTimeContinueButton, attackingButton, balancedButton,
                defensiveButton, confirmTeamButton, teamSelectBackButton, exitToTitleButton
            };
            foreach (Button editorButton in editorPlacedButtons)
            {
                if (editorButton != null) editorButton.onClick.AddListener(ManagerAudio.PlayClick);
            }

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
            InitializeWorldGenerationService();

            // Live team strength (session 16) - one-time-ever snapshot of the pure
            // trained values, before any Manager Mode gameplay this session ever gets a
            // chance to mutate a TeamStrength object. See the dictionaries' own comment
            // for why this can't just be read lazily from statisticalModel.GetTeamStrength
            // at whatever later moment each team's squad happens to be generated.
            foreach (string teamName in availableTeamNames)
            {
                StatisticalModel.TeamStrength trainedStrength = statisticalModel.GetTeamStrength(teamName);
                originalAttackStrengthByTeam[teamName] = trainedStrength.AttackStrength;
                originalDefenceStrengthByTeam[teamName] = trainedStrength.DefenceStrength;
            }

            ShowSplashScreen();
        }

        // Code-built screens are intentionally not serialized. If Play Mode is stopped
        // during an unusual Editor transition, however, an unsaved runtime panel can be
        // left under the Canvas while this component's non-serialized reference resets
        // to null. On the next run that orphan has no owner to hide it and can cover the
        // splash/title while intercepting every click. Remove only our known generated
        // panel names, and only direct siblings of the Inspector-authored title panel.
        private void PurgeOrphanedRuntimePanels()
        {
            Transform canvasRoot = titlePanel != null ? titlePanel.transform.parent : transform.parent;
            if (canvasRoot == null) return;

            HashSet<string> generatedPanelNames = new()
            {
                "SplashPanel", "SettingsPanel", "EndOfSeasonPanel", "TacticsBoardPanel",
                "TacticsScreenPanel", "SquadBrowsePanel", "ScoutingPanel", "TransferMarketPanel",
                "InboxPanel", "TrophyRoomPanel", "SaveBrowserPanel", "HalfTimePanel",
                "ConfirmDialogPanel", "BidDialogPanel", "MatchEventsPanel"
            };

            for (int index = canvasRoot.childCount - 1; index >= 0; index--)
            {
                GameObject child = canvasRoot.GetChild(index).gameObject;
                if (generatedPanelNames.Contains(child.name)) DestroyImmediate(child);
            }
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

            // Already saved just above - a later quit from Title/Team Select shouldn't
            // redundantly re-save this same career via OnApplicationQuit's own guard.
            careerLoadedThisSession = false;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);

            ShowTitleScreen();
        }

        // --- Splash Screen (backlog item 12, session 11) - shown once before Title on
        // launch only (OnExitToTitleClicked goes straight to ShowTitleScreen, never back
        // through here - exiting mid-career shouldn't replay the studio splash). Reuses
        // Title's own background styling calls directly rather than the titlePanel
        // GameObject itself, since this needs to be its own separate panel shown first. ---

        // Timing for the fade in / hold / fade out sequence (Thomas's own ask, session
        // 11) - logo+wordmark fade in, sit for ~3s, fade out, then Title's own content
        // (buttons + TFM wordmark) fades in separately once Splash is gone.
        private const float SplashFadeInDuration = 0.8f;
        private const float SplashHoldDuration = 3f;
        private const float SplashFadeOutDuration = 0.8f;
        private const float TitleFadeInDuration = 0.6f;

        private void ShowSplashScreen()
        {
            if (!splashScreenBuilt)
            {
                BuildSplashScreenContent();
                splashScreenBuilt = true;
            }

            splashAdvanced = false;

            // titlePanel starts active by the scene's own Editor default (confirmed
            // live) - splashPanel renders in front regardless since it's created later
            // (later sibling), but explicitly hiding Title here avoids any doubt.
            if (titlePanel != null) titlePanel.SetActive(false);

            // seasonHubPanel ALSO starts active by the scene's own Editor default (an
            // older leftover from before Title/Splash existed) - unlike titlePanel above,
            // nothing was hiding it here, so it sat active-but-covered behind splashPanel
            // for the whole fade-in/hold, then genuinely bled through during the fade-OUT
            // as splashPanel's own alpha dropped toward 0 (confirmed live: Thomas saw the
            // real dark-navy Hub panel + "View Squad / Table" button flash through, not an
            // empty scene). ShowTitleScreen() already hides seasonHubPanel too, but that
            // only runs at the very end of AdvanceFromSplashToTitle - too late to prevent
            // this.
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);

            if (splashPanel != null) splashPanel.SetActive(true);
            if (splashCanvasGroup != null) splashCanvasGroup.alpha = 0f;

            splashSequenceCoroutine = StartCoroutine(PlaySplashSequence());
        }

        private IEnumerator PlaySplashSequence()
        {
            yield return FadeCanvasGroup(splashCanvasGroup, 0f, 1f, SplashFadeInDuration);
            yield return new WaitForSeconds(SplashHoldDuration);
            yield return FadeCanvasGroup(splashCanvasGroup, 1f, 0f, SplashFadeOutDuration);

            AdvanceFromSplashToTitle();
        }

        // Shared by every fade in this project's UI could ever want, not splash-specific
        // logic baked into one place - a plain CanvasGroup.alpha lerp over real time
        // (deliberately WaitForSeconds-equivalent unscaled stepping via Time.deltaTime
        // directly, not a single blocking wait, so it can't freeze solid if anything
        // ever set Time.timeScale=0 before this ever runs - nothing does yet, at Splash/
        // Title, but no reason to make that assumption load-bearing).
        private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null)
            {
                yield break;
            }

            group.alpha = from;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            group.alpha = to;
        }

        // Click-to-skip (the panel's own Button) and the natural end of
        // PlaySplashSequence both funnel through here - splashAdvanced guards against
        // either firing twice. A skip click stops the sequence coroutine outright
        // (whichever phase it's in - fade-in, hold, or fade-out) and jumps straight to
        // Title, which still gets its own fade-in either way for a consistent handoff.
        private void AdvanceFromSplashToTitle()
        {
            if (splashAdvanced)
            {
                return;
            }

            splashAdvanced = true;

            if (splashSequenceCoroutine != null)
            {
                StopCoroutine(splashSequenceCoroutine);
                splashSequenceCoroutine = null;
            }

            if (splashPanel != null) splashPanel.SetActive(false);

            ShowTitleScreen();

            // Music starts here, not at launch (Thomas: the splash should play in
            // silence) - see ManagerAudio.PlayMusic's own comment.
            ManagerAudio.PlayMusic();

            if (titleCanvasGroup == null && titlePanel != null)
            {
                titleCanvasGroup = titlePanel.GetComponent<CanvasGroup>();
                if (titleCanvasGroup == null)
                {
                    titleCanvasGroup = titlePanel.AddComponent<CanvasGroup>();
                }
            }

            if (titleCanvasGroup != null)
            {
                StartCoroutine(FadeCanvasGroup(titleCanvasGroup, 0f, 1f, TitleFadeInDuration));
            }
        }

        // Code-built entirely (no Editor-placed panel to wire), same precedent as every
        // other screen added after the initial Editor layout - Tactics Board, Match
        // Events, Career, etc. Parented alongside titlePanel so it shares the same root
        // canvas/sort order.
        private void BuildSplashScreenContent()
        {
            if (titlePanel == null || titlePanel.transform.parent == null)
            {
                return;
            }

            splashPanel = new GameObject("SplashPanel", typeof(RectTransform), typeof(Image), typeof(Button));
            splashPanel.transform.SetParent(titlePanel.transform.parent, false);
            RectTransform panelRect = splashPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Same background treatment as Title itself (see BuildTitleScreenContent) -
            // the backlog note's own ask ("reuse the title screen's existing background").
            ManagerUITheme.ApplyPanelBackground(splashPanel);
            ManagerUITheme.ApplyDiagonalGradientBackground(splashPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

            // The whole panel is a Button (invisible, same background as its own
            // targetGraphic) purely so a click anywhere skips straight to Title instead
            // of waiting out the full auto-advance delay.
            Button skipButton = splashPanel.GetComponent<Button>();
            skipButton.targetGraphic = splashPanel.GetComponent<Image>();
            skipButton.transition = Selectable.Transition.None;
            skipButton.onClick.AddListener(AdvanceFromSplashToTitle);
            skipButton.onClick.AddListener(ManagerAudio.PlayClick);

            // Scale/font matched to the "STUDIO SPLASH" mockup in Thomas's Claude Design
            // project ("Unity UX design possibilities", Football Manager UI Concepts.dc.
            // html) - logo 170px wide, 26px gap, wordmark 52pt Oswald Bold with 9pt
            // character spacing, white. Background deliberately NOT matched (Thomas: "it's
            // slightly different from us" - kept Title's own gradient instead). This
            // project's own UI is already built at the same 1920x1080 reference canvas the
            // mockup uses, so its pixel values map directly with no scale conversion.
            const float logoSize = 170f;
            const float logoGap = 26f;
            const float wordmarkHeight = 64f;
            const float stackTop = (1080f - (logoSize + logoGap + wordmarkHeight)) / 2f;

            GameObject logoObj = new GameObject("EucnaLogo", typeof(RectTransform));
            logoObj.transform.SetParent(splashPanel.transform, false);
            ManagerUITheme.AnchorTopCenter(logoObj, stackTop, logoSize, logoSize);

            if (eucnaLogoSprite != null)
            {
                Image logoImage = logoObj.AddComponent<Image>();
                logoImage.sprite = eucnaLogoSprite;
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;
            }

            GameObject wordmarkObj = new GameObject("EucnaWordmark", typeof(RectTransform));
            wordmarkObj.transform.SetParent(splashPanel.transform, false);
            ManagerUITheme.AnchorTopCenter(wordmarkObj, stackTop + logoSize + logoGap, 600f, wordmarkHeight);
            TextMeshProUGUI wordmarkLabel = ManagerUITheme.BuildLabel(wordmarkObj.transform, "eucna", 52, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            wordmarkLabel.characterSpacing = 9f;
            StartCoroutine(RecoverBlankLabelNextFrame(wordmarkLabel));

            // Fade in/hold/fade out (Thomas's own ask) - CanvasGroup on the whole panel so
            // logo+wordmark+background fade together as one unit, then Title fades in
            // separately once this coroutine hands off. See AdvanceFromSplashToTitle and
            // FadeCanvasGroup.
            splashCanvasGroup = splashPanel.AddComponent<CanvasGroup>();
        }

        // --- Title Screen ---

        private void ShowTitleScreen()
        {
            if (!titleScreenBuilt)
            {
                BuildTitleScreenContent();
                titleScreenBuilt = true;
            }

            RefreshTitleScreenButtons();

            if (titlePanel != null) titlePanel.SetActive(true);
            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);
        }

        // Session 15 - Thomas: CONTINUE/LOAD CAREER should only be there once a save has
        // actually been made, not shown-disabled. Re-run every time Title is shown
        // (HasAnySaves() can flip true mid-session - a brand new career's first Exit to
        // Hub is also its first save), re-flowing SETTINGS/EXIT upward to close the gap
        // when the two save-dependent buttons are hidden rather than leaving a blank
        // space where they'd normally sit.
        private void RefreshTitleScreenButtons()
        {
            if (titleContinueButtonObj == null)
            {
                return;
            }

            const float buttonWidth = 340f;
            const float buttonHeight = 52f;
            const float spacing = 12f;

            bool hasSaves = ManagerSaveService.HasAnySaves();

            titleContinueButtonObj.SetActive(hasSaves);
            titleLoadCareerButtonObj.SetActive(hasSaves);

            int slot = 1;

            if (hasSaves)
            {
                ManagerUITheme.AnchorTopCenter(titleContinueButtonObj, titleButtonsStartY + slot * (buttonHeight + spacing), buttonWidth, buttonHeight);
                slot++;
                ManagerUITheme.AnchorTopCenter(titleLoadCareerButtonObj, titleButtonsStartY + slot * (buttonHeight + spacing), buttonWidth, buttonHeight);
                slot++;
            }

            if (titleSettingsButtonObj != null)
            {
                ManagerUITheme.AnchorTopCenter(titleSettingsButtonObj, titleButtonsStartY + slot * (buttonHeight + spacing), buttonWidth, buttonHeight);
            }
            slot++;

            if (titleExitButtonObj != null)
            {
                ManagerUITheme.AnchorTopCenter(titleExitButtonObj, titleButtonsStartY + slot * (buttonHeight + spacing) + 8f, buttonWidth, 40f);
            }
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
            titleButtonsStartY = startY;

            GameObject newCareerObj = new GameObject("NewCareerButton", typeof(RectTransform), typeof(Image), typeof(Button));
            newCareerObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(newCareerObj, startY, buttonWidth, buttonHeight);
            newCareerObj.GetComponent<Image>().color = ManagerUITheme.Accent;
            Button newCareerButton = newCareerObj.GetComponent<Button>();
            newCareerButton.targetGraphic = newCareerObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(newCareerObj.transform, "NEW CAREER", 17, ManagerUITheme.OnAccent, TextAlignmentOptions.Center, FontStyles.Bold);
            newCareerButton.onClick.AddListener(OnTitleNewCareerClicked);
            newCareerButton.onClick.AddListener(ManagerAudio.PlayClick);

            // CONTINUE (session 15, multi-save support) - jumps straight into whichever
            // save was written to most recently, no picker. Position is set for real by
            // RefreshTitleScreenButtons (called every ShowTitleScreen) rather than fixed
            // here - this call just needs A valid rect to exist before that first runs.
            titleContinueButtonObj = new GameObject("ContinueButton", typeof(RectTransform), typeof(Image), typeof(Button));
            titleContinueButtonObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(titleContinueButtonObj, startY + buttonHeight + spacing, buttonWidth, buttonHeight);
            titleContinueButtonObj.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            Button continueButton = titleContinueButtonObj.GetComponent<Button>();
            continueButton.targetGraphic = titleContinueButtonObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(titleContinueButtonObj.transform, "CONTINUE", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
            continueButton.onClick.AddListener(OnContinueClicked);
            continueButton.onClick.AddListener(ManagerAudio.PlayClick);

            titleLoadCareerButtonObj = new GameObject("LoadCareerButton", typeof(RectTransform), typeof(Image), typeof(Button));
            titleLoadCareerButtonObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(titleLoadCareerButtonObj, startY + 2 * (buttonHeight + spacing), buttonWidth, buttonHeight);
            titleLoadCareerButtonObj.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            Button loadCareerButton = titleLoadCareerButtonObj.GetComponent<Button>();
            loadCareerButton.targetGraphic = titleLoadCareerButtonObj.GetComponent<Image>();
            // Real now (career-arc addition, session 8, Phase 5; opens the save browser
            // since session 15 rather than the old single fixed slot directly - see
            // OnOpenLoadCareerBrowserClicked). Thomas, session 15: this button (and
            // CONTINUE above) should only be present at all once a save actually exists,
            // not shown-disabled - RefreshTitleScreenButtons handles that, not a
            // SetDisabledPlaceholder branch here.
            ManagerUITheme.BuildLabel(titleLoadCareerButtonObj.transform, "LOAD CAREER", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
            loadCareerButton.onClick.AddListener(OnOpenLoadCareerBrowserClicked);
            loadCareerButton.onClick.AddListener(ManagerAudio.PlayClick);

            titleSettingsButtonObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            titleSettingsButtonObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(titleSettingsButtonObj, startY + 3 * (buttonHeight + spacing), buttonWidth, buttonHeight);
            titleSettingsButtonObj.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            Button settingsButton = titleSettingsButtonObj.GetComponent<Button>();
            settingsButton.targetGraphic = titleSettingsButtonObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(titleSettingsButtonObj.transform, "SETTINGS", 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
            // Real now (backlog item, session 12) - was a disabled placeholder with no
            // settings screen at all.
            settingsButton.onClick.AddListener(() => OnOpenSettingsClicked(titlePanel));
            settingsButton.onClick.AddListener(ManagerAudio.PlayClick);

            titleExitButtonObj = new GameObject("ExitButton", typeof(RectTransform), typeof(Image), typeof(Button));
            titleExitButtonObj.transform.SetParent(titleContentContainer, false);
            ManagerUITheme.AnchorTopCenter(titleExitButtonObj, startY + 4 * (buttonHeight + spacing) + 8f, buttonWidth, 40f);
            GameObject exitObj = titleExitButtonObj;
            exitObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;
            Button exitButton = exitObj.GetComponent<Button>();
            exitButton.targetGraphic = exitObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(exitObj.transform, "EXIT", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
            exitButton.onClick.AddListener(OnTitleExitClicked);
            exitButton.onClick.AddListener(ManagerAudio.PlayClick);

            // Version tag (session 15, pre-alpha build prep) - reads Application.version
            // directly rather than hardcoding "0.1", so it can never silently drift out
            // of sync with the real PlayerSettings > bundleVersion the build actually
            // ships under. Anchored to the panel itself, not the button stack, so it
            // stays put in the corner regardless of how many buttons are showing.
            GameObject versionObj = new GameObject("VersionTag", typeof(RectTransform));
            versionObj.transform.SetParent(titlePanel.transform, false);
            RectTransform versionRect = versionObj.GetComponent<RectTransform>();
            versionRect.anchorMin = new Vector2(0f, 0f);
            versionRect.anchorMax = new Vector2(0f, 0f);
            versionRect.pivot = new Vector2(0f, 0f);
            versionRect.anchoredPosition = new Vector2(20f, 16f);
            versionRect.sizeDelta = new Vector2(300f, 20f);
            ManagerUITheme.BuildLabel(versionObj.transform, $"v{Application.version} · PRE-ALPHA", 12, ManagerUITheme.TextDim, TextAlignmentOptions.MidlineLeft);
        }

        // --- Settings screen (backlog item, session 12) - reached from either Title's or
        // the Hub's SETTINGS button, both previously disabled placeholders. Music on/off
        // and match sim speed, the two contents actually proposed for it. Code-built, same
        // precedent as every other screen added post-launch. ---

        public void OnOpenSettingsClicked(GameObject returnPanel)
        {
            if (!settingsScreenBuilt)
            {
                BuildSettingsScreenChrome();
                settingsScreenBuilt = true;
            }

            settingsReturnPanel = returnPanel;
            if (returnPanel != null) returnPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(true);

            RefreshSettingsUI();
        }

        public void OnSettingsBackClicked()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (settingsReturnPanel != null) settingsReturnPanel.SetActive(true);
        }

        private void BuildSettingsScreenChrome()
        {
            if (titlePanel == null || titlePanel.transform.parent == null)
            {
                return;
            }

            settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform));
            settingsPanel.transform.SetParent(titlePanel.transform.parent, false);
            RectTransform panelRect = settingsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(settingsPanel);
            ManagerUITheme.ApplyDiagonalGradientBackground(settingsPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

            const float headerHeight = 90f;
            GameObject header = ManagerUITheme.BuildAccentBand(settingsPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -28f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SETTINGS", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(settingsPanel.transform, "BACK", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(150f, 36f));
            backButton.onClick.AddListener(OnSettingsBackClicked);
            backButton.onClick.AddListener(ManagerAudio.PlayClick);

            settingsPanel.SetActive(false);
        }

        private void RefreshSettingsUI()
        {
            if (settingsPanel == null)
            {
                return;
            }

            foreach (GameObject row in spawnedSettingsRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedSettingsRows.Clear();

            GameObject content = new GameObject("SettingsContent", typeof(RectTransform));
            content.transform.SetParent(settingsPanel.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 1f);
            contentRect.anchorMax = new Vector2(0.5f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(760f, 300f);
            contentRect.anchoredPosition = new Vector2(0f, -150f);
            spawnedSettingsRows.Add(content);

            int musicIndex = ManagerAudio.IsMusicEnabled() ? 0 : 1;
            BuildSliderRow(content.transform, "MUSIC", 0f, new[] { "ON", "OFF" }, musicIndex,
                index => { ManagerAudio.SetMusicEnabled(index == 0); RefreshSettingsUI(); });

            BuildMusicVolumeSlider(content.transform, 90f);

            // Falls back to x1 (not index 0/slowest) if the current value somehow isn't an
            // exact match for any option - the exact bug just fixed above, guarded against
            // recurring the same way if this field is ever hand-edited again.
            int speedIndexRaw = System.Array.IndexOf(MatchSpeedSecondsOptions, matchReplayDurationSeconds);
            int speedIndex = speedIndexRaw >= 0 ? speedIndexRaw : MatchSpeedDefaultIndex;
            BuildSliderRow(content.transform, "MATCH SPEED", 200f, MatchSpeedLabels, speedIndex,
                index => { matchReplayDurationSeconds = MatchSpeedSecondsOptions[index]; RefreshSettingsUI(); });

            StartCoroutine(RecoverBlankLabelsNextFrame(settingsPanel.transform));
        }

        private void BuildMusicVolumeSlider(Transform parent, float top)
        {
            GameObject labelObj = new GameObject("MusicVolumeLabel", typeof(RectTransform));
            labelObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelObj, top, 24f, 0f);
            ManagerUITheme.BuildLabel(labelObj.transform, "MUSIC VOLUME", 16, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject valueObj = new GameObject("MusicVolumeValue", typeof(RectTransform));
            valueObj.transform.SetParent(labelObj.transform, false);
            RectTransform valueRect = valueObj.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.anchoredPosition = Vector2.zero;
            valueRect.sizeDelta = new Vector2(100f, 0f);
            TextMeshProUGUI valueLabel = ManagerUITheme.BuildLabel(valueObj.transform, string.Empty, 15, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject sliderObj = new GameObject("MusicVolumeSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(sliderObj, top + 38f, 34f, 0f);
            sliderObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(6f, 8f);
            fillAreaRect.offsetMax = new Vector2(-6f, -8f);

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            GameObject handleArea = new GameObject("HandleArea", typeof(RectTransform));
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObj.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handleObj.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22f, 34f);
            handleObj.GetComponent<Image>().color = ManagerUITheme.TextPrimary;

            Slider slider = sliderObj.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleObj.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = ManagerAudio.GetMusicVolume();
            valueLabel.text = $"{Mathf.RoundToInt(slider.value * 100f)}%";
            slider.onValueChanged.AddListener(value =>
            {
                ManagerAudio.SetMusicVolume(value);
                if (valueLabel != null) valueLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
            });
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

    }
}
