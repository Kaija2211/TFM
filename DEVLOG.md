# Development Log

A running, append-only journal of Manager Mode development sessions, kept for
MSc Major Project documentation purposes. Unlike `HANDOFF.md` (which is
overwritten each session to hand off *current* state to the next one), this
file accumulates — new entries go at the top.

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
