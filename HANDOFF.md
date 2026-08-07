# Tactics Board / Manager Mode Polish — Session Handoff (2026-08-07, session 3)

## 1. Branch / project state

- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree: commit pending as this file is written — see section 6 for the message. Everything in this handoff will be part of that commit.
- Unity Editor: stopped (not in Play mode), not compiling. Safe starting state.
- This was a very long, very dense session (previous session's handoff — session 2 — covered the Tactics Board build itself; this one is almost entirely live-evaluation bug-fixing and design-fidelity polish on top of it, plus one genuinely nasty debugging detour, see section 4).

## 2. What happened this session

Roughly three phases: (A) a design-fidelity pass matching the Claude Design mockups pulled live from the `Unity UX design possibilities` project, (B) a round of bugs found via live play-testing, and (C) a second round of bugs found via a *second* live play-testing pass after (B) landed. All of it is Manager Mode only — `AgentMatchSimulator.SimulateMatch` and `StatisticalModel` were never touched, Research Mode numbers are unaffected.

### Design fidelity (pulled from Claude Design, project `Unity UX design possibilities`)
- **Tactics Board pitch**: was stretched full-width, smearing every formation into an unreadable wide strip. Now constrained to a fixed 1130:700 aspect ratio (the exact ratio used in the design's own "TACTICS BOARD — DETAIL" board), centered with letterboxed margins either side, matching what design asked for directly.
- Raised `BuildTacticsBoardPin`'s `verticalCompression` (0.66 → 0.85) — GK/CB pins were visually overlapping in back-three formations (3-5-2, 3-4-2-1). Verified clean across every formation.
- Bench caption ("BENCH · DRAG A PLAYER...") was almost touching the card row below it — repositioned with breathing room.
- Added a bench horizontal scrollbar and a Match Events vertical scrollbar — both lists were *already* functionally scrollable (mouse wheel/drag), just undiscoverable with zero visual affordance, which read as "broken" rather than "scroll for more."
- Full-time summary header: team names/score moved up, goal-scorer names moved down — they were both crowding the header/body divider line.
- Full-time stats block was pinned near the top of its available space, leaving a large dead gap below before the footer. Now vertically centered in that space, matching the mockup's own centered layout. "Tactic used:" line centered too (was left-aligned).
- League table: GF/GA columns replaced with a single GD (goal difference) column — the old two-column layout was getting clipped by the table's own scrollbar. This is a **Manager Mode display-only** change; `LeagueTable.Entry.GoalsFor/GoalsAgainst` (used by Research Mode's evaluation output) are untouched, GD is just computed locally in `LeagueTableView.cs`.
- Player Inspect: attribute bars now show their numeric value (right-aligned, color-matched to the bar), not just a bare bar. This **reverses an earlier documented decision** ("kept off attribute rows/ratings per direction") — worth knowing if a past session's reasoning ever gets referenced, it's now stale.
- Match screen buttons (Attacking/Balanced/Defensive) were hand-placed Editor buttons from before the code-driven reskin — never routed through the styling helpers, so they rendered top-left-aligned and non-bold, visibly different from every other button (Pause vs. Skip to Results had the same mismatch). Fixed by extending `ManagerUITheme.NormalizeButtonLabel`/`StyleHubActionButton` to also force alignment + font weight, not just font/size/color as before.
- Imported the designer's three PNGs (`football-icon.png`, `star-filled.png`, `star-empty.png`) as TMP Sprite Assets in `Assets/Resources/Manager/`, wired the football icon into goal-scorer lines and the two star icons into Player Inspect's weak-foot rating (`star-empty` is set as `star-filled`'s fallback sprite asset, so one `spriteAsset` assignment resolves both glyphs). **Gotcha**: `<sprite>` has no `size=` attribute in TMP — silently prints the tag text literally instead of erroring. Use `<size=X%>...</size>` wrapping the sprite tag instead.

### Bugs found in the first live-testing pass
- **Match screen corrupted on matchday 2+** (fine on matchday 1). Root cause: the full-time-only repositioning code for the stats panel permanently mutates shared RectTransforms in place rather than rebuilding them, and nothing ever reset them back to the live-match layout, nor re-hid the full-time-only scorer lists/View Match Events button, nor re-showed the Match Log, before the next live match started. Fixed with an explicit `ResetMatchStatsPanelToLiveLayout()` called at the top of `OnSimulateMatchClicked`.
- **Pause + substitution**: pressing the sub button while paused visibly did nothing — the picker only popped open the instant you hit Resume. `Time.timeScale=0` freezes `WaitForSeconds`, and the coroutine only checked the "sub requested" flag once per (frozen) simulated minute. Replaced the per-minute `WaitForSeconds` with a per-frame poll (`yield return null` + `Time.deltaTime` accumulation) with an early-exit for `inMatchSubRequested && matchPaused`.
- **Hub byline text overlapping** ("Manager X · Matchday N" rendering garbled) after returning to the Hub. First fix attempt (destroy/recreate the label, tracking + cancelling any in-flight recovery coroutine) reduced it a lot but didn't eliminate it — see section 4, this turned out to be a different bug entirely.

### Bugs found in the second live-testing pass (after the first round landed)
- **Match Events scroll wheel felt backwards** (had to scroll down to see the *first* events). Negated `ScrollRect.scrollSensitivity` on that view. This is hardware/OS-scroll-setting-dependent and I can't verify the direction from here — if it's still backwards, it's a one-line sign flip back, needs the user to confirm live.
- **Skip to Results didn't work while paused** — identical root cause to the sub-picker bug, just never carried the same fix to `skipToResultsRequested`. Added `|| skipToResultsRequested` to the same early-exit check.
- **Hub byline overlap, actually fixed this time** — see section 4.
- Player Inspect weak-foot stars were rendering flush against "Weak Foot:" with no gap and sitting slightly high relative to the surrounding text baseline. Added a leading space + `<voffset=-0.15em>` around the star block.

## 3. A debugging detour worth remembering (session process lesson)

Spent real effort chasing the hub byline overlap as a TMP mesh-rendering race (matches the *documented* class of TMP flakiness in this file's "gotchas" — mesh generation silently failing on any label, fixed by destroy+recreate elsewhere in this codebase). That similarity was a red herring. The actual cause: earlier in *this same session*, a compile error (`TextAlignmentOptions.MidlineCenter` doesn't exist) silently blocked every Play Mode entry attempt for a while. During that stuck window, some of my automated test calls executed against the **Edit Mode** scene instead of a real Play session (confirmed by a `"Destroy may not be called from edit mode!"` console error at the time). Edit-mode object creation is *permanent* — it gets baked into the live scene state, survives every subsequent Play Mode stop/restart, unlike normal Play-mode-created objects which are torn down automatically.

I found and cleaned up one symptom of this (duplicate League Table header rows) earlier in the session, but only checked the League Table specifically — I didn't do a broader sweep, so a second piece of debris (a stray, permanently-baked-in "Byline" GameObject under `SeasonHubPanel`, frozen forever at "Matchday 1") sat there unnoticed. Every fresh Play session then legitimately built a *second*, correctly-updating Byline alongside the abandoned first one — hence the overlap, and hence why it looked like a rendering timing bug (both texts were real, simultaneously-existing GameObjects, not a mesh artifact).

**Diagnosis method that actually worked**: added a temporary `Debug.LogError` with a full stack trace at the top of the suspect builder method (`BuildHubChrome`), reproduced live, and read the trace — confirmed it was only ever called once per session (ruling out a runtime double-call), which meant the duplicate had to already exist *before* Play Mode started. Checked the Edit Mode scene directly and found it sitting there with stale runtime-set text. Removed it with `result.DestroyObject(...)`, verified clean across multiple fresh matchdays.

**Takeaway for next time a Unity Editor tool call reports something like "Destroy may not be called from edit mode" or "no fixtures found for X" for no obvious reason**: that's the signature of code running in Edit Mode when Play Mode was actually intended (usually because Play Mode silently failed to start — check for a compile error first, `Unity_GetConsoleLogs` with `logTypes: "Error"`). Any object creation that happened during that window needs a *full, deliberate scene-wide sweep* to clean up (not just the one symptom that happened to surface first) — a duplicate-named-sibling scan under the whole Canvas is the fastest way to check.

## 4. Outstanding — not started / needs the user to bring back up

- **Design overhaul**: the user had design redo the mockups at **1080×1920** (portrait), moving/adding things to fill the space that opened up compared to the old 960×540-derived layout. Mockups not sent yet as of end of session — this is the explicit next priority once received. Given the current canvas is landscape (960×540 reference), this is likely a substantial re-layout, not a tweak.
- **More sophisticated squad/stat generator** — explicitly deferred by the user this session (discussed at length but told to "bank it"). Summary of what was discussed, for whoever picks it up:
  - Team strength (`StatisticalModel.GetTeamStrength`'s attack/defence ratios) barely differentiates generated squads today — `AgentSquadGenerator.GenerateSquad` only blends 35% of the way toward the real ratio (`Mathf.Lerp(1f, attackStrength, 0.35f)`), so a genuinely dominant club and a genuinely weak one produce similarly-rated players.
  - Every attribute is rolled independently (`Random.Range` per stat), so a single player can have internally inconsistent profiles (e.g. elite Creativity, poor Passing) with nothing tying them together.
  - Name pool (30 first × 30 last names) is deduplicated only within a team, not league-wide — guaranteed cross-team collisions.
  - No age/potential/development arc at all — pure one-shot snapshot, regenerated fresh every session (no save system).
  - Ideas discussed, cheapest first: raise the strength-blend factor (or drive it off league percentile); dedupe names league-wide; roll one "quality" value per player and derive individual stats from it with small noise instead of fully independent rolls; add a basic age/prime-years curve; move `PlayerAgent.GetOverallRating`'s 11 near-identical per-position weight tables to static data instead of inline code.
- **Simplify match event text** to match the mockups' minimalistic FM (Football Manager) style — current text is narrative/verbose. This is the same "condensed Match Day event text with assists" item that's been carried forward across at least two previous handoffs without being picked up. Needs the actual event-list mockup pulled from Claude Design when it's picked up.
- Scroll-direction fix (section 2) needs live confirmation from the user — I can't verify hardware scroll-sign from here.

## 5. Gotchas learned this session (save yourself the rediscovery time)

- **`TextAlignmentOptions.MidlineCenter` does not exist** — the correct value is `TextAlignmentOptions.Center`. This typo caused a real compile error that silently blocked every Play Mode entry attempt for a long stretch (see section 3) — if Play Mode ever seems mysteriously "stuck" (isPlaying doesn't flip to true no matter how long you wait, or you see odd edit-mode-flavored console errors), check for a compile error *first* via `Unity_GetConsoleLogs` before assuming it's a tooling/environment issue.
- **TMP `<sprite>` tags have no `size=` attribute.** `<sprite name="x" size=60%>` doesn't error, it just silently fails to parse and prints the tag text literally. Use `<size=60%><sprite name="x"></size>` instead.
- **A TMP label assigned a `spriteAsset` can resolve a second glyph via that asset's own `fallbackSpriteAssets` list** — no need for a combined multi-glyph sprite asset or per-instance asset-switching in the text itself. Set it once on the asset (`starFilled.fallbackSpriteAssets = new List<TMP_SpriteAsset> { starEmpty }`), assign only `starFilled` to the label.
- **`Time.timeScale = 0` (pause) freezes any `WaitForSeconds`-based coroutine solid** — including its ability to *notice* a flag that got set while frozen. Anything that needs to react to input while paused (a sub picker, skip-to-results, anything similar in future) needs a per-frame poll (`yield return null` + manual `Time.deltaTime` accumulation) with an explicit early-exit check, not a blocking wait.
- **Editor UI helper functions that "normalize" a hand-placed button's style need to touch alignment and font weight, not just font/size/color** — `ManagerUITheme.NormalizeButtonLabel`/`StyleHubActionButton` both had this gap; any hand-placed Editor-era button routed through them still silently kept whatever alignment/weight the Editor originally gave it.
- **Unity's own "Create > TextMeshPro > Sprite Asset" menu item, invoked via `EditorApplication.ExecuteMenuItem` with `Selection.activeObject` set to the source texture, is far more reliable than manually constructing a `TMP_SpriteAsset` via `ScriptableObject.CreateInstance` + hand-built `TMP_SpriteGlyph`/`TMP_SpriteCharacter` tables** — the manual approach threw a `NullReferenceException` inside TMP's own `UpgradeSpriteAsset()` migration path the moment any table property was accessed on a freshly-created instance.
- **A fresh `RectTransform`'s default `sizeDelta` is (100,100)** — under stretched anchors this *adds* 100px to the computed size rather than being ignored. Hit this exact bug twice this session (bench scrollbar handle, then avoided it proactively on the Match Events scrollbar). Always explicitly zero `sizeDelta` (or set `offsetMin`/`offsetMax` instead) on any RectTransform using stretched anchors.
- **Edit-mode script execution (accidental or otherwise) creates *permanent* scene objects** that survive every subsequent Play Mode stop/restart — unlike normal Play-mode-created objects. If you ever suspect this happened (see section 3's diagnosis method), do a full duplicate-named-sibling sweep of the whole Canvas, not just the one symptom you noticed.
- Carried forward from last session, still true: `Destroy()` makes a `UnityEngine.Object` compare `== null` immediately despite deferred actual destruction; TMP mesh generation can silently fail on any label (not just the first one ever built), destroy+recreate after a frame is the only known fix; Unity's MCP/RunCommand tooling occasionally needs a full Play Mode restart to clear stale state; screenshots taken in the same tool call as the action they capture come back stale; `Image` needs full qualification (`UnityEngine.UI.Image`) inside `Unity_RunCommand` scripts; `System.Reflection` is blocked there.

## 6. Constraints (binding, do not relax without asking)

- Do not modify Research Mode behaviour/results without explicit confirmation. `AgentMatchSimulator.SimulateMatch` must stay byte-for-byte unchanged. (Confirmed untouched this session — every change was in `Manager/*.cs`, `Sprites`/`Resources` assets, or the font SDF asset's auto-regenerated atlas.)
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation — one explicit ask at a time.
- User pushes via GitHub Desktop themselves — give commit messages, don't run `git push`. **Exception this session**: user explicitly asked for both commit *and* push at end of session — flagged the standing preference back to them before doing anything, deferring to whatever they say in the moment this file is read.
- Build all dynamic UI positioning via code (`Build*Chrome()` methods), not hand-dragging in the Editor.
- No multithreading/parallelism — out of scope for this project.
- Tactics Board aspect ratio is now design-approved and implemented (1130:700) — not an open question anymore, but still don't relitigate proportions without design's input if it comes up again.

## 7. Suggested commit message for this session's changes

```
fix: Manager Mode design-fidelity pass and live-testing bug fixes

Design fidelity (matching Claude Design mockups):
- Tactics Board pitch constrained to 1130:700 aspect ratio, centered,
  instead of stretched full-width - formations are now readable
- Raised pin vertical-compression (0.66->0.85) - fixed GK/CB overlap
  in back-three formations
- Bench + Match Events lists get visible scrollbars (both were already
  functionally scrollable, just undiscoverable)
- Full-time summary header/stats spacing reworked to match mockup
- League table shows GD instead of GF/GA (display-only, Sim layer
  unchanged)
- Player Inspect attribute bars show numeric values
- Match screen buttons (tactic pills, Pause/Skip) normalized to
  consistent alignment/weight - were hand-placed pre-reskin buttons
  that never got styled
- Imported football icon + weak-foot star PNGs as TMP Sprite Assets,
  wired into goal-scorer lines and Player Inspect

Bug fixes from live testing:
- Match screen no longer corrupts on matchday 2+ (full-time-only
  layout changes weren't being reset before the next live match)
- Pause + substitution / Pause + Skip to Results now work immediately
  instead of only taking effect on Resume (Time.timeScale=0 was
  freezing the coroutine's ability to notice the request)
- Fixed a genuinely stray, permanently-baked-in duplicate Hub byline
  label left over from an earlier edit-mode execution incident this
  session (not a rendering race, despite looking like one)
- Match Events scroll direction reversed per user report
```

## 8. Suggested first prompt for the next session

> Continuing the FootballResearchProject on branch `unity6-ai-prototype` - see `HANDOFF.md` for full context. Last session was a big design-fidelity + live-bug-fixing pass on the Tactics Board/Manager Mode screens (all committed). Two things queued up for this session:
>
> 1. I'm redoing the mockups at 1080×1920 (portrait) - some things moved/got added to fill space that opened up vs. the old layout. I'll share them - implement against the new mockups.
> 2. After that, let's build a more sophisticated squad/stat generator - see HANDOFF.md section 4 for what we discussed last time (team strength barely differentiates squads today, attributes aren't correlated per-player, name pool collides across teams, no age/development arc).
>
> Same hard constraints as always: don't touch Research Mode without asking, keep Manager Mode separate, no new features without confirming with me first, give commit messages but don't push unless I explicitly say so in this session too.
