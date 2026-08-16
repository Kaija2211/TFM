# TFM Current Handoff — 2026-08-16

## State

- Active branch: `main`.
- The former MSc restrictions are retired; this is now the unrestricted game-development line.
- The working tree contains this session's AI squad-rotation batch and is intentionally uncommitted pending Thomas's instruction.
- Unity may update `ProjectSettings/ProjectAuditorSettings.asset` when audits run; preserve it as user/Editor state unless deliberately changing auditor settings.

## Verified current baseline

- Generated club world with player-derived live strength and 30-player squads.
- Day-by-day career calendar, transfer windows, multi-save/delete flow and save-v5 Form persistence.
- Searchable transfer market first pass, Scouted view, outgoing listings/offers and corrected value curve.
- Reversible live-match tactical/substitution drafts, five-sub limit, half-time checkpoint and immutable active-match result state.
- Formation-zone tactical interactions, player attribute route contribution and bounded opponent-aware AI tactical adjustment.
- Live senior/academy development, visible season deltas, independent youth scouts and varied report uncertainty.
- **New this session:** every AI-controlled club now has real Condition decay/recovery and injury risk (`ManagerMatchdayCondition`), and rotates its matchday XI for it (`ManagerAiSquadRotation`) — previously every AI club fielded the exact same static XI/bench forever with zero fitness awareness. The managed team's own Auto-Pick button was refactored onto a shared service (`ManagerSquadAutoPicker`) in the same pass, with no behavior change.

## Verification status

- `Assembly-CSharp` and `Assembly-CSharp-Editor`: compile with zero errors.
- Existing runtime warnings only: unassigned `SimulationRunner.Config.token`; nullable `FootballClubRegistry.FoundedYear` skipped by Unity serialization.
- Unity audits passed: Manager Career Systems, Leadership Distribution, World Generation Profile, Player Derived Strength, Tactical Shape, Manager Holy Balance (unchanged 2.55–2.95 band), and the new **AI Squad Rotation** audit.
- Latest holy-balance evidence: 76,000 matches, 2.699 goals per game — this is the un-rotated (managed-team-only Condition) baseline and still holds unchanged, since `ManagerHolyBalanceAudit` deliberately doesn't exercise AI Condition.
- **New, important:** giving AI clubs genuine fatigue for the first time intentionally moves the *with-AI-rotation* goals/game figure down to roughly 2.3–2.5 (the new `ManagerAiSquadRotationAudit` guards 2.15–2.60) — every hysteresis-margin tuning tried landed in this neighborhood regardless of algorithm details, so this reads as a structural consequence of the feature, not a tuning artifact. See DEVLOG's 2026-08-16 entry for the full reasoning. **This means the real shipped game's observed goals/game will now sit noticeably lower than the historic ~2.7–2.9 reference** — flagged explicitly here so it isn't mistaken for a regression later.
- Live in-Editor verification: new career as Liverpool, full season simulated with AI rotation live on all 380 fixtures, zero errors from the new code, plausible final table (3rd place) and finances.
- Latest manual test: Sunderland career through November; no recurrence of stale fixture/result state. Record of 1W–1D–6L is evidence to monitor, not yet a balance defect.

## Immediate next sequence

1. AI squad depth evaluation, need identification and recruitment/replacement — the next slice of the Intelligent AI Clubs epic, building on this session's Condition/rotation foundation.
2. Structured match-event and position-specific performance-model design, retaining the current simulator as the holy-balance benchmark.
3. Long-career squad-health safeguards (hoarding, churn, collapse).
4. Contracts, player interest, shortlists and richer negotiations.
5. Unified senior/youth scouting department.
6. Narrow formations (diamond first), named roles and richer tactical feedback; rerun holy balance.
7. TFM identity/manager progression design, then football-pyramid activation.

## Important remaining bugs/UX

- **New:** `ShowEndOfSeasonPanel` threw a `NullReferenceException` (`TMP_SubMeshUI.UpdateMaterial` via `Image.OnDisable`/masking) when End of Season was reached by simulating an entire season immediately from a brand-new career, before any other screen had been visited. The panel still displayed correctly with real data despite the exception — reads as a lazy-initialization/TMP-material gotcha, not confirmed game-breaking, but not yet root-caused either.
- Guard `PickGoalkeeper` against an empty XI even though current sale rules make it difficult to reach.
- Improve direct reserve/matchday bench management.
- Relocate/polish live substitutions UI and enlarge match-screen team names.
- Audit physical-attribute distributions and lower-club squad economics over more generated worlds.
- Add persistent player career statistics and broader development badges.

Detailed scope and acceptance criteria live in `BACKLOG.md`; ordered delivery lives in `ROADMAP.md`; completed work is recorded in `DEVLOG.md`.
