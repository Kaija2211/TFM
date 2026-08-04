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
