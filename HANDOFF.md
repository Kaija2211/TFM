# Backlog Sweep #3, Maxed-Attribute Overhaul, and Club-Strength Recalibration — Session Handoff (2026-08-11, session 12)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree at handoff time: same harmless font-atlas-glyph-population diff on `Oswald SDF.asset`/`Oswald Bold SDF.asset` as every prior session - deliberately excluded from the commit, same precedent as before. `ManagerMode.unity` **does** carry a real, meaningful diff this time (the Hub button raw-position cleanup from earlier this session) - included in the commit, not excluded like the usual noise.
- Unity Editor: left in Edit Mode, no in-progress test career saved. This session's many live-verification passes were self-driven Play Mode sessions Claude entered/exited directly, never saved to disk. Thomas's own live session (a genuine in-progress career, not yet saved) was lost partway through when a Play Mode restart was needed to compile a fix - explicitly flagged and confirmed with him before proceeding.
- **Very long session**, in two halves: (1) a clean sweep through 7 small backlog items Thomas asked Claude to work through unattended, each live-verified; (2) a live bug-hunting spiral after Thomas started spot-checking the results, which surfaced a real bug in item 6's own fix, then a much bigger pre-existing generation issue (maxed-out individual attributes), which snowballed into a multi-round rework of `AgentSquadGenerator.cs`'s attribute generation and club-strength calibration - explicitly authorized throughout, verified at every step, including a full 380-match league-wide Research Mode check at the end.

## 2. What happened this session

### A. Backlog items 1-7 (Thomas stepped away, asked Claude to go through the list solo)

Two decisions locked in via AskUserQuestion before starting: (1) build the Tactics-Board auto-pick button regardless of what AI clubs do - it doesn't raise the team's strength ceiling, only automates manual selection; (2) restrict role-assignment dropdowns to Starting XI only rather than adding a Bench-section divider.

1. **Role dropdowns restricted to Starting XI** - `RefreshTacticsScreenUI`'s player list dropped the `Bench` concat, kept `StartingEleven` only. Live-verified: 12 rows (11 + "None"), down from Starting XI + Bench.
2. **List scrolling sensitivity** - all 5 scroll views (Scouting/Transfers/Career/Squad/Match Events) had `scrollSensitivity` bumped 1→25. Turned out `1` was a *direction* fix from an old bug (negative sensitivity scrolled backwards), not a magnitude choice - safe to raise. Live-verified via simulated wheel event: ~25x bigger movement per notch, same correct direction.
3. **Rested-player Condition — confirmed NOT a bug, no change made.** `ApplyPostMatchCondition` is a linear add with a hard clamp, not asymptotic. Simulated the worst case (Condition driven to 0, then fully rested): reaches exactly 100 within 10-13 matchdays.
4. **Squad view sortable by Overall/Age/Transfer Value** - extended the *fixed* grid pair (`AddGridHeaderRow`/`AddPlayerGridRow`) with an optional sort-indicator header rather than porting to the fully-generic grid (would have dropped the injury-cross icon and rating bar). Added AGE and VALUE columns (7 total: POS/PLAYER/AGE/OVR/FIT/VALUE/RATING). Starting XI and Bench sort independently. Live-verified via real header click.
5. **Career Record tab shows the live in-progress season** - new `BuildLiveCareerRecordRow`, sourced from `playableTable` (same live table the Hub's own league position already reads). Shows above completed-season history with real GD (unlike completed rows, which show "-").
6. **Auto-Pick XI button** on the Tactics Board - greedy slot-by-slot assignment by `PlayerAgent.GetPositionFit`, explicit GK-vs-outfield guard, reuses `AgentTeam.ChangeFormation` for its "assign StartingEleven, rest to Bench" behavior. **Had a real bug, caught by Thomas immediately after shipping** (see section B).
7. **Real Settings screen** replacing both Title's and the Hub's disabled placeholders - Music ON/OFF (`ManagerAudio.SetMusicEnabled`/`IsMusicEnabled`, mutes not stops) and Match Speed. **Match Speed had a real default-selection bug, also caught by Thomas** (see section B).

### B. Live bug-hunting round (Thomas started clicking through the results)

- **Career Record tab was Italic instead of Bold** - cosmetic, changed to match the existing champion-row convention (Bold + Accent).
- **Auto-Pick XI did nothing when it should have swapped a player** - real bug. The greedy comparison used `fit > bestFit` (strictly greater); two candidates with identical `PrimaryPosition` for a slot both score a flat 1.00 fit, a genuine tie, and the old code kept whichever was encountered first (a weaker starter) over a clearly better bench player. Fixed by scoring `fit * 1000f + GetOverallRating()` instead of fit alone. Live-verified against the exact real scenario Thomas hit (an 87-rated bench CB losing a tie to an 84-rated starter) - reproduced organically from a fresh squad, not forced.
- **Role dropdown label touching the left edge, plus unwanted "v"/"— None —" clutter** - `BuildLabel` stretches full-size with zero padding by default; gave the label its own 10px-inset RectTransform. Removed the "v" indicator and "— None —" placeholder entirely per Thomas's ask - blank until a role is actually assigned.
- **Match Speed defaulted to the slowest option (x0.5) instead of the current pacing** - `matchReplayDurationSeconds` is `[SerializeField]`, and the *scene's own serialized value* is 45, not the C# declaration's inline default of 60 - never checked, so the option array didn't contain the real running value at all, and the `Mathf.Max(0, IndexOf(...))` fallback silently landed on index 0. Corrected the whole set to honestly anchor on the real 45s value, iterated live with Thomas across several messages: **x0.5 (90s) / x0.75 (60s) / x1 (45s, the actual default) / x1.5 (30s, Thomas's explicit "30 seconds max" cap)**.

### C. Maxed-attribute overhaul (the big one)

Thomas noticed low-rated Liverpool players with individual stats sitting at or near 100 ("even on low rated players... a striker with 100 finishing/composure but 42 passing/43 crossing/38 creativity - embarrassing for a premier league striker"). Investigated and root-caused precisely before touching anything (protected file, explicit authorization required and given):

- `RollAttribute`'s stdDev divisor tightened 4→6 (sharpens the curve project-wide).
- New `RollBoostedAttribute` replaces `RollAttribute(...) * multiplier` at all 52 call sites across all 10 role methods (mechanical regex swap, verified 1:1 via `git diff` first). **First version shifted the whole band down by the ceiling overshoot - caught live when Thomas's new Liverpool squad topped out at 77 Overall, and it turned out the shift could push a strong club's mean below what a weaker club produced for the same stat, nearly erasing club differentiation.** Replaced with a monotonic exponential squash (asymptotically approaches, never guarantees, 100) same session.
- New `RollSecondaryAttribute` - a dampened multiplier for general-quality stats (Passing/Creativity/Dribbling/Composure/Positioning) that were previously flat regardless of club quality, deliberately excluding genuinely position-irrelevant stats (a CB's Finishing, a striker's Tackling - real specialization, not a bug). First pass missed Composure for 5 of the 10 roles - caught when Thomas asked "fixed for every position?", re-audited, closed the gap.
- **Verification false alarm worth remembering**: a test appeared to show a weak club outscoring mid-table - the test script itself had `defenceStrength` backwards (lower = stronger defence, confirmed via `StatsticalModel.cs`), not a real bug. Thomas: "I remember thinking there might be confusion with the reversed defense thing... weeks ago."
- Final verified numbers: 150 fresh Liverpool-strength strikers → 0/3000 attribute rolls ≥100, Passing avg 52.6 (up from ~40), Finishing avg 93.2 (elite, never hit 100); full-position sweep at real Liverpool values landed every position 74-86 Overall.

### D. Club-strength gap recalibration

Thomas: "the difference between the teams are that big" doesn't match a competitive league like the Premier League, citing EA FC Career Mode (Wolves ~75 vs Man City ~84, a ~9pt gap) as reference. Pulled the actual trained per-club strengths for the first time (`statisticalModel.GetTeamStrength`, all 20 clubs) rather than guessing - real range Attack 0.70 (Burnley) to 1.66 (Man City). At the existing uniform 0.75 lerp, Man City vs Burnley measured 82.0 vs 58.9 (23.1pt gap). Split the lerp asymmetric: strong clubs (strength >= 1) keep the full 0.75 factor, weak clubs get a gentler 0.3 - landed Man City 82.0 vs Wolves 64.3 (17.7pt gap), a real improvement. Tried softening further (0.15) to close the gap toward EA's 9pts - hit a hard mathematical limit: a soft lerp toward a sub-1.0 target can only approach neutral (1.0), never exceed it, so pushing further just flattened all weak clubs toward the same ~65-66 baseline without closing the gap (Wolves ≈ Burnley, lost differentiation, gap barely moved). Reverted to 0.3 and told Thomas plainly that reaching EA's exact number would need a bigger rebaseline of the whole system's neutral point, not just multiplier tuning - **Thomas's call: "let's just say it's fine for now."**

### E. Full league-wide Research Mode check (explicitly requested)

Thomas: "make sure the relegation teams aren't winning all of a sudden, and that the table finishes, points, GD, goals per match average, is still realistic." Real 380-match round-robin - all 20 real clubs at their real trained strengths, real fixtures data, real protected `Sim.AgentMatchSimulator`, fresh squads via `GenerateReservePlayer` - via a temp test hook, removed after use. Results: avg goals/match **2.62** (matches the established ~2.5-2.8 reference), table topped by Manchester City/Arsenal/Liverpool (the three real-strongest clubs), bottomed by Burnley (the single real-weakest attack in the league) at 35pts/20th - no relegation-caliber club winning. Points spread 78→35 and GD +34→-18 both realistic; some ordinary single-season variance in mid-table (West Ham 4th despite modest real strength) read as normal upset-territory, not a broken correlation.

## 3. Technique notes worth reusing

- **Proxy-value calibration trick**: to test "what would a different lerp *factor* produce" without recompiling each iteration, compute a proxy *input strength* that produces the same output through the *existing* factor: `proxyS = 1 + (targetMultiplier - 1) / existingFactor`. Only valid for testing uniform-factor changes; doesn't work once the formula's *shape* changes (e.g., the asymmetric split needed real recompiles).
- **`defenceStrength` is inverted** - lower = stronger defence (`goalsAgainstPerMatch / leagueAverage`). Already bit this project once "weeks ago" per Thomas; bit a test script this session too. Worth remembering permanently.
- **Play Mode restart loses unsaved session state** - Manager Mode only saves on explicit "SAVE & EXIT TO TITLE" clicks, no auto-save. Always ask before restarting Play Mode if the user has a live, unsaved session open - confirmed this session when a restart (needed to compile a fix) discarded Thomas's in-progress career, exactly as flagged in advance.
- **Reading `[SerializeField]` runtime values via `SerializedObject`, not the C# declaration** - a field's inline default (`= 60f`) is not necessarily what's actually running; the scene's own serialized value silently overrides it. Caused the Match Speed default bug this session.

## 4. Open backlog

See `project_manager_mode_future_scope_ideas` in memory for full detail.

- **Item 14** - tactical shape/formation matchup effects. Explicitly pushed back by Thomas this session ("tricky and hard to balance") - deprioritized below everything else, pick up deliberately later.
- **Club-strength gap** - narrowed (23.1→17.7pt gap, Man City vs Wolves) but not fully closed to the EA FC reference (~9pt). Accepted as-is for now; closing it further would need raising the whole system's neutral baseline, a bigger change than multiplier tuning.
- Transfer bid/negotiation system + Inbox - large scope, deliberately deferred, needs its own dedicated design session.
- Full Time "player performance" tab - not scoped.
- 3 more easter-egg players - blocked on real name/age/height/position details from Thomas.
