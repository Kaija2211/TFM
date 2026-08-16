# Manager controller architecture

`ManagerPrototypeController` remains the Unity-facing scene coordinator, but its implementation is physically partitioned by responsibility. Unity still sees one partial `MonoBehaviour`, so existing scene references and serialized fields remain compatible.

## File ownership

- `ManagerPrototypeController.cs` — serialized scene references, shared runtime state, startup, title/settings chrome and cross-screen UI recovery helpers.
- `.TeamSelect.cs` — new-career reset, club selection and initial model/career setup.
- `.HubAndSeason.cs` — mentality controls, season hub, season rollover, player aging, retirement and wages.
- `.SaveLoad.cs` — save DTO construction/application, continue/load browser and save deletion.
- `.Scouting.cs` — youth missions, world prospects and academy UI/actions.
- `.Transfers.cs` — transfer search, scouting, bids, signing and selling UI/actions.
- `.Inbox.cs` — Inbox navigation, rendering and message expansion.
- `.Career.cs` — trophy room, career record and finance views.
- `.Tactics.cs` — tactics board, tactical settings, formation changes, auto-pick and in-match tactical drafts.
- `.SquadAndPlayer.cs` — squad list, player detail, role instructions and development-focus controls.
- `.Matchday.cs` — calendar advancement, match preparation, simulation/replay, half-time/full-time and post-match state.
- `.World.cs` — world-profile loading, squad creation, live strength and reserve/role lookup.
- `.DeveloperContent.cs` — development-only Easter egg identity/stat overrides.

## Boundary rule

Partial files are a navigation and merge-conflict improvement, not permission to keep growing the controller indefinitely. New reusable football logic should live in focused classes and be called by the relevant partial. Examples include `ManagerAiTacticalPlanner`, `ManagerTacticalShape`, `ManagerPlayerDerivedStrength`, `ManagerSquadAutoPicker` (shared best-XI selection, used by the managed team's Auto-Pick button), `ManagerMatchdayCondition` (shared Condition decay/injury-risk simulation, no human-facing side effects), `ManagerAiSquadRotation` (the AI-club matchday selection policy built on both of those), `ManagerAiSquadDepthEvaluator` (pure positional depth/quality/succession analysis), `ManagerAiTransferTargetSearch` (read-only target ranking consuming the depth evaluator's output), `ManagerClubFinance.ApplyAnnualWageBill` (budget seed/wage-deduction logic now shared by every club, not just the managed team), and `ManagerAiTransferExecutor` (the transaction layer - completes an AI-to-AI transfer once per season rollover when the depth evaluator's need signal, the target search's ranked candidates, and the buyer's/seller's finance all line up), called from `.HubAndSeason.cs`'s `RunAiTransferWindow`. See DEVLOG's 2026-08-16 entries.

Code belongs in a controller partial only when it directly coordinates Unity views, navigation, or the career lifecycle. Rules that can be evaluated without a scene object should be plain C# services with deterministic editor audits.

## Safe extraction path

Future structural work should proceed one responsibility at a time:

1. Introduce a tested service around existing behaviour.
2. Route one partial through that service without mixing in unrelated feature changes.
3. Compile both runtime and editor assemblies.
4. Run the Manager Career Systems audit and any relevant specialist audit.
5. Run the holy-balance audit whenever football outcomes, selection, development or squad quality may change.

The next intended piece is the actual AI recruitment/transaction layer (bid, sale, contract decisions) acting on `ManagerAiTransferTargetSearch`'s output using each club's now-real `ManagerClubFinance` budget.
