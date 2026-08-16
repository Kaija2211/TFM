# TFM — Project Summary

## What TFM is

TFM is a Unity/C# football management simulation. The core design choice is that the
world is generated, not real: clubs, players, career histories and league tables are
procedurally created per save rather than pulled from a licensed database of real
footballers. You manage one club — squad selection, tactics, transfers, scouting,
youth development, finances — across a persistent multi-season career, while every
other club in the league is run by its own AI decision-making rather than sitting
static.

## My role

I'm the sole developer and designer. That covers the match simulation and statistical
model, the tactical-shape/formation engine, the procedural world generator, the
save/load system, the full UI layer, and a standalone .NET CLI tool
(`Tools/OpenFootballImport`) that imports and normalizes historical football data used
to train the simulator.

## Major technical achievements

**Partitioned controller + extracted services.** The Unity-facing coordinator
(`ManagerPrototypeController`) could easily have become a single monolithic
`MonoBehaviour`. Instead it's split into responsibility-scoped partial files (one per
screen/system — matchday, transfers, scouting, save/load, etc.), and any football
logic that doesn't need a scene reference is pulled out into plain C# classes with
deterministic Editor audits, not left inline. See
`Docs/Technical/MANAGER_CONTROLLER_ARCHITECTURE.md`.

**AI clubs that actually run themselves.** The other ~19 clubs in the league aren't
scripted or static. `ManagerAiSquadRotation` gives them real Condition/injury tracking
and rotates their matchday XI for it. `ManagerAiSquadDepthEvaluator` scores each club's
own positional weaknesses. `ManagerAiTransferTargetSearch` finds upgrades for those
weaknesses across the generated world. `ManagerClubFinance` gives every club a real
budget and wage bill. `ManagerAiTransferExecutor` closes the loop — an AI club can
actually complete a transfer if it can afford it and the selling club keeps adequate
cover. Each of those is a separate, independently testable service, built and verified
one slice at a time rather than as one large "AI system."

**Structured match simulation with a statistical regression benchmark.** Match
outcomes come from a modeled sequence (recovery, buildup, chance creation, shot
outcome) rather than a single random roll per chance, trained against real historical
match data via the OpenFootball import tool. Any change to selection, tactics, or
squad generation that could move scoring rates gets checked against a goals-per-game
regression band before it ships, so balance regressions get caught before a playtest
does.

## Key engineering challenges

**AI rotation that looked stable but wasn't.** The first version of AI matchday
rotation re-ran a full best-XI reselection every match. A season-scale Editor audit
caught it thrashing the starting XI on close to 100% of fixtures — Condition decay has
no stable equilibrium short of an actual rest, so there was always someone marginally
tired enough to trigger a swap. The fix wasn't more tuning of the same approach; it
was changing the trigger condition — every slot gets reconsidered every match, but a
challenger only replaces the incumbent if it clears a hysteresis margin, and an injury
always forces a swap regardless of margin. A second attempt at a simpler
threshold-gate ("only reconsider once *this player's* Condition drops below X") fixed
the thrashing but introduced a worse problem: a recovered, clearly-better bench player
could get permanently locked out because nothing else was tired enough to trigger a
re-evaluation either. The version that shipped is the one that actually measured
better on goals/game and XI stability, not the first one that looked reasonable.

**A test that was accidentally testing the wrong thing.** `ManagerAiSquadDepthEvaluator`
scores a club's weakest position by comparing its best available player against the
squad's own average quality. The first version compared against the *whole 30-man
squad's* average — but bench and reserve players are deliberately generated at a lower
quality tier than the starting XI, so the whole-squad average was always dragged down,
which made every position look adequately covered even for a deliberately weak test
squad. The audit that caught this used a club with a thin, ageing goalkeeper — an
already-narrow test case that happened to expose the bug. Fixed by comparing against
the Starting XI's own average instead, a same-tier comparison. The same investigation
also found that CB, as a test position, is a bad choice for isolating "no cover"
scenarios, because RB/LB/DM all count as legitimate adjacent cover for CB — every
CB-based test I wrote kept quietly passing for the wrong reason until I switched the
test scenarios to goalkeeper, which has no adjacency cover at all.

**A threshold that was guessed wrong, and the data that fixed it.** `ManagerAiTransfer
Executor` only lets a club shop for a replacement if its weakest position's need score
clears a minimum. I started with 10, an arbitrary round number. A full 20-club league
pass at that threshold completed zero transfers — not because nothing needed fixing,
but because the threshold itself was wrong. The depth evaluator's own earlier
statistical audit had already shown that real generated squads usually score exactly 0
on this metric, with only occasional spikes as low as ~1.3. Lowering the threshold to
0.5 against that actual data, rather than guessing again, got a realistic ~1-in-20
clubs completing a transfer per pass.

## Lessons learned

The pattern across all three of the above is the same: a plausible-looking first
version passed a shallow check and failed a season-scale or full-league one, and the
fix came from looking at what the system actually produced rather than tuning the
existing approach further. I now default to writing the season-scale/full-population
Editor audit *before* trusting a smaller unit test, because the smaller test is the
one that's more likely to be accidentally testing a case the real data never
produces (or masking one it does).

For the full, unedited session-by-session history — including bugs, dead ends, and
the exact goals-per-game numbers behind the balance calls above — see
`Docs/Development/DEVLOG.md`.
