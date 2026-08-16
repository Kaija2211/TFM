# TFM Current Handoff — 2026-08-15

## State

- Active branch: `main`.
- The former MSc restrictions are retired; this is now the unrestricted game-development line.
- The working tree contains the current post-v0.1 implementation batch and is intentionally uncommitted pending Thomas's instruction.
- Unity may update `ProjectSettings/ProjectAuditorSettings.asset` when audits run; preserve it as user/Editor state unless deliberately changing auditor settings.

## Verified current baseline

- Generated club world with player-derived live strength and 30-player squads.
- Day-by-day career calendar, transfer windows, multi-save/delete flow and save-v5 Form persistence.
- Searchable transfer market first pass, Scouted view, outgoing listings/offers and corrected value curve.
- Reversible live-match tactical/substitution drafts, five-sub limit, half-time checkpoint and immutable active-match result state.
- Formation-zone tactical interactions, player attribute route contribution and bounded opponent-aware AI tactical adjustment.
- Live senior/academy development, visible season deltas, independent youth scouts and varied report uncertainty.

## Verification status

- `Assembly-CSharp` and `Assembly-CSharp-Editor`: compile with zero errors.
- Existing runtime warnings only: unassigned `SimulationRunner.Config.token`; nullable `FootballClubRegistry.FoundedYear` skipped by Unity serialization.
- Unity audits passed: Manager Career Systems and Leadership Distribution.
- Latest holy-balance evidence: 76,000 matches, 2.699 goals per game.
- Latest manual test: Sunderland career through November; no recurrence of stale fixture/result state. Record of 1W–1D–6L is evidence to monitor, not yet a balance defect.

## Immediate next sequence

1. AI squad evaluation and rotation across the full 30-player pool.
2. Structured match-event and position-specific performance-model design, retaining the current simulator as the holy-balance benchmark.
3. AI recruitment/replacement decisions and long-career squad-health safeguards.
4. Contracts, player interest, shortlists and richer negotiations.
5. Unified senior/youth scouting department.
6. Narrow formations (diamond first), named roles and richer tactical feedback; rerun holy balance.
7. TFM identity/manager progression design, then football-pyramid activation.

## Important remaining bugs/UX

- Guard `PickGoalkeeper` against an empty XI even though current sale rules make it difficult to reach.
- Improve direct reserve/matchday bench management.
- Relocate/polish live substitutions UI and enlarge match-screen team names.
- Audit physical-attribute distributions and lower-club squad economics over more generated worlds.
- Add persistent player career statistics and broader development badges.

Detailed scope and acceptance criteria live in `BACKLOG.md`; ordered delivery lives in `ROADMAP.md`; completed work is recorded in `DEVLOG.md`.
