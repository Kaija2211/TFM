# TFM UI handoff

Last updated: 2026-08-15

This is the working contract for the upcoming visual-design pass. The current UI is functional scaffolding, mostly created at runtime by `ManagerPrototypeController`; layouts, typography, colours and component styling may be replaced without changing the behaviours below.

## Design target

- Desktop-first football management game, not a mobile card game.
- Dense information should remain quickly scannable at 1920×1080.
- Prioritise clear hierarchy, compact controls, fast list navigation and obvious selected/disabled states.
- Preserve reusable patterns across Hub, Squad, Transfers, Inbox and Matchday.

## Stable gameplay states

### Hub

- Current calendar date and next fixture.
- League table includes played, won, drawn, lost, goal difference, points and form/record data.
- Continue advances one calendar day at a time internally and stops for a fixture or meaningful inbox event.
- Navigation: Squad/Tactics, Transfers, Scouting/Academy, Inbox, finances/settings and save controls.

### Squad and tactics board

- Club roster consists of 30 players: 11 starters, nine named substitutes and ten reserves.
- Starting XI and bench are ordered collections; order matters for formation slots and bench display.
- A non-starter's detail screen exposes either `CHANGE SUBSTITUTE` or `SELECT AS SUBSTITUTE`.
- Selecting a substitute swaps one bench player with one reserve. This does not change the XI.
- Auto-pick considers position fit, Overall, Condition and injuries.
- Required visual states: starter, substitute, reserve, injured, unavailable, low Condition, selected and drag target.
- Condition needs a stronger warning gradient than the current mostly-green treatment.

### Live match

- Header data: active fixture home club, away club, score and minute.
- Live data: event feed, possession proxy, chances, shots, shots on target, player ratings, substitutions and mentality.
- The fixture shown from kickoff through Match Events is immutable even when career state advances.
- `MAKE CHANGES` opens a paused tactical-board draft.
- Dragging and formation changes are provisional until the user leaves the board to resume.
- Players may be dragged back into the XI while drafting; no substitution is consumed yet.
- Five substitutions maximum. Only the named nine-player bench is eligible.
- Committing once applies the final delta, logs substitutions and resimulates the remaining match once.
- In-match changes are temporary; the pre-match formation, XI and bench return after full-time.

### Half-time

- Match pauses automatically after minute 45.
- Required content: `HALF TIME`, score, possession, chances created, shots and shots on target.
- Required actions: `MAKE CHANGES` and `START SECOND HALF`.
- Returning from Make Changes must return to the half-time state, not silently resume play.

### Full-time and Match Events

- Required content: correct active fixture, score, goal scorers/timeline and full match statistics.
- Match Events must use the same active fixture, score and event collection as the summary.
- Goal-event counts are validated against the displayed/stored result before full-time renders.
- Continue applies the result once, advances the fixture index and returns to the Hub.

### Transfers

- Search starts blank; it does not dump every player in the world onto the user.
- Current filters: player name, club, nationality, minimum/maximum age and exact primary/secondary position.
- Position is a direct-selection dropdown containing Any Position and every supported position.
- Result rows need availability: Available, Negotiable, Key Player or Not for Sale.
- Bids use a numeric input rather than preset amounts.
- The architecture must leave room for shortlist state, scouted knowledge, player interest, value/wage and additional filters.

### Inbox and saves

- Inbox items have read/unread and expanded/collapsed states.
- Leaving Inbox marks every unread message read and collapses all open messages.
- Save browser needs clear load/delete actions; delete requires confirmation.

## Components Claude can define

- Global shell/navigation and screen title system.
- Table/list row, sortable header, scrollbar and empty-state patterns.
- Dropdown, numeric field, slider, filter chip and search field.
- Player row/card and Condition/fitness indicator.
- Modal/confirmation pattern.
- Tactical-board pin, bench/reserve strip and drag states.
- Match header, event row, stat comparison row and score treatment.
- Half-time/full-time summary family.
- Inbox row and expanded-message treatment.

## Known functional follow-ups outside the mockup pass

- Automated assignment fallback when role holders leave the XI.
- Formation-zone and overload model.
- AI squad selection and recruitment intelligence.
- Remaining transfer filters, contracts, wages and negotiations.
- Visual treatment for the functional fast scrolling, music-volume slider and separate music toggle.

## Acceptance rule

Mockups should specify hierarchy, spacing, component states and responsive behaviour. They should not invent alternate game rules for the stable states above; proposed rule changes should be called out separately for implementation review.
