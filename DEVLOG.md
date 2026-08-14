# Development Log

A running, append-only journal of Manager Mode development sessions, kept for
MSc Major Project documentation purposes. Unlike `HANDOFF.md` (which is
overwritten each session to hand off *current* state to the next one), this
file accumulates — new entries go at the top.

---

## 2026-08-10 — Elite aging curve, an autopilot backlog sweep (loans/academy/scouting rework/nationalities), and two real live-caught bugs

**Commits:**
`0d81295` (2026-08-10 21:01:33 +0100) — "feat: elite aging curve, autopilot
batch (loans/academy/scouting rework/nationalities), and live bug fixes"
`46518d9` (2026-08-10 21:01:51 +0100) — "docs: add session 9 handoff"

### Goal
Continuation of the same day's career-arc session — Thomas asked to work
through the open Manager Mode backlog "one by one," then later handed off
autopilot authorization for a batch of features while away, and the
session closed with a live pairing stretch where he caught two genuine
bugs in real time by watching the numbers closely rather than trusting
the UI.

### Elite aging curve
Pushback on the original decline model: "I feel like players, or at
least superstars should at least remain stagnant for a few years... I'd
say harry kane has only gotten better since turning 30." Added an
aging-curve extension keyed off a player's current Overall (his own
reasoning — Potential can now go down too, so Overall is the more
honest signal) — high-rated players get up to five extra years before
decline starts, replacing the flat age-30 cutoff everywhere it was used.
Verified afterward with a full goals-per-match realism sanity check
against real Premier League rates.

### Transfer/Scouting name-click detail view
"We need to be able to click on their name to see detailed stats"
instead of buying/scouting blind. Added a name-click target to the
shared grid-row component, independent of the row's own click, and
generalized Player Detail to browse an arbitrary read-only list. A real
bug reported live the same day — clicking a youth prospect's name did
nothing until Back was pressed — turned out to be `OpenPlayerInspect`
never hiding the Scouting/Transfer Market panels underneath it, so the
detail view opened invisibly beneath them.

### Form column bug, and a genuine misunderstanding cleared up
A screenshot showed a stale Form strip at Season 2. Real cause:
`recentFormByTeamId` was never cleared alongside the league table on
season rollover or load — fixed at both sites. Also clarified that the
"promotion and relegation" Thomas thought he'd spotted (a newly-promoted
club with no training data) is just the season-file-cycling mechanic
from the career arc, not an actual football pyramid.

### Autopilot batch: loans, world-scattered scouting + nationalities, youth academy
Given explicit go-ahead to proceed without further check-ins on
everything except live in-match ratings, morale, and the inbox. Shipped:
a loan system (any squad player, automatic destination, free, fixed to
season-end); a rework of scouted prospects from being cosmetically tied
to a real club (which buying them never actually affected) to a
region-pooled system with a real generated nationality per prospect,
plus a per-career randomized regional quality bias — deliberately
randomized rather than fixed, to avoid a permanent claim about which
real nations produce better talent, a design choice not yet discussed
with Thomas directly; and a youth academy (5 slots, ages 14-15,
promotion age 16, manual promote-to-reserves), built as a second tab
inside the existing Scouting screen. Two real save/load data-loss bugs
were caught proactively before considering either feature done: a
loaned-out player is removed from the squad entirely, so the save DTO
would have silently lost them forever without a dedicated list; and
restoring an empty, never-generated academy pool would have permanently
frozen it at zero without a count guard. Also bell-curved the
Potential-roll headroom (a flat roll was making very high Potential far
too common) and fixed a real `DefenceStrength` inversion bug in both the
reserve pool and youth pool discount math, where "weakening" a player
was actually making their defense better.

### Design import: injury cross icon
Thomas linked a Claude Design mockup project and asked for its red
medical-cross sprite to be added to injured players on the Tactics Board
and Squad list. Read the mockup read-only via the project's design-sync
tool, then hand-translated the SVG spec into the game's existing
flat-rectangle UI convention (two crossed rectangles over a solid
square) rather than importing an image asset. Verified live end-to-end
with a real in-game injury: looped matchdays until three starters
actually got hurt, confirmed the icon on both screens, then pushed
matchdays past their return dates and confirmed it correctly cleared —
which also proves those players are genuinely selectable again, since
the icon and the actual match-day substitution logic read the exact
same underlying check.

### Two real bugs caught live
**Substitute fatigue.** "I switched them out and the borders remained
yellow, meaning the subbed on players have low stamina." Not cosmetic —
the fatigue formula judged every player by the absolute match clock
with no concept of when a substitute actually entered, so a player
brought on at minute 88 was penalized as if they'd played the whole
match, in both the display and the underlying chance-creation math.
Fixed by tracking each substitute's real entry minute and computing
fatigue off time actually on the pitch; verified with a direct
before/after comparison (a fresh sub reads fully rested at the same
match minute an unmodified starter reads as tired).

**Hub matchday label frozen at "Matchday 1."** Thomas noticed the
league table showing every team 21 games played while the header still
read Matchday 1. Genuinely useful bug to catch, and the first fix
attempt was wrong — a suspected coroutine race was removed and replaced
with a simpler synchronous fix, which compiled clean but made no
difference on a full clean re-test. A temporary diagnostic log found the
real cause: the label was cached as a controller field, and a separate,
general-purpose TMP recovery routine (used all over this screen to fix
an unrelated rare rendering glitch) had silently destroyed and recreated
that exact label at some point without knowing to update the cached
reference — leaving it permanently `null` and silently skipping every
future update. Fixed by not caching the label at all, looking it up
fresh by path on every refresh instead. Verified with 15 matchdays
played back-to-back, checking the label after every single one against
the league table.

### Discussed, not yet implemented
Confirmed there's currently no expiry or rival-club poaching for
unbought scouted prospects — leaning toward an age-out-and-replace
mechanic over a fake AI transfer economy, not decided. Confirmed
injured players can still be freely selected into the Tactics Board
lineup with no warning (they just get silently swapped out at kickoff);
a real block or warning is still open. Confirmed Condition genuinely
persists matchday-to-matchday but has no visible number anywhere until
it crosses a low-fitness warning threshold — floated always showing the
raw number instead. All three logged to the backlog, not built this
session.

---

## 2026-08-10 — Career arc (progression/scouting/transfers/finance/incentives/save-load), a real faux-bold font bug, and a UI text-size pass

**Commits:**
`e3d4da8` (2026-08-10 13:46:44 +0100) — "feat: career arc - progression,
scouting, transfers, finance, incentives, save/load"
`3610f37` (2026-08-10 13:47:04 +0100) — "fix: bold UI text was faux-bold -
add a real Oswald Bold font asset"
`01c991b` (2026-08-10 14:34:09 +0100) — "fix: bump small/hard-to-read text
sizes across several screens"

### Goal
Thomas described what excites him most about management games — earning
money to buy players who fit his system, discovering wonderkids, watching
them develop over many seasons, and a real incentive to win the league —
and asked for all of it in one sitting, explicitly not worrying about
scope. Delivered as a six-phase plan, each phase verified live in Play
Mode before the next started. What followed was a long live-debugging arc
chasing a text-crispness report that turned out to be a genuine,
comprehensive font bug, plus a smaller UI text-size pass.

### The career arc, six phases
- **Season loop** (previously nonexistent — fixtures just ran out and
  buttons disabled forever): a season counter, an End of Season panel,
  and a real rollover (ages every player, reloads a new real fixture
  calendar by cycling through the 9 historical Premier League season
  files already in the project). Added `PlayerId` and `Potential` as new
  inert fields on the protected `PlayerAgent.cs`, generated inside the
  existing RNG-safe wrapped block, verified with a same-seed
  regeneration check.
- **Player progression** toward the new hidden Potential — youth grow
  (decelerating as they approach it), veterans decline, retirement
  replaces via the existing reserve-generation wrapper.
- **Youth prospect scouting** — a hidden per-club pool of 16-19-year-olds,
  invisible until scouted (fuzzy Potential range until a scout
  assignment resolves).
- **Transfer market and club finance** — live Wage/MarketValue formulas,
  a real per-club budget, single-shot bidding, sell-from-bench-only
  selling.
- **Season incentives** — prize money and a separate board confidence
  budget boost, plus a Trophy Room history screen.
- **Full save/load** — wired the existing (previously non-functional)
  SAVE & EXIT TO TITLE and LOAD CAREER buttons to real persistence,
  verified across an actual Play Mode exit and fresh re-entry.

### Problems found and fixed along the way
A genuine formula bug in player progression: the first version spread
growth across roughly a dozen attributes at once, which barely moved the
position-weighted Overall rating since it's a weighted average, not a
sum — a tracked player only gained +0.8 Overall over 7 simulated seasons.
Fixed to apply the growth amount directly to the attributes that
actually carry weight; verified afterward with a realistic multi-season
deceleration curve. Also fixed: two brand-new buttons (Scouting, Trophy
Room) rendered with no label text at all, since the styling helper used
only updates an existing label rather than creating one; three status
labels (scouting assignment count, transfer budget, transfer status
message) silently froze after their first refresh, the same TMP
cached-label-reference gotcha hit twice in earlier sessions; a
pre-existing missing-glyph warning on the Tactics screen's role
dropdown; and a genuinely dead unused-variable warning, unrelated to
this session's own work.

### The font investigation
Thomas reported the Title screen's text looking blurry. Ruling this out
took a long back-and-forth: Canvas Scaler fractional scaling from a
non-16:9 Game View, Windows display scaling (checked, was already
100%), and a "baked texture upscaled" theory raised by a design
collaborator were all investigated and ruled out with direct evidence
(Camera target texture, Canvas render mode, RawImage usage, render
pipeline) before finding the real cause — `Oswald SDF.asset` had been
baked from a single weight of a variable font with no true bold face,
so every bold-styled label in the game was rendering through TMP's
synthetic weight simulation instead of real glyphs. Fixed by sourcing
the genuine static Oswald Bold font file, baking a matching font asset,
and linking it into the existing font's weight table so the fix applies
automatically everywhere bold text appears — verified across 246
live-checked labels covering every major screen. A mistake in the first
attempt at creating the new font asset (its atlas texture wasn't
persisted as a proper sub-asset, so it worked in-session but crashed on
the next Play Mode transition) was caught by testing through an actual
transition rather than trusting the first success.

### Text size pass
After the font fix, several elements turned out to be just genuinely too
small rather than blurry — the Team Select screen's header/subtitle/
caption, club grid names, match stat captions and values, and the
Matchday Prep opponent pitch's player labels. Caught a real regression
while verifying this: growing the pitch pin's circle size alongside its
font tipped two closely-spaced pins into visual overlap in some
formations, confirmed via direct bounding-box checks rather than
eyeballing — fixed by keeping the pin's footprint the same size and only
growing the text inside it.

### Discussed, not yet implemented
A live design discussion afterward identified a real bug in the
Potential-roll formula (a flat random roll instead of the project's
established bell-curve convention, making very high potential far too
common) and floated a proper youth academy system (separate from
scouting, including under-16 players who can't yet be promoted),
reworking scouted prospects to be unaffiliated with any specific club,
and moving player development from a once-a-season lump sum to visible
per-matchday increments tied to match form. None of this was
implemented this session — captured in the handoff and project memory
for whenever it's picked up.

---

## 2026-08-09 — Captaincy/set-piece roles, an attribute overhaul, the manager-influence arc (Leadership+captaincy, fitness/injuries), and a new Tactics screen

**Commits:**
`2b280ef` (2026-08-09 20:38:11 +0100) — "feat: Manager Mode captaincy, set-piece
taker, and attack/defend role designations"
`c9a2bed` (2026-08-09 20:52:48 +0100) — "fix: restrict attack/defend role
options by position"
`28f2808` (2026-08-09 21:19:19 +0100) — "feat: attribute overhaul - Long
Shots, Through Balls, Off The Ball, Marking, Free Kicks"
`316e129` (2026-08-09 21:36:20 +0100) — "feat: Leadership stat + real
captaincy consequence"
`66b863c` (2026-08-09 21:56:32 +0100) — "feat: phase 2 - fitness/condition,
injuries, and reserve pool"
`bc422b8` (2026-08-09 22:08:40 +0100) — "fix: Matchday Prep opponent pitch
elongating past the footer on window resize"
`6aac85e` (2026-08-09 22:30:50 +0100) — "feat: tactical sliders + Tactics
screen (sliders + centralized role assignment)"
`884aedb` (2026-08-09 22:54:19 +0100) — "fix: Tactics screen column spacing,
dropdown z-order/population, GK Leadership + role stat context"
`dfb0d14` (2026-08-09 23:04:28 +0100) — "fix: align dropdown option stat
columns into a real grid"
`a6a541c` (2026-08-09 23:10:44 +0100) — "fix: match Tactics screen layout to
the actual design mockup exactly"
`c490cb5` (2026-08-09 23:14:53 +0100) — "docs: add session 7 handoff"

### Goal
An unusually large session that kept growing thread by thread: captaincy and
set-piece taker designations, a full attribute overhaul (the first time the
protected `PlayerAgent.cs`/`AgentSquadGenerator.cs` were touched directly,
under explicit authorization), a two-phase "manager influence" push aimed at
making the mode feel less like a spectator sport, a real pre-existing UI bug
found live mid-playtest, and finally tactical sliders plus a brand-new
centralized Tactics screen built from a live design mockup.

### Captaincy, set-piece taker, and attack/defend role designations
New Manager Mode-only `ManagerSquadRoles` side structure (Captain,
Vice-Captain, Penalty/Free-Kick/Corner taker, per-player attack/defend
leaning) — deliberately kept off `PlayerAgent.cs` itself. Only the corner
taker got a real mechanical effect this session: the ManagerSim fork's
`PickCreatorForChance` prefers a team's designated corner taker (85% of the
time, if on the pitch) for `SetPiece` chances, so their real Crossing/
Creativity drives the outcome through the existing formula. Everything else
started organizational-only (no free-kick/penalty event exists in the sim to
hook into). Follow-up fix: attack/defend role options are now restricted by
position — no "Defensive" wingers, no "Attacking" centre-backs, no control
at all for goalkeepers.

### Attribute overhaul
Five new stats — Long Shots, Through Balls, Off The Ball, Marking, Free
Kicks — each chosen because it fills a real half-generic term already
sitting in an existing ManagerSim formula, not just added for its own sake.
First time this session's protected files were touched directly, under
explicit branch-scoped authorization ("we have a branch dedicated to all the
untouchable files... I give you full editor access"). `PlayerAgent.cs`
gained only new fields; `AgentSquadGenerator.cs` generates the new stats in
a single pass wrapped in a `Random.State` save/restore so the addition
consumes zero budget from the shared RNG stream — confirmed via same-seed
regeneration producing byte-identical existing stats across independent
generator instances. A 200-match calibration check confirmed no regression.

### Manager influence arc, phase 1 — Leadership + captaincy consequence
Agreed framing going in: existing manager levers didn't have real teeth
because nothing cost anything. New `Leadership` stat (flat across
positions, with a veteran/youth age nudge) feeds a new downside-only
captaincy-suitability penalty on expected goals — a sensible pick costs
nothing, a reckless one (young, low Leadership) genuinely hurts, up to -12%
at the extreme.

### Manager influence arc, phase 2 — fitness/condition, injuries, reserve pool
Built in the agreed order: the reserve-pool safety net first, then
persistent Condition, then injuries — so the risk (a squad thinned out by
injuries) never existed without the safety net already in place. Condition
decays with minutes played and Stamina, recovers with Age, and feeds match
performance as a second multiplier stacked on the existing position-fit
clone. Extending that clone for Condition surfaced a real bonus bug: it had
never been updated with this session's earlier new stats, so a managed-team
player playing out of position was silently using 0 for all of them —
fixed as part of the same edit. Injury risk scales sharply with low
pre-match Condition and modestly with Age, verified via 5000-trial
statistical reproduction of the formula. The squad can never actually be
left unable to field a position — a still-injured starter is auto-swapped
for the best fit bench cover, or a reserve is called up, before every
managed-team fixture.

### A real, unrelated UI bug found live mid-playtest
The user hit this playing his own career (Matchday 2 vs Newcastle), not
something introduced this session. The opponent-formation pitch on Matchday
Prep used a point anchor with its height snapshotted once, at chrome-build
time, from the container's measured height — correct for whatever window
size was active the very first time the screen was ever shown, silently
wrong after any resize for the rest of that session. The exact same drift
bug class as one already partially fixed (on a different axis) in an
earlier session. Fixed with proper stretch anchors.

### Tactical sliders + a new Tactics screen
Width/Defensive Depth/Tempo sliders — deliberately not another flat xG
multiplier (redundant with the existing Mentality system). `PickChanceType`,
the single entry point deciding which of six chance types happens for every
attack in the match, was refactored from six fixed if/else threshold chains
into an explicit weighted table with an optional bias multiplier; unbiased,
it reproduces the exact original odds. Mid-build, the user shared a design
mockup for a dedicated "TACTICS" screen and decided to move role assignment
off Player Detail onto it entirely — which also resolved a backlogged
"two corner takers" idea as a side effect. Several real bugs surfaced from
live feedback on the actual built screen and were fixed the same session:
columns reading as "far apart," dropdown lists rendering garbled or with no
names at all (two separate causes — sibling render order, and TMP labels
built while inactive failing mesh generation permanently), goalkeeper
Leadership never being displayed, dropdown stat columns not actually
aligning, and a final pass pulling the exact layout spec from the user's
Claude Design project via DesignSync instead of guessing proportions again.

### Problems encountered
- **A genuine compile bug blocked Play Mode for a while.** A bare
  `Random.value`/`Random.Range` call in the new injury code was ambiguous
  between `System.Random` and `UnityEngine.Random` (the file imports both
  namespaces) — a real `CS0104` error, not a tooling quirk. Confusing
  because trivial compile-check scripts kept succeeding (they never
  referenced the broken code path), and `EditorApplication.isPlaying = true`
  reports success even when Unity can't actually enter Play Mode. Traced
  down by checking the actual Editor console output directly rather than
  trusting isolated script compiles. Fixed by fully-qualifying
  `UnityEngine.Random` everywhere in that file.
- **Two more real UI bugs on the new Tactics screen's dropdowns**, found
  from a live screenshot: dropdowns nested inside their own trigger button
  always rendered behind later rows regardless of being "open" (Unity draws
  UI children in sibling order); and option buttons built while their
  panel was still inactive sometimes failed to render any text at all (a
  known class of TMP mesh-generation failure, not previously seen in this
  exact form). Both fixed the same session.
- Several rounds of the live verification tooling reporting stale or
  misleading compile errors after large edits — resolved each time by
  waiting for a genuine recompile and rechecking actual console output
  rather than trusting the first response.

---

## 2026-08-09 — Player Detail fixes, a match-viewing UX overhaul, a real calibration bug, and two real UI bugs

**Commits:**
`1ce92d1` (2026-08-09 19:11:43 +0100) — "feat: Manager Mode overhaul - Player
Detail/GK fixes, Full Time redesign, match-viewing UX, calibration fix,
matchday banner bug fix"
`7839796` (2026-08-09 19:13:24 +0100) — "docs: add session 6 handoff"

### Goal
Started with a small carried-over fix (GK stats on Player Detail), then spent
most of the session on a match-viewing UX thread that kept growing as the
user reacted to live screenshots and actual playtesting — Match Log text,
replay pacing, and three successive redesigns of the Full Time screen. Along
the way, caught and fixed a real goal-scoring calibration regression, shipped
a small developer easter egg, and finished by fixing two real bugs the user
found in live play.

### Player Detail / GK stats
- Player Detail's four attribute columns showed the same outfield-only
  layout for every player, including goalkeepers, whose real generated
  stats (`Goalkeeping`, `Reflexes`) never appeared anywhere. Made the column
  set conditional on `PrimaryPosition == PlayerPosition.GK`.
- The attribute columns were vertically *centered*, so columns with
  different row counts had their titles land at different heights —
  imported the original "PLAYER DETAIL" mockup from the user's Claude
  Design project and switched to top-aligned columns to match it.
- Photo box grown from 140px to 220px square (two rounds of "make it
  bigger" feedback), with the surrounding header layout adjusted to fit.

### Match Log, pacing, and the Full Time screen (the big thread)
- **Match Log phrasing**: diagnosed the repetition as structural — every
  event resolves to one of 6 chance types × 4 outcomes, 24 fixed templates
  total. Added 3 phrasing variants per slot (with short, punchy lines mixed
  into the frequent stopped/off-target events), then later added
  fatigue-aware and score-state-aware ("late drama") variants driven by
  real simulated values already being computed, not decorative randomness.
- **Replay pacing**: bumped `matchReplayDurationSeconds` 45→60 after the
  user found the faster pace hard to follow; confirmed as the right amount
  later in the session after watching several matches.
- **Full Time screen, three redesign passes** driven by live screenshots:
  goal scorer lists moved off tiny centered labels into their own big
  block with Match Stats moved to the right; then split into two side-by-
  side columns instead of stacked; then, on the user's own idea, the goal
  timeline moved out of its cramped strip into a large full-width band
  below everything, with minute labels on each marker.
- **Managed-team-relative red/green coloring** applied throughout (scorer
  names, timeline markers, match-stat bars) — based on which side the
  user's own club is on, not simply home/away, verified specifically on an
  away fixture to make sure that distinction actually held.
- **Real per-line dividers** added to the live event feed to match the
  original mockup, converting a single multi-line text block into a proper
  row-based list.
- **Match stat bars made into real comparisons** — both the live and
  full-time versions were previously not actually comparing the two teams
  (one was hardcoded to always show full, the other was proportional but
  single-color). Added a genuine two-color split bar to both.

### A real goal-scoring calibration bug, caught and fixed
While chasing a match where both teams scored (to test the new coloring),
repeatedly hit suspiciously low-scoring matches. A proper batch comparison
(200 matches, identical teams, Manager Mode's simulator vs. the protected
research one) confirmed a real regression: an earlier on/off-target split
had stacked a second probability filter in front of the existing goal roll,
roughly halving the effective scoring rate (1.21 goals/match vs. the
protected original's 2.82). Fixed by rescaling the goal-chance formula to
represent "given the shot is already on target" instead of leaving it
unconditional under the new gate — restored to 2.66 goals/match, closely
matching the original's 2.68 on the same teams. Added as a standing rule
for future changes to that part of the code: any change to the match
simulator's scoring probabilities needs a real before/after goals-per-match
check, not just a clean compile.

### Developer easter egg
Added one fixed player — name, age, height, and position set, stats
generated normally like everyone else — on the user's own club, with a
real portrait. Implemented so it only affects the game-facing squad
generation, not the shared generator research evaluation also uses.

### Two real bugs found and fixed
- **Goal event text was duplicating and over-coloring**: the event feed
  already labels every goal with a fixed prefix, but the goal-text variants
  added earlier in the session also said "goal" inside the description
  itself, producing a visible duplicate — and the whole line was being
  colored green instead of just the prefix. Rewrote the variants to drop
  the word entirely and fixed the coloring to only apply to the prefix.
- **Matchday Prep screen banner stuck on the very first fixture**: the
  header text never updated after the first match, while the squad list
  and opponent info right below it (fed by the same refresh call) updated
  correctly every time — a strong sign the underlying data was fine and
  this was a rendering bug. Root cause was a text-label component silently
  getting swapped out from under a cached reference the first time a
  recovery routine ran on it (the same class of bug already hit once
  before, in an earlier session, on a different screen). Fixed the same
  way as before, and verified end-to-end across two real matchdays that
  the banner now updates correctly.

### Smaller fixes
- Expanded the surname pool (81 → 183) after the user noticed two
  same-surname players on one squad — checked the actual math first and
  confirmed the small pool made a shared surname the *expected* outcome
  within a squad, not a rare coincidence.

### Problems encountered
- **A destructive mistake, caught and recovered.** The first attempt at
  cropping the easter-egg player's portrait overwrote the original source
  image in place with no backup, and came out too tight (no shoulders
  visible). Found an untouched copy in the user's own Downloads folder and
  re-cropped from that instead, this time leaving the backup alone.
- **Mid-session hot-reload proved unreliable.** A script change that
  compiled cleanly didn't always take effect in an already-running session,
  even after an earlier change in the very same session had worked live
  without a restart — cost real time before landing on "just restart to be
  sure" as the reliable verification method.
- **Wall-clock timing across separate tool calls was not trustworthy** for
  precision checks (verifying the new 60s replay pacing) — inherent
  round-trip latency between calls meant elapsed-time math came out wrong;
  had to fall back on the user's own real-time feel-check instead.
- The live-verification sandbox used this session has no access to
  reflection, so private game state had to be exercised through real
  public entry points (clicking actual buttons, simulating a real user)
  rather than reached into directly.

### Backlog captured, not implemented
Tactical shape (formation-vs-formation interaction) remains queued as its
own future discussion; player progression and a transfer market/finance
system remain larger, uncommitted roadmap ideas, alongside a newly-floated
scoped-down "free agent market only" version of the latter; a set-piece
taker designation and a per-player attack/defend role toggle remain
floated, smaller ideas; more developer easter-egg players are welcome any
time now that the pattern is established.

---

## 2026-08-09 — Player realism, New Career redesign, and a manager-influence push

**Commits:**
`1159def` (2026-08-09 10:39:54 +0100) — "docs: add session 4 handoff (live
verification pass on 1920x1080 redesign)"
`a277fc1` (2026-08-09 15:27:05 +0100) — "feat: player realism, New Career
redesign, manager influence pass"

### Goal
Picked up from the previous session's handoff (a long live-verification pass
on the 1920x1080 redesign), committed that session's own pending handoff doc,
then moved through four broadly different chunks of work across the rest of
the session: player-generation realism, a New Career screen redesign, a
"manager influence" design conversation that grew into several shipped
features, and a live playtesting bug-fix round at the end.

### Player generation realism
- Added `Age`/`Height` to `PlayerAgent` — height generated per-position via a
  bell curve with a hard 150–200cm wall (not a uniform clamp), age via a
  bell-curve roll, both coupled into existing attributes (height nudges
  Aerial/Strength/Pace, age nudges Composure/Positioning/Pace/Stamina).
- Expanded the name pool (30/30 → ~84/81) and fixed a real bug alongside it:
  `usedNames` was a HashSet local to `GenerateSquad`, so duplicate names
  were already possible *across* teams even before the pool was small —
  promoted to an instance field so dedup is genuinely league-wide.
- Converted every attribute roll (`ApplyBaseAttributes` and the ten
  `Generate<Position>` methods) from a flat `Random.Range` to a bell curve
  centered on the old range, and raised ~18 unrealistically low "dump stat"
  floors (a striker's Defending could roll as low as 5) — together these
  make rare outliers possible while eliminating the single-digit stats that
  prompted the complaint in the first place. Verified across ~6000 sampled
  attributes: only 0.05% fell below 10.

### New Career screen redesign
- Split into a real 2-step wizard (manager name, then club selection) —
  manager name is now actually required (Continue disabled until non-empty),
  the club grid stretches to the full content width once it isn't sharing
  the row with a name column, and the subtitle correctly reads "Step 1 of 2"
  /"Step 2 of 2" instead of a hardcoded "Step 1 of 1".
- Fixed a TMP mesh-regen bug found along the way: the step subtitle wasn't
  updating on step 2 because the existing blank-label recovery sweep can
  silently swap out a label's `TextMeshProUGUI` component, orphaning a
  cached reference to it — fixed by caching the parent GameObject and doing
  a fresh `GetComponentInChildren` lookup each time instead.

### Manager influence
A genuine back-and-forth discussion (per the user's own request, established
in an earlier session, to discuss before building) that produced several
shipped features:
- **Formation fit-penalty** (`ManagerFormationFit.cs`, new) — formation was
  confirmed genuinely cosmetic before this (`Formation` never appeared in
  `AgentMatchSimulator.SimulateMatch` at all). Now builds a throwaway
  fit-penalized clone of each team's XI before every simulated match, so
  playing someone out of position has a real mechanical cost, without
  touching the protected simulator or the real squad data.
- **Tactics Board mismatch flagging**, refined twice: first a binary
  red/normal flag on the slot's position label, then — after the user
  played with it — changed to show the player's *true* position in the
  warning color instead of the slot's (so a misplaced ST at DM shows red
  "ST", not red "DM"), plus a new lenient orange tier (half penalty, fit
  0.80) for positions "adjacent" to a player's primary even without an
  actually-rolled secondary (e.g. an LW at LM). The adjacency map mirrors
  the same position-family relationships the squad generator's own
  secondary-position rolls already use. Because the fit-penalty system reads
  the same underlying value, extending the tiers automatically flowed
  through to the real gameplay penalty too.
- **A new `ManagerSim` fork of the match simulator** (`Assets/Scripts/
  ManagerSim/AgentMatchSimulator.cs`) — created at the user's own suggestion
  ("make research duplicates... then you have free reign") after a "more
  match stats, without fabricating" discussion concluded an honest Shots on
  Target stat needed a real on/off-target split the protected simulator
  can't take. The fork shadows the protected original via same-namespace C#
  resolution, so no call sites needed to change; verified live with a
  temporary marker string that it's genuinely what Manager Mode runs, then
  removed.
- **New match stats** (Possession%, Chances Created, Shots on Target), all
  derived from real sim data — shipped in both the full-time report and the
  live in-match ticker.
- **A live stamina condition indicator** on Tactics Board pins (green/amber/
  red border tint, reading the sim's real fatigue formula).
- **"Tactic" renamed to "Mentality"** throughout (the user's own framing),
  and made genuinely live mid-match — the three mentality buttons were
  already visible/clickable during a match but a code comment admitted they
  were inert ("scaffolded... only affect the next match"); a mid-match
  change now actually reruns the rest of that match via the same
  resimulation path substitutions use.

### Live playtesting bug-fix round
The user played the app and reported four issues in one message, all
investigated and fixed the same session:
- **Scroll direction** (Match Events, Squad List) — root-caused via
  simulated scroll events to `Scrollbar.direction = TopToBottom` being
  mismatched with `ScrollRect`'s own convention (a different bug from the
  wheel-sensitivity issue a previous session already fixed and verified).
  Fixed in three places, including the Tactics Board bench rail, which had
  the same mistake even though it wasn't reported.
- **Pin-to-pin dragging** — pins could be dropped on but not dragged
  themselves, so a formation change that scattered two players' slots
  couldn't be undone by dragging one back. Added a new `AgentTeam.
  SwapStartingPositions` and made pins draggable.
- **Match Log readability** — font 15→19, added line spacing.
- **Stamina border staying stale after full-time** — confirmed a real bug:
  the match-minute counter only resets at the *next* match's kickoff, so
  the Tactics Board kept reading late-match fatigue from the previous match
  until a new one started.

### Problems encountered
- **A real ordering bug caught before it shipped.** The first attempt at
  gating the live-mentality-change logic inferred "is a match currently
  live" from panel visibility — which turned out to already read as "live"
  during a *new* match's own setup (before per-match state had actually
  reset), which would have misfired. Replaced with an explicit boolean flag
  set only for the true duration of the match replay coroutine — the same
  flag then turned out to also fix the separate stamina-staleness bug found
  later in the session.
- **Reading a freshly-built scrollbar's handle position via script is
  unreliable within a single synchronous call.** Directly setting
  `ScrollRect.verticalNormalizedPosition` doesn't synchronously recompute
  the linked `Scrollbar` handle's rendered size on a screen that hasn't run
  its own Update cycle yet; had to split verification into separate calls
  across a real frame boundary to get a trustworthy before/after reading.
- `OpenFootballMatch` turned out to be a struct, not a class — an
  `== null` guard written against it was a compile error, not a runtime
  bug; caught immediately by the compiler.
- Chrome-build-once (screens built once per Play Mode session) meant a
  scrollbar-direction fix needed a Play Mode restart to actually become
  visible, even though the underlying code change had already compiled
  clean — confirmed with the user before restarting, since there was
  visible in-progress career state on screen at the time.

### Backlog captured, not implemented
GK stats not shown on Player Detail (the screen shows only irrelevant
outfield attributes for a goalkeeper, never `Goalkeeping`/`Reflexes`,
explicitly deferred to after this session's handoff); tactical shape
(formation-vs-formation interaction) remains queued as its own future
discussion; player progression and a transfer market/finance system remain
larger, uncommitted roadmap ideas; a set-piece taker designation and a
per-player attack/defend role toggle were floated as smaller manager-
influence ideas but not committed to.

---

## 2026-08-08 — 1920x1080 redesign live-verification pass, screen by screen

**Commits:** `b3fccdb` (2026-08-08 22:58:02 +0100) — "fix: Manager Mode UI
polish pass - scaling, alignment, and a real position bug"

### Goal
Live-verify the 1920x1080 Manager Mode redesign screen by screen in Play
Mode, fixing whatever the user found on each pass. Ran as a long back-and-
forth loop: enter Play Mode, click through, user reports something off
(often with a screenshot), fix, restart Play Mode, re-verify.

### Fixed this session
- **Hub league table sizing.** First attempt only changed the C# field
  defaults on `LeagueTableView` (`rowHeight`/`headerRowHeight`/`fontSize`)
  and had zero effect — the scene file had its own baked serialized values
  overriding them. Fixed by editing the `.unity` scene data directly
  (28→48, 22→32, 13→20). Also center-aligned the PL/GD/PTS columns (were
  right-aligned with a dead gap) and wrapped the FORM column string in
  `<mspace=1.4em>` so W/D/L letters space evenly despite being different
  glyph widths.
- **GK/CB pin overlap on the three back-three formations** (3-5-2, 3-4-3,
  3-4-2-1). Reverted the GK pin back to the source mockup's own 0.90 (was
  compensated to 0.95 for the old, smaller canvas) and nudged the center
  CB from 0.80 to 0.74 depth to keep clear of it.
- **Player Detail banner** — three rounds. Centering the stat columns just
  moved the empty-space gap rather than closing it; grew the banner itself
  (130→240px, bigger photo/name/meta) to actually fill it; then removed a
  leftover centered-margin on the banner that the user caught was still
  narrower than the rest of the screen, making it full-bleed.
- **Weak-foot star alignment** — three failed attempts eyeballing
  `<voffset>` from screenshots (-0.15em, -0.06em, -0.02em, all reported
  still off). Solved for real by querying `TMP_TextInfo.characterInfo`
  directly for a reference letter vs. the star sprite's actual bounding
  box and computing the exact required offset (0.29em) — the star artwork
  itself sits well below its own reported baseline, so baseline-matching
  was never going to work no matter how carefully eyeballed.
- **Match Events / Squad List scroll direction** — flip-flopped across
  three rounds (default → `-1` → back to `+1`) because early reports were
  against stale Play Mode sessions that predated whichever fix had just
  landed (screens are built once per session and never rebuilt). Settled
  for good by simulating a real wheel event via `ExecuteEvents.Execute`
  and reading `verticalNormalizedPosition` before/after — proved `+1`
  (Unity's default) is correct.
- **Matchday Prep "pitch behind the list"** — reported as recurring a
  third time after two earlier (wrong) fixes aimed at z-order and a ghost
  object. Measured with `RectTransform.GetWorldCorners()` and found a real
  ~149-unit overlap at the user's actual (non-maximized, non-16:9) window
  size. Root cause: `CanvasScaler`'s effective canvas width only equals
  the 1920 reference at an exact 16:9 aspect ratio — the pitch was
  positioned using a literal `1920f` while the list was already positioned
  as an offset from the right anchor (aspect-independent by construction).
  Re-anchored the pitch the same way and derived its height from the
  container's measured `.rect.height` instead of a literal `1080f`.
  Verified a clean +27.75 gap at the same window size that previously
  showed the overlap.
- **Pitch markings near-invisible in a non-maximized window** — 1px-wide
  line images scale to sub-pixel width below the reference resolution and
  anti-alias away, worse at their already-low opacity. Bumped to 2px.
- Formation dropdown misalignment, Matchday Prep opponent-list background
  color, Match Day header overflow (plus a second, separately-stale copy
  of the same header-height constant in the post-match stats reset path),
  team names going blank at Full-Time (a new trigger case for the known
  TMP mesh-generation-failure bug — this time from a `fontSize` change on
  an already-rendered label, not creation), and Full-Time goal-scorer list
  overflowing the header on 3+ goal matches.

### Backlog captured, not implemented
Five items explicitly deferred by the user and saved to memory rather than
built this session: a larger name pool for generated players (900 possible
combinations for ~380 generated players), surfacing stamina in Manager
Mode's UI (the sim already uses it, nothing shows it), requiring a manager
name before Team Select can continue, a red position-mismatch label on the
Tactics Board when a dragged player is out of position, and a larger,
explicitly-uncommitted set of roadmap ideas (player progression over time,
a transfer market and the finance system it implies, and giving the
manager more real tactical influence over the squad rather than just XI
selection).

### Problems encountered
- **Chrome-build-once meant several "still broken" reports weren't actual
  fix failures.** Every screen's UI is built exactly once per Play Mode
  session (guarded by a bool), so neither code fixes nor scene-file edits
  take effect in an already-running session — only a fresh Play Mode entry
  picks them up. At least the scroll-direction flip-flopping (see above)
  burned real time on this before the pattern was recognized.
- **No save/load system exists yet**, so a Play Mode restart discards all
  season progress. Mid-session the user was 39 matchdays into a season
  when this was discovered — held off restarting Play Mode until they'd
  stopped it themselves.
- The "score stuck at 0-0 at Full-Time despite correct stats/scorers"
  issue investigated earlier in the session was never conclusively root-
  caused; concluded it was most likely a rapid-automated-testing artifact
  rather than a real bug, flagged as such rather than claimed fixed, and
  the user didn't hit it again for the rest of the session.

---

## 2026-08-07 — Design-fidelity pass on the Tactics Board + a full live-testing bug-fixing round

**Commits:** `18dad22`

### Goal
Pick up from the same-day Tactics Board session's handoff and bring the new
screens in line with the actual Claude Design mockups (pulled live from the
`Unity UX design possibilities` project), then clear whatever live play-
testing turned up on top of that — this ended up running through two full
rounds of "fix it, then go play it again."

### Design fidelity (matched against the Claude Design mockups)
- **Tactics Board pitch** was stretched full-width, smearing every formation
  into an unreadable wide strip. Constrained to a fixed 1130:700 aspect
  ratio — the exact ratio used in the design's own "TACTICS BOARD — DETAIL"
  board — centered with letterboxed margins either side.
- Raised the pin `verticalCompression` factor (0.66 → 0.85): GK and CB pins
  were visually overlapping in every back-three formation (3-5-2, 3-4-2-1).
  Verified clean across every formation afterward.
- Bench caption and Match Events list both got a scrollbar. Neither list
  was actually broken — both already scrolled fine via mouse wheel/drag —
  but with zero visual affordance either one read as "missing content"
  rather than "scroll for more."
- Full-Time Summary header spacing reworked: score/team names were
  crowding the top of the panel, goal-scorer names were crowding the
  header/body divider from below. Stats block was pinned near the top of
  its available space leaving a large dead gap before the footer; now
  vertically centered to match the mockup.
- League table's GF/GA columns replaced with a single GD (goal difference)
  column — the two-column version was getting clipped by the table's own
  scrollbar. Manager-Mode display-only change; `LeagueTable.Entry`'s
  `GoalsFor`/`GoalsAgainst` fields (which Research Mode's evaluation output
  reads) are untouched, GD is computed locally in `LeagueTableView.cs`.
- Player Inspect attribute bars now show their numeric value alongside the
  bar (right-aligned, colour-matched) — reverses an earlier documented
  decision to keep raw numbers off attribute rows.
- The match screen's tactic-pill buttons (Attacking/Balanced/Defensive)
  turned out to be hand-placed Editor buttons surviving from before the
  code-driven reskin, never routed through the styling helpers — rendered
  top-left-aligned and non-bold, visibly different from every other button
  (Pause vs. Skip to Results had the same mismatch). Fixed at the root by
  extending the shared `NormalizeButtonLabel`/`StyleHubActionButton`
  helpers to also force alignment and font weight, not just colour/size.
- Imported the designer's three PNGs (football icon, filled/empty star) as
  TMP Sprite Assets, wired into goal-scorer lines and Player Inspect's
  weak-foot rating.

### Live-testing round 1
- **Match screen laid out correctly on matchday 1, corrupted from matchday
  2 onward.** The full-time-only stats-panel repositioning code mutated
  shared RectTransforms in place rather than rebuilding them, and nothing
  ever reset them back to the live layout (or re-hid the full-time-only
  scorer lists, or re-showed the Match Log) before the next live match
  started. Fixed with an explicit reset routine called at the top of the
  "simulate match" handler.
- **Pausing, then requesting a substitution, did nothing until Resume was
  pressed** — the picker only ever popped open the instant the game
  unpaused. `Time.timeScale = 0` freezes any `WaitForSeconds`-based
  coroutine solid, and the replay coroutine only checked the "sub
  requested" flag once per simulated minute. Replaced the per-minute wait
  with a per-frame poll that can notice a paused request immediately.
- Hub byline text ("Manager X · Matchday N") started rendering visibly
  garbled/overlapping after the first matchday. First attempt (destroy and
  recreate the label a frame later, cancelling any previous in-flight
  attempt first) cut it down a lot but didn't eliminate it — see Problems
  below, this turned out not to be what it looked like.

### Live-testing round 2 (after round 1's fixes landed)
- Match Events scroll wheel felt backwards (had to scroll down to reach
  the *first* events) — negated the ScrollRect's `scrollSensitivity`.
- Skip to Results had the exact same paused-coroutine bug as the
  substitution picker, just never got the same fix — added the matching
  early-exit condition.
- Weak-foot star icons in Player Inspect were sitting flush against the
  "Weak Foot:" label with no gap and slightly off the text baseline —
  added spacing and a small `<voffset>` correction.
- Hub byline overlap, actually fixed this time — see Problems below.

### Problems encountered
- **A compile error silently blocked every Play Mode entry attempt for a
  long stretch, and looked nothing like a compile error while it was
  happening.** `TextAlignmentOptions.MidlineCenter` doesn't exist (the
  correct value is `.Center`) — Unity refuses to enter Play Mode with a
  broken build, but gives no obvious "stuck" signal for it, so repeated
  `EditorApplication.isPlaying = true` calls just silently never took
  effect. Diagnosed by finally checking the Console for compile errors
  instead of continuing to assume it was a tooling/environment problem.
- **That stuck window had a lasting side effect**: some navigation calls
  made while Play Mode silently wasn't running executed against the *Edit
  Mode* scene instead (confirmed by a `"Destroy may not be called from
  edit mode!"` console error at the time). Object creation in Edit Mode is
  permanent — it survives every later Play Mode stop/restart, unlike
  normal play-created objects. This produced two separate pieces of
  invisible debris: duplicate League Table header rows (found and cleaned
  up immediately), and — unnoticed at the time, because only the League
  Table was checked — a second, permanently stray "Byline" GameObject
  under the Hub panel, frozen forever at "Matchday 1". Every fresh Play
  session then legitimately built a *second*, correctly-updating Byline
  alongside the abandoned first one, which is what the "garbled overlap"
  text actually was — two real, simultaneously-existing GameObjects, not
  a rendering artifact. Traced conclusively by adding a temporary
  `Debug.LogError` with a full stack trace to the suspect builder method:
  it only ever fired once per session, which meant the duplicate had to
  already exist *before* Play Mode even started — checked the Edit Mode
  scene directly and found it sitting there with stale runtime text
  baked in. Removed it, verified clean across several fresh matchdays
  afterward.
  - **Lesson for next time**: `"Destroy may not be called from edit
    mode"` or a stale-looking warning like `"no fixtures found for X"`
    regardless of what was actually clicked is the signature of code
    running in Edit Mode when Play Mode was meant to be active — check
    for a compile error first. Any object creation from that window needs
    a full, deliberate scene-wide sweep to clean up, not just the one
    symptom that happened to surface first.
- TMP `<sprite>` tags have no `size=` attribute — `<sprite name="x"
  size=60%>` doesn't error, it silently fails to parse and prints the tag
  text literally. `<size=60%><sprite name="x"></size>` is the real syntax.
- Manually constructing a `TMP_SpriteAsset` via `ScriptableObject
  .CreateInstance` + hand-built glyph/character tables threw a
  `NullReferenceException` inside TMP's own migration path the moment any
  table property was touched on the fresh instance. Unity's own "Create >
  TextMeshPro > Sprite Asset" menu item, invoked via
  `EditorApplication.ExecuteMenuItem` with `Selection.activeObject` set to
  the source texture, worked cleanly first try.
- A fresh `RectTransform`'s default `sizeDelta` is `(100,100)` — under
  stretched anchors this *adds* 100px to the computed size rather than
  being ignored. Hit this building the bench scrollbar's handle (rendered
  as a huge block covering the whole row), then proactively avoided it on
  the Match Events scrollbar.

### Deferred
- A more sophisticated squad/stat generator was discussed at length but
  explicitly parked for a future session: team strength barely
  differentiates generated squads today (`AgentSquadGenerator` only blends
  35% of the way toward a club's real attack/defence rating), individual
  attributes are rolled fully independently with nothing tying a player's
  stats together, the name pool collides across the whole league (only
  deduplicated within a team), and there's no age/potential/development
  arc at all.
- Condensed, minimalistic (FM-style) match-event text — the same
  deferred item from the previous session's entry, raised again and
  parked again. Still not started.
- A portrait-orientation (1080×1920) mockup redo was announced mid-session
  but not yet delivered — next session's first priority once it lands.

### State at session end
Commit `18dad22` pushed (the user pushed directly this time rather than
via GitHub Desktop, an explicit one-off ahead of a phase transition).
Everything in this entry was live-verified in the Editor before commit —
unlike the previous two sessions, nothing was left in a "compiles clean
but not yet re-checked" state.

---

## 2026-08-07 — Tactics Board (drag-to-sub, formation switching) + live-evaluation bug hunt

**Commits:** `cd99991`, `d666b66`

### Goal
Pick up where the previous same-day session's handoff left off (live-verify
its pending fixes), then build the pitch-view Tactics Board that was scoped
as the next piece of Manager Mode work — replacing the Squad screen's plain
scrollview with position-pinned starters, a draggable bench, and formation
switching.

### Verification pass
All four items pending from the prior handoff (Match Day right-column pivot,
Tactic-button reparenting, Title screen logo, resuming the interrupted
playthrough) were confirmed working live. Also bumped the bench from 7 to 9
players (`AgentSquadGenerator.GetBenchPositions`) to match current real-world
Premier League matchday-squad rules — confirmed inert for Research Mode,
since `AgentMatchSimulator` only ever reads `.StartingEleven`, never `.Bench`.

### Tactics Board
Replaces the old scrollview-based Squad screen entirely:
- New files `TacticsBoardLayout.cs` (per-formation pin coordinates, taken
  from the project's Claude Design mockup) and `TacticsBoardPlayerCard.cs`
  (the drag/drop/tap component).
- Pitch view with the starting XI rendered as position-pinned tokens,
  draggable bench row below, and a formation-switch dropdown driven by a
  greedy best-fit reassignment (`rating x positionFit` per slot).
- `AgentTeam.SubstitutePlayer` fixed to preserve slot order (previously
  broke it via remove+append) so the board can reliably track which pin a
  substituted-in player belongs to.
- `AgentSquadGenerator.GetStartingPositions` made public; added the missing
  `ThreeFourTwoOne` shape (previously silently fell back to 4-2-3-1's -
  harmless until formation-switching existed, since no team was ever
  auto-assigned that formation).
- Retired the old click-based pre-match "Make Subs" flow, found sitting
  permanently off-screen (`y=-880`) — dead since an earlier reparenting bug
  nobody had noticed because nothing ever pointed a visible button at it.
- Matchday Prep's opponent scouting list was also retired at the user's
  direction, standing empty until the opposition's own tactics board view
  is built.

### Live-evaluation bug hunt
A full playthrough pass surfaced several more issues, all fixed in the same
session:
- Full-time goal-scorer boxes were geometrically overlapping regardless of
  content (260px wide, only 240px apart) — a pre-existing bug from the
  previous session's redesign that simply hadn't been exposed by a wide
  enough scoreline until now. Narrowed to 220px, stress-tested with a 3-goal
  one-sided result afterward.
- Tactics Board pin spacing was badly cramped — the source mockup's pin
  percentages assumed a much taller pitch region than this landscape
  960x540 canvas has room for. Added a vertical-compression factor, shrunk
  the pin/badge footprint, and added pitch markings (halfway line, both
  penalty boxes, all flat rectangles - no sprite assets in this project) to
  give the formation shape some visual structure back.
- A drag-and-drop substitution could leave its drag "ghost" frozen on
  screen after a successful drop. Root cause: the drop triggers a full
  board rebuild that destroys the dragged card's GameObject; `Destroy()`
  makes a `UnityEngine.Object` compare `== null` immediately even though
  actual destruction is deferred to end of frame, so the EventSystem's
  later `OnEndDrag` call saw a "null" source and silently skipped the
  cleanup it would otherwise have done. Fixed by having the drop handler
  clean up the dragged card's ghost itself, synchronously, before
  triggering the rebuild.
- A real drag gesture could also fire the card's click handler, opening
  Player Inspect mid-drag. Added an explicit `isDragging` guard, plus a
  defensive sweep that clears any stray ghost on leaving the Tactics Board
  or opening Player Inspect regardless of cause.
- Player Inspect's "OVERALL" rating number could render completely blank
  despite the label being structurally correct in every other respect
  (active, right text, right colour, right position). Same underlying bug
  as a Title-wordmark fix from the previous session
  (`TextMeshProUGUI.textInfo.characterCount` stuck at 0 forever,
  `ForceMeshUpdate()` doesn't recover it, only destroying and recreating
  the component after a frame does) — except this occurrence proved the
  bug isn't actually limited to "the very first TMP label in a session" as
  originally diagnosed; it can hit any label, especially ones rebuilt via
  rapid destroy/recreate churn. Generalized the earlier one-off fix into a
  reusable sweep (`RecoverBlankLabelsNextFrame`) and applied it to Player
  Inspect's content, which rebuilds its whole label set on every refresh.

### Problems encountered
- **Same-frame screenshots came back stale.** `ScreenCapture.CaptureScreenshotAsTexture()`
  called in the same script execution as the action it was meant to
  capture would return the previous frame's buffer. Every verification
  screenshot after the first few had to be split into a separate tool call
  from the action itself.
- **Unity's own MCP/RunCommand tooling intermittently corrupted its own
  session state**, independent of any code bug — symptoms were button
  clicks silently not registering at all, or a stale "no fixtures found
  for Liverpool" warning firing regardless of which team was actually
  clicked. Happened twice, both times immediately following an internal
  tooling error; a full Play-mode restart (stop, wait several seconds,
  confirm actually stopped, re-enter, wait again) cleared it both times.
  Cost real time before the pattern was recognised as tooling state rather
  than a regression to chase in the game code.
- **`result.Log`'s formatting doesn't support composite format specifiers**
  like `{0:F1}` — the placeholder prints literally instead of substituting.
  Not discovered until a diagnostic pass returned obviously-wrong output;
  worked around by wrapping in `string.Format(...)` first.
- Diagnosing the frozen-ghost and TMP-blank-label bugs both required
  reproducing the *exact* failure precisely (the ghost bug specifically
  needed the full real drag lifecycle - `OnBeginDrag`/`OnDrag`/`OnDrop`/
  `OnEndDrag` in the correct order via `ExecuteEvents.Execute`, not just
  calling `OnDrop` directly as the first verification pass had done, which
  is exactly why it slipped through that pass uncaught).

### State at session end
Both commits pushed. Three of the bug fixes above (drag ghost, click-during-
drag, blank OVERALL number) were made and confirmed compiling clean, but
**not yet re-verified live** - the session ended mid-check. Flagged as the
first thing to confirm next time, alongside two features scoped and agreed
but not started: a Manager-Mode-only squad-quality modifier on match
outcomes (deferred pending the Tactics Board, which now exists), and a
goal-scorer football-icon glyph via a TMP Sprite Asset (Oswald has no
symbol/emoji glyphs at all, same as every previous instance of this issue).

---

## 2026-08-07 — Unity MCP live-Editor access + Manager Mode UI overhaul

**Commits:** `ca44091`, `1ed9ccf`

### Goal
Move from static file inspection to live Unity Editor access for UI debugging,
then use that access to clear a backlog of Manager Mode UI bugs and implement
a v2 design restructure from updated mockups.

### Tooling change
First session with Unity MCP connected to **Claude Code** specifically (as
opposed to Claude Desktop — these use separate, non-interchangeable client
configs, which cost some setup time to untangle). This enabled live scene
inspection (`SerializedObject` reads of private fields), in-Editor script
execution, and console log access, replacing a slower guess-and-check loop
based on reading `.unity`/`.cs` files alone.

### Root-cause fixes
- **Global 2x scale mismatch.** Every screen had been rendering at roughly
  half its intended size project-wide. Traced to `CanvasScaler.referenceResolution`
  being left at `1920x1080` while every design mockup was authored against a
  960px-wide canvas. Corrected to `960x540` — this single fix resolved the
  large majority of "everything's too small" symptoms across every screen at
  once, rather than needing a per-element patch.
- **Font inconsistency.** Several UI-building helper methods only ever set
  text colour/size and never assigned a font, so any button styled by those
  code paths silently kept whatever font Unity's default happened to be.
  Imported Oswald (OFL-licensed) as the project's default TMP font and fixed
  the helper methods to apply it consistently.

### Design v2 restructure
Implemented from updated Claude Design mockups (fetched via the project's
`DesignSync` tool):
- New wordmark on the Title screen, replacing the old shield mark.
- Matchday Prep simplified to scouting-only; Tactic selection and
  Substitutions moved off it.
- Live Match Day gained the Tactic pills and Substitutions panel instead.
- Full-Time Summary reworked: centered Match Stats, real goal-scorer lists
  under the score, inline event log removed in favour of a link to a new
  standalone Match Events screen (scrollable full match timeline, built from
  scratch).

### Problems encountered
- **Broken TMP font asset — all text went blank project-wide.** The first
  attempt at generating the Oswald font asset saved the material as a
  sub-asset but not the atlas textures, so the texture references went
  dangling on the next domain reload (`MissingReferenceException`). Reverted
  the default font immediately, deleted the broken asset, and rebuilt it
  correctly (every atlas texture added as a sub-asset, not just the
  material) before re-applying it.
- **Networking blocked inside the Unity MCP execution sandbox.** Downloading
  the font file via `UnityWebRequest`/`HttpClient`/`WebClient` from inside a
  live-executed Editor script either hung or failed to compile. Worked around
  by downloading the file with the agent's own shell tooling instead, and
  having Unity only import the already-present file.
- **`RectMask2D` applied to the wrong GameObject.** A mask only clips its
  *children*, not a sibling `Graphic` on the same object — the Match Log
  overflow fix initially did nothing because of this, until the text was
  reparented under a dedicated mask container.
- **A shared anchor/pivot helper (`SetPointAnchor`) coupled two things that
  shouldn't have been coupled** — it always set `pivot == anchor`, which is
  wrong for elements meant to be left-edge-referenced rather than centred on
  their anchor point. Caused several Match Day elements to render straddling
  the panel centre instead of sitting in their column; fixed with explicit
  pivot overrides at each affected call site.
- **A reparenting step was missed** when moving the Tactic buttons from
  Matchday Prep to the live Match Day footer — their anchors were updated but
  `SetParent` was never called, so they stayed on the old screen and simply
  didn't appear on the new one.
- **A leftover manual Editor edit on the Title screen** (non-default scale
  plus a stray point-anchor offset from earlier ad-hoc tweaking) pushed the
  "New Career" button off-screen. Rather than a one-off manual re-fix, the
  screen's build code now defensively resets scale/anchors/position/size to
  known-good defaults every time it runs, so future manual nudges can't
  silently persist across the code-driven layout again.

### Academic-honesty / architecture notes
- Two additive-only fields were added to the shared `Sim` namespace this
  session (`AgentMatchEvent.ScorerName`, `LeagueTable.EnsureTeam`), both
  verified via `git diff` immediately after editing to confirm no existing
  simulation logic was touched — required given this codebase also backs the
  dissertation's separate Research Mode simulation.
- Full-Time goal-scorer lists use the real `shooter.Name` captured at the
  point of the simulated goal, not text parsed out of the existing
  human-readable match-event description — consistent with the project's
  standing rule against deriving displayed stats from a less-authoritative
  source when a direct one already exists.

### Deferred
A condensed match-event text format (e.g. "GOAL! *scorer* assisted by
*assister*") was discussed and scoped but explicitly deferred, pending a
future session — no code was written for it.

### State at session end
Working tree clean, both commits pushed. Two of the session's fixes (the
Match Day column pivot fix, the Tactic-button reparenting fix) had not yet
been verified in a live Play-mode pass when the session ended.
