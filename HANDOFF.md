# Live Ratings, Morale, Backlog Sweep + Session Review — Session Handoff (2026-08-10, session 10)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree at handoff time: same harmless font-atlas-glyph-population diff on `Oswald SDF.asset`/`Oswald Bold SDF.asset` as every prior session - deliberately excluded from the commit, same precedent as before. Everything else from this session is committed.
- Unity Editor: left in Edit Mode. No in-progress test career on disk beyond whatever pre-existing save file was already there at session start (this session's live-verification careers were mid-session Play Mode state only, never intentionally saved).
- This was a long session covering three distinct phases: a five-item backlog sweep, two new systems built from a live design conversation (live ratings + morale), and a final end-to-end review pass where Thomas filed 12 items after actually using the build himself.

## 2. What happened this session

### A. Backlog sweep (5 items, all live-verified)

- **LOAD CAREER** — turned out already fully wired since session 8 (Phase 5), just never click-tested. Verified live via real button clicks: a full double round-trip (Load → Save & Exit → Load again) with zero data drift.
- **Injury block on Tactics Board** — dragging an injured bench player onto a starter pin now blocks the swap and shows a red warning banner (`OnBenchPlayerDroppedOnPin`). Pin-to-pin swaps between existing starters stay unblocked (nothing new enters the XI there).
- **Always-visible Condition** — Squad Browse's FIT% badge and a new Player Detail "CONDITION" line now always show the number, color-graded, instead of only appearing below 60%.
- **Youth academy focus stats** — pick up to 3 attributes per academy prospect (position-restricted, growable-pool-only list) to double their per-season growth rate. UI is a chip-toggle grid reusing the Player Detail roles-band slot for academy prospects specifically.
- **Scouting pool expiry/refresh** — a prospect who ages past 22 unbought gets replaced with a fresh 16-19-year-old at season rollover (`ManagerScouting.AgeAndExpireProspects`).

### B. Condition fatigue/recovery fix (found via a live technical question, not the backlog)

Thomas asked directly how Condition recovery handles a benched-then-subbed-on player. Investigation found `ApplyPostMatchCondition` used a binary played/not-played flag driven by final Starting XI membership - a late substitute took full-match fatigue, an early substitution-off read as fully rested. Changed to a `minutesPlayed` float derived from `matchSubsLog`, blending recovery/fatigue linearly by real minutes played. Also confirmed (Thomas asked) that Condition and injuries both correctly reset at season rollover. Full detail: [[project_manager_mode_future_scope_ideas]] backlog entry.

### C. Live in-match player ratings (new system, Thomas's own screenshot design)

Asked to discuss "next items" (live ratings/morale/inbox, all previously out of scope), Thomas sketched the UI directly: a strip of 11 player cards under the Match Log, name + live rating each. Built:
- `AgentMatchEvent` (ManagerSim fork only) gained `CreatorName`/`ShooterName`/`DefenderName`/`GoalkeeperName`, populated in `ResolveAttack` from identities that were already being computed there and then discarded.
- New `ManagerMatchRatings.cs` - FM-style 0-10 rating, managed-team-only, ticks live in sync with the event feed reveal during `ReplayMatchCoroutine`.
- Grid genuinely reflects live substitutions (swaps cards, not just numbers).

Live bug caught by Thomas same session: the grid was left visible through Full Time and collided with the goal-timeline layout there. Reverted to live-match-only.

Full detail, including the exact rating-delta table and the goals/match sanity check this required (per `PROJECT_CONTEXT_FOR_AI.md` guardrail #13, since `AgentMatchSimulator.cs` was touched): [[project_live_match_ratings]].

### D. Team/player morale (new system)

Thomas's own design pivot when discussing scope: "maybe doesn't affect performance, but it affects development?" Built as a growth-rate multiplier (0.85x-1.15x) in `ManagerPlayerDevelopment.ApplyMatchdayProgression`, fed by `ManagerSquadRoles`'s new `morale` dict (playing time + match result driven). Resets every season to a happy 70 baseline ("assume the players went off to a beach somewhere... and are in good moods again"), not neutral. Does NOT touch match simulation at all - deliberately kept separate from Condition's `ManagerFormationFit` injection point. Shown on Player Detail below Condition. Live-verified with exact before/after deltas (a first messy read turned out to be a hot-reload timing artifact, not a real bug - a clean Play Mode restart proved the math exactly right).

### E. Also fixed same session

- **"Mentality used" label removed from Full Time stats** - Thomas flagged it as likely-obsolete; investigation showed it wasn't actually stale (a live pill change did keep it updated correctly), just decided not worth the screen space. Removed the label and the now-fully-dead `mentalityUsedForCurrentMatch` field.
- **Goals/match sanity check** - required by touching `AgentMatchSimulator.cs`. Two checks: 200 raw matches (protected `Sim.AgentMatchSimulator` vs the modified `Manager` fork) landed at 2.57 vs 2.78 goals/match, no meaningful divergence; a full 20-team single round-robin (380 matches) through the real fit-adjustment pipeline landed at 2.57 combined goals/match, 52.4% BTTS, 5.8% scoreless draws - all realistic. One important finding for whoever picks up item J below: `selectedMentality` is confirmed to never reset between matches (see the review batch).

### F. End-of-session review batch (12 items, Thomas went through the whole build himself)

Explicitly asked to just record these + handoff + commit + push this round, not action them. Full detail on every item in [[project_manager_mode_future_scope_ideas]] under "Session 10 review batch" - short version:

1. New Career screen: Manager Name input text too small, remove "Enter text..." placeholder.
2. Move Trophy Room into a new "Career" button tracking overall manager performance (record, money spent - scope TBD).
3. Squad screen: FIT% column not aligned like the rest of the row grid.
4. Transfers: buying is too easy to trigger by accident clicking a name - should open Player Detail instead, with the buy action + confirmation living there.
5. Transfers: only ~10 sellable players shown, needs clarity (likely just the existing bench-only sell rule needing a clearer label, not a bug - unconfirmed).
6. **Real bug, root-caused via code read, not yet live-verified**: live "Make Changes" lets you cycle the same player off/on repeatedly, each drop unconditionally calls `RegisterSubstitution` (resets their fatigue clock - an "infinite stamina" exploit) and unconditionally logs to `matchSubsLog` (duplicate "Subs Made" entries), and nothing blocks re-selecting a player already officially subbed off. All three trace to `OnBenchPlayerDroppedOnPin` needing to track *net* subs, not log/reset on every drag.
7. Live ratings barely move over 90 minutes - feels unnatural. Likely needs bigger per-event deltas or an ambient periodic tick, since only genuine chances generate rateable events today.
8. Question (answered live, not a code change): after scouting a prospect, what next? Academy and Scouting are separate pools by design - a scouted external prospect is bought via Transfer Market, never funneled into the 5-slot Academy. Surfaced a real gap: no way to release a bad Academy prospect to free a slot.
9. Mentality carries over between matches instead of resetting to Balanced - confirmed via code read, `selectedMentality` is never reset at kickoff.
10. SIMULATE SEASON produces unrealistic table collapses (won first 3 matches individually with Liverpool, simmed the rest, finished 15th) - Thomas's diagnosis is accumulated Condition/injury/morale/form decay with zero manager mitigation during an auto-skip; suggested neutralizing those factors for auto-resolved fixtures. `OnSimulateSeasonClicked` not yet read to confirm current behavior.
11. Background music + button-click SFX added under `Assets/Resources/Music & Sound/`, not wired to anything yet.
12. Splash screen logo added (studio "Eucna") - needs a new pre-title screen, logo centered on the title screen's existing background with an "eucna" wordmark below.

## 3. Open backlog (see `project_manager_mode_future_scope_ideas` in memory for full detail)

- All 12 items from section F above - none actioned yet, this was a record-only pass.
- Full Time "player performance" tab (ratings + scorers + assists in one Full Time view) - floated right after live ratings shipped.
- Inbox system - still just a disabled placeholder, explicitly backlogged twice now by Thomas's own call.
