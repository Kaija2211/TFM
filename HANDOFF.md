# Playtest Follow-Up Session — Squad Depth, Live Team Strength, a Real State-Leak Bug, and Academic-Honesty Fixes (2026-08-12, session 16)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- This session's code changes are about to be committed via Claude Code and pushed by Thomas via GitHub Desktop, same convention as every prior session.
- **None of this session's work has been live-verified in the Editor or the built `.exe` yet** - Unity wasn't running for most of the session (only connected partway through, for the app icon regeneration). Thomas is building next, right after this handoff.
- Session ran long and touched a lot of ground - started as "triage Thomas's first real playtest of the Windows build" and kept growing as he found more things while testing live.

## 2. Part 1: First Windows-build playtest triage (~20 notes)

Thomas played the session 15 Windows build end-to-end for the first time and left a big pile of notes - bugs, balance complaints, feature asks, and a couple of open questions. Investigated all of them (via a research subagent) before touching any code, then Thomas picked "quick bugs + tuning first" plus a live executive call on squad roles. Full triage in memory (`project_session16_playtest_notes`).

**Shipped from the triage:**
- **Duplicate scorer/assist bug** - `PickCreatorForChance`/`PickShooterForChance` (`AgentMatchSimulator.cs`) had overlapping candidate pools with no distinctness check, so the same player could be picked as both creator and shooter on a thin squad, reading like a self-assist. Fixed: shooter pick now excludes the creator whenever another candidate exists.
- **Sort by Potential bug** - only sorted by the fuzzy range's lower bound, so "70-82" and "70-95" tied. Now uses the upper bound as a fractional tiebreaker.
- **Tactics Board condition color recalibrated** - anchors moved from 50/100 to 40/80/100 so mid-80s condition visibly shows amber now, not solid green.
- **Transfer pricing rebalanced** - veteran discount curve steepened (full discount by 35 = retirement age, not 39), and `ComputeDepthReluctance` widened from exact position-tag matching to position-group matching (a lone "DM" tag no longer reads as irreplaceable when the club has covering CMs/CBs).
- **Squad roles made cosmetic-only** - Thomas's live executive call: "get rid of role assignments and the negative effects... too much of a headache for our scope... WAIT actually keep up, but it will be cosmetic only." Captain/vice/penalty/FK/corner-taker assignment UI is untouched, but the captaincy match-sim bonus and corner-taker weighting are both disabled, and the pre-kickoff "assign a Captain" warning dialog is gone entirely. Also fully resolves the "benched role-holder hurts the team" complaint - roles carry zero mechanical weight either way now.
- **Confirmed, not fixed**: match result/events mismatch Thomas saw (0-1 scoreline after seeing 2 goals live, then a wrong-opponent scoreline on View Match Events) - static code review couldn't reproduce it. Needs a live repro next time it happens.

## 3. Part 2: Bid dialog rewritten to free-text input

Thomas: "can we have the bid thing on a text input actually? well exclusively number input. remove the five or so set options."

- `ShowBidDialog` (`ManagerPrototypeController.cs`) no longer shows five preset-multiplier buttons - a single `TMP_InputField` (`ContentType.DecimalNumber`) prefilled with the scout's recommended amount, so a manager can hit Submit as-is or overwrite it.
- Validated on submit (blank/zero/negative rejected with a status message, dialog stays open).
- **Real bug caught from a screenshot in the same thread**: prefilling `.text` immediately after building the field (before Unity's own placeholder-hide logic runs) left the default amount rendering on top of the placeholder text instead of replacing it. Fixed by explicitly hiding the placeholder GameObject right after setting the default value.

## 4. Part 3: Academy progression made real-time

Thomas: "do our youth players stats only move after the year, and not necessarily real time? My GK hasn't changed at all in my academy at matchday 22."

Confirmed as a real design gap, not a bug report misunderstanding - the academy pool had zero per-matchday hook at all, only the once-a-season `ApplySeasonProgression` lump. Fixed:
- New `ApplyMatchdayAcademyProgression()`, called from `SimulateFixture` at the same guard `ApplyMatchdayConditionAndInjuries` already uses (fires once per matchday, not once per fixture).
- Removed the old season-rollover lump call for the academy pool entirely.
- `ManagerPlayerDevelopment.ApplyMatchdayProgression` gained an optional `focusAttributes` param so academy's focus-stat doubling still applies under the new tick.
- Academy prospects tick with `playedThisMatchday: true` every matchday (standing in for "always training," since they have no real senior-match played/not-played signal).

## 5. Part 4: Squad depth - expanded reserve pool + visible Reserves list

Thomas: "we need more players per team... an actual reserve. After an injury, my team's already looking a bit shallow." Given a scope choice between a quiet backend depth boost and actually surfacing it, picked "Visible Reserves list."

- Could **not** touch the shared `AgentSquadGenerator`/`GenerateSquad` squad-size logic - that's used by Research Mode too and off-limits per the Manager Mode constraints.
- Instead expanded the existing Manager-Mode-only `ReservePoolPositions` array from 11 to 21 entries (added a DM slot that didn't exist at all before, roughly doubled coverage elsewhere) - this system was already sanctioned for exactly this kind of extension (session 7, injuries phase).
- Added a real "Reserves (N)" section to the Squad screen (`RefreshSquadUI`) - the pool now generates eagerly so it's visible from the manager's very first visit, not just after an emergency call-up. Read-only rows (no click handler), same pattern the opponent-pitch browse view already used.

## 6. Part 5: AI clubs can refuse to sell + auto-backfill on sale

Thomas asked what actually happens if you buy up half an AI club's squad. Investigation confirmed: no replacement generation exists for a transfer-out at all (unlike retirement, which does regenerate), and match difficulty doesn't change either way since the xG baseline is trained per-team-name, decoupled from roster. **Also found a real latent crash risk** while answering: `PickGoalkeeper` falls back to `team.StartingEleven[0]` with no empty-list guard - buying an AI club's entire XI over a long career would throw.

Thomas's own follow-up rules, implemented in the same session:
- **Won't sell if it would leave zero players at that exact position** (`WouldLeaveSquadTooThin`, checked in `ManagerTransferNegotiation.ResolveDueBids` before the price/reluctance roll even runs) - this also forecloses the `PickGoalkeeper` crash risk outright, since a position can never be sold down to zero anymore.
- **Won't sell anything once the selling club's bench is down to 5 players**, a blanket depth floor on top of the position check.
- Declined-for-depth bids get refunded like any other decline, with a distinct "won't even discuss selling" Inbox message.
- **Selling a starter now auto-backfills from the bench** (`OnSignPlayerClicked`, new `FindBestFitBenchPlayer` + the existing `AgentTeam.SubstitutePlayer` swap) - the AI club's XI never has a hole in it after a sale.
- **Confirmed, not changed**: retirement already regenerates a same-position replacement unconditionally for every club (`ApplyRetirementsForTeam`) - answers Thomas's "what if their one GK retires" worry, that path was always safe.

## 7. Part 6: Live team strength

Thomas: "team strength to be live... City will just always win most seasons no matter what... their performance should reflect [decline/losing a player]." Given a scope choice (via AskUserQuestion), picked squad-average-Overall-vs-baseline as the driver and once-per-season-rollover as the cadence.

- New `RecalculateLiveTeamStrength`, called for every team (managed AND every AI club) right after that team's retirements are applied each rollover: `ratio = clamp(currentAvgOverall / baselineAvgOverall, 0.6, 1.5)`, `AttackStrength = originalAttackStrength * ratio`, `DefenceStrength = originalDefenceStrength / ratio` (inverted per the DefenceStrength gotcha in memory - divide for a genuine strengthening, not multiply).
- **Confirmed safe for Research Mode**: Manager Mode and Research Mode each instantiate their own separate `StatisticalModel` (verified via grep) - this can never touch the trained historical baseline the dissertation's SM evaluation depends on.
- Two correctness edge cases found and fixed while building this:
  1. The managed team's squad is restored directly from save data on load, bypassing the normal path where the average-Overall baseline gets captured - fixed by re-baselining on load too.
  2. Reading "original" strength lazily from the live `statisticalModel` at whatever moment a team's squad first generates would contaminate a newly-loaded/newly-started career if the same club had already drifted earlier in the same app session - fixed by snapshotting every team's pure trained strength ONCE, immediately after training in `Start()`, into separate immutable `originalAttackStrengthByTeam`/`originalDefenceStrengthByTeam` dictionaries never touched again.

## 8. Part 7: A real new-career state-leak bug, found live by Thomas

Thomas started a second career in the same running session and got the first career's entire Inbox and squad carried over ("everything is the same except i have a new name"). Confirmed reproducible on the second new career within any single running process (Editor or the built `.exe`) - not an Editor-only artifact, since a fresh process launch's first-ever career has nothing stale to inherit.

Root cause: `OnConfirmTeamClicked` (the actual "start new career" action) never reset any session state at all - only the Load Save path (`ApplySaveData`) did. Fixed with a new `ResetSessionStateForNewCareer()` mirroring that same clear block:
- `currentSeason`/`currentFixtureIndex`/`seasonEndRewardsAppliedForCurrentSeason` reset, league table reset+reseeded, every Inbox-tick cooldown/streak flag cleared.
- `squadsByTeamName`/`reservePoolByTeamName`/`squadRolesByTeamName`/`simulatedMatchdays` cleared, plus `.Clear()` on `loanTracker`/`academy`/`transferNegotiation`.
- **Added missing `Clear()` methods** to three classes that never had one at all: `ManagerInbox`, `ManagerScouting` (also resets `regionalQualityBiasByRegion` to null so it re-randomizes fresh per career), `ManagerCareerHistory`. Plus a `Clear()` on `ManagerClubFinance` - its "seed once, keep forever" budget idiom meant a club that already had a budget entry would never reseed.
- **A second, subtler bug caught while fixing this one**: this same session's live-team-strength feature (Part 7 above) mutates each club's strength at rollover - without a fix here, a second career would have generated its AI clubs off whatever drift the first career had already caused. Fixed by restoring every club's strength back to the immutable original snapshot as part of the same reset.

This is the kind of bug that's easy to reintroduce - any NEW piece of Manager Mode session state needs a line added to both `ResetSessionStateForNewCareer` AND `ApplySaveData`'s clear block going forward.

## 9. Part 8: Live match screen layout fixes

Two rounds, both screenshot-driven from Thomas actually playing a match live:

- **Round 1**: the "Subs Made" list (uncapped - see below) was physically overlapping "MAKE CHANGES" and "MATCH STATS" once 2-3+ subs were made. Moved Subs Made + Make Changes from the shared `x=0.55` column (stacked above Match Stats) to a new right-edge-anchored column, per a hand-drawn mockup Thomas sent.
- **Round 2**: that wasn't enough - a 6-sub match screenshot showed the same collision again, just in the new spot. Thomas asked for the real fix: Make Changes moved into the header toolbar next to PAUSE/SKIP TO RESULTS (a position nothing else ever grows into, so it structurally can't recur), and Match Stats top-aligned to start at the same y-offset as Match Log/Subs Made instead of sitting lower down.
- **Real finding along the way**: "do we have max subs in a game?" - no, and it's not an oversight. A comment on `tacticsBoardOpenedMidMatch` documents that a real per-match sub limit was **deliberately removed** at some past point when the mid-match sub flow got reworked. Didn't reintroduce a cap unilaterally since that reverses a past deliberate decision - flagged as an open question instead.

## 10. Part 9: App icon redesign

Thomas: "can we change the icon for our .exe? Can it just be the TFM, transparent background? Or at least make the TFM part bigger?"

- Checked the source `tfm-logo.png` before committing to transparency - the "TFM" text is white (only the "M" accent is green), designed for the dark navy branding. On a truly transparent background it goes nearly invisible on light Windows themes/wallpapers. Flagged this rather than shipping it blind - Thomas picked keeping the `#0b1120` navy background over transparency.
- Iterated the logo size up through the same live thread (85% -> 96% -> a final **98% canvas width**, ~10px left/right margin) until edges were as tight as possible without cropping. Flagged that the top/bottom gap can't close the same way without stretching (warped) or cropping (loses content) - Thomas confirmed he's fine leaving that as-is.
- Also caught and fixed `alphaIsTransparency` being off on the texture importer (would have caused dark fringing at compressed edges).
- Reimported and all 8 `PlayerSettings` icon slots reassigned live via `Unity_RunCommand` each iteration. **Needs a real rebuild to show up on the actual `.exe`/shortcut** - not visible until then.

## 11. Part 10: Premier League roster locked to season 1 - no simulated relegation

Thomas: "The premier league teams stay the same season to season... We dont have team strengths for championship teams, it's dishonest, so no huddersfield town in season two... Whatever teams are in the prem in season one, remain. No relegation for the MSc version." Framed explicitly around academic honesty for the dissertation artefact, not just preference.

Root cause: `AgeAndReloadFixturesForNewSeason` cycled through every real historical season file (`trainingSeasonFiles`) at each rollover, picking a different one in sequence. Since real Premier League rosters genuinely differ year to year, this silently swapped which 20 clubs the whole career was even about, with zero relegation/promotion actually simulated. Fixed by removing the cycling entirely - every season now always re-parses the same `seasonFile` season 1 started with, so `BuildAvailableTeamNames()` never changes for the rest of the career. `trainingSeasonFiles` is untouched for its other purpose (`TrainStatisticalModel` still combines all of them for the actual strength numbers).

**Side effect Thomas didn't ask to fix**: every season's fixture schedule is now structurally identical to season 1's (same opponent order/dates), since there's no more file variety. If that becomes a real complaint, the fix would be shuffling fixture order within the same fixed 20-team round-robin, not pulling in a different real file.

## 12. Technique notes worth reusing

- **A "New Career" flow and a "Load Save" flow both need the exact same session-state reset** - it's tempting to assume a fresh MonoBehaviour/scene load handles this, but Manager Mode's controller persists across Title -> New Career within one running session, so nothing resets unless explicitly coded to. Check both paths whenever new per-career state is added.
- **Never anchor a fixed-position UI element below something with a `ContentSizeFitter`/unbounded growth** - it's a matter of when, not if, it collides. Either cap/scroll the growing element, or put anything that must stay put somewhere structurally safe (a header toolbar, not a spot below a list).
- **`Unity_RunCommand` can reassign live `PlayerSettings`/reimport assets directly** (`AssetDatabase.ImportAsset` + `PlayerSettings.SetIcons`) - useful for iterating on build-level config without a manual rebuild each time, confirmed working this session for the app icon.
- **A feature that mutates shared per-team state at runtime (live team strength) needs its own reset story symmetric with new-career/load-career**, not just a "make it live" implementation - caught this by tracing through what a second career in one session would actually generate squads off.

## 13. Open backlog

See `project_manager_mode_future_scope_ideas` in memory for full detail, and `project_session16_playtest_notes` for this session's complete write-up. Still open after this session:

- **BIG BUG, unresolved**: the match result/events mismatch Thomas saw once (0-1 after 2 live goals, wrong-opponent scoreline on View Match Events) - couldn't reproduce via static code review, needs a live repro.
- **`PickGoalkeeper` empty-`StartingEleven` crash** - now unreachable in practice since sale guards prevent a position going to zero, but the underlying missing guard is still there if some other path ever empties a squad.
- **Real 5-sub cap** - deliberately not reintroduced without being asked; flagged as open.
- **SM/ABM decoupling** - signings don't change the underlying xG baseline (trained per-team-name from real historical data). Not a bug, but a real scope/framing note for the dissertation write-up if "transfers make you stronger" is ever advertised as a feature.
- Untouched feature requests from the original playtest triage: team-name UI sizing in-match, secondary positions visible pre-scout, half-time pause + resume, scout discovery rate, league record shown in Hub table, music volume slider, Player Detail matches/goals/assists/average rating panel.
