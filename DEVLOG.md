# Development Log

A running, append-only journal of Manager Mode development sessions, kept for
MSc Major Project documentation purposes. Unlike `HANDOFF.md` (which is
overwritten each session to hand off *current* state to the next one), this
file accumulates — new entries go at the top.

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
