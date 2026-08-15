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
        // --- Trophy Room (career-arc addition, session 8, Phase 4): season-by-season
        // history - final position, prize money, board boost, champion highlight. Same
        // code-built-panel/scroll-view pattern as Squad/Scouting/Transfers, but rows are
        // plain labels built directly (SquadListView is PlayerAgent-typed, not
        // applicable to SeasonRecord) rather than via that shared component. ---

        private bool trophyRoomChromeBuilt;
        private GameObject trophyRoomPanel;
        private RectTransform trophyRoomContentContainer;
        private readonly List<GameObject> spawnedTrophyRoomRows = new();

        // Career screen tabs (backlog item 2, session 11): 0 = Trophies (the original
        // Trophy Room content, unchanged), 1 = Record (season-by-season W/D/L/Points),
        // 2 = Finance (lifetime transfer spend/income + prize money/board boost totals).
        private int careerTab;
        private Button careerTrophiesTabButton;
        private Button careerRecordTabButton;
        private Button careerFinanceTabButton;

        public void OnOpenTrophyRoomClicked()
        {
            if (!trophyRoomChromeBuilt)
            {
                BuildTrophyRoomChrome();
                trophyRoomChromeBuilt = true;
            }

            careerTab = 0;

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(true);

            RefreshTrophyRoomUI();
        }

        public void OnTrophyRoomBackClicked()
        {
            if (trophyRoomPanel != null) trophyRoomPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void OnCareerTrophiesTabClicked()
        {
            careerTab = 0;
            RefreshTrophyRoomUI();
        }

        private void OnCareerRecordTabClicked()
        {
            careerTab = 1;
            RefreshTrophyRoomUI();
        }

        private void OnCareerFinanceTabClicked()
        {
            careerTab = 2;
            RefreshTrophyRoomUI();
        }

        private void BuildTrophyRoomChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            trophyRoomPanel = new GameObject("TrophyRoomPanel", typeof(RectTransform));
            trophyRoomPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = trophyRoomPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(trophyRoomPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(trophyRoomPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "CAREER", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnTrophyRoomBackClicked);

            // Three tabs (same BUY/SELL-style pattern as Transfer Market) sharing the one
            // scroll content container below rather than three separate ScrollRects -
            // RefreshTrophyRoomUI branches on careerTab to decide what rows go into it.
            careerFinanceTabButton = ManagerUITheme.BuildButton(header.transform, "FINANCE", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(careerFinanceTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            careerFinanceTabButton.onClick.AddListener(OnCareerFinanceTabClicked);

            careerRecordTabButton = ManagerUITheme.BuildButton(header.transform, "RECORD", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(careerRecordTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-406f, -27f), new Vector2(120f, 36f));
            careerRecordTabButton.onClick.AddListener(OnCareerRecordTabClicked);

            careerTrophiesTabButton = ManagerUITheme.BuildButton(header.transform, "TROPHIES", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(careerTrophiesTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-536f, -27f), new Vector2(120f, 36f));
            careerTrophiesTabButton.onClick.AddListener(OnCareerTrophiesTabClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("TrophyScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(trophyRoomPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(sideMargin, 40f);
            scrollViewRect.offsetMax = new Vector2(-sideMargin, -(headerHeight + 40f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(viewportObj.transform, false);
            trophyRoomContentContainer = contentObj.GetComponent<RectTransform>();
            trophyRoomContentContainer.anchorMin = new Vector2(0f, 1f);
            trophyRoomContentContainer.anchorMax = new Vector2(1f, 1f);
            trophyRoomContentContainer.pivot = new Vector2(0.5f, 1f);
            trophyRoomContentContainer.anchoredPosition = Vector2.zero;
            trophyRoomContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 4f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = trophyRoomContentContainer;
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

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomPanel.transform));
        }

        private void RefreshTrophyRoomUI()
        {
            if (trophyRoomContentContainer == null)
            {
                return;
            }

            if (careerTrophiesTabButton != null && careerTrophiesTabButton.TryGetComponent(out Image trophiesImage))
            {
                trophiesImage.color = careerTab == 0 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerTrophiesTabButton, "TROPHIES", careerTab == 0 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (careerRecordTabButton != null && careerRecordTabButton.TryGetComponent(out Image recordImage))
            {
                recordImage.color = careerTab == 1 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerRecordTabButton, "RECORD", careerTab == 1 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (careerFinanceTabButton != null && careerFinanceTabButton.TryGetComponent(out Image financeImage))
            {
                financeImage.color = careerTab == 2 ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(careerFinanceTabButton, "FINANCE", careerTab == 2 ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            foreach (GameObject row in spawnedTrophyRoomRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedTrophyRoomRows.Clear();

            if (careerTab == 0)
            {
                RefreshCareerTrophiesTab();
            }
            else if (careerTab == 1)
            {
                RefreshCareerRecordTab();
            }
            else
            {
                RefreshCareerFinanceTab();
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(trophyRoomContentContainer));
        }

        private void RefreshCareerTrophiesTab()
        {
            spawnedTrophyRoomRows.Add(BuildTrophyRoomHeaderRow());

            if (careerHistory.Records.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(trophyRoomContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "No seasons completed yet - finish your first season to start the history.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedTrophyRoomRows.Add(emptyObj);
                return;
            }

            // Most recent season first.
            for (int i = careerHistory.Records.Count - 1; i >= 0; i--)
            {
                spawnedTrophyRoomRows.Add(BuildTrophyRoomRow(careerHistory.Records[i]));
            }
        }

        private static readonly float[] CareerRecordColumnFractions = { 0.16f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f, 0.14f };

        private void RefreshCareerRecordTab()
        {
            spawnedTrophyRoomRows.Add(BuildCareerRecordHeaderRow());

            // Live in-progress row (backlog item, session 12, Thomas: Record should show
            // the current season live, not just completed ones). SeasonRecord/
            // careerHistory only ever gets a row once ApplySeasonEndRewards runs at
            // rollover - mid-season there was nothing here for the season actually being
            // played. Sourced straight from playableTable, the same live table the Hub's
            // own league position already reads from - no new tracking needed.
            GameObject liveRow = BuildLiveCareerRecordRow();
            if (liveRow != null)
            {
                spawnedTrophyRoomRows.Add(liveRow);
            }

            if (careerHistory.Records.Count == 0)
            {
                if (liveRow == null)
                {
                    GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                    emptyObj.transform.SetParent(trophyRoomContentContainer, false);
                    emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                    ManagerUITheme.BuildLabel(emptyObj.transform, "No seasons completed yet - finish your first season to start the history.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                    spawnedTrophyRoomRows.Add(emptyObj);
                }

                return;
            }

            for (int i = careerHistory.Records.Count - 1; i >= 0; i--)
            {
                spawnedTrophyRoomRows.Add(BuildCareerRecordRow(careerHistory.Records[i]));
            }
        }

        // Null if there's genuinely no live table yet (e.g. before a career's first
        // EnsureTeam call) - defensive, shouldn't happen in practice by the time this
        // screen is reachable at all.
        private GameObject BuildLiveCareerRecordRow()
        {
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            List<LeagueTable.Entry> sorted = playableTable.Sorted();
            int position = sorted.FindIndex(e => e.TeamId == managedTeamId) + 1;

            if (position <= 0)
            {
                return null;
            }

            LeagueTable.Entry live = sorted[position - 1];

            GameObject row = new GameObject($"RecordSeason_{currentSeason}_Live", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.08f);

            int goalDifference = live.GoalsFor - live.GoalsAgainst;
            string[] values =
            {
            $"Season {currentSeason} (live)",
            $"{position}{GetOrdinalSuffix(position)}",
            live.Points.ToString(),
            live.Wins.ToString(),
            live.Draws.ToString(),
            live.Losses.ToString(),
            (goalDifference > 0 ? "+" : "") + goalDifference
        };

            float x = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildCareerRecordHeaderRow()
        {
            GameObject row = new GameObject("RecordHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 30f;

            string[] headers = { "SEASON", "POSITION", "PTS", "W", "D", "L", "GD" };
            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                GameObject cell = new GameObject($"Header_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, headers[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildCareerRecordRow(SeasonRecord record)
        {
            GameObject row = new GameObject($"RecordSeason_{record.Season}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = record.IsChampion ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            // Goal difference isn't stored on SeasonRecord itself (GF/GA weren't part of
            // the original ask) - Points/W/D/L already carry the shape Thomas actually
            // asked for, so a GD column not being derivable exactly isn't worth widening
            // SeasonRecord for. Shown as "-" rather than a wrong number.
            string[] values =
            {
            $"Season {record.Season}",
            $"{record.FinalPosition}{GetOrdinalSuffix(record.FinalPosition)}",
            record.Points.ToString(),
            record.Wins.ToString(),
            record.Draws.ToString(),
            record.Losses.ToString(),
            "-"
        };

            Color textColor = record.IsChampion ? ManagerUITheme.Accent : ManagerUITheme.TextBody;
            FontStyles style = record.IsChampion ? FontStyles.Bold : FontStyles.Normal;

            float x = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + CareerRecordColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, textColor, TextAlignmentOptions.MidlineLeft, style);
                x += CareerRecordColumnFractions[i];
            }

            return row;
        }

        private void RefreshCareerFinanceTab()
        {
            float totalSpend = finance.GetTotalTransferSpend(managedTeamName);
            float totalIncome = finance.GetTotalTransferIncome(managedTeamName);

            float totalPrizeMoney = 0f;
            float totalBoardBoost = 0f;
            foreach (SeasonRecord record in careerHistory.Records)
            {
                totalPrizeMoney += record.PrizeMoney;
                totalBoardBoost += record.BoardBoost;
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            float currentBudget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

            (string label, string value, bool emphasize)[] rows =
            {
            ("CURRENT BUDGET", $"£{currentBudget:F1}m", true),
            ("TOTAL TRANSFER SPEND", $"£{totalSpend:F1}m", false),
            ("TOTAL TRANSFER INCOME", $"£{totalIncome:F1}m", false),
            ("NET TRANSFER SPEND", $"£{(totalSpend - totalIncome):F1}m", false),
            ("TOTAL PRIZE MONEY", $"£{totalPrizeMoney:F1}m", false),
            ("TOTAL BOARD BOOST", $"£{totalBoardBoost:F1}m", false),
        };

            foreach (var (label, value, emphasize) in rows)
            {
                spawnedTrophyRoomRows.Add(BuildCareerFinanceRow(label, value, emphasize));
            }
        }

        private GameObject BuildCareerFinanceRow(string label, string value, bool emphasize)
        {
            GameObject row = new GameObject($"Finance_{label}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 52f;
            row.GetComponent<Image>().color = emphasize ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            GameObject labelCell = new GameObject("Label", typeof(RectTransform));
            labelCell.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelCell.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.6f, 1f);
            labelRect.offsetMin = new Vector2(20f, 0f);
            labelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(labelCell.transform, label, 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject valueCell = new GameObject("Value", typeof(RectTransform));
            valueCell.transform.SetParent(row.transform, false);
            RectTransform valueRect = valueCell.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.6f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = new Vector2(-20f, 0f);
            ManagerUITheme.BuildLabel(valueCell.transform, value, 20, emphasize ? ManagerUITheme.Accent : ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            return row;
        }

        private static readonly float[] TrophyRoomColumnFractions = { 0.14f, 0.20f, 0.22f, 0.24f, 0.20f };

        private GameObject BuildTrophyRoomHeaderRow()
        {
            GameObject row = new GameObject("TrophyHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 30f;

            string[] headers = { "SEASON", "POSITION", "PRIZE MONEY", "BOARD BOOST", "" };
            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                GameObject cell = new GameObject($"Header_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + TrophyRoomColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, headers[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += TrophyRoomColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildTrophyRoomRow(SeasonRecord record)
        {
            GameObject row = new GameObject($"Season_{record.Season}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(trophyRoomContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = 44f;
            row.GetComponent<Image>().color = record.IsChampion ? new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.12f) : ManagerUITheme.CardNeutralAlt;

            string[] values =
            {
            $"Season {record.Season}",
            $"{record.FinalPosition}{GetOrdinalSuffix(record.FinalPosition)}",
            $"£{record.PrizeMoney:F1}m",
            record.BoardBoost > 0f ? $"£{record.BoardBoost:F1}m" : "-",
            record.IsChampion ? "CHAMPIONS" : ""
        };

            Color textColor = record.IsChampion ? ManagerUITheme.Accent : ManagerUITheme.TextBody;
            FontStyles style = record.IsChampion ? FontStyles.Bold : FontStyles.Normal;

            float x = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + TrophyRoomColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(cell.transform, values[i], 16, textColor, TextAlignmentOptions.MidlineLeft, style);
                x += TrophyRoomColumnFractions[i];
            }

            return row;
        }

    }
}
