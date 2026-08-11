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

        // --- Grid rows (Pos/Player/OVR/Fit/Rating) - used by the Squad screen (clickable,
        // reaches Player Detail) and Matchday Prep's read-only opponent scout list
        // (onRowClicked null -> no Button at all, purely informational). Column-fraction
        // technique mirrored from LeagueTableView.BuildRow, since both lists share the
        // same "fixed columns across the row's own width" grid shape.
        //
        // FIT column added session 11 (backlog item 3) - it used to be plain text
        // concatenated onto the end of the Player name (see BuildPlayerGridRow's old
        // badgeSuffix-only signature), so its horizontal position drifted with every
        // name's length instead of lining up in a clean column. Role badges (captain/
        // vice/etc.) stay inline with the name via badgeSuffix - only FIT/injury-return
        // status, the specific thing flagged as misaligned, moved to its own column.
        private static readonly float[] GridColumnFractions = { 0.08f, 0.38f, 0.14f, 0.12f, 0.28f };
        private static readonly string[] GridColumnHeaders = { "POS", "PLAYER", "OVR", "FIT", "RATING" };

        public void AddGridHeaderRow()
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildGridHeaderRow());
        }

        // badgeSuffix is appended straight to the player name cell as TMP rich text (e.g.
        // role badges like " <color=...>C VC</color>" - see ManagerPrototypeController.
        // BuildRoleBadgeSuffix) - empty by default so callers that don't care about roles
        // are unaffected. fitText is rendered in its own dedicated FIT column instead
        // (see ManagerPrototypeController.BuildFitnessBadgeSuffix) - kept separate from
        // badgeSuffix specifically so it lines up in a real column rather than drifting
        // with each player name's length.
        public void AddPlayerGridRow(PlayerAgent player, string position, int displayRating, float ratingPercent, Action<PlayerAgent> onRowClicked, string badgeSuffix = "", bool isInjured = false, string fitText = "")
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildPlayerGridRow(player, position, displayRating, ratingPercent, onRowClicked, badgeSuffix, isInjured, fitText));
        }

        // Generic N-column grid row/header - unlike the fixed Pos/Player/OVR/Rating grid
        // above, this takes arbitrary column text/widths, for callers whose columns
        // don't match that shape (e.g. the Scouting screen's Name/Pos/Age/Club/OVR/
        // Potential/Status). columnFractions must sum to 1 and match cellTexts/headers
        // in length. Real columns, not one concatenated label - the same "stat columns
        // didn't actually align" fix already applied to the Tactics screen's dropdown
        // options (session 7).
        // onColumnClicked (optional) makes every header cell clickable, passing its
        // column index back to the caller - the caller owns sort state/logic entirely,
        // this just renders the click target and the active-column indicator.
        // activeSortColumn/sortDescending draw a plain "v"/"^" suffix (not a Unicode
        // arrow glyph - Oswald SDF has no symbol glyphs at all, same reason the Tactics
        // Board's formation dropdown uses a plain "v") on whichever column is currently
        // sorted, in the sort's actual direction.
        public void AddCustomGridHeaderRow(string[] headers, float[] columnFractions, Action<int> onColumnClicked = null, int activeSortColumn = -1, bool sortDescending = false)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildCustomGridHeaderRow(headers, columnFractions, onColumnClicked, activeSortColumn, sortDescending));
        }

        // onNameClicked (optional) makes column 0 (always the player's name by
        // convention across every caller) its own independent click target - e.g.
        // "click a Transfer/Scouting target's name to see full stats" without that
        // click also firing the rest of the row's onRowClicked (bid/scout/sell). The
        // name cell renders on top of the row's own full-row Button as a later-added
        // child, so it naturally intercepts clicks within its own bounds - Unity's
        // event system resolves a click to the topmost raycast target, not both.
        public void AddCustomGridRow(PlayerAgent player, string[] cellTexts, float[] columnFractions, Action<PlayerAgent> onRowClicked, Action<PlayerAgent> onNameClicked = null)
        {
            EnsureLayoutComponents();
            spawnedRows.Add(BuildCustomGridRow(player, cellTexts, columnFractions, onRowClicked, onNameClicked));
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

        private GameObject BuildPlayerGridRow(PlayerAgent player, string position, int displayRating, float ratingPercent, Action<PlayerAgent> onRowClicked, string badgeSuffix = "", bool isInjured = false, string fitText = "")
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

            // Player - a fixed-width icon gutter is always reserved to the left of the
            // name (whether or not this particular row is injured) so columns don't
            // visibly shift between injured and healthy rows; the icon itself is just
            // toggled inactive when not needed.
            const float injuryIconSize = 18f;
            GameObject nameCell = new GameObject("Cell", typeof(RectTransform));
            nameCell.transform.SetParent(row.transform, false);
            RectTransform nameCellRect = nameCell.GetComponent<RectTransform>();
            nameCellRect.anchorMin = new Vector2(x, 0f);
            nameCellRect.anchorMax = new Vector2(x + GridColumnFractions[1], 1f);
            nameCellRect.offsetMin = new Vector2(10f, 0f);
            nameCellRect.offsetMax = new Vector2(-10f, 0f);

            GameObject injuryIcon = ManagerUITheme.BuildInjuryCrossIcon(nameCell.transform, injuryIconSize);
            RectTransform injuryIconRect = injuryIcon.GetComponent<RectTransform>();
            injuryIconRect.anchorMin = new Vector2(0f, 0.5f);
            injuryIconRect.anchorMax = new Vector2(0f, 0.5f);
            injuryIconRect.pivot = new Vector2(0f, 0.5f);
            injuryIconRect.anchoredPosition = Vector2.zero;
            injuryIcon.SetActive(isInjured);

            GameObject nameLabelObj = new GameObject("Label", typeof(RectTransform));
            nameLabelObj.transform.SetParent(nameCell.transform, false);
            RectTransform nameLabelRect = nameLabelObj.GetComponent<RectTransform>();
            nameLabelRect.anchorMin = new Vector2(0f, 0f);
            nameLabelRect.anchorMax = new Vector2(1f, 1f);
            nameLabelRect.offsetMin = new Vector2(injuryIconSize + 8f, 0f);
            nameLabelRect.offsetMax = Vector2.zero;
            ManagerUITheme.BuildLabel(nameLabelObj.transform, player.Name + badgeSuffix, fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);

            x += GridColumnFractions[1];

            // OVR
            BuildGridCell(row.transform, x, GridColumnFractions[2], displayRating.ToString(), fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            x += GridColumnFractions[2];

            // Fit - its own column (session 11, backlog item 3) instead of text tacked
            // onto the end of the name, which drifted with each name's length.
            BuildGridCell(row.transform, x, GridColumnFractions[3], fitText, fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            x += GridColumnFractions[3];

            // Rating bar
            GameObject barContainer = new GameObject("RatingBar", typeof(RectTransform));
            barContainer.transform.SetParent(row.transform, false);
            RectTransform barRect = barContainer.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(x, 0.5f);
            barRect.anchorMax = new Vector2(x + GridColumnFractions[4], 0.5f);
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

        private GameObject BuildCustomGridHeaderRow(string[] headers, float[] columnFractions, Action<int> onColumnClicked, int activeSortColumn, bool sortDescending)
        {
            GameObject row = new GameObject("CustomGridHeader", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = headerHeight;
            layoutElement.flexibleWidth = 1f;

            float x = 0f;
            for (int i = 0; i < headers.Length; i++)
            {
                bool isActiveSortColumn = i == activeSortColumn;
                string text = isActiveSortColumn ? $"{headers[i]} {(sortDescending ? "v" : "^")}" : headers[i];
                Color color = isActiveSortColumn ? ManagerUITheme.Accent : ManagerUITheme.TextMuted;

                if (onColumnClicked != null)
                {
                    BuildClickableHeaderCell(row.transform, x, columnFractions[i], text, color, i, onColumnClicked);
                }
                else
                {
                    BuildGridCell(row.transform, x, columnFractions[i], text, 12, color, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                }

                x += columnFractions[i];
            }

            return row;
        }

        // Same rect/inset shape as BuildGridCell, but the outer object itself carries
        // the Image/Button so the whole column header (not just the text) is a click
        // target - matches how BuildCustomGridRow makes the whole row clickable rather
        // than just its label.
        private void BuildClickableHeaderCell(Transform parent, float x, float widthFraction, string text, Color color, int columnIndex, Action<int> onColumnClicked)
        {
            GameObject cell = new GameObject($"HeaderCell_{columnIndex}", typeof(RectTransform), typeof(Image), typeof(Button));
            cell.transform.SetParent(parent, false);

            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(x, 0f);
            cellRect.anchorMax = new Vector2(x + widthFraction, 1f);
            cellRect.offsetMin = Vector2.zero;
            cellRect.offsetMax = Vector2.zero;

            Image background = cell.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);

            Button button = cell.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onColumnClicked(columnIndex));

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(cell.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-10f, 0f);

            ManagerUITheme.BuildLabel(labelObj.transform, text, 12, color, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        }

        private GameObject BuildCustomGridRow(PlayerAgent player, string[] cellTexts, float[] columnFractions, Action<PlayerAgent> onRowClicked, Action<PlayerAgent> onNameClicked)
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
                if (i == 0 && onNameClicked != null)
                {
                    BuildClickableNameCell(row.transform, x, columnFractions[i], cellTexts[i], player, onNameClicked);
                }
                else
                {
                    BuildGridCell(row.transform, x, columnFractions[i], cellTexts[i], fontSize, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
                }

                x += columnFractions[i];
            }

            return row;
        }

        // Accent-colored (matching the established "this cell is special/interactive"
        // convention already used for the Pos cell in BuildPlayerGridRow) rather than
        // plain text, so it visually reads as its own click target distinct from the
        // rest of the row.
        private void BuildClickableNameCell(Transform parent, float x, float widthFraction, string text, PlayerAgent player, Action<PlayerAgent> onNameClicked)
        {
            GameObject cell = new GameObject("NameCell", typeof(RectTransform), typeof(Image), typeof(Button));
            cell.transform.SetParent(parent, false);

            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(x, 0f);
            cellRect.anchorMax = new Vector2(x + widthFraction, 1f);
            cellRect.offsetMin = Vector2.zero;
            cellRect.offsetMax = Vector2.zero;

            Image background = cell.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);

            Button button = cell.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onNameClicked(player));

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(cell.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-10f, 0f);

            ManagerUITheme.BuildLabel(labelObj.transform, text, fontSize, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
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
