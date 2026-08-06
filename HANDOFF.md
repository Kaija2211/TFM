# Manager Mode UI Overhaul — Session Handoff (2026-08-07)

## 1. Branch / project state
- Branch: `unity6-ai-prototype` (main branch holds the stable research baseline, untouched).
- Working tree: **clean**, committed through `ca44091` ("feat: Unity MCP live-Editor Manager Mode overhaul (sizing, Oswald font, screen restructure)"). Not yet pushed — push via GitHub Desktop before switching machines.
- **First session with live Unity Editor access** via Unity MCP connected to *Claude Code* (not Claude Desktop — those are separate, non-interchangeable client configs; if MCP tools aren't available in a fresh session, that's the likely cause, not a broken install). This let bugs get diagnosed via live `SerializedObject` inspection and `Unity_GetConsoleLogs` instead of guessing from static files — much faster, use it again.

## 2. What happened this session

**Root-cause fixes (the two biggest wins):**
- `CanvasScaler.referenceResolution` was `1920×1080` but every mockup was authored at 960px wide — everything had effectively been rendering at half scale since the reskin began. Corrected to `960×540`. This one change fixed the majority of "everything's too small" reports across every screen.
- Default TMP font was inconsistent site-wide. Imported **Oswald** (OFL-licensed, `Assets/Fonts/Oswald-Variable.ttf` + generated `Oswald SDF.asset`) as the project default, and fixed several `ManagerUITheme.cs`/`ManagerPrototypeController.cs` styling call sites that only ever set color/size and never `.font`, leaving pre-existing buttons on stale fonts. Oswald has **no star/symbol/emoji glyphs** — em-dash, ★☆, and ⚽ all render as nothing; `·` (middot) is the established safe substitute, used everywhere a separator/bullet was needed.

**Design v2 restructure** (from updated Claude Design mockups, fetched via `DesignSync`):
- Title: new wordmark ("TF" + accent "M"), replacing the old shield + "Matchday Manager" text.
- Matchday Prep: simplified to scouting-only — Tactic buttons and Substitutions moved off this screen entirely.
- Match Day (live): gained real Tactic pills and a Substitutions section (moved in from Matchday Prep); "Key Moments" renamed "Match Log".
- Full-Time Summary: removed the inline event log, centered Match Stats, added real goal-scorer lists under the score (new `AgentMatchEvent.ScorerName` field, set from `shooter.Name` at the `IsGoal=true` point in `AgentMatchSimulator.ResolveAttack` — purely additive, verified via `git diff`), and a "View Match Events →" link.
- New **Match Events** screen: scrollable full match timeline, built entirely from scratch (`BuildMatchEventsPanel`/`PopulateMatchEventsList`), "← Back to Summary" link.

**Bug fixes found via live Play-mode testing this session** (each below was reported by the user while in Play mode, then fixed and confirmed compiling — see section 4 for which are still untested live):
- Pre-match squad subs were capped at 5 (the in-match limit) — now unlimited pre-match; the 5-per-match cap only applies once `subFlowIsInMatch` is true.
- Player Detail: attribute numeric values removed per mockup; Weak Foot switched from a star rating (`BuildStarRating`) to a bar-style rating (`BuildFootRating`) since Oswald can't render stars.
- Player Detail nav buttons (`Back`/`Previous`/`Next`) were vertically off — fixed a centering formula bug (was using `footerHeight/2f` for a bottom-pivoted button; correct is `(footerHeight - buttonHeight)/2f`).
- Match Log text was overlapping the footer — `RectMask2D` was mistakenly added directly to the text object instead of a parent container; fixed by wrapping in a dedicated `EventFeedMask` GameObject.
- Match Day right column ("+ Add Substitution" button, Substitutions/Match Stats captions) was floating mid-panel instead of left-aligned in its column — root cause was `ManagerUITheme.SetPointAnchor` always forcing `pivot == anchor`, wrong for a left-edge-referenced column at `anchor.x=0.55`. Fixed with explicit `.pivot = new Vector2(0f, 1f)` overrides after each call.
- Tactic buttons (`attackingButton`/`balancedButton`/`defensiveButton`) were repositioned for the new Match Day footer but never actually `SetParent`'d there — they stayed children of Matchday Prep and didn't show up live. Fixed by adding the missing `.transform.SetParent(footerBand.transform, false)` calls.
- Title screen "New Career" button was off-screen — a leftover manual Editor edit on `titleContentContainer` (non-default `localScale`, plus a second baked-in point-anchor offset) is now defensively reset in code (scale, anchors, position, size all forced to full-stretch defaults) every time the screen builds.

## 3. Outstanding — needs live verification next session
Two fixes above compile clean but were made right at the end of the session and have **not yet been tested live** (user hadn't re-entered Play mode before requesting this handoff):
1. The Match Day right-column pivot fix (Substitutions caption/status, Add Substitution button, Match Stats caption).
2. The tactic-button reparenting fix (buttons should now visibly appear in the live Match Day footer, not Matchday Prep).

Also still open:
- **Title screen logo**: user reported "no new logo" once, but my diagnostic check ran after they'd already navigated off the Title screen, so it read as inconclusive (everything under an inactive panel correctly reports `activeInHierarchy=False`). Needs a re-check while actually on the Title screen.
- User was mid-way through a full playthrough pass (Title → New Career → Hub → Matchday Prep → live Match Day incl. a sub and a tactic change → Full-Time → Match Events) when the session ended — worth resuming that pass first thing.

## 4. Deferred feature — discussed, not started
User wants to condense Match Day event text (mockup shows short lines like "GOAL! Felix Schneider converts for AFC Bournemouth.") and wants a "GOAL! [scorer] assisted by [assister]" format. Discussed and technically scoped (would need an additive `AssistName` field on `AgentMatchEvent`, format handling for the no-assist/solo-goal case, condensing done on the Manager Mode display side only) but **explicitly deferred by the user** — no code written. Don't start this without the user bringing it back up.

## 5. Constraints (binding, do not relax without asking)
- Do not modify Research Mode behaviour/results without explicit confirmation. `AgentMatchSimulator.SimulateMatch` must stay byte-for-byte unchanged — any shared-sim-code touch (`ScorerName` this session, `EnsureTeam` on `LeagueTable.cs`) must be purely additive and verified via `git diff` before moving on.
- Keep Manager Mode and Research Mode architecturally separate.
- Do not add new features without confirmation — one explicit ask at a time (see section 4).
- User pushes via GitHub Desktop themselves — give commit messages, don't run `git push`.
- Build all dynamic UI positioning via code (`Build*Chrome()` methods), not hand-dragging in the Editor — every manual-reposition bug this session (and last) came from stale/leftover manual edits; every code-positioned element was reliably fixable by re-deriving the anchor math.
- No multithreading/parallelism — out of scope for this project (see `PROJECT_CONTEXT_FOR_AI.md` guardrail #11).

## 6. Suggested first prompt for the next session

> Continuing the FootballResearchProject on branch `unity6-ai-prototype` — see `HANDOFF.md` for full context. Last session did a big v2 design restructure (new Title wordmark, Match Day gained Tactic/Subs, Full-Time Summary got a new Match Events screen) plus root-cause fixes for the sizing (CanvasScaler reference resolution) and font-consistency bugs. Working tree is clean through commit `ca44091`.
>
> Two recent fixes need live verification first (Match Day right-column layout, tactic buttons showing up in live Match Day — see section 3), then resume the full playthrough pass that was interrupted (Title → New Career → Hub → Matchday Prep → live Match Day → Full-Time → Match Events).
>
> Same hard constraints as always: don't touch Research Mode without asking, keep Manager Mode separate, no new features without confirming with me first (including the condensed-match-event-text idea we discussed but deferred — see section 4), give commit messages but don't push.
