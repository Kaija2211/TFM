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

        private void Start()
        {
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

            // Falls back to x1 (not index 0/slowest) if the current value somehow isn't an
            // exact match for any option - the exact bug just fixed above, guarded against
            // recurring the same way if this field is ever hand-edited again.
            int speedIndexRaw = System.Array.IndexOf(MatchSpeedSecondsOptions, matchReplayDurationSeconds);
            int speedIndex = speedIndexRaw >= 0 ? speedIndexRaw : MatchSpeedDefaultIndex;
            BuildSliderRow(content.transform, "MATCH SPEED", 90f, MatchSpeedLabels, speedIndex,
                index => { matchReplayDurationSeconds = MatchSpeedSecondsOptions[index]; RefreshSettingsUI(); });

            StartCoroutine(RecoverBlankLabelsNextFrame(settingsPanel.transform));
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

            // Save Name field (session 15) - own caption + code-built input, same
            // "MANAGER NAME"-style caption treatment, positioned by RefreshTeamSelectStepUI
            // right below the Manager Name field on step 1 and hidden entirely on step 2
            // (a save is created once per career, not re-named per club-select visit).
            GameObject saveNameCaption = new GameObject("SaveNameCaption", typeof(RectTransform));
            saveNameCaption.transform.SetParent(teamSelectPanel.transform, false);
            ManagerUITheme.BuildLabel(saveNameCaption.transform, "SAVE NAME", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            saveNameCaption.transform.SetAsFirstSibling();
            teamSelectSaveNameCaption = saveNameCaption;

            saveNameInput = ManagerUITheme.BuildInputField(teamSelectPanel.transform, "e.g. Rebuild Job", 22, characterLimit: 40);
            saveNameInput.transform.SetAsFirstSibling();

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

                    // The Editor-authored input field still carries TMP's stock "Enter
                    // text..." placeholder copy - never intentionally set, just never
                    // cleared. The big centered box plus the MANAGER NAME caption above it
                    // already say what the field is for.
                    placeholderLabel.text = "";
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

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 34;
                    if (managerNameInput.placeholder is TextMeshProUGUI bigPlaceholder) bigPlaceholder.fontSize = 34;
                }
                else
                {
                    ManagerUITheme.SetPointAnchor(
                        inputRect, new Vector2(0f, 1f), new Vector2(contentLeft, -contentTop), new Vector2(nameColumnWidth, 56f));

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 18;
                    if (managerNameInput.placeholder is TextMeshProUGUI smallPlaceholder) smallPlaceholder.fontSize = 18;
                }
            }

            // Save Name field (session 15) - sits just below the Manager Name field on
            // step 1 only, same centered-column layout. Never shown on step 2 - a save
            // is created once per new career, not re-named while picking a club.
            if (teamSelectSaveNameCaption != null)
            {
                teamSelectSaveNameCaption.SetActive(isNameStep);

                if (isNameStep)
                {
                    RectTransform captionRect = teamSelectSaveNameCaption.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(captionRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -52f), new Vector2(500f, 20f));

                    TextMeshProUGUI captionLabel = teamSelectSaveNameCaption.GetComponentInChildren<TextMeshProUGUI>();
                    if (captionLabel != null) captionLabel.alignment = TextAlignmentOptions.Center;
                }
            }

            if (saveNameInput != null)
            {
                saveNameInput.gameObject.SetActive(isNameStep);

                if (isNameStep)
                {
                    RectTransform saveNameRect = saveNameInput.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(saveNameRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(500f, 56f));
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

        // Session 16 - mirrors ApplySaveData's own clear block (see that method), just
        // for "starting fresh" instead of "restoring from a save". Every one of these
        // is a real thing Manager Mode accumulates over a career that has no other
        // reset point - a second career started in the same running session would
        // otherwise inherit all of it from whichever career ran before it.
        private void ResetSessionStateForNewCareer()
        {
            usesWorldGeneration = worldGenerationService != null;
            worldLeagueMeanOverall = 0f;
            worldLeagueMaxPositiveDelta = 0f;
            currentSeason = 1;
            currentFixtureIndex = 0;
            careerCalendar.StartSeason(FirstCareerSeasonStartYear);
            seasonEndRewardsAppliedForCurrentSeason = false;

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            recentFormByTeamId.Clear();

            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;

            squadsByTeamName.Clear();
            squadRolesByTeamName.Clear();
            simulatedMatchdays.Clear();
            loanTracker.Clear();
            academy.Clear();
            transferNegotiation.Clear();
            scouting.Clear();
            inbox.Clear();
            careerHistory.Clear();
            finance.Clear();

            // Live team strength (session 16) - restore every club's strength back to
            // the pure trained value BEFORE this new career generates any squads off it
            // (GetOrCreateAgentTeam reads strength.AttackStrength/DefenceStrength to
            // build a fresh squad) - otherwise a club that drifted during a previous
            // career this same session would hand that drift straight into the new one.
            baselineAverageOverallByTeam.Clear();
            foreach (string teamName in availableTeamNames)
            {
                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
                strength.AttackStrength = originalAttackStrengthByTeam[teamName];
                strength.DefenceStrength = originalDefenceStrengthByTeam[teamName];
            }
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

                // Save Name is optional (unlike Manager Name, doesn't block CONTINUE) -
                // falls back to a sensible default built from what's already been typed
                // rather than forcing a second required field for what's really just a
                // cosmetic label in the Load Career browser.
                currentSaveName = saveNameInput != null && !string.IsNullOrWhiteSpace(saveNameInput.text)
                    ? saveNameInput.text.Trim()
                    : $"{managerName}'s Save";

                // A fresh GUID per new career (session 15) - this is what
                // ManagerSaveService actually writes to disk as, so every save this
                // session (and any future one) lands on the same file instead of
                // minting a new one each time. Must happen here, not lazily inside
                // ManagerSaveService.Save, since OnExitToTitleClicked's autosave has no
                // other moment to know "this is a brand new career."
                currentSaveId = Guid.NewGuid().ToString("N");

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

            // Session 16 - real bug Thomas hit live: starting a second career in the
            // same running session (Editor Play Mode or the built .exe) inherited the
            // first career's entire Inbox and squad ("everything is the same except i
            // have a new name"). Nothing here ever reset between careers - only the
            // Load Save path did. Must run before anything below reads/writes squad,
            // fixture, or Inbox state for the new career.
            ResetSessionStateForNewCareer();

            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            if (managedTeamFixtures.Count == 0)
            {
                Debug.LogWarning($"ManagerPrototypeController: no fixtures found for '{managedTeamName}' in {seasonFile.name}.");
            }

            SendCareerStartInboxMessages();

            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Tier 1 potentialemails.txt batch (session 14) - the three career-start
        // flavour messages (#1 welcome, #2 pre-season expectations, #30 recruitment
        // teaser) land together the moment a brand new career actually begins, not on
        // every load - OnConfirmTeamClicked only ever runs once per career, unlike
        // ShowSeasonHub which also fires on load/return-to-hub. All three are pure
        // flavour text (no live data to bake in beyond the club name), so there's no
        // harm sending them at once - Thomas can read them whenever he opens the Inbox.
        private void SendCareerStartInboxMessages()
        {
            inbox.Add(InboxMessageType.WelcomeCareer, $"Welcome to {managedTeamName}",
                $"Welcome to {managedTeamName}. The board is pleased to confirm your appointment ahead of the new season. " +
                "Our expectations are simple: establish a clear identity, manage the squad responsibly, and ensure the club remains competitive over the full campaign. " +
                "You'll have immediate access to the squad screen, upcoming fixtures, tactical setup, and matchday controls. Good luck - the season starts now.",
                0);

            inbox.Add(InboxMessageType.SeasonExpectations, "Season Expectations",
                "Before the season begins, the board wants to outline its expectations. " +
                "We'll primarily judge performance through league position, consistency of results, and squad development. Individual defeats won't define your future, but long poor runs of form will naturally increase pressure. " +
                "We expect tactical decisions that reflect the strength of the squad and the quality of upcoming opposition.",
                0);

            inbox.Add(InboxMessageType.RecruitmentTeaser, "Player Recruitment Report Available",
                "The recruitment department has begun compiling reports on potential squad improvements. " +
                "This system isn't currently active, but future versions could let you review targets, compare player attributes, and strengthen weak areas of the squad.",
                0);
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
                ManagerUITheme.NormalizeButtonLabel(playNextMatchButton, "CONTINUE", ManagerUITheme.OnAccent, 20);
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
                transfersButton.onClick.AddListener(ManagerAudio.PlayClick);
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
            scoutingButton.onClick.AddListener(ManagerAudio.PlayClick);

            // CAREER (career-arc addition, session 8, Phase 4; folded from a standalone
            // "Trophy Room" button into a tabbed Career screen - backlog item 2, session
            // 11) - real, same styling as Squad/Transfers/Scouting rather than a disabled
            // placeholder. Internal identifiers (trophyRoomPanel, OnOpenTrophyRoomClicked,
            // etc.) deliberately kept as-is below - this button is the only thing that
            // changed name-wise, renaming everything downstream wasn't worth the risk.
            float trophyRoomTop = scoutingTop + subRowHeight + rowGap;

            GameObject trophyRoomObj = new GameObject("TrophyRoomButton", typeof(RectTransform), typeof(Image), typeof(Button));
            trophyRoomObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(trophyRoomObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -trophyRoomTop), new Vector2(menuWidth, subRowHeight));
            Button trophyRoomButton = trophyRoomObj.GetComponent<Button>();
            trophyRoomButton.targetGraphic = trophyRoomObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(trophyRoomObj.transform, "CAREER", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(trophyRoomButton);
            ManagerUITheme.NormalizeButtonLabel(trophyRoomButton, "CAREER", ManagerUITheme.TextBody, 17);
            trophyRoomButton.onClick.AddListener(OnOpenTrophyRoomClicked);
            trophyRoomButton.onClick.AddListener(ManagerAudio.PlayClick);

            float inboxTop = trophyRoomTop + subRowHeight + rowGap;

            // Real now (session 13) - phase 3 of the manager influence arc, the last
            // unclaimed item from the original session 7 plan (captaincy/fitness/morale
            // all shipped already, see project_manager_influence_arc in memory). Same
            // real-button styling as Squad/Transfers/Scouting/Career rather than the
            // disabled placeholder this used to be.
            GameObject inboxObj = new GameObject("InboxButton", typeof(RectTransform), typeof(Image), typeof(Button));
            inboxObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(inboxObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -inboxTop), new Vector2(menuWidth, subRowHeight));
            inboxButton = inboxObj.GetComponent<Button>();
            inboxButton.targetGraphic = inboxObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(inboxObj.transform, "INBOX", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(inboxButton);
            ManagerUITheme.NormalizeButtonLabel(inboxButton, "INBOX", ManagerUITheme.TextBody, 17);
            inboxButton.onClick.AddListener(OnOpenInboxClicked);
            inboxButton.onClick.AddListener(ManagerAudio.PlayClick);

            float settingsTop = inboxTop + subRowHeight + rowGap;

            GameObject settingsObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(settingsObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -settingsTop), new Vector2(menuWidth, subRowHeight));
            Button settingsButton = settingsObj.GetComponent<Button>();
            settingsButton.targetGraphic = settingsObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(settingsObj.transform, "SETTINGS", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            // Real now (backlog item, session 12) - was a disabled placeholder with no
            // settings screen at all, same as Title's own Settings button above.
            StyleHubActionButton(settingsButton);
            settingsButton.onClick.AddListener(() => OnOpenSettingsClicked(seasonHubPanel));
            settingsButton.onClick.AddListener(ManagerAudio.PlayClick);

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
                string window = careerCalendar.IsTransferWindowOpen ? "TRANSFER WINDOW OPEN" : "WINDOW CLOSED";
                bylineLabel.text = $"Manager {managerName}   ·   {careerCalendar.DisplayDate}   ·   {window}";
                bylineLabel.ForceMeshUpdate();
            }

            // Unread badge (session 13) - "INBOX (2)" style, same NormalizeButtonLabel
            // convention every other Hub button's label goes through.
            if (inboxButton != null)
            {
                int unread = inbox.UnreadCount;
                string inboxLabel = unread > 0 ? $"INBOX ({unread})" : "INBOX";
                ManagerUITheme.NormalizeButtonLabel(inboxButton, inboxLabel, ManagerUITheme.TextBody, 17);
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
            LeagueTable.Entry managedEntry = null;

            for (int i = 0; i < finalTable.Count; i++)
            {
                if (finalTable[i].TeamId == managedTeamId)
                {
                    finalPosition = i + 1;
                    managedEntry = finalTable[i];
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
                BoardBoost = boardBoost,
                Wins = managedEntry?.Wins ?? 0,
                Draws = managedEntry?.Draws ?? 0,
                Losses = managedEntry?.Losses ?? 0,
                Points = managedEntry?.Points ?? 0
            };

            careerHistory.AddRecord(lastSeasonRecord);

            // Tier 1 potentialemails.txt batch (#28/#29, session 14) - a top-half finish
            // reads as season success, bottom-half as disappointment. Simple top-half
            // cutoff rather than anything tied to pre-season expectations (there's no
            // real "expected finish" concept in this prototype to compare against).
            bool isTopHalf = finalPosition <= Mathf.Max(1, finalTable.Count / 2);
            inbox.Add(InboxMessageType.EndOfSeason, "Season Review",
                isTopHalf
                    ? $"The season has concluded, and the board is pleased with the progress made. Finishing {finalPosition}{GetOrdinalSuffix(finalPosition)} reflects good management, tactical decision-making, and effective squad use. This has been a strong foundation to build on."
                    : $"The season has concluded, and results have fallen short of expectations. A {finalPosition}{GetOrdinalSuffix(finalPosition)}-place finish had positive moments, but not enough consistency across the campaign. The board will review the situation carefully before deciding the next steps.",
                careerCalendar.CurrentDayNumber);
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
            AdvanceCalendarTo(new DateTime(careerCalendar.SeasonStartYear + 1, 6, 1), stopForNewInboxMessage: false);
            currentSeason++;
            careerCalendar.StartSeason(FirstCareerSeasonStartYear + currentSeason - 1);
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

            // Season-scoped Inbox-tick state (session 14) - same reasoning as
            // recentFormByTeamId just above: everything here is either "once per
            // season" (mid-season review) or keyed off a streak/cooldown that no longer
            // means anything once the fixture list and matchday count reset to 0.
            // injuredPlayersTracked specifically mirrors ManagerSquadRoles.ResetForNewSeason
            // clearing injuryReturnMatchday for every squad's roles right below.
            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();

            // A still-armed next-match-only override has nothing left to revert to once
            // the fixture list itself has rolled over - dropped rather than carried into
            // a season it was never meant for.
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;

            currentFixtureIndex = 0;
            simulatedMatchdays.Clear();
            transferNegotiation.ForceResolveAllPending(finance, managedTeamName, inbox, FindTeamContainingPlayer, careerCalendar.CurrentDayNumber);

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
                foreach (PlayerAgent player in team.Players) player.Age += 1;
            }

            // Discovered-but-unclaimed youth prospects keep developing whether or not
            // you've brought them into the Academy yet (session 13 mission rework) - no
            // more age-out-and-replace, the 3-matchday poach timer already keeps this
            // list from accumulating indefinitely (see ManagerScouting).
            scouting.AgeDiscoveredProspects();

            // Youth academy (session 9) - same "keeps developing whether or not you're
            // watching" reasoning as the scouting pool above.
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                player.Age += 1;
            }

            // Session 16 - Thomas: "the premier league teams stay the same season to
            // season... no relegation for the MSc version." This used to cycle through
            // every real historical season file (trainingSeasonFiles), picking a
            // different one each rollover - since real Premier League rosters genuinely
            // differ year to year, that silently swapped which 20 clubs the career was
            // even about (a team relegated in real history, e.g. Huddersfield Town,
            // could reappear mid-career with zero relegation/promotion actually
            // simulated). Dishonest for a project whose whole premise is a trained,
            // real-data-backed league - now always reuses the exact same seasonFile
            // season 1 started with, so the 20 clubs (and their trained
            // StatisticalModel strength) never change for the rest of the career.
            // trainingSeasonFiles is untouched elsewhere (TrainStatisticalModel still
            // combines all of them for strength training) - this only affects which
            // file drives THIS career's own fixture list/roster.
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
                        float playingTime = team.Reserves.Contains(player)
                            ? AssumedPlayingTimeFactorUncalledReserve
                            : AssumedPlayingTimeFactorAiFirstTeam;
                        ManagerPlayerDevelopment.ApplySeasonProgression(player, playingTime);
                    }
                }

                ApplyRetirementsForTeam(teamName, team);

                // Live team strength (session 16) - after growth/decline and retirement
                // replacements have both landed for this team, so the recalculation sees
                // this season's real final squad, not a stale one.
                RecalculateLiveTeamStrength(teamName, team);
            }

            // Discovered-but-unclaimed youth prospects (session 8, Phase 2; mission
            // rework session 13) - no real matches at all, so a low playing-time
            // assumption for growth-rate purposes, but exempted from neglect erosion
            // (see ApplySeasonProgression's own comment - they can't accrue real senior
            // appearances at this age, so a low factor here was never meant to read as
            // "being neglected").
            foreach (PlayerAgent player in scouting.DiscoveredProspects)
            {
                ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorYouthProspect, exemptFromErosion: true);
            }

            // Youth academy - growth moved to a per-matchday tick in session 16 (see
            // ApplyMatchdayAcademyProgression, called from SimulateFixture alongside the
            // managed team's own tick) - academy kids no longer get a season-end lump
            // sum here at all, matching how the managed squad itself works. Nothing left
            // to do for them at rollover: erosion was already exempt (they structurally
            // can't have real senior appearances at this age) and there's no delta badge
            // or prime-age noise that applies to a 14-16 year old.
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
                    originTeam.AddSquadPlayer(loan.Player);
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

            // Retirement announcement (session 14, Thomas's own suggestion while this
            // batch was being wired) - managed team only, same scope limit as every
            // other Inbox trigger; an AI club's retirements are invisible replacements,
            // nothing the manager would ever be told about. Sent here at season
            // rollover, before currentFixtureIndex resets to 0 for the new season (see
            // OnStartNewSeasonClicked), so matchdayReceived reads as 0 like the other
            // new-season messages (Welcome/Season Expectations use the same convention).
            if (teamName == managedTeamName)
            {
                foreach (PlayerAgent retiree in retirees)
                {
                    inbox.Add(InboxMessageType.Retirement, $"{retiree.Name} Retires",
                        $"{retiree.Name} has announced their retirement from professional football at age {retiree.Age}, bringing the curtain down on their playing career. " +
                        "Everyone at the club thanks them for their contribution and wishes them well for the future.",
                        0);
                }
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);

            foreach (PlayerAgent retiree in retirees)
            {
                PlayerAgent replacement = squadGenerator.GenerateReservePlayer(retiree.PrimaryPosition, strength.AttackStrength, strength.DefenceStrength);

                int startingIndex = team.StartingEleven.IndexOf(retiree);
                int benchIndex = team.Bench.IndexOf(retiree);
                int reserveIndex = team.Reserves.IndexOf(retiree);
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
                else if (reserveIndex >= 0)
                {
                    team.Reserves[reserveIndex] = replacement;
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
                UsesWorldGeneration = usesWorldGeneration,
                SaveId = currentSaveId,
                SaveName = currentSaveName,
                ManagerName = managerName,
                ManagedTeamName = managedTeamName,
                CurrentSeason = currentSeason,
                CurrentFixtureIndex = currentFixtureIndex,
                CurrentCareerDate = careerCalendar.SerializeDate(),
                SeasonStartYear = careerCalendar.SeasonStartYear,
                ActiveSeasonFileName = allSeasonFixtures.Count > 0 ? allSeasonFixtures[0].Season : seasonFile.name,
                ManagedSquad = AgentTeamSaveData.FromTeam(managedTeam),
                ManagedBudget = budget,
                ManagedTotalTransferSpend = finance.GetTotalTransferSpend(managedTeamName),
                ManagedTotalTransferIncome = finance.GetTotalTransferIncome(managedTeamName),
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

            // Youth academy (session 9; empty-slot rework session 13) - positional, see
            // ManagerSaveData.AcademySlots' comment.
            foreach (PlayerAgent slot in academy.GetFullAcademySlots())
            {
                data.AcademySlots.Add(slot == null
                    ? new AcademySlotSaveData { IsEmpty = true }
                    : new AcademySlotSaveData { IsEmpty = false, Prospect = PlayerAgentSaveData.FromPlayer(slot) });
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
                    BoardBoost = record.BoardBoost,
                    Wins = record.Wins,
                    Draws = record.Draws,
                    Losses = record.Losses,
                    Points = record.Points
                });
            }

            // Youth scouting missions + discoveries (session 13 rework) - positional
            // mission briefs, plus every still-unclaimed discovery paired with the
            // matchday it was found on so the poach timer resumes correctly.
            for (int slot = 0; slot < ManagerScouting.ScoutSlots; slot++)
            {
                data.ScoutMissions.Add(new ScoutMissionSaveData { TargetPositions = new List<PlayerPosition>(scouting.GetMissionPositions(slot)) });
            }

            foreach (PlayerAgent prospect in scouting.DiscoveredProspects)
            {
                data.DiscoveredProspects.Add(new DiscoveredProspectSaveData
                {
                    Prospect = PlayerAgentSaveData.FromPlayer(prospect),
                    DiscoveredMatchday = scouting.GetDiscoveredMatchday(prospect)
                });
            }

            // Inbox + transfer negotiation (session 13) - see ManagerSaveData's own
            // comment on PendingBidRefundOnLoad for why in-flight bids/transfer-scout
            // assignments don't round-trip by reference and are refunded instead.
            data.InboxMessages = inbox.BuildSaveList();
            data.PendingBidRefundOnLoad = transferNegotiation.GetTotalEscrowed();

            return data;
        }

        // Rebuilds every piece of state BuildSaveData captured, then jumps straight to
        // the Season Hub - a loaded career resumes exactly where Save & Exit left it,
        // not back at team select.
        private void ApplySaveData(ManagerSaveData data)
        {
            // Multi-save support (session 15) - so any save made later this session
            // (OnExitToTitleClicked) overwrites the file this career was actually loaded
            // from instead of minting a new one.
            currentSaveId = data.SaveId;
            currentSaveName = data.SaveName;
            usesWorldGeneration = data.UsesWorldGeneration && worldGenerationService != null;
            worldLeagueMeanOverall = 0f;
            worldLeagueMaxPositiveDelta = 0f;

            managerName = data.ManagerName;
            managedTeamName = data.ManagedTeamName;
            currentSeason = data.CurrentSeason;
            currentFixtureIndex = data.CurrentFixtureIndex;
            int restoredSeasonStartYear = data.SeasonStartYear > 0
                ? data.SeasonStartYear
                : FirstCareerSeasonStartYear + Mathf.Max(0, currentSeason - 1);
            careerCalendar.Restore(restoredSeasonStartYear, data.CurrentCareerDate, currentFixtureIndex);

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

            // Same scope limit as recentFormByTeamId just above - none of this survives
            // save/load either, so it's reset here rather than left holding stale
            // pre-load state (a mid-season-review flag from a different season, an
            // injured-player tracked set for a squad about to be rebuilt fresh below).
            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;

            foreach (LeagueTableEntrySaveData entry in data.TableEntries)
            {
                playableTable.SetEntry(entry.TeamId, entry.Played, entry.Wins, entry.Draws, entry.Losses, entry.GoalsFor, entry.GoalsAgainst, entry.Points);
            }

            squadsByTeamName.Clear();
            squadRolesByTeamName.Clear();
            simulatedMatchdays.Clear();
            loanTracker.Clear();
            academy.Clear();
            transferNegotiation.Clear();

            AgentTeam managedTeam = data.ManagedSquad.ToTeam();
            squadsByTeamName[managedTeamName] = managedTeam;

            // Live team strength (session 16) - this bypasses GetOrCreateAgentTeam
            // entirely (the managed squad is restored directly from save data, not
            // generated), so its average-Overall baseline would otherwise never get
            // captured and RecalculateLiveTeamStrength would silently no-op for the
            // player's own team for the rest of this session. Re-baselines the AVERAGE
            // to the just-loaded squad rather than trying to persist the original
            // career-start average across saves (would need new save-schema fields) - a
            // save/load "resets the clock" on live-strength drift for the managed team,
            // same real limitation AI clubs already have (their squads aren't persisted
            // at all, see squadsByTeamName.Clear() a few lines up - they regenerate
            // fresh, and fresh IS their new baseline too). originalAttackStrengthByTeam/
            // originalDefenceStrengthByTeam need no equivalent fix here - they're the
            // one-time-ever training snapshot (see Start), never mutated, so they're
            // already correct for any team including a freshly-loaded managed one.
            baselineAverageOverallByTeam[managedTeamName] = GetAverageOverall(managedTeam);
            if (usesWorldGeneration && TryGetWorldTarget(managedTeamName, out SquadQualityTarget loadedTarget))
            {
                ConfigureInitialWorldStrength(managedTeamName, loadedTarget.FirstTeamOverall);
            }

            Dictionary<string, PlayerAgent> managedPlayersById = new();
            foreach (PlayerAgent p in managedTeam.Players) managedPlayersById[p.PlayerId] = p;

            // Legacy saves kept a separate hidden emergency pool. New saves persist
            // reserves inside AgentTeamSaveData; import up to ten legacy players only
            // when that new list is absent, giving old careers the same 30-player shape.
            if (managedTeam.Reserves.Count == 0 && data.ManagedReservePool != null)
            {
                List<PlayerAgent> legacyPool = new List<PlayerAgent>();
                foreach (PlayerAgentSaveData dto in data.ManagedReservePool) legacyPool.Add(dto.ToPlayer());

                PlayerPosition[] migrationSlots =
                {
                    PlayerPosition.GK, PlayerPosition.CB, PlayerPosition.CB,
                    PlayerPosition.RB, PlayerPosition.LB, PlayerPosition.DM,
                    PlayerPosition.CM, PlayerPosition.RW, PlayerPosition.LW,
                    PlayerPosition.ST
                };

                foreach (PlayerPosition slot in migrationSlots)
                {
                    if (legacyPool.Count == 0) break;
                    PlayerAgent best = legacyPool[0];
                    float bestFit = best.GetPositionFit(slot);
                    foreach (PlayerAgent candidate in legacyPool)
                    {
                        float fit = candidate.GetPositionFit(slot);
                        if (fit > bestFit)
                        {
                            best = candidate;
                            bestFit = fit;
                        }
                    }

                    legacyPool.Remove(best);
                    managedTeam.AddReservePlayer(best);
                }
            }

            // Loan system (session 9) - re-register each restored player as on loan
            // (SendOnLoan rolls a fresh destination flavor name, harmless since it was
            // never saved - cosmetic only) rather than adding them back to
            // managedTeam.Players, since they're still out on loan in the loaded save.
            foreach (PlayerAgentSaveData dto in data.LoanedOutPlayers)
            {
                loanTracker.SendOnLoan(dto.ToPlayer(), managedTeamName);
            }

            // Youth academy (session 9; empty-slot rework session 13) - only restore if
            // the pool was actually generated before saving (data.AcademySlots.Count >
            // 0). If the player never opened the Academy tab this career, nothing was
            // ever generated to save - restoring an EMPTY list here would still mark the
            // pool as "already created" (GetOrCreateAcademyPool's null-check would never
            // trigger again), permanently freezing it at zero prospects instead of
            // lazily generating fresh ones the first time it's actually opened after
            // loading. Positional - a saved empty slot restores to the same index.
            if (data.AcademySlots.Count > 0)
            {
                List<PlayerAgent> restoredAcademy = new();
                foreach (AcademySlotSaveData slotData in data.AcademySlots)
                {
                    restoredAcademy.Add(slotData.IsEmpty ? null : slotData.Prospect.ToPlayer());
                }
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
            // + PendingBidRefundOnLoad - any bid still pending at save time was dropped
            // above (transferNegotiation.Clear()), so its escrowed amount is credited
            // back here instead of being silently lost (see ManagerSaveData's comment).
            finance.AdjustBudget(managedTeamName, data.ManagedBudget + data.PendingBidRefundOnLoad - finance.GetBudget(managedTeamName));
            finance.SetTotalTransferSpend(managedTeamName, data.ManagedTotalTransferSpend);
            finance.SetTotalTransferIncome(managedTeamName, data.ManagedTotalTransferIncome);

            foreach (SeasonRecordSaveData recordData in data.CareerHistory)
            {
                careerHistory.AddRecord(new SeasonRecord
                {
                    Season = recordData.Season,
                    FinalPosition = recordData.FinalPosition,
                    IsChampion = recordData.IsChampion,
                    PrizeMoney = recordData.PrizeMoney,
                    BoardBoost = recordData.BoardBoost,
                    Wins = recordData.Wins,
                    Draws = recordData.Draws,
                    Losses = recordData.Losses,
                    Points = recordData.Points
                });
            }

            // Youth scouting missions + discoveries (session 13 rework).
            for (int slot = 0; slot < data.ScoutMissions.Count && slot < ManagerScouting.ScoutSlots; slot++)
            {
                scouting.RestoreMissionBrief(slot, data.ScoutMissions[slot].TargetPositions);
            }

            List<PlayerAgent> restoredDiscoveries = new();
            List<int> restoredDiscoveryMatchdays = new();
            foreach (DiscoveredProspectSaveData dto in data.DiscoveredProspects)
            {
                restoredDiscoveries.Add(dto.Prospect.ToPlayer());
                int discoveredDay = dto.DiscoveredMatchday;
                if (data.SaveVersion < 3 && discoveredDay > 0)
                {
                    DateTime legacyDate = careerCalendar.GetFixtureDate(Mathf.Max(0, discoveredDay - 1));
                    discoveredDay = careerCalendar.CurrentDayNumber + (int)(legacyDate.Date - careerCalendar.CurrentDate.Date).TotalDays;
                }
                restoredDiscoveryMatchdays.Add(discoveredDay);
            }
            scouting.RestoreDiscoveredProspects(restoredDiscoveries, restoredDiscoveryMatchdays);

            if (data.SaveVersion < 3)
            {
                foreach (InboxMessageSaveData message in data.InboxMessages)
                {
                    if (message.MatchdayReceived <= 0) continue;
                    DateTime legacyDate = careerCalendar.GetFixtureDate(Mathf.Max(0, message.MatchdayReceived - 1));
                    message.MatchdayReceived = careerCalendar.CurrentDayNumber + (int)(legacyDate.Date - careerCalendar.CurrentDate.Date).TotalDays;
                }
            }

            inbox.RestoreFromSave(data.InboxMessages);

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

        // CONTINUE (session 15) - the most recently *saved* career, no picker.
        public void OnContinueClicked()
        {
            ManagerSaveData data = ManagerSaveService.GetMostRecentSave();
            if (data == null)
            {
                return;
            }

            if (titlePanel != null) titlePanel.SetActive(false);

            ApplySaveData(data);
        }

        // Called from a Save Browser row (session 15) - loads a specific career by
        // SaveId rather than just "whichever one's newest."
        private void OnLoadSpecificCareerClicked(string saveId)
        {
            ManagerSaveData data = ManagerSaveService.Load(saveId);
            if (data == null)
            {
                return;
            }

            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(false);
            if (titlePanel != null) titlePanel.SetActive(false);

            ApplySaveData(data);
        }

        // --- Save Browser (session 15, multi-save support) - reached from Title's LOAD
        // CAREER button. Same code-built-panel/scroll-view pattern as the Inbox screen -
        // a flat list of every save on disk, newest-saved first, each row a clickable
        // card loading that specific career. ---

        private bool saveBrowserChromeBuilt;
        private GameObject saveBrowserPanel;
        private RectTransform saveBrowserContentContainer;
        private readonly List<GameObject> spawnedSaveBrowserRows = new();

        public void OnOpenLoadCareerBrowserClicked()
        {
            if (!saveBrowserChromeBuilt)
            {
                BuildSaveBrowserChrome();
                saveBrowserChromeBuilt = true;
            }

            if (titlePanel != null) titlePanel.SetActive(false);
            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(true);

            RefreshSaveBrowserUI();
        }

        public void OnSaveBrowserBackClicked()
        {
            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(false);

            ShowTitleScreen();
        }

        private void BuildSaveBrowserChrome()
        {
            if (titlePanel == null || titlePanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            saveBrowserPanel = new GameObject("SaveBrowserPanel", typeof(RectTransform));
            saveBrowserPanel.transform.SetParent(titlePanel.transform.parent, false);
            RectTransform panelRect = saveBrowserPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(saveBrowserPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(saveBrowserPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(400f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "LOAD CAREER", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(160f, 36f));
            backButton.onClick.AddListener(OnSaveBrowserBackClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("SaveBrowserScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(saveBrowserPanel.transform, false);
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
            saveBrowserContentContainer = contentObj.GetComponent<RectTransform>();
            saveBrowserContentContainer.anchorMin = new Vector2(0f, 1f);
            saveBrowserContentContainer.anchorMax = new Vector2(1f, 1f);
            saveBrowserContentContainer.pivot = new Vector2(0.5f, 1f);
            saveBrowserContentContainer.anchoredPosition = Vector2.zero;
            saveBrowserContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 10f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = saveBrowserContentContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;

            StartCoroutine(RecoverBlankLabelsNextFrame(saveBrowserPanel.transform));
        }

        private void RefreshSaveBrowserUI()
        {
            if (saveBrowserContentContainer == null)
            {
                return;
            }

            foreach (GameObject row in spawnedSaveBrowserRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedSaveBrowserRows.Clear();

            List<ManagerSaveData> saves = ManagerSaveService.ListAllSaves();
            // Newest-saved first - same ordinal-string sort GetMostRecentSave uses, just
            // over the whole list instead of just picking the max.
            saves.Sort((a, b) => string.CompareOrdinal(b.LastSavedUtc, a.LastSavedUtc));

            if (saves.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(saveBrowserContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "No saved careers yet.", 18, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedSaveBrowserRows.Add(emptyObj);
            }
            else
            {
                foreach (ManagerSaveData data in saves)
                {
                    spawnedSaveBrowserRows.Add(BuildSaveBrowserRow(data));
                }
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(saveBrowserContentContainer));
        }

        private const float SaveBrowserRowHeight = 88f;

        private GameObject BuildSaveBrowserRow(ManagerSaveData data)
        {
            GameObject row = new GameObject($"Save_{data.SaveId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(saveBrowserContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = SaveBrowserRowHeight;
            Image rowImage = row.GetComponent<Image>();
            rowImage.color = ManagerUITheme.CardNeutralAlt;

            Button rowButton = row.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;
            string capturedSaveId = data.SaveId;
            rowButton.onClick.AddListener(() => OnLoadSpecificCareerClicked(capturedSaveId));

            GameObject nameObj = new GameObject("SaveName", typeof(RectTransform));
            nameObj.transform.SetParent(row.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(20f, -14f);
            nameRect.sizeDelta = new Vector2(-260f, 30f);
            ManagerUITheme.BuildLabel(nameObj.transform, data.SaveName, 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);

            GameObject detailObj = new GameObject("Detail", typeof(RectTransform));
            detailObj.transform.SetParent(row.transform, false);
            RectTransform detailRect = detailObj.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0f, 1f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.pivot = new Vector2(0f, 1f);
            detailRect.anchoredPosition = new Vector2(20f, -48f);
            detailRect.sizeDelta = new Vector2(-260f, 26f);
            ManagerUITheme.BuildLabel(detailObj.transform, $"{data.ManagerName} · {data.ManagedTeamName} · Season {data.CurrentSeason}", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            GameObject dateObj = new GameObject("LastSaved", typeof(RectTransform));
            dateObj.transform.SetParent(row.transform, false);
            RectTransform dateRect = dateObj.GetComponent<RectTransform>();
            dateRect.anchorMin = new Vector2(1f, 0.5f);
            dateRect.anchorMax = new Vector2(1f, 0.5f);
            dateRect.pivot = new Vector2(1f, 0.5f);
            dateRect.anchoredPosition = new Vector2(-110f, 0f);
            dateRect.sizeDelta = new Vector2(160f, 30f);
            ManagerUITheme.BuildLabel(dateObj.transform, FormatSaveTimestamp(data.LastSavedUtc), 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            // Session 16 - Thomas: "an option to delete a save... a crap top now from
            // testing... a confirmation thing too before you actually delete so i dont
            // accidentally delete my main one." ManagerSaveService.Delete already existed
            // (session 15) but was never wired to any UI. A child Button drawn on top of
            // the row's own full-row load Button correctly intercepts its own clicks via
            // normal Unity UI raycasting - clicking DELETE never also fires the row's own
            // load-this-save handler underneath it.
            Button deleteButton = ManagerUITheme.BuildButton(row.transform, "DELETE", ManagerUITheme.Danger, ManagerUITheme.TextPrimary, 13);
            RectTransform deleteRect = deleteButton.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(1f, 0.5f);
            deleteRect.anchorMax = new Vector2(1f, 0.5f);
            deleteRect.pivot = new Vector2(1f, 0.5f);
            deleteRect.anchoredPosition = new Vector2(-20f, 0f);
            deleteRect.sizeDelta = new Vector2(80f, 36f);
            string capturedSaveName = data.SaveName;
            deleteButton.onClick.AddListener(() => OnDeleteSaveClicked(capturedSaveId, capturedSaveName));

            return row;
        }

        private void OnDeleteSaveClicked(string saveId, string saveName)
        {
            ShowConfirmDialog(
                $"Delete \"{saveName}\"? This can't be undone.",
                "DELETE", () =>
                {
                    ManagerSaveService.Delete(saveId);
                    RefreshSaveBrowserUI();
                    RefreshTitleScreenButtons();
                },
                "CANCEL", null);
        }

        // LastSavedUtc is stored as DateTime.ToString("o") (round-trip ISO 8601) purely
        // because that format sorts correctly as a plain string - reparsed here just for
        // a friendlier on-screen "12 Aug 2026, 14:03" instead of the raw ISO string.
        private static string FormatSaveTimestamp(string lastSavedUtc)
        {
            if (string.IsNullOrEmpty(lastSavedUtc))
            {
                return "";
            }

            return DateTime.TryParse(lastSavedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed.ToLocalTime().ToString("d MMM yyyy, HH:mm")
                : "";
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

        // Academy sortable columns (session 15, Thomas: "like with our other lists, id
        // like to be able to sort our academy players") - separate state from
        // scoutingSortColumn/scoutingSortDescending above since the two grids don't
        // share a column layout (Academy has no NATION/EXPIRES columns). Originally
        // built without sorting at all ("short, fixed-order list of slots - sorting
        // adds little," see RefreshAcademyUI's own older comment) - Thomas asked for it
        // anyway, so it's wired the same way every other sortable grid in this file is.
        private int academySortColumn = -1;
        private bool academySortDescending = true;

        // Youth academy tab (session 9) - shares this screen/list with World Scouting.
        private Button scoutingAcademyTabButton;
        private Button scoutingWorldTabButton;
        private bool scoutingShowingAcademyTab;

        public void OnOpenScoutingClicked()
        {
            CloseAcademyIntakeDropdown();

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
            CloseAcademyIntakeDropdown();

            scoutingShowingAcademyTab = false;
            RefreshScoutingUI();
        }

        private void OnScoutingAcademyTabClicked()
        {
            CloseAcademyIntakeDropdown();

            scoutingShowingAcademyTab = true;
            RefreshScoutingUI();
        }

        public void OnScoutingBackClicked()
        {
            CloseAcademyIntakeDropdown();

            if (scoutingPanel != null) scoutingPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Extra vertical room reserved above the scroll list, only on the Missions tab,
        // for the two scout-mission brief boxes (session 13 rework) - toggled per tab in
        // RefreshScoutingUI rather than built into two separate screens.
        private const float ScoutingMissionsAreaHeight = 210f;
        private GameObject scoutingMissionsContainer;
        private RectTransform scoutingScrollViewRect;
        private readonly List<GameObject> spawnedMissionBoxes = new();

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
            // Renamed from "SCOUTING" (session 13) - Thomas: the page covers both the
            // youth missions and the Academy that develops what they find, "Youth"
            // covers the whole page better than "Scouting" ever did.
            ManagerUITheme.BuildLabel(titleObj.transform, "YOUTH", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

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

            // Renamed from "WORLD SCOUTING" (session 13 mission rework).
            scoutingWorldTabButton = ManagerUITheme.BuildButton(header.transform, "SCOUTING MISSIONS", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(scoutingWorldTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-436f, -27f), new Vector2(200f, 36f));
            scoutingWorldTabButton.onClick.AddListener(OnScoutingWorldTabClicked);

            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            // Mission brief area (session 13) - built once here, shown/hidden and
            // repopulated per refresh (see RefreshScoutingUI/RefreshMissionsArea)
            // rather than living inside the scrollable grid content, so it can sit at a
            // fixed position independent of how many rows the list below has.
            scoutingMissionsContainer = new GameObject("MissionsArea", typeof(RectTransform));
            scoutingMissionsContainer.transform.SetParent(scoutingPanel.transform, false);
            ManagerUITheme.AnchorTopStretch(scoutingMissionsContainer, headerHeight + 10f, ScoutingMissionsAreaHeight, sideMargin);

            GameObject scrollViewObj = new GameObject("ScoutingScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(scoutingPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scoutingScrollViewRect = scrollViewRect;
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
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = 25f;

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
                ManagerUITheme.NormalizeButtonLabel(scoutingWorldTabButton, "SCOUTING MISSIONS", !scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (scoutingAcademyTabButton != null && scoutingAcademyTabButton.TryGetComponent(out Image academyImage))
            {
                academyImage.color = scoutingShowingAcademyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(scoutingAcademyTabButton, "ACADEMY", scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            // Mission boxes (session 13) only make sense on the Missions tab - toggled
            // here, along with pushing the scroll list further down to make room while
            // they're visible (see BuildScoutingChrome's own comment on the fixed
            // ScoutingMissionsAreaHeight reservation).
            const float headerHeight = 90f;
            bool showMissions = !scoutingShowingAcademyTab;

            if (scoutingMissionsContainer != null) scoutingMissionsContainer.SetActive(showMissions);

            if (scoutingScrollViewRect != null)
            {
                float top = showMissions ? headerHeight + 10f + ScoutingMissionsAreaHeight + 30f : headerHeight + 40f;
                scoutingScrollViewRect.offsetMax = new Vector2(scoutingScrollViewRect.offsetMax.x, -top);
            }

            if (showMissions) RefreshMissionsArea();

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

        // --- Scout mission briefs (session 13) - two fixed slots, each up to 3 target
        // positions, reusing the same absolute-positioned chip-toggle technique the
        // Academy focus-stats picker already established (see BuildFocusStatsPicker). ---

        private void RefreshMissionsArea()
        {
            foreach (GameObject box in spawnedMissionBoxes)
            {
                if (box != null) Destroy(box);
            }
            spawnedMissionBoxes.Clear();

            if (scoutingMissionsContainer == null) return;

            const float boxWidth = 780f;
            const float gap = 40f;

            for (int slot = 0; slot < ManagerScouting.ScoutSlots; slot++)
            {
                GameObject box = BuildMissionBox(slot, slot * (boxWidth + gap), boxWidth);
                spawnedMissionBoxes.Add(box);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(scoutingMissionsContainer.transform));
        }

        private GameObject BuildMissionBox(int slotIndex, float x, float width)
        {
            GameObject box = new GameObject($"MissionBox_{slotIndex}", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(scoutingMissionsContainer.transform, false);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 1f);
            boxRect.anchorMax = new Vector2(0f, 1f);
            boxRect.pivot = new Vector2(0f, 1f);
            boxRect.anchoredPosition = new Vector2(x, 0f);
            boxRect.sizeDelta = new Vector2(width, ScoutingMissionsAreaHeight);
            box.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            IReadOnlyList<PlayerPosition> briefed = scouting.GetMissionPositions(slotIndex);
            bool active = scouting.IsMissionActive(slotIndex);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(box.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(width - 200f, 24f));
            ManagerUITheme.BuildLabel(titleObj.transform, $"SCOUT {slotIndex + 1}", 16, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            string statusText = active
                ? $"Searching for: {string.Join(", ", briefed)}"
                : "No brief set - pick up to 3 positions and send them out.";
            GameObject statusObj = new GameObject("Status", typeof(RectTransform));
            statusObj.transform.SetParent(box.transform, false);
            ManagerUITheme.SetPointAnchor(statusObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16f, -38f), new Vector2(width - 32f, 20f));
            ManagerUITheme.BuildLabel(statusObj.transform, statusText, 13, active ? ManagerUITheme.Accent : ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);

            // Position chip grid - selection is staged in missionBriefSelection until
            // SEND is clicked, so browsing positions doesn't reassign a live mission
            // brief on every click.
            if (!missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> staged))
            {
                staged = new List<PlayerPosition>(briefed);
                missionBriefSelection[slotIndex] = staged;
            }

            PlayerPosition[] allPositions = (PlayerPosition[])System.Enum.GetValues(typeof(PlayerPosition));
            const float chipWidth = 74f;
            const float chipHeight = 28f;
            const float chipGapX = 6f;
            const float chipGapY = 6f;
            const int chipsPerRow = 7;

            for (int i = 0; i < allPositions.Length; i++)
            {
                PlayerPosition position = allPositions[i];
                bool isSelected = staged.Contains(position);

                int row = i / chipsPerRow;
                int col = i % chipsPerRow;
                float chipX = 16f + col * (chipWidth + chipGapX);
                float chipY = -68f - row * (chipHeight + chipGapY);

                GameObject chip = new GameObject($"PosChip_{position}", typeof(RectTransform), typeof(Image), typeof(Button));
                chip.transform.SetParent(box.transform, false);
                RectTransform chipRect = chip.GetComponent<RectTransform>();
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(0f, 1f);
                chipRect.pivot = new Vector2(0f, 1f);
                chipRect.sizeDelta = new Vector2(chipWidth, chipHeight);
                chipRect.anchoredPosition = new Vector2(chipX, chipY);

                Image chipImage = chip.GetComponent<Image>();
                chipImage.color = isSelected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

                Button chipButton = chip.GetComponent<Button>();
                chipButton.targetGraphic = chipImage;
                chipButton.onClick.AddListener(() => OnMissionPositionToggled(slotIndex, position));

                ManagerUITheme.BuildLabel(chip.transform, position.ToString(), 12, isSelected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            // Session 15 fix: CANCEL spans x=16-156 (16 + its own 140 width) - SEND used
            // to start at x=91, well inside that span, so the two buttons visibly
            // overlapped (confirmed live, Thomas caught it on the Youth screen). SEND now
            // starts after CANCEL's right edge plus a 16px gap.
            Button cancelButton = ManagerUITheme.BuildButton(box.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16f, 20f), new Vector2(140f, 34f));
            int capturedSlot = slotIndex;
            cancelButton.onClick.AddListener(() => OnCancelMissionClicked(capturedSlot));

            Button sendButton = ManagerUITheme.BuildButton(box.transform, active ? "UPDATE BRIEF" : "SEND", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(sendButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16f + 140f + 16f, 20f), new Vector2(150f, 34f));
            sendButton.onClick.AddListener(() => OnSendMissionClicked(capturedSlot));

            return box;
        }

        // Staged position picks per slot, cleared to match the real brief whenever a
        // mission is actually sent/cancelled - see BuildMissionBox's own comment.
        private readonly Dictionary<int, List<PlayerPosition>> missionBriefSelection = new();

        private void OnMissionPositionToggled(int slotIndex, PlayerPosition position)
        {
            if (!missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> staged))
            {
                staged = new List<PlayerPosition>();
                missionBriefSelection[slotIndex] = staged;
            }

            if (staged.Contains(position))
            {
                staged.Remove(position);
            }
            else if (staged.Count < ManagerScouting.MaxTargetPositions)
            {
                staged.Add(position);
            }

            RefreshMissionsArea();
        }

        private void OnSendMissionClicked(int slotIndex)
        {
            List<PlayerPosition> staged = missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> s) ? s : new List<PlayerPosition>();
            scouting.SetMissionBrief(slotIndex, staged);
            RefreshMissionsArea();
        }

        private void OnCancelMissionClicked(int slotIndex)
        {
            scouting.CancelMission(slotIndex);
            missionBriefSelection[slotIndex] = new List<PlayerPosition>();
            RefreshMissionsArea();
        }

        // --- Discovered prospects list (session 13) - a discovery IS the scouting act,
        // so every row here already has full real stats (only Potential stays fuzzy,
        // same as Academy's own kids - see ManagerScouting.GetDisplayPotential). No more
        // per-row "assign a scout" action; clicking a row just opens full detail. ---

        private void RefreshWorldScoutingUI()
        {
            List<PlayerAgent> allProspects = new List<PlayerAgent>(scouting.DiscoveredProspects);

            if (scoutingSortColumn >= 0)
            {
                allProspects.Sort((a, b) => CompareScoutingColumn(a, b, scoutingSortColumn, scoutingSortDescending));
            }

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    bylineTMP.text = $"{allProspects.Count} discovered   ·   unclaimed for {ManagerScouting.DaysUntilPoached} days and they're poached   ·   bring them into an empty Academy slot to keep them";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(ScoutingColumnHeaders, ScoutingColumnFractions, OnScoutingColumnHeaderClicked, scoutingSortColumn, scoutingSortDescending);

            foreach (PlayerAgent prospect in allProspects)
            {
                string nation = ManagerPlayerNationality.GetNationality(prospect).Name;
                int left = scouting.GetDaysUntilPoached(prospect, careerCalendar.CurrentDayNumber);
                string expiresCell = left <= 2 ? $"<color=#e05a5a>{left}d left</color>" : $"{left}d left";

                string[] cells =
                {
                    prospect.Name,
                    prospect.PrimaryPosition.ToString(),
                    prospect.Age.ToString(),
                    nation,
                    GetDisplayRating(prospect.GetOverallRating()).ToString(),
                    scouting.GetDisplayPotential(prospect),
                    expiresCell
                };

                scoutingListView.AddCustomGridRow(prospect, cells, ScoutingColumnFractions, p => OpenScoutedProspectDetail(p, allProspects),
                    onNameClicked: p => OpenScoutedProspectDetail(p, allProspects));
            }
        }

        private static readonly string[] ScoutingColumnHeaders = { "PROSPECT", "POS", "AGE", "NATION", "OVR", "POTENTIAL", "EXPIRES" };
        private static readonly float[] ScoutingColumnFractions = { 0.20f, 0.07f, 0.07f, 0.22f, 0.09f, 0.14f, 0.21f };

        // Youth academy (session 9) - "grew them myself," complementary to the Missions
        // tab's "found them abroad." No NATION column (they're your own kids, not a
        // scouted discovery). Empty slots (session 13) render their own row with a
        // "BRING IN SCOUTED PLAYER" action instead of a normal grid row - see
        // AddPrebuiltRow. Sortable headers added session 15 (Thomas asked, after this
        // comment originally argued sorting "adds little" for a short fixed-order list) -
        // empty slots have no PlayerAgent to sort by, so when a sort is active they're
        // grouped at the bottom below every real prospect rather than interleaved by
        // their original slot index; with no sort active (academySortColumn == -1) the
        // list still renders in plain slot order exactly as before, empty slots included
        // in place, unchanged from the original behavior.
        private void RefreshAcademyUI()
        {
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            academy.GetOrCreateAcademyPool(squadGenerator, strength.AttackStrength, strength.DefenceStrength);
            IReadOnlyList<PlayerAgent> slots = academy.GetFullAcademySlots();
            IReadOnlyList<int> emptySlotIndices = academy.GetEmptySlotIndices();

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    int emptyCount = emptySlotIndices.Count;
                    bylineTMP.text = $"{ManagerAcademy.AcademySlots} academy slots ({emptyCount} empty)   ·   promotable to reserves at age {ManagerAcademy.PromotionAge}   ·   click a promotable prospect to promote";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(
                AcademyColumnHeaders,
                AcademyColumnFractions,
                OnAcademyColumnHeaderClicked,
                academySortColumn,
                academySortDescending
            );

            List<PlayerAgent> filledOnly = new List<PlayerAgent>(academy.GetAcademyPoolForAging());

            if (academySortColumn >= 0)
            {
                List<PlayerAgent> sortedFilled = new List<PlayerAgent>(filledOnly);
                sortedFilled.Sort((a, b) => CompareAcademyColumn(a, b, academySortColumn, academySortDescending));

                foreach (PlayerAgent prospect in sortedFilled)
                {
                    BuildAcademyRow(prospect, filledOnly);
                }

                // Important: use the REAL empty slot indices.
                // The old version used 0..emptySlotCount, which can point at filled slots after sorting.
                foreach (int emptySlotIndex in emptySlotIndices)
                {
                    scoutingListView.AddPrebuiltRow(BuildEmptyAcademySlotRow(emptySlotIndex));
                }

                return;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                PlayerAgent prospect = slots[i];

                if (prospect == null)
                {
                    scoutingListView.AddPrebuiltRow(BuildEmptyAcademySlotRow(i));
                    continue;
                }

                BuildAcademyRow(prospect, filledOnly);
            }
        }

        private void BuildAcademyRow(PlayerAgent prospect, List<PlayerAgent> filledOnly)
        {
            bool promotable = academy.CanPromote(prospect);
            string status = promotable ? "<color=#3ddc84>PROMOTABLE</color>" : "DEVELOPING";

            string[] cells =
            {
                prospect.Name,
                prospect.PrimaryPosition.ToString(),
                prospect.Age.ToString(),
                GetDisplayRating(prospect.GetOverallRating()).ToString(),
                scouting.GetDisplayPotential(prospect),
                status
            };

            scoutingListView.AddCustomGridRow(prospect, cells, AcademyColumnFractions, OnAcademyProspectClicked,
                onNameClicked: p => OpenAcademyProspectDetail(p, filledOnly));
        }

        private void OnAcademyColumnHeaderClicked(int column)
        {
            if (academySortColumn == column)
            {
                academySortDescending = !academySortDescending;
            }
            else
            {
                academySortColumn = column;
                academySortDescending = true;
            }

            RefreshAcademyUI();
        }

        // Column indices match AcademyColumnHeaders. Potential sorts by the same fuzzy-
        // band display string a Squad/Transfers list already sorts by (see
        // GetScoutingPotentialSortKey) rather than the true hidden value. Status sorts
        // PROMOTABLE before DEVELOPING on a descending (default) click, matching every
        // other column's "most interesting first" convention.
        private int CompareAcademyColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
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
                    result = GetScoutingPotentialSortKey(a).CompareTo(GetScoutingPotentialSortKey(b));
                    break;
                case 5:
                    result = academy.CanPromote(a).CompareTo(academy.CanPromote(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private static readonly string[] AcademyColumnHeaders = { "PROSPECT", "POS", "AGE", "OVR", "POTENTIAL", "STATUS" };
        private static readonly float[] AcademyColumnFractions = { 0.24f, 0.10f, 0.10f, 0.12f, 0.18f, 0.26f };

        // Session 13 - an empty slot is a real row (same rowHeight as a normal grid
        // row, via its own LayoutElement) with a single "BRING IN SCOUTED PLAYER"
        // action, rather than just vanishing from the list - the manager should be able
        // to see exactly how many open slots exist and fill them deliberately.
        private GameObject BuildEmptyAcademySlotRow(int slotIndex)
        {
            const float rowHeight = 40f;

            GameObject row = new GameObject($"EmptySlot_{slotIndex}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(scoutingListView.transform, false);
            row.GetComponent<LayoutElement>().preferredHeight = rowHeight;
            row.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelObj.transform, "EMPTY SLOT", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);

            Button bringInButton = ManagerUITheme.BuildButton(row.transform, "BRING IN SCOUTED PLAYER", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 12);
            ManagerUITheme.SetPointAnchor(bringInButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(240f, 30f));
            bringInButton.onClick.AddListener(() => OnBringInScoutedPlayerClicked(slotIndex));

            return row;
        }

        // Reuses the exact dropdown scaffold/option-row technique the Tactics screen's
        // role-assignment pickers already established (BuildEmptyDropdownScaffold/
        // PopulateDropdownOptions) - a scrollable "pick a player from a list" UI, just
        // sourced from ManagerScouting.DiscoveredProspects instead of the squad.
        private GameObject academyIntakeDropdown;

        private void OnBringInScoutedPlayerClicked(int slotIndex)
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
            }

            IReadOnlyList<PlayerAgent> academySlots = academy.GetFullAcademySlots();
            bool validSlot = slotIndex >= 0 && slotIndex < academySlots.Count;
            bool slotEmpty = validSlot && academySlots[slotIndex] == null;

            if (!validSlot || !slotEmpty)
            {
                Debug.LogWarning(
                    $"Academy intake blocked: slotIndex={slotIndex}, validSlot={validSlot}, slotEmpty={slotEmpty}."
                );
                return;
            }

            List<PlayerAgent> options = new List<PlayerAgent>(scouting.DiscoveredProspects);

            academyIntakeDropdown = BuildEmptyDropdownScaffold(scoutingPanel.transform, options.Count);

            RectTransform dropdownRect = academyIntakeDropdown.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(0.5f, 0.5f);
            dropdownRect.anchorMax = new Vector2(0.5f, 0.5f);
            dropdownRect.pivot = new Vector2(0.5f, 0.5f);
            dropdownRect.anchoredPosition = Vector2.zero;
            dropdownRect.sizeDelta = new Vector2(600f, dropdownRect.sizeDelta.y);
            academyIntakeDropdown.transform.SetAsLastSibling();

            // Defensive: if the shared dropdown scaffold starts inactive, this makes the picker visible.
            academyIntakeDropdown.SetActive(true);

            Transform content = academyIntakeDropdown.transform.Find("Viewport/Content");

            if (content == null)
            {
                Debug.LogWarning("Academy intake dropdown failed: could not find Viewport/Content.");
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
                return;
            }

            PopulateDropdownOptions(
                content,
                options,
                prospect => OnScoutedPlayerChosenForSlot(slotIndex, prospect),
                p => new[]
                {
            p.PrimaryPosition.ToString(),
            p.Age.ToString(),
            GetDisplayRating(p.GetOverallRating()).ToString()
                }
            );

            StartCoroutine(RecoverBlankLabelsNextFrame(academyIntakeDropdown.transform));
        }

        private void OnScoutedPlayerChosenForSlot(int slotIndex, PlayerAgent prospect)
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
            }

            // prospect is null when "— None —" was picked.
            if (prospect == null)
            {
                return;
            }

            IReadOnlyList<PlayerAgent> academySlots = academy.GetFullAcademySlots();
            bool validSlot = slotIndex >= 0 && slotIndex < academySlots.Count;
            bool slotEmpty = validSlot && academySlots[slotIndex] == null;
            bool wasDiscovered = scouting.DiscoveredProspects.Contains(prospect);

            bool placed = academy.PlaceProspectInSlot(slotIndex, prospect);

            if (placed)
            {
                scouting.RemoveDiscoveredProspect(prospect);
                Debug.Log($"Academy intake complete: brought in {prospect.Name} to academy slot {slotIndex}.");
            }
            else
            {
                Debug.LogWarning(
                    $"Academy intake failed: player={prospect.Name}, slotIndex={slotIndex}, validSlot={validSlot}, slotEmpty={slotEmpty}, wasDiscovered={wasDiscovered}."
                );
            }

            RefreshScoutingUI();
        }

        private void OnAcademyProspectClicked(PlayerAgent prospect)
        {
            if (academy.TryPromoteToReserves(prospect))
            {
                GetOrCreateAgentTeam(managedTeamName).AddReservePlayer(prospect);
            }

            RefreshScoutingUI();
        }

        // Manual release (backlog item 8, session 11; empty-slot rework session 13) -
        // leaves the slot genuinely empty now instead of auto-backfilling, see
        // ManagerAcademy.ReleaseProspect's own comment.
        private void OnReleaseAcademyProspectClicked(PlayerAgent prospect)
        {
            academy.ReleaseProspect(prospect);

            OnInspectBackClicked();
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
                    // Ascending = most urgent (fewest matchdays left) first by default,
                    // matching how every other column's "descending: true" first click
                    // already reads as "most interesting first" for that column.
                    result = scouting.GetDaysUntilPoached(b, careerCalendar.CurrentDayNumber).CompareTo(scouting.GetDaysUntilPoached(a, careerCalendar.CurrentDayNumber));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private float GetScoutingPotentialSortKey(PlayerAgent prospect)
        {
            string[] parts = scouting.GetDisplayPotential(prospect).Split('-');
            float lowerBand = parts.Length > 0 && float.TryParse(parts[0], out float lower) ? lower : 0f;
            float upperBand = parts.Length > 1 && float.TryParse(parts[1], out float upper) ? upper : 0f;

            // Two prospects can share a lower band (it's quantized to steps of 5) - the
            // upper band as a fractional tiebreaker (max 99/1000) makes "70-95" sort above
            // "70-82" without ever flipping the primary lower-band ordering.
            return lowerBand + (upperBand / 1000f);
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

        // --- Transfer Market (career-arc addition, session 8, Phase 3; bid/negotiation
        // redesign session 13): Buy tab browses every other club's squad plus already-
        // scouted youth prospects. AI-squad targets need their own transfer scout
        // assigned first (separate pool from World Scouting/Academy, see
        // ManagerTransferNegotiation) before a price range and Make Bid unlock - no more
        // one-click instant buy. A submitted bid escrows the amount and resolves a
        // matchday later via Inbox, with the selling club's own squad depth at that
        // position feeding how reluctant they are to sell. Sell tab is unchanged from
        // session 8: only your own Bench (Starting XI deliberately excluded - selling
        // your best XI by a misclick is the one mistake this screen shouldn't let you
        // make casually), one-click sell at 0.9x MarketValue. No AI-vs-AI transfer
        // activity (explicit scope boundary, see HANDOFF) - rival squads only change via
        // progression/retirement, never trading amongst themselves. Same code-built-
        // panel/scroll-view pattern as Squad/Scouting. ---

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
        private readonly ManagerTransferSearch transferSearch = new();
        private TMP_InputField transferPlayerSearchInput;
        private TMP_InputField transferClubSearchInput;
        private TMP_InputField transferNationSearchInput;
        private TMP_InputField transferMinAgeInput;
        private TMP_InputField transferMaxAgeInput;
        private Button transferPositionFilterButton;
        private Button transferClearFiltersButton;
        private int transferPositionFilterIndex = -1;

        // Session 13 - looks up a player's current AI club purely by scanning
        // squadsByTeamName, rather than trusting transferMarketRowClubs (only ever
        // populated for whichever tab is currently rendered, cleared on every refresh -
        // unreliable once a matchday tick needs to resolve a bid/scouting assignment
        // outside the Transfer Market screen entirely). Returns null for a scouted
        // prospect (never in squadsByTeamName, lives in the scouting pools instead) -
        // ManagerTransferNegotiation already treats a null selling team as "no depth
        // information available," the same case a prospect is meant to hit.
        private AgentTeam FindTeamContainingPlayer(PlayerAgent player)
        {
            foreach (KeyValuePair<string, AgentTeam> kvp in squadsByTeamName)
            {
                if (kvp.Value.Players.Contains(player)) return kvp.Value;
            }

            return null;
        }

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

            const float headerHeight = 180f;

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

            const float filterTop = 122f;
            const float filterHeight = 38f;
            float filterX = 60f;
            transferPlayerSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 220f, filterHeight, "Player name");
            transferClubSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 220f, filterHeight, "Club");
            transferNationSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 190f, filterHeight, "Nationality");
            transferMinAgeInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 120f, filterHeight, "Min age", numeric: true);
            transferMaxAgeInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 120f, filterHeight, "Max age", numeric: true);

            transferPositionFilterButton = ManagerUITheme.BuildButton(header.transform, "ANY POSITION", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(transferPositionFilterButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(filterX, -filterTop), new Vector2(180f, filterHeight));
            transferPositionFilterButton.onClick.AddListener(OnCycleTransferPositionFilter);
            filterX += 192f;

            transferClearFiltersButton = ManagerUITheme.BuildButton(header.transform, "CLEAR", ManagerUITheme.CardNeutral, ManagerUITheme.TextMuted, 12);
            ManagerUITheme.SetPointAnchor(transferClearFiltersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(filterX, -filterTop), new Vector2(110f, filterHeight));
            transferClearFiltersButton.onClick.AddListener(OnClearTransferFilters);

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
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = 25f;

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

        private TMP_InputField BuildTransferFilterInput(Transform parent, ref float x, float top, float width, float height, string placeholder, bool numeric = false)
        {
            GameObject container = new GameObject($"{placeholder.Replace(" ", string.Empty)}Filter", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            ManagerUITheme.SetPointAnchor(container.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(width, height));
            TMP_InputField input = ManagerUITheme.BuildInputField(container.transform, placeholder, 13, numeric ? 2 : 32);
            RectTransform inputRect = input.GetComponent<RectTransform>();
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = Vector2.one;
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;
            if (numeric) input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.onValueChanged.AddListener(_ => OnTransferSearchChanged());
            x += width + 12f;
            return input;
        }

        private void OnTransferSearchChanged()
        {
            transferSearch.PlayerName = transferPlayerSearchInput?.text ?? string.Empty;
            transferSearch.ClubName = transferClubSearchInput?.text ?? string.Empty;
            transferSearch.Nationality = transferNationSearchInput?.text ?? string.Empty;
            transferSearch.MinimumAge = int.TryParse(transferMinAgeInput?.text, out int minimumAge) ? minimumAge : null;
            transferSearch.MaximumAge = int.TryParse(transferMaxAgeInput?.text, out int maximumAge) ? maximumAge : null;
            if (transferMarketShowingBuyTab) RefreshTransferMarketUI();
        }

        private void OnCycleTransferPositionFilter()
        {
            Array values = Enum.GetValues(typeof(PlayerPosition));
            transferPositionFilterIndex++;
            if (transferPositionFilterIndex >= values.Length) transferPositionFilterIndex = -1;
            transferSearch.Position = transferPositionFilterIndex < 0
                ? null
                : (PlayerPosition?)values.GetValue(transferPositionFilterIndex);
            string label = transferSearch.Position.HasValue ? transferSearch.Position.Value.ToString() : "ANY POSITION";
            ManagerUITheme.NormalizeButtonLabel(transferPositionFilterButton, label, ManagerUITheme.TextBody, 12);
            RefreshTransferMarketUI();
        }

        private void OnClearTransferFilters()
        {
            transferSearch.Clear();
            transferPositionFilterIndex = -1;
            if (transferPlayerSearchInput != null) transferPlayerSearchInput.text = string.Empty;
            if (transferClubSearchInput != null) transferClubSearchInput.text = string.Empty;
            if (transferNationSearchInput != null) transferNationSearchInput.text = string.Empty;
            if (transferMinAgeInput != null) transferMinAgeInput.text = string.Empty;
            if (transferMaxAgeInput != null) transferMaxAgeInput.text = string.Empty;
            ManagerUITheme.NormalizeButtonLabel(transferPositionFilterButton, "ANY POSITION", ManagerUITheme.TextBody, 12);
            RefreshTransferMarketUI();
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
                    // Sell-tab clarification (backlog item 5, session 11) - not a bug,
                    // session 8 deliberately scoped selling to bench-only to protect
                    // against an accidental first-teamer sale, but nothing said so on
                    // screen, so a ~10-player bench-sized list read as suspiciously
                    // short/broken. Buy tab keeps its original plain budget line.
                    // Session 13 - budget already reflects escrowed bids (TryPlaceBid
                    // deducts immediately, see ManagerTransferNegotiation), so the plain
                    // £Xm figure is still the honest "what you can spend right now"
                    // number; the extra clauses just surface why it might look lower
                    // than expected and how close the two new caps are to being hit.
                    bylineTMP.text = transferMarketShowingBuyTab
                        ? $"Transfer budget: £{budget:F1}m   ·   {transferNegotiation.PendingBidCount}/{ManagerTransferNegotiation.MaxConcurrentBids} bids pending (£{transferNegotiation.GetTotalEscrowed():F1}m committed)   ·   {transferNegotiation.ActiveTransferScoutAssignmentCount}/{ManagerTransferNegotiation.MaxConcurrentTransferScouts} scouts assigned"
                        : $"Transfer budget: £{budget:F1}m   ·   Only bench players can be sold - your Starting XI is protected from an accidental sale.";
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

            bool showSearch = transferMarketShowingBuyTab;
            if (transferPlayerSearchInput != null) transferPlayerSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferClubSearchInput != null) transferClubSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferNationSearchInput != null) transferNationSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferMinAgeInput != null) transferMinAgeInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferMaxAgeInput != null) transferMaxAgeInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferPositionFilterButton != null) transferPositionFilterButton.gameObject.SetActive(showSearch);
            if (transferClearFiltersButton != null) transferClearFiltersButton.gameObject.SetActive(showSearch);

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
        private static readonly string[] TransferBuyColumnHeaders = { "PLAYER", "POS", "AGE", "CLUB/NATION", "OVR", "STATUS" };
        private static readonly float[] TransferBuyColumnFractions = { 0.24f, 0.09f, 0.07f, 0.20f, 0.09f, 0.31f };
        private static readonly string[] TransferSellColumnHeaders = { "PLAYER", "POS", "AGE", "OVR", "SELL FOR" };
        private static readonly float[] TransferSellColumnFractions = { 0.34f, 0.14f, 0.12f, 0.14f, 0.26f };

        private void RefreshTransferMarketBuyList(float budget)
        {
            List<PlayerAgent> players = new List<PlayerAgent>();
            transferMarketListView.AddCustomGridHeaderRow(TransferBuyColumnHeaders, TransferBuyColumnFractions, OnTransferBuyColumnHeaderClicked, transferBuySortColumn, transferBuySortDescending);

            if (!transferSearch.HasCriteria)
            {
                transferMarketListView.AddSectionHeader("SEARCH THE MARKET — choose at least one filter to discover players in this world");
                return;
            }

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

            // Generate every active club before evaluating nationality. Nationalities
            // are assigned lazily and consume Unity's random stream; filtering while
            // clubs were still being generated would let a UI search alter later clubs'
            // generated players in the same career.
            players.RemoveAll(player => !transferSearch.Matches(player,
                transferMarketRowClubs.TryGetValue(player, out string club) ? club : string.Empty));

            // Scouted youth prospects deliberately do NOT appear here anymore (session
            // 13 Youth rework) - Thomas's explicit call: the Missions/Youth page is
            // genuinely for youth now, every discovery has to be brought into the
            // Academy first regardless of age, never bid on directly. See
            // ManagerScouting/OnBringInScoutedPlayerClicked for where they actually go.

            if (transferBuySortColumn >= 0)
            {
                players.Sort((a, b) => CompareTransferBuyColumn(a, b, transferBuySortColumn, transferBuySortDescending));
            }

            if (players.Count == 0)
            {
                transferMarketListView.AddSectionHeader("NO PLAYERS MATCH — broaden the search or clear a filter");
                return;
            }

            transferMarketListView.AddSectionHeader($"SEARCH RESULTS ({players.Count})");

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
                    result = GetTransferStatusSortKey(a).CompareTo(GetTransferStatusSortKey(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        // Session 13 redesign - STATUS is no longer a single price, it's a state
        // (unscouted/scouting/ready-to-bid/pending/awaiting signature), so sorting on it
        // needs a tiered key rather than a plain price comparison: awaiting-signature
        // bids float to the top (the most actionable state), then pending bids, then
        // ready-to-bid targets (secondary-sorted by recommended price), then in-progress
        // scouting, then still-unscouted targets last.
        private float GetTransferStatusSortKey(PlayerAgent player)
        {
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(player);

            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature) return 4000f + pendingBid.BidAmount;
            if (pendingBid != null) return 3000f + pendingBid.BidAmount;

            bool scouted = transferNegotiation.IsTransferScouted(player);
            if (scouted)
            {
                AgentTeam sourceTeam = FindTeamContainingPlayer(player);
                return 2000f + ManagerTransferNegotiation.GetRecommendedBid(player, sourceTeam);
            }

            return transferNegotiation.IsTransferScoutAssigned(player) ? 1000f : 0f;
        }

        private void AddBuyRow(PlayerAgent player, string teamName, float budget, List<PlayerAgent> browseList)
        {
            bool scouted = transferNegotiation.IsTransferScouted(player);
            bool scoutAssigned = transferNegotiation.IsTransferScoutAssigned(player);
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(player);
            AgentTeam sourceTeam = FindTeamContainingPlayer(player);
            ManagerTransferNegotiation.TransferAvailability availability = ManagerTransferNegotiation.GetAvailability(player, sourceTeam);

            string ovrCell = scouted ? GetDisplayRating(player.GetOverallRating()).ToString() : ManagerTransferNegotiation.GetDisplayOverallBand(player);

            string statusCell;
            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature)
            {
                statusCell = $"<color=#3ddc84>ACCEPTED £{pendingBid.BidAmount:F1}m - CLICK TO SIGN</color>";
            }
            else if (pendingBid != null)
            {
                statusCell = $"<color=#e8c547>BID PENDING £{pendingBid.BidAmount:F1}m</color>";
            }
            else if (!scouted)
            {
                // Click-to-cancel (session 13 - Thomas: "I accidentally started
                // scouting a player I didn't want and couldn't undo it") - the row's
                // own click handler already branches on IsTransferScoutAssigned (see
                // OnBuyRowClicked), this is just the matching label.
                string availabilityLabel = FormatTransferAvailability(availability);
                statusCell = scoutAssigned
                    ? $"{availabilityLabel} · <color=#e8c547>SCOUTING... (click to cancel)</color>"
                    : $"{availabilityLabel} · SCOUT TO REVEAL";
            }
            else if (availability == ManagerTransferNegotiation.TransferAvailability.NotForSale)
            {
                statusCell = "<color=#e05a5a>NOT FOR SALE — NO POSITIONAL COVER</color>";
            }
            else
            {
                float recommended = ManagerTransferNegotiation.GetRecommendedBid(player, sourceTeam);
                string availabilityLabel = FormatTransferAvailability(availability);
                statusCell = recommended <= budget
                    ? $"{availabilityLabel} · ~£{recommended:F1}m · MAKE BID"
                    : $"{availabilityLabel} · ~£{recommended:F1}m · MAKE BID <color=#e05a5a>(over budget)</color>";
            }

            string[] cells =
            {
                player.Name,
                player.PrimaryPosition.ToString(),
                player.Age.ToString(),
                teamName,
                ovrCell,
                statusCell
            };

            // Session 13 redesign - name click no longer opens full detail for an
            // unscouted target (that would leak exact stats straight past the new
            // scouting gate, see the design notes above AddBuyRow's own comment
            // history); it falls back to the same row action instead.
            transferMarketListView.AddCustomGridRow(player, cells, TransferBuyColumnFractions, OnBuyRowClicked,
                onNameClicked: p => { if (scouted) OpenTransferTargetDetail(p, browseList); else OnBuyRowClicked(p); });
        }

        private static string FormatTransferAvailability(ManagerTransferNegotiation.TransferAvailability availability)
        {
            switch (availability)
            {
                case ManagerTransferNegotiation.TransferAvailability.Available: return "<color=#3ddc84>AVAILABLE</color>";
                case ManagerTransferNegotiation.TransferAvailability.KeyPlayer: return "<color=#e8c547>KEY PLAYER</color>";
                case ManagerTransferNegotiation.TransferAvailability.NotForSale: return "<color=#e05a5a>NOT FOR SALE</color>";
                default: return "NEGOTIABLE";
            }
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
                    GetDisplayRating(player.GetOverallRating()).ToString(),
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

        // Session 13 redesign - single click handler for whichever state a Buy row is
        // currently in, branching the same way the row's own STATUS cell does (see
        // AddBuyRow). Replaces the old instant-buy OnBuyRowClicked entirely - no state
        // in the new flow resolves in one click anymore.
        private void OnBuyRowClicked(PlayerAgent target)
        {
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(target);

            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature)
            {
                // Convenience path - the same Sign action the Inbox message offers,
                // just reachable straight from the row too. Walk Away deliberately
                // stays Inbox-only (see OnInboxWalkAwayClicked) so declining a done
                // deal is a deliberate visit to the message, not an accidental click.
                OnSignPlayerClicked(target);
                return;
            }

            if (pendingBid != null)
            {
                SetTransferMarketStatus($"Still waiting to hear back on {target.Name} - the response will arrive through Continue.");
                return;
            }

            bool scouted = transferNegotiation.IsTransferScouted(target);

            if (!scouted)
            {
                // Click-to-cancel (session 13 - Thomas: "I accidentally started
                // scouting a player I didn't want and couldn't undo it").
                if (transferNegotiation.IsTransferScoutAssigned(target))
                {
                    transferNegotiation.CancelTransferScout(target);
                    SetTransferMarketStatus($"Cancelled the scout assignment on {target.Name}.");
                }
                else if (transferNegotiation.TryAssignTransferScout(target, careerCalendar.CurrentDayNumber))
                {
                    SetTransferMarketStatus($"Scout assigned to {target.Name} - report due in {ManagerTransferNegotiation.TransferScoutDurationDays} days.");
                }
                else if (transferNegotiation.ActiveTransferScoutAssignmentCount >= ManagerTransferNegotiation.MaxConcurrentTransferScouts)
                {
                    SetTransferMarketStatus($"All {ManagerTransferNegotiation.MaxConcurrentTransferScouts} transfer scouts are already assigned - wait for a report to land first.");
                }

                RefreshTransferMarketUI();
                return;
            }

            string sourceTeamDisplay = transferMarketRowClubs.TryGetValue(target, out string t) ? t : "Unknown";
            AgentTeam sellingTeam = FindTeamContainingPlayer(target);
            if (ManagerTransferNegotiation.GetAvailability(target, sellingTeam) == ManagerTransferNegotiation.TransferAvailability.NotForSale)
            {
                SetTransferMarketStatus($"{sourceTeamDisplay} will not sell {target.Name} without positional cover.");
                return;
            }
            ShowBidDialog(target, sellingTeam, sourceTeamDisplay);
        }

        // --- Bid-amount dialog (session 13, free-text field session 16) - a numeric-only
        // TMP_InputField (ManagerUITheme.BuildInputField) rather than the original five
        // preset-multiplier picker - Thomas's explicit follow-up ask: "i'd like our bid
        // option to be a text field so you can enter your own bid... exclusively number
        // input, remove the five or so set options." Prefilled with the scout's
        // recommended amount so a manager who doesn't want to think about it can still
        // just hit Submit. ---

        private GameObject bidDialogPanel;
        private TMP_InputField bidAmountInputField;
        private PlayerAgent bidDialogTarget;
        private string bidDialogSourceTeam;

        private void ShowBidDialog(PlayerAgent target, AgentTeam sellingTeam, string sourceTeamDisplay)
        {
            if (bidDialogPanel != null)
            {
                Destroy(bidDialogPanel);
            }

            float recommended = ManagerTransferNegotiation.GetRecommendedBid(target, sellingTeam);
            bidDialogTarget = target;
            bidDialogSourceTeam = sourceTeamDisplay;

            Transform root = titlePanel.transform.parent;
            bidDialogPanel = new GameObject("BidDialogPanel", typeof(RectTransform), typeof(Image));
            bidDialogPanel.transform.SetParent(root, false);
            RectTransform panelRect = bidDialogPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            bidDialogPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            bidDialogPanel.transform.SetAsLastSibling();

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(bidDialogPanel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(760f, 300f);
            cardRect.anchoredPosition = Vector2.zero;
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(680f, 30f));
            ManagerUITheme.BuildLabel(titleObj.transform, $"MAKE A BID: {target.Name.ToUpperInvariant()}", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(subtitleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(680f, 24f));
            ManagerUITheme.BuildLabel(subtitleObj.transform, $"Market value ~£{ManagerClubFinance.GetMarketValue(target):F1}m   ·   scout's recommendation ~£{recommended:F1}m", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            GameObject inputLabelObj = new GameObject("InputLabel", typeof(RectTransform));
            inputLabelObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(inputLabelObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(680f, 22f));
            ManagerUITheme.BuildLabel(inputLabelObj.transform, "BID AMOUNT (£M)", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            GameObject inputContainer = new GameObject("InputContainer", typeof(RectTransform));
            inputContainer.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(inputContainer.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(300f, 44f));

            bidAmountInputField = ManagerUITheme.BuildInputField(inputContainer.transform, "e.g. 45.5", 20, characterLimit: 9);
            bidAmountInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            bidAmountInputField.text = recommended.ToString("F1");

            // Prefilling .text this early (same frame the field is built, before Unity's
            // own Awake/OnEnable-driven placeholder-hide logic has run) doesn't hide the
            // placeholder label the normal typing path would - without this the default
            // amount renders on top of "e.g. 45.5" instead of replacing it (session 16
            // playtest screenshot: "94.3" overlapping "45.5"). BuildInputField's other
            // caller (Save Name) never prefills text, so this is scoped to here rather
            // than the shared helper.
            if (bidAmountInputField.placeholder != null)
            {
                bidAmountInputField.placeholder.gameObject.SetActive(false);
            }

            Button confirmButton = ManagerUITheme.BuildButton(card.transform, "SUBMIT BID", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-100f, 36f), new Vector2(180f, 48f));
            confirmButton.onClick.AddListener(OnConfirmBidClicked);

            Button cancelButton = ManagerUITheme.BuildButton(card.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 15);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(100f, 36f), new Vector2(180f, 48f));
            cancelButton.onClick.AddListener(CloseBidDialog);

            StartCoroutine(RecoverBlankLabelsNextFrame(bidDialogPanel.transform));
        }

        private void OnConfirmBidClicked()
        {
            if (bidDialogTarget == null)
            {
                CloseBidDialog();
                return;
            }

            PlayerAgent target = bidDialogTarget;

            if (bidAmountInputField == null || !float.TryParse(bidAmountInputField.text, out float amount) || amount <= 0f)
            {
                SetTransferMarketStatus("Enter a bid amount above £0m.");
                return;
            }

            if (!careerCalendar.IsTransferWindowOpen)
            {
                SetTransferMarketStatus("The transfer window is closed.");
                return;
            }

            if (transferNegotiation.TryPlaceBid(target, amount, bidDialogSourceTeam, careerCalendar.CurrentDayNumber, finance, managedTeamName))
            {
                SetTransferMarketStatus($"Bid of £{amount:F1}m submitted for {target.Name} - response expected in {ManagerTransferNegotiation.BidResponseDays} days.");
            }
            else
            {
                SetTransferMarketStatus($"Couldn't submit that bid for {target.Name} - check your budget or your {ManagerTransferNegotiation.MaxConcurrentBids}-bid pending limit.");
            }

            CloseBidDialog();
            RefreshTransferMarketUI();
        }

        private void CloseBidDialog()
        {
            if (bidDialogPanel != null)
            {
                Destroy(bidDialogPanel);
                bidDialogPanel = null;
            }

            bidDialogTarget = null;
            bidAmountInputField = null;
        }

        // Finalizes an accepted bid - moving the player onto the managed squad mirrors
        // the old OnBuyRowClicked's "remove from whichever source they actually came
        // from" logic exactly, just triggered from a Sign action instead of happening
        // automatically the instant a bid was accepted (Thomas's explicit "confirm and
        // sign" flow). Reachable both from the Buy row (see OnBuyRowClicked) and from
        // the Inbox message itself (see OnInboxSignClicked).
        private void OnSignPlayerClicked(PlayerAgent target)
        {
            if (!careerCalendar.IsTransferWindowOpen)
            {
                SetTransferMarketStatus("The transfer window is closed - this deal cannot be completed today.");
                return;
            }

            if (!transferNegotiation.TrySign(target, finance, managedTeamName, out ManagerTransferNegotiation.PendingBid resolvedBid))
            {
                return;
            }

            // Every Transfer Market bid target is a regular AI-squad player now (session
            // 13 Youth rework routes every scouted prospect through the Academy
            // instead, never through here) - remove from their real club's squad.
            if (resolvedBid.SourceTeamName != null && squadsByTeamName.TryGetValue(resolvedBid.SourceTeamName, out AgentTeam sourceSquad))
            {
                // Session 16 - Thomas: "starting players sold automatically get replaced
                // with suitable bench players." Same SubstitutePlayer swap the managed
                // team's own injury/loan backfill already uses (see
                // EnsureNoInjuredStarters/OnLoanOutClicked) - promotes the best-fit bench
                // cover into the exact formation slot the sold player vacated instead of
                // leaving a hole in the AI club's XI. ManagerTransferNegotiation.
                // WouldLeaveSquadTooThin already guarantees a same-position player exists
                // somewhere in the squad before a sale is ever accepted, so this should
                // always find real cover in practice - still defensively falls through to
                // a plain removal if it somehow doesn't.
                if (sourceSquad.StartingEleven.Contains(target))
                {
                    PlayerAgent replacement = FindBestFitBenchPlayer(sourceSquad, target.PrimaryPosition);

                    if (replacement != null)
                    {
                        sourceSquad.SubstitutePlayer(target, replacement);
                    }
                    else
                    {
                        sourceSquad.StartingEleven.Remove(target);
                    }
                }

                sourceSquad.RemovePlayer(target);
            }

            GetOrCreateAgentTeam(managedTeamName).AddSquadPlayer(target);

            MarkInboxMessagesResolvedForPlayer(target);
            SetTransferMarketStatus($"Signed {target.Name} for £{resolvedBid.BidAmount:F1}m!");
            RefreshTransferMarketUI();
            if (inboxContentContainer != null) RefreshInboxUI();
        }

        private void OnWalkAwayClicked(PlayerAgent target)
        {
            if (!transferNegotiation.TryWalkAway(target, finance, managedTeamName))
            {
                return;
            }

            MarkInboxMessagesResolvedForPlayer(target);
            SetTransferMarketStatus($"Walked away from the {target.Name} deal - your money's back in the budget.");
            RefreshTransferMarketUI();
            if (inboxContentContainer != null) RefreshInboxUI();
        }

        // Both Sign and Walk Away leave the triggering message (whichever screen it was
        // clicked from) without a live action to perform anymore - clears the pending-
        // action flag on every message still pointing at this player so ResolveAction's
        // save/load-safety guarantee holds (see ManagerInbox.BuildSaveList) and the
        // message reads as a closed, historical record instead of a dead button.
        private void MarkInboxMessagesResolvedForPlayer(PlayerAgent player)
        {
            foreach (InboxMessage message in inbox.Messages)
            {
                if (message.ActionPlayer == player)
                {
                    inbox.ResolveAction(message);
                }
            }
        }

        private void OnSellRowClicked(PlayerAgent target)
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team)
                || team.StartingEleven.Contains(target)
                || !team.Players.Contains(target))
            {
                return;
            }

            float sellPrice = ManagerClubFinance.GetSellPrice(target);

            team.RemovePlayer(target);
            finance.AdjustBudget(managedTeamName, sellPrice);
            finance.RecordTransferIncome(managedTeamName, sellPrice);

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

        // --- Inbox (session 13) - phase 3 of the manager influence arc, the last
        // unclaimed item from the original session 7 plan (captaincy/fitness/morale all
        // shipped already, see project_manager_influence_arc in memory). Same code-
        // built-panel/chrome-guard/scroll-content pattern as Trophy Room/Career, just
        // simpler (no tabs) - a flat newest-first message list. Transfer bid results are
        // the first real message type, but the shape is deliberately generic (see
        // ManagerInbox) for whatever gets added here later. ---

        private bool inboxChromeBuilt;
        private GameObject inboxPanel;
        private RectTransform inboxContentContainer;
        private readonly List<GameObject> spawnedInboxRows = new();

        public void OnOpenInboxClicked()
        {
            if (!inboxChromeBuilt)
            {
                BuildInboxChrome();
                inboxChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (inboxPanel != null) inboxPanel.SetActive(true);

            RefreshInboxUI();
        }

        public void OnInboxBackClicked()
        {
            if (inboxPanel != null) inboxPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void BuildInboxChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            inboxPanel = new GameObject("InboxPanel", typeof(RectTransform));
            inboxPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = inboxPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(inboxPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(inboxPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "INBOX", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnInboxBackClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("InboxScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(inboxPanel.transform, false);
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
            inboxContentContainer = contentObj.GetComponent<RectTransform>();
            inboxContentContainer.anchorMin = new Vector2(0f, 1f);
            inboxContentContainer.anchorMax = new Vector2(1f, 1f);
            inboxContentContainer.pivot = new Vector2(0.5f, 1f);
            inboxContentContainer.anchoredPosition = Vector2.zero;
            inboxContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 10f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = inboxContentContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;

            StartCoroutine(RecoverBlankLabelsNextFrame(inboxPanel.transform));
        }

        private void RefreshInboxUI()
        {
            if (inboxContentContainer == null)
            {
                return;
            }

            foreach (GameObject row in spawnedInboxRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedInboxRows.Clear();

            if (inbox.Messages.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(inboxContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "Nothing here yet - scouting reports and transfer bid responses will land here.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedInboxRows.Add(emptyObj);
            }
            else
            {
                // Newest first.
                for (int i = inbox.Messages.Count - 1; i >= 0; i--)
                {
                    spawnedInboxRows.Add(BuildInboxMessageRow(inbox.Messages[i]));
                }
            }

            if (inboxButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(inboxButton, "INBOX", ManagerUITheme.TextBody, 17);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(inboxContentContainer));
        }

        // Session 13 - collapsed banner (headline + matchday only) by default, click to
        // expand and reveal the body (and Sign/Walk Away for an actionable message).
        // Requested ahead of the longer Youth scouting-report text about to start
        // landing here - a wall of always-expanded multi-line messages would make the
        // list unscannable. Banner itself is a full-row Button toggling IsExpanded; the
        // Sign/Walk Away buttons sit on top as later siblings so their own clicks
        // resolve to them instead of bubbling down to the row's own toggle (same
        // "topmost raycast target wins" convention BuildClickableNameCell already
        // relies on elsewhere).
        // Session 15 - Thomas: readability pass. Content text (title/body/matchday)
        // dropped to Normal weight regardless of read state - Bold was reserved for
        // emphasis, not baseline body copy, and made every unread row harder to read,
        // not easier (the "NEW" tag + row background tint already carry the unread
        // signal on their own). Every dimension bumped up a full tier too ("you might
        // have 20/20 vision, good sir, but I don't") - banner height, title/matchday/
        // body font sizes, and the expanded body's reserved height all scaled together
        // so nothing clips or crowds the larger text.
        private const float InboxBannerHeight = 80f;

        private GameObject BuildInboxMessageRow(InboxMessage message)
        {
            bool actionable = message.HasPendingAction;
            float bodyHeight = actionable ? 180f : 110f;
            float height = message.IsExpanded ? InboxBannerHeight + bodyHeight : InboxBannerHeight;

            GameObject row = new GameObject($"Message_{message.Id}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(inboxContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            Image rowImage = row.GetComponent<Image>();
            rowImage.color = message.IsRead
                ? ManagerUITheme.CardNeutralAlt
                : new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.10f);

            Button rowButton = row.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;
            rowButton.onClick.AddListener(() => OnInboxMessageBannerClicked(message));

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(row.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(20f, -20f);
            titleRect.sizeDelta = new Vector2(-320f, 36f);
            // Plain ASCII, not a unicode bullet (Oswald SDF has no glyph for "●" - the
            // same reason the Tactics Board formation dropdown uses a plain "v" instead
            // of a unicode arrow; confirmed live, see feedback_random_namespace_ambiguity-
            // adjacent font gotcha in HANDOFF).
            string unreadMarker = message.IsRead ? "" : "<color=#3ddc84>NEW</color> ";
            string expandMarker = message.IsExpanded ? "v " : "> ";
            ManagerUITheme.BuildLabel(titleObj.transform, $"{expandMarker}{unreadMarker}{message.Title}", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);

            GameObject matchdayObj = new GameObject("Matchday", typeof(RectTransform));
            matchdayObj.transform.SetParent(row.transform, false);
            RectTransform matchdayRect = matchdayObj.GetComponent<RectTransform>();
            matchdayRect.anchorMin = new Vector2(1f, 1f);
            matchdayRect.anchorMax = new Vector2(1f, 1f);
            matchdayRect.pivot = new Vector2(1f, 1f);
            matchdayRect.anchoredPosition = new Vector2(-20f, -22f);
            matchdayRect.sizeDelta = new Vector2(200f, 32f);
            ManagerUITheme.BuildLabel(matchdayObj.transform, ManagerCareerCalendar.DisplayDateForDay(message.MatchdayReceived), 18, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal);

            if (!message.IsExpanded)
            {
                return row;
            }

            GameObject bodyObj = new GameObject("Body", typeof(RectTransform));
            bodyObj.transform.SetParent(row.transform, false);
            RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.anchoredPosition = new Vector2(20f, -(InboxBannerHeight + 6f));
            bodyRect.sizeDelta = new Vector2(-40f, actionable ? bodyHeight - 54f : bodyHeight - 16f);
            ManagerUITheme.BuildLabel(bodyObj.transform, message.Body, 20, ManagerUITheme.TextBody, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);

            if (actionable)
            {
                PlayerAgent actionPlayer = message.ActionPlayer;

                Button signButton = ManagerUITheme.BuildButton(row.transform, "SIGN PLAYER", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
                ManagerUITheme.SetPointAnchor(signButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-16f, 24f), new Vector2(160f, 40f));
                signButton.onClick.AddListener(() => OnSignPlayerClicked(actionPlayer));

                Button walkAwayButton = ManagerUITheme.BuildButton(row.transform, "WALK AWAY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(walkAwayButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-184f, 24f), new Vector2(160f, 40f));
                walkAwayButton.onClick.AddListener(() => OnWalkAwayClicked(actionPlayer));
            }

            return row;
        }

        // Session 15 fix - Thomas: "as soon as you click the first one, they all turn
        // grey, despite the other ones still technically being unread." Root cause:
        // the old design marked EVERY message read the instant the Inbox screen opened
        // (see the removed comment on RefreshInboxUI), which only reads correctly if
        // you never look at the screen twice in one visit - the first expand click
        // re-ran that same screen-wide refresh and repainted every row from state that
        // had already flipped to all-read the moment the screen opened. Read status is
        // now genuinely per-message: expanding a specific message is what marks THAT
        // one read (collapsing doesn't un-read it), so an unopened message stays green
        // until you actually look at it, no matter what else you click in the meantime.
        private void OnInboxMessageBannerClicked(InboxMessage message)
        {
            message.IsExpanded = !message.IsExpanded;

            if (message.IsExpanded)
            {
                inbox.MarkRead(message);
            }

            RefreshInboxUI();
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

        // Career screen tabs (backlog item 2, session 11): 0 = Trophies (the original
        // Trophy Room content, unchanged), 1 = Record (season-by-season W/D/L/Points),
        // 2 = Finance (lifetime transfer spend/income + prize money/board boost totals).
        private int careerTab;
        private Button careerTrophiesTabButton;
        private Button careerRecordTabButton;
        private Button careerFinanceTabButton;

        public void OnOpenTrophyRoomClicked()
        {
            if (!trophyRoomChromeBuilt)
            {
                BuildTrophyRoomChrome();
                trophyRoomChromeBuilt = true;
            }

            careerTab = 0;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(true);

            RefreshTrophyRoomUI();
        }

        public void OnTrophyRoomBackClicked()
        {
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void OnCareerTrophiesTabClicked()
        {
            careerTab = 0;
            RefreshTrophyRoomUI();
        }

        private void OnCareerRecordTabClicked()
        {
            careerTab = 1;
            RefreshTrophyRoomUI();
        }

        private void OnCareerFinanceTabClicked()
        {
            careerTab = 2;
            RefreshTrophyRoomUI();
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
            ManagerUITheme.BuildLabel(titleObj.transform, "CAREER", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnTrophyRoomBackClicked);

            // Three tabs (same BUY/SELL-style pattern as Transfer Market) sharing the one
            // scroll content container below rather than three separate ScrollRects -
            // RefreshTrophyRoomUI branches on careerTab to decide what rows go into it.
            careerFinanceTabButton = ManagerUITheme.BuildButton(header.transform, "FINANCE", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(careerFinanceTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            careerFinanceTabButton.onClick.AddListener(OnCareerFinanceTabClicked);

            careerRecordTabButton = ManagerUITheme.BuildButton(header.transform, "RECORD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(careerRecordTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-406f, -27f), new Vector2(120f, 36f));
            careerRecordTabButton.onClick.AddListener(OnCareerRecordTabClicked);

            careerTrophiesTabButton = ManagerUITheme.BuildButton(header.transform, "TROPHIES", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(careerTrophiesTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-536f, -27f), new Vector2(120f, 36f));
            careerTrophiesTabButton.onClick.AddListener(OnCareerTrophiesTabClicked);

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
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = 25f;

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomPanel.transform));
        }

        private void RefreshTrophyRoomUI()
        {
            if (trophyRoomContentContainer == null)
            {
                return;
            }

            if (careerTrophiesTabButton != null && careerTrophiesTabButton.TryGetComponent(out Image trophiesImage))
            {
                trophiesImage.color = careerTab == 0 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerTrophiesTabButton, "TROPHIES", careerTab == 0 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (careerRecordTabButton != null && careerRecordTabButton.TryGetComponent(out Image recordImage))
            {
                recordImage.color = careerTab == 1 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerRecordTabButton, "RECORD", careerTab == 1 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (careerFinanceTabButton != null && careerFinanceTabButton.TryGetComponent(out Image financeImage))
            {
                financeImage.color = careerTab == 2 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerFinanceTabButton, "FINANCE", careerTab == 2 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            foreach (GameObject row in spawnedTrophyRoomRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedTrophyRoomRows.Clear();

            if (careerTab == 0)
            {
                RefreshCareerTrophiesTab();
            }
            else if (careerTab == 1)
            {
                RefreshCareerRecordTab();
            }
            else
            {
                RefreshCareerFinanceTab();
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomContentContainer));
        }

        private void RefreshCareerTrophiesTab()
        {
            spawnedTrophyRoomRows.Add(BuildTrophyRoomHeaderRow());

            if (careerHistory.Records.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(trophyRoomContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "No seasons completed yet - finish your first season to start the history.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedTrophyRoomRows.Add(emptyObj);
                return;
            }

            // Most recent season first.
            for (int i = careerHistory.Records.Count - 1; i >= 0; i--)
            {
                spawnedTrophyRoomRows.Add(BuildTrophyRoomRow(careerHistory.Records[i]));
            }
        }

        private static readonly float[] CareerRecordColumnFractions = { 0.16f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f };

        private void RefreshCareerRecordTab()
        {
            spawnedTrophyRoomRows.Add(BuildCareerRecordHeaderRow());

            // Live in-progress row (backlog item, session 12, Thomas: Record should show
            // the current season live, not just completed ones). SeasonRecord/
            // careerHistory only ever gets a row once ApplySeasonEndRewards runs at
            // rollover - mid-season there was nothing here for the season actually being
            // played. Sourced straight from playableTable, the same live table the Hub's
            // own league position already reads from - no new tracking needed.
            GameObject liveRow = BuildLiveCareerRecordRow();
            if (liveRow != null)
            {
                spawnedTrophyRoomRows.Add(liveRow);
            }

            if (careerHistory.Records.Count == 0)
            {
                if (liveRow == null)
                {
                    GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                    emptyObj.transform.SetParent(trophyRoomContentContainer, false);
                    emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                    ManagerUITheme.BuildLabel(emptyObj.transform, "No seasons completed yet - finish your first season to start the history.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                    spawnedTrophyRoomRows.Add(emptyObj);
                }

                return;
            }

            for (int i = careerHistory.Records.Count - 1; i >= 0; i--)
            {
                spawnedTrophyRoomRows.Add(BuildCareerRecordRow(careerHistory.Records[i]));
            }
        }

        // Null if there's genuinely no live table yet (e.g. before a career's first
        // EnsureTeam call) - defensive, shouldn't happen in practice by the time this
        // screen is reachable at all.
        private GameObject BuildLiveCareerRecordRow()
        {
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            List<LeagueTable.Entry> sorted = playableTable.Sorted();
            int position = sorted.FindIndex(e => e.TeamId == managedTeamId) + 1;

            if (position <= 0)
            {
                return null;
            }

            LeagueTable.Entry live = sorted[position - 1];

            GameObject row = new GameObject($"RecordSeason_{currentSeason}_Live", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.08f);

            int goalDifference = live.GoalsFor - live.GoalsAgainst;
            string[] values =
            {
                $"Season {currentSeason} (live)",
                $"{position}{GetOrdinalSuffix(position)}",
                live.Points.ToString(),
                live.Wins.ToString(),
                live.Draws.ToString(),
                live.Losses.ToString(),
                (goalDifference > 0 ? "+" : "") + goalDifference
            };

            float x = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildCareerRecordHeaderRow()
        {
            GameObject row = new GameObject("RecordHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 30f;

            string[] headers = { "SEASON", "POSITION", "PTS", "W", "D", "L", "GD" };
            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                GameObject cell = new GameObject($"Header_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, headers[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildCareerRecordRow(SeasonRecord record)
        {
            GameObject row = new GameObject($"RecordSeason_{record.Season}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = record.IsChampion ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            // Goal difference isn't stored on SeasonRecord itself (GF/GA weren't part of
            // the original ask) - Points/W/D/L already carry the shape Thomas actually
            // asked for, so a GD column not being derivable exactly isn't worth widening
            // SeasonRecord for. Shown as "-" rather than a wrong number.
            string[] values =
            {
                $"Season {record.Season}",
                $"{record.FinalPosition}{GetOrdinalSuffix(record.FinalPosition)}",
                record.Points.ToString(),
                record.Wins.ToString(),
                record.Draws.ToString(),
                record.Losses.ToString(),
                "-"
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
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, textColor, TextAlignmentOptions.MidlineLeft, style);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private void RefreshCareerFinanceTab()
        {
            float totalSpend = finance.GetTotalTransferSpend(managedTeamName);
            float totalIncome = finance.GetTotalTransferIncome(managedTeamName);

            float totalPrizeMoney = 0f;
            float totalBoardBoost = 0f;
            foreach (SeasonRecord record in careerHistory.Records)
            {
                totalPrizeMoney += record.PrizeMoney;
                totalBoardBoost += record.BoardBoost;
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            float currentBudget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

            (string label, string value, bool emphasize)[] rows =
            {
                ("CURRENT BUDGET", $"£{currentBudget:F1}m", true),
                ("TOTAL TRANSFER SPEND", $"£{totalSpend:F1}m", false),
                ("TOTAL TRANSFER INCOME", $"£{totalIncome:F1}m", false),
                ("NET TRANSFER SPEND", $"£{(totalSpend - totalIncome):F1}m", false),
                ("TOTAL PRIZE MONEY", $"£{totalPrizeMoney:F1}m", false),
                ("TOTAL BOARD BOOST", $"£{totalBoardBoost:F1}m", false),
            };

            foreach (var (label, value, emphasize) in rows)
            {
                spawnedTrophyRoomRows.Add(BuildCareerFinanceRow(label, value, emphasize));
            }
        }

        private GameObject BuildCareerFinanceRow(string label, string value, bool emphasize)
        {
            GameObject row = new GameObject($"Finance_{label}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 52f;
            row.GetComponent<Image>().color = emphasize ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            GameObject labelCell = new GameObject("Label", typeof(RectTransform));
            labelCell.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelCell.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = new Vector2(20f, 0f);
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelCell.transform, label, 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject valueCell = new GameObject("Value", typeof(RectTransform));
            valueCell.transform.SetParent(row.transform, false);
            RectTransform valueRect = valueCell.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.6f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = new Vector2(-20f, 0f);
            ManagerUITheme.BuildLabel(valueCell.transform, value, 20, emphasize ? ManagerUITheme.Accent : ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            return row;
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
            if (tacticsBoardOpenedMidMatch && !TryCommitMidMatchTacticsDraft())
            {
                return;
            }

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

            // Auto-pick best XI (backlog item, session 12) - fills every pin with the
            // highest position-fit player available for that slot, from the whole squad
            // (Starting XI + Bench combined), skipping injured/already-subbed-off players.
            // Built regardless of what AI clubs do (Thomas's call): it doesn't raise the
            // team's strength ceiling, a manager could always assemble this same XI by
            // hand - this just automates the clicking.
            autoPickButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "AUTO-PICK XI", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(autoPickButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-764f, -27f), new Vector2(170f, 36f));
            autoPickButton.onClick.AddListener(OnAutoPickBestXIClicked);
            autoPickButton.onClick.AddListener(ManagerAudio.PlayClick);

            // Per-match tactical override toggle (new feature suggestion, session 14) -
            // see the field-level comment above for the full design. Pre-match only
            // (hidden mid-match, same as Auto-Pick - see RefreshTacticsBoardUI), since
            // "revert after this match" has no meaning once a match is already live.
            nextMatchOnlyButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "NEXT MATCH ONLY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(nextMatchOnlyButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-954f, -27f), new Vector2(180f, 36f));
            nextMatchOnlyButton.onClick.AddListener(OnNextMatchOnlyToggleClicked);
            nextMatchOnlyButton.onClick.AddListener(ManagerAudio.PlayClick);

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
            // The gap between the header's own bottom accent line and the pitch's real top
            // edge turned out too tight to fit this label in at all (~25 world-units for a
            // ~22-unit-tall label, confirmed via live GetWorldCorners measurement - two
            // earlier attempts to thread that gap both failed live). Placed in the header's
            // own background instead, below the button row (bottom edge at local y=-45,
            // all buttons are anchoredPosition.y=-27 with height 36) and above the accent
            // line at the header's bottom edge (local y=-90) - a genuinely empty ~45-unit
            // band with real margin on both sides, confirmed live.
            warningRect.anchoredPosition = new Vector2(0f, -headerHeight + 34f);
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

            if (tacticsScreenBackButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(tacticsScreenBackButton, "BACK TO TACTICS BOARD", ManagerUITheme.TextBody, 13);
            }

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
            tacticsScreenBackButton = backButton;

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
            // Starting XI only, deliberately excluding Bench (backlog item, session 12,
            // Thomas's call) - scrolling past the Starting XI into Bench with no visual
            // separator risked accidentally assigning e.g. Captain to a player not even
            // playing. A bench player realistically shouldn't hold any of these roles
            // anyway. A role already pointing at a bench player from before this change
            // still displays correctly (BuildRoleDropdownRow shows currentValue.Name
            // regardless of the options list), it just won't be re-selectable here.
            List<PlayerAgent> squadPlayers = new List<PlayerAgent>(team.StartingEleven);

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

            sliderTop = BuildSliderRow(leftColumn.transform, "TEMPO", sliderTop,
                new[] { "SLOW", "BALANCED", "FAST" }, (int)tacticalSliders.Tempo,
                index => { tacticalSliders.Tempo = (TempoSetting)index; RefreshTacticsScreenUI(); });

            GameObject fitSummary = new GameObject("TacticalFitSummary", typeof(RectTransform), typeof(Image));
            fitSummary.transform.SetParent(leftColumn.transform, false);
            RectTransform fitRect = fitSummary.GetComponent<RectTransform>();
            fitRect.anchorMin = new Vector2(0f, 1f);
            fitRect.anchorMax = new Vector2(1f, 1f);
            fitRect.pivot = new Vector2(0.5f, 1f);
            fitRect.offsetMin = new Vector2(0f, -(sliderTop + 92f));
            fitRect.offsetMax = new Vector2(0f, -sliderTop);
            fitSummary.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            ManagerUITheme.BuildLabel(fitSummary.transform, BuildTacticalFitSummary(team), 14,
                ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold);

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

        // Moneyball-style tactical value: the same attributes used by the match engine's
        // chance pathways are summarized here, so a specialist can be valuable to this
        // system even when his headline Overall is unremarkable.
        private string BuildTacticalFitSummary(AgentTeam team)
        {
            if (team == null || team.StartingEleven.Count == 0) return "TACTICAL FIT —";

            float approach = tacticalSliders.Width == WidthSetting.Wide
                ? Average(team.StartingEleven, p => p.Crossing * 0.30f + p.Pace * 0.18f + p.Stamina * 0.14f + p.OffTheBall * 0.12f + p.Heading * 0.12f + p.JumpingReach * 0.14f)
                : tacticalSliders.Width == WidthSetting.Narrow
                    ? Average(team.StartingEleven, p => p.FirstTouch * 0.20f + p.Passing * 0.20f + p.Technique * 0.18f + p.Vision * 0.16f + p.Decisions * 0.16f + p.Agility * 0.10f)
                    : Average(team.StartingEleven, p => p.Passing * 0.18f + p.FirstTouch * 0.14f + p.Decisions * 0.14f + p.WorkRate * 0.12f + p.Pace * 0.12f + p.Technique * 0.12f + p.DefensivePositioning * 0.18f);

            float tempo = tacticalSliders.Tempo == TempoSetting.Fast
                ? Average(team.StartingEleven, p => p.Acceleration * 0.24f + p.Pace * 0.18f + p.Stamina * 0.18f + p.WorkRate * 0.16f + p.Decisions * 0.14f + p.OffTheBall * 0.10f)
                : tacticalSliders.Tempo == TempoSetting.Slow
                    ? Average(team.StartingEleven, p => p.FirstTouch * 0.22f + p.Passing * 0.20f + p.Technique * 0.18f + p.Decisions * 0.18f + p.Composure * 0.14f + p.Vision * 0.08f)
                    : Average(team.StartingEleven, p => p.Decisions * 0.20f + p.Composure * 0.18f + p.Passing * 0.16f + p.Stamina * 0.16f + p.FirstTouch * 0.15f + p.WorkRate * 0.15f);

            float defence = tacticalSliders.DefensiveDepth == DefensiveDepthSetting.High
                ? Average(team.StartingEleven, p => p.Acceleration * 0.20f + p.Pace * 0.18f + p.Anticipation * 0.20f + p.WorkRate * 0.16f + p.Stamina * 0.14f + p.DefensivePositioning * 0.12f)
                : tacticalSliders.DefensiveDepth == DefensiveDepthSetting.Deep
                    ? Average(team.StartingEleven, p => p.DefensivePositioning * 0.24f + p.Marking * 0.18f + p.JumpingReach * 0.16f + p.Strength * 0.14f + p.Anticipation * 0.16f + p.Heading * 0.12f)
                    : Average(team.StartingEleven, p => p.DefensivePositioning * 0.22f + p.Anticipation * 0.18f + p.Tackling * 0.16f + p.Decisions * 0.14f + p.Pace * 0.12f + p.Strength * 0.10f + p.WorkRate * 0.08f);

            float fit = approach * 0.40f + tempo * 0.30f + defence * 0.30f;
            return $"TACTICAL FIT  {fit:F0}/99\nAPPROACH {approach:F0}   ·   TEMPO {tempo:F0}   ·   DEFENCE {defence:F0}";
        }

        private static float Average(List<PlayerAgent> players, Func<PlayerAgent, float> selector)
        {
            float total = 0f;
            foreach (PlayerAgent player in players) total += selector(player);
            return players.Count > 0 ? total / players.Count : 0f;
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

            // No "v"/"— None —" (Thomas's call, session 12) - just the assigned name, or
            // blank until one's picked. BuildLabel stretches its label full-size with
            // zero padding, so the text touched the button's left edge directly - given
            // its own padded RectTransform here instead (same 10px inset BuildGridCell
            // uses elsewhere) rather than accepting that default.
            string currentLabel = currentValue != null ? currentValue.Name : "";
            TextMeshProUGUI dropdownLabel = ManagerUITheme.BuildLabel(dropdownButtonObj.transform, currentLabel, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            RectTransform dropdownLabelRect = dropdownLabel.GetComponent<RectTransform>();
            dropdownLabelRect.offsetMin = new Vector2(10f, 0f);
            dropdownLabelRect.offsetMax = new Vector2(-10f, 0f);

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
            List<PlayerAgent> pool = tacticsBoardOpenedMidMatch
                ? team.StartingEleven.Concat(team.Bench).ToList()
                : new List<PlayerAgent>(team.Players);
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

            // Playtest backlog (session 14, Thomas: "Auto-Pick shouldn't be offered
            // inside the mid-match Make Changes squad screen") - the button itself is
            // shared chrome (built once, this same panel is reused for both the pre-
            // match Tactics Board and the mid-match Make Changes flow), so it's toggled
            // here on every refresh rather than at build time.
            if (autoPickButton != null) autoPickButton.gameObject.SetActive(!tacticsBoardOpenedMidMatch);

            if (nextMatchOnlyButton != null)
            {
                nextMatchOnlyButton.gameObject.SetActive(!tacticsBoardOpenedMidMatch);
                ManagerUITheme.NormalizeButtonLabel(nextMatchOnlyButton, nextMatchOnlyOverrideActive ? "NEXT MATCH ONLY: ON" : "NEXT MATCH ONLY", ManagerUITheme.TextBody, 13);
                HighlightSelectedMentalityButton(nextMatchOnlyButton, nextMatchOnlyOverrideActive);
            }

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

            // Playtest backlog (session 14) - Thomas: separate genuine reserves from an
            // "unavailable" (injured) group, so an injured player reads as clearly
            // off-limits rather than just blocked-on-drag (see OnBenchPlayerDroppedOnPin's
            // injury check, which already refuses the drop - this makes the refusal
            // visible before the manager even tries). Available players keep the plain
            // bench list exactly as before; injured players are pushed below a caption
            // divider, still built by the same BuildTacticsBoardBenchCard (own injury
            // cross badge included) so they're still visible and still draggable-away-
            // from (a manager might want to shuffle who's next in line), just not mixed
            // in with who's actually pickable.
            ManagerSquadRoles benchRoles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerAgent> availableBench = new List<PlayerAgent>();
            List<PlayerAgent> unavailableBench = new List<PlayerAgent>();
            foreach (PlayerAgent player in team.Bench)
            {
                if (benchRoles.IsInjured(player, careerCalendar.CurrentDayNumber)) unavailableBench.Add(player);
                else availableBench.Add(player);
            }

            foreach (PlayerAgent player in availableBench)
            {
                BuildTacticsBoardBenchCard(player);
            }

            if (unavailableBench.Count > 0)
            {
                BuildTacticsBoardBenchSectionCaption($"UNAVAILABLE ({unavailableBench.Count})");
                foreach (PlayerAgent player in unavailableBench)
                {
                    BuildTacticsBoardBenchCard(player);
                }
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
            // Playtest backlog (session 14) - Thomas's own idea: "the pin's green border
            // smoothly shifts warmer (green->yellow->red) as Condition drops, not a
            // separate number." Previously this only reflected live in-match fatigue
            // (GetFatigueMultiplier) and was hardcoded flat green outside a live match -
            // the season-long Condition system (ManagerSquadRoles, session 7/13
            // rebalance) had no visual presence on the board at all pre-match. Now blends
            // both signals: the persistent Condition is always the base (so a fatigued
            // squad reads as such before a ball's even kicked), and during a live match
            // the worse of the two (persistent vs in-match fatigue) wins, so the border
            // still visibly warms up as a match wears on, same as before.
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            float seasonCondition = roles.GetCondition(player);
            float condition = isMatchCurrentlyLive
                ? Mathf.Min(seasonCondition, matchSimulator.GetFatigueMultiplier(player, currentMatchMinute) * 100f)
                : seasonCondition;
            Color conditionColor = ManagerUITheme.ConditionGradientColor(condition);

            // Injury cross (session 9) - the Tactics screen previously had zero injury
            // awareness at all (see feedback in HANDOFF), so a manager could plan a
            // lineup around a player who's silently benched at kickoff. Doesn't block
            // selection yet, just makes it visible where the lineup is actually built.
            bool isInjured = roles.IsInjured(player, careerCalendar.CurrentDayNumber);

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
            card.Configure(player, isDraggable: true, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin, OnPinPlayersSwapped, isPinCard: true);
        }

        // Playtest backlog (session 14) - divider between the available bench and the
        // unavailable (injured) group below it. Same plain LayoutElement-label approach
        // every other inline scroll-list caption in this file uses (e.g. Inbox's empty-
        // state row) - not a full card, just a fixed-height text row the VerticalLayoutGroup
        // slots in like any other child.
        private void BuildTacticsBoardBenchSectionCaption(string text)
        {
            GameObject captionObj = new GameObject("BenchSectionCaption", typeof(RectTransform), typeof(LayoutElement));
            captionObj.transform.SetParent(tacticsBoardBenchContent, false);
            captionObj.GetComponent<LayoutElement>().preferredHeight = 24f;
            ManagerUITheme.BuildLabel(captionObj.transform, text, 13, ManagerUITheme.Danger, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
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
            // Extra left inset (was 18f) makes room for the injury cross gutter below,
            // same fixed-gutter approach SquadListView.BuildInjuryCrossIcon's own caller
            // already uses - reserved whether or not this particular card is injured, so
            // the name doesn't visibly shift card-to-card.
            nameRect.offsetMin = new Vector2(40f, 0f);
            nameRect.offsetMax = new Vector2(-18f, -2f);
            ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 17, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Injury cross badge (playtest backlog, session 14) - Thomas: prevents
            // accidentally dragging an injured player onto the pitch. The pin and Squad
            // list already show this (session 9); the Tactics Board's own bench card was
            // the one place left without it, which mattered most for exactly the drag
            // gesture this icon is meant to warn against.
            bool benchCardIsInjured = GetOrCreateSquadRoles(managedTeamName).IsInjured(player, careerCalendar.CurrentDayNumber);
            GameObject benchInjuryIcon = ManagerUITheme.BuildInjuryCrossIcon(cardObj.transform, 16f);
            RectTransform benchInjuryIconRect = benchInjuryIcon.GetComponent<RectTransform>();
            benchInjuryIconRect.anchorMin = new Vector2(0f, 0.5f);
            benchInjuryIconRect.anchorMax = new Vector2(0f, 0.5f);
            benchInjuryIconRect.pivot = new Vector2(0f, 0.5f);
            benchInjuryIconRect.anchoredPosition = new Vector2(14f, 0f);
            benchInjuryIcon.SetActive(benchCardIsInjured);

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
            // isDropTarget: true now (was false), and OnBenchPlayerDroppedOnPin wired
            // (was null) - playtest backlog (session 14): dragging a starter pin onto a
            // bench card now substitutes them, same as the existing bench-onto-pin
            // direction (see TacticsBoardPlayerCard.OnDrop's isPinCard branch).
            card.Configure(player, isDraggable: true, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin, isPinCard: false);
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
            if (blockRoles.IsInjured(benchPlayer, careerCalendar.CurrentDayNumber))
            {
                ShowTacticsBoardWarning($"{benchPlayer.Name} is injured and can't start");
                RefreshTacticsBoardUI();
                return;
            }

            // Session 10 exploit fix: once a player has genuinely been subbed off this
            // match, real football doesn't let them come back on. Only checked mid-match
            // (see playersSubbedOffThisMatch's own comment) - pre-match team-sheet edits
            // are free to rearrange the XI as many times as the manager likes.
            if (tacticsBoardOpenedMidMatch && playersSubbedOffThisMatch.Contains(benchPlayer))
            {
                ShowTacticsBoardWarning($"{benchPlayer.Name} has already been substituted and can't return");
                RefreshTacticsBoardUI();
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool applied = team.SubstitutePlayer(pinPlayer, benchPlayer);

            RefreshTacticsBoardUI();
        }

        // Per-match tactical override toggle (session 14) - see the field-level comment
        // near nextMatchOnlyButton for the full design. Arming snapshots the CURRENT
        // formation/XI as "the default to come back to" - any edits made afterward
        // (formation switch, drag substitutions, auto-pick) are then provisional for
        // just the next fixture. Clicking it again before that fixture plays cancels
        // the snapshot, keeping whatever's currently set as the permanent default -
        // same as if the toggle had never been touched.
        private void OnNextMatchOnlyToggleClicked()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (!nextMatchOnlyOverrideActive)
            {
                nextMatchOnlyOverrideActive = true;
                nextMatchOverrideDefaultFormation = team.Formation;
                nextMatchOverrideDefaultStartingEleven = new List<PlayerAgent>(team.StartingEleven);
                ShowTacticsBoardWarning("Armed - changes from here apply to the next match only, then your usual XI returns.");
            }
            else
            {
                nextMatchOnlyOverrideActive = false;
                nextMatchOverrideDefaultStartingEleven = null;
                ShowTacticsBoardWarning("Cancelled - the current setup is your default again.");
            }

            RefreshTacticsBoardUI();
        }

        // Restores the snapshot taken in OnNextMatchOnlyToggleClicked, right after the
        // one fixture it was armed for has actually been resolved - called from both
        // places currentFixtureIndex advances (the Simulate Season loop and
        // OnFullTimeContinueClicked), same as ResolveMatchdayInboxTicks. Defensively
        // filters the snapshot down to players still on the squad (a departed player
        // between arming and revert is a real, if unlikely, possibility - a transfer or
        // retirement landing in that exact one-fixture window) and bails without
        // touching anything if that leaves the XI short, rather than risk restoring a
        // corrupt lineup.
        private void ResolveNextMatchOnlyOverride()
        {
            if (!nextMatchOnlyOverrideActive)
            {
                return;
            }

            nextMatchOnlyOverrideActive = false;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            List<PlayerAgent> restoredEleven = nextMatchOverrideDefaultStartingEleven?.FindAll(p => team.Players.Contains(p));
            nextMatchOverrideDefaultStartingEleven = null;

            if (restoredEleven == null || restoredEleven.Count != squadGenerator.GetStartingPositions(nextMatchOverrideDefaultFormation).Count)
            {
                Debug.LogWarning("ManagerPrototypeController: skipped next-match-only revert - the snapshotted XI no longer matches the current squad.");
                return;
            }

            team.ChangeFormation(nextMatchOverrideDefaultFormation, restoredEleven);
        }

        // Auto-pick best XI (backlog item, session 12). Greedy slot-by-slot assignment
        // (not a true combinatorial optimum, but a strong practical XI - this is a
        // convenience feature, not core simulation logic): for each formation slot in
        // order, picks whichever eligible remaining candidate has the best
        // PlayerAgent.GetPositionFit for that specific slot. Reuses AgentTeam.
        // ChangeFormation with the SAME formation the team already has, purely for its
        // existing "assign this StartingEleven, everyone else falls to Bench" behavior -
        // no formation change happens here.
        private void OnAutoPickBestXIClicked()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            // Same two exclusions OnBenchPlayerDroppedOnPin already enforces one player
            // at a time - applied here up front so auto-pick can never do in one click
            // what a manual drag isn't allowed to do at all.
            List<PlayerAgent> pool = new List<PlayerAgent>(team.Players);
            pool.RemoveAll(p => roles.IsInjured(p, careerCalendar.CurrentDayNumber)
                || (tacticsBoardOpenedMidMatch && playersSubbedOffThisMatch.Contains(p)));

            List<PlayerAgent> bestXI = new List<PlayerAgent>();
            foreach (PlayerPosition slot in slots)
            {
                PlayerAgent best = null;
                float bestScore = float.MinValue;

                foreach (PlayerAgent candidate in pool)
                {
                    if (bestXI.Contains(candidate))
                    {
                        continue;
                    }

                    // GetPositionFit alone doesn't hard-block a keeper from an outfield
                    // slot or vice versa (it has no real notion of goalkeeping at all -
                    // "GK deliberately has no entry" in its own AdjacentPositions table),
                    // so that exact mismatch is guarded explicitly here instead.
                    bool candidateIsGK = candidate.PrimaryPosition == PlayerPosition.GK;
                    bool slotIsGK = slot == PlayerPosition.GK;
                    if (candidateIsGK != slotIsGK)
                    {
                        continue;
                    }

                    // Fit alone isn't enough - two primary-position candidates for the
                    // same slot both score a flat 1.00 fit, so comparing fit only picked
                    // whoever happened to be first in `pool` (a weaker starter) over a
                    // clearly better bench player at the same fit tier (real bug Thomas
                    // caught live: an 87-rated bench CB lost a tie to an 84-rated starting
                    // CB, so Auto-Pick visibly did nothing). Fit's four tiers (0.60/0.80/
                    // 0.85/1.00) are categorical steps far apart from each other, so
                    // multiplying it by 1000 and adding Overall (0-99) keeps fit tier
                    // strictly dominant while letting Overall break ties within a tier.
                    float fit = candidate.GetPositionFit(slot);
                    float conditionAdjustedOverall = candidate.GetOverallRating() * roles.GetConditionMultiplier(candidate);
                    float score = fit * 1000f + conditionAdjustedOverall;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null)
                {
                    bestXI.Add(best);
                }
            }

            // Fallback for a genuinely short-handed squad (mass injuries etc.) - fill any
            // remaining slot with whoever's left rather than leave a pin empty. Real
            // football-manager UX: a weak/mismatched XI is still better than no XI.
            if (bestXI.Count < slots.Count)
            {
                foreach (PlayerAgent candidate in pool)
                {
                    if (bestXI.Count >= slots.Count)
                    {
                        break;
                    }

                    if (!bestXI.Contains(candidate))
                    {
                        bestXI.Add(candidate);
                    }
                }
            }

            if (bestXI.Count < slots.Count)
            {
                ShowTacticsBoardWarning("Not enough available players to fill the XI");
                RefreshTacticsBoardUI();
                return;
            }

            team.ChangeFormation(team.Formation, bestXI);
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

            RefreshTacticsBoardUI();
        }

        private void ShowTacticsBoardWarning(string message)
        {
            if (tacticsBoardWarningLabel == null)
            {
                return;
            }

            // Built early in BuildTacticsBoardChrome, before the pitch/bench elements -
            // those render on top of it as later siblings otherwise (same z-order gotcha
            // as the dropdown popups). Bring the warning's container to front each time
            // it's actually shown rather than reordering it once at build time, since a
            // full board rebuild (RefreshTacticsBoardUI) doesn't touch this object at all.
            tacticsBoardWarningLabel.transform.parent.SetAsLastSibling();

            tacticsBoardWarningLabel.text = message;

            if (tacticsBoardWarningCoroutine != null)
            {
                StopCoroutine(tacticsBoardWarningCoroutine);
            }

            tacticsBoardWarningCoroutine = StartCoroutine(ClearTacticsBoardWarningAfterDelay());
        }

        private IEnumerator ClearTacticsBoardWarningAfterDelay()
        {
            // Realtime, not scaled - OnOpenTacticsBoardDuringMatchClicked pauses the game
            // (Time.timeScale = 0) for the entire time this board is open, so a WaitForSeconds
            // here would never progress while the manager is actually looking at the warning,
            // then barely progress in the few real seconds after resuming before the board
            // pauses again on next open - the warning would look permanently stuck.
            yield return new WaitForSecondsRealtime(3f);

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

            Func<PlayerAgent, float> homeConditionLookup = currentFixture.HomeTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p)) : null;
            Func<PlayerAgent, float> awayConditionLookup = currentFixture.AwayTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p)) : null;
            AgentTeam adjustedHome = ManagerFormationFit.BuildFitAdjustedTeam(homeTeamAgent,
                squadGenerator.GetStartingPositions(homeTeamAgent.Formation), homeConditionLookup);
            AgentTeam adjustedAway = ManagerFormationFit.BuildFitAdjustedTeam(awayTeamAgent,
                squadGenerator.GetStartingPositions(awayTeamAgent.Formation), awayConditionLookup);
            ManagerPlayerDerivedStrength.MatchupPrediction livePrediction = ManagerPlayerDerivedStrength.PredictMatchup(
                ManagerPlayerDerivedStrength.Calculate(adjustedHome, squadGenerator.GetStartingPositions(adjustedHome.Formation)),
                ManagerPlayerDerivedStrength.Calculate(adjustedAway, squadGenerator.GetStartingPositions(adjustedAway.Formation)));

            lastRawExpectedHomeGoals = livePrediction.ExpectedHomeGoals;
            lastRawExpectedAwayGoals = livePrediction.ExpectedAwayGoals;
            float liveExpectedHomeGoals = livePrediction.ExpectedHomeGoals;
            float liveExpectedAwayGoals = livePrediction.ExpectedAwayGoals;
            if (currentFixture.HomeTeam == managedTeamName)
                ManagerMentalityModifier.Apply(selectedMentality, ref liveExpectedHomeGoals, ref liveExpectedAwayGoals);
            else if (currentFixture.AwayTeam == managedTeamName)
                ManagerMentalityModifier.Apply(selectedMentality, ref liveExpectedAwayGoals, ref liveExpectedHomeGoals);
            lastExpectedHomeGoals = liveExpectedHomeGoals;
            lastExpectedAwayGoals = liveExpectedAwayGoals;

            AgentMatchSimulator.AgentMatchResult tail = matchSimulator.SimulateFromMinute(
                adjustedHome,
                adjustedAway,
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
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = 25f;

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
            squadBrowseListView.AddGridHeaderRow(OnSquadColumnHeaderClicked, squadSortColumn, squadSortDescending);
            squadBrowseListView.AddSectionHeader("Starting XI");

            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            // Starting XI's slot-based POS (from the formation, not just the player's own
            // primary position) only makes sense paired with each player's original index
            // - captured before any sort reorders the list, so "who plays where" stays
            // correct regardless of sort column/direction.
            List<(PlayerAgent player, PlayerPosition slot)> startingWithSlots = new List<(PlayerAgent, PlayerPosition)>();
            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                startingWithSlots.Add((player, slot));
            }

            if (squadSortColumn >= 0)
            {
                startingWithSlots.Sort((a, b) => CompareSquadColumn(a.player, b.player, squadSortColumn, squadSortDescending));
            }

            foreach (var (player, slot) in startingWithSlots)
            {
                squadBrowseListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            squadBrowseListView.AddSectionHeader($"Bench ({team.Bench.Count})");

            List<PlayerAgent> benchPlayers = new List<PlayerAgent>(team.Bench);
            if (squadSortColumn >= 0)
            {
                benchPlayers.Sort((a, b) => CompareSquadColumn(a, b, squadSortColumn, squadSortDescending));
            }

            foreach (PlayerAgent player in benchPlayers)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            // Reserves section (session 16 - Thomas: "we need more players per team...
            // an actual reserve", and the follow-up "Visible Reserves list" scope choice
            // when offered a choice between a quiet backend depth boost and actually
            // surfacing it). The reserve pool (see GetOrCreateReservePool) already
            // existed as an invisible emergency safety net beneath the real 20-man squad
            // - it only ever showed up once a specific player got promoted onto the
            // Bench via an injury/loan call-up. Eagerly generating it here (rather than
            // waiting for the first call-up) so it's visible from the very first time
            // the manager opens Squad, not just after a crisis. Read-only (onRowClicked:
            // null, same pattern as the opponent-pitch browse view) - these players
            // aren't on the real matchday squad, so Sell/role-assignment/etc. don't
            // apply to them the way they do for Starting XI/Bench rows.
            List<PlayerAgent> reservePlayers = team.Reserves;
            squadBrowseListView.AddSectionHeader($"Reserves ({reservePlayers.Count})");

            List<PlayerAgent> sortedReserves = new List<PlayerAgent>(reservePlayers);
            if (squadSortColumn >= 0)
            {
                sortedReserves.Sort((a, b) => CompareSquadColumn(a, b, squadSortColumn, squadSortDescending));
            }

            foreach (PlayerAgent player in sortedReserves)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            // Rows are cleared and rebuilt fresh every refresh - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(squadBrowsePanel.transform));
        }

        private void OnSquadColumnHeaderClicked(int column)
        {
            if (squadSortColumn == column)
            {
                squadSortDescending = !squadSortDescending;
            }
            else
            {
                squadSortColumn = column;
                squadSortDescending = true;
            }

            RefreshSquadUI();
        }

        // Column indices match SquadListView's GridColumnHeaders (POS/PLAYER/AGE/OVR/
        // FIT/VALUE/RATING). FIT and RATING aren't sortable (FIT is condition-derived
        // text with a variable "(Ret. MDx)" suffix, not a clean number; RATING is a
        // live-match-only stat with no meaning outside a match) - clicking those headers
        // is a no-op via AddGridHeaderRow's onColumnClicked still firing but landing on
        // the default case below (falls through to 0, same as clicking POS/PLAYER twice
        // would after a no-op sort).
        private int CompareSquadColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = ManagerClubFinance.GetMarketValue(a).CompareTo(ManagerClubFinance.GetMarketValue(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private void OnSquadRowClicked(PlayerAgent player)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.Squad;
            OpenPlayerInspect(player);
        }

        // Session 11: used to apply a cosmetic +15% stretch away from 50 so strong
        // squads read as clearly elite without touching the true GetOverallRating()
        // value - removed once AgentSquadGenerator's team-strength multiplier was
        // strengthened (0.35->0.75) to make top clubs legitimately generate higher
        // attributes instead. Stacking the old stretch on top of that honest fix
        // tripled the league's count of "90+" players (6 true vs 18 displayed in a real
        // 20-club sample) and pulled players as low as a true 85 up into "90+" - closer
        // to inflation than to Liverpool no longer reading as underrated. This function
        // stays as the one place every screen routes an Overall through, in case a
        // display transform is ever wanted again - it just no longer changes anything.
        private static int GetDisplayRating(float trueRating)
        {
            return Mathf.RoundToInt(Mathf.Clamp(trueRating, 1f, 99f));
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
            if (roles.IsInjured(player, careerCalendar.CurrentDayNumber))
            {
                // No leading "INJ" text anymore - the injury cross icon (see
                // ManagerUITheme.BuildInjuryCrossIcon) already says that visually now;
                // this just adds the one piece of info the icon alone can't carry.
                int returnDay = roles.GetInjuryReturnMatchday(player);
                string dangerHex = ColorUtility.ToHtmlStringRGB(ManagerUITheme.Danger);
                return $"<color=#{dangerHex}>(Ret. {ManagerCareerCalendar.DisplayDateForDay(returnDay)})</color>";
            }

            float condition = roles.GetCondition(player);
            Color conditionColor = condition >= 85f
                ? ManagerUITheme.Accent
                : condition >= 60f
                    ? ManagerUITheme.Warning
                    : ManagerUITheme.Danger;
            string conditionHex = ColorUtility.ToHtmlStringRGB(conditionColor);
            return $"<color=#{conditionHex}>{condition:F0}%</color>";
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
                inspectSquadPlayers = new List<PlayerAgent>(team.Players);
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
            CloseMatchdaySquadSwapDialog();
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
        private GameObject matchdaySquadSwapDialog;

        private void ShowMatchdaySquadSwapDialog(PlayerAgent selectedPlayer)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool selectedIsBench = team.Bench.Contains(selectedPlayer);
            List<PlayerAgent> options = selectedIsBench
                ? new List<PlayerAgent>(team.Reserves)
                : new List<PlayerAgent>(team.Bench);
            if (options.Count == 0) return;

            if (matchdaySquadSwapDialog != null) Destroy(matchdaySquadSwapDialog);

            Transform root = titlePanel.transform.parent;
            matchdaySquadSwapDialog = new GameObject("MatchdaySquadSwapDialog", typeof(RectTransform), typeof(Image));
            matchdaySquadSwapDialog.transform.SetParent(root, false);
            RectTransform backdropRect = matchdaySquadSwapDialog.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            matchdaySquadSwapDialog.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(matchdaySquadSwapDialog.transform, false);
            ManagerUITheme.SetPointAnchor(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 660f));
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(title.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 34f));
            ManagerUITheme.BuildLabel(title.transform,
                selectedIsBench ? $"REPLACE {selectedPlayer.Name.ToUpperInvariant()}" : $"SELECT {selectedPlayer.Name.ToUpperInvariant()} AS SUBSTITUTE",
                20, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            float top = 82f;
            foreach (PlayerAgent option in options)
            {
                PlayerAgent captured = option;
                string label = $"{option.Name}  ·  {option.PrimaryPosition}  ·  OVR {GetDisplayRating(option.GetOverallRating())}  ·  {roles.GetCondition(option):F0}%";
                Button optionButton = ManagerUITheme.BuildButton(card.transform, label, ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(optionButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(540f, 42f));
                optionButton.onClick.AddListener(() => OnMatchdaySquadSwapSelected(selectedPlayer, captured));
                top += 48f;
            }

            Button cancel = ManagerUITheme.BuildButton(card.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextMuted, 13);
            ManagerUITheme.SetPointAnchor(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(180f, 42f));
            cancel.onClick.AddListener(CloseMatchdaySquadSwapDialog);
        }

        private void OnMatchdaySquadSwapSelected(PlayerAgent selectedPlayer, PlayerAgent option)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            PlayerAgent benchPlayer = team.Bench.Contains(selectedPlayer) ? selectedPlayer : option;
            PlayerAgent reservePlayer = team.Reserves.Contains(selectedPlayer) ? selectedPlayer : option;
            if (team.SwapBenchAndReserve(benchPlayer, reservePlayer))
            {
                CloseMatchdaySquadSwapDialog();
                RefreshPlayerInspectUI();
            }
        }

        private void CloseMatchdaySquadSwapDialog()
        {
            if (matchdaySquadSwapDialog != null) Destroy(matchdaySquadSwapDialog);
            matchdaySquadSwapDialog = null;
        }

        // Rebuilt in full each time (unlike Title/Team Select, which build once) since the
        // content changes per player. Only uses PlayerAgent fields that actually exist -
        // Archetypes are generated data now, so the descriptive footballing profile in
        // the header is genuine rather than inferred UI flavour text.
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
                : player.IsStartingEleven ? "Starting XI" : GetOrCreateAgentTeam(managedTeamName).Reserves.Contains(player) ? "Reserves" : "Bench";

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

            // Developer easter eggs (see ApplyDeveloperEasterEggPlayer) - real portraits
            // for these specific players, everyone else keeps the plain placeholder
            // color since there's no actual photo pipeline for generated players.
            Image photoImage = photo.GetComponent<Image>();
            Sprite easterEggPortrait = player.Name switch
            {
                "Hidde Rietberg" => hiddePortraitSprite,
                "Thomas Bernards" => thomasPortraitSprite,
                "Charles Herring" => charliePortraitSprite,
                "Victor Hamberg" => victorPortraitSprite,
                _ => null
            };

            if (easterEggPortrait != null)
            {
                photoImage.sprite = easterEggPortrait;
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
            string archetypeText = string.IsNullOrWhiteSpace(player.Archetype) ? player.Role.ToString() : player.Archetype;
            string metaText = $"{archetypeText}  ·  {nationalityName}  ·  {player.Age} yrs  ·  {player.Height:F0}cm  ·  Weak Foot: {BuildFootRating(player.WeakFoot)}  ·  Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})";
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

                // Morale (session 10) - same placement/treatment as Condition directly
                // above it, same inspectIsOwnSquad gate for the same reason (only the
                // managed squad ever has real morale tracked - see
                // ApplyMatchMoraleForManagedTeam).
                float morale = GetOrCreateSquadRoles(managedTeamName).GetMorale(player);
                Color moraleColor = morale >= 70f
                    ? ManagerUITheme.Accent
                    : morale >= 40f
                        ? ManagerUITheme.Warning
                        : ManagerUITheme.Danger;

                GameObject moraleCaption = new GameObject("MoraleCaption", typeof(RectTransform));
                moraleCaption.transform.SetParent(headerBand.transform, false);
                RectTransform moraleRect = moraleCaption.GetComponent<RectTransform>();
                moraleRect.anchorMin = new Vector2(1f, 1f);
                moraleRect.anchorMax = new Vector2(1f, 1f);
                moraleRect.pivot = new Vector2(1f, 1f);
                moraleRect.sizeDelta = new Vector2(180f, 18f);
                moraleRect.anchoredPosition = new Vector2(-36f, -146f);
                ManagerUITheme.BuildLabel(moraleCaption.transform, $"MORALE {morale:F0}", 13, moraleColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
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

                AgentTeam ownTeam = GetOrCreateAgentTeam(managedTeamName);
                if (!ownTeam.StartingEleven.Contains(player))
                {
                    string selectionLabel = ownTeam.Bench.Contains(player) ? "CHANGE SUBSTITUTE" : "SELECT AS SUBSTITUTE";
                    Button selectionButton = ManagerUITheme.BuildButton(rolesBand.transform, selectionLabel, ManagerUITheme.CardNeutral, ManagerUITheme.Accent, 12);
                    ManagerUITheme.SetPointAnchor(selectionButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-235f, 0f), new Vector2(190f, 40f));
                    selectionButton.onClick.AddListener(() => ShowMatchdaySquadSwapDialog(player));
                }
            }
            else if (inspectIsAcademyProspect)
            {
                BuildFocusStatsPicker(rolesBand.transform, player);

                // Manual release (backlog item 8, session 11) - right-anchored on the
                // same top row as the focus-stats caption, same "action button sits at
                // the far edge of the band" convention LOAN OUT uses above for an
                // own-squad player. No confirmation dialog, same precedent as LOAN OUT -
                // returning to the Academy list is itself the confirmation.
                Button releaseButton = ManagerUITheme.BuildButton(rolesBand.transform, "RELEASE", ManagerUITheme.CardNeutral, ManagerUITheme.Danger, 13);
                ManagerUITheme.SetPointAnchor(releaseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-16f, -4f), new Vector2(110f, 26f));
                releaseButton.onClick.AddListener(() => OnReleaseAcademyProspectClicked(player));
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
                    ("Handling", player.Handling), ("Reflexes", player.Reflexes),
                    ("One On Ones", player.OneOnOnes), ("GK Positioning", player.GoalkeeperPositioning),
                    ("Aerial Command", player.AerialCommand)
                });

                BuildAttributeColumn(attributeGridRect, 1, 4, "Mental", new (string, float)[]
                {
                    ("Anticipation", player.Anticipation), ("Decisions", player.Decisions),
                    ("Composure", player.Composure), ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 4, "Distribution", new (string, float)[]
                {
                    ("Distribution", player.Distribution), ("Passing", player.Passing),
                    ("First Touch", player.FirstTouch), ("Weak Foot", player.WeakFoot)
                });

                BuildAttributeColumn(attributeGridRect, 3, 4, "Physical", new (string, float)[]
                {
                    ("Acceleration", player.Acceleration), ("Pace", player.Pace),
                    ("Strength", player.Strength), ("Jumping Reach", player.JumpingReach)
                });
            }
            else
            {
                BuildAttributeColumn(attributeGridRect, 0, 3, "Technical", new (string, float)[]
                {
                    ("Finishing", player.Finishing), ("First Touch", player.FirstTouch),
                    ("Passing", player.Passing), ("Technique", player.Technique),
                    ("Dribbling", player.Dribbling), ("Crossing", player.Crossing),
                    ("Heading", player.Heading), ("Long Shots", player.LongShots),
                    ("Tackling", player.Tackling), ("Marking", player.Marking),
                    ("Free Kicks", player.FreeKicks), ("Corners", player.Corners), ("Penalties", player.Penalties)
                });

                BuildAttributeColumn(attributeGridRect, 1, 3, "Mental", new (string, float)[]
                {
                    ("Anticipation", player.Anticipation), ("Decisions", player.Decisions),
                    ("Composure", player.Composure), ("Vision", player.Vision),
                    ("Off The Ball", player.OffTheBall), ("Def. Positioning", player.DefensivePositioning),
                    ("Work Rate", player.WorkRate), ("Aggression", player.Aggression),
                    ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 3, "Physical", new (string, float)[]
                {
                    ("Acceleration", player.Acceleration), ("Pace", player.Pace),
                    ("Agility", player.Agility), ("Balance", player.Balance),
                    ("Strength", player.Strength), ("Stamina", player.Stamina),
                    ("Jumping Reach", player.JumpingReach)
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
                case "FirstTouch": return "1ST";
                case "Technique": return "TECH";
                case "Dribbling": return "DRI";
                case "Crossing": return "CRO";
                case "Heading": return "HEA";
                case "LongShots": return "L.SHOT";
                case "ThroughBalls": return "T.BALL";
                case "Creativity": return "CREA";
                case "Anticipation": return "ANT";
                case "Decisions": return "DEC";
                case "Vision": return "VIS";
                case "DefensivePositioning": return "D.POS";
                case "WorkRate": return "WORK";
                case "Positioning": return "POS";
                case "Composure": return "COMP";
                case "OffTheBall": return "OTB";
                case "Defending": return "DEF";
                case "Tackling": return "TACK";
                case "Marking": return "MARK";
                case "Pace": return "PACE";
                case "Acceleration": return "ACC";
                case "Agility": return "AGI";
                case "Balance": return "BAL";
                case "Strength": return "STR";
                case "Stamina": return "STAM";
                case "Aerial": return "AER";
                case "JumpingReach": return "JUMP";
                case "Goalkeeping": return "GK";
                case "Reflexes": return "REFL";
                case "Handling": return "HAND";
                case "OneOnOnes": return "1V1";
                case "AerialCommand": return "A.CMD";
                case "Distribution": return "DIST";
                case "GoalkeeperPositioning": return "GK.POS";
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

            DateTime fixtureDate = careerCalendar.GetFixtureDate(currentFixtureIndex);
            if (careerCalendar.CurrentDate.Date < fixtureDate.Date)
            {
                bool interrupted = AdvanceCalendarTo(fixtureDate, stopForNewInboxMessage: true);
                RefreshHubUI();
                if (interrupted || careerCalendar.CurrentDate.Date < fixtureDate.Date) return;
            }

            currentFixture = managedTeamFixtures[currentFixtureIndex];

            ShowMatchdayPrep();
        }

        private bool AdvanceCalendarTo(DateTime targetDate, bool stopForNewInboxMessage)
        {
            while (careerCalendar.CurrentDate.Date < targetDate.Date)
            {
                int messageCountBefore = inbox.Messages.Count;
                bool windowWasOpen = careerCalendar.IsTransferWindowOpen;
                careerCalendar.AdvanceOneDay();
                int currentDay = careerCalendar.CurrentDayNumber;

                scouting.ResolveDailyTick(currentDay, squadGenerator, inbox);
                transferNegotiation.ResolveDueTransferScoutAssignments(currentDay, inbox, FindTeamContainingPlayer);
                transferNegotiation.ResolveDueBids(currentDay, finance, managedTeamName, inbox, FindTeamContainingPlayer);
                transferNegotiation.ResolveExpiredSignatures(currentDay, finance, managedTeamName, inbox);
                ResolveDailyInjuryRecoveries();

                if (windowWasOpen != careerCalendar.IsTransferWindowOpen)
                {
                    bool opened = careerCalendar.IsTransferWindowOpen;
                    inbox.Add(InboxMessageType.RecruitmentTeaser,
                        opened ? "Transfer Window Open" : "Transfer Window Closed",
                        opened
                            ? "The transfer window is now open. Registered transfers can be completed until the deadline."
                            : "The transfer window has closed. New registered transfers must wait for the next window.",
                        currentDay);
                }

                if (stopForNewInboxMessage && inbox.Messages.Count > messageCountBefore)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveDailyInjuryRecoveries()
        {
            if (injuredPlayersTracked.Count == 0) return;

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerAgent> recovered = new List<PlayerAgent>();
            foreach (PlayerAgent player in injuredPlayersTracked)
            {
                if (!roles.IsInjured(player, careerCalendar.CurrentDayNumber)) recovered.Add(player);
            }

            foreach (PlayerAgent player in recovered)
            {
                injuredPlayersTracked.Remove(player);
                inbox.Add(InboxMessageType.Recovery, $"{player.Name} Fit Again",
                    $"{player.Name} has recovered from injury and is available for selection again.",
                    careerCalendar.CurrentDayNumber);
            }
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

                opponentSquadListView.AddSectionHeader($"Reserves ({opponentTeam.Reserves.Count})");
                foreach (PlayerAgent player in opponentTeam.Reserves)
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

            // Live ratings grid (session 10) - a full-width band of 11 player cards
            // sitting just above the footer. The gap it lives in wasn't actually free
            // space - it was the Match Log's own reserved scroll area, which simply
            // hadn't filled up with enough events yet to visually reach the bottom early
            // in a match. Genuinely claiming this height (shrinking the event feed mask
            // below) rather than just drawing on top of it, so the two never overlap
            // once the log grows.
            const float ratingsGridHeight = 108f;
            const float ratingsGridGap = 16f;
            const float ratingsGridBottomOffset = footerHeight + ratingsGridGap;

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

            // Session 16 - moved here from its old spot beneath the Subs Made log
            // (Thomas's follow-up: even after that relocation, a busy match with 5-6
            // subs still grew tall enough to reach it). A fixed-position button below a
            // list that can grow arbitrarily tall was always going to collide again
            // eventually - the header toolbar is a position nothing else ever grows
            // into, so it can't recur here. Left of Pause with the same 8px gap Pause
            // itself keeps from Skip to Results.
            Button makeChangesButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(makeChangesButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-286f, -14f), new Vector2(140f, 30f));
            makeChangesButton.onClick.AddListener(OnOpenTacticsBoardDuringMatchClicked);

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

            matchLiveOnlyElements = new[] { pauseButton.gameObject, makeChangesButton.gameObject, skipToResultsButton != null ? skipToResultsButton.gameObject : null, clockText != null ? clockText.gameObject : null };

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
                // Bottom edge raised from footerHeight+24 to clear the ratings grid band
                // now sitting between this and the footer - see ratingsGridBottomOffset.
                maskRect.offsetMin = new Vector2(40f, ratingsGridBottomOffset + ratingsGridHeight + ratingsGridGap);
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

            // --- Right column: Match Stats (unchanged position, x=0.55) ---
            // --- Far-right column: Substitutions (top) then Make Changes (below) ---
            // Session 16 (Thomas, screenshot with two drawn boxes) - Subs Made used to
            // stack directly above Match Stats in the SAME 0.55-anchored column, with
            // Make Changes and the Stats caption pinned at fixed y-offsets below it. Its
            // ContentSizeFitter grows the log taller as more subs get made, but nothing
            // below it ever moved to make room - by the 2nd-3rd sub the growing list
            // physically overlapped Make Changes and Match Stats. Moved to its own
            // right-edge-anchored column instead, clear of both. Right-edge anchor
            // (anchorMax=anchorMin=pivot=(1,1)) means SetPointAnchor's pivot==anchor
            // behavior is already correct here (unlike the old x=0.55 "left edge
            // reference" usage, which needed an explicit pivot.x=0 override to stop the
            // element straddling its own anchor point) - the column's right edge sits at
            // the anchor, growing left/down from there, exactly what a right-margin-
            // flush column needs.
            GameObject subsCaptionObj = new GameObject("SubsMadeCaption", typeof(RectTransform));
            subsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsCaptionRect = subsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsCaptionRect, new Vector2(1f, 1f), new Vector2(-halfMargin, -(headerHeight + 28f)), new Vector2(300f, 20f));
            ManagerUITheme.BuildLabel(subsCaptionObj.transform, "SUBS MADE  ·  MANAGE VIA TACTICS BOARD", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Read-only log of subs made this match (see matchSubsLog) - populated by
            // RefreshMatchSubsMadeList, one row per entry. Subs themselves happen on the
            // Tactics Board via "Make Changes" below, not here - no picker on this screen.
            GameObject subsLogObj = new GameObject("SubsLog", typeof(RectTransform));
            subsLogObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsLogRect = subsLogObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsLogRect, new Vector2(1f, 1f), new Vector2(-halfMargin, -(headerHeight + 54f)), new Vector2(300f, 76f));
            matchSubsLogContainer = subsLogRect;

            VerticalLayoutGroup subsLogLayout = subsLogObj.AddComponent<VerticalLayoutGroup>();
            subsLogLayout.childForceExpandWidth = true;
            subsLogLayout.childForceExpandHeight = false;
            subsLogLayout.childControlWidth = true;
            subsLogLayout.childControlHeight = true;
            subsLogLayout.spacing = 6f;

            ContentSizeFitter subsLogFitter = subsLogObj.AddComponent<ContentSizeFitter>();
            subsLogFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Subs Made is a live-match-only concept - the design's Full-Time Summary has
            // no equivalent section at all, so this whole column needs to disappear at
            // full-time exactly like the tactic pills do. Make Changes moved up to the
            // header toolbar (see pauseButton's own block above) - no longer built here.
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsLogObj;

            // Session 16 - top-aligned to the same y-offset as Match Log/Subs Made
            // (Thomas's explicit ask) rather than starting partway down the panel -
            // there's nothing above it in this column anymore now that Subs Made has
            // its own column and Make Changes lives in the header.
            GameObject statsCaptionObj = new GameObject("MatchStatsCaption", typeof(RectTransform));
            statsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform statsCaptionRect2 = statsCaptionObj.GetComponent<RectTransform>();
            matchStatsCaptionRect = statsCaptionRect2;
            ManagerUITheme.SetPointAnchor(statsCaptionRect2, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 28f)), new Vector2(360f, 20f));
            statsCaptionRect2.pivot = new Vector2(0f, 1f);
            matchStatsCaptionLabel = ManagerUITheme.BuildLabel(statsCaptionObj.transform, "MATCH STATS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject statsBarsObj = new GameObject("MatchStatsBars", typeof(RectTransform));
            statsBarsObj.transform.SetParent(matchdayPanel.transform, false);
            matchStatsBarsContainer = statsBarsObj.GetComponent<RectTransform>();
            matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
            // 28px below the caption's new top-aligned start (headerHeight + 28), same
            // internal caption-to-bars gap as before this section moved up.
            matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 56f));
            // Grown from 140 (1 row: Shots) to fit 4 rows (Possession/Chances Created/
            // Shots/Shots on Target) at 36px pitch each.
            matchStatsBarsContainer.sizeDelta = new Vector2(360f, 190f);

            // --- Live ratings grid: full-width strip of 11 player cards, bottom-anchored
            // just above the footer (see ratingsGridHeight/ratingsGridBottomOffset at the
            // top of this method). Live-only (session 10 fix, live bug report) - the
            // original design kept this visible through Full Time as a "final ratings"
            // readout, but Full Time's own layout (goal timeline + scorer lists) uses
            // that same bottom region and the two overlapped in practice. Added to
            // matchLiveOnlyElements below, same as Subs Made/Make Changes, so it's gone
            // by the time Full Time's layout takes over.
            GameObject ratingsGridCaptionObj = new GameObject("RatingsGridCaption", typeof(RectTransform));
            ratingsGridCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform ratingsGridCaptionRect = ratingsGridCaptionObj.GetComponent<RectTransform>();
            ratingsGridCaptionRect.anchorMin = new Vector2(0f, 0f);
            ratingsGridCaptionRect.anchorMax = new Vector2(0f, 0f);
            ratingsGridCaptionRect.pivot = new Vector2(0f, 0f);
            ratingsGridCaptionRect.anchoredPosition = new Vector2(halfMargin, ratingsGridBottomOffset + ratingsGridHeight + 6f);
            ratingsGridCaptionRect.sizeDelta = new Vector2(400f, 20f);
            ManagerUITheme.BuildLabel(ratingsGridCaptionObj.transform, "PLAYER RATINGS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject ratingsGridObj = new GameObject("RatingsGrid", typeof(RectTransform));
            ratingsGridObj.transform.SetParent(matchdayPanel.transform, false);
            matchRatingsGridContainer = ratingsGridObj.GetComponent<RectTransform>();
            matchRatingsGridContainer.anchorMin = new Vector2(0f, 0f);
            matchRatingsGridContainer.anchorMax = new Vector2(1f, 0f);
            matchRatingsGridContainer.pivot = new Vector2(0f, 0f);
            matchRatingsGridContainer.offsetMin = new Vector2(halfMargin, ratingsGridBottomOffset);
            matchRatingsGridContainer.offsetMax = new Vector2(-halfMargin, ratingsGridBottomOffset + ratingsGridHeight);

            HorizontalLayoutGroup ratingsGridLayout = ratingsGridObj.AddComponent<HorizontalLayoutGroup>();
            ratingsGridLayout.childForceExpandWidth = true;
            ratingsGridLayout.childForceExpandHeight = true;
            ratingsGridLayout.childControlWidth = true;
            ratingsGridLayout.childControlHeight = true;
            ratingsGridLayout.spacing = 8f;

            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = ratingsGridCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = ratingsGridObj;

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

        // Live ratings grid (session 10) - one card per CURRENT managed-team starter
        // (so a mid-match substitution swaps the card, not just the number inside it),
        // rebuilt fresh on every call same as RefreshMatchSubsMadeList above - called
        // once per revealed event during ReplayMatchCoroutine, so this genuinely does
        // run a lot over the course of a match; destroy/recreate at this frequency is
        // already the established pattern for this screen's other live lists, and 11
        // small cards is cheap.
        private void RefreshMatchRatingsGrid()
        {
            if (matchRatingsGridContainer == null)
            {
                return;
            }

            foreach (Transform child in matchRatingsGridContainer)
            {
                Destroy(child.gameObject);
            }

            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);

            foreach (PlayerAgent player in managedTeam.StartingEleven)
            {
                float rating = matchRatings.GetRating(player.Name);

                GameObject card = new GameObject($"RatingCard_{player.Name}", typeof(RectTransform), typeof(Image));
                card.transform.SetParent(matchRatingsGridContainer, false);
                card.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

                GameObject nameObj = new GameObject("Name", typeof(RectTransform));
                nameObj.transform.SetParent(card.transform, false);
                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.pivot = new Vector2(0.5f, 1f);
                nameRect.offsetMin = new Vector2(4f, -36f);
                nameRect.offsetMax = new Vector2(-4f, -6f);
                TextMeshProUGUI nameLabel = ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 12, ManagerUITheme.TextMuted, TextAlignmentOptions.Top, FontStyles.Bold);
                nameLabel.enableAutoSizing = true;
                nameLabel.fontSizeMin = 9;
                nameLabel.fontSizeMax = 12;
                nameLabel.textWrappingMode = TextWrappingModes.NoWrap;

                GameObject ratingObj = new GameObject("Rating", typeof(RectTransform));
                ratingObj.transform.SetParent(card.transform, false);
                RectTransform ratingRect = ratingObj.GetComponent<RectTransform>();
                ratingRect.anchorMin = new Vector2(0f, 0f);
                ratingRect.anchorMax = new Vector2(1f, 1f);
                ratingRect.offsetMin = new Vector2(4f, 8f);
                ratingRect.offsetMax = new Vector2(-4f, -38f);
                ManagerUITheme.BuildLabel(ratingObj.transform, rating.ToString("F1"), 26, ManagerUITheme.RatingColor(rating * 10f), TextAlignmentOptions.Center, FontStyles.Bold);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(matchRatingsGridContainer));
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

            BeginMidMatchTacticsDraft();

            RefreshTacticsBoardUI();
        }

        private void ShowHalfTimePanel(int homeShots, int awayShots, int homeShotsOnTarget, int awayShotsOnTarget, int homeAttackEvents, int awayAttackEvents)
        {
            if (halfTimePanel == null)
            {
                halfTimePanel = new GameObject("HalfTimePanel", typeof(RectTransform), typeof(Image));
                halfTimePanel.transform.SetParent(matchdayPanel.transform, false);
                RectTransform panelRect = halfTimePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                halfTimePanel.GetComponent<Image>().color = ManagerUITheme.Background;

                GameObject title = new GameObject("Title", typeof(RectTransform));
                title.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(title.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(500f, 44f));
                ManagerUITheme.BuildLabel(title.transform, "HALF TIME", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

                GameObject score = new GameObject("Score", typeof(RectTransform));
                score.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(score.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -155f), new Vector2(500f, 70f));
                halfTimeScoreLabel = ManagerUITheme.BuildLabel(score.transform, "0 - 0", 52, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

                GameObject stats = new GameObject("Stats", typeof(RectTransform));
                stats.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(stats.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(680f, 260f));
                halfTimeStatsLabel = ManagerUITheme.BuildLabel(stats.transform, string.Empty, 18, ManagerUITheme.TextBody, TextAlignmentOptions.Center);

                Button changes = ManagerUITheme.BuildButton(halfTimePanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 14);
                ManagerUITheme.SetPointAnchor(changes.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-120f, 80f), new Vector2(210f, 48f));
                changes.onClick.AddListener(OnOpenTacticsBoardDuringMatchClicked);

                Button resume = ManagerUITheme.BuildButton(halfTimePanel.transform, "START SECOND HALF", ManagerUITheme.Accent, Color.white, 14);
                ManagerUITheme.SetPointAnchor(resume.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(120f, 80f), new Vector2(210f, 48f));
                resume.onClick.AddListener(OnResumeFromHalfTimeClicked);
            }

            int totalAttacks = Mathf.Max(1, homeAttackEvents + awayAttackEvents);
            int homePossession = Mathf.RoundToInt(homeAttackEvents * 100f / totalAttacks);
            halfTimeScoreLabel.text = $"{liveHomeGoalsSoFar} - {liveAwayGoalsSoFar}";
            halfTimeStatsLabel.text =
                $"POSSESSION      {homePossession}%     {100 - homePossession}%\n\n" +
                $"CHANCES CREATED      {homeAttackEvents}     {awayAttackEvents}\n\n" +
                $"SHOTS      {homeShots}     {awayShots}\n\n" +
                $"SHOTS ON TARGET      {homeShotsOnTarget}     {awayShotsOnTarget}";
            halfTimePanel.SetActive(true);
            halfTimePanel.transform.SetAsLastSibling();
            waitingAtHalfTime = true;
            matchPaused = true;
            Time.timeScale = 0f;
        }

        private void OnResumeFromHalfTimeClicked()
        {
            waitingAtHalfTime = false;
            matchPaused = false;
            Time.timeScale = 1f;
            if (halfTimePanel != null) halfTimePanel.SetActive(false);
            if (pauseButton != null) ManagerUITheme.NormalizeButtonLabel(pauseButton, "PAUSE", ManagerUITheme.TextBody, 12);
        }

        private void BeginMidMatchTacticsDraft()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            midMatchDraftFormation = team.Formation;
            midMatchDraftStartingEleven = new List<PlayerAgent>(team.StartingEleven);
            midMatchDraftBench = new List<PlayerAgent>(team.Bench);
            midMatchDraftReserves = new List<PlayerAgent>(team.Reserves);
            tacticsBoardOpenedMidMatch = true;
        }

        private bool TryCommitMidMatchTacticsDraft()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            if (midMatchDraftStartingEleven == null) return true;

            List<PlayerAgent> incoming = team.StartingEleven.Where(player => !midMatchDraftStartingEleven.Contains(player)).ToList();
            List<PlayerAgent> outgoing = midMatchDraftStartingEleven.Where(player => !team.StartingEleven.Contains(player)).ToList();
            int remainingSubs = MaxSubsPerMatch - matchSubsLog.Count;
            if (incoming.Count > remainingSubs || incoming.Count != outgoing.Count)
            {
                ShowTacticsBoardWarning($"You can make {Mathf.Max(0, remainingSubs)} more substitution{(remainingSubs == 1 ? "" : "s")}");
                return false;
            }

            bool changed = team.Formation != midMatchDraftFormation
                || !team.StartingEleven.SequenceEqual(midMatchDraftStartingEleven);

            for (int i = 0; i < incoming.Count; i++)
            {
                PlayerAgent playerOn = incoming[i];
                PlayerAgent playerOff = outgoing[i];
                matchSubsLog.Add((playerOff.Name, playerOff.PrimaryPosition.ToString(), playerOn.Name, playerOn.PrimaryPosition.ToString(), currentMatchMinute));
                playersSubbedOffThisMatch.Add(playerOff);
                matchSimulator.RegisterSubstitution(playerOn, currentMatchMinute);
                matchRatings.EnsureTracked(playerOn.Name);
            }

            if (changed)
            {
                TriggerMidMatchResimulation();
                RefreshMatchSubsMadeList();
                RefreshMatchRatingsGrid();
            }

            midMatchDraftStartingEleven = null;
            midMatchDraftBench = null;
            midMatchDraftReserves = null;
            return true;
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

        // --- Reusable confirm dialog (backlog items 13 + 15, session 11) - both "you
        // haven't set a Captain yet" and "are you sure you want to auto-skip the
        // season" are the same shape: a message + Confirm/Cancel. Built fresh each
        // time rather than chrome-cached, since content varies per call. ---

        private GameObject confirmDialogPanel;

        private void ShowConfirmDialog(string message, string confirmLabel, System.Action onConfirm, string cancelLabel, System.Action onCancel)
        {
            if (confirmDialogPanel != null)
            {
                Destroy(confirmDialogPanel);
            }

            Transform root = titlePanel.transform.parent;
            confirmDialogPanel = new GameObject("ConfirmDialogPanel", typeof(RectTransform), typeof(Image));
            confirmDialogPanel.transform.SetParent(root, false);
            RectTransform panelRect = confirmDialogPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            confirmDialogPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            // Last sibling so it renders above whatever screen is currently active,
            // regardless of that screen's own build order - same z-order technique as
            // the Tactics Board warning label.
            confirmDialogPanel.transform.SetAsLastSibling();

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(confirmDialogPanel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(640f, 260f);
            cardRect.anchoredPosition = Vector2.zero;
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject messageObj = new GameObject("Message", typeof(RectTransform));
            messageObj.transform.SetParent(card.transform, false);
            RectTransform messageRect = messageObj.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = new Vector2(0f, -40f);
            messageRect.sizeDelta = new Vector2(-80f, 140f);
            ManagerUITheme.BuildLabel(messageObj.transform, message, 17, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Normal, noWrap: false);

            Button confirmButton = ManagerUITheme.BuildButton(card.transform, confirmLabel, ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-90f, 36f), new Vector2(160f, 48f));
            confirmButton.onClick.AddListener(() => { CloseConfirmDialog(); onConfirm?.Invoke(); });

            Button cancelButton = ManagerUITheme.BuildButton(card.transform, cancelLabel, ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 15);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(90f, 36f), new Vector2(160f, 48f));
            cancelButton.onClick.AddListener(() => { CloseConfirmDialog(); onCancel?.Invoke(); });

            StartCoroutine(RecoverBlankLabelsNextFrame(confirmDialogPanel.transform));
        }

        private void CloseConfirmDialog()
        {
            if (confirmDialogPanel != null)
            {
                Destroy(confirmDialogPanel);
                confirmDialogPanel = null;
            }
        }

        // Session 16 - squad roles (captain/vice/penalty/FK/corner takers) were made
        // cosmetic-only (Thomas's explicit scope call: real mechanical effects tied to
        // assigning/not-assigning them were more headache than the feature was worth).
        // The pre-first-match "you haven't assigned a Captain" warning (backlog item 13,
        // session 11) no longer has anything real to warn about, so it's gone.
        public void OnSimulateMatchButtonClicked()
        {
            OnSimulateMatchClicked();
        }

        // Backlog item 15 (session 11) - Thomas: an accidental click currently costs the
        // whole rest of the season with no way back (see backlog item 10's collapse
        // finding, which this pairs with). Straightforward confirm-before-irreversible
        // pattern, same shape as item 13's dialog above.
        public void OnSimulateSeasonButtonClicked()
        {
            ShowConfirmDialog(
                "Simulate the rest of the season automatically? This can't be undone.",
                "SIMULATE SEASON", OnSimulateSeasonClicked,
                "CANCEL", null);
        }

        public void OnSimulateMatchClicked()
        {
            // Mentality has no pre-match picker (the Attacking/Balanced/Defensive buttons
            // only exist in the live match footer, see matchLiveOnlyElements) - so whatever
            // was left active at the end of the previous match (including a live change
            // made late on) would otherwise silently carry into this match's expected-goals
            // calc below, before the manager gets any chance to see or choose it again.
            // Reset here, before SimulateFixture uses selectedMentality, not after (where
            // matchSubsLog.Clear() sits below) - resetting after would still let this match
            // kick off on the stale value and only take effect from the match after.
            selectedMentality = ManagerMentality.Balanced;

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
            playersSubbedOffThisMatch.Clear();
            RefreshMatchSubsMadeList();
            if (matchFullTimeCaptionGroup != null) matchFullTimeCaptionGroup.SetActive(false);

            // Live ratings (session 10) - seeded with whichever XI SimulateFixture just
            // locked in (post EnsureNoInjuredStarters) for the managed team specifically,
            // same managed-team-only scope as Condition/appearances/form bonus.
            List<string> ratingsPlayerNames = new List<string>();
            foreach (PlayerAgent p in GetOrCreateAgentTeam(managedTeamName).StartingEleven) ratingsPlayerNames.Add(p.Name);
            matchRatings.ResetForMatch(ratingsPlayerNames);
            RefreshMatchRatingsGrid();

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
                AdvanceCalendarTo(careerCalendar.GetFixtureDate(currentFixtureIndex), stopForNewInboxMessage: false);
                OpenFootballMatch fixture = managedTeamFixtures[currentFixtureIndex];

                // isAutoResolved: true (backlog item 10) - see ApplyMatchdayConditionAndInjuries
                // and SimulateFixture's own comments for why only Condition/injury are
                // neutralized here, not morale/form/development.
                ApplyFixtureResult(fixture, SimulateFixture(fixture, isAutoResolved: true));
                SimulateOtherFixturesInMatchday(fixture.Matchday);

                currentFixtureIndex++;
                ResolveMatchdayInboxTicks();
                ResolveNextMatchOnlyOverride();
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
            ApplyMatchMoraleForManagedTeam(fixture, result);
            SendPostMatchReactionForManagedTeam(fixture, result);
        }

        // Tier 1 potentialemails.txt batch, #6-10 (session 14) - post-match reaction,
        // gated to avoid flooding the Inbox over a 38-match season (explicitly flagged
        // as an open decision in the session 13 handoff). A notable result (margin 3+)
        // always gets a message immediately - that's exactly the kind of result worth
        // reacting to on its own. An ordinary win/draw/loss only sends once the gap
        // since the last post-match message has reached PostMatchReactionMinGapMatchdays,
        // so routine results still surface periodically without one landing after
        // literally every fixture.
        private const int PostMatchReactionMinGapMatchdays = 2;
        private int lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;

        private void SendPostMatchReactionForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            bool isManagedHome = fixture.HomeTeam == managedTeamName;
            bool isManagedAway = fixture.AwayTeam == managedTeamName;

            if (!isManagedHome && !isManagedAway)
            {
                return;
            }

            int managedGoals = isManagedHome ? result.HomeGoals : result.AwayGoals;
            int opponentGoals = isManagedHome ? result.AwayGoals : result.HomeGoals;
            int margin = managedGoals - opponentGoals;
            string opponentName = isManagedHome ? fixture.AwayTeam : fixture.HomeTeam;

            bool isNotable = Mathf.Abs(margin) >= 3;
            bool gapElapsed = currentFixtureIndex - lastPostMatchReactionMatchday >= PostMatchReactionMinGapMatchdays;

            if (!isNotable && !gapElapsed)
            {
                CheckFormStreakMessages();
                return;
            }

            lastPostMatchReactionMatchday = currentFixtureIndex;

            string title;
            string body;

            if (margin >= 3)
            {
                title = "Excellent Performance";
                body = $"That was an excellent result against {opponentName}. The players looked confident and the scoreline will give the dressing room a real lift. Let's make sure we build on it rather than treating it as a one-off.";
            }
            else if (margin > 0)
            {
                title = "Good Result";
                body = $"Congratulations on the result against {opponentName}. The performance has helped strengthen our position in the league table and should give the squad confidence going into the next fixture. Keep the standards high.";
            }
            else if (margin == 0)
            {
                title = "Points Shared";
                body = $"The draw against {opponentName} leaves us with mixed feelings. There were positives in the performance, but also moments where the match could have slipped away. There is still room to improve.";
            }
            else if (margin > -3)
            {
                title = "Disappointing Result";
                body = $"The result against {opponentName} was disappointing. Setbacks are part of a long season, but we expect a response in the next match. Consistency will be important if we are to meet our objectives.";
            }
            else
            {
                title = "Performance Concerns";
                body = $"The defeat to {opponentName} has raised concerns. It was not simply the result, but the manner of the performance that disappointed us. We expect you to review the tactical approach, squad selection, and mentality ahead of the next fixture.";
            }

            inbox.Add(InboxMessageType.PostMatchReaction, title, body, careerCalendar.CurrentDayNumber);

            CheckFormStreakMessages();
        }

        // Tier 1 potentialemails.txt batch, #11-12 (session 14) - fires once when the
        // managed team's recent-form strip (see recentFormByTeamId/GetRecentFormString,
        // last 5 results) first reaches a 3-result streak, not on every single match
        // still inside that streak - a flag per direction, reset the moment the streak
        // breaks, keeps this to one message per streak rather than one per match.
        private const int FormStreakLength = 3;
        private bool poorRunMessageSentForCurrentStreak;
        private bool strongRunMessageSentForCurrentStreak;

        private void CheckFormStreakMessages()
        {
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            if (!recentFormByTeamId.TryGetValue(managedTeamId, out List<char> history) || history.Count < FormStreakLength)
            {
                return;
            }

            bool allLossesRecently = true;
            bool allWinsRecently = true;
            for (int i = history.Count - FormStreakLength; i < history.Count; i++)
            {
                if (history[i] != 'L') allLossesRecently = false;
                if (history[i] != 'W') allWinsRecently = false;
            }

            if (allLossesRecently)
            {
                if (!poorRunMessageSentForCurrentStreak)
                {
                    poorRunMessageSentForCurrentStreak = true;
                    inbox.Add(InboxMessageType.FormStreak, "Recent Form",
                        "Recent results have not met expectations. The board still supports your work, but we need to see signs of improvement soon. The squad has enough quality to be more competitive than recent performances suggest.",
                        careerCalendar.CurrentDayNumber);
                }
            }
            else
            {
                poorRunMessageSentForCurrentStreak = false;
            }

            if (allWinsRecently)
            {
                if (!strongRunMessageSentForCurrentStreak)
                {
                    strongRunMessageSentForCurrentStreak = true;
                    inbox.Add(InboxMessageType.FormStreak, "Momentum Building",
                        "The squad is starting to build momentum. Recent performances have improved confidence around the club, and the league table is beginning to reflect that. The challenge now is maintaining standards when the fixture list becomes more difficult.",
                        careerCalendar.CurrentDayNumber);
                }
            }
            else
            {
                strongRunMessageSentForCurrentStreak = false;
            }
        }

        // Remaining Tier 1 potentialemails.txt triggers that only make sense checked
        // once per matchday tick rather than at a specific single call site - mid-season
        // review (#27), low-stamina warning (#18), and injury recovery (playtest
        // backlog item, paired with the injury message TryRollInjury sends directly).
        // Called from both places currentFixtureIndex actually advances (the Simulate
        // Season loop and OnFullTimeContinueClicked), same as every other per-matchday
        // resolver above (ResolveDueBids etc.).
        private bool midSeasonReviewSentForCurrentSeason;
        private const float LowStaminaWarningThreshold = 60f;
        private const int LowStaminaWarningCooldownMatchdays = 5;
        private int lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
        private readonly HashSet<PlayerAgent> injuredPlayersTracked = new();

        private void ResolveMatchdayInboxTicks()
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team))
            {
                return;
            }

            // Mid-season review (#27) - fires once, the first matchday the season is at
            // least half played out. Reset alongside every other season-scoped flag in
            // OnStartNewSeasonClicked.
            if (!midSeasonReviewSentForCurrentSeason && managedTeamFixtures.Count > 0 &&
                currentFixtureIndex >= managedTeamFixtures.Count / 2)
            {
                midSeasonReviewSentForCurrentSeason = true;
                inbox.Add(InboxMessageType.MidSeasonReview, "Mid-Season Review",
                    "We have reached the midpoint of the season. The board has reviewed our league position, recent form, and overall squad performance. There is still time to improve, but the second half of the campaign will be important. Continue to make decisions that serve the long-term interests of the club.",
                    careerCalendar.CurrentDayNumber);
            }

            // Low-stamina warning (#18) - cooldown-gated so a squad that stays fatigued
            // for a long stretch doesn't get the same warning every single matchday.
            if (currentFixtureIndex - lastLowStaminaWarningMatchday >= LowStaminaWarningCooldownMatchdays)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                bool anyLowStamina = false;
                foreach (PlayerAgent player in team.Players)
                {
                    if (roles.GetCondition(player) < LowStaminaWarningThreshold)
                    {
                        anyLowStamina = true;
                        break;
                    }
                }

                if (anyLowStamina)
                {
                    lastLowStaminaWarningMatchday = currentFixtureIndex;
                    inbox.Add(InboxMessageType.LowStamina, "Fitness Concern",
                        "A few players are showing signs of fatigue. Heavy minutes can reduce sharpness late in matches, especially for players with lower stamina. Rotating the squad or using substitutions earlier may help avoid performance drops.",
                        careerCalendar.CurrentDayNumber);
                }
            }

            // Injury recovery (playtest backlog) - diffs the tracked injured set against
            // ManagerSquadRoles.IsInjured (a threshold check, not an event) to catch
            // whoever's return matchday just passed.
            if (injuredPlayersTracked.Count > 0)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                List<PlayerAgent> recovered = null;

                foreach (PlayerAgent player in injuredPlayersTracked)
                {
                    if (!roles.IsInjured(player, careerCalendar.CurrentDayNumber))
                    {
                        recovered ??= new List<PlayerAgent>();
                        recovered.Add(player);
                    }
                }

                if (recovered != null)
                {
                    foreach (PlayerAgent player in recovered)
                    {
                        injuredPlayersTracked.Remove(player);
                        inbox.Add(InboxMessageType.Recovery, $"{player.Name} Fit Again",
                            $"{player.Name} has recovered from injury and is available for selection again.",
                            careerCalendar.CurrentDayNumber);
                    }
                }
            }
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

        // Morale (session 10 - Thomas: doesn't affect performance, affects development
        // instead - see ManagerSquadRoles.ApplyPostMatchMorale/GetMoraleGrowthMultiplier).
        // Deliberately loops the WHOLE squad (team.Players, StartingEleven + Bench), not
        // just playedThisMatch like the form bonus above - a benched player's morale
        // needs to react to being overlooked, which means iterating players who did NOT
        // play, not just the ones who did.
        private void ApplyMatchMoraleForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
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
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            foreach (PlayerAgent player in managedTeam.Players)
            {
                roles.ApplyPostMatchMorale(player, playedThisMatch.Contains(player), outcome);
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
        private AgentMatchSimulator.AgentMatchResult SimulateFixture(OpenFootballMatch fixture, bool isAutoResolved = false)
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
            // isAutoResolved (backlog item 10) - during a SIMULATE SEASON skip, lean on
            // team-strength alone rather than feeding possibly-stale, un-recoverable
            // Condition into this match's fit-adjusted strength - see
            // ApplyMatchdayConditionAndInjuries's own comment for the full reasoning.
            Func<PlayerAgent, float> homeConditionLookup = (fixture.HomeTeam == managedTeamName && !isAutoResolved)
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p))
                : null;
            Func<PlayerAgent, float> awayConditionLookup = (fixture.AwayTeam == managedTeamName && !isAutoResolved)
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
                ApplyMatchdayConditionAndInjuries(homeTeam, isAutoResolved);
                ApplyMatchdayAcademyProgression();
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ApplyMatchdayConditionAndInjuries(awayTeam, isAutoResolved);
                ApplyMatchdayAcademyProgression();
            }

            ManagerPlayerDerivedStrength.Profile homeProfile = ManagerPlayerDerivedStrength.Calculate(
                fitAdjustedHomeTeam, squadGenerator.GetStartingPositions(fitAdjustedHomeTeam.Formation));
            ManagerPlayerDerivedStrength.Profile awayProfile = ManagerPlayerDerivedStrength.Calculate(
                fitAdjustedAwayTeam, squadGenerator.GetStartingPositions(fitAdjustedAwayTeam.Formation));
            ManagerPlayerDerivedStrength.MatchupPrediction prediction =
                ManagerPlayerDerivedStrength.PredictMatchup(homeProfile, awayProfile);

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

            // Session 16 - squad roles (captain/vice/penalty/FK/corner takers) are
            // cosmetic-only now (Thomas's explicit scope call), so neither the captaincy
            // expected-goals modifier nor the corner-taker name wiring runs anymore.
            // ManagerCaptaincyModifier/CornerTakerNamesByTeamName are left in place
            // (unused) rather than deleted, in case this gets revisited later.
            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;

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
                if (!roles.IsInjured(starter, careerCalendar.CurrentDayNumber))
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
                if (roles.IsInjured(candidate, careerCalendar.CurrentDayNumber))
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

        // Same best-fit search as FindFitBenchReplacement, minus the injury filter - AI
        // clubs have no ManagerSquadRoles/injury tracking at all (that's a managed-team-
        // only system), so there's nothing to skip. Used only for backfilling an AI
        // club's XI after a transfer sale (session 16, see OnSignPlayerClicked).
        private PlayerAgent FindBestFitBenchPlayer(AgentTeam team, PlayerPosition neededPosition)
        {
            PlayerAgent best = null;
            float bestFit = -1f;

            foreach (PlayerAgent candidate in team.Bench)
            {
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
                    team.Reserves.Remove(player);
                }
                else
                {
                    team.StartingEleven.Remove(player);
                }
            }
            else
            {
                team.Bench.Remove(player);
                team.Reserves.Remove(player);
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

        // isAutoResolved (backlog item 10, session 11) - Thomas's real test: won his
        // first 3 individually-played matches with Liverpool, hit SIMULATE SEASON for
        // the rest, finished 15th. Root cause traced here: Condition decay and injury
        // rolls compound every auto-resolved match with zero manager mitigation (no
        // rest, no rotation, no tactical response - none of that is even possible
        // during a skip), and SimulateFixture's homeConditionLookup/awayConditionLookup
        // then feeds that same un-recovered fatigue into THIS match's fit-adjusted
        // strength, producing a genuine unrealistic performance spiral, not just a
        // cosmetic number. Only Condition/injury are gated here - development
        // (RecordAppearance, ApplyMatchdayProgression) stays unconditional, since a
        // skipped season should still let players grow normally; morale/form are
        // deliberately left alone too (see OnSimulateSeasonClicked's own comment on
        // why - they only ever affect development speed, never match performance, so
        // neutralizing them wouldn't touch the actual collapse symptom at all).
        private void ApplyMatchdayConditionAndInjuries(AgentTeam team, bool isAutoResolved = false)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            List<PlayerAgent> fullSquad = new List<PlayerAgent>(team.StartingEleven);
            fullSquad.AddRange(team.Bench);

            foreach (PlayerAgent player in fullSquad)
            {
                float minutesPlayed = ComputeMinutesPlayed(player, team);
                bool played = minutesPlayed > 0f;
                float preMatchCondition = roles.GetCondition(player);

                if (!isAutoResolved)
                {
                    roles.ApplyPostMatchCondition(player, minutesPlayed, player.Age, player.Stamina);
                }

                if (played)
                {
                    roles.RecordAppearance(player);

                    if (!isAutoResolved)
                    {
                        TryRollInjury(roles, player, preMatchCondition);
                    }
                }

                // Per-matchday development tick (session 9 backlog item) - same hook
                // Condition already uses, same played/not-played signal computed above.
                // Whole squad, not just starters - a benched player still ticks (at the
                // 0.7x floor rate), same as the old season-lump version's playing-time
                // floor. Deliberately still the binary `played` flag here, not
                // minutesPlayed - growth ticks were never the reported issue, only
                // Condition was, so left unchanged to keep this fix minimal. Morale
                // multiplier (session 10) rides along on this same call.
                ManagerPlayerDevelopment.ApplyMatchdayProgression(player, played, roles.GetMoraleGrowthMultiplier(player));
            }
        }

        // Academy growth moved off the once-a-season lump sum (session 16 - Thomas:
        // "do our youth players stats only move after the year, and not necessarily
        // real time? My GK hasn't changed at all in my academy at matchday 22" - a real
        // design gap confirmed by investigation, not a bug: academy had no per-matchday
        // hook at all before this). Mirrors ApplyMatchdayConditionAndInjuries's call
        // site exactly - fires once per matchday, alongside the managed team's own
        // tick, not once per fixture (academy isn't tied to a specific match).
        // playedThisMatchday: true for every tick, standing in for "always training" -
        // academy prospects don't play senior matches to have a real played/not-played
        // signal, and full-intensity coaching every matchday is close enough to the old
        // AssumedPlayingTimeFactorAcademyProspect (0.8) lump-sum pace without adding a
        // second continuous-factor overload just for this one caller. Focus stats
        // (session 10) still ride along exactly as they did in the old season-end call.
        private void ApplyMatchdayAcademyProgression()
        {
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                ManagerPlayerDevelopment.ApplyMatchdayProgression(player, playedThisMatchday: true, focusAttributes: academy.GetFocusAttributes(player));
            }
        }

        private void CloseAcademyIntakeDropdown()
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
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

            int durationWeeks = Mathf.Clamp(Mathf.RoundToInt((UnityEngine.Random.Range(1f, 6f) + UnityEngine.Random.Range(1f, 6f)) / 2f), 1, 8);
            int durationDays = durationWeeks * 7;
            roles.SetInjured(player, careerCalendar.CurrentDayNumber + durationDays);
            injuredPlayersTracked.Add(player);

            // Playtest backlog item (session 14) - injury Inbox message. Recovery is
            // handled separately (see ResolveMatchdayInboxTicks) since there's no single
            // call site for "a player's return matchday just passed" - it's a threshold
            // crossed silently by IsInjured, not a discrete event like this roll is.
            inbox.Add(InboxMessageType.Injury, $"Injury: {player.Name}",
                $"{player.Name} has picked up an injury and is expected to be out for approximately {durationWeeks} week{(durationWeeks == 1 ? "" : "s")}.",
                careerCalendar.CurrentDayNumber);
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
            waitingAtHalfTime = false;
            if (halfTimePanel != null) halfTimePanel.SetActive(false);
            CapturePreMatchTeamSheet();
            currentMatchMinute = 0;
            liveHomeGoalsSoFar = 0;
            liveAwayGoalsSoFar = 0;
            matchSubsLog.Clear();
            playersSubbedOffThisMatch.Clear();
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

                    // Live ratings (session 10) - ApplyEvent silently no-ops for any name
                    // not seeded into this match's tracked set (i.e. every opponent
                    // player), so it's safe to call for every event regardless of which
                    // side was attacking. A mid-match resimulation (TriggerMidMatchResimulation)
                    // mutates this same result.Events list in place, so replayed/regenerated
                    // tail events flow through this exact loop and get rated normally -
                    // no special-casing needed.
                    matchRatings.ApplyEvent(matchEvent);
                    RefreshMatchRatingsGrid();
                }

                // Ambient drift (session 11, backlog item 7) - Thomas: a player sitting at
                // the same rating for a full 90 minutes reads as broken, not calm. Every 5
                // match-minutes regardless of whether an event happened this minute, so a
                // player who's on the pitch but never directly involved in a discrete
                // chance still shows some natural movement. See ManagerMatchRatings.
                // ApplyAmbientTick's own comment for the tuning reasoning.
                if (minute % 5 == 0)
                {
                    bool managedTeamIsHome = currentFixture.HomeTeam == managedTeamName;

                    int managedGoalsSoFar = managedTeamIsHome ? liveHomeGoalsSoFar : liveAwayGoalsSoFar;
                    int opponentGoalsSoFar = managedTeamIsHome ? liveAwayGoalsSoFar : liveHomeGoalsSoFar;

                    int managedShotsSoFar = managedTeamIsHome ? homeShots : awayShots;
                    int opponentShotsSoFar = managedTeamIsHome ? awayShots : homeShots;

                    matchRatings.ApplyAmbientTick();

                    matchRatings.ApplyTeamPerformanceTick(
                        managedGoalsSoFar,
                        opponentGoalsSoFar,
                        managedShotsSoFar,
                        opponentShotsSoFar
                    );

                    RefreshMatchRatingsGrid();
                }

                if (minute == 45 && !skipToResultsRequested)
                {
                    ShowHalfTimePanel(homeShots, awayShots, homeShotsOnTarget, awayShotsOnTarget, homeAttackEvents, awayAttackEvents);
                    while (waitingAtHalfTime)
                    {
                        yield return null;
                    }
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
            RestorePreMatchTeamSheet();
            ApplyFixtureResult(currentFixture, lastSimulatedResult);

            currentFixtureIndex++;
            ResolveMatchdayInboxTicks();
            ResolveNextMatchOnlyOverride();

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

        private void CapturePreMatchTeamSheet()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            preMatchFormation = team.Formation;
            preMatchStartingEleven = new List<PlayerAgent>(team.StartingEleven);
            preMatchBench = new List<PlayerAgent>(team.Bench);
            preMatchReserves = new List<PlayerAgent>(team.Reserves);
        }

        private void RestorePreMatchTeamSheet()
        {
            if (preMatchStartingEleven == null) return;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            team.Formation = preMatchFormation;
            team.StartingEleven = new List<PlayerAgent>(preMatchStartingEleven);
            team.Bench = new List<PlayerAgent>(preMatchBench);
            team.Reserves = new List<PlayerAgent>(preMatchReserves);
            foreach (PlayerAgent player in team.Players)
            {
                player.IsStartingEleven = team.StartingEleven.Contains(player);
            }

            preMatchStartingEleven = null;
            preMatchBench = null;
            preMatchReserves = null;
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
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = 25f;

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

        private void InitializeWorldGenerationService()
        {
            try
            {
                TextAsset historyAsset = Resources.Load<TextAsset>("World/football_world_history");
                TextAsset registryAsset = Resources.Load<TextAsset>("World/football_club_registry");
                if (historyAsset == null || registryAsset == null)
                {
                    Debug.LogWarning("World generation data is unavailable; fresh careers will use the legacy bootstrap.");
                    worldGenerationService = null;
                    return;
                }
                worldGenerationService = new WorldClubGenerationService(
                    FootballClubRegistry.FromTextAsset(registryAsset),
                    FootballWorldHistory.FromTextAsset(historyAsset));
            }
            catch (Exception exception)
            {
                worldGenerationService = null;
                Debug.LogError($"World generation data failed to load; using legacy bootstrap. {exception.Message}");
            }
        }

        private bool TryGetWorldTarget(string teamName, out SquadQualityTarget target)
        {
            if (worldGenerationService != null &&
                worldGenerationService.TryGetSquadQualityTarget("eng", teamName, out _, out target))
            {
                return true;
            }
            target = default;
            return false;
        }

        private float GetWorldLeagueMeanOverall()
        {
            if (worldLeagueMeanOverall > 0f) return worldLeagueMeanOverall;
            float total = 0f;
            int count = 0;
            foreach (string teamName in availableTeamNames)
            {
                if (!TryGetWorldTarget(teamName, out SquadQualityTarget target)) continue;
                total += target.FirstTeamOverall;
                count++;
            }
            worldLeagueMeanOverall = count > 0 ? total / count : 79.5f;
            if (count > 0)
            {
                foreach (string teamName in availableTeamNames)
                {
                    if (!TryGetWorldTarget(teamName, out SquadQualityTarget target)) continue;
                    worldLeagueMaxPositiveDelta = Mathf.Max(worldLeagueMaxPositiveDelta, target.FirstTeamOverall - worldLeagueMeanOverall);
                }
            }
            return worldLeagueMeanOverall;
        }

        private void ConfigureInitialWorldStrength(string teamName, float firstTeamOverall)
        {
            // Player quality remains the source of truth. These factors translate the
            // generated league-relative quality gap into the xG prior consumed by the
            // existing match simulator; reputation and historical results never enter.
            const float ratingToLogStrength = 0.24f;
            float delta = firstTeamOverall - GetWorldLeagueMeanOverall();
            if (delta > 0f && worldLeagueMaxPositiveDelta > 0f)
            {
                // A concave positive curve keeps the best club fixed while avoiding a
                // cliff immediately below the elite. Mid/high-table clubs remain
                // ordered by player quality but are not treated as relegation-level
                // opposition simply because they trail a capped historical outlier.
                delta = Mathf.Sqrt(delta / worldLeagueMaxPositiveDelta) * worldLeagueMaxPositiveDelta;
            }
            float attack = Mathf.Exp(delta * ratingToLogStrength);
            float defence = Mathf.Exp(-delta * ratingToLogStrength);
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
            strength.AttackStrength = attack;
            strength.DefenceStrength = defence;
            originalAttackStrengthByTeam[teamName] = attack;
            originalDefenceStrengthByTeam[teamName] = defence;
        }

        private AgentTeam GetOrCreateAgentTeam(string teamName)
        {
            if (squadsByTeamName.TryGetValue(teamName, out AgentTeam existingTeam))
            {
                return existingTeam;
            }

            AgentTeam newTeam;
            if (usesWorldGeneration && TryGetWorldTarget(teamName, out SquadQualityTarget target))
            {
                newTeam = squadGenerator.GenerateSquad(teamName, target);
                ConfigureInitialWorldStrength(teamName, target.FirstTeamOverall);
            }
            else
            {
                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
                newTeam = squadGenerator.GenerateSquad(teamName, strength.AttackStrength, strength.DefenceStrength);
            }
            ApplyDeveloperEasterEggPlayer(newTeam);

            squadsByTeamName[teamName] = newTeam;

            // Live team strength (session 16) baseline - captured once, the very first
            // time this team's squad exists, before anything ever mutates it. See
            // RecalculateLiveTeamStrength for how this gets used every season rollover.
            baselineAverageOverallByTeam[teamName] = GetAverageOverall(newTeam);

            return newTeam;
        }

        // Live team strength (session 16) - Thomas: "team strength to be live... City
        // will just always win most seasons no matter what, but if they have player
        // decline... or if they lose the player, their performance should reflect that."
        // Manager Mode's own statisticalModel instance is completely separate from
        // Research Mode's (each instantiates its own, see ResearchEvaluationRunner.cs) -
        // mutating TeamStrength here never touches the trained historical baseline
        // Research Mode's own evaluation runs depend on.
        //
        // Driven by squad average Overall vs. the baseline captured at generation time
        // (Thomas's explicit choice over a transfers-only signal) - one number that
        // already reflects transfers in/out, retirements, and the aging/growth/decline
        // every AI first-team player gets via ApplySeasonProgression every season,
        // without needing separate bookkeeping for each cause. Recalculated from the
        // ORIGINAL baseline every time, not compounded onto last season's already-
        // adjusted value, so this can't drift or double-count across many seasons - it's
        // always "how different is this squad from where it started," full stop.
        // Clamped to 0.6x-1.5x - the sale-guard rules (WouldLeaveSquadTooThin) already
        // keep a squad from being hollowed out entirely, but the clamp is a second,
        // independent backstop against a pathological swing feeding back into
        // ApplyRetirementsForTeam's replacement generation (which reads this same
        // TeamStrength) and compounding.
        //
        // DefenceStrength is inverted (see feedback_defencestrength_inverted in memory -
        // lower DefenceStrength means fewer goals conceded, i.e. a BETTER defence), so a
        // stronger squad DIVIDES it rather than multiplying, same fix already applied to
        // the reserve-pool discount and confirmed live there.
        private const float LiveStrengthMinRatio = 0.6f;
        private const float LiveStrengthMaxRatio = 1.5f;

        private void RecalculateLiveTeamStrength(string teamName, AgentTeam team)
        {
            if (!baselineAverageOverallByTeam.TryGetValue(teamName, out float baselineAverage) || baselineAverage <= 0f)
            {
                return;
            }

            float currentAverage = GetAverageOverall(team);
            float ratio = Mathf.Clamp(currentAverage / baselineAverage, LiveStrengthMinRatio, LiveStrengthMaxRatio);

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
            strength.AttackStrength = originalAttackStrengthByTeam[teamName] * ratio;
            strength.DefenceStrength = originalDefenceStrengthByTeam[teamName] / ratio;
        }

        private static float GetAverageOverall(AgentTeam team)
        {
            if (team.Players.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            foreach (PlayerAgent player in team.Players) total += player.GetOverallRating();
            return total / team.Players.Count;
        }

        private List<PlayerAgent> GetOrCreateReservePool(string teamName)
        {
            return GetOrCreateAgentTeam(teamName).Reserves;
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

            GetOrCreateAgentTeam(teamName).PromoteReserveToBench(best);

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
        // returns, overwriting only Name/Age/Height/nationality on one already-generated
        // player - attributes/Overall are whatever normal generation rolled, untouched
        // (Thomas, session 16: "stats can be randomized just like before, but everything
        // else is fixed"). Three more friends added this session alongside Hidde -
        // Liverpool gets two (a CB and a DM), Tottenham gets one.
        private void ApplyDeveloperEasterEggPlayer(AgentTeam team)
        {
            if (team == null)
            {
                return;
            }

            switch (team.TeamName)
            {
                case "Arsenal":
                    ApplyEasterEggIdentity(team, PlayerPosition.ST, "Hidde Rietberg", 25, 183f, "Netherlands");

                    BoostStrikerEasterEgg(team, "Hidde Rietberg");
                    break;

                case "Liverpool":
                    ApplyEasterEggIdentity(team, PlayerPosition.CB, "Thomas Bernards", 25, 200f, "Germany");
                    ApplyEasterEggIdentity(team, PlayerPosition.DM, "Charles Herring", 25, 175f, "England");

                    BoostDefensiveMidfielderEasterEgg(team, "Charles Herring");
                    break;

                case "Tottenham Hotspur":
                    ApplyEasterEggIdentity(team, PlayerPosition.ST, "Victor Hamberg", 26, 195f, "Sweden");
                    

                    BoostStrikerEasterEgg(team, "Victor Hamberg");
                    break;
            }
        }

        private void ApplyEasterEggIdentity(
            AgentTeam team,
            PlayerPosition position,
            string name,
            int age,
            float height,
            string nationName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent target = team.StartingEleven.Find(p => p.PrimaryPosition == position)
                ?? team.Bench.Find(p => p.PrimaryPosition == position);

            if (target == null)
            {
                return;
            }

            target.Name = name;
            target.Age = age;
            target.Height = height;
            ManagerPlayerNationality.SetNationality(
                target,
                new ManagerPlayerNationality.Nation(nationName, "Western Europe")
            );
        }

        private void BoostStrikerEasterEgg(AgentTeam team, string playerName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent player = team.Players.Find(p => p.Name == playerName);

            if (player == null)
            {
                return;
            }

            player.Finishing = Mathf.Max(player.Finishing, 88f);
            player.Pace = Mathf.Max(player.Pace, 85f);
            player.Dribbling = Mathf.Max(player.Dribbling, 84f);
            player.Composure = Mathf.Max(player.Composure, 82f);
            player.Positioning = Mathf.Max(player.Positioning, 81f);
            player.Heading = Mathf.Max(player.Heading, 87f);
            player.Strength = Mathf.Max(player.Strength, 78f);
            player.Aerial = Mathf.Max(player.Aerial, 82f);

            // These bespoke clamps were authored against the original attribute set;
            // rebuild the detailed profile so they remain real strengths under v2.
            player.AttributeSchemaVersion = 0;
            PlayerAttributeModel.EnsureCurrent(player);


            // Make sure the boost does not leave him with no development room.
            player.Potential = Mathf.Max(player.Potential, player.GetOverallRating() + 3f);
        }

        private void BoostDefensiveMidfielderEasterEgg(AgentTeam team, string playerName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent player = team.Players.Find(p => p.Name == playerName);

            if (player == null)
            {
                return;
            }

            player.Passing = Mathf.Max(player.Passing, 82f);
            player.Positioning = Mathf.Max(player.Positioning, 82f);
            player.Composure = Mathf.Max(player.Composure, 81f);
            player.Defending = Mathf.Max(player.Defending, 81f);
            player.Tackling = Mathf.Max(player.Tackling, 81f);
            player.Marking = Mathf.Max(player.Marking, 80f);
            player.Stamina = Mathf.Max(player.Stamina, 83f);
            player.Strength = Mathf.Max(player.Strength, 76f);
            player.ThroughBalls = Mathf.Max(player.ThroughBalls, 78f);
            player.LongShots = Mathf.Max(player.LongShots, 81f);
            player.Dribbling = Mathf.Max(player.Dribbling, 85f);
            player.Pace = Mathf.Max(player.Pace, 77f);
            player.FreeKicks = Mathf.Max(player.FreeKicks, 89f);

            player.AttributeSchemaVersion = 0;
            PlayerAttributeModel.EnsureCurrent(player);

            // Make sure the boost does not leave him with no development room.
            player.Potential = Mathf.Max(player.Potential, player.GetOverallRating() + 3f);
        }
    }
}
