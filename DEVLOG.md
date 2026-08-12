# Development Log

A running, append-only journal of Manager Mode development sessions, kept for
MSc Major Project documentation purposes. Unlike `HANDOFF.md` (which is
overwritten each session to hand off *current* state to the next one), this
file accumulates — new entries go at the top.

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
