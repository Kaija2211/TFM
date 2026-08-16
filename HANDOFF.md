# TFM Current Handoff — 2026-08-16

## State

- Active branch: `main`.
- The former MSc restrictions are retired; this is now the unrestricted game-development line.
- Four batches shipped today: AI squad rotation (committed separately by Thomas via GitHub Desktop as `3ed9046`), a Liverpool-playtest repair cycle, an AI squad depth/need evaluator, and an AI transfer target search. The latter three are still uncommitted, pending Thomas's instruction.
- Unity may update `ProjectSettings/ProjectAuditorSettings.asset` when audits run; preserve it as user/Editor state unless deliberately changing auditor settings.

## Verified current baseline

- Generated club world with player-derived live strength and 30-player squads.
- Day-by-day career calendar, transfer windows, multi-save/delete flow and save-v5 Form persistence.
- Searchable transfer market first pass, Scouted view, outgoing listings/offers and corrected value curve.
- Reversible live-match tactical/substitution drafts, five-sub limit, half-time checkpoint and immutable active-match result state.
- Formation-zone tactical interactions, player attribute route contribution and bounded opponent-aware AI tactical adjustment.
- Live senior/academy development, visible season deltas, independent youth scouts and varied report uncertainty.
- Every AI-controlled club now has real Condition decay/recovery and injury risk (`ManagerMatchdayCondition`), and rotates its matchday XI for it (`ManagerAiSquadRotation`) - previously every AI club fielded the exact same static XI/bench forever with zero fitness awareness. The managed team's own Auto-Pick button was refactored onto a shared service (`ManagerSquadAutoPicker`) in the same pass, with no behavior change.
- The game now saves on quit (not just via the Hub's "Exit to Title" button), the Academy sort/duplicate-list bug is fixed, Inbox/Save Browser reset scroll position on open, the Hub's Inbox button visibly highlights when unread, scouting-discovery Inbox messages are batched instead of one-per-prospect, and season-2 transfer bids give clear in-dialog failure feedback (plus an Inbox message when the annual wage bill is deducted).
- `ManagerAiSquadDepthEvaluator` scores every AI club's own formation-relevant positions on missing-cover count, quality-vs-own-Starting-XI-average, and succession/age-cliff risk, identifying each club's weakest position.
- **New this session:** `ManagerAiTransferTargetSearch` finds and ranks genuine upgrades (position fit, quality improvement, age-aware suitability) for a club's weakest position across the wider generated world, fed directly by the depth evaluator above. Read-only - no budget check, no transaction, since AI clubs have no finance/budget tracking of any kind yet.

## Verification status

- `Assembly-CSharp` and `Assembly-CSharp-Editor`: compile with zero errors.
- Existing runtime warnings only: unassigned `SimulationRunner.Config.token`; nullable `FootballClubRegistry.FoundedYear` skipped by Unity serialization.
- Unity audits passed (9 total): Manager Career Systems, Leadership Distribution, World Generation Profile, Player Derived Strength, Tactical Shape, Manager Holy Balance (unchanged 2.55–2.95 band), AI Squad Rotation, AI Squad Depth Evaluator, and the new **AI Transfer Target Search**.
- **Important, carried over from earlier today:** giving AI clubs genuine fatigue for the first time intentionally moves the *with-AI-rotation* goals/game figure down to roughly 2.3–2.5 (guarded at 2.15–2.60). The real shipped game's observed goals/game will now sit noticeably lower than the historic ~2.7–2.9 reference — see `PROJECT_CONTEXT_FOR_AI.md` §12 and DEVLOG.
- The depth evaluator's "weakest position" signal is real but genuinely small at the generated Premier League's compressed quality band (intentional world-generation design, not a bug) - its audit uses 400 samples for a stable measurement. Two real formula bugs were caught and fixed in that work before shipping - see DEVLOG's first 2026-08-16 depth-evaluator entry.
- The target search integration audit found 19/20 generated clubs had at least one genuine upgrade target available for their weakest position across the full 20-club world, with every returned target independently verified as an actual fit-and-quality improvement.
- Live in-Editor verification (playtest-fix batch only): full click-through New Career → Liverpool → Hub → Scouting/Academy tab → repeated sort clicks (row count confirmed stable across a real frame boundary) → Inbox (unread badge colour confirmed) → Transfers screen. Both AI-club analysis services (depth evaluator, target search) are pure analysis with no UI wiring yet, so they were verified via their audits only.
- A `TMP_SubMeshUI.UpdateMaterial` NullReferenceException recurred in a second, unrelated screen (Scouting, after first appearing in End-of-Season) during this session's rapid headless automation — same signature both times, both only via automated testing, not normal play; reads as a testing-harness timing artifact, flagged in BACKLOG rather than chased further this session.
- Latest manual test (from Thomas): Sunderland career through November; no recurrence of stale fixture/result state. Record of 1W–1D–6L is evidence to monitor, not yet a balance defect.

## Immediate next sequence

1. **AI-club finance/budget foundation.** No AI club has any budget tracking today (only the managed team's is ever spent or displayed) - this blocks `ManagerAiTransferTargetSearch`'s output from being acted on and is the real prerequisite for actual AI recruitment (bids, contracts, squad changes).
2. Structured match-event and position-specific performance-model design, retaining the current simulator as the holy-balance benchmark.
3. Long-career squad-health safeguards (hoarding, churn, collapse).
4. Contracts, player interest, shortlists and richer negotiations.
5. Unified senior/youth scouting department.
6. Narrow formations (diamond first), named roles and richer tactical feedback; rerun holy balance.
7. TFM identity/manager progression design, then football-pyramid activation.

## Important remaining bugs/UX

- The annual wage bill (`DeductManagedTeamWageBill`) is unaudited and can plausibly exceed a season's income for a competitive squad, driving the unclamped budget negative. Now visible via an Inbox message instead of silent, but the underlying balance hasn't been reviewed — sample real numbers across club tiers before touching the formula.
- The `TMP_SubMeshUI` NullReferenceException noted above — worth a real root-cause pass only if it's ever reproduced through normal play rather than automated testing.
- Guard `PickGoalkeeper` against an empty XI even though current sale rules make it difficult to reach.
- Improve direct reserve/matchday bench management.
- Relocate/polish live substitutions UI and enlarge match-screen team names.
- Audit physical-attribute distributions and lower-club squad economics over more generated worlds.
- Add persistent player career statistics and broader development badges.
- Minor: chosen academy focus-attribute picks (which stat is currently being trained) don't persist across save/load, though already-earned development does — a smaller, separate gap from the quit-save fix.

Detailed scope and acceptance criteria live in `BACKLOG.md`; ordered delivery lives in `ROADMAP.md`; completed work is recorded in `DEVLOG.md`.
