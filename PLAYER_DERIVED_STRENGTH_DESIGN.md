# Player-Derived Club Strength Design

Status: architecture draft for implementation and simulation testing.

## Objective

After a new world has generated its players, historical club identity must have no
direct effect on match probability. A club is strong because of the players it can
field, their suitability and availability, and its tactical choices.

Historical results remain useful for:

- initial club and division squad-generation priors;
- calibration of league scoring environments and home advantage;
- validation of points, goals, GD, table shape and divisional overlap.

They must not remain an invisible club-name modifier during a live save.

## Club reputation is not team strength

Every club should have a persistent `0–100` reputation value. Reputation describes
status and pulling power, not current match ability. It must never directly boost a
scoreline.

Initial reputation can combine:

- current division and recent league finishes;
- long-term presence at higher levels;
- historically sustained success in the available archive;
- promotion/relegation trajectory;
- a bounded legacy component so one poor season does not erase an established club.

Reputation should move slowly during a save. Sustained performance, trophies,
promotion, relegation and continental participation can alter it, but a single hot
season must not instantly turn a small promoted club into a global destination.

Reputation can influence:

- whether a player is interested in negotiations;
- wage expectations and willingness to accept rotation/development roles;
- transfer competition and preferred destinations;
- board expectations, sponsorship and commercial income;
- manager/job attractiveness and AI club ambition;
- the quality and reach of recruitment/scouting networks.

Player interest should compare club reputation with player quality, career stage,
current club, promised role, wages, competition level and personal ambition. Reputation
is therefore an input to a decision, never an absolute “cannot sign” wall.

## Avoiding double-counted quality

The current system applies historical quality twice:

1. team attack/defence ratings establish expected goals and attack share;
2. those ratings generate stronger players, whose attributes then win each event.

The replacement must not calculate player-derived xG from every attribute and then
feed the same attributes into the existing chance resolver unchanged. That would
still count player quality twice.

Instead, each quality should influence one understandable phase:

1. **Territory and control** — passing, composure, positioning, stamina and tactical
   shape determine which side develops attacks and where.
2. **Chance creation** — creativity, passing, through balls, dribbling, crossing and
   off-ball movement compete against marking, tackling and defensive positioning.
3. **Shot quality** — finishing, composure, heading, long shots and positioning
   determine the quality and target accuracy of the attempt.
4. **Shot prevention** — defending, marking, aerial ability and strength affect the
   chance before or during the shot.
5. **Goalkeeping** — goalkeeping, reflexes, positioning and composure affect the final
   save outcome.

Home advantage, score state, mentality and formation shape can modify phases, but
must not duplicate the same player signal in several generic multipliers.

## Diagnostic team profile

The first implementation slice should calculate a read-only profile from the selected
XI:

- `Control`
- `ChanceCreation`
- `GoalThreat`
- `DefensiveResistance`
- `Goalkeeping`
- `Depth` (separate from matchday XI strength)

Every value must be traceable to players and formation slots. Position fit and
Condition reduce the relevant player contribution before aggregation. A diagnostic
profile does not alter scorelines until its distributions have been inspected across
the league.

An overall club or lineup rating may be displayed, but match simulation must consume
the component values rather than that single overall number.

## Aggregation constraints

- Use the selected XI, not the average of the entire squad, for matchday strength.
- Bench/depth affects substitution quality and season resilience, not kickoff ability.
- Weight players by their actual formation slot and phase responsibility.
- Use bounded averages or soft caps so one superstar cannot offset ten weak players.
- Do not improve defence merely because an unrelated striker was signed.
- Goalkeepers must have a distinct contribution and replacement path.
- AI and managed clubs must use the same Condition and availability rules.
- Recalculate immediately when XI, formation, Condition or personnel changes.

## Initial world generation

Historical `club + season + division` records produce a generation prior, not live
strength. A prior should define distributions for:

- starting-XI quality;
- reserve depth;
- attack/midfield/defence/goalkeeper balance;
- age and potential profile;
- within-squad variance.

For a current starting season, direct club weighting should use only a recent rolling
window. Older history teaches transitions, divisional relationships and variance; it
must not directly drag present-day Manchester City toward their 2000/01 level.

## Delivery sequence

1. Generate and inspect diagnostic lineup profiles beside the legacy model.
2. Measure generated squad and XI spreads by table tier and division.
3. Calibrate generation priors from validated OpenFootball club-season records.
4. Refactor match simulation into explicit phases with neutral league scoring and home
   advantage baselines.
5. Remove direct historical club strength from live match calls.
6. Run holy-balance and long-career tests before making the replacement authoritative.

## Acceptance evidence

At minimum, compare legacy and replacement models across repeated full seasons:

- goals per match, BTTS, 0-0 and scoreline distributions;
- points, GF, GA and GD by every table position;
- gaps between 1st, 2nd, 6th, 10th and 17th;
- frequency of extreme `+50`/`+60` GD seasons;
- upset rates by lineup-strength gap;
- generated squad and selected-XI quality spread;
- effects of a major signing, sale, injury and tired lineup;
- promoted-club survival and division overlap;
- five- and ten-season strength drift.

The target is not artificial parity. Elite lineups should remain favourites, but
dominance must emerge from their players and should not make most of the Premier
League routinely cluster around zero or negative GD.
