# Backlog Sweep #2, Squad-Strength Overhaul, Splash Screen + Confirm Dialogs — Session Handoff (2026-08-11, session 11)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree at handoff time: same harmless font-atlas-glyph-population diff on `Oswald SDF.asset`/`Oswald Bold SDF.asset` as every prior session - deliberately excluded from the commit, same precedent as before. Also excluded: a trivial `ManagerMode.unity` scene diff (a Scrollbar's runtime handle position + a sub-pixel `AnchoredPosition` rounding jitter, both incidental Play Mode session noise, not an intentional change).
- Unity Editor: left in Edit Mode. No in-progress test career intentionally saved - this session's many live-verification careers were self-driven Play Mode sessions Claude entered/exited directly (see section 4), never saved to disk.
- **Very long session** covering: a full self-driven verification pass of session 10's backlog fixes, a real display-bug investigation that grew into a squad-strength/Overall-rating overhaul (a protected-file change, explicitly authorized), the rest of the numbered backlog sweep (items 6-13, 15), the studio splash screen (built, then refined against a real Claude Design mockup, then given a fade sequence + correct music timing), and a large batch of new backlog items from Thomas's own hands-on testing.

## 2. What happened this session

### A. Self-driven Play Mode verification ("claude take the wheel")

Thomas handed over full control for live verification. Claude entered/exited Play Mode directly via `EditorApplication.isPlaying` (no user action needed) and drove the real game via actual `Button.onClick`/method calls, reading back ground truth from the live component tree. Confirmed live: backlog items **1** (New Career Manager Name font/placeholder), **2** (Career screen's 3 tabs render/behave correctly), **3** (Squad FIT% column is a real column now), **5** (Transfers Sell tab clarity byline), **9** (mentality resets to Balanced before kickoff) - all from session 10's carryover work. Also caught and fixed a **struct-vs-null compile error** in Claude's own diagnostic code (not the game) and confirmed the established "Play Mode hot-reload doesn't apply until a full exit+re-enter" limitation applies reliably in this environment - full technique + gotchas logged as its own memory entry for reuse.

### B. Live "Make Changes" substitution exploit + 3 follow-on Tactics Board bugs (backlog item 6)

`OnBenchPlayerDroppedOnPin` treated every drag-drop as independent, letting a manager cycle the same two players for infinite fresh legs + duplicate "Subs Made" log entries + no block on re-subbing someone already off. Fixed with a `playersSubbedOffThisMatch` tracking set. Live-verified via real `OnDrop` calls on Thomas's actual live match. Surfaced 3 more bugs in the warning banner used for this (and the pre-existing injury block): rendered behind the pitch (z-order, `SetAsLastSibling` fix, confirmed live), vertical position genuinely overlapped board elements (took 3 attempts and a live `GetWorldCorners` measurement to land correctly - **final position not yet visually re-confirmed**, see Open Backlog), and the 3s auto-clear timer used scaled time so it froze solid while the board's own pause was active (`WaitForSecondsRealtime` fix, confirmed live).

### C. Real bug → squad-strength/Overall-rating overhaul

Thomas spotted a bench GK ("Jakub Baker") showing 85 on Squad but 80 on Transfer Sell for the *same player*. Root cause: a cosmetic `GetDisplayRating()` stretch (+15% away from 50) was applied inconsistently - Squad/Player Detail routed through it, but Transfer Buy/Sell/Scouting/Academy read the raw `GetOverallRating()` directly. Fixed the routing everywhere first, then Thomas pushed further: *"how can we make players higher rated without lying?"* Root cause of the underlying complaint: `AgentSquadGenerator`'s team-strength multiplier only pulled attribute generation 35% of the way toward a club's real trained strength, capping honest elite-club Overalls in the low-70s. **With explicit authorization** (protected `Sim/AgentSquadGenerator.cs`, shared with Research Mode), strengthened the multiplier 0.35→0.75. Verified empirically (Liverpool starting XI avg 73.7→80.9, 57% now genuinely 80+; weak clubs stayed correctly low) and re-checked Research Mode realism as requested via a 50-run repeated ABM evaluation mirroring `ResearchEvaluationRunner`'s exact methodology (Points MAE landed well inside the old documented spread, goals/match realistic, BTTS%/scoreless-draw% both within ~1pt of the session 10 reference). Given the honest generation fix alone already produced a small, realistic 90+ tier (6 players league-wide, all at the top 2 clubs) vs. 18 with the old stretch still stacked on top, **the display stretch was removed entirely** - `GetDisplayRating` now just rounds/clamps the true value.

### D. Rest of the numbered backlog, in order (items 7, 8, 10, 11, 12)

- **7 - Live ratings barely move**: added `ManagerMatchRatings.ApplyAmbientTick()`, a small periodic drift for every tracked player regardless of event involvement, ticked every 5 match-minutes.
- **8 - Academy release**: `ManagerAcademy.ReleaseProspect` backfills the same slot with a fresh 14-15-year-old (unlike promotion, which deliberately shrinks the pool) - RELEASE button on the prospect's Player Detail. Live-verified end-to-end.
- **10 - SIMULATE SEASON unrealistic collapses**: investigated first - of Condition/injury/morale/form, only Condition and injury actually affect match *results* (morale/form only ever touch development speed). `SimulateFixture`/`ApplyMatchdayConditionAndInjuries` gained an `isAutoResolved` flag that neutralizes just those two during an auto-skip. Live-verified: a full 38-match auto-simmed season kept Condition at exactly 100.0 and 0 injuries throughout, finishing Liverpool 3rd with 89 points instead of the unmanaged death-spiral Thomas hit.
- **11 - Background music + click SFX**: new `ManagerAudio.cs`. Click SFX needed 3 separate wiring passes since the codebase builds buttons 3 different ways (Editor-placed, `ManagerUITheme.BuildButton`, and a handful of raw one-offs) - all confirmed live.
- **12 - Splash screen**: built, then refined twice more same session (see section E below).

### E. Splash screen refinement (mockup match, fade sequence, music timing)

Thomas pointed at the actual "STUDIO SPLASH" frame in his Claude Design project (`Football Manager UI Concepts.dc.html`, pulled read-only via `DesignSync`) and asked for the logo/text scale to match it (ignoring its background). Applied the mockup's exact pixel values directly - this project's own UI is already built at the same 1920x1080 reference canvas, so no scale conversion was needed: logo 260→170px, wordmark 30→52pt Oswald **Bold**, character spacing 6→9, white. Then added the fade in (0.8s) / hold (3s) / fade out (0.8s) sequence Thomas asked for, with Title getting its own 0.6s fade-in once Splash hands off - one shared `FadeCanvasGroup` coroutine helper, `Time.unscaledDeltaTime` so it can't freeze if timeScale is ever 0 there. Mid-build, Thomas caught that music was starting immediately at launch (during the silent splash) instead of waiting for Title - `ManagerAudio.PlayMusic()` split out from setup, called specifically at the Splash→Title handoff. Live-verified: caught the splash mid-fade (alpha 0.75, genuine interpolation, not a snap) with every mockup number exactly matching, music confirmed silent throughout splash and playing immediately after the handoff.

### F. Backlog items 13 + 15 - confirm dialogs

Thomas: *"should be quick fixes right? go ahead."* Built one shared, reusable `ShowConfirmDialog` modal (message + Confirm/Cancel, code-built overlay). Item 13: warns once, only on the very first match of a new career, if no Captain is assigned (chosen as the single highest-signal "never visited Tactics" proxy) - not a hard block, CONTINUE proceeds, GO TO TACTICS navigates there directly. Item 15: SIMULATE SEASON now always confirms first, given item 10's collapse finding made an accidental click expensive. Both live-verified via real clicks; the underlying simulate logic itself was untouched, only what triggers it.

## 3. New backlog items recorded this session (not yet actioned)

All from Thomas's own hands-on testing after the fact - explicitly asked to just record these, not build them yet:
- Role assignment dropdown needs a Starting XI/Bench separator (or restrict to Starting XI only)
- List scrolling feels slow (needs investigation - might be a real setting, might be Thomas's mouse)
- Rested players should hit 100% Condition, not asymptote toward it
- Squad view sortable by Overall/Age/Transfer Value
- Career Record tab should show the in-progress season live, not just completed ones
- Auto-pick best-XI button for the Tactics Board (open question: do AI clubs already auto-pick their best XI? Worth answering before building - otherwise it's a one-sided advantage)
- Settings screen: two proposed contents now - music on/off checkbox, and a match sim speed slider
- 3 more permanent "easter egg" players (Thomas + 2 friends), same pattern as the existing Hidde Rietberg - **blocked on real name/age/height/position details from Thomas**, can't invent them
- The transfer bid/negotiation system + Inbox integration (Thomas's "insane idea" from earlier this session) - fully designed and written up, explicitly deferred as its own dedicated session (the accept/decline formula needs a real design conversation first)

## 4. Technique notes worth reusing

- **Self-driven Play Mode control**: `EditorApplication.isPlaying = true/false` via `Unity_RunCommand` lets Claude enter/exit Play Mode directly. Real limits: editing a script mid-Play-Mode needs a full exit+re-enter to take effect (confirms the existing hot-reload-unreliable finding); a compile error anywhere breaks every method's availability, not just the broken one; no true pixel screenshot is possible (Screen Space Overlay canvas); always drive the same navigation sequence a real player would (skipping prerequisite steps throws real exceptions that look like game bugs).
- **`System.Reflection` is sandboxed/blocked** in this project's `Unity_RunCommand` tool - private-field verification needs a temporary public test-hook method added directly to the class (has natural access), removed after use.
- **`Destroy()` is deferred to end-of-frame** - querying `GameObject.Find`/component state in the same synchronous script as a `Destroy()` call can produce false "still exists" positives. Use a fresh subsequent call, or `Resources.FindObjectsOfTypeAll` (finds inactive objects too, useful when timers race real wall-clock time between separate tool calls).

## 5. Open backlog

See `project_manager_mode_future_scope_ideas` in memory for full detail on everything above and below.

- **Not yet visually re-confirmed**: the Tactics Board warning label's final vertical position (item 6's follow-on fix) - needs one more real look.
- Item **14** - tactical shape / formation matchup effects (still open, resurfaced twice independently by Thomas).
- All of section 3's new items above.
- The transfer bid/negotiation + Inbox system - fully scoped, deliberately deferred.
