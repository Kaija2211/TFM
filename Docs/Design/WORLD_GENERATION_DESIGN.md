# TFM world generation

## Boundary

Historical results initialise a new football world. They do not remain a hidden
match-strength modifier after players have been generated.

```text
validated history -> club generation profile -> generated players -> live strength
```

Club reputation is separate from squad and match strength. It may later influence
transfer interest, finances, AI ambition and player retention, but never directly
adds goals or win probability.

## Generated profile

Each evidence-backed club receives:

- stable club ID and country;
- reference competition and season;
- 0–100 reputation;
- first-team, bench and reserve overall targets;
- evidence-season count and confidence.

Recent results are normalised against the club's own division before applying a
division/country quality baseline. This prevents raw lower-division points or goals
from being compared directly with Champions League-level clubs. Recent seasons
receive more weight, while reputation also recognises sustained top-flight presence.

Reputation combines three deliberately separate signals:

- recent domestic performance establishes the baseline;
- recent UEFA results add a bounded, diminishing-returns boost, weighted Champions
  League > Europa League > Conference League, with qualifying rounds heavily reduced;
- reviewed historical honours establish a permanent prestige floor.

European evidence never changes generated player quality. This allows a historically
prestigious club to remain attractive after a poor spell without secretly making its
players better, and allows a strong continental run to improve stature without pushing
every elite club to 99. Raw UEFA rows that cannot be resolved unambiguously to the club
registry are excluded and reported rather than guessed.

Identity-only global clubs remain instantiable through a conservative, explicitly
low-confidence fallback. This supports continental opponents without claiming their
domestic competitions are deeply modelled.

## Player ownership of strength

`AgentSquadGenerator` has an explicit quality-target route. It first generates
positionally varied neutral players, then calibrates first-team and bench averages
without removing individual variation. Reserve players use the reserve target.

After creation, transfers, development, decline, injuries, condition, selection and
tactics alter the players and therefore alter club strength. Historical inputs are
not reapplied.

Fresh Manager Mode careers use profile-based generation and record that choice in
save schema v2. Older saves deserialize the missing flag as false and retain the
legacy strength-based generation route. Research Mode remains unchanged.

## Generated player identity

The locally cloned `popular-names-by-country-dataset` is an approved future input for
country-aware generated names. It is CC0 and provides romanised/localised forenames,
gender, country, surname rank and frequency where available. The source currently has
2,480 forename rows across 106 countries and 2,576 surname rows across 75 countries.

It must not be treated as complete on its own. Some covered countries have only ten
male forenames, surname coverage is narrower than forename coverage, and important
football countries or talent regions are absent (including Sweden and several West
African countries). Integration therefore requires:

- an ISO country-code mapping shared with club and nationality data;
- male-name filtering for the current men's competition scope;
- weighted sampling when trustworthy rank/count data exists;
- duplicate-name suppression within each generated club and sensible world-level caps;
- Unicode-safe storage plus a deliberate localised-versus-romanised display policy;
- a coverage audit and explicit fallback pools for every generated nationality;
- bundled source attribution even though CC0 does not require it.

The source repository remains external. When identity generation is implemented, a
deterministic import step should produce a small, validated runtime asset rather than
making Unity parse or depend directly on the cloned repository.

## Holy balance gates

Major changes must check at least:

- squad-quality and player-derived profile spreads;
- goals per match and home advantage;
- champion and bottom-club points;
- best and worst goal difference;
- promoted-club survival;
- concentration of titles and relegations over repeated seasons.

The deterministic target-only audit runs 1,000 seasons per latest top division. It
is a calibration check, not a substitute for the live match-engine audit. The editor
menu `TFM -> Audits -> World Generation Profiles` checks generated squad calibration.
`TFM -> Audits -> Manager Holy Balance` then runs repeated full seasons through the
actual generated players, formation-fit layer and Manager Mode match simulator.
