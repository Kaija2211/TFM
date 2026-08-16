# TFM tactical shape model

Last updated: 2026-08-15

## Problem

Formation currently matters through player-to-slot fit and lineup phase strength. Tactical sliders alter the managed club's chance-type mixture, but neither side occupies or contests space. The match engine therefore cannot express ideas such as pairing a full-back with a winger to overload a lone wide midfielder, crowding a number ten out of the centre, or leaving transition space behind an advanced flank.

The model must derive interactions from shape. It must not contain a table saying one named formation beats another.

## First implementation slice

Treat the existing tactical-board pin coordinates as the canonical nominal shape. Every outfield slot contributes occupancy to a small pitch grid:

```text
                 left     centre     right
attacking line     A-L       A-C       A-R
midfield line      M-L       M-C       M-R
defensive line     D-L       D-C       D-R
```

Contributions are soft rather than binary. A wide central midfielder can support both centre and flank; a full-back contributes strongly to defensive-wide coverage and modestly to attacking-wide support. This avoids discontinuities caused by moving a pin across an arbitrary boundary.

The two teams' grids are compared from opposite directions: attacking left contests defending right, attacking centre contests defending centre, and attacking right contests defending left.

### Tactical slider transforms

- `Wide` moves non-central support outward and increases flank occupancy at the cost of central density.
- `Narrow` compresses support toward the centre.
- `High Line` advances defensive occupancy and raises transition space behind it.
- `Deep` protects the defensive line but yields midfield/control space.
- `Fast` increases transition exploitation and exposure; `Slow` increases settled support and reduces transition volatility.

AI clubs initially use Balanced transforms. Opponent-aware AI selection comes later, but AI formation still participates immediately because its nominal pin shape is real input.

### Match effects

The first slice changes the chance-type distribution, not the base expected-goal volume:

- left/right overloads favour crosses and dribble combinations;
- central overloads favour through balls and central dribbles;
- exposed space behind an advanced line favours counters and through balls;
- compact central defence suppresses central routes and redirects attacks wide;
- strong wide coverage suppresses crosses from that side.

Effects are multiplicative and tightly bounded. Chance weights are renormalised by the existing selector, so this slice redistributes attacks toward tactically available routes instead of simply creating free goals. Player attributes still decide whether those routes become shots and goals.

## Later slices

1. Feed bounded shape quality into chance volume only after the chance-mixture version passes holy balance.
2. Add player role transforms and duty-specific movement.
3. Explain likely overloads, central congestion and transition risk on Matchday Prep.
4. Let AI clubs select shape/sliders in response to opponent strengths while retaining club identity.
5. Add in-match feedback and tactical adaptation.

## Acceptance tests

- Mirrored identical shapes with Balanced settings produce neutral matchup multipliers.
- Swapping home/away produces mirrored left/right effects rather than a different rule.
- A wide setting increases wide occupancy and decreases central occupancy.
- High line increases transition exposure; Deep decreases it.
- A two-player flank against a lone wide defender increases wide-route frequency.
- No route multiplier leaves the configured safe bounds.
- Tactical-sensitivity simulations show different chance mixes for materially different shapes using the same players.
- Full holy-balance audit remains within accepted goals, points, GD and title-concentration ranges.

## Non-goals for this slice

- No formation rock-paper-scissors table.
- No guaranteed tactical counter.
- No direct Overall bonuses for choosing a supposedly superior formation.
- No unbounded expected-goal modifier.
- No claim that three coarse horizontal lines are the finished tactical simulation.
