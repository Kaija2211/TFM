# Manager Mode UI Overhaul — Session Handoff (2026-08-05)

## 1. Branch / project state
- Branch: `unity6-ai-prototype` (main branch holds the stable pre-Unity-6.5 research baseline, untouched).
- Working tree: **clean**, committed through `7dbac0b` ("feat: add matchday prep screen and rebuild hub layout"). Double-check it's actually pushed (GitHub Desktop) before assuming the PC has it after pulling.
- This is a **machine switch**, not a fresh project: previous session was on a laptop; work continues today on a different ("main") PC for the first time. Claude Code session history and auto-memory do **not** sync across machines — only what's committed to git (this file included) carries over. Point a fresh session at this file first thing.

## 2. What happened this session (full UI reskin + restructure, in order)

**Reskin pass 1** (from Claude Design mockups, "Matchday Manager" dark/green aesthetic):
- New `ManagerUITheme.cs` — shared palette/type constants + runtime UI-building helpers (`BuildLabel`, `BuildBar`, `BuildButton`, `BuildAccentBand`, `SetDisabledPlaceholder`, `NormalizeButtonLabel`, `SetPointAnchor`). Everything below builds on this.
- Code-generated Title screen (New Career / Load Career [disabled] / Settings [disabled] / Exit).
- Team Select rebuilt as a code-generated 5-column grid of the real 20 EPL clubs (not Prev/Next), with a Manager Name field (in-memory only, no save system).
- Squad screen: Starting XI/Bench section headers + per-row rating bars (SquadListView.cs extended).
- Player Detail rebuilt as grouped attribute columns with bars (no fabricated Age/Apps/Goals/Assists - that data doesn't exist).
- Matchday: per-team shot split added via one new **additive** field (`HomeTeamAttacking`) on `AgentMatchSimulator.AgentMatchEvent`, set in `ResolveAttack`. Verified via `git diff` to be purely additive - `SimulateMatch` itself (which Research Mode also calls) is untouched. Possession dropped (no data source, wasn't invented).

**Reskin pass 2 — Matchday Prep restructure:**
- New pre-match screen (`ShowMatchdayPrep`/`BuildMatchdayPrepChrome` in `ManagerPrototypeController.cs`): opponent name/formation, a read-only opponent squad list (second `SquadListView` instance), Tactic selection, and pre-match Subs — all **moved off the Hub** onto this screen.
- "Inspect Player" removed from the Hub entirely (redundant - clicking a squad row already jumps to Player Inspect).
- "Play Next Match" relabeled "Next Matchday", now opens this screen instead of simulating instantly; a new "Simulate Match" button here does what the old button used to do.
- Tactic buttons/Make Subs/subs counter were **reparented** (dragged in the Editor) from the Hub onto this new panel, not rebuilt - same working C# references, just repositioned.

**Reskin pass 3 — Hub visual rebuild** (to match the newer Hub mockup):
- New `LeagueTableView.cs` (parallel to `SquadListView.cs`) - scrollable, styled league table grid (#, Club, Pts, P, W, D, L, GF, GA), managed club's row highlighted.
- `BuildHubChrome()`: crest badge (colored initials badge - **not** a real crest shape, no artwork/mesh pipeline exists for that), club name + manager/matchday byline, Simulate Season moved to top-right, two-column body (menu left, table right). "Next Fixture"/"Tactic" lines dropped from the Hub entirely (redundant with Matchday Prep, confirmed with user).
- Key architectural decision made and validated this session: **reposition existing buttons via code** (`ManagerUITheme.SetPointAnchor`) instead of hand-dragging them in the Editor. Every manual-reposition this session caused a bug (buttons ending up hidden behind other UI); every code-positioned element didn't. Apply this same principle to any future screen work.
- `headerText`/`nextFixtureText`/`tacticText`/`leagueTableText` fields and their supporting methods (`BuildSeasonTableSummary`, `DescribeFixture`) were removed entirely - retired, not deprecated.

## 3. Current known-working behaviour
Verified this session via direct scene-file inspection (not just screenshots) after each fix: Title screen, Team Select grid (real clubs, Manager Name), Hub (crest/name/byline/two-column layout/styled table), Matchday Prep (opponent scouting/formation/tactics/subs), live Matchday replay with in-match subs, Squad screen, Player Detail. Formation now displays as "4-3-3" style (was showing the raw C# enum name "FourThreeThree" - fixed via a `FormatFormation` helper).

## 4. Outstanding — one Editor item left
`Transfers` and `Exit to Title` buttons on the Hub were never actually created (confirmed via scene file: both still `{fileID: 0}`, unwired). `BuildHubChrome` will reposition/style them automatically the moment they exist - just:
1. `UI → Button - TextMeshPro` on `SeasonHubPanel`, rename `TransfersButton`. Position doesn't matter, code moves it.
2. Same again, rename `ExitToTitleButton`.
3. Drag both into their slots on `ManagerPrototypeController`.

That's the last item before the whole Hub rebuild is fully wired.

## 5. Constraints (binding, do not relax without asking)
- Do not modify Research Mode behaviour/results without explicit confirmation. `AgentMatchSimulator.SimulateMatch` must stay byte-for-byte unchanged - any shared-sim-code touch (see `HomeTeamAttacking` above) must be purely additive and verified via `git diff` before moving on.
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation - one explicit ask at a time.
- User pushes via GitHub Desktop themselves - give commit messages, don't run `git push`.
- Prefer direct scene-file edits (`.unity` is plain YAML) over asking for manual Editor repositioning where practical - but only after confirming the scene has actually been *saved* first (Unity holds unsaved state in memory; editing the file while it's open with unsaved changes gets silently overwritten on next Ctrl+S). Ask the user to save, then close/reopen the scene tab after any direct file edit so Unity picks it up.

## 6. Bigger-picture context (not urgent, just context)
User is thinking post-14th (submission deadline) about two possible directions: eventually generalizing the agent-based sim into a reusable "engine" for multiple future football games, vs. shipping something small first. Current lean: ship a simple, elegant **Statistical-Model-only** mobile game first (inspired by "38-0-0"'s minimalism) as an actual completed portfolio piece, before attempting anything bigger. Nothing to act on yet - just useful framing if it comes up again.

## 7. Suggested first prompt for the next session

> Continuing the FootballResearchProject on branch `unity6-ai-prototype`, now on a different machine for the first time - see `HANDOFF.md` for full context (three UI reskin passes completed: Title/Team Select/Squad/Player Detail/Matchday, then a Matchday Prep restructure, then a Hub visual rebuild). Working tree was clean through commit `7dbac0b` on the previous machine.
>
> One Editor item is still outstanding: `Transfers` and `Exit to Title` buttons on the Hub were never created (see section 4). Everything else should be fully wired and working.
>
> Same hard constraints as always: don't touch Research Mode without asking, keep Manager Mode separate, no new features without confirming with me first, give commit messages but don't push.
