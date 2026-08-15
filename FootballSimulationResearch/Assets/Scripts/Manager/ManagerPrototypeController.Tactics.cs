using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Data;
using Sim;
using Manager.Save;

namespace Manager
{
    public partial class ManagerPrototypeController
    {
        // --- Squad: Tactics Board (pitch view, position-pinned starters, drag a bench
        // card onto a pin to substitute, switch formation from the header dropdown - no
        // Editor-placed panel to wire, built entirely in code the first time it's
        // opened, same precedent as Match Events). Mid-match subs also go through this
        // same board now (see OnOpenTacticsBoardDuringMatchClicked) - the old in-match
        // off-then-on picker flow (playerListPanel/squadListView) is gone entirely. ---

        public void OnViewSquadClicked()
        {
            if (!tacticsBoardChromeBuilt)
            {
                BuildTacticsBoardChrome();
                tacticsBoardChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);

            RefreshTacticsBoardUI();
        }

        public void OnTacticsBoardBackClicked()
        {
            if (tacticsBoardOpenedMidMatch && !TryCommitMidMatchTacticsDraft())
            {
                return;
            }

            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            CloseTacticsBoardFormationDropdown();
            CleanupStrayDragGhosts();

            if (tacticsBoardOpenedMidMatch)
            {
                // Opened via "Make Changes" during a live match - return there instead of
                // the Hub, and don't auto-resume (matches every other manual pause/resume
                // flow in this file - the user hits Resume explicitly when ready).
                tacticsBoardOpenedMidMatch = false;
                if (matchdayPanel != null) matchdayPanel.SetActive(true);
            }
            else
            {
                ShowSeasonHub();
            }
        }

        // Belt-and-suspenders on top of TacticsBoardPlayerCard's own drag-cleanup fixes -
        // a drag ghost is parented directly to the root Canvas (so it can float above
        // everything while dragging), which means it survives a screen change even if
        // something upstream left it undestroyed. Cheap no-op when nothing's stray;
        // called on every way of leaving the Tactics Board so one can never linger onto
        // whatever screen comes next (confirmed live: a click firing mid-drag navigated
        // to Player Inspect with the ghost still floating on top of it).
        private void CleanupStrayDragGhosts()
        {
            if (tacticsBoardPanel == null)
            {
                return;
            }

            Transform canvasTransform = tacticsBoardPanel.transform.parent;

            if (canvasTransform == null)
            {
                return;
            }

            for (int i = canvasTransform.childCount - 1; i >= 0; i--)
            {
                Transform child = canvasTransform.GetChild(i);

                if (child.name == "DragGhost")
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void BuildTacticsBoardChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            tacticsBoardPanel = new GameObject("TacticsBoardPanel", typeof(RectTransform));
            tacticsBoardPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = tacticsBoardPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(tacticsBoardPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(tacticsBoardPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -28f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SQUAD", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Just "BACK" rather than "BACK TO HUB" - this same button/label is reused
            // for the mid-match "Make Changes" flow (see OnTacticsBoardBackClicked),
            // where it actually returns to the live match, not the Hub.
            Button backButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "BACK", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(150f, 36f));
            backButton.onClick.AddListener(OnTacticsBoardBackClicked);

            tacticsBoardFormationButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "FORMATION", ManagerUITheme.CardNeutral, ManagerUITheme.TextPrimary, 14);
            ManagerUITheme.SetPointAnchor(tacticsBoardFormationButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-226f, -27f), new Vector2(200f, 36f));
            tacticsBoardFormationButton.onClick.AddListener(ToggleTacticsBoardFormationDropdown);

            // Not in the mockup (which assumed a separate Squad-list-first navigation) -
            // the user's chosen flow keeps the Tactics Board as the direct landing screen
            // from the Hub's Squad button, with this as the way to reach the read-only
            // Squad list instead of the other way around.
            Button listViewButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "LIST VIEW", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(listViewButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-442f, -27f), new Vector2(150f, 36f));
            listViewButton.onClick.AddListener(OnOpenSquadListClicked);

            // Session 7 - sliders + captaincy/set-piece-taker assignment, centralized
            // here instead of scattered across each player's own detail page.
            Button tacticsScreenButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "TACTICS", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(tacticsScreenButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-598f, -27f), new Vector2(150f, 36f));
            tacticsScreenButton.onClick.AddListener(OnOpenTacticsScreenClicked);

            // Auto-pick best XI (backlog item, session 12) - fills every pin with the
            // highest position-fit player available for that slot, from the whole squad
            // (Starting XI + Bench combined), skipping injured/already-subbed-off players.
            // Built regardless of what AI clubs do (Thomas's call): it doesn't raise the
            // team's strength ceiling, a manager could always assemble this same XI by
            // hand - this just automates the clicking.
            autoPickButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "AUTO-PICK XI", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(autoPickButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-764f, -27f), new Vector2(170f, 36f));
            autoPickButton.onClick.AddListener(OnAutoPickBestXIClicked);
            autoPickButton.onClick.AddListener(ManagerAudio.PlayClick);

            // Per-match tactical override toggle (new feature suggestion, session 14) -
            // see the field-level comment above for the full design. Pre-match only
            // (hidden mid-match, same as Auto-Pick - see RefreshTacticsBoardUI), since
            // "revert after this match" has no meaning once a match is already live.
            nextMatchOnlyButton = ManagerUITheme.BuildButton(tacticsBoardPanel.transform, "NEXT MATCH ONLY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(nextMatchOnlyButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-954f, -27f), new Vector2(180f, 36f));
            nextMatchOnlyButton.onClick.AddListener(OnNextMatchOnlyToggleClicked);
            nextMatchOnlyButton.onClick.AddListener(ManagerAudio.PlayClick);

            // Injury block warning (session 10) - centered under the header row, empty
            // by default (BuildLabel with empty text still reserves the space so it pops
            // in without shifting anything else when a blocked drop fills it).
            GameObject warningObj = new GameObject("InjuryWarning", typeof(RectTransform));
            warningObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform warningRect = warningObj.GetComponent<RectTransform>();
            warningRect.anchorMin = new Vector2(0.5f, 1f);
            warningRect.anchorMax = new Vector2(0.5f, 1f);
            warningRect.pivot = new Vector2(0.5f, 1f);
            warningRect.sizeDelta = new Vector2(700f, 24f);
            // The gap between the header's own bottom accent line and the pitch's real top
            // edge turned out too tight to fit this label in at all (~25 world-units for a
            // ~22-unit-tall label, confirmed via live GetWorldCorners measurement - two
            // earlier attempts to thread that gap both failed live). Placed in the header's
            // own background instead, below the button row (bottom edge at local y=-45,
            // all buttons are anchoredPosition.y=-27 with height 36) and above the accent
            // line at the header's bottom edge (local y=-90) - a genuinely empty ~45-unit
            // band with real margin on both sides, confirmed live.
            warningRect.anchoredPosition = new Vector2(0f, -headerHeight + 34f);
            tacticsBoardWarningLabel = ManagerUITheme.BuildLabel(warningObj.transform, "", 15, ManagerUITheme.Danger, TextAlignmentOptions.Center, FontStyles.Bold);

            // Body row: pitch (flex, capped at 1320px wide) beside a 300px vertical bench
            // rail, both filling the row band between the header and the panel's own
            // bottom margin - replaces the old bottom-anchored horizontal bench strip
            // beneath a fixed-1130:700-aspect-ratio pitch. Centered within a max-width
            // 1700px content region, matching the mockup's own centered body row (this
            // leaves a small unused margin after the bench rail when 1320+40+300 <
            // 1700, same as the mockup's own flex layout would - not a bug).
            const float outerContentWidth = 1700f;
            const float sideMargin = (1920f - outerContentWidth) / 2f;
            const float bodyPadding = 28f;
            const float benchRailWidth = 300f;
            const float columnGap = 40f;
            const float pitchMaxWidth = 1320f;

            float rowTop = headerHeight + bodyPadding;
            float rowHeight = panelRect.rect.height - headerHeight - bodyPadding * 2f;
            float availablePitchWidth = outerContentWidth - benchRailWidth - columnGap;
            float pitchWidth = Mathf.Min(pitchMaxWidth, availablePitchWidth);

            // Pitch: flat rectangles for the halfway line/penalty boxes (no sprites in
            // this project, same convention as everywhere else) - without them the pins
            // are just numbers scattered on a plain rectangle, with nothing anchoring the
            // eye to "this is a football formation" or explaining why the goalkeeper
            // sits close behind the back line. Pin positions come from TacticsBoardLayout.
            GameObject pitchObj = new GameObject("Pitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(tacticsBoardPanel.transform, false);
            tacticsBoardPitchContainer = pitchObj.GetComponent<RectTransform>();
            tacticsBoardPitchContainer.anchorMin = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.anchorMax = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.pivot = new Vector2(0f, 1f);
            tacticsBoardPitchContainer.anchoredPosition = new Vector2(sideMargin, -rowTop);
            tacticsBoardPitchContainer.sizeDelta = new Vector2(pitchWidth, rowHeight);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(tacticsBoardPitchContainer);

            float benchRailLeft = sideMargin + pitchWidth + columnGap;

            GameObject benchCaptionObj = new GameObject("BenchCaption", typeof(RectTransform));
            benchCaptionObj.transform.SetParent(tacticsBoardPanel.transform, false);
            ManagerUITheme.SetPointAnchor(benchCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(benchRailLeft, -rowTop), new Vector2(benchRailWidth, 18f));
            ManagerUITheme.BuildLabel(benchCaptionObj.transform, "BENCH", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject benchSubCaptionObj = new GameObject("BenchSubCaption", typeof(RectTransform));
            benchSubCaptionObj.transform.SetParent(tacticsBoardPanel.transform, false);
            ManagerUITheme.SetPointAnchor(benchSubCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(benchRailLeft, -(rowTop + 24f)), new Vector2(benchRailWidth, 16f));
            ManagerUITheme.BuildLabel(benchSubCaptionObj.transform, "DRAG ONTO THE PITCH TO SUBSTITUTE", 11, ManagerUITheme.TextDim, TextAlignmentOptions.MidlineLeft);

            float benchListTop = rowTop + 24f + 24f;
            float benchListHeight = rowHeight - 24f - 24f;

            // Vertical scroll rail: same ScrollRect+Viewport+Content pattern as every
            // other list in this file (SquadListView/LeagueTableView/MatchEvents), just
            // a plain VerticalLayoutGroup instead of the bench's old rotated horizontal
            // one now that it's a right-side column instead of a bottom strip.
            GameObject scrollViewObj = new GameObject("BenchScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 1f);
            scrollViewRect.anchorMax = new Vector2(0f, 1f);
            scrollViewRect.pivot = new Vector2(0f, 1f);
            scrollViewRect.anchoredPosition = new Vector2(benchRailLeft, -benchListTop);
            scrollViewRect.sizeDelta = new Vector2(benchRailWidth - 16f, benchListHeight);

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            tacticsBoardBenchContent = contentObj.GetComponent<RectTransform>();
            tacticsBoardBenchContent.anchorMin = new Vector2(0f, 1f);
            tacticsBoardBenchContent.anchorMax = new Vector2(1f, 1f);
            tacticsBoardBenchContent.pivot = new Vector2(0.5f, 1f);
            tacticsBoardBenchContent.anchoredPosition = Vector2.zero;
            // Height must be explicit, not zero - childForceExpandHeight below stretches
            // every card to fill THIS rect's own height, so a zero-height Content
            // silently squashed every bench card to zero height too (invisible despite
            // existing, with correct width/position - confirmed live, on the old
            // horizontal version of this same rail).
            tacticsBoardBenchContent.sizeDelta = new Vector2(0f, 76f);

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.spacing = 10f;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = viewportRect;
            scrollRect.content = tacticsBoardBenchContent;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            // Slim scrollbar in the 16px gap to the right of the card column - the bench
            // rail itself is already a working vertical ScrollRect (drag or mouse-wheel
            // scrolls it), but with more bench players than fit in one screen's height
            // and no visible affordance, it reads as broken/missing subs rather than
            // "there's more, scroll for it" (same lesson as the old horizontal strip's
            // scrollbar, and Match Events' vertical one).
            GameObject scrollbarObj = new GameObject("BenchScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(0f, 1f);
            scrollbarRect.anchorMax = new Vector2(0f, 1f);
            scrollbarRect.pivot = new Vector2(0f, 1f);
            scrollbarRect.anchoredPosition = new Vector2(benchRailLeft + benchRailWidth - 10f, -benchListTop);
            scrollbarRect.sizeDelta = new Vector2(6f, benchListHeight);
            scrollbarObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject handleAreaObj = new GameObject("SlidingArea", typeof(RectTransform));
            handleAreaObj.transform.SetParent(scrollbarObj.transform, false);
            RectTransform handleAreaRect = handleAreaObj.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObj.transform.SetParent(handleAreaObj.transform, false);
            RectTransform handleRect = handleObj.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = new Vector2(1f, 0.3f);
            // Must be zeroed explicitly - a fresh RectTransform's default sizeDelta is
            // (100,100), which under stretched anchors ADDS 100px to the computed size
            // rather than being ignored (confirmed live on this exact scrollbar's
            // earlier horizontal incarnation).
            handleRect.sizeDelta = Vector2.zero;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            // BottomToTop, not the seemingly-obvious TopToBottom - ScrollRect's
            // verticalNormalizedPosition convention is 1=viewing the top of the content,
            // 0=viewing the bottom, and it drives the linked Scrollbar's .value directly.
            // Confirmed empirically (not guessed): with TopToBottom, value=1 (viewing the
            // list's top) rendered the handle at the BOTTOM of the track and vice versa -
            // exactly backwards, matching the reported "scroll to the bottom of the
            // scrollbar to see the top of the list" symptom.
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            BuildTacticsBoardFormationDropdown();

            // See BuildTeamSelectChrome's identical call for why. This only catches the
            // static chrome built here (title/buttons/captions) - pins and bench cards
            // are rebuilt fresh on every RefreshTacticsBoardUI call, so that method gets
            // its own sweep too.
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardPanel.transform));
        }

        // Halfway line + both penalty boxes, all built from thin flat-color rectangles
        // (no sprite/mesh assets in this project - a true circle for the center circle
        // isn't practical the same way, so it's skipped; the boxes alone are enough to
        // read as "this is a pitch" and to explain why the goalkeeper sits close behind
        // the back line). Static per formation - built once, not part of the per-refresh
        // pin rebuild.
        private void BuildPitchMarkings(RectTransform pitch)
        {
            Color lineColor = new Color(1f, 1f, 1f, 0.10f);

            GameObject halfwayLine = new GameObject("HalfwayLine", typeof(RectTransform), typeof(Image));
            halfwayLine.transform.SetParent(pitch, false);
            RectTransform halfwayRect = halfwayLine.GetComponent<RectTransform>();
            halfwayRect.anchorMin = new Vector2(0f, 0.5f);
            halfwayRect.anchorMax = new Vector2(1f, 0.5f);
            halfwayRect.pivot = new Vector2(0.5f, 0.5f);
            halfwayRect.anchoredPosition = Vector2.zero;
            // 2px, not 1px - these lines are declared at the 1920x1080 reference
            // resolution, and CanvasScaler downscales that reference pixel to LESS than
            // one real screen pixel whenever the actual window is smaller (not maximized/
            // fullscreen) - a sub-pixel-wide line at only 10% opacity anti-aliases down to
            // essentially invisible (confirmed live: almost the entire pitch marking set
            // vanished in a windowed, non-maximized Game view). 2px keeps a visible line
            // down to roughly half the reference resolution.
            halfwayRect.sizeDelta = new Vector2(0f, 2f);
            halfwayLine.GetComponent<Image>().color = lineColor;

            BuildPenaltyBox(pitch, atTop: true, lineColor);
            BuildPenaltyBox(pitch, atTop: false, lineColor);
        }

        // An open-ended rectangle (three sides, no side facing the halfway line) built
        // from three thin Image strips - top/bottom edge plus two verticals, same
        // "no sprites, flat rectangles" approach as everywhere else.
        private void BuildPenaltyBox(RectTransform pitch, bool atTop, Color lineColor)
        {
            const float boxWidthPercent = 0.30f;
            const float boxDepthPercent = 0.16f;

            float edgeY = atTop ? 1f : 0f;
            float innerY = atTop ? 1f - boxDepthPercent : boxDepthPercent;

            GameObject edgeLine = new GameObject(atTop ? "TopBoxLine" : "BottomBoxLine", typeof(RectTransform), typeof(Image));
            edgeLine.transform.SetParent(pitch, false);
            RectTransform edgeRect = edgeLine.GetComponent<RectTransform>();
            edgeRect.anchorMin = new Vector2(0.5f - boxWidthPercent, innerY);
            edgeRect.anchorMax = new Vector2(0.5f + boxWidthPercent, innerY);
            edgeRect.pivot = new Vector2(0.5f, 0.5f);
            edgeRect.anchoredPosition = Vector2.zero;
            // See BuildPitchMarkings' identical comment for why 2px, not 1px.
            edgeRect.sizeDelta = new Vector2(0f, 2f);
            edgeLine.GetComponent<Image>().color = lineColor;

            foreach (float xPercent in new[] { 0.5f - boxWidthPercent, 0.5f + boxWidthPercent })
            {
                GameObject sideLine = new GameObject(atTop ? "TopBoxSide" : "BottomBoxSide", typeof(RectTransform), typeof(Image));
                sideLine.transform.SetParent(pitch, false);
                RectTransform sideRect = sideLine.GetComponent<RectTransform>();
                sideRect.anchorMin = new Vector2(xPercent, Mathf.Min(edgeY, innerY));
                sideRect.anchorMax = new Vector2(xPercent, Mathf.Max(edgeY, innerY));
                sideRect.pivot = new Vector2(0.5f, 0.5f);
                sideRect.anchoredPosition = Vector2.zero;
                sideRect.sizeDelta = new Vector2(2f, 0f);
                sideLine.GetComponent<Image>().color = lineColor;
            }
        }

        private void BuildTacticsBoardFormationDropdown()
        {
            tacticsBoardFormationDropdown = new GameObject("FormationDropdown", typeof(RectTransform), typeof(Image));
            tacticsBoardFormationDropdown.transform.SetParent(tacticsBoardPanel.transform, false);
            RectTransform dropdownRect = tacticsBoardFormationDropdown.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(1f, 1f);
            dropdownRect.anchorMax = new Vector2(1f, 1f);
            dropdownRect.pivot = new Vector2(1f, 1f);
            // Right edge aligned with the Formation button's own right edge (-226, same
            // anchor/pivot), sitting just below its bottom edge (button top -27, height
            // 36, so bottom is -63) - was left at the button's old pre-rework position
            // (-30,-58), which no longer lines up now that the header also has the List
            // View button squeezed in next to it.
            dropdownRect.anchoredPosition = new Vector2(-226f, -66f);
            dropdownRect.sizeDelta = new Vector2(200f, 6 * 34f);
            tacticsBoardFormationDropdown.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            VerticalLayoutGroup layoutGroup = tacticsBoardFormationDropdown.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;

            Formation[] formations =
            {
            Formation.FourThreeThree, Formation.FourTwoThreeOne, Formation.FourFourTwo,
            Formation.ThreeFiveTwo, Formation.ThreeFourThree, Formation.ThreeFourTwoOne
        };

            foreach (Formation formation in formations)
            {
                Button optionButton = ManagerUITheme.BuildButton(tacticsBoardFormationDropdown.transform, TacticsBoardLayout.FormatFormation(formation), ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                optionButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
                optionButton.onClick.AddListener(() => OnFormationSelected(formation));
            }

            tacticsBoardFormationDropdown.SetActive(false);
        }

        private void ToggleTacticsBoardFormationDropdown()
        {
            if (tacticsBoardFormationDropdown != null)
            {
                tacticsBoardFormationDropdown.SetActive(!tacticsBoardFormationDropdown.activeSelf);
            }
        }

        private void CloseTacticsBoardFormationDropdown()
        {
            if (tacticsBoardFormationDropdown != null) tacticsBoardFormationDropdown.SetActive(false);
        }

        // --- Tactics screen (session 7): sliders + captaincy/set-piece-taker assignment,
        // reached from the Tactics Board. Two independently right/left-edge-anchored
        // columns (not a fixed-width-assumption layout) - see
        // feedback_snapshot_anchor_drift_pattern for why that distinction matters after
        // the Matchday Prep pitch bug found earlier this session.

        public void OnOpenTacticsScreenClicked()
        {
            if (!tacticsScreenChromeBuilt)
            {
                BuildTacticsScreenChrome();
                tacticsScreenChromeBuilt = true;
            }

            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            if (tacticsScreenPanel != null) tacticsScreenPanel.SetActive(true);

            if (tacticsScreenBackButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(tacticsScreenBackButton, "BACK TO TACTICS BOARD", ManagerUITheme.TextBody, 13);
            }

            RefreshTacticsScreenUI();
        }

        public void OnTacticsScreenBackClicked()
        {
            if (tacticsScreenPanel != null) tacticsScreenPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);
        }

        private void BuildTacticsScreenChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            tacticsScreenPanel = new GameObject("TacticsScreenPanel", typeof(RectTransform));
            tacticsScreenPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = tacticsScreenPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(tacticsScreenPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(tacticsScreenPanel.transform, topBand: true, height: TacticsScreenHeaderHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -28f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "TACTICS", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(tacticsScreenPanel.transform, "BACK TO TACTICS BOARD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(240f, 36f));
            backButton.onClick.AddListener(OnTacticsScreenBackClicked);
            tacticsScreenBackButton = backButton;

            ManagerUITheme.BuildAccentBand(tacticsScreenPanel.transform, topBand: false, height: TacticsScreenFooterHeight);

            // Everything here is already live the moment you pick it (same immediate-
            // apply pattern as every other assignment this session) - SAVE has nothing
            // to actually commit, it's just a clearly-labeled way back matching the
            // mockup's own footer.
            Button saveButton = ManagerUITheme.BuildButton(tacticsScreenPanel.transform, "SAVE", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(saveButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, 22f), new Vector2(180f, 46f));
            saveButton.onClick.AddListener(OnTacticsScreenBackClicked);

            tacticsScreenPanel.SetActive(false);
        }

        private const float TacticsScreenHeaderHeight = 90f;
        private const float TacticsScreenFooterHeight = 90f;

        private void RefreshTacticsScreenUI()
        {
            if (tacticsScreenPanel == null)
            {
                return;
            }

            foreach (GameObject element in spawnedTacticsScreenElements)
            {
                if (element != null) Destroy(element);
            }

            spawnedTacticsScreenElements.Clear();
            tacticsScreenOpenDropdowns.Clear();

            const float columnTopMargin = 30f;

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            // Starting XI only, deliberately excluding Bench (backlog item, session 12,
            // Thomas's call) - scrolling past the Starting XI into Bench with no visual
            // separator risked accidentally assigning e.g. Captain to a player not even
            // playing. A bench player realistically shouldn't hold any of these roles
            // anyway. A role already pointing at a bench player from before this change
            // still displays correctly (BuildRoleDropdownRow shows currentValue.Name
            // regardless of the options list), it just won't be re-selectable here.
            List<PlayerAgent> squadPlayers = new List<PlayerAgent>(team.StartingEleven);

            // Matches the actual design mockup (DesignSync, "Football Manager UI
            // Concepts.dc.html", TACTICS frame) exactly: a centered fixed-width row -
            // 520px left column + 80px gap + 2px partition + 80px gap + 820px right
            // column = 1502px total - rather than the earlier version's two
            // independently-edge-anchored fraction-width columns, which was drift-safe
            // but left the columns reading as "far apart" on a real 1920-wide canvas.
            // Anchored from the CENTER (0.5) with fixed pixel offsets on either side of
            // it, not from an edge assuming a literal total canvas width - the center
            // point itself is always correct regardless of true canvas width/aspect
            // ratio, so this is just as immune to the drift bug as the fraction version
            // was, without the oversized gap.
            const float leftColumnWidth = 520f;
            const float rightColumnWidth = 820f;
            const float columnRowGap = 80f;
            const float partitionWidth = 2f;
            const float partitionVerticalInset = 60f;

            float halfTotalWidth = (leftColumnWidth + columnRowGap + partitionWidth + columnRowGap + rightColumnWidth) / 2f;
            float leftColumnLeft = -halfTotalWidth;
            float leftColumnRight = leftColumnLeft + leftColumnWidth;
            float partitionLeft = leftColumnRight + columnRowGap;
            float partitionRight = partitionLeft + partitionWidth;
            float rightColumnLeft = partitionRight + columnRowGap;

            GameObject leftColumn = new GameObject("ShapeApproachColumn", typeof(RectTransform));
            leftColumn.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform leftColumnRect = leftColumn.GetComponent<RectTransform>();
            leftColumnRect.anchorMin = new Vector2(0.5f, 0f);
            leftColumnRect.anchorMax = new Vector2(0.5f, 1f);
            leftColumnRect.offsetMin = new Vector2(leftColumnLeft, TacticsScreenFooterHeight);
            leftColumnRect.offsetMax = new Vector2(leftColumnRight, -(TacticsScreenHeaderHeight + columnTopMargin));
            spawnedTacticsScreenElements.Add(leftColumn);

            GameObject partition = new GameObject("Partition", typeof(RectTransform), typeof(Image));
            partition.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform partitionRect = partition.GetComponent<RectTransform>();
            partitionRect.anchorMin = new Vector2(0.5f, 0f);
            partitionRect.anchorMax = new Vector2(0.5f, 1f);
            partitionRect.offsetMin = new Vector2(partitionLeft, TacticsScreenFooterHeight + partitionVerticalInset);
            partitionRect.offsetMax = new Vector2(partitionRight, -(TacticsScreenHeaderHeight + columnTopMargin + partitionVerticalInset));
            partition.GetComponent<Image>().color = ManagerUITheme.BarTrack;
            spawnedTacticsScreenElements.Add(partition);

            GameObject shapeCaption = new GameObject("Caption", typeof(RectTransform));
            shapeCaption.transform.SetParent(leftColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(shapeCaption, 0f, 20f, 0f);
            ManagerUITheme.BuildLabel(shapeCaption.transform, "SHAPE & APPROACH", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            float sliderTop = 40f;
            sliderTop = BuildSliderRow(leftColumn.transform, "WIDTH", sliderTop,
                new[] { "NARROW", "BALANCED", "WIDE" }, (int)tacticalSliders.Width,
                index => { tacticalSliders.Width = (WidthSetting)index; RefreshTacticsScreenUI(); });

            sliderTop = BuildSliderRow(leftColumn.transform, "DEFENSIVE DEPTH", sliderTop,
                new[] { "DEEP", "BALANCED", "HIGH LINE" }, (int)tacticalSliders.DefensiveDepth,
                index => { tacticalSliders.DefensiveDepth = (DefensiveDepthSetting)index; RefreshTacticsScreenUI(); });

            sliderTop = BuildSliderRow(leftColumn.transform, "TEMPO", sliderTop,
                new[] { "SLOW", "BALANCED", "FAST" }, (int)tacticalSliders.Tempo,
                index => { tacticalSliders.Tempo = (TempoSetting)index; RefreshTacticsScreenUI(); });

            GameObject fitSummary = new GameObject("TacticalFitSummary", typeof(RectTransform), typeof(Image));
            fitSummary.transform.SetParent(leftColumn.transform, false);
            RectTransform fitRect = fitSummary.GetComponent<RectTransform>();
            fitRect.anchorMin = new Vector2(0f, 1f);
            fitRect.anchorMax = new Vector2(1f, 1f);
            fitRect.pivot = new Vector2(0.5f, 1f);
            fitRect.offsetMin = new Vector2(0f, -(sliderTop + 92f));
            fitRect.offsetMax = new Vector2(0f, -sliderTop);
            fitSummary.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            ManagerUITheme.BuildLabel(fitSummary.transform, BuildTacticalFitSummary(team), 14,
                ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject rightColumn = new GameObject("RoleAssignmentColumn", typeof(RectTransform));
            rightColumn.transform.SetParent(tacticsScreenPanel.transform, false);
            RectTransform rightColumnRect = rightColumn.GetComponent<RectTransform>();
            rightColumnRect.anchorMin = new Vector2(0.5f, 0f);
            rightColumnRect.anchorMax = new Vector2(0.5f, 1f);
            rightColumnRect.offsetMin = new Vector2(rightColumnLeft, TacticsScreenFooterHeight);
            rightColumnRect.offsetMax = new Vector2(rightColumnLeft + rightColumnWidth, -(TacticsScreenHeaderHeight + columnTopMargin));
            spawnedTacticsScreenElements.Add(rightColumn);

            GameObject leadershipCaption = new GameObject("Caption", typeof(RectTransform));
            leadershipCaption.transform.SetParent(rightColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(leadershipCaption, 0f, 20f, 0f);
            ManagerUITheme.BuildLabel(leadershipCaption.transform, "LEADERSHIP", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Each role shows whichever stats actually matter for it - Leadership/
            // Composure/Age for captaincy (the exact inputs ManagerCaptaincyModifier's
            // suitability score reads), Crossing/Creativity for corners (the exact
            // PickCreatorForChance formula a designated corner taker's stats drive),
            // Free Kicks/Finishing+Composure for the other two, organizational for now
            // but still the honest "this is what a real free-kick/penalty taker needs"
            // proxy - rather than picking blind by name alone.
            static string[] CaptaincySummary(PlayerAgent p) => new[] { $"LDR {p.Leadership:F0}", $"COMP {p.Composure:F0}", $"AGE {p.Age}" };
            static string[] PenaltySummary(PlayerAgent p) => new[] { $"FIN {p.Finishing:F0}", $"COMP {p.Composure:F0}" };
            static string[] FreeKickSummary(PlayerAgent p) => new[] { $"FK {p.FreeKicks:F0}" };
            static string[] CornerSummary(PlayerAgent p) => new[] { $"CRS {p.Crossing:F0}", $"CRE {p.Creativity:F0}" };

            float roleTop = 40f;
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "CAPTAIN", roleTop, roles.Captain, squadPlayers,
                player => AssignRole(SquadRoleSlot.Captain, player), CaptaincySummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "VICE-CAPTAIN", roleTop, roles.ViceCaptain, squadPlayers,
                player => AssignRole(SquadRoleSlot.ViceCaptain, player), CaptaincySummary);

            roleTop += 30f;

            GameObject setPiecesCaption = new GameObject("Caption", typeof(RectTransform));
            setPiecesCaption.transform.SetParent(rightColumn.transform, false);
            ManagerUITheme.AnchorTopStretch(setPiecesCaption, roleTop, 20f, 0f);
            ManagerUITheme.BuildLabel(setPiecesCaption.transform, "SET PIECES", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            roleTop += 30f;

            roleTop = BuildRoleDropdownRow(rightColumn.transform, "PENALTY TAKER", roleTop, roles.PenaltyTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.PenaltyTaker, player), PenaltySummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "FREE-KICK TAKER", roleTop, roles.FreeKickTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.FreeKickTaker, player), FreeKickSummary);
            roleTop = BuildRoleDropdownRow(rightColumn.transform, "LEFT CORNER TAKER", roleTop, roles.LeftCornerTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.LeftCornerTaker, player), CornerSummary);
            BuildRoleDropdownRow(rightColumn.transform, "RIGHT CORNER TAKER", roleTop, roles.RightCornerTaker, squadPlayers,
                player => AssignRole(SquadRoleSlot.RightCornerTaker, player), CornerSummary);

            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsScreenPanel.transform));
        }

        // Moneyball-style tactical value: the same attributes used by the match engine's
        // chance pathways are summarized here, so a specialist can be valuable to this
        // system even when his headline Overall is unremarkable.
        private string BuildTacticalFitSummary(AgentTeam team)
        {
            if (team == null || team.StartingEleven.Count == 0) return "TACTICAL FIT —";

            float approach = tacticalSliders.Width == WidthSetting.Wide
                ? Average(team.StartingEleven, p => p.Crossing * 0.30f + p.Pace * 0.18f + p.Stamina * 0.14f + p.OffTheBall * 0.12f + p.Heading * 0.12f + p.JumpingReach * 0.14f)
                : tacticalSliders.Width == WidthSetting.Narrow
                    ? Average(team.StartingEleven, p => p.FirstTouch * 0.20f + p.Passing * 0.20f + p.Technique * 0.18f + p.Vision * 0.16f + p.Decisions * 0.16f + p.Agility * 0.10f)
                    : Average(team.StartingEleven, p => p.Passing * 0.18f + p.FirstTouch * 0.14f + p.Decisions * 0.14f + p.WorkRate * 0.12f + p.Pace * 0.12f + p.Technique * 0.12f + p.DefensivePositioning * 0.18f);

            float tempo = tacticalSliders.Tempo == TempoSetting.Fast
                ? Average(team.StartingEleven, p => p.Acceleration * 0.24f + p.Pace * 0.18f + p.Stamina * 0.18f + p.WorkRate * 0.16f + p.Decisions * 0.14f + p.OffTheBall * 0.10f)
                : tacticalSliders.Tempo == TempoSetting.Slow
                    ? Average(team.StartingEleven, p => p.FirstTouch * 0.22f + p.Passing * 0.20f + p.Technique * 0.18f + p.Decisions * 0.18f + p.Composure * 0.14f + p.Vision * 0.08f)
                    : Average(team.StartingEleven, p => p.Decisions * 0.20f + p.Composure * 0.18f + p.Passing * 0.16f + p.Stamina * 0.16f + p.FirstTouch * 0.15f + p.WorkRate * 0.15f);

            float defence = tacticalSliders.DefensiveDepth == DefensiveDepthSetting.High
                ? Average(team.StartingEleven, p => p.Acceleration * 0.20f + p.Pace * 0.18f + p.Anticipation * 0.20f + p.WorkRate * 0.16f + p.Stamina * 0.14f + p.DefensivePositioning * 0.12f)
                : tacticalSliders.DefensiveDepth == DefensiveDepthSetting.Deep
                    ? Average(team.StartingEleven, p => p.DefensivePositioning * 0.24f + p.Marking * 0.18f + p.JumpingReach * 0.16f + p.Strength * 0.14f + p.Anticipation * 0.16f + p.Heading * 0.12f)
                    : Average(team.StartingEleven, p => p.DefensivePositioning * 0.22f + p.Anticipation * 0.18f + p.Tackling * 0.16f + p.Decisions * 0.14f + p.Pace * 0.12f + p.Strength * 0.10f + p.WorkRate * 0.08f);

            float fit = approach * 0.40f + tempo * 0.30f + defence * 0.30f;
            return $"TACTICAL FIT  {fit:F0}/99\nAPPROACH {approach:F0}   ·   TEMPO {tempo:F0}   ·   DEFENCE {defence:F0}";
        }

        private static float Average(List<PlayerAgent> players, Func<PlayerAgent, float> selector)
        {
            float total = 0f;
            foreach (PlayerAgent player in players) total += selector(player);
            return players.Count > 0 ? total / players.Count : 0f;
        }

        // One row: a left-aligned label plus a 3-way toggle-button group, same
        // BuildRoleToggleButton control used for the attack/defend leaning on Player
        // Detail - deliberately not a literal drag Slider widget (the mockup that
        // inspired this screen was a layout suggestion, not a pixel spec) since the
        // backend is three discrete settings either way. Returns the top offset the next
        // row should start at.
        private float BuildSliderRow(Transform parent, string label, float top, string[] optionLabels, int currentIndex, Action<int> onSelect)
        {
            const float labelHeight = 22f;
            const float labelGap = 10f;
            const float buttonHeight = 40f;
            const float rowGap = 30f;

            GameObject labelObj = new GameObject("SliderLabel", typeof(RectTransform));
            labelObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelObj, top, labelHeight, 0f);
            ManagerUITheme.BuildLabel(labelObj.transform, label, 16, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject buttonRow = new GameObject("SliderButtons", typeof(RectTransform));
            buttonRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(buttonRow, top + labelHeight + labelGap, buttonHeight, 0f);

            float x = 0f;

            for (int i = 0; i < optionLabels.Length; i++)
            {
                int capturedIndex = i;
                x = BuildRoleToggleButton(buttonRow.transform, optionLabels[i], x, currentIndex == i, () => onSelect(capturedIndex));
            }

            return top + labelHeight + labelGap + buttonHeight + rowGap;
        }

        // One row: a left-aligned label, a button showing the current holder's name (or
        // "- None -") that toggles a scrollable list of every squad player to pick from.
        // statSummary formats whichever stats actually matter for this specific role
        // (e.g. Leadership/Composure/Age for captaincy, Crossing/Creativity for corners -
        // the same attributes the real formula/mechanism for that role reads, where one
        // exists) - Thomas's point: picking blind by name alone doesn't work for
        // generated players nobody already knows by heart. Returns the top offset the
        // next row should start at.
        private float BuildRoleDropdownRow(Transform parent, string label, float top, PlayerAgent currentValue, List<PlayerAgent> options, Action<PlayerAgent> onSelect, Func<PlayerAgent, string[]> statColumns)
        {
            const float rowHeight = 44f;
            const float rowGap = 14f;

            GameObject rowObj = new GameObject("RoleRow", typeof(RectTransform));
            rowObj.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(rowObj, top, rowHeight, 0f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(rowObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.42f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelObj.transform, label, 14, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject dropdownButtonObj = new GameObject("DropdownButton", typeof(RectTransform), typeof(Image), typeof(Button));
            dropdownButtonObj.transform.SetParent(rowObj.transform, false);
            RectTransform dropdownButtonRect = dropdownButtonObj.GetComponent<RectTransform>();
            dropdownButtonRect.anchorMin = new Vector2(0.44f, 0f);
            dropdownButtonRect.anchorMax = new Vector2(1f, 1f);
            dropdownButtonRect.offsetMin = Vector2.zero;
            dropdownButtonRect.offsetMax = Vector2.zero;
            Image dropdownButtonImage = dropdownButtonObj.GetComponent<Image>();
            dropdownButtonImage.color = ManagerUITheme.CardNeutral;
            Button dropdownButton = dropdownButtonObj.GetComponent<Button>();
            dropdownButton.targetGraphic = dropdownButtonImage;

            // No "v"/"— None —" (Thomas's call, session 12) - just the assigned name, or
            // blank until one's picked. BuildLabel stretches its label full-size with
            // zero padding, so the text touched the button's left edge directly - given
            // its own padded RectTransform here instead (same 10px inset BuildGridCell
            // uses elsewhere) rather than accepting that default.
            string currentLabel = currentValue != null ? currentValue.Name : "";
            TextMeshProUGUI dropdownLabel = ManagerUITheme.BuildLabel(dropdownButtonObj.transform, currentLabel, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            RectTransform dropdownLabelRect = dropdownLabel.GetComponent<RectTransform>();
            dropdownLabelRect.offsetMin = new Vector2(10f, 0f);
            dropdownLabelRect.offsetMax = new Vector2(-10f, 0f);

            // Two real bugs, found live and fixed together: (1) the dropdown used to be
            // nested inside its own row's button - Unity draws UI children in sibling
            // order, so it always rendered BEHIND every later row regardless of being
            // "open" (the garbled overlapping list Thomas saw). Building it as a sibling
            // of the row instead (parented to the column) and calling SetAsLastSibling()
            // on open fixes this by construction. (2) the option buttons used to be
            // built eagerly while the panel was still inactive - TMP labels built inside
            // an inactive hierarchy can permanently fail mesh generation (see
            // feedback_tmp_cached_label_reference_gotcha), which is why some rows showed
            // no names at all. Populating them only at the moment the panel actually
            // becomes active sidesteps the bug rather than trying to detect/repair it.
            GameObject dropdownPanel = BuildEmptyDropdownScaffold(parent, options.Count);
            RectTransform dropdownPanelRect = dropdownPanel.GetComponent<RectTransform>();
            dropdownPanelRect.anchorMin = new Vector2(0.44f, 1f);
            dropdownPanelRect.anchorMax = new Vector2(1f, 1f);
            dropdownPanelRect.pivot = new Vector2(0.5f, 1f);
            dropdownPanelRect.anchoredPosition = new Vector2(0f, -(top + rowHeight + 4f));

            Transform dropdownContent = dropdownPanel.transform.Find("Viewport/Content");
            tacticsScreenOpenDropdowns.Add(dropdownPanel);

            dropdownButton.onClick.AddListener(() =>
            {
                bool wasOpen = dropdownPanel.activeSelf;
                CloseAllTacticsDropdowns();

                if (!wasOpen)
                {
                    PopulateDropdownOptions(dropdownContent, options, onSelect, statColumns);
                    dropdownPanel.transform.SetAsLastSibling();
                    dropdownPanel.SetActive(true);
                }
            });

            return top + rowHeight + rowGap;
        }

        private void CloseAllTacticsDropdowns()
        {
            foreach (GameObject dropdown in tacticsScreenOpenDropdowns)
            {
                if (dropdown != null) dropdown.SetActive(false);
            }
        }

        // Scrollable option list scaffold (ScrollRect+Viewport+RectMask2D+Content, same
        // shape as the Tactics Board's own bench rail) rather than a plain unclipped
        // VerticalLayoutGroup - with up to 20 squad players plus "- None -" to choose
        // from, an unclipped list could easily run past the bottom of the screen for
        // whichever role row happens to sit lowest, the same class of overflow bug
        // fixed earlier this session on Matchday Prep's pitch. Deliberately empty - see
        // PopulateDropdownOptions, called only once this actually becomes active.
        private GameObject BuildEmptyDropdownScaffold(Transform parent, int optionCount)
        {
            const float optionHeight = 30f;
            const float maxVisibleHeight = 220f;

            GameObject dropdownPanel = new GameObject("DropdownOptions", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            dropdownPanel.transform.SetParent(parent, false);
            RectTransform dropdownPanelRect = dropdownPanel.GetComponent<RectTransform>();
            dropdownPanelRect.sizeDelta = new Vector2(0f, Mathf.Min(maxVisibleHeight, (optionCount + 1) * optionHeight));
            dropdownPanel.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(dropdownPanel.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, optionHeight);

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = dropdownPanel.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            dropdownPanel.SetActive(false);
            return dropdownPanel;
        }

        // Called only once the dropdown panel is actually active (see
        // BuildRoleDropdownRow's click handler) - building these TMP-labeled buttons
        // while active avoids the inactive-hierarchy mesh generation bug entirely.
        // Clears any previously-populated options first, since a dropdown can be opened
        // more than once across a single Tactics screen visit.
        private static void PopulateDropdownOptions(Transform content, List<PlayerAgent> options, Action<PlayerAgent> onSelect, Func<PlayerAgent, string[]> statColumns)
        {
            const float optionHeight = 30f;

            foreach (Transform child in content)
            {
                Destroy(child.gameObject);
            }

            Button noneOption = ManagerUITheme.BuildButton(content, "— None —", ManagerUITheme.CardNeutral, ManagerUITheme.TextDim, 12);
            noneOption.gameObject.AddComponent<LayoutElement>().preferredHeight = optionHeight;
            noneOption.onClick.AddListener(() => onSelect(null));

            foreach (PlayerAgent option in options)
            {
                BuildOptionRow(content, option.Name, statColumns(option), optionHeight, () => onSelect(option));
            }
        }

        // A real grid row (name cell + up to 3 fixed-width stat cells, same column-
        // fraction technique SquadListView.BuildPlayerGridRow already uses) rather than
        // one concatenated label - Thomas's point: with a single label, the stat values
        // start at a different X per row depending on how long each player's name is, so
        // they never actually line up into columns.
        private static readonly float[] OptionRowColumnFractions = { 0.46f, 0.18f, 0.18f, 0.18f };

        private static void BuildOptionRow(Transform parent, string name, string[] statColumns, float rowHeight, Action onClick)
        {
            GameObject row = new GameObject($"Option_{name}", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = rowHeight;

            Image background = row.GetComponent<Image>();
            background.color = ManagerUITheme.CardNeutral;

            Button button = row.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onClick());

            float x = 0f;
            BuildOptionCell(row.transform, x, OptionRowColumnFractions[0], name, ManagerUITheme.TextBody, FontStyles.Normal);
            x += OptionRowColumnFractions[0];

            for (int i = 0; i < 3; i++)
            {
                string cellText = statColumns != null && i < statColumns.Length ? statColumns[i] : string.Empty;
                BuildOptionCell(row.transform, x, OptionRowColumnFractions[i + 1], cellText, ManagerUITheme.TextMuted, FontStyles.Normal);
                x += OptionRowColumnFractions[i + 1];
            }
        }

        private static void BuildOptionCell(Transform parent, float x, float widthFraction, string text, Color color, FontStyles style)
        {
            GameObject cell = new GameObject("Cell", typeof(RectTransform));
            cell.transform.SetParent(parent, false);

            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(x, 0f);
            cellRect.anchorMax = new Vector2(x + widthFraction, 1f);
            cellRect.offsetMin = new Vector2(8f, 0f);
            cellRect.offsetMax = new Vector2(-4f, 0f);

            ManagerUITheme.BuildLabel(cell.transform, text, 12, color, TextAlignmentOptions.MidlineLeft, style);
        }

        // Greedy best-fit reassignment: for each slot in the new formation (in order),
        // pick the best remaining player from the full squad by
        // GetOverallRating() * GetPositionFit(slot) - a CB played at CB scores at full
        // rating, the same CB pressed into an ST slot scores at 60% of it. Applies
        // instantly, same immediacy as the drag-substitute mechanic on this same screen.
        private void OnFormationSelected(Formation formation)
        {
            CloseTacticsBoardFormationDropdown();

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (team.Formation == formation)
            {
                return;
            }

            List<PlayerPosition> newSlots = squadGenerator.GetStartingPositions(formation);
            List<PlayerAgent> pool = tacticsBoardOpenedMidMatch
                ? team.StartingEleven.Concat(team.Bench).ToList()
                : new List<PlayerAgent>(team.Players);
            List<PlayerAgent> newStartingEleven = new List<PlayerAgent>();

            foreach (PlayerPosition slot in newSlots)
            {
                PlayerAgent best = null;
                float bestScore = float.MinValue;

                foreach (PlayerAgent candidate in pool)
                {
                    float score = candidate.GetOverallRating() * candidate.GetPositionFit(slot);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    break;
                }

                newStartingEleven.Add(best);
                pool.Remove(best);
            }

            team.ChangeFormation(formation, newStartingEleven);
            RefreshTacticsBoardUI();
        }

        private void RefreshTacticsBoardUI()
        {
            if (tacticsBoardPitchContainer == null || tacticsBoardBenchContent == null)
            {
                return;
            }

            CloseTacticsBoardFormationDropdown();

            // Playtest backlog (session 14, Thomas: "Auto-Pick shouldn't be offered
            // inside the mid-match Make Changes squad screen") - the button itself is
            // shared chrome (built once, this same panel is reused for both the pre-
            // match Tactics Board and the mid-match Make Changes flow), so it's toggled
            // here on every refresh rather than at build time.
            if (autoPickButton != null) autoPickButton.gameObject.SetActive(!tacticsBoardOpenedMidMatch);

            if (nextMatchOnlyButton != null)
            {
                nextMatchOnlyButton.gameObject.SetActive(!tacticsBoardOpenedMidMatch);
                ManagerUITheme.NormalizeButtonLabel(nextMatchOnlyButton, nextMatchOnlyOverrideActive ? "NEXT MATCH ONLY: ON" : "NEXT MATCH ONLY", ManagerUITheme.TextBody, 13);
                HighlightSelectedMentalityButton(nextMatchOnlyButton, nextMatchOnlyOverrideActive);
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            // Only clear pins, not the pitch markings built once in BuildPitchMarkings -
            // those are siblings in the same container and would otherwise get destroyed
            // right along with the pins on the very first refresh (confirmed live: the
            // pitch had zero marking children left after OnViewSquadClicked's own first
            // RefreshTacticsBoardUI call).
            for (int i = tacticsBoardPitchContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = tacticsBoardPitchContainer.GetChild(i);

                if (child.name.StartsWith("Pin_"))
                {
                    Destroy(child.gameObject);
                }
            }

            foreach (Transform child in tacticsBoardBenchContent)
            {
                Destroy(child.gameObject);
            }

            if (tacticsBoardFormationButton != null)
            {
                // "v" not the mockup's ▾ glyph - Oswald has no symbol glyphs at all (same
                // reason "·" replaced the design's bullet/star/emoji elsewhere).
                ManagerUITheme.NormalizeButtonLabel(tacticsBoardFormationButton, $"Formation: {TacticsBoardLayout.FormatFormation(team.Formation)} v", ManagerUITheme.TextPrimary, 14);
            }

            IReadOnlyList<Vector2> pins = TacticsBoardLayout.GetPins(team.Formation);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            for (int i = 0; i < team.StartingEleven.Count && i < pins.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slotPosition = i < slots.Count ? slots[i] : player.PrimaryPosition;
                BuildTacticsBoardPin(player, slotPosition, pins[i]);
            }

            // Playtest backlog (session 14) - Thomas: separate genuine reserves from an
            // "unavailable" (injured) group, so an injured player reads as clearly
            // off-limits rather than just blocked-on-drag (see OnBenchPlayerDroppedOnPin's
            // injury check, which already refuses the drop - this makes the refusal
            // visible before the manager even tries). Available players keep the plain
            // bench list exactly as before; injured players are pushed below a caption
            // divider, still built by the same BuildTacticsBoardBenchCard (own injury
            // cross badge included) so they're still visible and still draggable-away-
            // from (a manager might want to shuffle who's next in line), just not mixed
            // in with who's actually pickable.
            ManagerSquadRoles benchRoles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerAgent> availableBench = new List<PlayerAgent>();
            List<PlayerAgent> unavailableBench = new List<PlayerAgent>();
            foreach (PlayerAgent player in team.Bench)
            {
                if (benchRoles.IsInjured(player, careerCalendar.CurrentDayNumber)) unavailableBench.Add(player);
                else availableBench.Add(player);
            }

            foreach (PlayerAgent player in availableBench)
            {
                BuildTacticsBoardBenchCard(player);
            }

            if (unavailableBench.Count > 0)
            {
                BuildTacticsBoardBenchSectionCaption($"UNAVAILABLE ({unavailableBench.Count})");
                foreach (PlayerAgent player in unavailableBench)
                {
                    BuildTacticsBoardBenchCard(player);
                }
            }

            // Pins/bench cards are destroyed and rebuilt fresh every time this runs
            // (every formation change, every substitution, every time the board opens) -
            // exactly the kind of rapid destroy/recreate churn that triggers the TMP
            // mesh-generation flakiness documented on Player Inspect's own equivalent
            // sweep.
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardPitchContainer));
            StartCoroutine(RecoverBlankLabelsNextFrame(tacticsBoardBenchContent));
        }

        private void BuildTacticsBoardPin(PlayerAgent player, PlayerPosition slotPosition, Vector2 pinPercent)
        {
            // No more vertical-compression fudge - that existed only to squeeze the
            // mockup's pin percentages into the old 960x540 canvas's much shorter pitch
            // region (see TacticsBoardLayout's own header comment). The 1920x1080 pitch
            // is close enough to the source design's own proportions that the raw
            // percentages should already read cleanly; re-verify live per formation
            // (including the un-mocked 4-3-3) and reintroduce a compression factor here
            // only if a specific formation still shows real overlap.
            Vector2 anchor = new Vector2(pinPercent.x, 1f - pinPercent.y);

            // Three tiers now, matching PlayerAgent.GetPositionFit: 1.00 primary or 0.85
            // listed secondary both read as comfortable (plain slot label, no color) -
            // 0.80 "adjacent but never rolled as an actual secondary" (e.g. an LW never
            // got LM) reads as a lenient orange warning - anything below that is a
            // genuinely foreign position, flagged red. Both warning tiers show the
            // player's own true PrimaryPosition rather than the slot's position label -
            // showing "DM" in red for a misplaced ST just relabels the empty slot, not
            // where the manager actually needs to move him; showing "ST" makes that
            // unambiguous. This is purely a visual flag - see ManagerFormationFit for
            // the actual gameplay consequence (which reads the same GetPositionFit
            // value, so the color tier and the real penalty always agree).
            float positionFit = player.GetPositionFit(slotPosition);
            string slotLabel;

            if (positionFit >= 0.85f)
            {
                slotLabel = slotPosition.ToString();
            }
            else if (positionFit >= 0.80f)
            {
                slotLabel = $"<color=#{ColorUtility.ToHtmlStringRGB(ManagerUITheme.Warning)}>{player.PrimaryPosition}</color>";
            }
            else
            {
                slotLabel = $"<color=#{ColorUtility.ToHtmlStringRGB(ManagerUITheme.Danger)}>{player.PrimaryPosition}</color>";
            }

            // Live condition, not just a static Stamina number on Player Detail - reads
            // the exact same GetFatigueMultiplier the sim itself plays the match against
            // (made public in the ManagerSim fork for this). Tints the pin's border
            // (previously always a flat Accent green, purely decorative) instead of
            // adding new pin real estate, which the position-mismatch text already uses
            // for its own separate signal.
            //
            // Gated on isMatchCurrentlyLive rather than assuming currentMatchMinute is 0
            // whenever no match is live - it isn't; ReplayMatchCoroutine only resets it
            // at kickoff, so it's left sitting at ~90 between full-time and the next
            // match's kickoff. Without this gate, players read as still gassed from the
            // *previous* match on the Tactics Board right up until the next one starts
            // (confirmed live - reported as "not sure if this is by design", it wasn't).
            // Playtest backlog (session 14) - Thomas's own idea: "the pin's green border
            // smoothly shifts warmer (green->yellow->red) as Condition drops, not a
            // separate number." Previously this only reflected live in-match fatigue
            // (GetFatigueMultiplier) and was hardcoded flat green outside a live match -
            // the season-long Condition system (ManagerSquadRoles, session 7/13
            // rebalance) had no visual presence on the board at all pre-match. Now blends
            // both signals: the persistent Condition is always the base (so a fatigued
            // squad reads as such before a ball's even kicked), and during a live match
            // the worse of the two (persistent vs in-match fatigue) wins, so the border
            // still visibly warms up as a match wears on, same as before.
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            float seasonCondition = roles.GetCondition(player);
            float condition = isMatchCurrentlyLive
                ? Mathf.Min(seasonCondition, matchSimulator.GetFatigueMultiplier(player, currentMatchMinute) * 100f)
                : seasonCondition;
            Color conditionColor = ManagerUITheme.ConditionGradientColor(condition);

            // Injury cross (session 9) - the Tactics screen previously had zero injury
            // awareness at all (see feedback in HANDOFF), so a manager could plan a
            // lineup around a player who's silently benched at kickoff. Doesn't block
            // selection yet, just makes it visible where the lineup is actually built.
            bool isInjured = roles.IsInjured(player, careerCalendar.CurrentDayNumber);

            GameObject pinObj = ManagerUITheme.BuildPitchPinVisual(
                tacticsBoardPitchContainer,
                $"Pin_{player.Name}",
                anchor,
                circleSize: 68f,
                borderColor: conditionColor,
                ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                ratingFontSize: 18,
                labelText: $"{player.Name} · {slotLabel}",
                labelFontSize: 14,
                showInjuryIcon: isInjured);

            pinObj.GetComponent<Image>().raycastTarget = true;

            TacticsBoardPlayerCard card = pinObj.AddComponent<TacticsBoardPlayerCard>();
            // isDraggable: true now (was false) - lets a pin be dragged onto another
            // pin to swap their positions, not just a bench card dragged onto a pin.
            card.Configure(player, isDraggable: true, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin, OnPinPlayersSwapped, isPinCard: true);
        }

        // Playtest backlog (session 14) - divider between the available bench and the
        // unavailable (injured) group below it. Same plain LayoutElement-label approach
        // every other inline scroll-list caption in this file uses (e.g. Inbox's empty-
        // state row) - not a full card, just a fixed-height text row the VerticalLayoutGroup
        // slots in like any other child.
        private void BuildTacticsBoardBenchSectionCaption(string text)
        {
            GameObject captionObj = new GameObject("BenchSectionCaption", typeof(RectTransform), typeof(LayoutElement));
            captionObj.transform.SetParent(tacticsBoardBenchContent, false);
            captionObj.GetComponent<LayoutElement>().preferredHeight = 24f;
            ManagerUITheme.BuildLabel(captionObj.transform, text, 13, ManagerUITheme.Danger, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        }

        private void BuildTacticsBoardBenchCard(PlayerAgent player)
        {
            GameObject cardObj = new GameObject($"Bench_{player.Name}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cardObj.transform.SetParent(tacticsBoardBenchContent, false);

            LayoutElement layoutElement = cardObj.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 66f;

            cardObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject nameObj = new GameObject("Name", typeof(RectTransform));
            nameObj.transform.SetParent(cardObj.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            // Extra left inset (was 18f) makes room for the injury cross gutter below,
            // same fixed-gutter approach SquadListView.BuildInjuryCrossIcon's own caller
            // already uses - reserved whether or not this particular card is injured, so
            // the name doesn't visibly shift card-to-card.
            nameRect.offsetMin = new Vector2(40f, 0f);
            nameRect.offsetMax = new Vector2(-18f, -2f);
            ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 17, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Injury cross badge (playtest backlog, session 14) - Thomas: prevents
            // accidentally dragging an injured player onto the pitch. The pin and Squad
            // list already show this (session 9); the Tactics Board's own bench card was
            // the one place left without it, which mattered most for exactly the drag
            // gesture this icon is meant to warn against.
            bool benchCardIsInjured = GetOrCreateSquadRoles(managedTeamName).IsInjured(player, careerCalendar.CurrentDayNumber);
            GameObject benchInjuryIcon = ManagerUITheme.BuildInjuryCrossIcon(cardObj.transform, 16f);
            RectTransform benchInjuryIconRect = benchInjuryIcon.GetComponent<RectTransform>();
            benchInjuryIconRect.anchorMin = new Vector2(0f, 0.5f);
            benchInjuryIconRect.anchorMax = new Vector2(0f, 0.5f);
            benchInjuryIconRect.pivot = new Vector2(0f, 0.5f);
            benchInjuryIconRect.anchoredPosition = new Vector2(14f, 0f);
            benchInjuryIcon.SetActive(benchCardIsInjured);

            GameObject ovrObj = new GameObject("OVR", typeof(RectTransform));
            ovrObj.transform.SetParent(cardObj.transform, false);
            RectTransform ovrRect = ovrObj.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(0f, 0.5f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.offsetMin = new Vector2(18f, 0f);
            ovrRect.offsetMax = new Vector2(-18f, -2f);
            ManagerUITheme.BuildLabel(ovrObj.transform, GetDisplayRating(player.GetOverallRating()).ToString(), 17, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject posObj = new GameObject("Position", typeof(RectTransform));
            posObj.transform.SetParent(cardObj.transform, false);
            RectTransform posRect = posObj.GetComponent<RectTransform>();
            posRect.anchorMin = new Vector2(0f, 0f);
            posRect.anchorMax = new Vector2(1f, 0.5f);
            posRect.offsetMin = new Vector2(18f, 2f);
            posRect.offsetMax = new Vector2(-18f, 0f);
            ManagerUITheme.BuildLabel(posObj.transform, player.PrimaryPosition.ToString(), 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            TacticsBoardPlayerCard card = cardObj.AddComponent<TacticsBoardPlayerCard>();
            // isDropTarget: true now (was false), and OnBenchPlayerDroppedOnPin wired
            // (was null) - playtest backlog (session 14): dragging a starter pin onto a
            // bench card now substitutes them, same as the existing bench-onto-pin
            // direction (see TacticsBoardPlayerCard.OnDrop's isPinCard branch).
            card.Configure(player, isDraggable: true, isDropTarget: true, OnTacticsBoardPlayerTapped, OnBenchPlayerDroppedOnPin, isPinCard: false);
        }

        private void OnTacticsBoardPlayerTapped(PlayerAgent player)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.TacticsBoard;
            OpenPlayerInspect(player);
        }

        private void OnBenchPlayerDroppedOnPin(PlayerAgent benchPlayer, PlayerAgent pinPlayer)
        {
            if (benchPlayer == pinPlayer)
            {
                return;
            }

            // Block, don't just decorate (session 10 - the injury cross icon from
            // session 9 only made an injured starter visible, it never stopped one being
            // dragged into the XI). Only checked here, not in OnPinPlayersSwapped - a
            // pin-to-pin swap never adds anyone to the starting XI who wasn't already in
            // it, so there's nothing new to block there.
            ManagerSquadRoles blockRoles = GetOrCreateSquadRoles(managedTeamName);
            if (blockRoles.IsInjured(benchPlayer, careerCalendar.CurrentDayNumber))
            {
                ShowTacticsBoardWarning($"{benchPlayer.Name} is injured and can't start");
                RefreshTacticsBoardUI();
                return;
            }

            // Session 10 exploit fix: once a player has genuinely been subbed off this
            // match, real football doesn't let them come back on. Only checked mid-match
            // (see playersSubbedOffThisMatch's own comment) - pre-match team-sheet edits
            // are free to rearrange the XI as many times as the manager likes.
            if (tacticsBoardOpenedMidMatch && playersSubbedOffThisMatch.Contains(benchPlayer))
            {
                ShowTacticsBoardWarning($"{benchPlayer.Name} has already been substituted and can't return");
                RefreshTacticsBoardUI();
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool applied = team.SubstitutePlayer(pinPlayer, benchPlayer);

            RefreshTacticsBoardUI();
        }

        // Per-match tactical override toggle (session 14) - see the field-level comment
        // near nextMatchOnlyButton for the full design. Arming snapshots the CURRENT
        // formation/XI as "the default to come back to" - any edits made afterward
        // (formation switch, drag substitutions, auto-pick) are then provisional for
        // just the next fixture. Clicking it again before that fixture plays cancels
        // the snapshot, keeping whatever's currently set as the permanent default -
        // same as if the toggle had never been touched.
        private void OnNextMatchOnlyToggleClicked()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (!nextMatchOnlyOverrideActive)
            {
                nextMatchOnlyOverrideActive = true;
                nextMatchOverrideDefaultFormation = team.Formation;
                nextMatchOverrideDefaultStartingEleven = new List<PlayerAgent>(team.StartingEleven);
                ShowTacticsBoardWarning("Armed - changes from here apply to the next match only, then your usual XI returns.");
            }
            else
            {
                nextMatchOnlyOverrideActive = false;
                nextMatchOverrideDefaultStartingEleven = null;
                ShowTacticsBoardWarning("Cancelled - the current setup is your default again.");
            }

            RefreshTacticsBoardUI();
        }

        // Restores the snapshot taken in OnNextMatchOnlyToggleClicked, right after the
        // one fixture it was armed for has actually been resolved - called from both
        // places currentFixtureIndex advances (the Simulate Season loop and
        // OnFullTimeContinueClicked), same as ResolveMatchdayInboxTicks. Defensively
        // filters the snapshot down to players still on the squad (a departed player
        // between arming and revert is a real, if unlikely, possibility - a transfer or
        // retirement landing in that exact one-fixture window) and bails without
        // touching anything if that leaves the XI short, rather than risk restoring a
        // corrupt lineup.
        private void ResolveNextMatchOnlyOverride()
        {
            if (!nextMatchOnlyOverrideActive)
            {
                return;
            }

            nextMatchOnlyOverrideActive = false;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            List<PlayerAgent> restoredEleven = nextMatchOverrideDefaultStartingEleven?.FindAll(p => team.Players.Contains(p));
            nextMatchOverrideDefaultStartingEleven = null;

            if (restoredEleven == null || restoredEleven.Count != squadGenerator.GetStartingPositions(nextMatchOverrideDefaultFormation).Count)
            {
                Debug.LogWarning("ManagerPrototypeController: skipped next-match-only revert - the snapshotted XI no longer matches the current squad.");
                return;
            }

            team.ChangeFormation(nextMatchOverrideDefaultFormation, restoredEleven);
        }

        // Auto-pick best XI (backlog item, session 12). Greedy slot-by-slot assignment
        // (not a true combinatorial optimum, but a strong practical XI - this is a
        // convenience feature, not core simulation logic): for each formation slot in
        // order, picks whichever eligible remaining candidate has the best
        // PlayerAgent.GetPositionFit for that specific slot. Reuses AgentTeam.
        // ChangeFormation with the SAME formation the team already has, purely for its
        // existing "assign this StartingEleven, everyone else falls to Bench" behavior -
        // no formation change happens here.
        private void OnAutoPickBestXIClicked()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            // Same two exclusions OnBenchPlayerDroppedOnPin already enforces one player
            // at a time - applied here up front so auto-pick can never do in one click
            // what a manual drag isn't allowed to do at all.
            List<PlayerAgent> pool = new List<PlayerAgent>(team.Players);
            pool.RemoveAll(p => roles.IsInjured(p, careerCalendar.CurrentDayNumber)
                || (tacticsBoardOpenedMidMatch && playersSubbedOffThisMatch.Contains(p)));

            List<PlayerAgent> bestXI = new List<PlayerAgent>();
            foreach (PlayerPosition slot in slots)
            {
                PlayerAgent best = null;
                float bestScore = float.MinValue;

                foreach (PlayerAgent candidate in pool)
                {
                    if (bestXI.Contains(candidate))
                    {
                        continue;
                    }

                    // GetPositionFit alone doesn't hard-block a keeper from an outfield
                    // slot or vice versa (it has no real notion of goalkeeping at all -
                    // "GK deliberately has no entry" in its own AdjacentPositions table),
                    // so that exact mismatch is guarded explicitly here instead.
                    bool candidateIsGK = candidate.PrimaryPosition == PlayerPosition.GK;
                    bool slotIsGK = slot == PlayerPosition.GK;
                    if (candidateIsGK != slotIsGK)
                    {
                        continue;
                    }

                    // Fit alone isn't enough - two primary-position candidates for the
                    // same slot both score a flat 1.00 fit, so comparing fit only picked
                    // whoever happened to be first in `pool` (a weaker starter) over a
                    // clearly better bench player at the same fit tier (real bug Thomas
                    // caught live: an 87-rated bench CB lost a tie to an 84-rated starting
                    // CB, so Auto-Pick visibly did nothing). Fit's four tiers (0.60/0.80/
                    // 0.85/1.00) are categorical steps far apart from each other, so
                    // multiplying it by 1000 and adding Overall (0-99) keeps fit tier
                    // strictly dominant while letting Overall break ties within a tier.
                    float fit = candidate.GetPositionFit(slot);
                    float conditionAdjustedOverall = candidate.GetOverallRating() * roles.GetConditionMultiplier(candidate);
                    float score = fit * 1000f + conditionAdjustedOverall;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null)
                {
                    bestXI.Add(best);
                }
            }

            // Fallback for a genuinely short-handed squad (mass injuries etc.) - fill any
            // remaining slot with whoever's left rather than leave a pin empty. Real
            // football-manager UX: a weak/mismatched XI is still better than no XI.
            if (bestXI.Count < slots.Count)
            {
                foreach (PlayerAgent candidate in pool)
                {
                    if (bestXI.Count >= slots.Count)
                    {
                        break;
                    }

                    if (!bestXI.Contains(candidate))
                    {
                        bestXI.Add(candidate);
                    }
                }
            }

            if (bestXI.Count < slots.Count)
            {
                ShowTacticsBoardWarning("Not enough available players to fill the XI");
                RefreshTacticsBoardUI();
                return;
            }

            team.ChangeFormation(team.Formation, bestXI);
            RefreshTacticsBoardUI();
        }

        // A pin dragged onto another pin - e.g. after a formation change scatters the
        // ST onto the LM spot and vice versa, dragging the ST back onto the ST pin.
        // Both players stay in the starting XI (unlike OnBenchPlayerDroppedOnPin, this
        // never touches the Bench), so it's not logged as a "Subs Made" entry - no sub
        // was used, nobody came off. Still resimulates the rest of a live match though,
        // same as a real substitution would - position genuinely affects the sim now
        // (see ManagerFormationFit), so repositioning players mid-match should too.
        private void OnPinPlayersSwapped(PlayerAgent draggedPlayer, PlayerAgent targetPlayer)
        {
            if (draggedPlayer == targetPlayer)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool applied = team.SwapStartingPositions(draggedPlayer, targetPlayer);

            RefreshTacticsBoardUI();
        }

        private void ShowTacticsBoardWarning(string message)
        {
            if (tacticsBoardWarningLabel == null)
            {
                return;
            }

            // Built early in BuildTacticsBoardChrome, before the pitch/bench elements -
            // those render on top of it as later siblings otherwise (same z-order gotcha
            // as the dropdown popups). Bring the warning's container to front each time
            // it's actually shown rather than reordering it once at build time, since a
            // full board rebuild (RefreshTacticsBoardUI) doesn't touch this object at all.
            tacticsBoardWarningLabel.transform.parent.SetAsLastSibling();

            tacticsBoardWarningLabel.text = message;

            if (tacticsBoardWarningCoroutine != null)
            {
                StopCoroutine(tacticsBoardWarningCoroutine);
            }

            tacticsBoardWarningCoroutine = StartCoroutine(ClearTacticsBoardWarningAfterDelay());
        }

        private IEnumerator ClearTacticsBoardWarningAfterDelay()
        {
            // Realtime, not scaled - OnOpenTacticsBoardDuringMatchClicked pauses the game
            // (Time.timeScale = 0) for the entire time this board is open, so a WaitForSeconds
            // here would never progress while the manager is actually looking at the warning,
            // then barely progress in the few real seconds after resuming before the board
            // pauses again on next open - the warning would look permanently stuck.
            yield return new WaitForSecondsRealtime(3f);

            if (tacticsBoardWarningLabel != null)
            {
                tacticsBoardWarningLabel.text = "";
            }

            tacticsBoardWarningCoroutine = null;
        }

        // Regenerates the remainder of the currently-live match (from the minute after
        // the change was made) against the current prediction, so a mid-match sub or
        // mentality change (see ApplyLiveMentalityChangeIfMatchInProgress) actually
        // affects the rest of that match's events/result instead of only taking effect
        // from the *next* match onward. lastSimulatedResult is the same object
        // reference ReplayMatchCoroutine holds as its own "result" parameter, so
        // mutating it here is visible to that coroutine as soon as it resumes (it's
        // sitting frozen at Time.timeScale=0 while the Tactics Board is open, not
        // actively reading events right now).
        private void TriggerMidMatchResimulation()
        {
            if (lastSimulatedResult == null)
            {
                return;
            }

            OpenFootballMatch fixture = hasActiveMatchFixture ? activeMatchFixture : currentFixture;
            AgentTeam homeTeamAgent = GetOrCreateAgentTeam(fixture.HomeTeam);
            AgentTeam awayTeamAgent = GetOrCreateAgentTeam(fixture.AwayTeam);

            Func<PlayerAgent, float> homeConditionLookup = fixture.HomeTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p)) : null;
            Func<PlayerAgent, float> awayConditionLookup = fixture.AwayTeam == managedTeamName
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p)) : null;
            AgentTeam adjustedHome = ManagerFormationFit.BuildFitAdjustedTeam(homeTeamAgent,
                squadGenerator.GetStartingPositions(homeTeamAgent.Formation), homeConditionLookup);
            AgentTeam adjustedAway = ManagerFormationFit.BuildFitAdjustedTeam(awayTeamAgent,
                squadGenerator.GetStartingPositions(awayTeamAgent.Formation), awayConditionLookup);
            ManagerPlayerDerivedStrength.MatchupPrediction livePrediction = ManagerPlayerDerivedStrength.PredictMatchup(
                ManagerPlayerDerivedStrength.Calculate(adjustedHome, squadGenerator.GetStartingPositions(adjustedHome.Formation)),
                ManagerPlayerDerivedStrength.Calculate(adjustedAway, squadGenerator.GetStartingPositions(adjustedAway.Formation)));

            lastRawExpectedHomeGoals = livePrediction.ExpectedHomeGoals;
            lastRawExpectedAwayGoals = livePrediction.ExpectedAwayGoals;
            float liveExpectedHomeGoals = livePrediction.ExpectedHomeGoals;
            float liveExpectedAwayGoals = livePrediction.ExpectedAwayGoals;
            if (fixture.HomeTeam == managedTeamName)
                ManagerMentalityModifier.Apply(selectedMentality, ref liveExpectedHomeGoals, ref liveExpectedAwayGoals);
            else if (fixture.AwayTeam == managedTeamName)
                ManagerMentalityModifier.Apply(selectedMentality, ref liveExpectedAwayGoals, ref liveExpectedHomeGoals);
            lastExpectedHomeGoals = liveExpectedHomeGoals;
            lastExpectedAwayGoals = liveExpectedAwayGoals;

            matchSimulator.TacticalShapeMatchup = ManagerTacticalShape.BuildMatchup(
                homeTeamAgent.TeamName, homeTeamAgent.Formation, ResolveFixtureTactics(homeTeamAgent, awayTeamAgent, true),
                awayTeamAgent.TeamName, awayTeamAgent.Formation, ResolveFixtureTactics(awayTeamAgent, homeTeamAgent, false),
                homeTeamAgent, homeTeamAgent.TeamName == managedTeamName ? GetOrCreateSquadRoles(managedTeamName) : null,
                awayTeamAgent, awayTeamAgent.TeamName == managedTeamName ? GetOrCreateSquadRoles(managedTeamName) : null);

            AgentMatchSimulator.AgentMatchResult tail = matchSimulator.SimulateFromMinute(
                adjustedHome,
                adjustedAway,
                lastExpectedHomeGoals,
                lastExpectedAwayGoals,
                currentMatchMinute + 1,
                liveHomeGoalsSoFar,
                liveAwayGoalsSoFar);

            lastSimulatedResult.Events.RemoveAll(e => e.Minute > currentMatchMinute);
            lastSimulatedResult.Events.AddRange(tail.Events);
            lastSimulatedResult.HomeGoals = tail.HomeGoals;
            lastSimulatedResult.AwayGoals = tail.AwayGoals;
        }

    }
}
