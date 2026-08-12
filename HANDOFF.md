# Comprehensive Playtest, Real Bug Fixes, Multi-Save System, and the First Windows Build — Session Handoff (2026-08-12, session 15)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- This session's code changes are already committed and pushed - `d4d88b7 feat: multi-save system, Inbox/Academy UI fixes, and first Windows build prep` is on `origin/unity6-ai-prototype` (Thomas committed via GitHub Desktop using the message drafted mid-session, same convention as every prior session).
- Unity Editor: left in Edit Mode. Every verification pass this session ran through temporary public test-hook methods added to the controller, exercised via `Unity_RunCommand`, then fully removed before handoff - same precedent as every prior session. No leftover `TestHook` references anywhere in the codebase (checked explicitly before handoff).
- **A real Windows build now exists**: `FootballSimulationResearch/Builds/Windows/TFM.exe` (gitignored, not in the repo). Succeeded clean - 0 errors, 485 warnings (all from the third-party Sentis/AI Inference package's shader compilation, unrelated to any project code), ~145MB, 3.5 minute build time.

## 2. Part 1: Comprehensive multi-season playtest (Thomas: "go through a season or two, or three... tracking numbers, growth, youth, just anything relevant")

Ran via temporary test hooks simulating 5 total seasons on a fresh Liverpool career, deliberately bypassing `OnSimulateSeasonClicked`'s `isAutoResolved: true` fast path (which intentionally freezes Condition/injuries during a skip, session 11) and instead replaying the real per-matchday pipeline without the cosmetic coroutine replay - so Condition, injuries, and Inbox messages all fired for real across the whole run.

- **Retirement - CONFIRMED working smoothly** (explicit Thomas ask: "make sure player retirement actually happens and goes smoothly"). Zero retirements in the first 3 seasons simply because the generated squad skewed young (oldest was 34) - not a bug. Extended the run and caught the first real one live: a player crossed `VeteranRetirementAge = 35`, retired cleanly at rollover, squad size stayed exactly 20 before and after, fresh replacement generated at the same position, zero nulls/duplicates.
- **Growth**: all 20 original squad players moved Overall across the run, avg |delta| ~3.8-3.9 by season 3 - reinforces session 14's finding that prime-age players are not static.
- **Youth pipeline**: ~26-29 discoveries/season with 2 active scout missions, a healthy steady stream. Academy claims were low (only 1 total) - a test-methodology limitation (only checked once a season), not a system problem.
- **Transfer market**: scout → report → bid flow confirmed working. One bid attempt was correctly *rejected* by `TryPlaceBid` once the budget couldn't cover it - no money taken, clean refusal, guard rail confirmed holding.
- **Real finding, not a bug**: managed-team league form collapsed hard across the run - 81pts (title form) → 45pts → 29pts (relegation form) despite the squad's Overall genuinely growing throughout. Root cause: the test never rotated the Starting XI once across 5 straight seasons - zero human tactical management. Condition ground down continuously with no rest, dragging match performance (fit-adjusted strength) and spiking injury risk. **This is the Condition/fatigue system working exactly as session 13's rebalance intended** (punish never-rotating), just far more starkly than any real playtest would show since a human reacts to the fatigue warnings. Not a bug - flagged because the severity of the compounding over a long unmanaged stretch is genuinely striking.
- **Data integrity**: zero nulls, zero duplicate player references, squad size exactly preserved through every retirement/transfer/rollover across all 5 seasons. League-wide goals/match held steady at 1.28-1.39 throughout (matches the trained model's 1.45 reference) even while the managed team itself collapsed - confirms the match simulator itself stayed correct; the story is entirely about one team's own fatigue.

Full write-up in memory (`project_session15_multiseason_playtest`).

## 3. Part 2: Youth development convergence age (Thomas: "don't want them to reach potential at the age of 50" → "its not crazy for some to hit it by like 25 right?")

Two follow-up isolated simulations (no career/save state touched), calling `ManagerPlayerDevelopment.ApplySeasonProgression` directly on synthetic prospects.

- **First pass** (worst-case, biggest possible headroom, +25 points - a genuine 90+ OVR generational talent): converges to within 1pt of Potential by **age 30-32** across every playing-time scenario tested (nailed-on starter, academy prospect, typical rotation player, even a rarely-used reserve). Nobody anywhere close to still developing at 50.
- **Second pass** (Thomas's direct follow-up, a real graduated range of headroom sizes): confirmed the age scales with how good the prospect actually is, not a flat number - modest prospect (~75 POT) peaks at **age 25**, decent (~79 POT) at **26**, right at the elite line (~81 POT) at **26**, strong (~85 POT) at **27**, genuine wonderkid (~92 POT) at **30**. The reason elite prospects take longer is `GetAgingCurveOffset` - once a player's current Overall crosses 80, their own peak-development age auto-extends (up to +5 years), mirroring how real elite players keep sharpening into their late 20s. A modest prospect never crosses that threshold and converges earlier, exactly matching Thomas's own instinct.

## 4. Part 3: Inbox readability + a real read-status bug

- **Readability pass** (Thomas: "text that isn't UI or button related isn't bold... make the entire thing bigger, you might have 20/20 vision, good sir, but I don't"). `BuildInboxMessageRow` - title no longer switches to Bold when unread (unread signal now carried entirely by the "NEW" tag + row background tint), banner height 56→80, title 17→24pt, matchday 13→18pt, body 14→20pt, expanded-body height scaled to match.
- **Real bug found and fixed** (Thomas: "as soon as you click the first one, they all turn grey, despite the other ones still technically being unread"). Root cause was bigger than a re-render glitch: the original session-13 design marked **every** message read the instant the Inbox screen opened (`RefreshInboxUI`'s own mark-all-read loop), which only ever looked correct because nobody had reopened the screen mid-visit before - the first expand click re-ran that same screen-wide refresh and repainted every row from state that had already silently flipped to all-read. Fixed by switching to genuine per-message read tracking: expanding a specific message marks *that one* read (collapsing doesn't un-read it); an unopened message now stays green no matter what else gets clicked in the same visit.

## 5. Part 4: Small UI fixes (screenshot-driven)

- **Mission box CANCEL/SEND overlap** (Youth screen, Thomas sent a screenshot) - `SetPointAnchor` uses the bottom-left corner as its anchor point; SEND's x-offset (`16 + 75 = 91`) landed well inside CANCEL's own span (16 to 156, since it's 140px wide). Fixed by starting SEND right after CANCEL's edge with a clean 16px gap (`16 + 140 + 16 = 172`).
- **Academy sortable headers** (Thomas: "like with our other lists, id like to be able to sort our academy players") - same click-to-sort pattern as Scouting/Transfers/Squad, new `academySortColumn`/`academySortDescending` state (separate from Scouting's, different column layout). Empty slots have no player to sort by, so when a sort is active they group at the bottom below every real prospect rather than staying interleaved by original slot index; unsorted view is unchanged plain slot order.

## 6. Part 5: Multi-save system (Thomas: "I think we should do multiple saves and you can choose which one to load... a Continue button... and a Load button")

Replaces the old single fixed `career_save.json` slot entirely.

- **`ManagerSaveService.cs` rewritten** - one file per career now, named by a stable GUID (`career_{SaveId}.json`) rather than the player-facing name, so a save's display name can never break its file link. New `ListAllSaves()`, `GetMostRecentSave()` (ordinal string-compare on `LastSavedUtc`, stored as `DateTime.ToString("o")` specifically because that format sorts correctly as plain text), `Delete(saveId)`. `Save()` assigns a `SaveId` automatically if still blank and always stamps `LastSavedUtc`.
- **`ManagerSaveData` gained** `SaveId`, `SaveName`, `LastSavedUtc`.
- **New Save Name field** on Team Select step 1, right below Manager Name - the first ever *code-built* `TMP_InputField` in this project (new `ManagerUITheme.BuildInputField` helper; every input field before this was Editor-placed, since there was no spare one to reuse for a second field). Optional - defaults to `"{ManagerName}'s Save"` if left blank, so it doesn't add friction to starting a career. A fresh `Guid.NewGuid()` is generated at the same moment (`OnConfirmTeamClicked`, step 1→2), so every save this session (and any future one) for that career lands on the same file.
- **Title screen**: LOAD CAREER split into two buttons. **CONTINUE** loads `GetMostRecentSave()` directly, no picker. **LOAD CAREER** opens a new Save Browser screen. Both are **fully hidden**, not shown-disabled, until at least one save exists (Thomas's explicit follow-up ask) - `RefreshTitleScreenButtons` re-evaluates and re-flows SETTINGS/EXIT upward to close the gap every time Title is shown, since `HasAnySaves()` can flip true mid-session (a brand new career's first Exit to Hub is also its first save).
- **New Save Browser screen** - same code-built-panel/scroll-view pattern as the Inbox, one card per save (name, manager, club, season, last-saved date), click to load that specific career.
- **Live-verified end-to-end** (temp test hook, writes real files to the real `Application.persistentDataPath`, cleans up everything it creates in a `finally` block): two separately-named careers produced two separate files; both showed up correctly in `ListAllSaves()` with correct metadata; `GetMostRecentSave()` correctly picked whichever was saved later; loading a specific save restored its own state, not the other career's; re-saving after a load overwrote the same file rather than minting a third one. A separate pass confirmed CONTINUE/LOAD CAREER's hidden-until-a-save-exists behavior, both in a genuine zero-save state (confirmed no save folder existed at all on this machine before this session) and once a save exists.

## 7. Part 6: First Windows build

Thomas: "once done i think im going to build it... can you add the icon for the application and whatnot... anything else needed before a build. Never done it before."

- **App icon** - `tfm-logo.png` (the in-game wordmark) is 700×220, not square, so composited it onto a proper 1024×1024 square canvas (dark navy `#0b1120` background matching the game's own theme, wordmark centered with padding) via a PowerShell + `System.Drawing` script, imported into `Assets/Icons/tfm-app-icon.png`, and assigned to all 8 required Windows icon sizes (1024 down to 16) via `PlayerSettings.SetIconsForTargetGroup`.
- **Unity's own splash screen** - was still on its default config (`show=true`, `showUnityLogo=true`, no custom logo). This is a completely separate system from the project's own hand-built Eucna splash (`ManagerPrototypeController.Start()` → `ShowSplashScreen()`) - Unity's runs *before* the game even boots, at the engine level. Trimmed to its minimum footprint (`showUnityLogo=false`, `overlayOpacity=0`) - can't be fully removed on Unity Personal license (a build requirement, not a settings toggle), so a brief unavoidable Unity flash will still precede the real Eucna splash in the built game.
- **Build Settings scenes list was completely empty** - a real blocker, found only by checking; would have either failed the build outright or produced a non-functional one. Added `ManagerMode.unity` as the sole scene.
- **Version tag** - "v0.1 · PRE-ALPHA" now shown bottom-left on the title screen, reading `Application.version` live (mirrors `PlayerSettings.bundleVersion`) rather than a hardcoded string, so it can't silently drift from the real build version.
- **Confirmed already correct**: Company/Product name (Eucna/TFM, matching the splash/title branding already in place), build target (Windows x64 Standalone, Mono2x scripting backend), default resolution (1920×1080 fullscreen, matching the UI's own design canvas).
- **Build itself**: ran via `BuildPipeline.BuildPlayer` from the Editor, `BuildTarget.StandaloneWindows64`, output to `Builds/Windows/TFM.exe` (already covered by the existing `.gitignore`'s `[Bb]uilds/` pattern - confirmed before building, nothing needed adding). Succeeded clean.

## 8. Technique notes worth reusing

- **`Unity_RunCommand` scripts can't use `System.Reflection`** - confirmed again this session (a first attempt at driving controller state via reflection failed immediately at script-validation time). The established "temporary public test-hook method, removed before handoff" pattern remains the only path for exercising private controller state from outside Play Mode.
- **`SetPointAnchor`'s anchor point is the object's bottom-left corner when anchor=(0,0)**, not top-left or center - the exact source of the mission-box CANCEL/SEND overlap bug. Worth double-checking button-row math against this convention specifically, not just eyeballing offset numbers.
- **A screen-wide "mark everything read on open" pattern silently breaks the moment a screen can be re-rendered mid-visit** (e.g. an expand/collapse toggle calling the same refresh method) - the fix (per-message read tracking, marked on the specific interaction that constitutes "reading" that item) generalizes to any future read/seen-state UI in this project.
- **Player Settings' Splash Screen section and a project's own code-built splash are two unrelated systems** - the former runs before the engine even loads the first scene and is partially license-gated (Personal tier can't fully disable it); the latter is just normal in-game UI that happens to run first. Worth clarifying this distinction again if it comes up.
- **A build's Build Settings scenes list isn't automatically populated from what's open in the Editor** - it's separate, persistent project state (`EditorBuildSettings.scenes`) that has to be explicitly set at least once. Always worth checking before a first build on any project, not just this one.

## 9. Open backlog

See `project_manager_mode_future_scope_ideas` in memory for full detail. Unchanged from session 14 except where noted above (Academy sorting, Inbox read-status, mission-box overlap all now resolved):

- **Tier 2 potentialemails.txt batch** (7 templates) - needs light new plumbing (per-player recent form surfaced, sub-timing correlated with a later goal/assist).
- **Tier 3 potentialemails.txt batch** (9 templates) - needs genuinely new concepts (appearance/drop tracking, per-team attribute aggregates, a fabricated rivalry list, a "supporter sentiment" concept).
- **Language/localization support** - still explicitly deprioritized, big scope, not attempted.
- Full Time "player performance" tab - not scoped.
- 3 more easter-egg players - blocked on real details from Thomas.
- Item 14 (tactical shape/formation matchup effects) - still explicitly deprioritized (session 12).
- AI clubs never proactively bid on the manager's own players; Academy's own homegrown prospects still show full stats, no fog-of-war - both still deliberately out of scope.
- **New from this session**: the post-match reaction/low-stamina Inbox frequency-gating constants (2-matchday gap, 5-matchday cooldown, picked as reasonable defaults in session 14) still haven't been validated against a real human-played season - worth revisiting once Thomas plays the actual build.
- **New from this session**: no delete/rename affordance for a save in the Save Browser yet (`ManagerSaveService.Delete` exists and is tested, just not wired to any UI button) - a natural small follow-up if the save list gets cluttered with test/throwaway careers.
