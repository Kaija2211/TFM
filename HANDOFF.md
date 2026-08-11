# Transfer Bid/Negotiation + Inbox, Youth Missions/Academy Rework, and a Post-Playtest Fix Pass — Session Handoff (2026-08-11, session 13)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree at handoff time: same harmless font-atlas-glyph-population diff on `Oswald SDF.asset`/`Oswald Bold SDF.asset` as every prior session — deliberately excluded from the commit. New files: `ManagerInbox.cs`, `ManagerTransferNegotiation.cs`. Heavily rewritten: `ManagerScouting.cs` (near-total rewrite, see part 2 below). Edited: `ManagerPrototypeController.cs`, `ManagerClubFinance.cs`, `ManagerAcademy.cs`, `ManagerPlayerDevelopment.cs`, `ManagerSquadRoles.cs`, `SquadListView.cs`, `Manager/Save/ManagerSaveData.cs`.
- Also present in `Assets/`: `potentialemails.txt`, a batch of 30 candidate Inbox message templates Thomas dropped in. Assessed and categorized (see part 5) but **not yet implemented** — that's the natural next session.
- Unity Editor: left in Edit Mode after live verification. No in-progress test career saved — every verification pass ran through temporary public test-hook methods added to the controller, exercised via `Unity_RunCommand`, then removed before handoff (same precedent as session 9's fork-verification marker).
- This was a very long, three-part session. Part 1: Thomas asked for the transfer bid/negotiation system + Inbox, gave every design answer up front via two rounds of `AskUserQuestion`, then stepped away and let it build solo. Part 2: after reviewing the result, Thomas asked for a cluster of follow-ups (fog-of-war for youth scouting, cancel-in-progress-scouting, academy empty-slot refill) which snowballed - through his own follow-up questions - into a full mission-based rework of youth scouting itself, an Academy empty-slot system, an erosion bug fix, and an Inbox UI redesign. Part 3: Thomas played a full career season live during a downtime window and came back with a real bug list - Condition/fitness was explicitly called out as the top priority and rebalanced same session, plus the root cause of a separately-reported "paid £100m, player arrived 60-rated" bug was found and fixed (an accepted bid had no signing deadline, so the source player kept aging/declining on their real club indefinitely).

## 2. Part 1: Transfer bid/negotiation system + Inbox

### Design decisions locked in before building (all defaults were the recommended options)
1. **Bid amount**: pick an amount from a shown range — reuses `BuildSliderRow`'s discrete-option picker.
2. **Scouting gate**: Transfer Market AI-squad targets need their own scout assignment first - a **separate** pool from World Scouting/Academy (cap of 2), not shared.
3. **Reluctance formula**: depth-based - harder to buy from a club whose squad has no close replacement at that position.
4. **Escrow**: bid amount deducted from budget immediately on placement, refunded on decline/walk-away.
5. **Rebid**: immediate, no cooldown, after a decline.
6. **Concurrent bids**: capped at 3.
7. **Resolution cadence**: one matchday later.

Mid-build, Thomas clarified the intended flow: *"you scout, next matchday, you get the information and recommended bid... then you place bid, go next matchday again, and then you see if it's been accepted or declined... then you can confirm and sign player."* Scouting reports and bid results both arrive as real Inbox messages; an accepted bid needs a deliberate **Sign Player** (or **Walk Away**) action rather than auto-completing.

### What shipped
- **`ManagerInbox.cs`** (new) - generic message system, phase 3 of the manager influence arc (the last unclaimed item from the original session 7 three-phase plan). `InboxMessage`: Type/Title/Body/MatchdayReceived/IsRead + optional `ActionPlayer` for a live pending action. Messages are baked plain-text snapshots at creation time, not a live view over a `PlayerAgent`.
- **`ManagerTransferNegotiation.cs`** (new) - the bid engine: pending bids keyed by `PlayerAgent`, `TryPlaceBid` escrows immediately, `ComputeDepthReluctance` (best same-position replacement gap on the seller's own squad, 15+ Overall points = near-max reluctance), `RollAcceptance` (flat refusal 5-35% + randomised accept threshold, both scaled by reluctance), `GetRecommendedBid`, a separate transfer-target scout pool, `GetDisplayOverallBand` (fuzzy Overall for unscouted AI players).
- **Transfer Market Buy tab rework** - `OnBuyRowClicked` branches on state (unscouted -> scout; scouted -> bid dialog; pending -> status only; awaiting signature -> Sign). New bid dialog reuses `BuildSliderRow` inside a `ShowConfirmDialog`-style card, 5 amounts spanning 0.8x-1.35x recommended.
- **Save/load** - `InboxMessages` round-trips fully (action-free historical records only). Pending bids do NOT round-trip by reference (same "AI squads regenerate fresh" limitation) - `PendingBidRefundOnLoad` credits the escrow back to budget on load instead.
- **Inbox screen + Hub wiring** - mirrors Trophy Room/Career's panel pattern. Hub's INBOX button is real now (was a disabled placeholder since first added to the mockup), shows an unread-count badge.

**Real bug caught before shipping**: `RollAcceptance` used bare `Random` in a file with both `using System;` and `using UnityEngine;` - genuine CS0104, third confirmed occurrence of this exact class of bug this project (see `feedback_random_namespace_ambiguity` in memory). Fixed by qualifying `UnityEngine.Random`.

**Live-verified** (temp test hook, real Play Mode): decline path (escrow -> refund exact), accept path (re-run to catch the random outcome - player confirmed removed from the AI club's actual `Players` list, not just "found on some team" - caught and fixed a false-alarm in my own test check, not a real game bug), save/load refund (budget restored exactly, pending bid dropped, historical messages survived), scout-pool independence. A separate 400-trial statistical logic test confirmed the reluctance formula's real shape: an only-option club accepted a fair-value bid 0% of the time vs. ~40% for an identical relative bid on a free-agent prospect.

## 3. Part 2: Youth missions + Academy rework

Started as four separate small asks, each answered via `AskUserQuestion`, that compounded into one connected rebuild:

1. **Fog-of-war for World Scouting** - same fuzzy-Overall-band + gated-detail treatment as Transfer Market.
2. **Cancel an in-progress scout assignment** - Thomas: "I accidentally started scouting a player I didn't want and couldn't undo it."
3. **Academy release should leave a real empty slot**, fillable by bringing in a scouted player - which surfaced Thomas noticing World Scouting only ever generated ages 16-19 ("I see no players under 16"), and then a follow-up call that **every** scouted find, any age, has to go through the Academy first - no more direct Transfer Market bidding on a scouted youth prospect at all. Prompted renaming the tab SCOUTING -> YOUTH.
4. **Then, mid-build, a bigger pitch**: *"The youth page has no players off the bat. You have two boxes for each of your scouts where you can input three positions you're looking for, and then send them on missions. Then with each matchday that goes by, you see the scouting list slowly populate as they find players around the world! And SUPER high overalls should be very rare."* This fully replaced the old fixed "10 prospects per region, assign a scout to reveal one" pool with a mission/discovery model - approved outright ("It's perfect. Go for it my dear.").
5. **Then**: discoveries from a given matchday expire ("poached") if left unclaimed for 3 matchdays - real urgency, not just a passive list.
6. **Then**: confirmed the fast-path is intentional - sign a 16+ discovery into an empty Academy slot and it's immediately promotion-eligible, no artificial wait.
7. **Then**: a direct question about academy growth rate surfaced a real bug (see 3E below), not just a tuning question.
8. **Then**: `AcademySlots` bumped 5 -> 11.
9. **Finally**: Inbox redesigned as a collapsed banner (headline only) that expands to reveal the body, ahead of the longer scouting-report text this rework introduces.

### A. `ManagerScouting.cs` - near-total rewrite
Old model: `GetOrCreateYouthPool(region)` pre-generates 10 fixed prospects per region up front; `TryAssignScout` reveals one existing entry's stats. New model: the pool starts genuinely empty. Two mission slots (`ScoutSlots = 2`), each briefed with 1-3 target positions (`SetMissionBrief`/`CancelMission`). Every matchday an active mission has a flat 30% chance (`DiscoveryChancePerActiveMissionPerMatchday`) to generate a brand-new prospect at one of its briefed positions and add it to a single growing `discoveredProspects` list (`ResolveMatchdayTick`). Age range widened to 14-19 (was 16-19). A discovery IS the scouting act - full real stats visible immediately, only Potential stays fuzzy (unchanged `GetDisplayPotential`, same reasoning: a ceiling is always somewhat speculative even once current ability is known). "Very rare high Overalls" needed no new rarity mechanic - the session 12 attribute-overhaul calibration already makes an elite individual roll rare on its own.

**Real stakes**: a discovered prospect is poached (removed, with an Inbox "Prospect Lost" message) if left unclaimed `MatchdaysUntilPoached = 3` matchdays after being found. The only way to keep one is bringing them into an empty Academy slot.

### B. `ManagerAcademy.cs` - empty-slot rework
`AcademySlots` 5 -> 11. `ReleaseProspect(player)` (dropped its generator/strength params) now sets `academyPool[index] = null` instead of auto-backfilling - a real empty slot, not silently refilled. New `HasEmptySlot`/`GetEmptySlotIndices`/`PlaceProspectInSlot` for the intake flow. `GetAcademyPoolForAging` filters nulls (aging/progression/save only care about real prospects); new `GetFullAcademySlots` returns the positional view including nulls, for the UI and save/load.

### C. Youth screen UI rebuild (`ManagerPrototypeController.cs`)
Renamed SCOUTING -> YOUTH (title + Hub button), tab renamed WORLD SCOUTING -> SCOUTING MISSIONS. New mission-brief area (`BuildMissionBox`, two boxes side by side) above the scroll list, only shown on the Missions tab - a 14-position chip grid per slot (reuses the same absolute-positioned chip-toggle technique the Academy focus-stats picker already established), staged selection committed via SEND, CANCEL frees the slot. Discovered-prospects list replaces the old pool grid: no more per-row scout-assign click, EXPIRES column shows matchdays left before poaching. Academy tab renders an explicit "EMPTY SLOT - BRING IN SCOUTED PLAYER" row for a null slot, opening a picker (reused the existing `BuildEmptyDropdownScaffold`/`PopulateDropdownOptions` dropdown scaffold from the Tactics screen's role-assignment pickers) sourced from `ManagerScouting.DiscoveredProspects`.

New `SquadListView.AddPrebuiltRow` - an escape hatch for a row shape the class doesn't build itself (an empty slot has no `PlayerAgent` to key off), while still getting tracked/destroyed by `Clear()` like every other row.

### D. Transfer Market simplification
Scouted youth prospects no longer appear on the Buy list at all, at any age - the entire prospect-inclusion loop in `RefreshTransferMarketBuyList` and the `wasProspect` removal branch in `OnSignPlayerClicked` are gone. `isProspect`/`IsProspect` stripped from `ManagerTransferNegotiation` entirely (`TryPlaceBid`, `GetRecommendedBid`, `PendingBid`) since every bid target is now guaranteed to be a regular AI-squad player.

### E. Real bug found and fixed: Academy/Youth-pool erosion (`ManagerPlayerDevelopment.cs`)
Thomas asked directly: "should [academy] progression be higher than regular senior development while they sit inside the academy untouched?" Checking the actual code surfaced something worse than a rate question: `ApplySeasonProgression`'s neglect-erosion block triggers whenever the passed `playingTimeFactor` is below `NeglectPlayingTimeThreshold = 0.3`, and `AssumedPlayingTimeFactorYouthProspect = 0.1` (used for both the raw discovered-prospect pool AND Academy prospects) was **permanently eroding every academy kid's Potential every single season**, identically to how a genuinely neglected senior player gets punished - even though a 14-year-old structurally can't ever have real senior appearances. New `exemptFromErosion` parameter (default `false`, so AI first team/uncalled reserves/loan returns keep the original intentional behavior) passed `true` for both youth pools. Academy prospects also got their own, higher assumed factor (`AssumedPlayingTimeFactorAcademyProspect = 0.8`, vs. the unclaimed pool's `0.1`) for genuinely faster growth - answering the original question honestly once the bug itself was fixed.

### F. Inbox redesigned as collapsed banner + expand
`InboxMessage` gained a runtime-only `IsExpanded` bool (not persisted - always collapsed on a fresh visit). Banner shows just title + matchday + unread dot; the whole banner is a `Button` toggling expand/collapse and calling `RefreshInboxUI()`. Body + Sign/Walk Away only build when expanded, sitting as later siblings so their own clicks don't bubble down to the banner's toggle.

### Live verification (temp test hook, real Play Mode, all in one pass)
- Mission brief set -> 2 matchday ticks -> real discovery (a genuine age-14 CB, confirming the widened range) with full real stats and a correct Inbox scouting-report message.
- Mission cancel correctly deactivated.
- Academy release left a genuinely empty, non-backfilled slot; empty-slot index reporting correct.
- Placing the discovery into that slot worked and correctly removed it from the discovered list.
- **Erosion fix confirmed directly**: an academy prospect's Potential held exactly flat across 5 simulated seasons of low-playing-time progression with the exemption flag (74.3 -> 74.3, previously would have eroded).
- Transfer scout cancel: assigned before, correctly unassigned after cancel.
- Poach timer: a fresh discovery correctly vanished from the list after `MatchdaysUntilPoached` matchdays passed unclaimed.

Compiled clean throughout (two real bugs caught via `Unity_GetConsoleLogs` mid-build and fixed before verification: a missing `using Manager.Save;` in the new `ManagerScouting.cs`, and an invalid throwaway-`PlayerAgent` construction used to roll a random region, replaced with a direct `AllRegions` pick).

## 4. Part 3: post-playtest fixes (Thomas played a full season during the downtime between parts 1/2)

While Claude was mid-session, Thomas played a full career season on the code as it stood before the Youth/Academy rework even started (Play Mode was already running and didn't pick up the in-flight edit - see the hot-reload note in part 5) and filed a real list of notes. Full triage in `project_session13_playtest_findings` memory; the two most important items:

**Fixed same session - Condition recovery rebalance.** Thomas: "Condition needs to recover way faster, I cannot stop my players from getting injured as is," plus GK stamina/condition decline should be minimal and CB stamina felt punishingly low. Checked `TryRollInjury` first - reasonably tuned already (0 extra risk above Condition 70, caps at +9% at Condition 0). The real problem: `ManagerSquadRoles.ApplyPostMatchCondition`'s old linear recovery (+8 to +14/matchday) was far smaller than the fatigue cost of actually playing (-12 to -25/matchday), so even real rotation barely climbed out of the danger zone. Fixed: a genuinely unused bench matchday now snaps Condition straight to 100 (matches Thomas's literal ask), and goalkeepers get fatigue dampened to 0.35x of the outfield formula. Verified live: a low-stamina CB with zero rotation across 4 matches still drops to 59.4 (the "never rotate" tension is preserved), the same CB given ONE rest matchday hits exactly 100.0, and a GK playing 4 straight full matches stays pinned at 100.0.

**FIXED - the "£100m player arrived 60-rated" bug.** An accepted Transfer Market bid sitting `AwaitingSignature` had no expiry (unlike the Youth poach-timer built earlier the same session) - the source player kept developing/declining on their real club for as long as the deal sat unsigned. Proved live: left an accepted bid unsigned across 4 simulated seasons and the target's Overall moved 6 points on its own before ever being signed - for an aging player that's normal veteran decline (up to ~8pts/season) compounding for real, easily enough to explain a 25+ point crater. Fixed with `ManagerTransferNegotiation.MatchdaysUntilSignatureExpires = 3` (same window as the Youth poach timer): `AwaitingSignature` bids now carry an `AcceptedMatchday`, and a new `ResolveExpiredSignatures` (called from both matchday-tick hooks alongside `ResolveDueBids`) auto-refunds and sends a "Deal Fell Through" Inbox message if the manager doesn't sign in time. Season rollover (`ForceResolveAllPending`) also force-expires any still-open signature rather than letting its deadline silently reset against the new season's matchday 0. Verified live: a forced-accepted bid left unsigned for 4 matchdays (past the 3-matchday window) correctly fell through with an exact refund and the right Inbox message.

**Also found and fixed**: the Inbox unread marker used a unicode bullet ("●") that Oswald SDF has no glyph for (same root cause class as the project's existing "no unicode arrows, plain 'v'/'^' instead" convention) - replaced with plain "NEW" text.

**Also investigated, not a bug**: Thomas observed zero senior-squad OVR movement across 30 matchdays. `ApplyMatchdayProgression` only ticks per-matchday growth for players with real Potential headroom (roughly under-26) or decline for veterans (roughly 29+) - a prime-age player (the bulk of a normal first team) gets no movement at all by design until a small random nudge at season rollover. A squad reading as static all season is expected today given a typical age curve, not broken - but may not be the experience Thomas wants. Flagged, not resolved; a real season-long OVR-movement count (Thomas's own requested test) would settle it with numbers instead of formula-reading.

Everything else from the playtest (Auto-Pick shouldn't appear in mid-match "Make Changes", one-directional Squad-screen drag, injury cross on the Tactics Board's own bench card, a Condition color gradient on Tactics Board pins, per-match tactical overrides, injury/recovery Inbox messages, a real Reserves/unavailable split, language support) is queued as backlog, not yet actioned - see the memory file for the full list.

## 5. `potentialemails.txt` - assessed, not yet built

Thomas dropped 30 candidate Inbox message templates into `Assets/`. Triaged by how ready each is:
- **Tier 1 (14, zero new systems needed)**: welcome/pre-season, all 5 post-match reaction variants (win/big win/draw/loss/heavy loss), form-streak pair, low-stamina warning, mid-season review, both end-of-season variants, the static recruitment-team teaser.
- **Tier 2 (7, needs light new plumbing)**: opponent-strength preview (3 near-duplicate variants, pick one), per-player in-form/struggling (needs per-player recent form surfaced), substitute-impact (needs correlating sub timing with a later goal/assist event).
- **Tier 3 (9, needs a genuinely new/invented concept)**: bench-player-wants-minutes, player-unhappy-after-benching (need appearance/drop tracking), two tactical-hint templates (need per-team attribute aggregates, not just the single strength scalar), two mentality-tip templates (redundant with the existing live-match mentality buttons), rivalry fixture (needs a fabricated rivalry list - same caution as the nationality-bias flag from session 9), two fan-mood templates (no "supporter sentiment" concept exists at all yet).

Recommended next step, not yet actioned: build all 14 Tier 1 messages, and decide whether to gate frequency (a message after literally every match risks flooding the Inbox over a 38-game season) before wiring the post-match reaction set specifically.

## 6. Technique notes worth reusing

- **Unity MCP bridge drops transiently on domain reload** (entering/exiting Play Mode, recompiling) - `Unity_RunCommand`/`Unity_GetConsoleLogs` return `"Unity not detected"` for ~5-8s afterward. Not a real failure, just retry.
- **Play Mode hot-reload is still unreliable** - editing a script while already in Play Mode did not take effect until a full exit + re-enter cycle, confirmed again this session.
- **`Random` ambiguity bites any new Manager-namespace file with both `using System;` and `using UnityEngine;`** - third confirmed occurrence this project. Default to `UnityEngine.Random` explicitly in new files rather than waiting to get bitten again.
- **Test a specific squad, not "any team"** - a verification check that matches ANY team (e.g. `FindTeamContainingPlayer(target) != null`) will false-positive once the player legitimately joins the managed team. Always check membership on the specific team the assertion is actually about.
- **A direct question can surface a bug the question wasn't about** - "should academy growth be faster?" led straight to "it should never have been eroding at all." Worth actually reading the code when asked a seemingly simple tuning question, not just answering from intuition.

## 7. Open backlog

See `project_manager_mode_future_scope_ideas` in memory for full detail.

- **Playtest backlog from part 4** - Auto-Pick in mid-match "Make Changes", one-directional Squad drag, injury cross on the Tactics Board bench card, Condition color gradient on pins, per-match tactical overrides, injury/recovery Inbox messages, real Reserves/unavailable split, language support. See `project_session13_playtest_findings` in memory for the full list.
- A real season-long OVR-movement count (Thomas's own requested test) - would confirm or refute whether "prime-age players are static all season" (part 4) actually needs a design change.
- **`potentialemails.txt` Tier 1 batch** - ready to build next session, see part 5 above.
- **Item 14** - tactical shape/formation matchup effects. Still explicitly deprioritized (Thomas, session 12: "tricky and hard to balance").
- **Club-strength gap** - narrowed but not fully closed to the EA FC reference, accepted as-is (session 12).
- Full Time "player performance" tab - not scoped.
- 3 more easter-egg players - blocked on real name/age/height/position details from Thomas.
- Not built this session, deliberately out of scope: AI clubs never proactively bid on the manager's own players (Sell tab untouched, still instant); Academy's own homegrown prospects (not sourced from a Youth discovery) still show full stats, no fog-of-war applied there.
