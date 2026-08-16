# TFM Development Backlog

Last reviewed: 2026-08-15

This is the current post-v0.1 game-development backlog. It replaces scattered memory-only lists as the primary place to record unfinished work. Completed work is summarized in `DEVLOG.md`; checked regression items may remain inside a named playtest cluster temporarily so the report and its resolution stay traceable.

Priority labels:

- **P0** — corrupts or misrepresents game state; fix before broader feature work.
- **P1** — frequently encountered gameplay/UX problem.
- **P2** — meaningful feature or systemic improvement.
- **Epic** — requires design/planning before implementation.

## Current priority snapshot

No known save-corruption or match-result P0 remains after the August 15 Sunderland regression pass. The next ordered work is:

1. AI squad evaluation and coherent rotation across 30-player clubs.
2. Structured match events and position-specific performance ratings, preserving the current holy-balance benchmark.
3. AI recruitment, replacement and long-career squad-health safeguards.
4. Contracts, player interest, shortlists and richer negotiations.
5. Unified senior/youth scouting department.
6. Narrow formations and named player roles, followed by holy-balance regression.

Balance watch: Sunderland opened one manual career 1W–1D–6L. Keep collecting lower-club seasons and generated-world distributions; do not retune from that single save.

## Newly reported after v0.1 testing

### P0 — State, persistence, and misleading UI

- [x] **League-table Form disappears after closing the game.** Save v5 persists each club's last-five W/D/L sequence and restores it defensively; legacy saves without the field continue with an empty Form strip until new matches are played.

- [x] **In-match substitutions and tactical changes persist after full time.** The complete pre-match team sheet, formation, Width, Depth, Tempo and player instructions now restore after the fixture.
- [x] **In-match changes are committed while they are still being edited.** The live Tactics Board is a reversible draft; only its final difference commits when leaving the board, and half-time Back commits then resumes play.
- [x] **Persist the complete tactical setup in saves.** Formation/XI/bench and role assignments round-trip with the squad; Width, Defensive Depth and Tempo use version-tolerant fields retained by save v5. Match mentality remains deliberately match-scoped and resets to Balanced at kickoff.
- [x] **Full-time banner sometimes shows Matchday 1 / Liverpool v Bournemouth.** All live-match presentation now reads the immutable fixture snapshot captured at kickoff. Silent fallback to the calendar's current fixture has been removed, so a missing snapshot fails loudly during development instead of showing a plausible stale Matchday 1 banner.
- [x] **Scout 2 accepts a mission brief but never returns youth prospects.** The daily-calendar implementation resolves both slots independently. The permanent Manager Career Systems audit assigns distinct briefs and proves both slots generate discoveries under a deterministic 500-day run.

### P1 — UX and quality-of-life

- [x] **Thomas's wishlist: use a real position dropdown in Transfer Search.** Transfer Search now has a scrollable direct-selection dropdown with `Any position` plus every position.
- [x] **Increase mouse-wheel scrolling speed on every list/dropdown without a draggable scrollbar.** Runtime scroll views now share a central 70px sensitivity, including role assignments and the matchday bench.
- [x] **Auto-pick must consider Condition.** Selection combines categorical position fit with condition-adjusted ability and excludes injured/ineligible players.
- [x] **Collapse expanded Inbox emails when leaving the Inbox.** Reopening starts from a clean collapsed list.
- [x] **Mark unread Inbox messages as read when pressing Back.** Back marks every message in the Inbox read.
- [x] **Change 3-4-2-1 wing-back slots from LWB/RWB to LM/RM.** Starting and generated bench slots now use LM/RM; the Manager Career Systems audit locks both templates against regression.
- [ ] **Continue the generated-attribute audit beyond Leadership.** Leadership now has a tested sparse natural-leader tail; physical attributes and any other chronically compressed distributions still need population evidence before tuning.
- [ ] **Finish academy-player inspect note.** Original report was cut off after: “When looking at your academy player, …” Await Thomas’s missing detail.

### 2026-08-15 Sunderland playtest

#### P0 — Confirmed behavioural bugs

- [x] **Continue reverts to matchday-sized jumps after the first fixture.** Hub Continue now advances exactly one calendar day; it opens Matchday Prep only when Continue is pressed on the fixture date.
- [x] **Auto-pick can return an injured player to the matchday bench.** Matchday auto-pick is restricted to the named XI/bench and rebuilds the bench from healthy eligible players, preventing injured or already-subbed players from returning.
- [x] **Temporary mid-match player instructions persist after full-time.** Attacking/Balanced/Defensive roles are now included in the pre-match snapshot and restored after full-time; assignment controls are locked during a live match.
- [x] **Transfer bid failure is silent inside the modal.** Bid validation, closed-window and budget failures now appear inside the blocking dialog, and decimal input accepts invariant (`45.1`) as well as local formatting. Pre-window agreements remain part of the wider transfer redesign below.
- [x] **Player Detail Back loses the originating sub-view.** Transfer and Scouting inspection now preserve the originating tab, filters, sort and exact scroll position; returning from Sell no longer defaults to Buy.
- [x] **Youth scout brief output is suspiciously uneven.** Scout slots resolve independently and now have a persisted ten-day maximum active drought. Updating an active brief retains its cadence instead of artificially waking it; the Manager Career Systems audit checks both slots, balance, maximum gaps and brief changes over 700 deterministic days.

#### P1 — Immediate UX and squad-management work

- [x] **Player Detail attributes overflow behind the bottom action band.** Attribute rows now use compact spacing so all technical/mental/physical stats remain above the fixed action band.
- [x] **Use per-view scroll sensitivity.** Compact assignment dropdowns now use a restrained 12px sensitivity while long lists and the Hub table use 70px.
- [x] **Make secondary positions obvious and add position-slot selection.** Clicking a pitch player's badge/card opens Player Detail; clicking the name/position strip opens eligible primary, secondary and adjacent choices ranked by fit and Condition. Primary and secondary options carry no redundant label, adjacent cover is labelled, emergency mismatches are omitted, and live matches are strictly restricted to the originally named squad. Bench cards list primary position first followed directly by secondary positions.
- [x] **Restyle Transfer Search text fields.** Search inputs now have visible field backgrounds/outlines, corrected margins and non-italic entered text/placeholders.
- [x] **Add a Scouted Players recruitment view — first pass.** Transfers now has a dedicated SCOUTED tab listing completed senior reports with club, position, exact reported Overall, bounded potential clue and recommended fee; existing recruitment filters and direct Player Detail/bid actions work there. Ongoing reports and manual shortlists remain for the scouting overhaul.
- [x] **Remove Loan Out from in-match Player Detail.** Loans belong in a later contracts/transfer workflow and are now hidden from paused matchday Player Detail.
- [x] **Prevent reserves entering the active matchday squad during half-time.** Matchday auto-pick and Player Detail controls are restricted to the named pre-match XI/bench once kickoff occurs.
- [ ] **Improve reserve/matchday squad management.** The existing Player Detail `Select as Substitute` route is discoverable only by accident. Design direct bench/reserve swaps and injured-player replacement from Squad/Tactics without repeatedly opening Player Detail.
- [x] **Surface development deltas — first pass.** Player Detail shows live Overall change plus the five largest whole-number attribute gains/losses against a season-start baseline for senior and academy players. Persistent multi-season career progression history remains a later enhancement.

- [x] **Do not persist live-match tactical overrides.** Formation, XI, bench, reserves, player instructions, Width, Defensive Depth and Tempo all restore to their pre-match state after full time.
- [x] **Reduce repetitive scouting bands.** Youth Potential and unscouted senior Overall reports now use deterministic but asymmetric uncertainty rather than fixed 15-point bands snapped to multiples of five. The Career Systems audit requires meaningful range variety.
- [x] **Cap newly discovered academy prospects at 18.** Existing players may naturally remain in the academy at 19 as they age, but scouts no longer introduce a fresh 19-year-old prospect.

#### P2 — Transfer and generation redesign evidence

- [x] **Overhaul outgoing transfers — first functional pass.** Sell now includes the full squad; listing requires confirmation, interest arrives after three days through Inbox, offers require separate acceptance, and listing/offer state survives save/load. Transfers cannot complete outside the window, below an 18-player floor or when selling the only goalkeeper. Named bidding clubs, competing offers and richer AI demand remain part of the intelligent-club phase.
- [ ] **Redesign bid amount entry and pre-window agreements.** Explore a clear digit-stepper/nine-box control as an alternative to fragile free text. Decide whether pre-window agreements are allowed and, if so, queue registration/arrival for the opening date rather than blocking negotiation entirely.
- [x] **Expose bounded senior-potential clues.** Completed senior reports now classify players as Near Peak, Established, Room to Grow, Promising or High Potential without revealing the hidden numeric Potential.
- [ ] **Audit generated transfer values and lower-club squad economics.** Sunderland's bench/reserves reportedly contain many £30m–£50m players, making liquidation unrealistically lucrative. Sample value distributions by division, club tier, XI/bench/reserve status, age and Overall before changing the formula.

  First correction implemented: intrinsic value now uses an elite-weighted exponential ability curve, a youth-only potential premium and progressive post-27 depreciation. The World Generation Profiles audit now reports mean XI/bench/reserve values per club; run and review that CSV before closing this item or tuning individual club tiers.

  First audit result: across the generated English top flight, mean player values are £32.1m for XIs, £21.8m for benches and £14.3m for reserves. Sunderland now average £30.9m/£21.0m/£14.1m; Liverpool £47.3m/£31.8m/£20.7m. The remaining Sunderland concern is primarily their 80.1/77.8/74.9 squad-quality prior and instant-sale mechanics, not the intrinsic valuation curve. Retain this item until lower divisions and the selling overhaul can be audited too.
- [ ] **Monitor squad-quality depth before retuning.** Liverpool's bench/reserves appeared slightly too rich in 80+ players, but no change is authorized from one observation. Include XI-to-bench and XI-to-reserve rating gaps in the world-generation audit.
- [x] **Audit Leadership generation.** Leadership now retains its ordinary-player baseline while adding a sparse natural-leader tail, weighted toward veterans and organizing central positions with a small precocious-youth chance. The permanent 6,000-player Leadership Distribution audit bounds the median, upper percentiles, 70+/80+ prevalence and veteran-versus-youth gap.
- [x] **Clarify positional-fit tiers in UI and mechanics.** Primary and listed secondary positions now carry full fit, adjacent families carry a 0.80 modifier and unrelated assignments 0.60. The pitch-slot selector communicates the tier directly and the Manager Career Systems audit locks the values.
- [ ] **Add genuinely narrow shapes/roles where structurally appropriate.** Formation width must arise from pins plus the Width instruction; assess narrow 4-2-3-1/diamond variants instead of assuming every nominal 4-2-3-1 is wide.

### Epic — Tactical shape and matchups

- [x] **Make formation-versus-formation shape matter — first systemic slice.** Formation pins, tactical sliders and player instructions generate bounded lane occupancy, route advantages, transition exposure, pre-match feedback and opponent-aware AI adjustment. Named roles, narrow formations, richer feedback and in-match AI adaptation remain follow-up work.

Initial overhaul complete: formation pins produce bounded lane occupancy and route modifiers for crosses, through balls, dribbles, long shots, set pieces and counters. Per-player Attacking/Defensive instructions shift the player's occupancy, while the existing chance-resolution model rewards the relevant real attributes. Matchday Prep reports the clearest route edge and opponent risk. AI clubs evaluate the opposing formation and make one deliberate width, depth or tempo adjustment, avoiding a universal extreme preset. Tactical-sensitivity tests pass, and the AI-enabled 76,000-match holy-balance audit passes at 2.699 goals per game. Richer named role types and in-match AI adaptation can follow later.

Planning must cover at least:

- width and central compactness;
- defensive-line and midfield-line occupancy;
- numerical overloads by zone;
- wing-back space and transitions behind advanced wide players;
- formation matchup effects versus tactical-slider effects;
- readable pre-match feedback explaining likely advantages/risks;
- AI formation/tactical selection;
- exploit resistance and bounded modifiers;
- holy-balance regression testing before acceptance.

Do not implement tactical shape as a hardcoded rock-paper-scissors formation table; continue using the general zone/shape model.

### Epic — Structured match simulation and player performance

- [ ] **Evolve the match simulator from isolated chance events into an authoritative football-event model.** Preserve the existing simulator as a benchmark until the replacement reproduces or intentionally improves its scoreline and table balance. This epic must support trustworthy statistics, player ratings, tactical explanations, AI decisions, persistent career records, Moneyball recruitment and save-specific stories from one shared event truth.

Planning must cover at least:

- possession sequences moving through recovery, buildup, progression, chance creation, shooting and save/goal resolution;
- authoritative participants for every action, including creator, assister, shooter, pressured defender, error-maker and goalkeeper;
- tactical route, pitch zone, chance quality and defensive pressure attached to structured events rather than inferred from prose;
- possession, chances, shots, shots on target, expected-goal quality, assists, saves, turnovers, progression, recoveries, tackles, aerials and errors derived from those events;
- position-specific player-rating models for goalkeepers, defenders, midfielders and attackers, normalized for minutes and bounded against rating inflation;
- tactical execution and role suitability without rewarding decorative event volume or double-counting the same contribution;
- a persistent match state so substitutions, fatigue, score state and tactical changes alter the remaining simulation rather than replacing it with an unrelated reroll;
- deterministic/debuggable event traces and readable explanations for why a player or tactical route performed well or badly;
- performance-history persistence feeding Player Detail, form, scouting, recruitment, AI selection and narrative systems;
- computational cost suitable for simulating many background fixtures and long careers;
- migration/coexistence strategy while old and new simulation paths are compared;
- holy-balance acceptance across goals per game, score distribution, home advantage, points, GD, table spread, upset frequency and positional player-rating distributions.

Recommended delivery slices: structured event schema and benchmark capture; possession/chance sequence prototype; derived statistics; position-specific ratings; continuous mid-match state; downstream career/AI integration; then full holy-balance acceptance.

### Epic — Promotion, relegation, and the football pyramid

- [ ] **Add promotion and relegation properly.** v0.1 intentionally locks the original Premier League clubs because no lower-league model was trained. Turning this into a real game requires lower-league club data and systems rather than rotating unrelated historical Premier League files.

Planning must cover at least:

- Championship and potentially deeper-league club strength generation/data;
- league-specific fixture generation and tables;
- promotion/relegation rules and playoff handling;
- season transitions and club movement;
- finances, reputation, budgets, wages, and squad quality by division;
- save migration/versioning;
- promoted-club strength calibration and holy-balance tests across divisions;
- what happens when the managed club is relegated or promoted.

**Discussion required — lower boundary of the playable pyramid:** consider making the Premier League through League Two fully playable while treating the National League as a lightweight hidden feeder. Do not lock this design yet. Resolve how clubs enter and leave the hidden National League from National League North/South or deeper levels; how clubs with no historical match data receive identities, initial generation priors, squads, finances, and reputations; whether dormant lower-pyramid clubs are real, procedural, or a mixture; how much state persists below the simulated boundary; and what happens when a previously data-less club subsequently reaches League Two and must become fully simulated/playable. Also decide whether movement at the League Two/National League boundary mirrors real rules or uses a simplified game-specific count.

Leading reference design from Football Manager research: retain a database of real but dormant lower-pyramid clubs with names, colours/kits, stadium/location coordinates, rivalries, reputation and approximate finances even when their divisions have no active fixtures. Resolve hidden-league outcomes probabilistically from reputation, approximate squad quality and finances; promote selected clubs into the simulated boundary; geographically allocate regional divisions from the resulting club pool where required; and generate/activate a proper squad plus normal AI recruitment only when a dormant club reaches an actively simulated level. TFM need not copy FM exactly, but this offers a proven answer to clubs with no match-history or populated roster eventually entering the playable pyramid.


### Epic — Intelligent AI club management

- [ ] **Make AI-controlled clubs manage themselves coherently across seasons.** Clubs should evaluate their own squad, make purposeful decisions, and adapt to events rather than acting as static player containers. This should be a transparent, deterministic decision system with club-specific priorities—not an external language model dependency.

Planning must cover at least:

- squad selection and rotation using ability, position fit, Condition, form, injuries, suspensions, fixture congestion, and development value;
- tactical identity, opponent-aware formation/tactical adjustments, and sensible in-match substitutions;
- squad-depth analysis by role/position, including versatile players and youth readiness;
- transfer target identification based on actual weaknesses, budget, age profile, potential, value, wages, and club stature;
- rational bid, sale, loan, contract, and replacement decisions, including refusing structurally damaging sales;
- planned succession for aging/declining players and goalkeepers;
- youth promotion, loans, development minutes, and disposal of players who have no pathway;
- differing club personalities such as win-now contenders, developers/sellers, cautious spenders, and relegation battlers;
- safeguards against hoarding, endless churn, duplicate-position recruitment, squad collapse, and runaway rich-club accumulation;
- readable evidence for debugging why an AI club made each major decision;
- multi-season simulation tests for squad health, competitive balance, transfer realism, and holy-balance stability.

Build this in stages: squad evaluation first, then selection/rotation, transfers and contracts, tactical adaptation, and finally richer club identities. The AI should use bounded scoring/utility decisions with controlled variation so it is believable without being perfectly optimal.

### Epic — Transfers overhaul and player search

- [ ] **Replace the universal flat player list with a realistic recruitment market.** Every generated player is currently shown and effectively available to bid on. Expanding clubs to 30 players would already produce roughly 600 Premier League players, before academy prospects or future lower divisions, making the existing screen both implausible and difficult to use.

Planning must cover at least:

- a searchable player database with combined filters for name, primary/secondary position, age range, club, nationality, Overall, potential, value, contract status, transfer status, and scouting status;
- useful sorting, filter chips, clear/reset controls, result counts, pagination or performant scrolling, and saved/recent searches;
- separate views for player search, shortlisted/scouted players, transfer-listed players, loan-listed players, expiring contracts/free agents, and active negotiations;
- incomplete knowledge: basic public facts such as club, age, nationality, height, and positions remain visible, while uncertain ability/potential and detailed attributes require scouting;
- club and player availability states such as not for sale, reluctant, available at the right price, transfer-listed, loan-only, and unsettled;
- persistent `0–100` club reputation, separate from player-derived match strength, feeding player interest, wage/role expectations, transfer competition and club ambition; reputation should be initialized from historical/divisional standing and change slowly through sustained career outcomes;
- realistic willingness based on squad depth, importance, contract length, age, form, club stature, rivalry, finances, replacement options, and the player’s own interest;
- enquiry/scouting before bidding, negotiated offers rather than one-shot decisions, counteroffers, deadlines, competing bids, add-ons/clauses if the finance model grows to support them, and clear failure reasons;
- transfer windows, registration deadlines, free agents, loans, contracts, renewals, release/expiry, and pre-contract handling;
- development-only Easter eggs should enter each new career as four unique free agents, never replace generated club players, and be omitted behind a public-release content switch; preserve their fixed identities, portraits, nationalities, heights, and intentional attribute clamps;
- AI clubs actively buying, selling, loaning, replacing, shortlisting, and competing for targets through the Intelligent AI club management system;
- keeping generated player populations discoverable without dumping every player onto one screen;
- save migration and deterministic multi-season market tests covering prices, squad health, transfer volume, positional demand, player movement, and financial inflation.

Recommended delivery slices: searchable database and filters; scouting/knowledge and shortlist; availability and negotiation; transfer windows/contracts/loans; AI market participation; then long-career balancing.

### Epic — Unified senior and youth scouting department

- [ ] **Replace instant senior knowledge and the fixed two-youth-scout abstraction with a real scouting department.** Senior recruitment currently has no meaningful knowledge-gathering loop, while youth discovery assumes exactly two permanent scouts regardless of club, staff, reputation or budget. Build one coherent assignment system serving senior recruitment and youth discovery without turning scouting into repetitive busywork.

Planning must cover at least:

- a variable number of scouts determined by club resources, reputation, facilities, staff budget and later manager/staff decisions—not a universal fixed two;
- scout identities with relevant strengths such as judging current ability, judging potential, tactical-profile recognition, region/country knowledge, adaptability, speed and youth specialism;
- separate senior-player, youth-prospect, club/opposition and regional assignments using shared staff capacity;
- specific recruitment briefs combining position/role, age, ability/potential floor, physical profile, preferred foot, height, value/wage range, availability and tactical needs;
- profile instructions such as fast direct winger, high-crossing wing-back, aerial target forward, ball-playing centre-back, pressing midfielder or sweeper keeper, derived from real attributes rather than decorative tags;
- manager-created templates tied to the active tactical vision, plus an automatic “find improvements for this tactic/squad weakness” brief;
- gradual report knowledge: public identity/position facts first, then estimated ability/value/personality and finally higher-confidence attributes/potential as scouting continues;
- uncertainty and scout disagreement without repeatedly rerolling the same report on every UI refresh;
- country/competition knowledge, travel time, assignment duration, report cadence and diminishing returns from overlapping scouts;
- discoverability of generated players without exposing the whole world database for free;
- youth batches, academy intake and poaching deadlines integrated with the same calendar and staff capacity;
- shortlists, follow-up scouting, recruitment meetings and Inbox delivery without flooding the player;
- AI clubs using the same underlying briefs and knowledge rules when identifying targets;
- wages/contracts or a simplified staff-budget model, hiring/releasing scouts and delegation options;
- save persistence and deterministic audits covering assignment completion, report quality, scout workload, regional coverage, wonderkid discovery rate and senior-target relevance.

Recommended delivery slices: knowledge model and senior assignments; variable scout department; tactical/profile briefs; youth integration; AI use; staff hiring/budgets and long-career tuning.

### Epic — Save-specific narrative and media engine (post-alpha)

- [ ] **Make each generated football world remember and narrate its own history.** Produce tailored news, Inbox messages, match reports, headlines, interviews and retrospective stories from verified career facts. This is deliberately outside the first England alpha and must never become a dependency for simulation, progression, saving or basic accessibility.

Architecture must remain offline-first:

- ship a polished template/composable-text narrator in the base game with negligible hardware requirements;
- treat hosted LLM generation as an explicit opt-in enhanced-narrative mode, with clear disclosure of transmitted save facts;
- consider an optional separately downloaded small local-model pack later, never bundled into the base install or required by minimum specifications;
- generate asynchronously outside live match playback, batch related outputs, cache completed stories permanently and cap local worker CPU/memory use;
- fall back immediately to the offline narrator on timeout, invalid output, lost connectivity or unsupported hardware;
- pass only structured, approved world facts and require structured responses so prose cannot invent scores, players, transfers, injuries or records;
- support player-controlled frequency such as Off/Templates Only, Key Stories, Weekly and Extensive;
- let rare generated relationships, partnerships, rivalries, academy generations and club legends feed stories once those underlying systems exist;
- include privacy, moderation, age-rating, localisation, save portability, provider-cost and API-key/service-shutdown planning before implementation.

The product promise is not “an LLM inside a football game”; it is that the player's unique generated world remembers what happened and talks about it.

### Epic — Player-derived club strength and world generation

- [x] **Replace persistent historical club-strength ratings with strength derived from the players on the pitch — first live implementation.** Historical/Elo profiles now seed world-generation squad quality only. Live manager matches calculate control, creation, threat, defensive resistance and goalkeeping from the selected players, position fit and Condition, so transfers, development, injuries and selection alter performance immediately. Continue calibrating this implementation against the acceptance criteria below as leagues and AI behaviour expand.

Use a two-stage model to avoid circular generation:

1. **World-generation prior:** assign each club an initial quality profile used only to generate its starting squad. This can be calibrated from historical league performance, finances/reputation, hand-authored tiers, or a blend. It should describe distributions—not a permanent match modifier—including expected starting-XI quality, squad depth, age profile, positional quality, potential, and variance.
2. **Live football strength:** once players exist, calculate attacking, defensive, midfield/control, depth, and tactical-fit strength from the selected players themselves. Historical club identity must no longer directly boost match odds. Selling stars, signing upgrades, fielding tired reserves, or developing youth should therefore affect performance immediately and explainably.

Planning must cover at least:

- defining player-level contributions by attribute and position rather than relying only on Overall;
- lineup-weighted strength, bench/depth influence, Condition, injuries, form, morale if added, position fit, tactical roles, and formation interactions;
- goalkeeper, defending, buildup/control, chance creation, and finishing as separate team components;
- ensuring one superstar cannot unrealistically outweigh ten weak teammates;
- eliminating the current double-counting of club quality, where historical strength both sets expected-goals opportunity volume and generates stronger players who then dominate chance resolution;
- compressing the Premier League player-quality range to a believable elite-to-lower-table spread while preserving meaningful differences—the working reference is roughly a seven-point average-squad gap (for example, about 84 versus 77), not a gulf that makes most of the division noncompetitive;
- initial squad-quality distributions by club and division, including sensible overlap between strong Championship clubs and weak Premier League clubs;
- promoted/relegated club calibration without permanently attaching performance to club names;
- whether historical data estimates club priors directly or trains a mapping from finances/reputation/league level to squad distributions;
- data acquisition, provenance, licensing, season weighting, promoted-club handling, and missing-data fallbacks if expanding across the English football pyramid;
- regeneration/new-save reproducibility and migration of existing saves without silently rewriting their players;
- UI explanations of current club/lineup strength and how a transfer or selection change affects it;
- computationally cheap recalculation whenever a lineup or squad changes;
- holy-balance validation across many generated worlds and long careers, including goals, points, GD, table shape, upset frequency, promoted-club survival, and strength drift.

Competitive-balance acceptance criteria must examine more than the champion’s points. Across repeated 380-match seasons, record the gaps between 1st/2nd/6th/10th/17th, the number of positive- and negative-GD clubs, each position’s GD distribution, title/race/relegation spreads, and how often one or two clubs produce extreme `+50`/`+60` seasons while nearly everyone else clusters around zero or below. Elite seasons may occasionally be dominant, but this must not be the model’s routine shape. Compare generated squad-average and starting-XI ratings by table tier to prove whether an excessive table gap originates in player generation, match conversion, or both.

Recommended research sequence: preserve the current eight-season model as a benchmark; prototype player-to-unit and lineup-to-team formulas against existing Premier League squads; fit initial generation priors to reproduce plausible team tiers; remove direct club-name strength from match simulation; then expand priors and validation data division by division. Do not discard the historical pipeline until the player-derived model reproduces or intentionally improves the holy balance.

**Known data foundation:** the upstream [`openfootball/england`](https://github.com/openfootball/england) repository is CC0/public-domain and contains long-run Football.TXT match results for the Premier League, Championship, League One, League Two, and National League/Football Conference. The project already vendors and parses nine Premier League seasons in this format, so expansion should extend the existing importer rather than invent a second ingestion path. Audit exact season/division completeness before modelling. Use this data to estimate club-season and division-level attacking/defensive priors, promoted/relegated-team overlap, home advantage, scoring distributions, and year-to-year strength movement. It does not provide complete player attributes, finances, or squad profiles; those must be generated or sourced separately and calibrated so their aggregate output reproduces the observed team-level data.

## Previously known open items not included in the new list

### P0 / bugs

- [x] **Match result/events mismatch.** Result, banner, replay and Match Events now share the immutable fixture/result state captured for the active match; the follow-up Sunderland playtest found no recurrence.
- [ ] **Guard `PickGoalkeeper` against an empty Starting XI.** Current sale rules make this difficult to reach, but the underlying unsafe index fallback still exists.
- [x] **Academy “Bring In Scouted Player” needs live verification.** Claiming is now one atomic scouting-to-academy operation and the permanent Manager Career Systems audit proves the discovery is placed and removed from the scouting list.

### P1 / UX and matchday

- [x] **Add a real half-time checkpoint.** The replay stops after minute 45 with score, first-half statistics, Make Changes and Resume actions. Returning from half-time changes now commits the draft and immediately resumes the second half; Skip to Results bypasses the checkpoint.
- [x] **Decide and enforce the substitution rule.** Manager matches enforce five substitutions; the reversible draft validates its final incoming/outgoing difference against the remaining allowance.
- [x] **Show secondary positions before transfer scouting.** Positions are treated as public information and displayed without requiring a completed report.
- [x] **Music volume slider.** Settings now has a persistent 0–100% slider alongside a separately persisted ON/OFF toggle; muting preserves the selected level.
- [ ] **Player career statistics on Player Detail.** Matches, starts/minutes if tracked, goals, assists, and average rating; requires persistent per-player stat tracking first.
- [ ] **Improve in-match team-name sizing/alignment.** Earlier build feedback requested larger names aligned more closely with the central score.
- [ ] **Surface live Overall change more broadly.** The +1/-1 badge currently appears only on Player Detail and is easy to miss; consider Squad/Tactics rows.
- [ ] **Save rename UI.** Delete-with-confirmation exists; rename is still absent.

### P2 / systems and content

- [x] **Give every club a genuinely deep senior/reserve squad — generation slice.** Every generated club now has 30 players: 11 starters, nine named-bench options and ten reserves, including three goalkeepers overall and wider positional cover. Matchday selection distinguishes the named squad from reserves. AI rotation, transfer rebuilding and long-career squad-health behaviour remain under the Intelligent AI Clubs epic.
- [ ] **Immediate squad-quality effect after transfers.** Superseded by the Player-derived club strength epic above: every lineup/squad change should feed live strength without waiting for season rollover.
- [ ] **AI squad rebuilding and transfer activity.** Superseded by the Intelligent AI club management epic above; retain this as the first major implementation slice after all-club squad depth exists.
- [ ] **Generate a varied fixture order each season while keeping the same league membership.** v0.1 reuses the season-one schedule structure because relegation was locked.
- [ ] **Full-time player-performance view.** Earlier scope idea: a dedicated final ratings/performance tab rather than only match summary statistics.
- [ ] **Additional Inbox content and frequency tuning.** Complete remaining email-template batches and validate post-match message frequency across human-played seasons.
- [ ] **Localization readiness.** Deprioritized during the MSc phase; revisit once UI/content architecture stabilizes.

## Completed items that should not be re-added

- [x] Multi-save browser and delete-save confirmation.
- [x] Managed-club injury reserve pool (an interim safety net; superseded by the all-club squad-depth item above).
- [x] Numeric free-entry transfer bids.
- [x] Live academy progression.
- [x] Batched youth discoveries (2–3 prospects per successful scout hit).
- [x] Fixed nationalities and portraits for easter-egg players.
- [x] Potential sorting uses the upper range as a tie-breaker.
- [x] Role assignments retained as cosmetic-only.
- [x] Premier League roster locked for the submitted MSc build.
