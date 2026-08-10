# Manager Mode Backlog Sweep + Live Bug Fixes — Session Handoff (2026-08-10, session 9)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree: same harmless font-atlas-glyph-population diff as every prior session on `Oswald SDF.asset`/`Oswald Bold SDF.asset` — deliberately excluded from the commit, same precedent as before.
- Unity Editor: left in Edit Mode, no in-progress test career on disk (test careers created during this session's live verification were throwaway, never saved to a real file).
- This was the biggest session yet, in two very different modes: a long "work through the whole backlog" pass done largely on autopilot while Thomas was away, followed by a live pairing session where he caught two genuine bugs in real time by watching the numbers closely rather than trusting the UI.

## 2. What happened this session

### A. Player development tuning — elite aging curve

Thomas's pushback on the original aging model: "I feel like players, or at least superstars should at least remain stagnant for a few years... I'd say harry kane has only gotten better since turning 30." Added an elite-player aging extension to `ManagerPlayerDevelopment.cs` — players with a high CURRENT Overall (his own reasoning: "since now potentials can go down, maybe overall?", not Potential) get up to +5 extra years before decline kicks in (`GetAgingCurveOffset`, `EliteAgingExtensionYears`, `GetGrowthEligibleUntilAge`, `GetPeakDevelopmentAge`, `GetVeteranFactor`), replacing the flat age-30 cutoff everywhere it was used. Verified live with a full sanity pass on goals-per-match realism after the change — still matching real-world Premier League rates.

### B. Transfer/Scouting detail view

"In the transfers, as well as youth talent, we need to be able to click on their name to see detailed stats" instead of buying/scouting blind. `SquadListView` gained `onNameClicked` support (a separate click target layered on the row's own click), `OpenPlayerInspect` gained `browseList`/`ownSquad` params so Player Detail can browse an arbitrary list read-only (no roles band for players you don't own). **Live bug caught and fixed same session**: clicking a youth prospect's name did nothing until Back was pressed — `OpenPlayerInspect` never hid `scoutingPanel`/`transferMarketPanel`, so Player Detail opened underneath and stayed invisible. Fixed by adding both to the hide list.

### C. Form column bug + a real misunderstanding cleared up

Live bug report via screenshot: Form strip showing stale/wrong data at Season 2. Real cause: `recentFormByTeamId` was never cleared alongside the league table reset on season rollover or load. Fixed both call sites. Along the way, clarified for Thomas that the "promotion and relegation" he thought he saw (Sunderland appearing with no training data) is just the season-file-cycling mechanic from session 8, not an actual football pyramid — no lower leagues exist.

### D. Autopilot batch (loans, world-scattered scouting rework, nationalities, youth academy)

Thomas: "yeah go on with all of them, apart from the biggeru nscoped items [live ratings/morale/inbox]... anyway, i shall go for a shower, feel free to go on autopilot." Four features shipped and live-verified without further check-ins:

- **Loan system** (`ManagerLoanTracker.cs`, new) — any squad player, automatic destination, free, fixed to season-end. A starter loaned out backfills via the same bench/reserve logic injuries use. **Caught proactively**: a loaned player is removed from `team.Players` entirely, so the save DTO would have silently lost them forever on the next load — fixed with `ManagerSaveData.LoanedOutPlayers`.
- **World-scattered scouting + player nationalities** (`ManagerPlayerNationality.cs`, new; `ManagerScouting.cs` reworked) — prospects were cosmetically tagged to a real PL club that buying them never actually touched, which was a bit dishonest. Reworked to pool by region instead, with a real nationality per prospect. Regional "hotbed" quality bias is randomized fresh per career rather than a fixed real-world hierarchy — **flag to Thomas, not explicitly discussed with him**, a deliberate choice to avoid a permanent claim about which real nations produce better talent.
- **Youth academy** (`ManagerAcademy.cs`, new) — 5 slots, ages 14-15, promotion age 16, manual "click to promote," built as a second tab inside the existing Scouting screen. **Caught proactively**: an empty `AcademyPool` on load would permanently freeze the pool at zero via a stale "already generated" flag — guarded with a count check before restoring.
- Along the way: bell-curved the Potential-roll headroom (was flat `Random.Range`, re-tuned after the shape change alone barely moved the 90+ rate), and fixed a real `DefenceStrength` inversion bug in both the reserve pool and youth pool discount math (dividing, not multiplying, gives a genuinely worse defense — see `feedback_defencestrength_inverted` in memory, hit twice).

### E. Design-to-implementation: injury cross icon

Thomas linked a Claude Design mockup project ("Unity UX design possibilities," claude.ai/design) and asked for its red medical-cross sprite to be added to injured players on both the Tactics Board and the Squad list. Read via the `DesignSync` tool (read-only — no push, just spec extraction), translated into `ManagerUITheme.BuildInjuryCrossIcon` (two crossed rectangles over a red square — flat-rectangles-only, matching the project's existing convention, no image asset). Tactics Board pin gets it bottom-right (per Thomas's own corrected screenshot — his first phrasing said "bottom left," the circle he actually drew was bottom-right); Squad list gets it left of the name, in an always-reserved gutter so rows don't shift. **Live-verified appear AND disappear**: looped a real career until three starters got genuinely injured, confirmed the icon on both screens, then pushed matchdays past their return dates and confirmed it correctly cleared — proven by construction to also mean they're selectable again, since the icon and the actual match-day auto-substitution swap read the exact same `IsInjured` check.

### F. Two real bugs Thomas caught live by watching closely

**Substitute fatigue bug.** "I paused to make subs... i switched them out and the borders remained yellow, meaning the subbed on players have low stamina." Not cosmetic — `AgentMatchSimulator.GetFatigueMultiplier` judged every player purely by the absolute match clock, so a player subbed on at minute 88 was treated as having played 88 minutes themselves, same penalty as the starter they replaced, in both the Tactics Board display AND the actual chance-creation math. Fixed by tracking each substitute's real entry minute (`RegisterSubstitution`/`ClearSubstitutions`) and computing fatigue off minutes actually on the pitch. Verified via direct logic test: a fresh sub 8 minutes after entering reads 1.000 (fully fresh) vs. 0.866 for an unmodified starter at the same match minute.

**Hub matchday label stale-reference bug.** "how is the screen currently on matchday 1, but each team has played 21 matches?" Real bug — confirmed via Matchday Prep's own separate matchday label, which stayed correct throughout, isolating it to one display. First fix attempt (suspecting a coroutine race in the pre-existing `RecreateHubBylineLabelNextFrame` hack) was wrong — removed it, replaced with a synchronous `ForceMeshUpdate()`, restarted Play Mode clean, reproduced identically. A temporary diagnostic log found the real cause: `hubClubNameLabel`/`hubBylineLabel` were cached controller fields, and the *general* TMP blank-label recovery sweep this screen also runs (`RecoverBlankLabelsNextFrame`) had silently destroyed and recreated the byline label at some point without knowing to update the cached field — leaving it pointing at a destroyed object, `== null` forever after, silently skipping every future update. Fixed by not caching these as fields at all — look them up fresh by path every refresh instead, immune to any recovery mechanism destroying the underlying component. Verified live: 15 matchdays back-to-back, byline correctly tracked every single one against the league table.

## 3. Open backlog (see `project_manager_mode_future_scope_ideas` in memory for full detail)

- Youth academy "focus stats" (pick 3 attributes to double-grow per prospect) — not scoped.
- LOAD CAREER wiring — title screen button is still a disabled placeholder; save/load has been built and reviewed for every system (reserves, roles, loans, academy) but never click-tested end-to-end since there's no UI entry point to actually reload yet.
- Mid-season progress UI — the OVR delta badge only updates at season-end even though growth ticks per-matchday now.
- Scouting pool expiry/refresh — pools never expire today; leaning toward age-out-and-replace over fake AI-club poaching, not decided.
- Injured player still selectable in Tactics/Matchday Prep lineup — doesn't block selection, only shows the icon now (this session's fix). A real block/warning is still open.
- Condition/fitness always-visible number — Condition genuinely persists matchday-to-matchday but is invisible until it crosses 60%; no raw number shown anywhere.
- Live in-match player ratings, team/player morale, inbox system — all still just floated, not scoped, explicitly out of tonight's scope by Thomas's own instruction.
