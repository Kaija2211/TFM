using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Manager
{
    // Shared palette/type + small runtime UI builders for the "Matchday Manager" reskin.
    // Centralized here so every screen reads the same values instead of duplicating hex
    // colors, and so new/complex layout (bars, labels, buttons) is built consistently in
    // code rather than hand-placed per screen in the Editor.
    public static class ManagerUITheme
    {
        public static readonly Color Background = HexColor("#0b1120");
        public static readonly Color PanelDark = HexColor("#111c2e");
        public static readonly Color Accent = HexColor("#3ddc84");
        public static readonly Color OnAccent = HexColor("#0b1120");
        public static readonly Color CardNeutral = HexColor("#26344a");
        public static readonly Color CardNeutralAlt = HexColor("#1a2333");
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextMuted = HexColor("#7d8ea3");
        public static readonly Color TextDim = HexColor("#5a6572");
        public static readonly Color TextBody = HexColor("#c4ccd6");
        public static readonly Color BarTrack = HexColor("#1e2a3d");
        public static readonly Color BarFillNeutral = HexColor("#445064");
        public static readonly Color Warning = HexColor("#e0a030");
        public static readonly Color Danger = HexColor("#c0392b");
        public static readonly Color Disabled = HexColor("#3a4658");
        public static readonly Color DisabledText = HexColor("#5a6572");

        // Traffic-light coloring for a 0-100 stat: green when strong, amber when
        // mediocre, red when weak.
        public static Color RatingColor(float value)
        {
            if (value >= 75f) return Accent;
            if (value >= 45f) return Warning;
            return Danger;
        }

        private static Color HexColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
        }

        // Anchors an element to the top-center of its parent at a fixed width/height,
        // offset down by `top` pixels. Used for small fixed-count screens (Title, Player
        // Detail) where explicit stacked positions are simpler and more predictable than a
        // layout group.
        public static RectTransform AnchorTopCenter(GameObject go, float top, float width, float height)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = go.AddComponent<RectTransform>();
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        // Anchors an element to stretch the full width of its parent (minus horizontal
        // margin), offset down by `top` pixels, at a fixed height.
        public static RectTransform AnchorTopStretch(GameObject go, float top, float height, float horizontalMargin = 0f)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = go.AddComponent<RectTransform>();
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-horizontalMargin * 2f, height);
            return rect;
        }

        // The recurring "band" motif across the mockups: a lighter PanelDark strip along
        // the top or bottom edge of a screen, separated from the darker body by a thin
        // green accent line. SetAsFirstSibling() so it always renders behind whatever
        // content (buttons, fields) already exists as a sibling in that panel.
        public static GameObject BuildAccentBand(Transform parent, bool topBand, float height, float lineThickness = 3f)
        {
            GameObject band = new GameObject(topBand ? "HeaderBand" : "FooterBand", typeof(RectTransform), typeof(Image));
            band.transform.SetParent(parent, false);
            band.transform.SetAsFirstSibling();

            RectTransform rect = band.GetComponent<RectTransform>();
            float edgeY = topBand ? 1f : 0f;
            rect.anchorMin = new Vector2(0f, edgeY);
            rect.anchorMax = new Vector2(1f, edgeY);
            rect.pivot = new Vector2(0.5f, edgeY);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);

            band.GetComponent<Image>().color = PanelDark;

            GameObject line = new GameObject("AccentLine", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(band.transform, false);

            RectTransform lineRect = line.GetComponent<RectTransform>();
            float lineEdgeY = topBand ? 0f : 1f;
            lineRect.anchorMin = new Vector2(0f, lineEdgeY);
            lineRect.anchorMax = new Vector2(1f, lineEdgeY);
            lineRect.pivot = new Vector2(0.5f, lineEdgeY);
            lineRect.anchoredPosition = Vector2.zero;
            lineRect.sizeDelta = new Vector2(0f, lineThickness);

            line.GetComponent<Image>().color = Accent;

            return band;
        }

        public static void ApplyPanelBackground(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            if (!panel.TryGetComponent(out Image image))
            {
                image = panel.AddComponent<Image>();
            }

            image.color = Background;
        }

        // Thin horizontal bar (dark track + colored fill) sized to pct (0-1). Used for
        // squad rating rows and player attribute breakdowns alike.
        public static RectTransform BuildBar(Transform parent, float pct, Color fillColor, float height = 6f)
        {
            pct = Mathf.Clamp01(pct);

            GameObject track = new GameObject("BarTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);

            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, height);
            trackRect.anchoredPosition = Vector2.zero;
            track.GetComponent<Image>().color = BarTrack;

            GameObject fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);

            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(pct, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = fillColor;

            return trackRect;
        }

        public static TextMeshProUGUI BuildLabel(
            Transform parent,
            string text,
            int fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            FontStyles fontStyle = FontStyles.Normal,
            bool noWrap = true)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = fontStyle;
            label.textWrappingMode = noWrap ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            label.overflowMode = noWrap ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
            label.raycastTarget = false;

            return label;
        }

        public static Button BuildButton(
            Transform parent,
            string label,
            Color background,
            Color textColor,
            int fontSize = 16)
        {
            GameObject buttonObject = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = background;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            BuildLabel(buttonObject.transform, label, fontSize, textColor, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);

            return button;
        }

        // Marks a placeholder button as visibly present but not yet functional (Transfers,
        // Settings, Load Career, Sort/Filter) rather than silently omitting it or faking
        // behavior it doesn't have.
        public static void SetDisabledPlaceholder(Button button, string label, int fontSize = 13)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = false;

            if (button.TryGetComponent(out Image image))
            {
                image.color = Disabled;
            }

            NormalizeButtonLabel(button, $"{label} (Soon)", DisabledText, fontSize);
        }

        // Fresh Editor-created buttons ("Button - TextMeshPro") default to a large TMP font
        // size, which wraps/overflows once restyled with real copy. Every button touched at
        // runtime should go through this so it never depends on whatever size the Editor
        // happened to default to.
        public static void NormalizeButtonLabel(Button button, string text, Color color, int fontSize)
        {
            if (button == null)
            {
                return;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();

            if (label == null)
            {
                return;
            }

            label.text = text;
            label.color = color;
            label.fontSize = fontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
        }
    }
}
