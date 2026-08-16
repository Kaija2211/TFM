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
        // --- Scouting (career-arc addition, session 8, Phase 2): browse every club's
        // hidden youth-prospect pool, assign a scout to reveal a specific player's real
        // Potential (fuzzy range until then). Same code-built-panel/scroll-view pattern
        // as the Squad screen (BuildSquadChrome), reusing SquadListView's flat
        // Populate rather than the grid variant since the label here is a custom
        // composite (name/position/age/club/potential/status) rather than fixed columns. ---

        private bool scoutingChromeBuilt;
        private GameObject scoutingPanel;
        private SquadListView scoutingListView;
        // GameObject, not TextMeshProUGUI - see matchdayPrepTitleLabel's comment. This
        // label starts with text="" at build time (populated later by
        // RefreshScoutingUI), which is exactly the shape that trips the blank-label
        // recovery sweep into destroying/recreating it - a cached TextMeshProUGUI
        // reference would silently start writing to the dead original. Confirmed live
        // (Thomas: byline stuck at "0/2" while the per-row status text updated fine).
        private GameObject scoutingBylineObj;

        // Sortable columns (session 9 - Thomas: "click OVR to sort high to low").
        // -1 = no explicit sort (original generation order). First click on any column
        // defaults to descending (matches "high to low" as the expected first click for
        // a numeric column); clicking the same column again toggles direction.
        private int scoutingSortColumn = -1;
        private bool scoutingSortDescending = true;

        // Academy sortable columns (session 15, Thomas: "like with our other lists, id
        // like to be able to sort our academy players") - separate state from
        // scoutingSortColumn/scoutingSortDescending above since the two grids don't
        // share a column layout (Academy has no NATION/EXPIRES columns). Originally
        // built without sorting at all ("short, fixed-order list of slots - sorting
        // adds little," see RefreshAcademyUI's own older comment) - Thomas asked for it
        // anyway, so it's wired the same way every other sortable grid in this file is.
        private int academySortColumn = -1;
        private bool academySortDescending = true;

        // Youth academy tab (session 9) - shares this screen/list with World Scouting.
        private Button scoutingAcademyTabButton;
        private Button scoutingWorldTabButton;
        private bool scoutingShowingAcademyTab;
        private ScrollRect scoutingScrollRect;
        private float scoutingInspectReturnScroll = 1f;

        public void OnOpenScoutingClicked()
        {
            ShowScouting(resetToWorldTab: true);
        }

        private void ShowScouting(bool resetToWorldTab)
        {
            CloseAcademyIntakeDropdown();

            if (!scoutingChromeBuilt)
            {
                BuildScoutingChrome();
                scoutingChromeBuilt = true;
            }

            if (resetToWorldTab) scoutingShowingAcademyTab = false;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (scoutingPanel != null) scoutingPanel.SetActive(true);

            RefreshScoutingUI();
        }

        private void OnScoutingWorldTabClicked()
        {
            CloseAcademyIntakeDropdown();

            scoutingShowingAcademyTab = false;
            RefreshScoutingUI();
        }

        private void OnScoutingAcademyTabClicked()
        {
            CloseAcademyIntakeDropdown();

            scoutingShowingAcademyTab = true;
            RefreshScoutingUI();
        }

        public void OnScoutingBackClicked()
        {
            CloseAcademyIntakeDropdown();

            if (scoutingPanel != null) scoutingPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Extra vertical room reserved above the scroll list, only on the Missions tab,
        // for the two scout-mission brief boxes (session 13 rework) - toggled per tab in
        // RefreshScoutingUI rather than built into two separate screens.
        private const float ScoutingMissionsAreaHeight = 210f;
        private GameObject scoutingMissionsContainer;
        private RectTransform scoutingScrollViewRect;
        private readonly List<GameObject> spawnedMissionBoxes = new();

        private void BuildScoutingChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            scoutingPanel = new GameObject("ScoutingPanel", typeof(RectTransform));
            scoutingPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = scoutingPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(scoutingPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(scoutingPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            // Renamed from "SCOUTING" (session 13) - Thomas: the page covers both the
            // youth missions and the Academy that develops what they find, "Youth"
            // covers the whole page better than "Scouting" ever did.
            ManagerUITheme.BuildLabel(titleObj.transform, "YOUTH", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            scoutingBylineObj = bylineObj;

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnScoutingBackClicked);

            // Youth academy (session 9) - same tab-toggle pattern as Transfer Market's
            // Buy/Sell (see BuildTransferMarketChrome), sharing this one screen/list
            // rather than building an entire second panel from scratch for what's
            // thematically the same "discover/develop young players" concern.
            scoutingAcademyTabButton = ManagerUITheme.BuildButton(header.transform, "ACADEMY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(scoutingAcademyTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            scoutingAcademyTabButton.onClick.AddListener(OnScoutingAcademyTabClicked);

            // Renamed from "WORLD SCOUTING" (session 13 mission rework).
            scoutingWorldTabButton = ManagerUITheme.BuildButton(header.transform, "SCOUTING MISSIONS", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(scoutingWorldTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-436f, -27f), new Vector2(200f, 36f));
            scoutingWorldTabButton.onClick.AddListener(OnScoutingWorldTabClicked);

            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            // Mission brief area (session 13) - built once here, shown/hidden and
            // repopulated per refresh (see RefreshScoutingUI/RefreshMissionsArea)
            // rather than living inside the scrollable grid content, so it can sit at a
            // fixed position independent of how many rows the list below has.
            scoutingMissionsContainer = new GameObject("MissionsArea", typeof(RectTransform));
            scoutingMissionsContainer.transform.SetParent(scoutingPanel.transform, false);
            ManagerUITheme.AnchorTopStretch(scoutingMissionsContainer, headerHeight + 10f, ScoutingMissionsAreaHeight, sideMargin);

            GameObject scrollViewObj = new GameObject("ScoutingScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(scoutingPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scoutingScrollViewRect = scrollViewRect;
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(sideMargin, 40f);
            scrollViewRect.offsetMax = new Vector2(-(sideMargin + 20f), -(headerHeight + 40f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(SquadListView));
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            scoutingListView = contentObj.GetComponent<SquadListView>();
            scoutingListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scoutingScrollRect = scrollRect;
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            GameObject scrollbarObj = new GameObject("ScoutingScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(scoutingPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-(sideMargin + 20f), 40f);
            scrollbarRect.offsetMax = new Vector2(-(sideMargin + 4f), -(headerHeight + 40f));
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
            handleRect.sizeDelta = Vector2.zero;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            StartCoroutine(RecoverBlankLabelsNextFrame(scoutingPanel.transform));
        }

        // Generates every club's youth pool the first time this screen is opened (cheap -
        // object allocation only, no RNG-safety concern this deep into live play) so the
        // list has full league breadth rather than only clubs already encountered via
        // fixtures. Rebuilt fresh every open, same destroy/recreate pattern as every
        // other dynamic list in this file.
        private void RefreshScoutingUI()
        {
            if (scoutingListView == null)
            {
                return;
            }

            if (scoutingWorldTabButton != null && scoutingWorldTabButton.TryGetComponent(out Image worldImage))
            {
                worldImage.color = !scoutingShowingAcademyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(scoutingWorldTabButton, "SCOUTING MISSIONS", !scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (scoutingAcademyTabButton != null && scoutingAcademyTabButton.TryGetComponent(out Image academyImage))
            {
                academyImage.color = scoutingShowingAcademyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(scoutingAcademyTabButton, "ACADEMY", scoutingShowingAcademyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            // Mission boxes (session 13) only make sense on the Missions tab - toggled
            // here, along with pushing the scroll list further down to make room while
            // they're visible (see BuildScoutingChrome's own comment on the fixed
            // ScoutingMissionsAreaHeight reservation).
            const float headerHeight = 90f;
            bool showMissions = !scoutingShowingAcademyTab;

            if (scoutingMissionsContainer != null) scoutingMissionsContainer.SetActive(showMissions);

            if (scoutingScrollViewRect != null)
            {
                float top = showMissions ? headerHeight + 10f + ScoutingMissionsAreaHeight + 30f : headerHeight + 40f;
                scoutingScrollViewRect.offsetMax = new Vector2(scoutingScrollViewRect.offsetMax.x, -top);
            }

            if (showMissions) RefreshMissionsArea();

            scoutingListView.Clear();

            if (scoutingShowingAcademyTab)
            {
                RefreshAcademyUI();
            }
            else
            {
                RefreshWorldScoutingUI();
            }
        }

        // --- Scout mission briefs (session 13) - two fixed slots, each up to 3 target
        // positions, reusing the same absolute-positioned chip-toggle technique the
        // Academy focus-stats picker already established (see BuildFocusStatsPicker). ---

        private void RefreshMissionsArea()
        {
            foreach (GameObject box in spawnedMissionBoxes)
            {
                if (box != null) Destroy(box);
            }
            spawnedMissionBoxes.Clear();

            if (scoutingMissionsContainer == null) return;

            const float boxWidth = 780f;
            const float gap = 40f;

            for (int slot = 0; slot < ManagerScouting.ScoutSlots; slot++)
            {
                GameObject box = BuildMissionBox(slot, slot * (boxWidth + gap), boxWidth);
                spawnedMissionBoxes.Add(box);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(scoutingMissionsContainer.transform));
        }

        private GameObject BuildMissionBox(int slotIndex, float x, float width)
        {
            GameObject box = new GameObject($"MissionBox_{slotIndex}", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(scoutingMissionsContainer.transform, false);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 1f);
            boxRect.anchorMax = new Vector2(0f, 1f);
            boxRect.pivot = new Vector2(0f, 1f);
            boxRect.anchoredPosition = new Vector2(x, 0f);
            boxRect.sizeDelta = new Vector2(width, ScoutingMissionsAreaHeight);
            box.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            IReadOnlyList<PlayerPosition> briefed = scouting.GetMissionPositions(slotIndex);
            bool active = scouting.IsMissionActive(slotIndex);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(box.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(width - 200f, 24f));
            ManagerUITheme.BuildLabel(titleObj.transform, $"SCOUT {slotIndex + 1}", 16, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            string statusText = active
                ? $"Searching for: {string.Join(", ", briefed)}"
                : "No brief set - pick up to 3 positions and send them out.";
            GameObject statusObj = new GameObject("Status", typeof(RectTransform));
            statusObj.transform.SetParent(box.transform, false);
            ManagerUITheme.SetPointAnchor(statusObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16f, -38f), new Vector2(width - 32f, 20f));
            ManagerUITheme.BuildLabel(statusObj.transform, statusText, 13, active ? ManagerUITheme.Accent : ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);

            // Position chip grid - selection is staged in missionBriefSelection until
            // SEND is clicked, so browsing positions doesn't reassign a live mission
            // brief on every click.
            if (!missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> staged))
            {
                staged = new List<PlayerPosition>(briefed);
                missionBriefSelection[slotIndex] = staged;
            }

            PlayerPosition[] allPositions = (PlayerPosition[])System.Enum.GetValues(typeof(PlayerPosition));
            const float chipWidth = 74f;
            const float chipHeight = 28f;
            const float chipGapX = 6f;
            const float chipGapY = 6f;
            const int chipsPerRow = 7;

            for (int i = 0; i < allPositions.Length; i++)
            {
                PlayerPosition position = allPositions[i];
                bool isSelected = staged.Contains(position);

                int row = i / chipsPerRow;
                int col = i % chipsPerRow;
                float chipX = 16f + col * (chipWidth + chipGapX);
                float chipY = -68f - row * (chipHeight + chipGapY);

                GameObject chip = new GameObject($"PosChip_{position}", typeof(RectTransform), typeof(Image), typeof(Button));
                chip.transform.SetParent(box.transform, false);
                RectTransform chipRect = chip.GetComponent<RectTransform>();
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(0f, 1f);
                chipRect.pivot = new Vector2(0f, 1f);
                chipRect.sizeDelta = new Vector2(chipWidth, chipHeight);
                chipRect.anchoredPosition = new Vector2(chipX, chipY);

                Image chipImage = chip.GetComponent<Image>();
                chipImage.color = isSelected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

                Button chipButton = chip.GetComponent<Button>();
                chipButton.targetGraphic = chipImage;
                chipButton.onClick.AddListener(() => OnMissionPositionToggled(slotIndex, position));

                ManagerUITheme.BuildLabel(chip.transform, position.ToString(), 12, isSelected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            // Session 15 fix: CANCEL spans x=16-156 (16 + its own 140 width) - SEND used
            // to start at x=91, well inside that span, so the two buttons visibly
            // overlapped (confirmed live, Thomas caught it on the Youth screen). SEND now
            // starts after CANCEL's right edge plus a 16px gap.
            Button cancelButton = ManagerUITheme.BuildButton(box.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16f, 20f), new Vector2(140f, 34f));
            int capturedSlot = slotIndex;
            cancelButton.onClick.AddListener(() => OnCancelMissionClicked(capturedSlot));

            Button sendButton = ManagerUITheme.BuildButton(box.transform, active ? "UPDATE BRIEF" : "SEND", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(sendButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16f + 140f + 16f, 20f), new Vector2(150f, 34f));
            sendButton.onClick.AddListener(() => OnSendMissionClicked(capturedSlot));

            return box;
        }

        // Staged position picks per slot, cleared to match the real brief whenever a
        // mission is actually sent/cancelled - see BuildMissionBox's own comment.
        private readonly Dictionary<int, List<PlayerPosition>> missionBriefSelection = new();

        private void OnMissionPositionToggled(int slotIndex, PlayerPosition position)
        {
            if (!missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> staged))
            {
                staged = new List<PlayerPosition>();
                missionBriefSelection[slotIndex] = staged;
            }

            if (staged.Contains(position))
            {
                staged.Remove(position);
            }
            else if (staged.Count < ManagerScouting.MaxTargetPositions)
            {
                staged.Add(position);
            }

            RefreshMissionsArea();
        }

        private void OnSendMissionClicked(int slotIndex)
        {
            List<PlayerPosition> staged = missionBriefSelection.TryGetValue(slotIndex, out List<PlayerPosition> s) ? s : new List<PlayerPosition>();
            scouting.SetMissionBrief(slotIndex, staged);
            RefreshMissionsArea();
        }

        private void OnCancelMissionClicked(int slotIndex)
        {
            scouting.CancelMission(slotIndex);
            missionBriefSelection[slotIndex] = new List<PlayerPosition>();
            RefreshMissionsArea();
        }

        // --- Discovered prospects list (session 13) - a discovery IS the scouting act,
        // so every row here already has full real stats (only Potential stays fuzzy,
        // same as Academy's own kids - see ManagerScouting.GetDisplayPotential). No more
        // per-row "assign a scout" action; clicking a row just opens full detail. ---

        private void RefreshWorldScoutingUI()
        {
            List<PlayerAgent> allProspects = new List<PlayerAgent>(scouting.DiscoveredProspects);

            if (scoutingSortColumn >= 0)
            {
                allProspects.Sort((a, b) => CompareScoutingColumn(a, b, scoutingSortColumn, scoutingSortDescending));
            }

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    bylineTMP.text = $"{allProspects.Count} discovered   ·   unclaimed for {ManagerScouting.DaysUntilPoached} days and they're poached   ·   bring them into an empty Academy slot to keep them";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(ScoutingColumnHeaders, ScoutingColumnFractions, OnScoutingColumnHeaderClicked, scoutingSortColumn, scoutingSortDescending);

            foreach (PlayerAgent prospect in allProspects)
            {
                string nation = ManagerPlayerNationality.GetNationality(prospect).Name;
                int left = scouting.GetDaysUntilPoached(prospect, careerCalendar.CurrentDayNumber);
                string expiresCell = left <= 2 ? $"<color=#e05a5a>{left}d left</color>" : $"{left}d left";

                string[] cells =
                {
                prospect.Name,
                prospect.PrimaryPosition.ToString(),
                prospect.Age.ToString(),
                nation,
                GetDisplayRating(prospect.GetOverallRating()).ToString(),
                scouting.GetDisplayPotential(prospect),
                expiresCell
            };

                scoutingListView.AddCustomGridRow(prospect, cells, ScoutingColumnFractions, p => OpenScoutedProspectDetail(p, allProspects),
                    onNameClicked: p => OpenScoutedProspectDetail(p, allProspects));
            }
        }

        private static readonly string[] ScoutingColumnHeaders = { "PROSPECT", "POS", "AGE", "NATION", "OVR", "POTENTIAL", "EXPIRES" };
        private static readonly float[] ScoutingColumnFractions = { 0.20f, 0.07f, 0.07f, 0.22f, 0.09f, 0.14f, 0.21f };

        // Youth academy (session 9) - "grew them myself," complementary to the Missions
        // tab's "found them abroad." No NATION column (they're your own kids, not a
        // scouted discovery). Empty slots (session 13) render their own row with a
        // "BRING IN SCOUTED PLAYER" action instead of a normal grid row - see
        // AddPrebuiltRow. Sortable headers added session 15 (Thomas asked, after this
        // comment originally argued sorting "adds little" for a short fixed-order list) -
        // empty slots have no PlayerAgent to sort by, so when a sort is active they're
        // grouped at the bottom below every real prospect rather than interleaved by
        // their original slot index; with no sort active (academySortColumn == -1) the
        // list still renders in plain slot order exactly as before, empty slots included
        // in place, unchanged from the original behavior.
        private void RefreshAcademyUI()
        {
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            academy.GetOrCreateAcademyPool(squadGenerator, strength.AttackStrength, strength.DefenceStrength);
            IReadOnlyList<PlayerAgent> slots = academy.GetFullAcademySlots();
            IReadOnlyList<int> emptySlotIndices = academy.GetEmptySlotIndices();

            if (scoutingBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = scoutingBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    int emptyCount = emptySlotIndices.Count;
                    bylineTMP.text = $"{ManagerAcademy.AcademySlots} academy slots ({emptyCount} empty)   ·   promotable to reserves at age {ManagerAcademy.PromotionAge}   ·   click a promotable prospect to promote";
                }
            }

            scoutingListView.AddCustomGridHeaderRow(
                AcademyColumnHeaders,
                AcademyColumnFractions,
                OnAcademyColumnHeaderClicked,
                academySortColumn,
                academySortDescending
            );

            List<PlayerAgent> filledOnly = new List<PlayerAgent>(academy.GetAcademyPoolForAging());

            if (academySortColumn >= 0)
            {
                List<PlayerAgent> sortedFilled = new List<PlayerAgent>(filledOnly);
                sortedFilled.Sort((a, b) => CompareAcademyColumn(a, b, academySortColumn, academySortDescending));

                foreach (PlayerAgent prospect in sortedFilled)
                {
                    BuildAcademyRow(prospect, filledOnly);
                }

                // Important: use the REAL empty slot indices.
                // The old version used 0..emptySlotCount, which can point at filled slots after sorting.
                foreach (int emptySlotIndex in emptySlotIndices)
                {
                    scoutingListView.AddPrebuiltRow(BuildEmptyAcademySlotRow(emptySlotIndex));
                }

                return;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                PlayerAgent prospect = slots[i];

                if (prospect == null)
                {
                    scoutingListView.AddPrebuiltRow(BuildEmptyAcademySlotRow(i));
                    continue;
                }

                BuildAcademyRow(prospect, filledOnly);
            }
        }

        private void BuildAcademyRow(PlayerAgent prospect, List<PlayerAgent> filledOnly)
        {
            bool promotable = academy.CanPromote(prospect);
            string status = promotable ? "<color=#3ddc84>PROMOTABLE</color>" : "DEVELOPING";

            string[] cells =
            {
            prospect.Name,
            prospect.PrimaryPosition.ToString(),
            prospect.Age.ToString(),
            GetDisplayRating(prospect.GetOverallRating()).ToString(),
            scouting.GetDisplayPotential(prospect),
            status
        };

            scoutingListView.AddCustomGridRow(prospect, cells, AcademyColumnFractions, OnAcademyProspectClicked,
                onNameClicked: p => OpenAcademyProspectDetail(p, filledOnly));
        }

        private void OnAcademyColumnHeaderClicked(int column)
        {
            if (academySortColumn == column)
            {
                academySortDescending = !academySortDescending;
            }
            else
            {
                academySortColumn = column;
                academySortDescending = true;
            }

            // RefreshAcademyUI alone never clears scoutingListView first (only
            // RefreshScoutingUI does, right before dispatching to either tab) - calling
            // it directly here appended a second, freshly-sorted row set on top of the
            // previous one on every sort click instead of replacing it. Matches
            // OnScoutingColumnHeaderClicked's own fix shape for the World Scouting tab.
            RefreshScoutingUI();
        }

        // Column indices match AcademyColumnHeaders. Potential sorts by the same fuzzy-
        // band display string a Squad/Transfers list already sorts by (see
        // GetScoutingPotentialSortKey) rather than the true hidden value. Status sorts
        // PROMOTABLE before DEVELOPING on a descending (default) click, matching every
        // other column's "most interesting first" convention.
        private int CompareAcademyColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 4:
                    result = GetScoutingPotentialSortKey(a).CompareTo(GetScoutingPotentialSortKey(b));
                    break;
                case 5:
                    result = academy.CanPromote(a).CompareTo(academy.CanPromote(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private static readonly string[] AcademyColumnHeaders = { "PROSPECT", "POS", "AGE", "OVR", "POTENTIAL", "STATUS" };
        private static readonly float[] AcademyColumnFractions = { 0.24f, 0.10f, 0.10f, 0.12f, 0.18f, 0.26f };

        // Session 13 - an empty slot is a real row (same rowHeight as a normal grid
        // row, via its own LayoutElement) with a single "BRING IN SCOUTED PLAYER"
        // action, rather than just vanishing from the list - the manager should be able
        // to see exactly how many open slots exist and fill them deliberately.
        private GameObject BuildEmptyAcademySlotRow(int slotIndex)
        {
            const float rowHeight = 40f;

            GameObject row = new GameObject($"EmptySlot_{slotIndex}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(scoutingListView.transform, false);
            row.GetComponent<LayoutElement>().preferredHeight = rowHeight;
            row.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelObj.transform, "EMPTY SLOT", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);

            Button bringInButton = ManagerUITheme.BuildButton(row.transform, "BRING IN SCOUTED PLAYER", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 12);
            ManagerUITheme.SetPointAnchor(bringInButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(240f, 30f));
            bringInButton.onClick.AddListener(() => OnBringInScoutedPlayerClicked(slotIndex));

            return row;
        }

        // Reuses the exact dropdown scaffold/option-row technique the Tactics screen's
        // role-assignment pickers already established (BuildEmptyDropdownScaffold/
        // PopulateDropdownOptions) - a scrollable "pick a player from a list" UI, just
        // sourced from ManagerScouting.DiscoveredProspects instead of the squad.
        private GameObject academyIntakeDropdown;

        private void OnBringInScoutedPlayerClicked(int slotIndex)
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
            }

            IReadOnlyList<PlayerAgent> academySlots = academy.GetFullAcademySlots();
            bool validSlot = slotIndex >= 0 && slotIndex < academySlots.Count;
            bool slotEmpty = validSlot && academySlots[slotIndex] == null;

            if (!validSlot || !slotEmpty)
            {
                Debug.LogWarning(
                    $"Academy intake blocked: slotIndex={slotIndex}, validSlot={validSlot}, slotEmpty={slotEmpty}."
                );
                return;
            }

            List<PlayerAgent> options = new List<PlayerAgent>(scouting.DiscoveredProspects);

            academyIntakeDropdown = BuildEmptyDropdownScaffold(scoutingPanel.transform, options.Count);

            RectTransform dropdownRect = academyIntakeDropdown.GetComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(0.5f, 0.5f);
            dropdownRect.anchorMax = new Vector2(0.5f, 0.5f);
            dropdownRect.pivot = new Vector2(0.5f, 0.5f);
            dropdownRect.anchoredPosition = Vector2.zero;
            dropdownRect.sizeDelta = new Vector2(600f, dropdownRect.sizeDelta.y);
            academyIntakeDropdown.transform.SetAsLastSibling();

            // Defensive: if the shared dropdown scaffold starts inactive, this makes the picker visible.
            academyIntakeDropdown.SetActive(true);

            Transform content = academyIntakeDropdown.transform.Find("Viewport/Content");

            if (content == null)
            {
                Debug.LogWarning("Academy intake dropdown failed: could not find Viewport/Content.");
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
                return;
            }

            PopulateDropdownOptions(
                content,
                options,
                prospect => OnScoutedPlayerChosenForSlot(slotIndex, prospect),
                p => new[]
                {
        p.PrimaryPosition.ToString(),
        p.Age.ToString(),
        GetDisplayRating(p.GetOverallRating()).ToString()
                }
            );

            StartCoroutine(RecoverBlankLabelsNextFrame(academyIntakeDropdown.transform));
        }

        private void OnScoutedPlayerChosenForSlot(int slotIndex, PlayerAgent prospect)
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
            }

            // prospect is null when "— None —" was picked.
            if (prospect == null)
            {
                return;
            }

            IReadOnlyList<PlayerAgent> academySlots = academy.GetFullAcademySlots();
            bool validSlot = slotIndex >= 0 && slotIndex < academySlots.Count;
            bool slotEmpty = validSlot && academySlots[slotIndex] == null;
            bool wasDiscovered = scouting.DiscoveredProspects.Contains(prospect);

            bool placed = scouting.TryClaimProspectToAcademy(prospect, academy, slotIndex);

            if (placed)
            {
                Debug.Log($"Academy intake complete: brought in {prospect.Name} to academy slot {slotIndex}.");
            }
            else
            {
                Debug.LogWarning(
                    $"Academy intake failed: player={prospect.Name}, slotIndex={slotIndex}, validSlot={validSlot}, slotEmpty={slotEmpty}, wasDiscovered={wasDiscovered}."
                );
            }

            RefreshScoutingUI();
        }

        private void OnAcademyProspectClicked(PlayerAgent prospect)
        {
            if (academy.TryPromoteToReserves(prospect))
            {
                GetOrCreateAgentTeam(managedTeamName).AddReservePlayer(prospect);
            }

            RefreshScoutingUI();
        }

        // Manual release (backlog item 8, session 11; empty-slot rework session 13) -
        // leaves the slot genuinely empty now instead of auto-backfilling, see
        // ManagerAcademy.ReleaseProspect's own comment.
        private void OnReleaseAcademyProspectClicked(PlayerAgent prospect)
        {
            academy.ReleaseProspect(prospect);

            OnInspectBackClicked();
        }

        private void OpenAcademyProspectDetail(PlayerAgent prospect, List<PlayerAgent> browseList)
        {
            scoutingInspectReturnScroll = scoutingScrollRect != null ? scoutingScrollRect.verticalNormalizedPosition : 1f;
            playerInspectReturnTarget = PlayerInspectReturnTarget.Scouting;
            OpenPlayerInspect(prospect, browseList, ownSquad: false, isAcademyProspect: true);
        }

        private void OnScoutingColumnHeaderClicked(int column)
        {
            if (scoutingSortColumn == column)
            {
                scoutingSortDescending = !scoutingSortDescending;
            }
            else
            {
                scoutingSortColumn = column;
                scoutingSortDescending = true;
            }

            RefreshScoutingUI();
        }

        // Column indices match ScoutingColumnHeaders. Potential sorts by the same
        // fuzzy-band display string an unscouted prospect already shows (see
        // ManagerScouting.GetDisplayPotential) rather than the true hidden value -
        // sorting shouldn't leak information scouting itself hasn't revealed yet.
        private int CompareScoutingColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    string nationA = ManagerPlayerNationality.GetNationality(a).Name;
                    string nationB = ManagerPlayerNationality.GetNationality(b).Name;
                    result = string.Compare(nationA, nationB, StringComparison.OrdinalIgnoreCase);
                    break;
                case 4:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = GetScoutingPotentialSortKey(a).CompareTo(GetScoutingPotentialSortKey(b));
                    break;
                case 6:
                    // Ascending = most urgent (fewest matchdays left) first by default,
                    // matching how every other column's "descending: true" first click
                    // already reads as "most interesting first" for that column.
                    result = scouting.GetDaysUntilPoached(b, careerCalendar.CurrentDayNumber).CompareTo(scouting.GetDaysUntilPoached(a, careerCalendar.CurrentDayNumber));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private float GetScoutingPotentialSortKey(PlayerAgent prospect)
        {
            string[] parts = scouting.GetDisplayPotential(prospect).Split('-');
            float lowerBand = parts.Length > 0 && float.TryParse(parts[0], out float lower) ? lower : 0f;
            float upperBand = parts.Length > 1 && float.TryParse(parts[1], out float upper) ? upper : 0f;

            // Two prospects can share a lower band (it's quantized to steps of 5) - the
            // upper band as a fractional tiebreaker (max 99/1000) makes "70-95" sort above
            // "70-82" without ever flipping the primary lower-band ordering.
            return lowerBand + (upperBand / 1000f);
        }

        // Session 9 - Thomas: "click a prospect's name to see detailed stats" instead of
        // buying/scouting blind off just Age/OVR. browseList is the exact same list
        // (allProspects) Prev/Next will cycle through - browsing every scouted prospect
        // without going back to the list each time. ownSquad:false hides the roles band
        // (captaincy/set-piece/attack-defend) in RefreshPlayerInspectUI - none of that
        // applies to a prospect you don't own yet.
        private void OpenScoutedProspectDetail(PlayerAgent prospect, List<PlayerAgent> browseList)
        {
            scoutingInspectReturnScroll = scoutingScrollRect != null ? scoutingScrollRect.verticalNormalizedPosition : 1f;
            playerInspectReturnTarget = PlayerInspectReturnTarget.Scouting;
            OpenPlayerInspect(prospect, browseList, ownSquad: false);
        }

        private IEnumerator RestoreScoutingScrollNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scoutingScrollRect != null) scoutingScrollRect.verticalNormalizedPosition = scoutingInspectReturnScroll;
        }

    }
}
