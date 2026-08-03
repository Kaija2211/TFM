# Manager Mode / Research Sim — Session Handoff (2026-08-03)

## 1. Branch / project state
- Branch: `unity6-ai-prototype` (main branch holds the stable pre-Unity-6.5 research baseline, untouched).
- Working tree: **clean**, all changes committed and pushed via GitHub Desktop through commit `532b35f` ("feat: expand manager mode with club selection and squad views").
- Note: this repo's memory files (`C:\Users\thoma\.claude\projects\...\memory\`) have also been updated to reflect everything below — a fresh chat should already have most of this context loaded automatically.

## 2. Major features completed

**Research Mode (refactor + bugfix):**
- Split the 1126-line `OpenFootballLoader` god class into `TeamRegistry`, `OpenFootballTextParser`, `SimulationStatistics`, `EvidenceExporter`, `AverageTeamResult`, `ResearchEvaluationRunner`. `OpenFootballLoader` is now a thin loader that hands off to `ResearchEvaluationRunner`.
- Fixed a real evidence-export bug: the ABM repeated-evaluation export was mislabeling its own numbers as the "statistical" summary. Each model now exports its own genuine numbers independently.
- Parser now tracks real `▪ Matchday N` header lines from the OpenFootball text format (additive `Matchday` field on `OpenFootballMatch`).

**Manager Mode (built from scratch this session — fully working, not just planned):**
- Full playable loop: Choose Your Club → Season Hub → Matchday → Continue → back to Hub.
- Choose Your Club: Prev/Next through all 20 real clubs + Confirm (previously hardcoded to Liverpool).
- Season Hub: next fixture, chosen tactic, full 20-team division table, Play Next Match / Simulate Season / View Squad / Inspect Player.
- Matchday screen: instant-simulate-then-replay accelerated clock, live scoreline, event feed, Skip to Results, Full-Time Stats (score/tactic/events/shots/goals), Continue.
- Tactics (Attacking/Balanced/Defensive) moved to the Season Hub, chosen between matches — affects only the managed club's expected goals, never Research Mode.
- Simulate Season: instantly resolves all remaining fixtures.
- Full division table synced via real Matchday markers — playing your fixture also resolves the other 9 fixtures in that round, so standings are always genuinely complete (verified against real rearranged/postponed fixtures in two different season files).
- Squads now generated from real per-club team strength (previously flat `1f, 1f` for every team).
- Position-weighted "Overall" rating (`PlayerAgent.GetOverallRating()`) — EA-style, weighted per position.
- Cosmetic-only display-rating stretch (Manager Mode only) so elite squads visibly read as elite, without touching the true rating or any shared sim code.
- View Squad (compact list) and Player Inspect (Prev/Next full attribute breakdown) screens.

## 3. Files created / modified

**New:**
- `Assets/Scripts/Data/TeamRegistry.cs`, `OpenFootballTextParser.cs`, `SimulationStatistics.cs`, `AverageTeamResult.cs`, `EvidenceExporter.cs`, `ResearchEvaluationRunner.cs`
- `Assets/Scripts/Manager/ManagerTactic.cs`, `ManagerTacticModifier.cs`, `ManagerPrototypeController.cs`
- `Assets/Scenes/ManagerMode.unity`
- `Assets/TextMesh Pro/` (TMP Essential Resources, imported with original GUIDs preserved)

**Modified:**
- `Assets/Scripts/Data/OpenFootballLoader.cs` (shrunk to thin orchestrator)
- `Assets/Scripts/Sim/AgentMatchSimulator.cs` (additive `IsGoal`/`HomeTeamScored`/`IsShot` event fields)
- `Assets/Scripts/Sim/PlayerAgent.cs` (additive `GetOverallRating()` — `ToString()` untouched)

## 4. Current known-working behaviour
Verified across multiple user screenshots this session: club selection, full table sync across matchdays, squad view, player inspect, tactic effect on match outcome, full-time stats screen — all confirmed working. Research Mode confirmed still exports correctly-separated, genuine numbers post-fix.

## 5. Current unresolved issues
- **The blind-comparison study tool is the big one.** Per the Major Project Proposal's actual methodology, the qualitative "user testing" portion needs to be a blinded, counterbalanced, Likert-rated comparison of SM-vs-ABM text output (or a forced-choice outcome-plausibility design, extensively discussed) — recruited from FM communities, ~15-20 participants, no PII. This has been **designed in detail in conversation but has zero code written.** Manager Mode as built is a general showcase, not this instrument.
- Subs feature — parked, design agreed (reuse Prev/Next: pick who's off from XI, who's on from Bench, confirm), not built.
- FIFA-card-style stat visual — "eventually," not started.
- WebGL/web deployment of the study tool for wider recruitment — discussed, not decided (pending your own discussion with your COO/Art). If pursued: file-based export won't work in-browser; would need a copyable results-code + Google Form instead.
- Multithreading — deliberately rescoped/dropped due to disclosed personal circumstances (tutors aware). Not an open gap to re-flag.

## 6. Exact next task
**Not locked in.** Two live candidates, in order of dissertation risk:
1. Build the blind-comparison study tool — most dissertation-critical, has real external lead time (participant recruitment), fully designed already.
2. Continue Manager Mode polish — subs feature next in line (already designed), or the FIFA-card visual redesign.

Recommend starting the new chat by confirming which to prioritize — (1) has recruitment lead-time pressure that (2) doesn't.

## 7. Constraints (binding, do not relax without asking)
- Do not modify Research Mode behaviour/results without explicit confirmation.
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation — one explicit ask at a time.

## 8. Evidence numbers (verified, real exported files, post-bugfix)
- **Statistical Model** (100 runs): avg Points MAE 11.59, best 8.05, worst 16.05, ~0.048s exec, ~123,921 sims/min.
- **ABM** (100 runs): avg Points MAE 11.89, median 11.68, stdev 1.55, best 8.15, worst 15.10, ~4.196s exec, ~1,430 sims/min. Title winners: Man City 64%, Liverpool 30%, Arsenal 4%, Brentford 1%, Chelsea 1%.
- Reading: paradigms are statistically near-tied on accuracy; differ ~87x in execution speed — the core trade-off finding.

## 9. Suggested first prompt for the next chat

> Continuing the FootballResearchProject on branch `unity6-ai-prototype` (see your memory files for full context — Manager Mode is fully built and working, not just planned). Working tree is clean, everything's committed and pushed through `532b35f`.
>
> I need to decide what's next: (a) build the blind SM-vs-ABM comparison study tool — the actual dissertation-required methodology instrument, fully designed in a prior conversation but zero code written, with real recruitment lead-time pressure, or (b) continue Manager Mode polish — the subs feature is next in line and already designed (reuse the Prev/Next pattern: pick who's off from the XI, who's on from the bench, confirm swap).
>
> What do you recommend, and do you have any thoughts of your own before we proceed? Same hard constraints as always: don't touch Research Mode without asking, keep Manager Mode separate, no new features without confirming with me first.
