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
        private static float GetRatingPercent(PlayerAgent player)
        {
            return GetDisplayRating(player.GetOverallRating()) / 99f;
        }

        // --- Squad list: read-only Pos/Player/OVR/Rating browse screen (Starting XI +
        // Bench), reached via the Tactics Board's "List View" button. Built entirely in
        // code the first time it's opened, same precedent as the Tactics Board and Match
        // Events panels - no Editor-placed panel to wire. ---

        public void OnOpenSquadListClicked()
        {
            if (!squadBrowseChromeBuilt)
            {
                BuildSquadChrome();
                squadBrowseChromeBuilt = true;
            }

            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(true);

            RefreshSquadUI();
        }

        public void OnSquadListBackClicked()
        {
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(false);

            OnViewSquadClicked();
        }

        private void BuildSquadChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            squadBrowsePanel = new GameObject("SquadBrowsePanel", typeof(RectTransform));
            squadBrowsePanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = squadBrowsePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(squadBrowsePanel);

            GameObject header = ManagerUITheme.BuildAccentBand(squadBrowsePanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SQUAD", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1000f, 20f));
            squadBrowseByline = ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO TACTICS BOARD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(230f, 36f));
            backButton.onClick.AddListener(OnSquadListBackClicked);

            Button sortButton = ManagerUITheme.BuildButton(header.transform, "SORT: POSITION", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(sortButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-306f, -27f), new Vector2(160f, 36f));
            ManagerUITheme.SetDisabledPlaceholder(sortButton, "SORT: POSITION");

            Button filterButton = ManagerUITheme.BuildButton(header.transform, "FILTER", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(filterButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-482f, -27f), new Vector2(150f, 36f));
            ManagerUITheme.SetDisabledPlaceholder(filterButton, "FILTER");

            // Centered max-width:1600 scrollable list - code-built ScrollRect+Viewport+
            // Content+Scrollbar, same pattern as BuildMatchEventsPanel.
            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("SquadScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(squadBrowsePanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
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

            squadBrowseListView = contentObj.GetComponent<SquadListView>();
            squadBrowseListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // See BuildMatchEventsPanel's identical comment for how this was verified -
            // +1 is Unity's own default and is confirmed (via simulated scroll input,
            // not guessed) to move content the correct direction.
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            GameObject scrollbarObj = new GameObject("SquadScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(squadBrowsePanel.transform, false);
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

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(squadBrowsePanel.transform));
        }

        private void RefreshSquadUI()
        {
            if (squadBrowseListView == null)
            {
                return;
            }

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);

            if (squadBrowseByline != null)
            {
                string formationText = TacticsBoardLayout.FormatFormation(team.Formation);

                if (currentFixtureIndex < managedTeamFixtures.Count)
                {
                    OpenFootballMatch nextFixture = managedTeamFixtures[currentFixtureIndex];
                    bool managedIsHome = nextFixture.HomeTeam == managedTeamName;
                    string opponentName = managedIsHome ? nextFixture.AwayTeam : nextFixture.HomeTeam;
                    squadBrowseByline.text = $"Next: vs {opponentName} ({(managedIsHome ? "H" : "A")})   ·   Formation {formationText}   ·   Mentality: {selectedMentality}";
                }
                else
                {
                    squadBrowseByline.text = $"Season complete   ·   Formation {formationText}   ·   Mentality: {selectedMentality}";
                }
            }

            ManagerSquadRoles squadRoles = GetOrCreateSquadRoles(managedTeamName);

            squadBrowseListView.Clear();
            squadBrowseListView.AddGridHeaderRow(OnSquadColumnHeaderClicked, squadSortColumn, squadSortDescending);
            squadBrowseListView.AddSectionHeader("Starting XI");

            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(team.Formation);

            // Starting XI's slot-based POS (from the formation, not just the player's own
            // primary position) only makes sense paired with each player's original index
            // - captured before any sort reorders the list, so "who plays where" stays
            // correct regardless of sort column/direction.
            List<(PlayerAgent player, PlayerPosition slot)> startingWithSlots = new List<(PlayerAgent, PlayerPosition)>();
            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent player = team.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                startingWithSlots.Add((player, slot));
            }

            if (squadSortColumn >= 0)
            {
                startingWithSlots.Sort((a, b) => CompareSquadColumn(a.player, b.player, squadSortColumn, squadSortDescending));
            }

            foreach (var (player, slot) in startingWithSlots)
            {
                squadBrowseListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            squadBrowseListView.AddSectionHeader($"Bench ({team.Bench.Count})");

            List<PlayerAgent> benchPlayers = new List<PlayerAgent>(team.Bench);
            if (squadSortColumn >= 0)
            {
                benchPlayers.Sort((a, b) => CompareSquadColumn(a, b, squadSortColumn, squadSortDescending));
            }

            foreach (PlayerAgent player in benchPlayers)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            // Reserves section (session 16 - Thomas: "we need more players per team...
            // an actual reserve", and the follow-up "Visible Reserves list" scope choice
            // when offered a choice between a quiet backend depth boost and actually
            // surfacing it). The reserve pool (see GetOrCreateReservePool) already
            // existed as an invisible emergency safety net beneath the real 20-man squad
            // - it only ever showed up once a specific player got promoted onto the
            // Bench via an injury/loan call-up. Eagerly generating it here (rather than
            // waiting for the first call-up) so it's visible from the very first time
            // the manager opens Squad, not just after a crisis. Read-only (onRowClicked:
            // null, same pattern as the opponent-pitch browse view) - these players
            // aren't on the real matchday squad, so Sell/role-assignment/etc. don't
            // apply to them the way they do for Starting XI/Bench rows.
            List<PlayerAgent> reservePlayers = team.Reserves;
            squadBrowseListView.AddSectionHeader($"Reserves ({reservePlayers.Count})");

            List<PlayerAgent> sortedReserves = new List<PlayerAgent>(reservePlayers);
            if (squadSortColumn >= 0)
            {
                sortedReserves.Sort((a, b) => CompareSquadColumn(a, b, squadSortColumn, squadSortDescending));
            }

            foreach (PlayerAgent player in sortedReserves)
            {
                squadBrowseListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), OnSquadRowClicked, BuildRoleBadgeSuffix(player, squadRoles), squadRoles.IsInjured(player, careerCalendar.CurrentDayNumber), BuildFitnessBadgeSuffix(player, squadRoles), player.Age.ToString(), $"£{ManagerClubFinance.GetMarketValue(player):F1}m");
            }

            // Rows are cleared and rebuilt fresh every refresh - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(squadBrowsePanel.transform));
        }

        private void OnSquadColumnHeaderClicked(int column)
        {
            if (squadSortColumn == column)
            {
                squadSortDescending = !squadSortDescending;
            }
            else
            {
                squadSortColumn = column;
                squadSortDescending = true;
            }

            RefreshSquadUI();
        }

        // Column indices match SquadListView's GridColumnHeaders (POS/PLAYER/AGE/OVR/
        // FIT/VALUE/RATING). FIT and RATING aren't sortable (FIT is condition-derived
        // text with a variable "(Ret. MDx)" suffix, not a clean number; RATING is a
        // live-match-only stat with no meaning outside a match) - clicking those headers
        // is a no-op via AddGridHeaderRow's onColumnClicked still firing but landing on
        // the default case below (falls through to 0, same as clicking POS/PLAYER twice
        // would after a no-op sort).
        private int CompareSquadColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
        {
            int result;
            switch (column)
            {
                case 0:
                    result = string.Compare(a.PrimaryPosition.ToString(), b.PrimaryPosition.ToString(), StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    result = a.Age.CompareTo(b.Age);
                    break;
                case 3:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = ManagerClubFinance.GetMarketValue(a).CompareTo(ManagerClubFinance.GetMarketValue(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        private void OnSquadRowClicked(PlayerAgent player)
        {
            playerInspectReturnTarget = PlayerInspectReturnTarget.Squad;
            OpenPlayerInspect(player);
        }

        // Session 11: used to apply a cosmetic +15% stretch away from 50 so strong
        // squads read as clearly elite without touching the true GetOverallRating()
        // value - removed once AgentSquadGenerator's team-strength multiplier was
        // strengthened (0.35->0.75) to make top clubs legitimately generate higher
        // attributes instead. Stacking the old stretch on top of that honest fix
        // tripled the league's count of "90+" players (6 true vs 18 displayed in a real
        // 20-club sample) and pulled players as low as a true 85 up into "90+" - closer
        // to inflation than to Liverpool no longer reading as underrated. This function
        // stays as the one place every screen routes an Overall through, in case a
        // display transform is ever wanted again - it just no longer changes anything.
        private static int GetDisplayRating(float trueRating)
        {
            return Mathf.RoundToInt(Mathf.Clamp(trueRating, 1f, 99f));
        }

        // Compact role indicators for the Squad screen's PLAYER cell - "C"/"VC" for
        // captaincy, "PK"/"FK"/"CK" for set-piece takers. Assignment itself happens on
        // The Tactics screen (see BuildTacticsScreenChrome); this is read-only.
        private static string BuildRoleBadgeSuffix(PlayerAgent player, ManagerSquadRoles roles)
        {
            List<string> badges = new();

            if (roles.Captain == player) badges.Add("C");
            if (roles.ViceCaptain == player) badges.Add("VC");
            if (roles.PenaltyTaker == player) badges.Add("PK");
            if (roles.FreeKickTaker == player) badges.Add("FK");
            if (roles.LeftCornerTaker == player) badges.Add("CK-L");
            if (roles.RightCornerTaker == player) badges.Add("CK-R");

            if (badges.Count == 0)
            {
                return string.Empty;
            }

            string accentHex = ColorUtility.ToHtmlStringRGB(ManagerUITheme.Accent);
            return $"  <size=80%><color=#{accentHex}>{string.Join(" ", badges)}</color></size>";
        }

        // Injured takes priority over a plain Condition readout - no point showing a
        // fitness number next to a player who's actually out.
        //
        // Always-visible Condition (backlog item, session 10) - this used to only
        // appear once Condition dropped below 60%, staying an empty string otherwise.
        // Condition genuinely persists matchday-to-matchday, but hiding the number above
        // that threshold meant a manager had no way to see it trending down before it
        // was already a crisis - the whole point of tracking it per-matchday in the
        // first place. Always shown now; color grading (Accent/Warning/Danger by band)
        // keeps a fully-fit player's number calm rather than loud, without hiding it.
        private string BuildFitnessBadgeSuffix(PlayerAgent player, ManagerSquadRoles roles)
        {
            if (roles.IsInjured(player, careerCalendar.CurrentDayNumber))
            {
                // No leading "INJ" text anymore - the injury cross icon (see
                // ManagerUITheme.BuildInjuryCrossIcon) already says that visually now;
                // this just adds the one piece of info the icon alone can't carry.
                int returnDay = roles.GetInjuryReturnMatchday(player);
                string dangerHex = ColorUtility.ToHtmlStringRGB(ManagerUITheme.Danger);
                return $"<color=#{dangerHex}>(Ret. {ManagerCareerCalendar.DisplayDateForDay(returnDay)})</color>";
            }

            float condition = roles.GetCondition(player);
            Color conditionColor = condition >= 85f
                ? ManagerUITheme.Accent
                : condition >= 60f
                    ? ManagerUITheme.Warning
                    : ManagerUITheme.Danger;
            string conditionHex = ColorUtility.ToHtmlStringRGB(conditionColor);
            return $"<color=#{conditionHex}>{condition:F0}%</color>";
        }

        // --- Player Inspect (Prev/Next once inside; entry point jumps straight to a
        // specific player from the squad browse list - no standalone Hub entry point) ---

        // browseList/ownSquad (session 9 - Thomas: "we need to be able to click on
        // [a Transfer/Scouting target's] name to see detailed stats") let Player Detail
        // browse an arbitrary list instead of always the managed squad - e.g. Prev/Next
        // cycles through the exact Scouting or Transfer Market list you clicked from.
        // Every pre-existing call site omits both and keeps browsing the managed squad
        // exactly as before. ownSquad also gates the roles band in RefreshPlayerInspectUI
        // - captaincy/set-piece/attack-defend assignment only makes sense for a player
        // you actually manage, not someone else's player you're scouting or bidding on.
        private void OpenPlayerInspect(PlayerAgent preselected, List<PlayerAgent> browseList = null, bool ownSquad = true, bool isAcademyProspect = false)
        {
            CleanupStrayDragGhosts();

            if (browseList != null)
            {
                inspectSquadPlayers = browseList;
            }
            else
            {
                AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
                inspectSquadPlayers = new List<PlayerAgent>(team.Players);
            }

            inspectIsAcademyProspect = isAcademyProspect;

            inspectIsOwnSquad = ownSquad;

            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            int preselectedIndex = preselected != null ? inspectSquadPlayers.IndexOf(preselected) : -1;
            inspectPlayerIndex = preselectedIndex >= 0 ? preselectedIndex : 0;

            if (!playerInspectChromeBuilt)
            {
                BuildPlayerInspectChrome();
                playerInspectChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(false);
            // squadBrowsePanel didn't exist when this method was first written - missing
            // here meant opening Player Detail from the Squad list left that screen
            // active underneath (or on top of, depending on sibling order) Player
            // Detail instead of actually navigating away from it.
            if (squadBrowsePanel != null) squadBrowsePanel.SetActive(false);
            // Same gap, hit again (session 9 live bug report): scoutingPanel/
            // transferMarketPanel didn't exist when this method was first written
            // either, so clicking a name on either screen correctly opened Player
            // Detail underneath, but the still-active source panel stayed on top and
            // visually hid it - confirmed live: it only appeared after pressing that
            // screen's own Back button, which hid the panel actually covering it.
            if (scoutingPanel != null) scoutingPanel.SetActive(false);
            if (transferMarketPanel != null) transferMarketPanel.SetActive(false);
            if (playerInspectPanel != null) playerInspectPanel.SetActive(true);

            RefreshPlayerInspectUI();
        }

        // Footer band + the three nav buttons (Prev/Next/Back), which were only ever
        // wired to click handlers, never positioned or styled - same "wired but
        // untouched" gap as TransfersButton/ExitToTitleButton originally were.
        private void BuildPlayerInspectChrome()
        {
            if (playerInspectPanel == null)
            {
                return;
            }

            const float footerHeight = 90f;
            ManagerUITheme.BuildAccentBand(playerInspectPanel.transform, topBand: false, height: footerHeight);

            // Positioned relative to the panel itself (not reparented into the footer
            // band). SetPointAnchor forces pivot == anchor, and these use a bottom
            // anchor (y=0), so the Y offset here is the button's BOTTOM edge, not its
            // center - true vertical centering needs (footerHeight - buttonHeight) / 2,
            // not footerHeight / 2 (that was the earlier bug: it centered as if pivot.y
            // were 0.5, pushing every button up and out the top of the band).
            const float navButtonHeight = 48f;
            float navButtonY = (footerHeight - navButtonHeight) / 2f;

            if (inspectBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, navButtonY), new Vector2(220f, navButtonHeight));
                if (inspectBackButton.TryGetComponent(out Image backImage))
                {
                    backImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectBackButton, "BACK TO SQUAD", ManagerUITheme.TextBody, 15);
            }

            if (inspectPreviousButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectPreviousButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(60f, navButtonY), new Vector2(140f, navButtonHeight));
                if (inspectPreviousButton.TryGetComponent(out Image prevImage))
                {
                    prevImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectPreviousButton, "< PREV", ManagerUITheme.TextBody, 14);
            }

            if (inspectNextButton != null)
            {
                ManagerUITheme.SetPointAnchor(inspectNextButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(216f, navButtonY), new Vector2(140f, navButtonHeight));
                if (inspectNextButton.TryGetComponent(out Image nextImage))
                {
                    nextImage.color = ManagerUITheme.CardNeutral;
                }

                ManagerUITheme.NormalizeButtonLabel(inspectNextButton, "NEXT >", ManagerUITheme.TextBody, 13);
            }
        }

        public void OnInspectPreviousClicked()
        {
            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            inspectPlayerIndex = (inspectPlayerIndex - 1 + inspectSquadPlayers.Count) % inspectSquadPlayers.Count;
            RefreshPlayerInspectUI();
        }

        public void OnInspectNextClicked()
        {
            if (inspectSquadPlayers.Count == 0)
            {
                return;
            }

            inspectPlayerIndex = (inspectPlayerIndex + 1) % inspectSquadPlayers.Count;
            RefreshPlayerInspectUI();
        }

        public void OnInspectBackClicked()
        {
            CloseMatchdaySquadSwapDialog();
            if (playerInspectPanel != null) playerInspectPanel.SetActive(false);

            switch (playerInspectReturnTarget)
            {
                case PlayerInspectReturnTarget.TacticsBoard:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnViewSquadClicked();
                    break;
                case PlayerInspectReturnTarget.Squad:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenSquadListClicked();
                    break;
                case PlayerInspectReturnTarget.Scouting:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenScoutingClicked();
                    break;
                case PlayerInspectReturnTarget.TransferMarket:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    OnOpenTransferMarketClicked();
                    break;
                default:
                    playerInspectReturnTarget = PlayerInspectReturnTarget.Hub;
                    ShowSeasonHub();
                    break;
            }
        }

        private readonly List<GameObject> spawnedInspectElements = new();
        private GameObject matchdaySquadSwapDialog;

        private void ShowMatchdaySquadSwapDialog(PlayerAgent selectedPlayer)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool selectedIsBench = team.Bench.Contains(selectedPlayer);
            List<PlayerAgent> options = selectedIsBench
                ? new List<PlayerAgent>(team.Reserves)
                : new List<PlayerAgent>(team.Bench);
            if (options.Count == 0) return;

            if (matchdaySquadSwapDialog != null) Destroy(matchdaySquadSwapDialog);

            Transform root = titlePanel.transform.parent;
            matchdaySquadSwapDialog = new GameObject("MatchdaySquadSwapDialog", typeof(RectTransform), typeof(Image));
            matchdaySquadSwapDialog.transform.SetParent(root, false);
            RectTransform backdropRect = matchdaySquadSwapDialog.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            matchdaySquadSwapDialog.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(matchdaySquadSwapDialog.transform, false);
            ManagerUITheme.SetPointAnchor(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 660f));
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(title.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 34f));
            ManagerUITheme.BuildLabel(title.transform,
                selectedIsBench ? $"REPLACE {selectedPlayer.Name.ToUpperInvariant()}" : $"SELECT {selectedPlayer.Name.ToUpperInvariant()} AS SUBSTITUTE",
                20, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            float top = 82f;
            foreach (PlayerAgent option in options)
            {
                PlayerAgent captured = option;
                string label = $"{option.Name}  ·  {option.PrimaryPosition}  ·  OVR {GetDisplayRating(option.GetOverallRating())}  ·  {roles.GetCondition(option):F0}%";
                Button optionButton = ManagerUITheme.BuildButton(card.transform, label, ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(optionButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(540f, 42f));
                optionButton.onClick.AddListener(() => OnMatchdaySquadSwapSelected(selectedPlayer, captured));
                top += 48f;
            }

            Button cancel = ManagerUITheme.BuildButton(card.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextMuted, 13);
            ManagerUITheme.SetPointAnchor(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(180f, 42f));
            cancel.onClick.AddListener(CloseMatchdaySquadSwapDialog);
        }

        private void OnMatchdaySquadSwapSelected(PlayerAgent selectedPlayer, PlayerAgent option)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            PlayerAgent benchPlayer = team.Bench.Contains(selectedPlayer) ? selectedPlayer : option;
            PlayerAgent reservePlayer = team.Reserves.Contains(selectedPlayer) ? selectedPlayer : option;
            if (team.SwapBenchAndReserve(benchPlayer, reservePlayer))
            {
                CloseMatchdaySquadSwapDialog();
                RefreshPlayerInspectUI();
            }
        }

        private void CloseMatchdaySquadSwapDialog()
        {
            if (matchdaySquadSwapDialog != null) Destroy(matchdaySquadSwapDialog);
            matchdaySquadSwapDialog = null;
        }

        // Rebuilt in full each time (unlike Title/Team Select, which build once) since the
        // content changes per player. Only uses PlayerAgent fields that actually exist -
        // Archetypes are generated data now, so the descriptive footballing profile in
        // the header is genuine rather than inferred UI flavour text.
        private void RefreshPlayerInspectUI()
        {
            if (playerInspectContentContainer == null || inspectSquadPlayers.Count == 0)
            {
                return;
            }

            foreach (GameObject element in spawnedInspectElements)
            {
                if (element != null)
                {
                    Destroy(element);
                }
            }

            spawnedInspectElements.Clear();

            PlayerAgent player = inspectSquadPlayers[inspectPlayerIndex];
            // player.IsStartingEleven/Bench membership is meaningless for a browsed
            // Scouting/Transfer target that isn't part of the managed squad at all -
            // would otherwise misleadingly default to "Bench" for everyone.
            string squadStatus = !inspectIsOwnSquad
                ? (playerInspectReturnTarget == PlayerInspectReturnTarget.Scouting ? "Scouting Target" : "Transfer Target")
                : player.IsStartingEleven ? "Starting XI" : GetOrCreateAgentTeam(managedTeamName).Reserves.Contains(player) ? "Reserves" : "Bench";

            // Centered max-width:1600px content region within the full-stretch 1920-wide
            // container, matching the mockup's centered layout instead of edge-to-edge.
            const float contentMargin = (1920f - 1600f) / 2f;

            // Bumped from 130 to 240 - centering the stat columns below (see
            // BuildAttributeColumn) moved the empty space that used to sit below the
            // stats to between the header and the stats instead, since the grid area
            // shrank but the header didn't grow to compensate (confirmed live: a large
            // gap opened up between the header band and "TECHNICAL"). Growing the banner
            // itself - bigger photo/name/meta - eats into that gap directly rather than
            // just relocating it.
            // Bumped again to 300 (2026-08-09) to fit the bigger photo below (220px, was
            // 140) with matching top/bottom margins - user feedback wanted the photo to
            // fill more of its area, twice ("fill in the red borders", then "bigger
            // actually").
            const float headerBandHeight = 300f;

            // A new strip between the header band and the attribute grid for role
            // assignment (captaincy, set-piece takers, attack/defend leaning) - see
            // RolesBand below. Kept as its own band rather than crammed into the header,
            // which already took two rounds of tuning to fit the bigger photo. Taller for
            // an academy prospect (session 10) - the focus-stats picker needs room for a
            // caption line plus a wrapped 2-row chip grid (up to 18 outfield attributes),
            // not just the single row of role toggles/LOAN OUT this band was sized for.
            // Not a const anymore since it now varies, but everything downstream
            // (attributeGridRect below) already reads it as a variable, so the rest of
            // the layout adjusts automatically.
            float rolesBandHeight = inspectIsAcademyProspect ? 130f : 56f;

            // Full-width (no contentMargin) unlike the centered stat grid below it - the
            // margined header looked like it wasn't filling the screen, with visible
            // background peeking on both sides (confirmed live). The name/meta/badges
            // stay left-anchored at their existing offsets, so widening this just gives
            // the right-anchored OVR number more room out toward the true screen edge.
            GameObject headerBand = new GameObject("HeaderBand", typeof(RectTransform), typeof(Image));
            headerBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(headerBand, 0f, headerBandHeight, 0f);
            headerBand.GetComponent<Image>().color = ManagerUITheme.PanelDark;
            spawnedInspectElements.Add(headerBand);

            GameObject photo = new GameObject("PhotoPlaceholder", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(headerBand.transform, false);
            RectTransform photoRect = photo.GetComponent<RectTransform>();
            photoRect.anchorMin = new Vector2(0f, 1f);
            photoRect.anchorMax = new Vector2(0f, 1f);
            photoRect.pivot = new Vector2(0f, 1f);
            photoRect.sizeDelta = new Vector2(220f, 220f);
            photoRect.anchoredPosition = new Vector2(36f, -40f);

            // Developer easter eggs (see ApplyDeveloperEasterEggPlayer) - real portraits
            // for these specific players, everyone else keeps the plain placeholder
            // color since there's no actual photo pipeline for generated players.
            Image photoImage = photo.GetComponent<Image>();
            Sprite easterEggPortrait = player.Name switch
            {
                "Hidde Rietberg" => hiddePortraitSprite,
                "Thomas Bernards" => thomasPortraitSprite,
                "Charles Herring" => charliePortraitSprite,
                "Victor Hamberg" => victorPortraitSprite,
                _ => null
            };

            if (easterEggPortrait != null)
            {
                photoImage.sprite = easterEggPortrait;
                photoImage.color = Color.white;
                photoImage.preserveAspect = true;
            }
            else
            {
                photoImage.color = ManagerUITheme.CardNeutralAlt;
            }

            // Start-x and sizeDelta shrink both bumped from 200/-320 to 300/-420 to clear
            // the wider photo (220px, was 140) with the same ~44px gap after it.
            GameObject nameLabel = new GameObject("Name", typeof(RectTransform));
            nameLabel.transform.SetParent(headerBand.transform, false);
            RectTransform nameRect = nameLabel.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.sizeDelta = new Vector2(-420f, 40f);
            nameRect.anchoredPosition = new Vector2(300f, -60f);
            ManagerUITheme.BuildLabel(nameLabel.transform, player.Name.ToUpperInvariant(), 32, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject metaLabel = new GameObject("Meta", typeof(RectTransform));
            metaLabel.transform.SetParent(headerBand.transform, false);
            RectTransform metaRect = metaLabel.GetComponent<RectTransform>();
            metaRect.anchorMin = new Vector2(0f, 1f);
            metaRect.anchorMax = new Vector2(1f, 1f);
            metaRect.pivot = new Vector2(0f, 1f);
            metaRect.sizeDelta = new Vector2(-420f, 34f);
            metaRect.anchoredPosition = new Vector2(300f, -116f);
            string nationalityName = ManagerPlayerNationality.GetNationality(player).Name;
            string archetypeText = string.IsNullOrWhiteSpace(player.Archetype) ? player.Role.ToString() : player.Archetype;
            string metaText = $"{archetypeText}  ·  {nationalityName}  ·  {player.Age} yrs  ·  {player.Height:F0}cm  ·  Weak Foot: {BuildFootRating(player.WeakFoot)}  ·  Player {inspectPlayerIndex + 1} of {inspectSquadPlayers.Count} ({squadStatus})";
            TextMeshProUGUI metaTMP = ManagerUITheme.BuildLabel(metaLabel.transform, metaText, 21, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            if (weakFootStarSpriteAsset != null) metaTMP.spriteAsset = weakFootStarSpriteAsset;

            float badgeX = 300f;
            AddPositionBadge(headerBand.transform, player.PrimaryPosition.ToString(), badgeX, true);
            badgeX += 68f;

            foreach (PlayerPosition secondary in player.SecondaryPositions)
            {
                AddPositionBadge(headerBand.transform, secondary.ToString(), badgeX, false);
                badgeX += 68f;
            }

            int displayRating = GetDisplayRating(player.GetOverallRating());

            GameObject ovrValue = new GameObject("OvrValue", typeof(RectTransform));
            ovrValue.transform.SetParent(headerBand.transform, false);
            RectTransform ovrRect = ovrValue.GetComponent<RectTransform>();
            ovrRect.anchorMin = new Vector2(1f, 1f);
            ovrRect.anchorMax = new Vector2(1f, 1f);
            ovrRect.pivot = new Vector2(1f, 1f);
            ovrRect.sizeDelta = new Vector2(120f, 64f);
            ovrRect.anchoredPosition = new Vector2(-36f, -36f);
            ManagerUITheme.BuildLabel(ovrValue.transform, displayRating.ToString(), 56, ManagerUITheme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject ovrCaption = new GameObject("OvrCaption", typeof(RectTransform));
            ovrCaption.transform.SetParent(headerBand.transform, false);
            RectTransform ovrCaptionRect = ovrCaption.GetComponent<RectTransform>();
            ovrCaptionRect.anchorMin = new Vector2(1f, 1f);
            ovrCaptionRect.anchorMax = new Vector2(1f, 1f);
            ovrCaptionRect.pivot = new Vector2(1f, 1f);
            ovrCaptionRect.sizeDelta = new Vector2(180f, 18f);
            ovrCaptionRect.anchoredPosition = new Vector2(-36f, -106f);
            ManagerUITheme.BuildLabel(ovrCaption.transform, $"OVERALL ({player.PrimaryPosition})", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            // Always-visible Condition (backlog item, session 10) - Squad Browse's own
            // "FIT XX%" suffix (see BuildFitnessBadgeSuffix) used to be the only place
            // Condition showed at all, and only once it dropped below 60% - Player
            // Detail had no raw number anywhere. Gated on inspectIsOwnSquad like the
            // roles band below: Condition isn't tracked for browsed Scouting/Transfer
            // targets or other clubs' players (ApplyMatchdayConditionAndInjuries only
            // ticks the managed squad), so GetCondition would just silently read back
            // its 100f default for anyone else - showing that as a real number would be
            // misleading rather than merely absent.
            if (inspectIsOwnSquad)
            {
                float condition = GetOrCreateSquadRoles(managedTeamName).GetCondition(player);
                Color conditionColor = condition >= 85f
                    ? ManagerUITheme.Accent
                    : condition >= 60f
                        ? ManagerUITheme.Warning
                        : ManagerUITheme.Danger;

                GameObject conditionCaption = new GameObject("ConditionCaption", typeof(RectTransform));
                conditionCaption.transform.SetParent(headerBand.transform, false);
                RectTransform conditionRect = conditionCaption.GetComponent<RectTransform>();
                conditionRect.anchorMin = new Vector2(1f, 1f);
                conditionRect.anchorMax = new Vector2(1f, 1f);
                conditionRect.pivot = new Vector2(1f, 1f);
                conditionRect.sizeDelta = new Vector2(180f, 18f);
                conditionRect.anchoredPosition = new Vector2(-36f, -126f);
                ManagerUITheme.BuildLabel(conditionCaption.transform, $"CONDITION {condition:F0}%", 13, conditionColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

                // Morale (session 10) - same placement/treatment as Condition directly
                // above it, same inspectIsOwnSquad gate for the same reason (only the
                // managed squad ever has real morale tracked - see
                // ApplyMatchMoraleForManagedTeam).
                float morale = GetOrCreateSquadRoles(managedTeamName).GetMorale(player);
                Color moraleColor = morale >= 70f
                    ? ManagerUITheme.Accent
                    : morale >= 40f
                        ? ManagerUITheme.Warning
                        : ManagerUITheme.Danger;

                GameObject moraleCaption = new GameObject("MoraleCaption", typeof(RectTransform));
                moraleCaption.transform.SetParent(headerBand.transform, false);
                RectTransform moraleRect = moraleCaption.GetComponent<RectTransform>();
                moraleRect.anchorMin = new Vector2(1f, 1f);
                moraleRect.anchorMax = new Vector2(1f, 1f);
                moraleRect.pivot = new Vector2(1f, 1f);
                moraleRect.sizeDelta = new Vector2(180f, 18f);
                moraleRect.anchoredPosition = new Vector2(-36f, -146f);
                ManagerUITheme.BuildLabel(moraleCaption.transform, $"MORALE {morale:F0}", 13, moraleColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            }

            // In-season delta (career arc backlog item, session 9/10) - a small badge
            // tucked into the top-right corner of the OVR number itself (live feedback:
            // a full "+3 LAST SEASON" text line read as too heavy - just the signed
            // number, right where you're already looking, reads faster). Switched from
            // GetLastSeasonOverallDelta to GetCurrentSeasonOverallDelta (session 10) -
            // the old one only updated at rollover, so it sat frozen showing last
            // season's final number for the entire following season even though growth
            // now ticks per matchday. Live version climbs in real time as ticks land and
            // resets to 0 right at rollover. Hidden entirely rather than showing "+0",
            // since a brand-new player (just scouted/signed/promoted) genuinely has no
            // season-start snapshot to compare against yet, and "+0" would misleadingly
            // read as "no growth this season" instead.
            int overallDelta = ManagerPlayerDevelopment.GetCurrentSeasonOverallDelta(player);
            if (overallDelta != 0)
            {
                GameObject ovrDelta = new GameObject("OvrDelta", typeof(RectTransform));
                ovrDelta.transform.SetParent(headerBand.transform, false);
                RectTransform ovrDeltaRect = ovrDelta.GetComponent<RectTransform>();
                ovrDeltaRect.anchorMin = new Vector2(1f, 1f);
                ovrDeltaRect.anchorMax = new Vector2(1f, 1f);
                ovrDeltaRect.pivot = new Vector2(1f, 1f);
                ovrDeltaRect.sizeDelta = new Vector2(44f, 22f);
                ovrDeltaRect.anchoredPosition = new Vector2(-8f, -18f);
                string deltaSign = overallDelta > 0 ? "+" : "";
                Color deltaColor = overallDelta > 0 ? ManagerUITheme.Accent : ManagerUITheme.Danger;
                ManagerUITheme.BuildLabel(ovrDelta.transform, $"{deltaSign}{overallDelta}", 17, deltaColor, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            }

            // Captain/vice-captain/penalty/free-kick/corner-taker assignment moved to the
            // Tactics screen (see BuildTacticsScreenChrome) - a centralized dropdown-
            // picker layout reads better than clicking into each individual player's own
            // page to toggle their role. Per-player attack/defend leaning stays here
            // though, since it's inherently about this one specific player rather than a
            // single-holder-per-team assignment, and wasn't part of that redesign.
            GameObject rolesBand = new GameObject("AttackDefendBand", typeof(RectTransform));
            rolesBand.transform.SetParent(playerInspectContentContainer, false);
            ManagerUITheme.AnchorTopStretch(rolesBand, headerBandHeight, rolesBandHeight, contentMargin);
            spawnedInspectElements.Add(rolesBand);

            // Captaincy/set-piece/attack-defend assignment only makes sense for a player
            // you actually manage - a Scouting/Transfer target browsed via
            // OpenPlayerInspect's browseList (session 9) isn't part of the managed squad
            // at all, so ManagerSquadRoles has no real state for them and toggling a role
            // here would incorrectly start tracking one. Same band height reserved either
            // way (see attributeGridRect below) so the layout doesn't shift.
            if (inspectIsOwnSquad)
            {
                ManagerSquadRoles squadRoles = GetOrCreateSquadRoles(managedTeamName);
                AttackDefendRole currentAttackDefendRole = squadRoles.GetRole(player);

                // Which leanings even make tactical sense varies by position - a winger
                // "defending" or a centre-back "attacking" isn't a real football
                // instruction the way it is for a fullback or a central midfielder.
                // Restricted per position rather than offering all three everywhere;
                // goalkeepers don't get the control at all, since it doesn't apply to
                // them.
                AttackDefendRole[] allowedRoles = GetAllowedAttackDefendRoles(player.PrimaryPosition);
                float roleX = 0f;

                foreach (AttackDefendRole allowedRole in allowedRoles)
                {
                    roleX = BuildRoleToggleButton(rolesBand.transform, allowedRole.ToString().ToUpperInvariant(), roleX, currentAttackDefendRole == allowedRole, () => SetAttackDefendRole(player, allowedRole));
                }

                // Loan system (session 9) - right-anchored so it sits at the far edge of
                // the band regardless of how many attack/defend toggles are on the left
                // (goalkeepers get none at all - see GetAllowedAttackDefendRoles).
                Button loanButton = ManagerUITheme.BuildButton(rolesBand.transform, "LOAN OUT", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(loanButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(130f, 40f));
                loanButton.onClick.AddListener(() => OnLoanOutClicked(player));

                AgentTeam ownTeam = GetOrCreateAgentTeam(managedTeamName);
                if (!ownTeam.StartingEleven.Contains(player))
                {
                    string selectionLabel = ownTeam.Bench.Contains(player) ? "CHANGE SUBSTITUTE" : "SELECT AS SUBSTITUTE";
                    Button selectionButton = ManagerUITheme.BuildButton(rolesBand.transform, selectionLabel, ManagerUITheme.CardNeutral, ManagerUITheme.Accent, 12);
                    ManagerUITheme.SetPointAnchor(selectionButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-235f, 0f), new Vector2(190f, 40f));
                    selectionButton.onClick.AddListener(() => ShowMatchdaySquadSwapDialog(player));
                }
            }
            else if (inspectIsAcademyProspect)
            {
                BuildFocusStatsPicker(rolesBand.transform, player);

                // Manual release (backlog item 8, session 11) - right-anchored on the
                // same top row as the focus-stats caption, same "action button sits at
                // the far edge of the band" convention LOAN OUT uses above for an
                // own-squad player. No confirmation dialog, same precedent as LOAN OUT -
                // returning to the Academy list is itself the confirmation.
                Button releaseButton = ManagerUITheme.BuildButton(rolesBand.transform, "RELEASE", ManagerUITheme.CardNeutral, ManagerUITheme.Danger, 13);
                ManagerUITheme.SetPointAnchor(releaseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-16f, -4f), new Vector2(110f, 26f));
                releaseButton.onClick.AddListener(() => OnReleaseAcademyProspectClicked(player));
            }
            else
            {
                ManagerUITheme.BuildLabel(rolesBand.transform, "NOT ON YOUR SQUAD - VIEW ONLY", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            }

            GameObject attributeGrid = new GameObject("AttributeGrid", typeof(RectTransform));
            attributeGrid.transform.SetParent(playerInspectContentContainer, false);
            spawnedInspectElements.Add(attributeGrid);

            // Full stretch down to the footer (not a fixed height) - the old fixed 220px
            // height left most of the panel as dead empty space below the grid. Same
            // centered max-width:1600 region as the header band above.
            RectTransform attributeGridRect = attributeGrid.GetComponent<RectTransform>();
            attributeGridRect.anchorMin = new Vector2(0f, 0f);
            attributeGridRect.anchorMax = new Vector2(1f, 1f);
            attributeGridRect.offsetMin = new Vector2(contentMargin + 20f, 110f);
            attributeGridRect.offsetMax = new Vector2(-(contentMargin + 20f), -(headerBandHeight + rolesBandHeight + 20f));

            if (player.PrimaryPosition == PlayerPosition.GK)
            {
                // GKs never roll meaningful Finishing/Dribbling/Crossing/Heading/Creativity/
                // Tackling values (see AgentSquadGenerator.GenerateGoalkeeper - those stay at
                // low dump-stat floors), so showing them here was always a bit dishonest.
                // Goalkeeping/Reflexes are the two stats actually generated for and used by
                // a keeper (AgentMatchSimulator's shot-stopping resolution) but were never
                // surfaced anywhere in the UI until now.
                BuildAttributeColumn(attributeGridRect, 0, 4, "Goalkeeping", new (string, float)[]
                {
                ("Handling", player.Handling), ("Reflexes", player.Reflexes),
                ("One On Ones", player.OneOnOnes), ("GK Positioning", player.GoalkeeperPositioning),
                ("Aerial Command", player.AerialCommand)
                });

                BuildAttributeColumn(attributeGridRect, 1, 4, "Mental", new (string, float)[]
                {
                ("Anticipation", player.Anticipation), ("Decisions", player.Decisions),
                ("Composure", player.Composure), ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 4, "Distribution", new (string, float)[]
                {
                ("Distribution", player.Distribution), ("Passing", player.Passing),
                ("First Touch", player.FirstTouch), ("Weak Foot", player.WeakFoot)
                });

                BuildAttributeColumn(attributeGridRect, 3, 4, "Physical", new (string, float)[]
                {
                ("Acceleration", player.Acceleration), ("Pace", player.Pace),
                ("Strength", player.Strength), ("Jumping Reach", player.JumpingReach)
                });
            }
            else
            {
                BuildAttributeColumn(attributeGridRect, 0, 3, "Technical", new (string, float)[]
                {
                ("Finishing", player.Finishing), ("First Touch", player.FirstTouch),
                ("Passing", player.Passing), ("Technique", player.Technique),
                ("Dribbling", player.Dribbling), ("Crossing", player.Crossing),
                ("Heading", player.Heading), ("Long Shots", player.LongShots),
                ("Tackling", player.Tackling), ("Marking", player.Marking),
                ("Free Kicks", player.FreeKicks), ("Corners", player.Corners), ("Penalties", player.Penalties)
                });

                BuildAttributeColumn(attributeGridRect, 1, 3, "Mental", new (string, float)[]
                {
                ("Anticipation", player.Anticipation), ("Decisions", player.Decisions),
                ("Composure", player.Composure), ("Vision", player.Vision),
                ("Off The Ball", player.OffTheBall), ("Def. Positioning", player.DefensivePositioning),
                ("Work Rate", player.WorkRate), ("Aggression", player.Aggression),
                ("Leadership", player.Leadership)
                });

                BuildAttributeColumn(attributeGridRect, 2, 3, "Physical", new (string, float)[]
                {
                ("Acceleration", player.Acceleration), ("Pace", player.Pace),
                ("Agility", player.Agility), ("Balance", player.Balance),
                ("Strength", player.Strength), ("Stamina", player.Stamina),
                ("Jumping Reach", player.JumpingReach)
                });

            }

            // Player Inspect fully destroys and rebuilds every label on every refresh
            // (see spawnedInspectElements above) - that rapid churn turns out to trigger
            // the same TMP mesh-generation failure the Title wordmark hit once (see
            // RecoverBlankLabelNextFrame): a label with correct text/color/position but
            // characterCount stuck at 0 forever, invisible despite everything else about
            // it checking out. Confirmed live on OvrValue (the big number next to
            // "OVERALL (GK)") - blank on screen, structurally perfect otherwise. This is
            // a general sweep rather than a fix targeted at that one label, since nothing
            // about the failure is specific to it.
            StartCoroutine(RecoverBlankLabelsNextFrame(playerInspectContentContainer));
        }

        private void AddPositionBadge(Transform parent, string label, float x, bool primary)
        {
            GameObject badge = new GameObject($"Badge_{label}", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);

            RectTransform rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(60f, 28f);
            rect.anchoredPosition = new Vector2(x, -150f);

            badge.GetComponent<Image>().color = primary ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            ManagerUITheme.BuildLabel(
                badge.transform,
                label,
                14,
                primary ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody,
                TextAlignmentOptions.Center,
                FontStyles.Bold);
        }

        // Which single-holder-per-team slot a RolesBand button toggles - see
        // AssignRole. Attack/defend role isn't here since it's per-player rather than
        // single-holder (see SetAttackDefendRole).
        private enum SquadRoleSlot
        {
            Captain,
            ViceCaptain,
            PenaltyTaker,
            FreeKickTaker,
            LeftCornerTaker,
            RightCornerTaker
        }

        // Directly assigns (or clears, if player is null) whoever holds a given role -
        // the Tactics screen's dropdown pickers call this after a selection, rather than
        // the old Player Detail "click a player to toggle their own role" interaction
        // this replaced. Captain and vice-captain stay mutually exclusive - assigning one
        // clears the other if the same player held it.
        private void AssignRole(SquadRoleSlot slot, PlayerAgent player)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            switch (slot)
            {
                case SquadRoleSlot.Captain:
                    roles.Captain = player;
                    if (player != null && roles.ViceCaptain == player) roles.ViceCaptain = null;
                    break;
                case SquadRoleSlot.ViceCaptain:
                    roles.ViceCaptain = player;
                    if (player != null && roles.Captain == player) roles.Captain = null;
                    break;
                case SquadRoleSlot.PenaltyTaker:
                    roles.PenaltyTaker = player;
                    break;
                case SquadRoleSlot.FreeKickTaker:
                    roles.FreeKickTaker = player;
                    break;
                case SquadRoleSlot.LeftCornerTaker:
                    roles.LeftCornerTaker = player;
                    break;
                case SquadRoleSlot.RightCornerTaker:
                    roles.RightCornerTaker = player;
                    break;
            }

            RefreshTacticsScreenUI();
        }

        private void SetAttackDefendRole(PlayerAgent player, AttackDefendRole role)
        {
            GetOrCreateSquadRoles(managedTeamName).SetRole(player, role);
            RefreshPlayerInspectUI();
        }

        // Which AttackDefendRole values are even offered for a given position - a real
        // manager wouldn't tell a winger to "defend" or a centre-back to "attack" the way
        // they would a fullback or central midfielder, whose whole job varies by
        // instruction. Empty for GK - the leaning doesn't apply to a goalkeeper at all.
        private static AttackDefendRole[] GetAllowedAttackDefendRoles(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK:
                    return Array.Empty<AttackDefendRole>();

                case PlayerPosition.CB:
                case PlayerPosition.DM:
                    return new[] { AttackDefendRole.Defensive, AttackDefendRole.Balanced };

                case PlayerPosition.RB:
                case PlayerPosition.LB:
                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                case PlayerPosition.CM:
                    return new[] { AttackDefendRole.Defensive, AttackDefendRole.Balanced, AttackDefendRole.Attacking };

                default: // AM, RW, LW, RM, LM, ST
                    return new[] { AttackDefendRole.Balanced, AttackDefendRole.Attacking };
            }
        }

        // Small pill-style toggle button for RolesBand - active state mirrors
        // HighlightSelectedMentalityButton's Accent/CardNeutral treatment for the
        // existing mentality selector, so the two read as the same kind of control.
        // Returns the x position the next button in the row should start at.
        private float BuildRoleToggleButton(Transform parent, string label, float x, bool active, Action onClick)
        {
            const float buttonWidth = 130f;
            const float buttonHeight = 40f;
            const float gap = 8f;

            GameObject buttonObject = new GameObject($"RoleButton_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            rect.anchoredPosition = new Vector2(x, 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = active ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            ManagerUITheme.BuildLabel(
                buttonObject.transform,
                label,
                13,
                active ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody,
                TextAlignmentOptions.Center,
                FontStyles.Bold);

            return x + buttonWidth + gap;
        }

        // Academy focus stats picker (session 10) - up to 3 attributes per prospect,
        // doubling their growth rate for as long as they stay in the academy (see
        // ManagerAcademy.ToggleFocusAttribute / ManagerPlayerDevelopment's Focused
        // helper). Reuses the same RolesBand slot the attack/defend toggles occupy for
        // an owned-squad player - mutually exclusive with that content (a player is
        // never both an academy prospect and on your own squad), so no extra layout
        // region is needed beyond the taller rolesBandHeight already reserved for this
        // case in RefreshPlayerInspectUI.
        private void BuildFocusStatsPicker(Transform parent, PlayerAgent prospect)
        {
            IReadOnlyList<string> selected = academy.GetFocusAttributes(prospect);

            GameObject captionObj = new GameObject("FocusCaption", typeof(RectTransform));
            captionObj.transform.SetParent(parent, false);
            RectTransform captionRect = captionObj.GetComponent<RectTransform>();
            captionRect.anchorMin = new Vector2(0f, 1f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.pivot = new Vector2(0f, 1f);
            captionRect.sizeDelta = new Vector2(0f, 20f);
            ManagerUITheme.BuildLabel(captionObj.transform, $"FOCUS STATS - {selected.Count}/3 SELECTED (2x GROWTH)", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            string[] focusable = ManagerAcademy.GetFocusableAttributes(prospect.PrimaryPosition);

            const float chipWidth = 140f;
            const float chipHeight = 30f;
            const float gapX = 8f;
            const float gapY = 6f;
            const int chipsPerRow = 9;

            for (int i = 0; i < focusable.Length; i++)
            {
                string attributeName = focusable[i];
                bool isSelected = selected.Contains(attributeName);

                int row = i / chipsPerRow;
                int col = i % chipsPerRow;
                float x = col * (chipWidth + gapX);
                float y = -28f - row * (chipHeight + gapY);

                GameObject chip = new GameObject($"FocusChip_{attributeName}", typeof(RectTransform), typeof(Image), typeof(Button));
                chip.transform.SetParent(parent, false);
                RectTransform chipRect = chip.GetComponent<RectTransform>();
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(0f, 1f);
                chipRect.pivot = new Vector2(0f, 1f);
                chipRect.sizeDelta = new Vector2(chipWidth, chipHeight);
                chipRect.anchoredPosition = new Vector2(x, y);

                Image chipImage = chip.GetComponent<Image>();
                chipImage.color = isSelected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

                Button chipButton = chip.GetComponent<Button>();
                chipButton.targetGraphic = chipImage;
                chipButton.onClick.AddListener(() => OnFocusAttributeToggled(prospect, attributeName));

                Color textColor = isSelected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
                ManagerUITheme.BuildLabel(chip.transform, AbbreviateAttributeName(attributeName), 12, textColor, TextAlignmentOptions.Center, FontStyles.Bold);
            }
        }

        private void OnFocusAttributeToggled(PlayerAgent prospect, string attributeName)
        {
            academy.ToggleFocusAttribute(prospect, attributeName);
            RefreshPlayerInspectUI();
        }

        // Short display labels for the focus-stat chips - full attribute names
        // ("ThroughBalls", "OffTheBall") don't fit a 140px chip at a readable size,
        // same abbreviation instinct as the existing role/set-piece badges
        // (BuildRoleBadgeSuffix's "PK"/"FK"/"CK-L").
        private static string AbbreviateAttributeName(string attributeName)
        {
            switch (attributeName)
            {
                case "Finishing": return "FIN";
                case "Passing": return "PAS";
                case "FirstTouch": return "1ST";
                case "Technique": return "TECH";
                case "Dribbling": return "DRI";
                case "Crossing": return "CRO";
                case "Heading": return "HEA";
                case "LongShots": return "L.SHOT";
                case "ThroughBalls": return "T.BALL";
                case "Creativity": return "CREA";
                case "Anticipation": return "ANT";
                case "Decisions": return "DEC";
                case "Vision": return "VIS";
                case "DefensivePositioning": return "D.POS";
                case "WorkRate": return "WORK";
                case "Positioning": return "POS";
                case "Composure": return "COMP";
                case "OffTheBall": return "OTB";
                case "Defending": return "DEF";
                case "Tackling": return "TACK";
                case "Marking": return "MARK";
                case "Pace": return "PACE";
                case "Acceleration": return "ACC";
                case "Agility": return "AGI";
                case "Balance": return "BAL";
                case "Strength": return "STR";
                case "Stamina": return "STAM";
                case "Aerial": return "AER";
                case "JumpingReach": return "JUMP";
                case "Goalkeeping": return "GK";
                case "Reflexes": return "REFL";
                case "Handling": return "HAND";
                case "OneOnOnes": return "1V1";
                case "AerialCommand": return "A.CMD";
                case "Distribution": return "DIST";
                case "GoalkeeperPositioning": return "GK.POS";
                default: return attributeName.ToUpperInvariant();
            }
        }

        private static void BuildAttributeColumn(RectTransform parent, int columnIndex, int totalColumns, string title, (string label, float value)[] attributes)
        {
            GameObject column = new GameObject($"Column_{title}", typeof(RectTransform));
            column.transform.SetParent(parent, false);

            RectTransform columnRect = column.GetComponent<RectTransform>();
            float colWidth = 1f / totalColumns;
            columnRect.anchorMin = new Vector2(columnIndex * colWidth, 0f);
            columnRect.anchorMax = new Vector2((columnIndex + 1) * colWidth, 1f);
            columnRect.offsetMin = new Vector2(6f, 0f);
            columnRect.offsetMax = new Vector2(-6f, 0f);

            const float titleHeight = 22f;
            const float titleGap = 14f;
            const float rowSpacing = 42f;
            float contentHeight = titleHeight + titleGap + attributes.Length * rowSpacing;

            // Top-aligned (matches the "PLAYER DETAIL" mockup's align-items:start), not
            // vertically centered. Centering was tried first, but with columns holding
            // different row counts (2-5) each one centers independently, so column titles
            // land at different heights depending on how many stats that column has - the
            // "why are Technical and Defensive's titles not in line" bug. Pinning every
            // stack to the top keeps titles level across all columns regardless of length,
            // and reads as using the grid's space top-down rather than floating in the
            // middle of a tall, mostly-empty area.
            GameObject stack = new GameObject("Stack", typeof(RectTransform));
            stack.transform.SetParent(column.transform, false);
            RectTransform stackRect = stack.GetComponent<RectTransform>();
            stackRect.anchorMin = new Vector2(0f, 1f);
            stackRect.anchorMax = new Vector2(1f, 1f);
            stackRect.pivot = new Vector2(0.5f, 1f);
            stackRect.sizeDelta = new Vector2(0f, contentHeight);
            stackRect.anchoredPosition = Vector2.zero;

            GameObject titleObj = new GameObject("ColumnTitle", typeof(RectTransform));
            titleObj.transform.SetParent(stack.transform, false);
            ManagerUITheme.AnchorTopStretch(titleObj, 0f, titleHeight);
            ManagerUITheme.BuildLabel(titleObj.transform, title.ToUpperInvariant(), 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            float offset = titleHeight + titleGap;

            foreach ((string label, float value) in attributes)
            {
                offset = BuildAttributeRow(stack.transform, offset, label, value);
            }
        }

        private static float BuildAttributeRow(Transform parent, float topOffset, string label, float value)
        {
            GameObject labelRow = new GameObject($"AttrLabel_{label}", typeof(RectTransform));
            labelRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(labelRow, topOffset, 18f);

            GameObject nameText = new GameObject("Name", typeof(RectTransform));
            nameText.transform.SetParent(labelRow.transform, false);
            RectTransform nameRect = nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(0.8f, 1f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(nameText.transform, label, 15, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);

            GameObject valueText = new GameObject("Value", typeof(RectTransform));
            valueText.transform.SetParent(labelRow.transform, false);
            RectTransform valueRect = valueText.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.8f, 0f);
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(valueText.transform, Mathf.RoundToInt(value).ToString(), 15, ManagerUITheme.RatingColor(value), TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject barRow = new GameObject($"AttrBar_{label}", typeof(RectTransform));
            barRow.transform.SetParent(parent, false);
            ManagerUITheme.AnchorTopStretch(barRow, topOffset + 21f, 7f);
            ManagerUITheme.BuildBar(barRow.transform, value / 100f, ManagerUITheme.RatingColor(value), 7f);

            return topOffset + 42f;
        }

        // Weak foot uses a star rating rather than a raw number - unlike the attribute
        // rows above (which do show their numeric value), weak foot is intentionally
        // kept as a qualitative 1-5 rating instead.
        // Star icons rather than the old "|||--" ASCII bars - relies on the caller
        // assigning weakFootStarSpriteAsset to the label's spriteAsset (star-empty is
        // wired as its fallback, so both glyphs resolve from a single sprite tag).
        private static string BuildFootRating(float rawValue)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(rawValue / 20f), 1, 5);
            const string filledTag = "<sprite name=\"star-filled\">";
            const string emptyTag = "<sprite name=\"star-empty\">";

            // <sprite> has no "size" attribute in TMP - an earlier attempt at
            // size=60% directly on the tag silently failed to parse and printed the
            // tag text literally (confirmed live). <size=X%>...</size> is the
            // real, documented way to scale inline content.
            // <voffset> nudges the sprite block onto the surrounding text's visual
            // center. Three earlier attempts (-0.15em, -0.06em, -0.02em) were each eyeballed
            // off zoomed screenshots and still read as low - the star sprite's own artwork
            // sits well below its reported baseline, so baseline-matching was never going
            // to be enough. This value is derived, not eyeballed: queried
            // TMP_TextInfo.characterInfo live for 'o' (a plain x-height glyph, unlike the
            // ascenders 'k'/'F' checked earlier) vs the star sprite's own bounds at the old
            // -0.02em - centers were 1.31 and -5.18 respectively, a 6.49-unit gap at
            // fontSize 21 (6.49/21 ≈ 0.31em), added on top of the old -0.02em.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(" <voffset=0.29em><size=60%>");
            for (int i = 0; i < filled; i++) sb.Append(filledTag);
            for (int i = filled; i < 5; i++) sb.Append(emptyTag);
            sb.Append("</size></voffset>");
            return sb.ToString();
        }
    }
}
