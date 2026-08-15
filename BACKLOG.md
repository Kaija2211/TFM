# TFM Development Backlog

Last reviewed: 2026-08-15

This is the current post-v0.1 game-development backlog. It replaces scattered memory-only lists as the primary place to record unfinished work. Items should move to `DEVLOG.md` when completed rather than remaining here indefinitely.

Priority labels:

- **P0** — corrupts or misrepresents game state; fix before broader feature work.
- **P1** — frequently encountered gameplay/UX problem.
- **P2** — meaningful feature or systemic improvement.
- **Epic** — requires design/planning before implementation.

## Newly reported after v0.1 testing

### P0 — State, persistence, and misleading UI

- [ ] **In-match substitutions and tactical changes persist after full time.** Mid-match changes mutate the real `AgentTeam` XI/formation. Snapshot the pre-match tactical state and restore it after the fixture, while preserving deliberately permanent pre-match changes.
- [ ] **In-match changes are committed while they are still being edited.** Dragging a player off the pitch currently performs the substitution immediately, preventing the user from simply dragging them back and carrying on. Treat the mid-match Tactics Board as a reversible draft: snapshot the live XI/formation/tactics on entry; allow any number of drag/drop changes and reversals locally; and do not update the substitution log, subbed-off lockout, simulator state, or remaining match until the user leaves the board and resumes play. Cancelling/undoing the draft must restore the exact entry state. Only the final difference should count as official substitutions.
- [x] **Persist the complete tactical setup in saves.** Formation/XI/bench and role assignments round-trip with the squad; Width, Defensive Depth and Tempo now use version-tolerant save-v4 fields. Match mentality remains deliberately match-scoped and resets to Balanced at kickoff.
- [ ] **Full-time banner sometimes shows Matchday 1 / Liverpool v Bournemouth.** Investigate stale fixture/banner state separately from the score/events mismatch below. Reproduce across normal play, skip-to-results, in-match tactics changes, and save/load.
- [x] **Scout 2 accepts a mission brief but never returns youth prospects.** The daily-calendar implementation resolves both slots independently. The permanent Manager Career Systems audit assigns distinct briefs and proves both slots generate discoveries under a deterministic 500-day run.

### P1 — UX and quality-of-life

- [x] **Thomas's wishlist: use a real position dropdown in Transfer Search.** Transfer Search now has a scrollable direct-selection dropdown with `Any position` plus every position.
- [x] **Increase mouse-wheel scrolling speed on every list/dropdown without a draggable scrollbar.** Runtime scroll views now share a central 70px sensitivity, including role assignments and the matchday bench.
- [ ] **Auto-pick must consider Condition.** Candidate score should combine ability, position fit, availability, and Condition so a tired star is not blindly selected over fit cover.
- [x] **Collapse expanded Inbox emails when leaving the Inbox.** Reopening starts from a clean collapsed list.
- [x] **Mark unread Inbox messages as read when pressing Back.** Back marks every message in the Inbox read.
- [ ] **Change 3-4-2-1 wing-back slots from LWB/RWB to LM/RM.** Update formation slot definitions and verify tactics-board labels, auto-pick, position fit, and AI-generated team compatibility.
- [ ] **Audit chronically low generated attributes.** Leadership and physical attributes were specifically reported; sample large generated populations by position/age/team strength before changing distributions.
- [ ] **Finish academy-player inspect note.** Original report was cut off after: “When looking at your academy player, …” Await Thomas’s missing detail.

### Epic — Tactical shape and matchups

- [ ] **Make formation-versus-formation shape matter.** Current formation influence is primarily player-to-slot fit; it does not model tactical interactions such as overloading the wings against a 3-4-3.

First two slices complete: formation pins now produce bounded lane occupancy and route modifiers for crosses, through balls, dribbles, long shots, set pieces and counters. Per-player Attacking/Defensive instructions shift the player's occupancy, while the existing chance-resolution model rewards the relevant real attributes. Matchday Prep reports the clearest route edge and opponent risk. Tactical-sensitivity tests pass, and the 76,000-match holy-balance audit remained stable at 2.697 goals per game. Richer role types and opponent-aware AI adaptation remain outstanding.

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

Do not implement this as a hardcoded rock-paper-scissors formation table without first designing a general zone/shape model.

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

### Epic — Player-derived club strength and world generation

- [ ] **Replace persistent historical club-strength ratings with strength derived from the players on the pitch.** The current architecture begins with trained team attack/defence values, generates players from those values, and continues using team-level values in match simulation. This makes player quality partly a presentation of pre-existing club strength rather than its true cause. Redesign the system so transfers, development, decline, injuries, selection, and tactical fit naturally change a club’s strength immediately.

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

- [ ] **Match result/events mismatch.** One report showed a 0-1 result after two apparent live goals, followed by View Match Events showing 3-0 against the wrong opponent. Static review never reproduced it. May share stale-state causes with the full-time banner bug, but treat them as separate until proven otherwise.
- [ ] **Guard `PickGoalkeeper` against an empty Starting XI.** Current sale rules make this difficult to reach, but the underlying unsafe index fallback still exists.
- [x] **Academy “Bring In Scouted Player” needs live verification.** Claiming is now one atomic scouting-to-academy operation and the permanent Manager Career Systems audit proves the discovery is placed and removed from the scouting list.

### P1 / UX and matchday

- [ ] **Add a real half-time checkpoint.** Stop the replay after minute 45 and show a full-time-style summary containing the score and statistics accumulated only from the first half, with **Make Changes** and **Resume** actions. Do not reveal or process second-half events until Resume. Half-time tactical edits use the same reversible draft flow as other mid-match changes; on commit, preserve every first-half event/stat/result and regenerate only the remaining match from minute 46. Skip to Results must have a deliberate behaviour (recommended: bypass the checkpoint and complete the fixture).
- [ ] **Decide and enforce the substitution rule.** Mid-match substitutions are currently uncapped. Decide between the real five-sub rule, competition-configurable rules, or an intentionally permissive system.
- [ ] **Show secondary positions before transfer scouting.** Current inspect UI can display secondary positions, but the transfer browse/scouting-information policy still needs a deliberate implementation.
- [x] **Music volume slider.** Settings now has a persistent 0–100% slider alongside a separately persisted ON/OFF toggle; muting preserves the selected level.
- [ ] **Player career statistics on Player Detail.** Matches, starts/minutes if tracked, goals, assists, and average rating; requires persistent per-player stat tracking first.
- [ ] **Improve in-match team-name sizing/alignment.** Earlier build feedback requested larger names aligned more closely with the central score.
- [ ] **Surface live Overall change more broadly.** The +1/-1 badge currently appears only on Player Detail and is easy to miss; consider Squad/Tactics rows.
- [ ] **Save rename UI.** Delete-with-confirmation exists; rename is still absent.

### P2 / systems and content

- [ ] **Give every club a genuinely deep senior/reserve squad.** The current transferable `AgentTeam` contains only 20 players (11 starters + 9 bench). The separate 21-player reserve pool is generated only for the managed club, is primarily an injury call-up safety net, and does not provide AI clubs with transfer depth. Replace this split with a consistent all-club squad model. Recommended TFM target: **30 available players per club**—a 25-player senior group, matching the Premier League registration ceiling, plus five reserve/development players (academy prospects remain separate). Include three goalkeepers overall and at least two credible options in every formation-critical position. Distinguish the selected nine-player matchday bench from the wider squad and expose the full pool appropriately in Squad and Transfers. Revisit sale refusal thresholds after expansion so clubs protect true positional shortages but do not reject ordinary bids merely because the old 20-player structure has only 5–7 bench players remaining. Include save migration, retirements/replacements, development, wages/value generation, UI scrolling, auto-pick, injuries, and transfer-market source/removal logic. Run holy-balance and long-career squad-health tests because larger pools can affect selection quality and team strength.
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
