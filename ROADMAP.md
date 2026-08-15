# TFM Development Roadmap

Last updated: 2026-08-15

This is the ordered post-v0.1 roadmap following completion of the day-by-day career calendar, 30-player club squads, generated-player transfer search, and initial transfer-availability system. `BACKLOG.md` remains the detailed issue register; this document records the intended delivery sequence.

## 1. Finish the transfer overhaul

- Replace the temporary position-cycle control with a proper dropdown.
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
- Relocate the substitutions panel and Make Changes button.
- Improve tactical-board stamina colours and match-screen team-name sizing.

## 4. Formation and tactical-shape overhaul

- Model pitch zones, width, compactness and line height.
- Model wing and central overloads, transition space and defensive coverage.
- Make formation-versus-formation interactions emerge from occupied zones.
- Make player roles and tactical sliders interact with relevant attributes.
- Explain tactical advantages and vulnerabilities clearly to the manager.
- Add opponent-aware AI tactical adaptation.
- Require tactical-sensitivity and holy-balance audits before acceptance.

## 5. Intelligent AI clubs

- Evaluate positional depth, squad quality and age profile.
- Identify needs and search for tactically appropriate targets.
- Replace sold, injured, declining and retiring players.
- Rotate for Condition and fixture congestion.
- Develop youth, arrange loans and manage contracts, budgets and wages.
- Give clubs distinct recruitment and financial personalities.
- Run long-career safeguards against hoarding, churn and squad collapse.

## 6. Scouting, academy and player development

- [x] Verify both youth scouts under daily simulation.
- Tune discovery and poaching pacing against calendar time.
- Add training schedules and individual development plans.
- Explain the causes of player growth and decline.
- Persist matches, goals, assists, minutes and average rating.
- Let staff/scout quality affect report speed and accuracy.
- Show public secondary positions before full scouting.
- Continue auditing generated mental and physical attribute variety.

## 7. Define TFM's identity

Hold a dedicated design pass around the question: **Why play TFM instead of a smaller Football Manager?**

Leading pillars:

- Every save creates a unique generated football world.
- Recruitment centres on finding exact tactical and Moneyball profiles, not memorising a real database.
- The simulation clearly explains why players, transfers and tactics succeed or fail.
- A grounded manager identity/skill system covering coaching, tactical teaching, recruitment networks, relationships, youth development and delegation—with trade-offs rather than flat arcade bonuses.
- Generated stars, club histories and discoveries create careers personal to the player.
- Fewer chores and more consequential decisions than Football Manager.

## 8. Activate the football pyramid and wider world

- Make the Championship, League One and League Two playable.
- Add promotion, relegation and playoffs.
- Use the National League as a lightweight hidden feeder, subject to the unresolved lower-boundary design.
- Handle clubs entering from below available historical data.
- Activate the top-five European leagues.
- Add domestic cups and European qualification.
- Add the Champions League, Europa League and Conference League.
- Keep wider world clubs available as transfer participants and European opponents.
- Generate future fixture calendars rather than replaying one historical order.

## 9. Reputation, finances and manager career

- Add slowly changing club reputation distinct from match strength.
- Model player interest through reputation, role, wages and competition level.
- Add competition income and sustainable club finances.
- Add manager reputation, job offers, interviews, sackings and club changes.
- Add board expectations and club philosophies.
- Let historical honours influence—but never permanently dictate—reputation.

## 10. UI, accessibility and release preparation

- Complete a desktop-first visual redesign.
- [x] Add a proper transfer-position dropdown and fast scrolling. Additional filter chips remain part of the transfer overhaul.
- [x] Add the music-volume slider and Inbox read/collapse improvements.
- Add save renaming.
- Test resolutions, controller navigation and accessibility.
- Profile careers containing thousands of generated players.
- Add the public-release switch that removes development Easter eggs.
- Run long-career, migration, balance and release-build testing.

## Immediate continuation order

1. Build the tactical-zone and formation-interaction model.
2. Add AI squad evaluation, rotation and recruitment.
3. Add contracts, player interest and richer transfer negotiations.
4. Hold the TFM identity and manager-skill-system design session.
5. Activate the wider football pyramid.
