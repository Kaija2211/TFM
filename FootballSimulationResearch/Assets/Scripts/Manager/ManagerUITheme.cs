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

        // Far end of the Title/Hub background gradient wash (mockup's
        // linear-gradient(..., #0b1120 40%, #132118 100%)) - a dark green the gradient
        // fades toward, never used as a flat/solid color anywhere else.
        public static readonly Color GradientEnd = HexColor("#132118");

        // Traffic-light coloring for a 0-100 stat: green when strong, amber when
        // mediocre, red when weak.
        public static Color RatingColor(float value)
        {
            if (value >= 75f) return Accent;
            if (value >= 45f) return Warning;
            return Danger;
        }

        // Continuous green->yellow->red gradient for a 0-100 value, rather than
        // RatingColor's hard 3-step bands - playtest backlog (session 14), Thomas's own
        // idea for the Tactics Board pin border: "smoothly shifts warmer as Condition
        // drops, not a separate number." Anchors recalibrated in session 16 - the
        // original 50/100 anchors meant 84 condition still read as solid green in a
        // live playtest; a manager expects concern to show earlier than the midpoint.
        // 100 = pure Accent, 80 = pure Warning, 40 = pure Danger (and below).
        public static Color ConditionGradientColor(float value01to100)
        {
            const float GreenAnchor = 100f;
            const float AmberAnchor = 80f;
            const float RedAnchor = 40f;

            if (value01to100 >= AmberAnchor)
            {
                float t = Mathf.Clamp01((value01to100 - AmberAnchor) / (GreenAnchor - AmberAnchor));
                return Color.Lerp(Warning, Accent, t);
            }

            float tLow = Mathf.Clamp01((value01to100 - RedAnchor) / (AmberAnchor - RedAnchor));
            return Color.Lerp(Danger, Warning, tLow);
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

        // Repositions an already-existing RectTransform using a point anchor (anchorMin
        // == anchorMax == pivot == anchor). Lets already-placed Editor elements (buttons
        // that already exist and work) be positioned precisely from code instead of by
        // hand-dragging - the same failure mode that caused the Matchday Prep tactic
        // buttons to end up hidden behind the opponent squad list.
        public static void SetPointAnchor(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
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

        // Two-segment proportional bar - home team's share fills from the left, the
        // remainder fills from the right in a second color, meeting at the split point.
        // Unlike BuildBar above (a single 0-100% fill against a neutral track), this is
        // for match-stat comparison rows where both teams' numbers matter side by side.
        public static RectTransform BuildSplitBar(Transform parent, float homeShare, Color homeColor, Color awayColor, float height = 6f)
        {
            homeShare = Mathf.Clamp01(homeShare);

            GameObject track = new GameObject("SplitBarTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);

            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, height);
            trackRect.anchoredPosition = Vector2.zero;
            track.GetComponent<Image>().color = BarTrack;

            GameObject homeFill = new GameObject("HomeFill", typeof(RectTransform), typeof(Image));
            homeFill.transform.SetParent(track.transform, false);
            RectTransform homeFillRect = homeFill.GetComponent<RectTransform>();
            homeFillRect.anchorMin = new Vector2(0f, 0f);
            homeFillRect.anchorMax = new Vector2(homeShare, 1f);
            homeFillRect.offsetMin = Vector2.zero;
            homeFillRect.offsetMax = Vector2.zero;
            homeFill.GetComponent<Image>().color = homeColor;

            GameObject awayFill = new GameObject("AwayFill", typeof(RectTransform), typeof(Image));
            awayFill.transform.SetParent(track.transform, false);
            RectTransform awayFillRect = awayFill.GetComponent<RectTransform>();
            awayFillRect.anchorMin = new Vector2(homeShare, 0f);
            awayFillRect.anchorMax = new Vector2(1f, 1f);
            awayFillRect.offsetMin = Vector2.zero;
            awayFillRect.offsetMax = Vector2.zero;
            awayFill.GetComponent<Image>().color = awayColor;

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
            ApplyThemeFont(label);
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

            // Click SFX (backlog item 11, session 11) - every button built through this
            // one shared factory gets it automatically; ManagerPrototypeController.Start
            // wires the same sound onto the handful of Editor-placed buttons that don't
            // go through here.
            button.onClick.AddListener(ManagerAudio.PlayClick);

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

        // Code-built TMP_InputField (session 15, multi-save naming) - every input field
        // in this project up to now was Editor-placed ("aren't worth rebuilding from
        // scratch in code" per the Team Select screen's own comment), but a second,
        // independent field (Save Name, alongside the existing Editor-placed Manager
        // Name field) needed to exist without touching that Editor object at all. Same
        // three-part TMP_InputField anatomy Unity's own UI > Input Field (TMP) menu item
        // generates (background Image + a masked Text Area holding Placeholder/Text),
        // built directly since this project has no Editor-placed field spare to clone.
        public static TMP_InputField BuildInputField(Transform parent, string placeholderText, int fontSize = 18, int characterLimit = 40)
        {
            GameObject fieldObj = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldObj.transform.SetParent(parent, false);

            Image background = fieldObj.GetComponent<Image>();
            background.color = PanelDark;

            GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(fieldObj.transform, false);
            RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(14f, 4f);
            textAreaRect.offsetMax = new Vector2(-14f, -4f);

            TextMeshProUGUI placeholderLabel = BuildLabel(textArea.transform, placeholderText, fontSize, TextDim, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            TextMeshProUGUI textLabel = BuildLabel(textArea.transform, "", fontSize, TextPrimary, TextAlignmentOptions.MidlineLeft);

            TMP_InputField inputField = fieldObj.GetComponent<TMP_InputField>();
            inputField.targetGraphic = background;
            inputField.textViewport = textAreaRect;
            inputField.textComponent = textLabel;
            inputField.placeholder = placeholderLabel;
            inputField.characterLimit = characterLimit;

            return inputField;
        }

        // Shared pitch-pin visual: a bordered circular badge (rating number inside) with a
        // name/position label beneath it. Used by both the interactive Tactics Board pins
        // (accent border, draggable/droppable - caller adds TacticsBoardPlayerCard on the
        // returned object) and Matchday Prep's read-only opponent pitch (danger-red border,
        // no interaction) - the two only differ in size/color/interactivity, not in how the
        // badge itself is built, so this is a pure extraction with no visual change to the
        // existing interactive board.
        public static GameObject BuildPitchPinVisual(
            Transform pitch,
            string objectName,
            Vector2 anchor,
            float circleSize,
            Color borderColor,
            string ratingText,
            int ratingFontSize,
            string labelText,
            int labelFontSize,
            bool showInjuryIcon = false)
        {
            float labelHeight = labelFontSize + 8f;
            float labelWidth = circleSize + 70f;

            GameObject pinObj = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            pinObj.transform.SetParent(pitch, false);

            RectTransform pinRect = pinObj.GetComponent<RectTransform>();
            pinRect.anchorMin = anchor;
            pinRect.anchorMax = anchor;
            pinRect.pivot = new Vector2(0.5f, 0.5f);
            pinRect.anchoredPosition = Vector2.zero;
            pinRect.sizeDelta = new Vector2(labelWidth, circleSize + labelHeight + 6f);

            // Transparent - exists only so interactive pins have a Graphic to raycast
            // against (IDropHandler needs one); read-only pins never raycast at all.
            Image pinImage = pinObj.GetComponent<Image>();
            pinImage.color = new Color(0f, 0f, 0f, 0f);
            pinImage.raycastTarget = false;

            // Two-layer "border" (colored square behind, dark square inset on top) - a
            // stand-in for the mockup's colored circle ring, since true circles need a
            // sprite this project doesn't have (same flat-rectangles-only constraint as
            // the pitch markings).
            GameObject badgeBorderObj = new GameObject("BadgeBorder", typeof(RectTransform), typeof(Image));
            badgeBorderObj.transform.SetParent(pinObj.transform, false);
            RectTransform badgeBorderRect = badgeBorderObj.GetComponent<RectTransform>();
            badgeBorderRect.anchorMin = new Vector2(0.5f, 1f);
            badgeBorderRect.anchorMax = new Vector2(0.5f, 1f);
            badgeBorderRect.pivot = new Vector2(0.5f, 1f);
            badgeBorderRect.anchoredPosition = Vector2.zero;
            badgeBorderRect.sizeDelta = new Vector2(circleSize, circleSize);
            badgeBorderObj.GetComponent<Image>().color = borderColor;

            GameObject badgeObj = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeObj.transform.SetParent(badgeBorderObj.transform, false);
            RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.zero;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.offsetMin = new Vector2(2f, 2f);
            badgeRect.offsetMax = new Vector2(-2f, -2f);
            badgeObj.GetComponent<Image>().color = CardNeutralAlt;
            BuildLabel(badgeObj.transform, ratingText, ratingFontSize, TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            // Overlaps the badge's bottom-right corner slightly (a small outward
            // overhang, like a notification badge) - matches where Thomas circled it on
            // the design mockup's own "69" square example, not literally flush inside
            // the corner.
            if (showInjuryIcon)
            {
                GameObject injuryIcon = BuildInjuryCrossIcon(badgeBorderObj.transform, circleSize * 0.4f);
                injuryIcon.GetComponent<RectTransform>().anchoredPosition = new Vector2(6f, -6f);
            }

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(pinObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(labelWidth, labelHeight);
            BuildLabel(labelObj.transform, labelText, labelFontSize, TextPrimary, TextAlignmentOptions.Center);

            return pinObj;
        }

        // Injury cross badge - a small red square (Danger, matching the design mockup's
        // own #c0392b exactly) with a white medical-cross plus mark, built from two
        // crossed rectangles rather than an actual sprite asset (same flat-rectangles-
        // only constraint as the pin border above - no rounded corners either, for the
        // same reason). anchor/pivot both (1,0) so callers get bottom-right placement
        // for free by just setting anchoredPosition; pass a different anchor/pivot
        // after the call for any other corner.
        public static GameObject BuildInjuryCrossIcon(Transform parent, float size)
        {
            GameObject iconObj = new GameObject("InjuryCross", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(parent, false);

            RectTransform iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(1f, 0f);
            iconRect.anchorMax = new Vector2(1f, 0f);
            iconRect.pivot = new Vector2(1f, 0f);
            iconRect.sizeDelta = new Vector2(size, size);
            iconObj.GetComponent<Image>().color = Danger;

            GameObject verticalBar = new GameObject("Vertical", typeof(RectTransform), typeof(Image));
            verticalBar.transform.SetParent(iconObj.transform, false);
            RectTransform verticalRect = verticalBar.GetComponent<RectTransform>();
            verticalRect.anchorMin = new Vector2(0.5f, 0.5f);
            verticalRect.anchorMax = new Vector2(0.5f, 0.5f);
            verticalRect.pivot = new Vector2(0.5f, 0.5f);
            verticalRect.sizeDelta = new Vector2(size * 0.15f, size * 0.7f);
            verticalBar.GetComponent<Image>().color = TextPrimary;

            GameObject horizontalBar = new GameObject("Horizontal", typeof(RectTransform), typeof(Image));
            horizontalBar.transform.SetParent(iconObj.transform, false);
            RectTransform horizontalRect = horizontalBar.GetComponent<RectTransform>();
            horizontalRect.anchorMin = new Vector2(0.5f, 0.5f);
            horizontalRect.anchorMax = new Vector2(0.5f, 0.5f);
            horizontalRect.pivot = new Vector2(0.5f, 0.5f);
            horizontalRect.sizeDelta = new Vector2(size * 0.7f, size * 0.15f);
            horizontalBar.GetComponent<Image>().color = TextPrimary;

            return iconObj;
        }

        // Unity UI has no native CSS-style linear-gradient - this bakes a small diagonal
        // two-color gradient into a texture once (cached, reused everywhere) and stretches
        // it behind a panel's content as the very first sibling, matching the mockup's
        // `linear-gradient(...)` wash on Title/Hub closely enough for a decorative
        // background (not pixel-exact angle math - nobody will measure it, it just needs
        // to read as "less monotonous than a flat color").
        private static Texture2D cachedGradientTexture;

        public static void ApplyDiagonalGradientBackground(GameObject panel, Color colorA, Color colorB)
        {
            if (panel == null)
            {
                return;
            }

            if (cachedGradientTexture == null)
            {
                const int size = 64;
                cachedGradientTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                cachedGradientTexture.wrapMode = TextureWrapMode.Clamp;
                cachedGradientTexture.filterMode = FilterMode.Bilinear;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float t = Mathf.Clamp01(((x / (float)(size - 1)) + (1f - y / (float)(size - 1))) * 0.5f);
                        cachedGradientTexture.SetPixel(x, y, Color.Lerp(colorA, colorB, t));
                    }
                }

                cachedGradientTexture.Apply();
            }

            GameObject gradientObj = new GameObject("GradientBackground", typeof(RectTransform), typeof(Image));
            gradientObj.transform.SetParent(panel.transform, false);
            gradientObj.transform.SetAsFirstSibling();

            RectTransform rect = gradientObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = gradientObj.GetComponent<Image>();
            image.sprite = Sprite.Create(cachedGradientTexture, new Rect(0f, 0f, cachedGradientTexture.width, cachedGradientTexture.height), new Vector2(0.5f, 0.5f));
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
        }

        // Fresh Editor-created buttons ("Button - TextMeshPro") default to a large TMP font
        // size, which wraps/overflows once restyled with real copy. Every button touched at
        // runtime should go through this so it never depends on whatever size, alignment,
        // or weight the Editor happened to default to - hand-placed buttons from before the
        // code-driven reskin (e.g. the match screen's Attacking/Balanced/Defensive tactic
        // buttons) never got their alignment/fontStyle touched by anything, so they kept
        // whatever the Editor originally had (confirmed live: top-left aligned, non-bold,
        // visibly different from every other button). BuildButton already sets both
        // correctly at creation time, so this is a no-op for buttons that came from there.
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

            ApplyThemeFont(label);
            label.text = text;
            label.color = color;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
        }

        // Buttons placed by hand in the Editor (before this reskin took over positioning)
        // had a font baked into their TextMeshProUGUI at creation time - changing the
        // project's default TMP font afterward doesn't retroactively update components
        // that already have an explicit font reference. Every label this file touches
        // goes through here so it's forced onto the current theme font regardless of
        // whatever it started with.
        private static void ApplyThemeFont(TextMeshProUGUI label)
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                label.font = TMP_Settings.defaultFontAsset;
            }
        }
    }
}
