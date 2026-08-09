# Manager Mode — Player Detail, Full Time Redesign, Match-Viewing UX, Real Bug Fixes — Session Handoff (2026-08-09, session 6)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree: clean. This session's work is committed (`1ce92d1`) and pushed to `origin/unity6-ai-prototype`.
- Unity Editor: was in Play Mode at the end of the session (mid-verification of a Liverpool career), `Time.timeScale` reset to `1f`. Safe starting state either way.
- Another long, dense session, roughly chronological: (A) a Player Detail/GK fix carried over from the top of the session, (B) a large "match-viewing UX" thread that kept growing based on live screenshots and playtesting (Match Log phrasing, pacing, a full Full Time screen redesign across several iterations, managed-team-relative coloring, real proportional stat bars), (C) a genuine goal-scoring calibration bug caught and fixed, (D) a developer easter egg, and (E) two real, user-reported bugs found and fixed at the very end (goal-text duplication/over-coloring, and a matchday-prep banner stuck on the first fixture).

## 2. What happened this session

### A. Player Detail / GK stats (small fix, explicitly deferred from session 5)

- **GK-specific attribute columns** (`ManagerPrototypeController.cs`) — Player Detail's four attribute columns (Technical/Mental/Defensive/Physical) previously showed the same layout for every player, including goalkeepers, whose real generated stats (`Goalkeeping`, `Reflexes`) never appeared anywhere. Now conditional on `PrimaryPosition == PlayerPosition.GK`: Goalkeeping/Reflexes/Positioning/Composure/Passing replace the outfield-only columns; non-GK players are unaffected.
- **Attribute grid top-alignment** — columns were vertically *centered* (`Stack` anchored at `(0.5,0.5)`), so columns with different row counts (2 rows vs 5) had their titles land at different heights. Changed to top-anchored, matching the "PLAYER DETAIL" mockup imported from Thomas's Claude Design project (`Football Manager UI Concepts.dc.html`) via the `DesignSync` tool.
- **Bigger photo + developer easter egg portrait** (see section D) — photo box grown from 140px to 220px square (two rounds of "bigger" feedback), header band height grown to 300 to fit, name/meta/badges start-x shifted from 200 to 300 to clear the wider photo.

### B. Match-viewing UX (the big thread — Match Log, pacing, Full Time redesign)

This started as a discussion (per standing instruction: discuss before building for anything non-trivial) about Match Log text feeling repetitive/simple, and kept growing across many follow-ups as Thomas actually watched matches and reacted to what he saw.

- **Match Log phrasing variety** (`ManagerSim/AgentMatchSimulator.cs`) — diagnosed the actual mechanism: every event resolves to one of 6 `ChanceType`s × 4 outcomes = 24 fixed templates, so repetition was structural. Presented three directions (more variants / real-sim-state-driven phrasing / rhythm variety); Thomas picked variants + rhythm first (`PickVariant` helper, 3 phrasings per slot, short punchy lines mixed into the frequent stopped/off-target events), then later in the session came back for the third direction: fatigue-aware phrasing (`GetFatigueMultiplier < 0.90` triggers "tired legs" variants) and score-state-aware late-goal drama (`isDramaticLateGoal`: 80'+ and the scorer was level or behind beforehand).
- **Match replay pacing** — `matchReplayDurationSeconds` bumped 45→60 (0.5s/min → 0.667s/min real time). Thomas confirmed 60s feels right after watching several matches later in the session.
- **Full Time screen redesign, three iterations**, all from Thomas reacting to live screenshots:
  1. First pass: goal scorer lists moved off tiny centered labels into their own left half (bigger font, self-labeled with team name), Match Stats moved to the right half, a small goal timeline added between them.
  2. Second pass: scorer lists became two side-by-side columns (not stacked) so a high-scoring team has real room to grow.
  3. Third pass (Thomas's own idea, from a screenshot showing a large empty band below both halves): the timeline moved out of the cramped left-half strip entirely into a **big full-width band** below everything (1840px wide, 26px markers, minute labels next to each marker), and the scorer lists shrank back to a compact block up top since the timeline now owns "when did it happen."
- **Managed-team-relative red/green coloring** — goal scorer headers, timeline markers, and (later) match-stat bars all color by `currentFixture.HomeTeam == managedTeamName`, not simply home=green/away=red, so it's correct on away fixtures too (verified specifically on an Everton-away fixture).
- **Real per-line dividers in the live event feed** — Thomas referenced the original mockup's `border-bottom:1px solid #1e2a3d` treatment. The live ticker (`eventFeedText`, a single multi-line `TMP_Text` block) was converted to a proper row-based system (`matchEventFeedContainer`, `AppendMatchEventRow`) — one `GameObject` per event with its own label + a real divider `Image`, same shape as the existing `RefreshMatchSubsMadeList` pattern. `eventFeedText` itself was left in the hierarchy but disabled rather than touching its Inspector wiring.
- **Match stats made real comparisons** — neither the live ticker nor the full-time panel's stat bars were actually comparing two teams: `BuildFullTimeStatRow` was hardcoded to `pct=1f` (always full), and the live version was proportional but single-color. Added `ManagerUITheme.BuildSplitBar` (two `Image` fills meeting at the home-team's real share point) and wired it into both, colored managed-team-relative.

### C. Goal-scoring calibration bug (caught mid-session, real, significant)

While chasing a both-teams-scored match to test coloring, both Thomas and this session kept hitting suspiciously low-scoring matches. Investigated properly with a 200-match same-teams batch comparing `Manager.AgentMatchSimulator` (the fork) against the protected `Sim.AgentMatchSimulator`:

- **Before fix**: Manager fork 1.21 goals/match, ~30% scoreless draws, vs. the protected original's 2.82 goals/match — using identical generated teams.
- **Root cause**: the fork's earlier on/off-target split (added for a real Shots-on-Target stat) stacked a second probability gate (`onTargetChance`) in front of the *same* unconditional `goalChance` roll the original uses, roughly halving the effective scoring rate.
- **Fix**: rescaled `goalChance` to represent "given the shot is already on target" (`unconditionalGoalChance / onTargetChance`, clamped to 0.85) instead of leaving the unconditional formula under the new gate.
- **After fix**: 2.66 goals/match, 56% both-teams-scored, closely matching the protected original's 2.68/58% on the same teams.
- Added as a durable guardrail: **`PROJECT_CONTEXT_FOR_AI.md` guardrail #13** — any future change to the ManagerSim fork's scoring/shot/save probability chain needs a real before/after goals-per-match check against the protected original, not just a clean compile.

### D. Developer easter egg

- One fixed player on Arsenal: **Hidde Rietberg**, 25yrs, 183cm, ST, real portrait, everything else (stats, Overall) generated normally like any other player. Implemented entirely in `ManagerPrototypeController.cs` (`ApplyDeveloperEasterEggPlayer`, called *after* `GenerateSquad` returns) — deliberately not inside `AgentSquadGenerator.cs` itself, since that generator is shared with Research Mode and special-casing inside the generation loop would shift the RNG sequence for every other player.
- Portrait handling: the source PNG needed its `TextureImporter.textureType` fixed from `Default` to `Sprite` before `Resources.Load<Sprite>` would return anything. First crop attempt overwrote the source file in place without a backup and came out too tight (no shoulders visible) — recovered from an untouched copy in Thomas's Downloads folder, recropped properly (1000×1000, head-and-shoulders), this time without touching the backup.

### E. Two real, user-reported bugs (end of session)

1. **Goal text duplication + over-coloring** — the row-based live feed (and the separate "Match Events" full list) prepend a fixed "N' GOAL ·" label to every goal event, but the goal-text variants added in section B also said "goal!"/"GOAL!" inside the description, producing visible duplicates ("GOAL · GOAL! ..."). Also, the *entire* row was colored green for a goal, not just the prefix. Fixed both: rewrote all `BuildGoalEventText` variants to never contain the word "goal", and changed both event-list builders to keep the row's base color at `TextBody`, wrapping only the "N' GOAL" prefix in an inline `<color>` tag.
2. **Matchday Prep banner stuck on the first fixture** — a real, independently-confirmed bug (Thomas noticed it in actual play; this session had separately brushed past the same symptom once during automated testing and wrongly assumed it was a testing artifact). The header ("Liverpool vs X", "Matchday N") never updated after the very first fixture, while the scout list/opponent pitch below it (fed by the same refresh call) updated correctly every time. Root cause: the exact same TMP mesh-generation-failure trap that hit the New Career subtitle in session 5 — `matchdayPrepTitleLabel`/`matchdayPrepSubtitleLabel` were cached as `TextMeshProUGUI` fields (starting `text=""`, a prime failure candidate), and `RecoverBlankLabelsNextFrame` orphaned that cached reference the first time it swept the container. Fixed with the already-established pattern (`teamSelectSubtitleObj`'s style): cache the parent `GameObject`, fetch `GetComponentInChildren<TextMeshProUGUI>()` fresh every refresh. Verified end-to-end: matchday 2 now correctly reads "Liverpool vs Newcastle United (Away)" / "Matchday 2".

## 3. Outstanding — captured to auto-memory, not started

- **Name pool question** was resolved this session (expanded 81→183 surnames) — not outstanding anymore.
- **Match Log direction 2** (fatigue/score-state phrasing) was also resolved this session.
- **Tactical shape** (formation-vs-formation interaction) — still queued as its own discussion, unchanged from prior sessions.
- **Player progression** and **transfer market/finances** (+ the scoped-down **free agent market** idea from earlier this session) — bigger, still-uncommitted ideas.
- **Set-piece taker designation** and a **per-player attack/defend role toggle** — floated, not committed.
- **More developer easter egg friends** — the pattern is now established (`ApplyDeveloperEasterEggPlayer`); Thomas can supply name/age/height/position/portrait for more whenever he wants.

## 4. Gotchas learned this session (save yourself the rediscovery time)

- **TMP cached-label reference gotcha, now hit twice** — see `feedback_tmp_cached_label_reference_gotcha` in memory. If a `TextMeshProUGUI` is cached directly as a field (not its parent `GameObject`) and lives in a container swept by `RecoverBlankLabelsNextFrame`, the sweep can silently orphan that reference — every future `.text =` write goes to a dead object while the real on-screen label freezes at its build-time value. **Diagnostic tell:** one specific label stuck on its first-ever value while sibling UI fed by the same refresh call updates correctly. Fix: cache the parent `GameObject`, call `GetComponentInChildren<TextMeshProUGUI>()` fresh each time.
- **Mid-session Play Mode hot-reload is unreliable** (see `feedback_playmode_hotreload_unreliable`) — a script edit that compiles clean doesn't always take effect in an already-running Play session, even though it worked earlier in the very same session. If a live-verified fix still shows old behavior, don't conclude the fix is wrong — stop and restart Play Mode before re-checking.
- **`Unity_RunCommand`'s own sandbox rejects `System.Reflection`** — live verification has to go through real public entry points (calling public methods, invoking real `Button.onClick`), simulating an actual user, not reflection into private state. Fully-qualify `UnityEngine.UI.Image` (`Image` alone collides with a namespace in that sandbox).
- **Any change to `ManagerSim/AgentMatchSimulator.cs`'s scoring probability chain needs a real before/after goals-per-match check** against the protected original (same generated teams, ~200 matches) — see section C. Now a durable guardrail (`PROJECT_CONTEXT_FOR_AI.md` #13), not just a one-off lesson.
- **Wall-clock timing across separate `Unity_RunCommand` calls is not reliable** for precision checks (e.g. verifying real-time pacing) — there's inherent round-trip latency between calls that isn't accounted for by explicit `sleep`s. Use in-game `Time.realtimeSinceStartup` snapshots taken within single calls if timing precision actually matters, or just trust the math and ask the user to feel-check subjective pacing.
- Carried forward, still true: `Destroy()` makes a `UnityEngine.Object` compare `== null` immediately despite deferred actual destruction; chrome-build-once means neither code nor scene-file fixes apply to an already-running Play Mode session without a restart; there is still no save/load system, so always check before restarting Play Mode if there's visible in-progress state the user cares about (this session, restarts were done freely for verification purposes with no objection, but always worth a quick check-in first).

## 5. Constraints (binding, do not relax without asking)

- Do not modify Research Mode behaviour/results without explicit confirmation. `Assets/Scripts/Sim/AgentMatchSimulator.cs`, `Assets/Scripts/Sim/PlayerAgent.cs` must stay byte-for-byte unchanged — confirmed via `git diff` at multiple checkpoints this session, always empty. `Assets/Scripts/Sim/AgentSquadGenerator.cs` **was** touched this session (the name-pool array expansion) — this is a shared file (Research Mode uses it too via `ResearchEvaluationRunner`), but the change was a pure array-literal addition with zero effect on RNG call count/sequence or any numeric generation logic, confirmed safe by design (no new/removed `Random` calls). Any *other* future change to this file needs the same scrutiny before being considered safe.
- `Assets/Scripts/ManagerSim/AgentMatchSimulator.cs` is the Manager-only fork, free to edit — but see guardrail #13 (section C above): any scoring-probability change needs a real before/after goals-per-match check.
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation.
- User pushes via GitHub Desktop themselves normally — give commit messages, don't push — **except this session, where pushing was explicitly requested** (commit `1ce92d1`, pushed to `origin/unity6-ai-prototype`).
- Build all dynamic UI positioning via code, not hand-dragging in the Editor.
- No multithreading/parallelism.
- Position badges/pins are code-positioned, not freely drag-repositioned on the pitch itself.

## 6. Suggested first prompt for the next session

> Continuing the FootballResearchProject on branch `unity6-ai-prototype` — see `HANDOFF.md` for full context. Last session was a very long match-viewing UX pass: Player Detail GK columns + top-alignment + bigger portrait, a Match Log phrasing overhaul (variants, rhythm, and later fatigue/score-state-driven text), a Full Time screen redesigned three times based on live screenshots (ending in a full-width goal timeline with minute labels), managed-team-relative red/green coloring throughout (scorer names, timeline markers, and now-real proportional match-stat bars), a developer easter egg (Hidde Rietberg on Arsenal), and two real bugs found and fixed: goal-text duplication/over-coloring, and a matchday-prep banner that was permanently stuck on the first fixture (a TMP cached-label reference gotcha now documented in memory - check that memory before assuming any future "one label stuck, everything else fine" bug is a data problem).
>
> Also caught and fixed a genuine goal-scoring calibration regression in the ManagerSim fork mid-session (goals/match had silently halved) - there's now a standing guardrail (#13 in `PROJECT_CONTEXT_FOR_AI.md`) that any future scoring-probability change to that fork needs a real before/after goals-per-match check against the protected original, not just a clean compile.
>
> Outstanding, not started: tactical shape (formation-vs-formation interaction) is still queued as its own discussion; player progression and transfer market/finances (plus a scoped-down free-agent-market idea) are bigger uncommitted ideas; set-piece taker designation and a per-player attack/defend role toggle are floated but not committed.
>
> Same hard constraints as always: don't touch `Assets/Scripts/Sim/AgentMatchSimulator.cs` or `Assets/Scripts/Sim/PlayerAgent.cs` (the protected ones) without asking - `Assets/Scripts/ManagerSim/AgentMatchSimulator.cs` is the free-to-edit fork, don't confuse them, and any scoring-probability change there needs a real goals/match check. `Assets/Scripts/Sim/AgentSquadGenerator.cs` is shared but was safely touched this session (name pool only) - any other change there needs the same RNG-sequence scrutiny. Keep Manager Mode separate from Research Mode, no new features without confirming with me first, give commit messages but don't push unless I say so in that session too. Still no save/load system - check before restarting Play Mode if there's visible in-progress state.
