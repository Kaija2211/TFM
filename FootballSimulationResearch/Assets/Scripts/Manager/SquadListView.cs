using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sim;

namespace Manager
{
    // Runtime-generated clickable list: one row per player, built and laid out entirely
    // in code (no hand-authored prefab). Reused by squad browsing, player inspect entry,
    // and both substitution pickers, so none of them require Prev/Next cycling to reach
    // a specific player - click their row and you're there. Also supports non-clickable
    // section header rows (e.g. "Starting XI" / "Bench") interleaved with player rows.
    public class SquadListView : MonoBehaviour
    {
        // Assign to the "Content" RectTransform of a standard Unity Scroll View
        // (GameObject > UI > Scroll View in the Editor). Rows are laid out via a
        // VerticalLayoutGroup added automatically the first time a row is built.
        [SerializeField] private RectTransform rowContainer;
        [SerializeField] private float rowHeight = 40f;
        [SerializeField] private float headerHeight = 28f;
        [SerializeField] private int fontSize = 20;

        private readonly List<GameObject> spawnedRows = new();

        // For instances added via AddComponent at runtime (no Editor Inspector wiring
        // available) rather than hand-placed in the scene - e.g. the Squad screen and
        // Matchday Prep's opponent scout list, both built entirely in code the same way
        // the Tactics Board and Match Events panels are.
        public void Bind(RectTransform contentRect)
        {
            rowContainer = contentRect;
        }

        // Flat list, no section headers - used by the substitution pickers (Starting
        // XI-only or Bench-only, no grouping needed).
        public void Populate(
            IReadOnlyList<PlayerAgent> players,
            Func<PlayerAgent, string> labelBuilder,
            Action<PlayerAgent> onRowClicked,
            PlayerAgent highlightedPlayer = null,
            Func<PlayerAgent, float> ratingSelector = null)
        {
            Clear();

            if (rowContainer == null || players == null)
            {
                return;
            }

            EnsureLayoutComponents();

            foreach (PlayerAgent player in players)
            {
                AddPlayerRow(player, labelBuilder(player), onRowClicked, ratingSelector?.Invoke(player) ?? -1f, player == highlightedPlayer);
            }
        }

        // ratingPercent in [0,1] draws a small bar next to the row; pass a negative value
        // to omit the bar entirely.
        public void AddPlayerRow(PlayerAgent player, string label, Action<PlayerAgent> onRowClicked, float ratingPercent = -1f, bool highlighted = false)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildPlayerRow(player, label, onRowClicked, ratingPercent, highlighted));
        }

        public void AddSectionHeader(string label)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildSectionHeader(label));
        }

        // --- Grid rows (Pos/Player/OVR/Rating) - used by the Squad screen (clickable,
        // reaches Player Detail) and Matchday Prep's read-only opponent scout list
        // (onRowClicked null -> no Button at all, purely informational). Column-fraction
        // technique mirrored from LeagueTableView.BuildRow, since both lists share the
        // same "fixed columns across the row's own width" grid shape.
        private static readonly float[] GridColumnFractions = { 0.08f, 0.50f, 0.14f, 0.28f };
        private static readonly string[] GridColumnHeaders = { "POS", "PLAYER", "OVR", "RATING" };

        public void AddGridHeaderRow()
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildGridHeaderRow());
        }

        // badgeSuffix is appended straight to the player name cell as TMP rich text (e.g.
        // role badges like " <color=...>C VC</color>" - see ManagerPrototypeController.
        // BuildRoleBadgeSuffix) - empty by default so callers that don't care about roles
        // are unaffected.
        public void AddPlayerGridRow(PlayerAgent player, string position, int displayRating, float ratingPercent, Action<PlayerAgent> onRowClicked, string badgeSuffix = "")
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildPlayerGridRow(player, position, displayRating, ratingPercent, onRowClicked, badgeSuffix));
        }

        // Generic N-column grid row/header - unlike the fixed Pos/Player/OVR/Rating grid
        // above, this takes arbitrary column text/widths, for callers whose columns
        // don't match that shape (e.g. the Scouting screen's Name/Pos/Age/Club/OVR/
        // Potential/Status). columnFractions must sum to 1 and match cellTexts/headers
        // in length. Real columns, not one concatenated label - the same "stat columns
        // didn't actually align" fix already applied to the Tactics screen's dropdown
        // options (session 7).
        public void AddCustomGridHeaderRow(string[] headers, float[] columnFractions)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildCustomGridHeaderRow(headers, columnFractions));
        }

        public void AddCustomGridRow(PlayerAgent player, string[] cellTexts, float[] columnFractions, Action<PlayerAgent> onRowClicked)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildCustomGridRow(player, cellTexts, columnFractions, onRowClicked));
        }

        private GameObject BuildGridHeaderRow()
        {
            GameObject row = new GameObject("GridHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = headerHeight;
            layoutElement.flexibleWidth = 1f;

            float x = 0f;

            for (int i = 0; i < GridColumnHeaders.Length; i++)
            {
                GameObject cell = new GameObject($"HeaderCell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);

                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + GridColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(10f, 0f);
                cellRect.offsetMax = new Vector2(-10f, 0f);

                ManagerUITheme.BuildLabel(cell.transform, GridColumnHeaders[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

                x += GridColumnFractions[i];
            }

            return row;
        }

        private GameObject BuildPlayerGridRow(PlayerAgent player, string position, int displayRating, float ratingPercent, Action<PlayerAgent> onRowClicked, string badgeSuffix = "")
        {
            bool clickable = onRowClicked != null;

            GameObject row = clickable
                ? new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement))
                : new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = rowHeight;
            layoutElement.flexibleWidth = 1f;

            // Transparent row background with a thin bottom border line, matching the
            // mockup's border-bottom-separated rows rather than the alternating solid
            // background used by the flat single-column rows above.
            Image background = row.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);

            if (clickable && row.TryGetComponent(out Button button))
            {
                button.targetGraphic = background;
                button.onClick.AddListener(() => onRowClicked(player));
            }

            GameObject borderObj = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderObj.transform.SetParent(row.transform, false);
            RectTransform borderRect = borderObj.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 0f);
            borderRect.pivot = new Vector2(0.5f, 0f);
            borderRect.sizeDelta = new Vector2(0f, 1f);
            borderRect.anchoredPosition = Vector2.zero;
            borderObj.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            float x = 0f;

            // Pos
            BuildGridCell(row.transform, x, GridColumnFractions[0], position, fontSize, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            x += GridColumnFractions[0];

            // Player
            BuildGridCell(row.transform, x, GridColumnFractions[1], player.Name + badgeSuffix, fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            x += GridColumnFractions[1];

            // OVR
            BuildGridCell(row.transform, x, GridColumnFractions[2], displayRating.ToString(), fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            x += GridColumnFractions[2];

            // Rating bar
            GameObject barContainer = new GameObject("RatingBar", typeof(RectTransform));
            barContainer.transform.SetParent(row.transform, false);
            RectTransform barRect = barContainer.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(x, 0.5f);
            barRect.anchorMax = new Vector2(x + GridColumnFractions[3], 0.5f);
            barRect.offsetMin = new Vector2(10f, 0f);
            barRect.offsetMax = new Vector2(-10f, 0f);
            ManagerUITheme.BuildBar(barContainer.transform, ratingPercent, ManagerUITheme.Accent, height: 7f);

            return row;
        }

        private void BuildGridCell(Transform parent, float x, float widthFraction, string text, int size, Color color, TextAlignmentOptions alignment, FontStyles style)
        {
            GameObject cell = new GameObject("Cell", typeof(RectTransform));
            cell.transform.SetParent(parent, false);

            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(x, 0f);
            cellRect.anchorMax = new Vector2(x + widthFraction, 1f);
            cellRect.offsetMin = new Vector2(10f, 0f);
            cellRect.offsetMax = new Vector2(-10f, 0f);

            ManagerUITheme.BuildLabel(cell.transform, text, size, color, alignment, style);
        }

        private GameObject BuildCustomGridHeaderRow(string[] headers, float[] columnFractions)
        {
            GameObject row = new GameObject("CustomGridHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = headerHeight;
            layoutElement.flexibleWidth = 1f;

            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                BuildGridCell(row.transform, x, columnFractions[i], headers[i], 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += columnFractions[i];
            }

            return row;
        }

        private GameObject BuildCustomGridRow(PlayerAgent player, string[] cellTexts, float[] columnFractions, Action<PlayerAgent> onRowClicked)
        {
            bool clickable = onRowClicked != null;

            GameObject row = clickable
                ? new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement))
                : new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = rowHeight;
            layoutElement.flexibleWidth = 1f;

            Image background = row.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);

            if (clickable && row.TryGetComponent(out Button button))
            {
                button.targetGraphic = background;
                button.onClick.AddListener(() => onRowClicked(player));
            }

            GameObject borderObj = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderObj.transform.SetParent(row.transform, false);
            RectTransform borderRect = borderObj.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 0f);
            borderRect.pivot = new Vector2(0.5f, 0f);
            borderRect.sizeDelta = new Vector2(0f, 1f);
            borderRect.anchoredPosition = Vector2.zero;
            borderObj.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            float x = 0f;
            for (int i = 0; i < cellTexts.Length; i++)
            {
                BuildGridCell(row.transform, x, columnFractions[i], cellTexts[i], fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
                x += columnFractions[i];
            }

            return row;
        }

        public void Clear()
        {
            foreach (GameObject row in spawnedRows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }

            spawnedRows.Clear();
        }

        private void EnsureLayoutComponents()
        {
            if (rowContainer == null)
            {
                return;
            }

            if (!rowContainer.TryGetComponent(out VerticalLayoutGroup layout))
            {
                layout = rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 2f;

            if (!rowContainer.TryGetComponent(out ContentSizeFitter fitter))
            {
                fitter = rowContainer.gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private GameObject BuildSectionHeader(string label)
        {
            GameObject header = new GameObject($"Header_{label}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            header.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = header.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = headerHeight;
            layoutElement.flexibleWidth = 1f;

            header.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            ManagerUITheme.BuildLabel(
                header.transform,
                label.ToUpperInvariant(),
                14,
                ManagerUITheme.Accent,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold);

            return header;
        }

        private GameObject BuildPlayerRow(PlayerAgent player, string label, Action<PlayerAgent> onRowClicked, float ratingPercent, bool highlighted)
        {
            GameObject row = new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = rowHeight;
            layoutElement.flexibleWidth = 1f;

            Image background = row.GetComponent<Image>();
            background.color = highlighted ? ManagerUITheme.CardNeutral : ManagerUITheme.CardNeutralAlt;

            Button button = row.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onRowClicked(player));

            bool showRating = ratingPercent >= 0f;
            float textRightEdge = showRating ? 0.72f : 1f;

            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(row.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(textRightEdge, 1f);
            textRect.offsetMin = new Vector2(10f, 2f);
            textRect.offsetMax = new Vector2(-6f, -2f);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = fontSize;
            text.color = ManagerUITheme.TextBody;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            // Rows have a fixed height (see rowHeight above) - word-wrapped text would
            // spill into the row below instead of just getting cut off, so force a
            // single line regardless of how narrow the container ends up being.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;

            if (showRating)
            {
                GameObject barContainer = new GameObject("RatingBar", typeof(RectTransform));
                barContainer.transform.SetParent(row.transform, false);

                RectTransform barRect = barContainer.GetComponent<RectTransform>();
                barRect.anchorMin = new Vector2(0.75f, 0f);
                barRect.anchorMax = new Vector2(0.98f, 1f);
                barRect.offsetMin = Vector2.zero;
                barRect.offsetMax = Vector2.zero;

                ManagerUITheme.BuildBar(barContainer.transform, ratingPercent, ManagerUITheme.RatingColor(ratingPercent * 100f));
            }

            return row;
        }
    }
}
