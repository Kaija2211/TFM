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
        // --- Inbox (session 13) - phase 3 of the manager influence arc, the last
        // unclaimed item from the original session 7 plan (captaincy/fitness/morale all
        // shipped already, see project_manager_influence_arc in memory). Same code-
        // built-panel/chrome-guard/scroll-content pattern as Trophy Room/Career, just
        // simpler (no tabs) - a flat newest-first message list. Transfer bid results are
        // the first real message type, but the shape is deliberately generic (see
        // ManagerInbox) for whatever gets added here later. ---

        private bool inboxChromeBuilt;
        private GameObject inboxPanel;
        private RectTransform inboxContentContainer;
        private ScrollRect inboxScrollRect;
        private readonly List<GameObject> spawnedInboxRows = new();

        public void OnOpenInboxClicked()
        {
            if (!inboxChromeBuilt)
            {
                BuildInboxChrome();
                inboxChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (inboxPanel != null) inboxPanel.SetActive(true);

            RefreshInboxUI();

            // Playtest report (2026-08-16): the ScrollRect only ever gets built once
            // (BuildInboxChrome is guarded by inboxChromeBuilt) and nothing repositioned
            // it on later opens, so scrolling down, leaving and coming back resumed at
            // the old offset instead of the newest messages at the top. 1 = top for a
            // top-pivoted content container (see BuildInboxChrome).
            if (inboxScrollRect != null) inboxScrollRect.verticalNormalizedPosition = 1f;
        }

        public void OnInboxBackClicked()
        {
            inbox.MarkAllReadAndCollapse();
            if (inboxPanel != null) inboxPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void BuildInboxChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            inboxPanel = new GameObject("InboxPanel", typeof(RectTransform));
            inboxPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = inboxPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(inboxPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(inboxPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "INBOX", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnInboxBackClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("InboxScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(inboxPanel.transform, false);
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
            inboxContentContainer = contentObj.GetComponent<RectTransform>();
            inboxContentContainer.anchorMin = new Vector2(0f, 1f);
            inboxContentContainer.anchorMax = new Vector2(1f, 1f);
            inboxContentContainer.pivot = new Vector2(0.5f, 1f);
            inboxContentContainer.anchoredPosition = Vector2.zero;
            inboxContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 10f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = inboxContentContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = UiScrollSensitivity;
            inboxScrollRect = scrollRect;

            StartCoroutine(RecoverBlankLabelsNextFrame(inboxPanel.transform));
        }

        private void RefreshInboxUI()
        {
            if (inboxContentContainer == null)
            {
                return;
            }

            foreach (GameObject row in spawnedInboxRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedInboxRows.Clear();

            if (inbox.Messages.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(inboxContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "Nothing here yet - scouting reports and transfer bid responses will land here.", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedInboxRows.Add(emptyObj);
            }
            else
            {
                // Newest first.
                for (int i = inbox.Messages.Count - 1; i >= 0; i--)
                {
                    spawnedInboxRows.Add(BuildInboxMessageRow(inbox.Messages[i]));
                }
            }

            if (inboxButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(inboxButton, "INBOX", ManagerUITheme.TextBody, 17);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(inboxContentContainer));
        }

        // Session 13 - collapsed banner (headline + matchday only) by default, click to
        // expand and reveal the body (and Sign/Walk Away for an actionable message).
        // Requested ahead of the longer Youth scouting-report text about to start
        // landing here - a wall of always-expanded multi-line messages would make the
        // list unscannable. Banner itself is a full-row Button toggling IsExpanded; the
        // Sign/Walk Away buttons sit on top as later siblings so their own clicks
        // resolve to them instead of bubbling down to the row's own toggle (same
        // "topmost raycast target wins" convention BuildClickableNameCell already
        // relies on elsewhere).
        // Session 15 - Thomas: readability pass. Content text (title/body/matchday)
        // dropped to Normal weight regardless of read state - Bold was reserved for
        // emphasis, not baseline body copy, and made every unread row harder to read,
        // not easier (the "NEW" tag + row background tint already carry the unread
        // signal on their own). Every dimension bumped up a full tier too ("you might
        // have 20/20 vision, good sir, but I don't") - banner height, title/matchday/
        // body font sizes, and the expanded body's reserved height all scaled together
        // so nothing clips or crowds the larger text.
        private const float InboxBannerHeight = 80f;

        private GameObject BuildInboxMessageRow(InboxMessage message)
        {
            bool actionable = message.HasPendingAction;
            float bodyHeight = actionable ? 180f : 110f;
            float height = message.IsExpanded ? InboxBannerHeight + bodyHeight : InboxBannerHeight;

            GameObject row = new GameObject($"Message_{message.Id}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(inboxContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            Image rowImage = row.GetComponent<Image>();
            rowImage.color = message.IsRead
                ? ManagerUITheme.CardNeutralAlt
                : new Color(ManagerUITheme.Accent.r, ManagerUITheme.Accent.g, ManagerUITheme.Accent.b, 0.10f);

            Button rowButton = row.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;
            rowButton.onClick.AddListener(() => OnInboxMessageBannerClicked(message));

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(row.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(20f, -20f);
            titleRect.sizeDelta = new Vector2(-320f, 36f);
            // Plain ASCII, not a unicode bullet (Oswald SDF has no glyph for "●" - the
            // same reason the Tactics Board formation dropdown uses a plain "v" instead
            // of a unicode arrow; confirmed live, see feedback_random_namespace_ambiguity-
            // adjacent font gotcha in HANDOFF).
            string unreadMarker = message.IsRead ? "" : "<color=#3ddc84>NEW</color> ";
            string expandMarker = message.IsExpanded ? "v " : "> ";
            ManagerUITheme.BuildLabel(titleObj.transform, $"{expandMarker}{unreadMarker}{message.Title}", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);

            GameObject matchdayObj = new GameObject("Matchday", typeof(RectTransform));
            matchdayObj.transform.SetParent(row.transform, false);
            RectTransform matchdayRect = matchdayObj.GetComponent<RectTransform>();
            matchdayRect.anchorMin = new Vector2(1f, 1f);
            matchdayRect.anchorMax = new Vector2(1f, 1f);
            matchdayRect.pivot = new Vector2(1f, 1f);
            matchdayRect.anchoredPosition = new Vector2(-20f, -22f);
            matchdayRect.sizeDelta = new Vector2(200f, 32f);
            ManagerUITheme.BuildLabel(matchdayObj.transform, ManagerCareerCalendar.DisplayDateForDay(message.MatchdayReceived), 18, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal);

            if (!message.IsExpanded)
            {
                return row;
            }

            GameObject bodyObj = new GameObject("Body", typeof(RectTransform));
            bodyObj.transform.SetParent(row.transform, false);
            RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.anchoredPosition = new Vector2(20f, -(InboxBannerHeight + 6f));
            bodyRect.sizeDelta = new Vector2(-40f, actionable ? bodyHeight - 54f : bodyHeight - 16f);
            ManagerUITheme.BuildLabel(bodyObj.transform, message.Body, 20, ManagerUITheme.TextBody, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);

            if (actionable)
            {
                PlayerAgent actionPlayer = message.ActionPlayer;

                Button signButton = ManagerUITheme.BuildButton(row.transform, "SIGN PLAYER", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
                ManagerUITheme.SetPointAnchor(signButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-16f, 24f), new Vector2(160f, 40f));
                signButton.onClick.AddListener(() => OnSignPlayerClicked(actionPlayer));

                Button walkAwayButton = ManagerUITheme.BuildButton(row.transform, "WALK AWAY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
                ManagerUITheme.SetPointAnchor(walkAwayButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-184f, 24f), new Vector2(160f, 40f));
                walkAwayButton.onClick.AddListener(() => OnWalkAwayClicked(actionPlayer));
            }

            return row;
        }

        // Session 15 fix - Thomas: "as soon as you click the first one, they all turn
        // grey, despite the other ones still technically being unread." Root cause:
        // the old design marked EVERY message read the instant the Inbox screen opened
        // (see the removed comment on RefreshInboxUI), which only reads correctly if
        // you never look at the screen twice in one visit - the first expand click
        // re-ran that same screen-wide refresh and repainted every row from state that
        // had already flipped to all-read the moment the screen opened. Read status is
        // now genuinely per-message: expanding a specific message is what marks THAT
        // one read (collapsing doesn't un-read it), so an unopened message stays green
        // until you actually look at it, no matter what else you click in the meantime.
        private void OnInboxMessageBannerClicked(InboxMessage message)
        {
            message.IsExpanded = !message.IsExpanded;

            if (message.IsExpanded)
            {
                inbox.MarkRead(message);
            }

            RefreshInboxUI();
        }

    }
}
