# Tactics Board — Session Handoff (2026-08-07, session 2)

## 1. Branch / project state
- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree: **not clean**. Committed through `cd99991` ("feat: add Tactics Board (drag-to-sub, formation switching) to replace Squad scrollview") — that one's pushed. On top of it, **two files have uncommitted changes** from this session's live-evaluation bug-fix pass:
  - `FootballSimulationResearch/Assets/Scripts/Manager/ManagerPrototypeController.cs`
  - `FootballSimulationResearch/Assets/Scripts/Manager/TacticsBoardPlayerCard.cs`
- Unity Editor: **stopped** (not in Play mode), not compiling. Safe starting state.
- Suggested commit message for the uncommitted changes is in section 6.

## 2. What happened this session

**Quick wins first:** all four items pending from the previous handoff (Match Day right-column pivot, tactic-button reparenting, Title logo, resuming the interrupted playthrough) were live-verified working — see that session's fixes, all held up.

**Bench size bumped 7 → 9** (`AgentSquadGenerator.GetBenchPositions`) to match current real-world Premier League matchday-squad rules. Confirmed inert for Research Mode — `AgentMatchSimulator` never reads `.Bench`, only `.StartingEleven`.

**Planning discussion (no code):** player-quality-affects-win-probability and out-of-position penalties. Confirmed current sim behavior: team-level expected goals come purely from `StatisticalModel`'s historical team strength, with zero squad awareness — swapping your whole XI never changes total attacking output. Individual player attributes DO modestly affect chance/goal conversion once a chance happens (via `AgentMatchSimulator`'s weighted player-picking), just not overall chance volume. Agreed direction if/when this gets built: a **Manager-Mode-only squad-quality modifier** applied in `ManagerPrototypeController.SimulateFixture` (same place `ManagerTacticModifier` already applies), never touching `AgentMatchSimulator`/`StatisticalModel` so Research Mode's numbers stay untouched. User wants the swing to stay **plausible/subtle** — no single-player-wins-the-match magnitude. **Explicitly deferred until the Tactics Board existed** (it now does) — not picked up yet, still needs the user to bring it back up.

**Tactics Board built** (commit `cd99991`) — replaces the old scrollview-based Squad screen entirely:
- New files: `TacticsBoardLayout.cs` (pin coordinates per formation, pulled from the Claude Design mockup), `TacticsBoardPlayerCard.cs` (drag/drop/tap component).
- Pitch view with position-pinned starting XI, draggable bench row, formation-switch dropdown (greedy best-fit reassignment: `rating × positionFit` per slot).
- `AgentTeam.SubstitutePlayer` fixed to preserve slot order (was silently breaking it via remove+append) — needed so the board can reliably say "who's in which pin" after a sub.
- `AgentSquadGenerator.GetStartingPositions` made public; added the missing `ThreeFourTwoOne` shape (previously silently fell back to 4-2-3-1's — inert until formation-switching existed, since no team was ever auto-assigned it).
- Retired the old click-based pre-match "Make Subs" flow (`makeSubsButton`/`OnMakeSubsClicked` — found sitting off-screen at `y=-880`, dead since a past reparenting bug). In-match subs still use the same underlying picker.
- Matchday Prep's opponent scouting list was also retired (per user: the tactics board will show the opposition's formation there once that's built) — currently an intentional empty body under the header.

**Bugs found via live evaluation, fixed:**
1. **Full-time scorer-list boxes geometrically overlapped** (260px wide, only 240px apart — pre-existing from last session's redesign, not introduced this session; earlier testing just hadn't hit a wide-enough scoreline to expose it). Narrowed to 220px. Stress-tested with a 3-goal one-sided scoreline afterward.
2. Scorer-list text nudged down slightly to stop grazing the header divider.
3. Pitch-marking line opacity tuned down per user feedback (0.10, after briefly being at 0.18 for visibility).
4. Tactics Board pitch pins were badly cramped/overlapping (mockup's pin percentages assumed a much taller pitch region than our landscape 960×540 canvas has room for) — added a vertical-compression factor, shrunk pin/badge footprint, nudged GK further from the back line specifically, and added pitch markings (halfway line, both penalty boxes, all flat rectangles — no sprites) plus a bordered "player token" badge look, closer to the mockup. **User is separately working with design on the board's still-quite-wide aspect ratio** — don't relitigate that, it's their call to make with design's input, not something to autonomously redesign.
5. **Drag ghost freezing on screen after a successful drop.** Root cause: dropping triggers a full board rebuild that destroys the dragged card's GameObject; `Destroy()` makes a `UnityEngine.Object` compare `== null` immediately even though actual destruction is deferred, so the EventSystem's later `OnEndDrag` call saw a "null" source and silently skipped it, orphaning the ghost. Fixed by having `OnDrop` clean up the dragged card's ghost itself, synchronously, before triggering the rebuild — no longer dependent on `OnEndDrag` firing afterward.
6. **A real drag was also triggering the click handler**, opening Player Inspect mid-drag (Unity's own click-vs-drag suppression didn't hold up here, unclear exactly why). Added an explicit `isDragging` guard in `TacticsBoardPlayerCard.OnPointerClick`. Also added `ManagerPrototypeController.CleanupStrayDragGhosts()` as a belt-and-suspenders sweep — called on leaving the Tactics Board and on opening Player Inspect — so a stray ghost can never survive a screen change regardless of root cause.
7. **Player Inspect's "OVERALL" number was rendering completely blank** despite everything about the label checking out (active, correct text/color/position). Turned out to be the **same underlying bug as the Title-wordmark fix from last session** (`characterCount` stuck at 0, `ForceMeshUpdate()` doesn't recover it, only destroy+recreate after a frame does) — but this proves that bug isn't actually limited to "the very first TMP label in a session" as originally diagnosed; it can hit any label, especially ones rebuilt via rapid destroy/recreate churn (which Player Inspect does on every refresh). Generalized the old one-off `RecoverBlankLabelNextFrame` (single label) into a reusable `RecoverBlankLabelsNextFrame` (sweeps every label under a root), applied to Player Inspect's content. **If you spot the same "structurally fine but invisible" text symptom anywhere else, it's almost certainly this same bug — apply the same sweep there.**

**Deferred, discussed, not started:** a goal-scorer football icon (⚽) for the post-match summary — Oswald can't render it (same class of issue as the em-dash/star fixes). Options discussed: commission a PNG from design, or generate one in-Editor via Unity's built-in AI sprite tool; either way it'd get wired up as a **TMP Sprite Asset** (`<sprite name="football">` inline in the existing scorer-line strings) rather than restructuring the list-building code. User said "not now" — table it until they bring it back up.

## 3. Outstanding — needs live verification next session
Fixes #5, #6, #7 above (drag ghost, click-during-drag, blank OVERALL number) were made and confirmed **compiling clean**, but the session ended (hit the session limit) right as Play mode was being cycled to re-test them — **none of the three have been re-verified live yet.** First thing next session:
1. Re-enter Play mode, navigate to the Tactics Board, do a real drag-and-drop sub — confirm no ghost freeze, confirm it doesn't also open Player Inspect.
2. Open Player Inspect (tap a pin or bench card) — confirm the big "OVERALL" number now renders.
3. Do a couple of drags in a row to be sure nothing regressed from the fixes.

Also still open (deferred, not blocking):
- Player-quality-affects-win-probability / out-of-position penalty modifier (see section 2 — scoped and agreed, not built).
- Goal-scorer football icon (see section 2 — not started).
- Tactics Board aspect-ratio polish — with the user's designer, not ours to drive.
- Condensed Match Day event text with assists (from the *previous* handoff, section 4 there — still never picked up, still deferred; carrying this note forward since it keeps getting bumped).

## 4. Gotchas learned this session (save yourself the rediscovery time)
- **`Destroy()` makes a `UnityEngine.Object` compare `== null` immediately**, even though actual C++-side destruction is deferred to end of frame. If a chain of Unity EventSystem calls (e.g. `OnDrop` → your callback → `OnEndDrag`) destroys an object partway through, any subsequent `!= null` check on that same object (including ones inside Unity's own EventSystem code) will treat it as already gone. Don't rely on a later callback firing on an object your own earlier callback might have destroyed — do the cleanup yourself, synchronously, before triggering whatever destroys it.
- **TMP mesh generation can silently fail on any label** (`characterCount` stuck at 0 forever despite correct `.text`/color/position, `ForceMeshUpdate()` doesn't help) — not limited to "the first TMP text in a session" as first thought. Reusable fix now exists: `RecoverBlankLabelNextFrame` (one label) / `RecoverBlankLabelsNextFrame` (sweep a root), both in `ManagerPrototypeController.cs` — wait one frame, then destroy+recreate the component if still blank. Apply the sweep version to any screen that rebuilds its own labels from scratch on refresh.
- **Unity's MCP/RunCommand tooling itself occasionally corrupts session state** — symptoms: button clicks silently stop registering (no visual/state change), or a `"no fixtures found for 'Liverpool'"`-style warning fires regardless of which team was actually clicked. Not a code bug both times it happened this session. Fix: full Play mode restart — stop, wait ~5-8s, confirm `IsPlaying: False` before re-entering, wait again after re-entering before testing anything. Don't spend time debugging game code for this symptom until you've ruled out a stale session via restart.
- **Screenshots taken in the same tool call as the action they're meant to capture come back stale** (previous frame). Always split "do the thing" and "take the screenshot" into separate `Unity_RunCommand` calls.
- `result.Log` in `Unity_RunCommand` doesn't support C# composite format specifiers like `{0:F1}` — the placeholder prints literally instead of substituting. Wrap in `string.Format(...)` first, or stick to plain `{0}`.
- `Image` is ambiguous inside `Unity_RunCommand` scripts (some namespace collision in that sandboxed context) — always fully qualify as `UnityEngine.UI.Image`.
- `System.Reflection` is blocked inside `Unity_RunCommand` scripts — don't reach for it to peek at private fields; use public methods/properties or add a temporary public accessor instead.

## 5. Constraints (binding, do not relax without asking)
- Do not modify Research Mode behaviour/results without explicit confirmation. `AgentMatchSimulator.SimulateMatch` must stay byte-for-byte unchanged.
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation — one explicit ask at a time.
- User pushes via GitHub Desktop themselves — give commit messages, don't run `git push`.
- Build all dynamic UI positioning via code (`Build*Chrome()` methods), not hand-dragging in the Editor.
- No multithreading/parallelism — out of scope for this project (see `PROJECT_CONTEXT_FOR_AI.md` guardrail #11).
- Don't touch the Tactics Board's aspect ratio/proportions — user is working that with their designer.

## 6. Suggested commit message for the current uncommitted changes

```
fix: Tactics Board drag ghost freeze, click-during-drag, blank OVR number

- Drag ghost could freeze on screen after a successful drop - OnDrop now
  cleans up the dragged card's ghost itself before triggering the board
  rebuild, rather than relying on OnEndDrag (which silently gets skipped
  since Destroy() makes the source compare == null immediately)
- A real drag could also fire the click handler, opening Player Inspect
  mid-drag - added an explicit isDragging guard, plus a defensive sweep
  that clears any stray ghost on leaving the Tactics Board or opening
  Player Inspect regardless of cause
- Player Inspect's "OVERALL" rating could render completely blank (same
  underlying TMP mesh-generation bug as the Title wordmark fix last
  session, now confirmed more general than "first label only") -
  generalized the recovery into a reusable sweep, applied here
- Full-time goal-scorer boxes narrowed so they can't geometrically
  overlap regardless of scoreline; nudged down off the header divider
- Tactics Board pitch markings (halfway line, penalty boxes) and pin
  spacing tuned for this canvas's much wider/flatter aspect ratio than
  the source mockup's own pitch region
```

## 7. Suggested first prompt for the next session

> Continuing the FootballResearchProject on branch `unity6-ai-prototype` — see `HANDOFF.md` for full context. Last session built the Tactics Board (drag-to-sub, formation switching, committed as `cd99991`), then found and fixed three more bugs during live evaluation (drag ghost freezing on screen, a drag also triggering Player Inspect to open, and a blank "OVERALL" rating number) - all compile clean but **none have been live-verified yet**, session ended mid-check.
>
> First thing: re-enter Play mode, do a real drag-and-drop sub on the Tactics Board (confirm no ghost freeze, confirm it doesn't also open Player Inspect), then open Player Inspect and confirm the OVERALL number now renders.
>
> Same hard constraints as always: don't touch Research Mode without asking, keep Manager Mode separate, no new features without confirming with me first, give commit messages but don't push. Don't touch the Tactics Board's aspect ratio - I'm working that with my designer separately.
