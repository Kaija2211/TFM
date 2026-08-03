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
    // a specific player - click their row and you're there.
    public class SquadListView : MonoBehaviour
    {
        // Assign to the "Content" RectTransform of a standard Unity Scroll View
        // (GameObject > UI > Scroll View in the Editor). Rows are laid out via a
        // VerticalLayoutGroup added automatically the first time Populate runs.
        [SerializeField] private RectTransform rowContainer;
        [SerializeField] private float rowHeight = 40f;
        [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.08f);
        [SerializeField] private Color rowHighlightColor = new Color(1f, 1f, 1f, 0.22f);
        [SerializeField] private int fontSize = 20;

        private readonly List<GameObject> spawnedRows = new();

        public void Populate(
            IReadOnlyList<PlayerAgent> players,
            Func<PlayerAgent, string> labelBuilder,
            Action<PlayerAgent> onRowClicked,
            PlayerAgent highlightedPlayer = null)
        {
            Clear();

            if (rowContainer == null || players == null)
            {
                return;
            }

            EnsureLayoutComponents();

            foreach (PlayerAgent player in players)
            {
                spawnedRows.Add(BuildRow(player, labelBuilder(player), onRowClicked, player == highlightedPlayer));
            }
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

        private GameObject BuildRow(PlayerAgent player, string label, Action<PlayerAgent> onRowClicked, bool highlighted)
        {
            GameObject row = new GameObject($"Row_{player.Name}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = rowHeight;
            layoutElement.flexibleWidth = 1f;

            Image background = row.GetComponent<Image>();
            background.color = highlighted ? rowHighlightColor : rowColor;

            Button button = row.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onRowClicked(player));

            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(row.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 2f);
            textRect.offsetMax = new Vector2(-10f, -2f);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            // Rows have a fixed height (see rowHeight above) - word-wrapped text would
            // spill into the row below instead of just getting cut off, so force a
            // single line regardless of how narrow the container ends up being.
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;

            return row;
        }
    }
}
