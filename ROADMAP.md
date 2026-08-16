# TFM Development Roadmap

Last updated: 2026-08-15

This is the ordered post-v0.1 roadmap following completion of the day-by-day career calendar, 30-player club squads, generated-player transfer search, and initial transfer-availability system. `BACKLOG.md` remains the detailed issue register; this document records the intended delivery sequence.

## 1. Finish the transfer overhaul

- [x] Replace the temporary position-cycle control with a proper dropdown.
- Add filters for Overall/potential bands, value, height, preferred foot and scouting status.
- Add shortlists, recent searches and saved searches.
- Separate scouted, shortlisted, transfer-listed, loan-listed and free-agent views.
- Add contracts, wages, squad-role promises and player interest.
- Add counteroffers, negotiation deadlines and competing bids.
- Add loans, expiring contracts and pre-contract agreements.
- Add registration deadlines and deadline-day behaviour.
- Move the four development Easter eggs into a development-only free-agent pool.
- Persist rival squads and transfer state properly across saves.

## 2. Matchday squad management

- [x] Select the starting XI and nine-player matchday bench separately.
- [x] Move players between the matchday bench and wider reserves.
- [x] Make auto-pick consider Condition, injuries, quality and position fit.
- [x] Click a pitch position to select eligible primary, secondary and adjacent players ranked by fit, ability and Condition.
- [x] Treat explicitly listed secondary positions as full suitability; label adjacent cover and omit emergency mismatches from the quick selector.
- Automate captain, set-piece and role reassignment when necessary.
- Make AI clubs select and rotate coherent matchday squads.
- Model fixture congestion and recovery using actual calendar dates.

## 3. Match and tactics bug pass

- [x] Make in-match tactical edits reversible drafts until Resume is pressed.
- [x] Restore the pre-match setup after temporary in-match changes.
- [x] Save the complete tactical setup.
- [x] Add the half-time statistics, Make Changes and Resume checkpoint.
- [x] Decide and enforce the five-substitution rule.
- [x] Bind fixture banners, results and Match Events to one active-match snapshot and validate score/event consistency.
- [x] Prevent scorer/provider duplication in the manager match simulator by excluding the creator from shooter selection.
- [x] Relocate the substitutions panel and Make Changes button so growing substitution history cannot overlap controls/statistics.
- [x] Improve tactical-board Condition colours.
- Improve match-screen team-name sizing and alignment.
- Add genuinely narrow formations, beginning with 4-1-2-1-2 Diamond, then assess 4-3-2-1 and 4-3-1-2.

## 4. Formation and tactical-shape overhaul

- [x] Model initial pitch-zone occupancy, width and line-height transforms.
- [x] Model bounded wing/central overload routes, transition space and defensive coverage.
- [x] Make formation-versus-formation route interactions emerge from occupied zones.
- [x] Make per-player attack/defend instructions alter bounded tactical occupancy while route success uses relevant attributes.
- [x] Explain the clearest tactical route advantage and vulnerability on Matchday Prep.
- [x] Add first-generation opponent-aware AI adaptation with one bounded matchup adjustment.
- [x] Require tactical-sensitivity and holy-balance audits for the first interaction slice.
- Add named player roles and duties that alter occupancy and route contribution without replacing real attributes.
- Add genuinely narrow shapes whose central overloads and exposed flanks emerge from their pins.
- Add readable post-match evidence showing which tactical routes actually produced chances.

## 5. Match simulation and performance model

- Preserve the current simulator and holy-balance results as the benchmark rather than rewriting blindly.
- Replace isolated chance events with a structured sequence: recovery, buildup, progression, creation, shot and save/goal.
- Record authoritative participants, tactical route, location, chance quality and outcome for every contribution.
- Derive possession, chances, shots, assists and other match statistics from those structured events.
- Rebuild player ratings around position-specific positive contributions, errors, tactical execution, minutes and opposition difficulty.
- Continue the existing match state after substitutions and tactical changes rather than regenerating an unrelated remainder.
- Expose enough evidence for tactical explanations, AI decisions, persistent career statistics, Moneyball recruitment and narrative systems.
- Recalibrate goals, scorelines, GD, points and table shape through holy-balance regression before replacing the benchmark.

## 6. Intelligent AI clubs

- [x] Partition the Manager Unity coordinator by screen/system responsibility before adding further AI complexity.
- [x] Give every AI club real Condition/injury tracking and rotate for it. AI clubs previously fielded the exact same static XI/bench forever with zero fitness awareness; `ManagerAiSquadRotation` now rests an injured or meaningfully fatigued starter for the best genuinely-better-scoring available cover (bench, then a called-up reserve), using the same fit-tier/Condition-adjusted-Overall scoring the human's own Auto-Pick uses. Explicit fixture-congestion modeling (midweek cup fixtures) remains inapplicable until a secondary competition calendar exists.
- [x] Evaluate positional depth, squad quality and age profile. `ManagerAiSquadDepthEvaluator` scores each of a club's own formation-relevant positions on three explainable terms (missing-cover count, best-option quality against the club's own Starting-XI average, and a succession/age-cliff flag) and identifies the weakest one. Pure analysis, not yet wired to any transfer action - the foundation the next item (need identification/target search) builds on.
- [x] Identify needs and search for tactically appropriate targets. `ManagerAiTransferTargetSearch` finds and ranks genuine upgrades (position fit, quality improvement over the club's current best, age-aware suitability) for a club's weakest position across the wider generated world. Read-only - no budget check, no transaction. AI clubs have no finance/budget tracking at all yet (only the managed team's is ever spent or displayed), so actually signing a target is separate future work needing that foundation first.
- Replace sold, injured, declining and retiring players.
- Develop youth, arrange loans and manage contracts, budgets and wages.
- Give clubs distinct recruitment and financial personalities.
- Run long-career safeguards against hoarding, churn and squad collapse.

## 7. Scouting, academy and player development

- [x] Verify both youth scouts under daily simulation.
- [x] Tune first-pass discovery pacing against calendar time: independent scouts, 2–3-player batches and a persisted ten-day maximum drought. Poaching pressure remains subject to longer playtests.
- Add training schedules and individual development plans.
- [x] Surface first-pass season Overall and whole-number attribute deltas. Richer explanations and persistent career histories remain.
- Persist matches, goals, assists, minutes and average rating.
- Let staff/scout quality affect report speed and accuracy.
- [x] Show public secondary positions before full scouting and treat them as full positional suitability.
- [x] Audit Leadership generation with a permanent 6,000-player distribution test.
- Continue auditing physical and other generated attribute variety.

## 8. Define TFM's identity

Hold a dedicated design pass around the question: **Why play TFM instead of a smaller Football Manager?**

Leading pillars:

- Every save creates a unique generated football world.
- Recruitment centres on finding exact tactical and Moneyball profiles, not memorising a real database.
- The simulation clearly explains why players, transfers and tactics succeed or fail.
- A grounded manager identity/skill system covering coaching, tactical teaching, recruitment networks, relationships, youth development and delegation—with trade-offs rather than flat arcade bonuses.
- Generated stars, club histories and discoveries create careers personal to the player.
- Fewer chores and more consequential decisions than Football Manager.
- Post-alpha candidate: an offline-first narrative/media engine, with optional enhanced generation, that turns verified save events and emergent relationships into unique career history.

## 9. Activate the football pyramid and wider world

- Make the Championship, League One and League Two playable.
- Add promotion, relegation and playoffs.
- Use the National League as a lightweight hidden feeder, subject to the unresolved lower-boundary design.
- Handle clubs entering from below available historical data.
- Activate the top-five European leagues.
- Add domestic cups and European qualification.
- Add the Champions League, Europa League and Conference League.
- Keep wider world clubs available as transfer participants and European opponents.
- Generate future fixture calendars rather than replaying one historical order.

## 10. Reputation, finances and manager career

- Add slowly changing club reputation distinct from match strength.
- Model player interest through reputation, role, wages and competition level.
- Add competition income and sustainable club finances.
- Add manager reputation, job offers, interviews, sackings and club changes.
- Add board expectations and club philosophies.
- Let historical honours influence—but never permanently dictate—reputation.

## 11. UI, accessibility and release preparation

- Use `CLAUDE_DESIGN_MOCKUP_QUEUE.md` as the ordered mockup brief and `UI_HANDOFF.md` as the stable interaction contract.
- Complete a desktop-first visual redesign.
- [x] Add a proper transfer-position dropdown and fast scrolling. Additional filter chips remain part of the transfer overhaul.
- [x] Add the music-volume slider and Inbox read/collapse improvements.
- Add save renaming.
- Test resolutions, controller navigation and accessibility.
- Profile careers containing thousands of generated players.
- Add the public-release switch that removes development Easter eggs.
- Run long-career, migration, balance and release-build testing.

## Immediate continuation order

1. [x] Give AI clubs real Condition/injury-aware matchday rotation, a positional depth/need evaluator and a read-only transfer-target search (`ManagerAiSquadRotation`, `ManagerAiSquadDepthEvaluator`, `ManagerAiTransferTargetSearch`) on top of the completed 30-player club squads. Actually signing a target is the next slice - it needs an AI-club finance/budget foundation first, which doesn't exist yet.
2. Design and implement the structured match-event/performance-model slice while preserving the current holy-balance benchmark.
3. Let AI clubs identify needs, recruit replacements and protect genuine depth using trustworthy performance evidence.
4. Add contracts, player interest, shortlists and richer transfer negotiations.
5. Replace the fixed youth-scout abstraction with the unified senior/youth scouting department.
6. Add narrow formations and the next player-role/tactical-feedback slice, followed by holy-balance regression.
7. Hold the TFM identity and manager-skill-system design session.
8. Activate the wider football pyramid once club-management systems can sustain promotion/relegation.
