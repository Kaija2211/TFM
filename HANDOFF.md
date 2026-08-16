# TFM Current Handoff — 2026-08-16

## State

- Active branch: `main`.
- The former MSc restrictions are retired; this is now the unrestricted game-development line.
- Five batches shipped today: AI squad rotation and a Liverpool-playtest repair cycle (both committed by Thomas via GitHub Desktop), an AI squad depth/need evaluator, an AI transfer target search, and an AI club finance foundation. The latter three are still uncommitted, pending Thomas's instruction.
- Unity may update `ProjectSettings/ProjectAuditorSettings.asset` when audits run; preserve it as user/Editor state unless deliberately changing auditor settings.

## Verified current baseline

- Generated club world with player-derived live strength and 30-player squads.
- Day-by-day career calendar, transfer windows, multi-save/delete flow and save-v5 Form persistence.
- Searchable transfer market first pass, Scouted view, outgoing listings/offers and corrected value curve.
- Reversible live-match tactical/substitution drafts, five-sub limit, half-time checkpoint and immutable active-match result state.
- Formation-zone tactical interactions, player attribute route contribution and bounded opponent-aware AI tactical adjustment.
- Live senior/academy development, visible season deltas, independent youth scouts and varied report uncertainty.
- Every AI-controlled club now has real Condition decay/recovery and injury risk (`ManagerMatchdayCondition`), and rotates its matchday XI for it (`ManagerAiSquadRotation`).
- The game saves on quit, Academy sort/duplicate-list is fixed, Inbox/Save Browser reset scroll position on open, the Hub's Inbox button highlights when unread, scouting-discovery messages are batched, and season-2 transfer bids give clear in-dialog failure feedback plus an Inbox message on wage-bill deduction.
- `ManagerAiSquadDepthEvaluator` scores every AI club's own formation-relevant positions and identifies each club's weakest one.
- `ManagerAiTransferTargetSearch` finds and ranks genuine upgrades for a club's weakest position across the wider generated world.
- **New this session:** every AI club now has a real transfer budget and pays its own annual wage bill (`ManagerClubFinance.ApplyAnnualWageBill`), seeded the moment its squad first exists and deducted at every season rollover, silently (no Inbox spam - that principle already applied to AI Condition/injuries). Live-verified against a real season: Liverpool's own wage bill (£140.8m) deducted correctly, all ~20 clubs' rollovers ran with zero errors. AI clubs still can't spend this budget on anything yet.

## Verification status

- `Assembly-CSharp` and `Assembly-CSharp-Editor`: compile with zero errors.
- Existing runtime warnings only: unassigned `SimulationRunner.Config.token`; nullable `FootballClubRegistry.FoundedYear` skipped by Unity serialization.
- Unity audits passed (10 total): Manager Career Systems, Leadership Distribution, World Generation Profile, Player Derived Strength, Tactical Shape, Manager Holy Balance (unchanged 2.55–2.95 band), AI Squad Rotation, AI Squad Depth Evaluator, AI Transfer Target Search, and the new **AI Club Finance**.
- **Important, carried over from earlier today:** giving AI clubs genuine fatigue intentionally moves the *with-AI-rotation* goals/game figure down to roughly 2.3–2.5 (guarded at 2.15–2.60) versus the historic ~2.7–2.9 reference — see `PROJECT_CONTEXT_FOR_AI.md` §12 and DEVLOG.
- The depth evaluator's "weakest position" signal is real but genuinely small at the generated Premier League's compressed quality band (intentional world-generation design). The target search integration audit found 19/20 clubs had at least one genuine upgrade target available. The finance audit confirmed budgets and wage bills scale sensibly with squad/club strength across a full 20-club league.
- Live in-Editor verification today covered: the Liverpool playtest-fix batch (Scouting/Academy sort, Inbox), and a full new-career season-1-through-rollover run confirming the finance wiring's exact arithmetic. The depth evaluator and target search are pure analysis with no UI wiring yet, verified via audit only.
- A `TMP_SubMeshUI.UpdateMaterial` NullReferenceException continues to recur (End-of-Season panel, Scouting panel, and again on this session's End-of-Season/Inbox opens) — same signature every time, always via `GameObject.SetActive` during this session's rapid headless automation, never during normal interactive play. Reads as a testing-harness timing artifact at this point (now seen 4+ times across unrelated screens), not a real gameplay bug — flagged in BACKLOG, not chased further.
- Latest manual test (from Thomas): Sunderland career through November; no recurrence of stale fixture/result state. Record of 1W–1D–6L is evidence to monitor, not yet a balance defect.

## Immediate next sequence

1. **Actual AI recruitment** — bid/sale/contract decisions and squad changes acting on `ManagerAiTransferTargetSearch`'s output using each club's now-real `ManagerClubFinance` budget. This is the transaction/decision layer; the finance foundation it needed is done. Needs care: it's the first AI-club work that would touch shared/human-visible state (could an AI club compete with the human for the same target?).
2. Structured match-event and position-specific performance-model design, retaining the current simulator as the holy-balance benchmark.
3. Long-career squad-health safeguards (hoarding, churn, collapse).
4. Contracts, player interest, shortlists and richer negotiations.
5. Unified senior/youth scouting department.
6. Narrow formations (diamond first), named roles and richer tactical feedback; rerun holy balance.
7. TFM identity/manager progression design, then football-pyramid activation.

## Important remaining bugs/UX

- The annual wage bill formula itself (`ManagerClubFinance.GetAnnualWage`) is unaudited for realism/balance and can plausibly exceed a season's income for a competitive squad, driving the unclamped budget negative — now visible for both the managed team (Inbox message) and AI clubs (silently), but the underlying balance hasn't been reviewed.
- The `TMP_SubMeshUI` NullReferenceException noted above — worth a real root-cause pass only if it's ever reproduced through normal play rather than automated testing.
- Guard `PickGoalkeeper` against an empty XI even though current sale rules make it difficult to reach.
- Improve direct reserve/matchday bench management.
- Relocate/polish live substitutions UI and enlarge match-screen team names.
- Audit physical-attribute distributions and lower-club squad economics over more generated worlds.
- Add persistent player career statistics and broader development badges.
- Minor: chosen academy focus-attribute picks (which stat is currently being trained) don't persist across save/load, though already-earned development does.

Detailed scope and acceptance criteria live in `BACKLOG.md`; ordered delivery lives in `ROADMAP.md`; completed work is recorded in `DEVLOG.md`.
