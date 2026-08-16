# TFM Claude Design Mockup Queue

Last updated: 2026-08-15

This converts the development roadmap into design-sized briefs. Use it with `UI_HANDOFF.md`, which defines the visual target and stable gameplay behaviour. Mockups should target desktop 1920×1080 and establish reusable components rather than treating every screen as a separate visual language.

## How to use Claude Design efficiently

Request one batch at a time. Start each batch by supplying the accepted design system and previous approved screens so navigation, spacing, typography and components remain consistent. Ask for the default screen plus the named states—not a single idealized screenshot with no error, empty or selection behaviour.

For every mockup, request:

- full 1920×1080 frame and layout measurements;
- component hierarchy, spacing, typography, colours and interaction states;
- default, hover, selected, disabled, loading, empty and error states where relevant;
- treatment at 16:9 desktop resolutions below 1920×1080;
- explicit reuse of existing components;
- no invented gameplay rules unless clearly marked as a proposal.

## Batch 0 — Visual system and global shell (do first)

1. **Global application shell** — persistent navigation, club identity, manager identity, calendar date, unread Inbox count, settings/save access and page-title/breadcrumb pattern.
2. **Design-system sheet** — typography scale, colour tokens, spacing grid, panels, cards, dividers, buttons, icon rules, focus states and disabled states.
3. **Data-component sheet** — sortable table header, dense row, player row, status badge, rating badge, Condition indicator, form strip, pagination/scrollbar and empty/loading/error states.
4. **Input-component sheet** — text search, numeric input, dropdown, multi-select, range control, slider, filter chip, segmented tabs, checkbox/toggle and confirmation modal.
5. **Responsive rules sheet** — behaviour at 1920×1080, 1600×900, 1366×768 and ultrawide; minimum readable density and overflow rules.

These five artifacts are the foundation. Do not commission dozens of final screens before accepting them.

## Batch 1 — Playable core redesign (highest priority)

These screens already exist functionally and can be implemented immediately after mockup approval.

1. **Title / save entry**
   - New Career, Continue, Load Career, Settings and Exit.
   - Continue with recent-save summary; no-save state.

2. **New Career setup**
   - Manager name, save name, club selection and club summary.
   - Future-safe space for league, database size and advanced setup without showing controls that do not work yet.

3. **Season Hub**
   - Date, next fixture, Continue, Inbox alerts, club record/form, league table and primary navigation.
   - Fixture-day state, unread-message interruption and no-upcoming-fixture state.

4. **Squad list**
   - Full 30-player roster separated into XI, named bench and reserves.
   - Overall, position(s), age, Condition, availability, value and development indicator.
   - Injured, unavailable, low-Condition, selected and empty-filter states.

5. **Tactics board**
   - Formation pitch, starters, nine-player bench, reserves, formation selection, tactical sliders, role assignments and auto-pick.
   - Player-detail click target distinct from position-replacement click target.
   - Primary/secondary choices unlabelled; adjacent cover labelled; low Condition and injury states.
   - Pre-match editing and paused live-match draft variants.

6. **Position replacement overlay**
   - Eligible player list ranked by suitability, ability and Condition.
   - Primary/secondary, adjacent, injured, unavailable and no-eligible-player states.

7. **Player Detail**
   - Portrait, identity, primary/secondary positions, Overall, Potential knowledge state, attributes, Condition, availability, value and season development deltas.
   - Own senior, academy, fully scouted target, partially known target and unscouted target variants.
   - Space reserved for future career statistics and development history.

8. **Transfers — Search**
   - Search-first empty state rather than every world player.
   - Name, club, nationality, age and position filters now; future filter expansion accommodated.
   - Search results with knowledge, availability and scouting states.

9. **Transfers — Scouted / Sell**
   - Scouted-player report list and own-squad sale/listing view.
   - Listed, awaiting interest, offer received, protected-depth refusal and transfer-window states.

10. **Scouting and Academy (current version)**
    - Two mission briefs, discovery batches, poaching deadline and academy slots.
    - Empty academy slot, report batch, elite prospect and no-results/drought states.

11. **Inbox**
    - Read/unread, collapsed/expanded, actionable/non-actionable and grouped-message states.
    - Transfer response, scouting batch, injury, recovery and match reaction examples.

12. **Save browser and Settings**
    - Load, delete-with-confirmation and future rename affordance.
    - Music toggle plus volume slider; layout ready for audio, display, gameplay and accessibility categories.

## Batch 2 — Matchday family (highest priority alongside Batch 1)

Design these as one related family so fixture identity, score treatment and statistics never drift between screens.

1. **Matchday Prep** — opponent formation, tactical route advantage/risk, squad readiness and confirm-team action.
2. **Live Match** — enlarged aligned team names, score/minute, event feed, statistics, player ratings, mentality, substitutions and Make Changes.
3. **Paused live Tactics Board** — visible paused/draft state, remaining substitutions and Resume/commit behaviour.
4. **Half-time Summary** — score, first-half statistics, Make Changes and Start Second Half; Back from changes automatically resumes.
5. **Full-time Summary** — immutable fixture, result, scorers, statistics, player-of-the-match treatment and Match Events link.
6. **Match Events / Timeline** — structured chronological events, filters and clear home/away attribution.
7. **Post-match Performance** — future-facing layout for player ratings, key contributions, errors, tactical routes and comparison with expectation.

Required stress states: 0–0, high score, long club names, five substitutions, red/injury events when added, extra-time-ready layout for future cups, and scroll overflow.

## Batch 3 — Transfers, contracts and recruitment overhaul

Commission after the core visual language is accepted; much of this functionality is still upcoming.

1. Advanced player search with filter drawer/chips and saved/recent searches.
2. Shortlist with priority, notes, scouting progress and comparison selection.
3. Player comparison for two to four candidates, emphasizing tactical/attribute profiles.
4. Recruitment hub separating Search, Scouted, Shortlisted, Listed, Loans, Free Agents and Active Deals.
5. Detailed scouting report with confidence, strengths, weaknesses, role fit and uncertainty.
6. Enquiry/bid negotiation workspace with offer structure, counteroffer, deadline and failure explanation.
7. Contract negotiation with wage, duration, squad role, bonuses/promises and player-interest feedback.
8. Active negotiations and transfer-window/deadline-day dashboard.
9. Loan negotiation and loan-development expectations.
10. Registration/squad-list submission and deadline warnings.

Required states: not interested, club refusal, not for sale, competing bid, insufficient budget, window closed, pre-window agreement, expiring contract and completed/failed deal.

## Batch 4 — Unified scouting department

1. Scouting department dashboard with scout capacity, workload and coverage.
2. Scout/staff profile showing judging ability/potential, tactical recognition, regional knowledge, adaptability and speed.
3. Assignment builder for senior player, youth, opposition, competition and region missions.
4. Tactical recruitment brief builder such as fast direct winger or ball-playing centre-back.
5. Assignment progress and report-confidence states.
6. Regional/world knowledge map or list-based alternative.
7. Recruitment meeting summarizing new reports, recommendations and follow-up actions.
8. Scout hiring/releasing and staff-budget view.

Required states: no free scout, overlapping coverage, unknown player, partial report, conflicting reports, completed report and quiet assignment.

## Batch 5 — Training, development and player history

1. Training overview with team schedule, intensity, rest and tactical familiarity.
2. Individual development plan with position/role training and attribute focus.
3. Development Progress screen with season and career attribute history.
4. Academy overview with pathway, readiness, training focus and promotion/loan decisions.
5. Player career-statistics panel: appearances, starts, minutes, goals, assists, cards, average rating and competition splits.
6. Squad development dashboard highlighting risers, decliners, blocked prospects and expiring development windows.

## Batch 6 — Tactics depth and match analysis

1. Formation browser including 4-1-2-1-2 Diamond, 4-3-2-1 and 4-3-1-2.
2. Named player role/duty picker with suitability and expected positional movement.
3. Team instructions grouped by possession, transition and defending without imitating FM's density blindly.
4. Set-piece assignment and future routine editor.
5. Pre-match tactical analysis showing occupied lanes, overloads and risks.
6. Post-match tactical analysis showing which routes created or conceded chances.
7. Tactical preset/library and next-match-only override states.

## Batch 7 — Intelligent club and manager-facing AI evidence

Most AI logic is backend-only and does not need decorative screens. Mock up only the evidence and controls visible to the manager:

1. Squad planner/depth chart by role and position.
2. Transfer-needs/recruitment-priority explanation for delegated staff.
3. Delegation settings defining which decisions staff may make.
4. Selection explanation: why Auto-Pick or staff recommends a player.
5. Development-path recommendation: first team, bench, loan, academy or sale.

Do not request a fictional “AI brain dashboard” for ordinary players. Debug tooling can remain utilitarian.

## Batch 8 — Club, competitions and football world

1. Expanded club/league selection for playable countries and divisions.
2. Competition overview with rules, qualification, holders and schedule.
3. League table with promotion, playoff, European and relegation zones.
4. Fixture calendar supporting league, cups, Europe, postponements and congestion.
5. Cup bracket/draw and match-round presentation.
6. Promotion/relegation and playoff summary.
7. European draw/group/league-phase screens.
8. Club World Cup overview for the later depth showcase.
9. World/club browser for non-playable but active clubs.

## Batch 9 — Reputation, finances and manager career

1. Club overview with reputation, honours, rivals, stadium and strategic identity.
2. Finance dashboard with budgets, wages, income, expenses and projections.
3. Board expectations, objectives, confidence and club philosophy.
4. Manager profile with reputation, record, trophies and career history.
5. Manager skill tree/development system—grounded trade-offs, not arcade stat boosts.
6. Job centre, vacancy, interview, offer and club-change flow.
7. Sacking/resignation and end-of-tenure review.
8. Trophy room and generated club-history timeline.

## Batch 10 — Narrative and media (post-alpha concept only)

1. News feed and story detail.
2. Match report and weekly roundup.
3. Press conference/interview choice layout.
4. Transfer-rumour and deadline-day presentation.
5. Player partnership, rivalry, milestone and club-legend stories.
6. Narrative settings: Off, Templates Only, Key Stories, Weekly and Extensive, with privacy/online-generation disclosure.

Do not spend near-term design time polishing this batch before the structured event/history systems exist.

## Batch 11 — Accessibility and release states

1. Complete Settings categories: display, audio, controls, gameplay and accessibility.
2. Colour-blind-safe Condition, form, availability and tactical-warning alternatives.
3. Keyboard/controller focus and navigation states.
4. Text scale and reduced-motion options.
5. First-run onboarding and contextual help.
6. Error recovery: missing/corrupt save, migration warning, unsupported resolution and failed optional online service.
7. Public-release content switch behaviour for removing development Easter eggs.

## Recommended first Claude session

Do not ask for every screen above. For the next design session, request:

1. Batch 0 design-system/global-shell package.
2. Season Hub.
3. Squad List and Tactics Board as one connected flow.
4. Player Detail.
5. Transfers Search plus Scouted/Sell tabs.
6. Matchday family: Prep, Live, Half-time and Full-time.

That gives implementation-ready coverage of the surfaces players spend most of their time using. Inbox, Scouting/Academy, Save/Settings and Match Events can follow using the accepted system without forcing Claude to reinvent the shell.
