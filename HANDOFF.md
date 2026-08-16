# TFM Current Handoff — 2026-08-16

## State

- Active branch: `main`.
- The former MSc restrictions are retired; this is now the unrestricted game-development line.
- The working tree contains three batches from today (AI squad rotation, a Liverpool-playtest repair cycle, an AI squad depth/need evaluator) and is intentionally uncommitted pending Thomas's instruction.
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
- **New this session:** `ManagerAiSquadDepthEvaluator` scores every AI club's own formation-relevant positions on missing-cover count, quality-vs-own-Starting-XI-average, and succession/age-cliff risk, identifying each club's weakest position. Pure analysis, not yet wired to any transfer/recruitment action - the foundation the next slice (need identification/target search) builds on.

## Verification status

- `Assembly-CSharp` and `Assembly-CSharp-Editor`: compile with zero errors.
- Existing runtime warnings only: unassigned `SimulationRunner.Config.token`; nullable `FootballClubRegistry.FoundedYear` skipped by Unity serialization.
- Unity audits passed (8 total): Manager Career Systems, Leadership Distribution, World Generation Profile, Player Derived Strength, Tactical Shape, Manager Holy Balance (unchanged 2.55–2.95 band), AI Squad Rotation, and the new **AI Squad Depth Evaluator**.
- **Important, carried over from earlier today:** giving AI clubs genuine fatigue for the first time intentionally moves the *with-AI-rotation* goals/game figure down to roughly 2.3–2.5 (guarded at 2.15–2.60). The real shipped game's observed goals/game will now sit noticeably lower than the historic ~2.7–2.9 reference — see `PROJECT_CONTEXT_FOR_AI.md` §12 and DEVLOG.
- **New:** the depth evaluator's "weakest position" signal is real but genuinely small at the generated Premier League's compressed quality band (matches the world-generation design's own intentional "not a gulf that makes most of the division noncompetitive" philosophy) - its statistical audit uses 400 samples specifically because the effect size needed a large, stable measurement rather than a knife-edge single-seed pass. Two real formula bugs (formation-irrelevant positions like RWB/LWB dominating as noise; comparing against the whole 30-man squad's average instead of the Starting XI's) were caught and fixed via this same audit before shipping - see DEVLOG.
- Live in-Editor verification (playtest-fix batch): full click-through New Career → Liverpool → Hub → Scouting/Academy tab → repeated sort clicks (row count confirmed stable across a real frame boundary) → Inbox (unread badge colour confirmed) → Transfers screen. The depth evaluator itself is pure analysis with no UI wiring yet, so it was verified via its audit only, not a live playthrough.
- A `TMP_SubMeshUI.UpdateMaterial` NullReferenceException recurred in a second, unrelated screen (Scouting, after first appearing in End-of-Season) during this session's rapid headless automation — same signature both times, both only via automated testing, not normal play; reads as a testing-harness timing artifact, flagged in BACKLOG rather than chased further this session.
- Latest manual test (from Thomas): Sunderland career through November; no recurrence of stale fixture/result state. Record of 1W–1D–6L is evidence to monitor, not yet a balance defect.

## Immediate next sequence

1. AI need identification and target search — the next slice of the Intelligent AI Clubs epic, consuming `ManagerAiSquadDepthEvaluator`'s weakest-position output. Actual recruitment (bids, budget checks, contracts) follows once targeting exists.
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
