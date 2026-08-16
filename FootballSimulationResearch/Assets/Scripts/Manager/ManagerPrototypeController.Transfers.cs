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
        // --- Transfer Market (career-arc addition, session 8, Phase 3; bid/negotiation
        // redesign session 13): Buy tab browses every other club's squad plus already-
        // scouted youth prospects. AI-squad targets need their own transfer scout
        // assigned first (separate pool from World Scouting/Academy, see
        // ManagerTransferNegotiation) before a price range and Make Bid unlock - no more
        // one-click instant buy. A submitted bid escrows the amount and resolves a
        // matchday later via Inbox, with the selling club's own squad depth at that
        // position feeding how reluctant they are to sell. Sell tab is unchanged from
        // session 8: only your own Bench (Starting XI deliberately excluded - selling
        // your best XI by a misclick is the one mistake this screen shouldn't let you
        // make casually), one-click sell at 0.9x MarketValue. No AI-vs-AI transfer
        // activity (explicit scope boundary, see HANDOFF) - rival squads only change via
        // progression/retirement, never trading amongst themselves. Same code-built-
        // panel/scroll-view pattern as Squad/Scouting. ---

        private bool transferMarketChromeBuilt;
        private GameObject transferMarketPanel;
        private SquadListView transferMarketListView;
        // GameObject, not TextMeshProUGUI - same reasoning/gotcha as
        // scoutingBylineObj: both start with text="" at build time, which trips the
        // blank-label recovery sweep into destroying/recreating them.
        private GameObject transferMarketBylineObj;
        private GameObject transferMarketStatusLabelObj;
        private Button transferMarketBuyTabButton;
        private Button transferMarketSellTabButton;
        private Button transferMarketScoutedTabButton;
        private bool transferMarketShowingBuyTab = true;
        private bool transferMarketShowingScoutedTab;
        private readonly ManagerTransferSearch transferSearch = new();
        private TMP_InputField transferPlayerSearchInput;
        private TMP_InputField transferClubSearchInput;
        private TMP_InputField transferNationSearchInput;
        private TMP_InputField transferMinAgeInput;
        private TMP_InputField transferMaxAgeInput;
        private Button transferPositionFilterButton;
        private GameObject transferPositionDropdown;
        private Button transferClearFiltersButton;
        private ScrollRect transferMarketScrollRect;
        private float transferInspectReturnScroll = 1f;
        private readonly Dictionary<PlayerAgent, int> outgoingListedDay = new();
        private readonly Dictionary<PlayerAgent, float> outgoingOffers = new();

        // Session 13 - looks up a player's current AI club purely by scanning
        // squadsByTeamName, rather than trusting transferMarketRowClubs (only ever
        // populated for whichever tab is currently rendered, cleared on every refresh -
        // unreliable once a matchday tick needs to resolve a bid/scouting assignment
        // outside the Transfer Market screen entirely). Returns null for a scouted
        // prospect (never in squadsByTeamName, lives in the scouting pools instead) -
        // ManagerTransferNegotiation already treats a null selling team as "no depth
        // information available," the same case a prospect is meant to hit.
        private AgentTeam FindTeamContainingPlayer(PlayerAgent player)
        {
            foreach (KeyValuePair<string, AgentTeam> kvp in squadsByTeamName)
            {
                if (kvp.Value.Players.Contains(player)) return kvp.Value;
            }

            return null;
        }

        public void OnOpenTransferMarketClicked()
        {
            ShowTransferMarket(resetToBuyTab: true);
        }

        private void ShowTransferMarket(bool resetToBuyTab)
        {
            if (!transferMarketChromeBuilt)
            {
                BuildTransferMarketChrome();
                transferMarketChromeBuilt = true;
            }

            if (resetToBuyTab)
            {
                transferMarketShowingBuyTab = true;
                transferMarketShowingScoutedTab = false;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (transferMarketPanel != null) transferMarketPanel.SetActive(true);

            RefreshTransferMarketUI();
        }

        public void OnTransferMarketBackClicked()
        {
            CloseTransferPositionDropdown();
            if (transferMarketPanel != null) transferMarketPanel.SetActive(false);

            ShowSeasonHub();
        }

        private void OnTransferMarketBuyTabClicked()
        {
            transferMarketShowingBuyTab = true;
            transferMarketShowingScoutedTab = false;
            RefreshTransferMarketUI();
        }

        private void OnTransferMarketSellTabClicked()
        {
            transferMarketShowingBuyTab = false;
            transferMarketShowingScoutedTab = false;
            RefreshTransferMarketUI();
        }

        private void OnTransferMarketScoutedTabClicked()
        {
            transferMarketShowingBuyTab = false;
            transferMarketShowingScoutedTab = true;
            RefreshTransferMarketUI();
        }

        private void BuildTransferMarketChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 180f;

            transferMarketPanel = new GameObject("TransferMarketPanel", typeof(RectTransform));
            transferMarketPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = transferMarketPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(transferMarketPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(transferMarketPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(300f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "TRANSFERS", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -58f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            transferMarketBylineObj = bylineObj;

            GameObject statusObj = new GameObject("StatusLabel", typeof(RectTransform));
            statusObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(statusObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -84f), new Vector2(1200f, 20f));
            ManagerUITheme.BuildLabel(statusObj.transform, "", 14, ManagerUITheme.Accent, TextAlignmentOptions.MidlineLeft);
            transferMarketStatusLabelObj = statusObj;

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK TO HUB", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(200f, 36f));
            backButton.onClick.AddListener(OnTransferMarketBackClicked);

            transferMarketBuyTabButton = ManagerUITheme.BuildButton(header.transform, "BUY", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 13);
            ManagerUITheme.SetPointAnchor(transferMarketBuyTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-276f, -27f), new Vector2(120f, 36f));
            transferMarketBuyTabButton.onClick.AddListener(OnTransferMarketBuyTabClicked);

            transferMarketSellTabButton = ManagerUITheme.BuildButton(header.transform, "SELL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(transferMarketSellTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-406f, -27f), new Vector2(120f, 36f));
            transferMarketSellTabButton.onClick.AddListener(OnTransferMarketSellTabClicked);

            transferMarketScoutedTabButton = ManagerUITheme.BuildButton(header.transform, "SCOUTED", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(transferMarketScoutedTabButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-536f, -27f), new Vector2(120f, 36f));
            transferMarketScoutedTabButton.onClick.AddListener(OnTransferMarketScoutedTabClicked);

            const float filterTop = 122f;
            const float filterHeight = 38f;
            float filterX = 60f;
            transferPlayerSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 220f, filterHeight, "Player name");
            transferClubSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 220f, filterHeight, "Club");
            transferNationSearchInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 190f, filterHeight, "Nationality");
            transferMinAgeInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 120f, filterHeight, "Min age", numeric: true);
            transferMaxAgeInput = BuildTransferFilterInput(header.transform, ref filterX, filterTop, 120f, filterHeight, "Max age", numeric: true);

            float positionFilterX = filterX;
            transferPositionFilterButton = ManagerUITheme.BuildButton(header.transform, "ANY POSITION v", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(transferPositionFilterButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(filterX, -filterTop), new Vector2(180f, filterHeight));
            transferPositionFilterButton.onClick.AddListener(ToggleTransferPositionDropdown);
            filterX += 192f;

            transferClearFiltersButton = ManagerUITheme.BuildButton(header.transform, "CLEAR", ManagerUITheme.CardNeutral, ManagerUITheme.TextMuted, 12);
            ManagerUITheme.SetPointAnchor(transferClearFiltersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(filterX, -filterTop), new Vector2(110f, filterHeight));
            transferClearFiltersButton.onClick.AddListener(OnClearTransferFilters);

            BuildTransferPositionDropdown(header.transform, positionFilterX, filterTop + filterHeight + 4f);

            const float contentWidth = 1600f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("TransferScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(transferMarketPanel.transform, false);
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

            transferMarketListView = contentObj.GetComponent<SquadListView>();
            transferMarketListView.Bind(contentRect);

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            transferMarketScrollRect = scrollRect;
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

            GameObject scrollbarObj = new GameObject("TransferScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(transferMarketPanel.transform, false);
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

            StartCoroutine(RecoverBlankLabelsNextFrame(transferMarketPanel.transform));
        }

        private TMP_InputField BuildTransferFilterInput(Transform parent, ref float x, float top, float width, float height, string placeholder, bool numeric = false)
        {
            GameObject container = new GameObject($"{placeholder.Replace(" ", string.Empty)}Filter", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            ManagerUITheme.SetPointAnchor(container.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(width, height));
            TMP_InputField input = ManagerUITheme.BuildInputField(container.transform, placeholder, 13, numeric ? 2 : 32);
            Image inputBackground = input.GetComponent<Image>();
            inputBackground.color = ManagerUITheme.CardNeutralAlt;
            Outline outline = input.gameObject.AddComponent<Outline>();
            outline.effectColor = ManagerUITheme.BarTrack;
            outline.effectDistance = new Vector2(1f, -1f);
            if (input.textComponent != null)
            {
                input.textComponent.fontStyle = FontStyles.Normal;
                input.textComponent.margin = new Vector4(4f, 0f, 0f, 0f);
            }
            if (input.placeholder is TextMeshProUGUI placeholderLabel)
            {
                placeholderLabel.fontStyle = FontStyles.Normal;
                placeholderLabel.margin = new Vector4(4f, 0f, 0f, 0f);
            }
            RectTransform inputRect = input.GetComponent<RectTransform>();
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = Vector2.one;
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;
            if (numeric) input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.onValueChanged.AddListener(_ => OnTransferSearchChanged());
            x += width + 12f;
            return input;
        }

        private void OnTransferSearchChanged()
        {
            transferSearch.PlayerName = transferPlayerSearchInput?.text ?? string.Empty;
            transferSearch.ClubName = transferClubSearchInput?.text ?? string.Empty;
            transferSearch.Nationality = transferNationSearchInput?.text ?? string.Empty;
            transferSearch.MinimumAge = int.TryParse(transferMinAgeInput?.text, out int minimumAge) ? minimumAge : null;
            transferSearch.MaximumAge = int.TryParse(transferMaxAgeInput?.text, out int maximumAge) ? maximumAge : null;
            if (transferMarketShowingBuyTab || transferMarketShowingScoutedTab) RefreshTransferMarketUI();
        }

        private void BuildTransferPositionDropdown(Transform parent, float x, float top)
        {
            Array values = Enum.GetValues(typeof(PlayerPosition));
            transferPositionDropdown = BuildEmptyDropdownScaffold(parent, values.Length + 1);
            ManagerUITheme.SetPointAnchor(transferPositionDropdown.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(180f, Mathf.Min(360f, (values.Length + 1) * 30f)));

            Transform content = transferPositionDropdown.transform.Find("Viewport/Content");
            AddTransferPositionOption(content, "ANY POSITION", null);
            foreach (PlayerPosition position in values)
            {
                AddTransferPositionOption(content, position.ToString(), position);
            }

            transferPositionDropdown.SetActive(false);
        }

        private void AddTransferPositionOption(Transform content, string label, PlayerPosition? position)
        {
            Button option = ManagerUITheme.BuildButton(content, label, ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            LayoutElement layout = option.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 30f;
            option.onClick.AddListener(() => SelectTransferPosition(position));
        }

        private void ToggleTransferPositionDropdown()
        {
            if (transferPositionDropdown == null) return;
            transferPositionDropdown.SetActive(!transferPositionDropdown.activeSelf);
            if (transferPositionDropdown.activeSelf)
            {
                transferPositionDropdown.transform.parent.SetAsLastSibling();
                transferPositionDropdown.transform.SetAsLastSibling();
                StartCoroutine(RecoverBlankLabelsNextFrame(transferPositionDropdown.transform));
            }
        }

        private void SelectTransferPosition(PlayerPosition? position)
        {
            transferSearch.Position = position;
            CloseTransferPositionDropdown();
            string label = position.HasValue ? position.Value.ToString() : "ANY POSITION";
            ManagerUITheme.NormalizeButtonLabel(transferPositionFilterButton, $"{label} v", ManagerUITheme.TextBody, 12);
            RefreshTransferMarketUI();
        }

        private void CloseTransferPositionDropdown()
        {
            if (transferPositionDropdown != null) transferPositionDropdown.SetActive(false);
        }

        private void OnClearTransferFilters()
        {
            transferSearch.Clear();
            CloseTransferPositionDropdown();
            if (transferPlayerSearchInput != null) transferPlayerSearchInput.text = string.Empty;
            if (transferClubSearchInput != null) transferClubSearchInput.text = string.Empty;
            if (transferNationSearchInput != null) transferNationSearchInput.text = string.Empty;
            if (transferMinAgeInput != null) transferMinAgeInput.text = string.Empty;
            if (transferMaxAgeInput != null) transferMaxAgeInput.text = string.Empty;
            ManagerUITheme.NormalizeButtonLabel(transferPositionFilterButton, "ANY POSITION v", ManagerUITheme.TextBody, 12);
            RefreshTransferMarketUI();
        }

        private readonly Dictionary<PlayerAgent, string> transferMarketRowClubs = new();

        // Sortable columns (session 9 - Thomas: "click OVR to sort high to low"), same
        // pattern as scoutingSortColumn/scoutingSortDescending above. Separate state per
        // tab since Buy and Sell have different column layouts.
        private int transferBuySortColumn = -1;
        private bool transferBuySortDescending = true;
        private int transferSellSortColumn = -1;
        private bool transferSellSortDescending = true;

        private void RefreshTransferMarketUI()
        {
            if (transferMarketListView == null)
            {
                return;
            }

            StatisticalModel.TeamStrength managedStrength = statisticalModel.GetTeamStrength(managedTeamName);
            float budget = finance.GetOrSeedBudget(managedTeamName, managedStrength.AttackStrength, managedStrength.DefenceStrength);

            if (transferMarketBylineObj != null)
            {
                TextMeshProUGUI bylineTMP = transferMarketBylineObj.GetComponentInChildren<TextMeshProUGUI>();
                if (bylineTMP != null)
                {
                    // Sell-tab clarification (backlog item 5, session 11) - not a bug,
                    // session 8 deliberately scoped selling to bench-only to protect
                    // against an accidental first-teamer sale, but nothing said so on
                    // screen, so a ~10-player bench-sized list read as suspiciously
                    // short/broken. Buy tab keeps its original plain budget line.
                    // Session 13 - budget already reflects escrowed bids (TryPlaceBid
                    // deducts immediately, see ManagerTransferNegotiation), so the plain
                    // £Xm figure is still the honest "what you can spend right now"
                    // number; the extra clauses just surface why it might look lower
                    // than expected and how close the two new caps are to being hit.
                    bylineTMP.text = transferMarketShowingBuyTab
                        ? $"Transfer budget: £{budget:F1}m   ·   {transferNegotiation.PendingBidCount}/{ManagerTransferNegotiation.MaxConcurrentBids} bids pending (£{transferNegotiation.GetTotalEscrowed():F1}m committed)   ·   {transferNegotiation.ActiveTransferScoutAssignmentCount}/{ManagerTransferNegotiation.MaxConcurrentTransferScouts} scouts assigned"
                        : transferMarketShowingScoutedTab
                            ? $"Transfer budget: £{budget:F1}m   ·   {transferNegotiation.TransferScoutedPlayers.Count} completed senior reports"
                            : $"Transfer budget: £{budget:F1}m   ·   List players, wait for interest, then review each offer before accepting.";
                }
            }

            if (transferMarketBuyTabButton != null && transferMarketBuyTabButton.TryGetComponent(out Image buyImage))
            {
                buyImage.color = transferMarketShowingBuyTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(transferMarketBuyTabButton, "BUY", transferMarketShowingBuyTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (transferMarketSellTabButton != null && transferMarketSellTabButton.TryGetComponent(out Image sellImage))
            {
                bool sellActive = !transferMarketShowingBuyTab && !transferMarketShowingScoutedTab;
                sellImage.color = sellActive ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(transferMarketSellTabButton, "SELL", sellActive ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            if (transferMarketScoutedTabButton != null && transferMarketScoutedTabButton.TryGetComponent(out Image scoutedImage))
            {
                scoutedImage.color = transferMarketShowingScoutedTab ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;
                ManagerUITheme.NormalizeButtonLabel(transferMarketScoutedTabButton, "SCOUTED", transferMarketShowingScoutedTab ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody, 13);
            }

            bool showSearch = transferMarketShowingBuyTab || transferMarketShowingScoutedTab;
            if (transferPlayerSearchInput != null) transferPlayerSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferClubSearchInput != null) transferClubSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferNationSearchInput != null) transferNationSearchInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferMinAgeInput != null) transferMinAgeInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferMaxAgeInput != null) transferMaxAgeInput.transform.parent.gameObject.SetActive(showSearch);
            if (transferPositionFilterButton != null) transferPositionFilterButton.gameObject.SetActive(showSearch);
            if (!showSearch) CloseTransferPositionDropdown();
            if (transferClearFiltersButton != null) transferClearFiltersButton.gameObject.SetActive(showSearch);

            transferMarketListView.Clear();
            transferMarketRowClubs.Clear();

            if (transferMarketShowingScoutedTab)
            {
                RefreshScoutedSeniorPlayersList();
            }
            else if (transferMarketShowingBuyTab)
            {
                RefreshTransferMarketBuyList(budget);
            }
            else
            {
                RefreshTransferMarketSellList();
            }
        }

        // Grid-column layout (same AddCustomGridRow/AddCustomGridHeaderRow technique
        // already shipped for Scouting - see RefreshScoutingUI) replacing the old flat
        // concatenated-string label, which didn't align into columns since name lengths
        // vary (backlog item, see HANDOFF).
        private static readonly string[] TransferBuyColumnHeaders = { "PLAYER", "POS", "AGE", "CLUB/NATION", "OVR", "STATUS" };
        private static readonly float[] TransferBuyColumnFractions = { 0.24f, 0.09f, 0.07f, 0.20f, 0.09f, 0.31f };
        private static readonly string[] TransferSellColumnHeaders = { "PLAYER", "POS", "AGE", "OVR", "SELL FOR" };
        private static readonly float[] TransferSellColumnFractions = { 0.34f, 0.14f, 0.12f, 0.14f, 0.26f };
        private static readonly string[] TransferScoutedColumnHeaders = { "PLAYER", "POS", "AGE", "CLUB", "OVR", "POTENTIAL / FEE" };
        private static readonly float[] TransferScoutedColumnFractions = { 0.24f, 0.08f, 0.07f, 0.20f, 0.08f, 0.33f };

        private void RefreshScoutedSeniorPlayersList()
        {
            transferMarketListView.AddCustomGridHeaderRow(TransferScoutedColumnHeaders, TransferScoutedColumnFractions, null, -1, true);
            foreach (PlayerAgent player in transferNegotiation.TransferScoutedPlayers.ToList())
            {
                AgentTeam team = FindTeamContainingPlayer(player);
                string club = squadsByTeamName.FirstOrDefault(pair => pair.Value == team).Key ?? "Unknown club";
                if (!transferSearch.Matches(player, club)) continue;
                float recommended = ManagerTransferNegotiation.GetRecommendedBid(player, team);
                string[] cells = { player.Name, player.PrimaryPosition.ToString(), player.Age.ToString(), club,
                    GetDisplayRating(player.GetOverallRating()).ToString(),
                    $"{ManagerTransferNegotiation.GetPotentialClue(player)} · ~£{recommended:F1}m" };
                transferMarketListView.AddCustomGridRow(player, cells, TransferScoutedColumnFractions, OnBuyRowClicked,
                    onNameClicked: p => OpenTransferTargetDetail(p, transferNegotiation.TransferScoutedPlayers.ToList()));
            }
        }

        private void RefreshTransferMarketBuyList(float budget)
        {
            List<PlayerAgent> players = new List<PlayerAgent>();
            transferMarketListView.AddCustomGridHeaderRow(TransferBuyColumnHeaders, TransferBuyColumnFractions, OnTransferBuyColumnHeaderClicked, transferBuySortColumn, transferBuySortDescending);

            if (!transferSearch.HasCriteria)
            {
                transferMarketListView.AddSectionHeader("SEARCH THE MARKET — choose at least one filter to discover players in this world");
                return;
            }

            foreach (string teamName in availableTeamNames)
            {
                if (teamName == managedTeamName)
                {
                    continue;
                }

                AgentTeam team = GetOrCreateAgentTeam(teamName);

                foreach (PlayerAgent player in team.Players)
                {
                    transferMarketRowClubs[player] = teamName;
                    players.Add(player);
                }
            }

            // Generate every active club before evaluating nationality. Nationalities
            // are assigned lazily and consume Unity's random stream; filtering while
            // clubs were still being generated would let a UI search alter later clubs'
            // generated players in the same career.
            players.RemoveAll(player => !transferSearch.Matches(player,
                transferMarketRowClubs.TryGetValue(player, out string club) ? club : string.Empty));

            // Scouted youth prospects deliberately do NOT appear here anymore (session
            // 13 Youth rework) - Thomas's explicit call: the Missions/Youth page is
            // genuinely for youth now, every discovery has to be brought into the
            // Academy first regardless of age, never bid on directly. See
            // ManagerScouting/OnBringInScoutedPlayerClicked for where they actually go.

            if (transferBuySortColumn >= 0)
            {
                players.Sort((a, b) => CompareTransferBuyColumn(a, b, transferBuySortColumn, transferBuySortDescending));
            }

            if (players.Count == 0)
            {
                transferMarketListView.AddSectionHeader("NO PLAYERS MATCH — broaden the search or clear a filter");
                return;
            }

            transferMarketListView.AddSectionHeader($"SEARCH RESULTS ({players.Count})");

            foreach (PlayerAgent player in players)
            {
                string teamName = transferMarketRowClubs.TryGetValue(player, out string t) ? t : "?";
                AddBuyRow(player, teamName, budget, players);
            }
        }

        private void OnTransferBuyColumnHeaderClicked(int column)
        {
            if (transferBuySortColumn == column)
            {
                transferBuySortDescending = !transferBuySortDescending;
            }
            else
            {
                transferBuySortColumn = column;
                transferBuySortDescending = true;
            }

            RefreshTransferMarketUI();
        }

        // Column indices match TransferBuyColumnHeaders.
        private int CompareTransferBuyColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
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
                    string clubA = transferMarketRowClubs.TryGetValue(a, out string ca) ? ca : "";
                    string clubB = transferMarketRowClubs.TryGetValue(b, out string cb) ? cb : "";
                    result = string.Compare(clubA, clubB, StringComparison.OrdinalIgnoreCase);
                    break;
                case 4:
                    result = a.GetOverallRating().CompareTo(b.GetOverallRating());
                    break;
                case 5:
                    result = GetTransferStatusSortKey(a).CompareTo(GetTransferStatusSortKey(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        // Session 13 redesign - STATUS is no longer a single price, it's a state
        // (unscouted/scouting/ready-to-bid/pending/awaiting signature), so sorting on it
        // needs a tiered key rather than a plain price comparison: awaiting-signature
        // bids float to the top (the most actionable state), then pending bids, then
        // ready-to-bid targets (secondary-sorted by recommended price), then in-progress
        // scouting, then still-unscouted targets last.
        private float GetTransferStatusSortKey(PlayerAgent player)
        {
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(player);

            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature) return 4000f + pendingBid.BidAmount;
            if (pendingBid != null) return 3000f + pendingBid.BidAmount;

            bool scouted = transferNegotiation.IsTransferScouted(player);
            if (scouted)
            {
                AgentTeam sourceTeam = FindTeamContainingPlayer(player);
                return 2000f + ManagerTransferNegotiation.GetRecommendedBid(player, sourceTeam);
            }

            return transferNegotiation.IsTransferScoutAssigned(player) ? 1000f : 0f;
        }

        private void AddBuyRow(PlayerAgent player, string teamName, float budget, List<PlayerAgent> browseList)
        {
            bool scouted = transferNegotiation.IsTransferScouted(player);
            bool scoutAssigned = transferNegotiation.IsTransferScoutAssigned(player);
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(player);
            AgentTeam sourceTeam = FindTeamContainingPlayer(player);
            ManagerTransferNegotiation.TransferAvailability availability = ManagerTransferNegotiation.GetAvailability(player, sourceTeam);

            string ovrCell = scouted ? GetDisplayRating(player.GetOverallRating()).ToString() : ManagerTransferNegotiation.GetDisplayOverallBand(player);

            string statusCell;
            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature)
            {
                statusCell = $"<color=#3ddc84>ACCEPTED £{pendingBid.BidAmount:F1}m - CLICK TO SIGN</color>";
            }
            else if (pendingBid != null)
            {
                statusCell = $"<color=#e8c547>BID PENDING £{pendingBid.BidAmount:F1}m</color>";
            }
            else if (!scouted)
            {
                // Click-to-cancel (session 13 - Thomas: "I accidentally started
                // scouting a player I didn't want and couldn't undo it") - the row's
                // own click handler already branches on IsTransferScoutAssigned (see
                // OnBuyRowClicked), this is just the matching label.
                string availabilityLabel = FormatTransferAvailability(availability);
                statusCell = scoutAssigned
                    ? $"{availabilityLabel} · <color=#e8c547>SCOUTING... (click to cancel)</color>"
                    : $"{availabilityLabel} · SCOUT TO REVEAL";
            }
            else if (availability == ManagerTransferNegotiation.TransferAvailability.NotForSale)
            {
                statusCell = "<color=#e05a5a>NOT FOR SALE — NO POSITIONAL COVER</color>";
            }
            else
            {
                float recommended = ManagerTransferNegotiation.GetRecommendedBid(player, sourceTeam);
                string availabilityLabel = FormatTransferAvailability(availability);
                statusCell = recommended <= budget
                    ? $"{availabilityLabel} · ~£{recommended:F1}m · MAKE BID"
                    : $"{availabilityLabel} · ~£{recommended:F1}m · MAKE BID <color=#e05a5a>(over budget)</color>";
            }

            string[] cells =
            {
            player.Name,
            player.PrimaryPosition.ToString(),
            player.Age.ToString(),
            teamName,
            ovrCell,
            statusCell
        };

            // Session 13 redesign - name click no longer opens full detail for an
            // unscouted target (that would leak exact stats straight past the new
            // scouting gate, see the design notes above AddBuyRow's own comment
            // history); it falls back to the same row action instead.
            transferMarketListView.AddCustomGridRow(player, cells, TransferBuyColumnFractions, OnBuyRowClicked,
                onNameClicked: p => { if (scouted) OpenTransferTargetDetail(p, browseList); else OnBuyRowClicked(p); });
        }

        private static string FormatTransferAvailability(ManagerTransferNegotiation.TransferAvailability availability)
        {
            switch (availability)
            {
                case ManagerTransferNegotiation.TransferAvailability.Available: return "<color=#3ddc84>AVAILABLE</color>";
                case ManagerTransferNegotiation.TransferAvailability.KeyPlayer: return "<color=#e8c547>KEY PLAYER</color>";
                case ManagerTransferNegotiation.TransferAvailability.NotForSale: return "<color=#e05a5a>NOT FOR SALE</color>";
                default: return "NEGOTIABLE";
            }
        }

        private void OpenTransferTargetDetail(PlayerAgent player, List<PlayerAgent> browseList)
        {
            transferInspectReturnScroll = transferMarketScrollRect != null ? transferMarketScrollRect.verticalNormalizedPosition : 1f;
            playerInspectReturnTarget = PlayerInspectReturnTarget.TransferMarket;
            OpenPlayerInspect(player, browseList, ownSquad: false);
        }

        private void RefreshTransferMarketSellList()
        {
            // GetOrCreateAgentTeam, not a TryGetValue no-op - the managed team's squad
            // may genuinely not exist yet if Transfers is opened before ever viewing
            // Squad or playing a match (squads generate lazily), which would otherwise
            // silently show an empty Sell list instead of your real bench.
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            List<PlayerAgent> players = new List<PlayerAgent>(team.Players);

            if (transferSellSortColumn >= 0)
            {
                players.Sort((a, b) => CompareTransferSellColumn(a, b, transferSellSortColumn, transferSellSortDescending));
            }

            transferMarketListView.AddCustomGridHeaderRow(TransferSellColumnHeaders, TransferSellColumnFractions, OnTransferSellColumnHeaderClicked, transferSellSortColumn, transferSellSortDescending);

            foreach (PlayerAgent player in players)
            {
                float marketValue = ManagerClubFinance.GetMarketValue(player);
                string status = outgoingOffers.TryGetValue(player, out float offer)
                    ? $"OFFER £{offer:F1}m — REVIEW"
                    : outgoingListedDay.ContainsKey(player)
                        ? "LISTED — AWAITING INTEREST"
                        : $"VALUE £{marketValue:F1}m — LIST PLAYER";
                string[] cells =
                {
                player.Name,
                player.PrimaryPosition.ToString(),
                player.Age.ToString(),
                GetDisplayRating(player.GetOverallRating()).ToString(),
                status
            };

                // Session 9 - unlike Buy/Scouting, a Sell-list player IS on your own
                // squad, so this opens the normal full Player Detail (roles band and
                // all) rather than the read-only external mode - just returning to
                // Transfers instead of the Hub.
                transferMarketListView.AddCustomGridRow(player, cells, TransferSellColumnFractions, OnSellRowClicked,
                    onNameClicked: p => OpenOwnSquadDetailFromTransferMarket(p, players));
            }
        }

        private void OpenOwnSquadDetailFromTransferMarket(PlayerAgent player, List<PlayerAgent> browseList)
        {
            transferInspectReturnScroll = transferMarketScrollRect != null ? transferMarketScrollRect.verticalNormalizedPosition : 1f;
            playerInspectReturnTarget = PlayerInspectReturnTarget.TransferMarket;
            OpenPlayerInspect(player, browseList, ownSquad: true);
        }

        private IEnumerator RestoreTransferScrollNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (transferMarketScrollRect != null) transferMarketScrollRect.verticalNormalizedPosition = transferInspectReturnScroll;
        }

        private void OnTransferSellColumnHeaderClicked(int column)
        {
            if (transferSellSortColumn == column)
            {
                transferSellSortDescending = !transferSellSortDescending;
            }
            else
            {
                transferSellSortColumn = column;
                transferSellSortDescending = true;
            }

            RefreshTransferMarketUI();
        }

        // Column indices match TransferSellColumnHeaders.
        private int CompareTransferSellColumn(PlayerAgent a, PlayerAgent b, int column, bool descending)
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
                    result = ManagerClubFinance.GetSellPrice(a).CompareTo(ManagerClubFinance.GetSellPrice(b));
                    break;
                default:
                    result = 0;
                    break;
            }

            return descending ? -result : result;
        }

        // Session 13 redesign - single click handler for whichever state a Buy row is
        // currently in, branching the same way the row's own STATUS cell does (see
        // AddBuyRow). Replaces the old instant-buy OnBuyRowClicked entirely - no state
        // in the new flow resolves in one click anymore.
        private void OnBuyRowClicked(PlayerAgent target)
        {
            ManagerTransferNegotiation.PendingBid pendingBid = transferNegotiation.GetPendingBid(target);

            if (pendingBid != null && pendingBid.Status == ManagerTransferNegotiation.BidStatus.AwaitingSignature)
            {
                // Convenience path - the same Sign action the Inbox message offers,
                // just reachable straight from the row too. Walk Away deliberately
                // stays Inbox-only (see OnInboxWalkAwayClicked) so declining a done
                // deal is a deliberate visit to the message, not an accidental click.
                OnSignPlayerClicked(target);
                return;
            }

            if (pendingBid != null)
            {
                SetTransferMarketStatus($"Still waiting to hear back on {target.Name} - the response will arrive through Continue.");
                return;
            }

            bool scouted = transferNegotiation.IsTransferScouted(target);

            if (!scouted)
            {
                // Click-to-cancel (session 13 - Thomas: "I accidentally started
                // scouting a player I didn't want and couldn't undo it").
                if (transferNegotiation.IsTransferScoutAssigned(target))
                {
                    transferNegotiation.CancelTransferScout(target);
                    SetTransferMarketStatus($"Cancelled the scout assignment on {target.Name}.");
                }
                else if (transferNegotiation.TryAssignTransferScout(target, careerCalendar.CurrentDayNumber))
                {
                    SetTransferMarketStatus($"Scout assigned to {target.Name} - report due in {ManagerTransferNegotiation.TransferScoutDurationDays} days.");
                }
                else if (transferNegotiation.ActiveTransferScoutAssignmentCount >= ManagerTransferNegotiation.MaxConcurrentTransferScouts)
                {
                    SetTransferMarketStatus($"All {ManagerTransferNegotiation.MaxConcurrentTransferScouts} transfer scouts are already assigned - wait for a report to land first.");
                }

                RefreshTransferMarketUI();
                return;
            }

            string sourceTeamDisplay = transferMarketRowClubs.TryGetValue(target, out string t) ? t : "Unknown";
            AgentTeam sellingTeam = FindTeamContainingPlayer(target);
            if (ManagerTransferNegotiation.GetAvailability(target, sellingTeam) == ManagerTransferNegotiation.TransferAvailability.NotForSale)
            {
                SetTransferMarketStatus($"{sourceTeamDisplay} will not sell {target.Name} without positional cover.");
                return;
            }
            ShowBidDialog(target, sellingTeam, sourceTeamDisplay);
        }

        // --- Bid-amount dialog (session 13, free-text field session 16) - a numeric-only
        // TMP_InputField (ManagerUITheme.BuildInputField) rather than the original five
        // preset-multiplier picker - Thomas's explicit follow-up ask: "i'd like our bid
        // option to be a text field so you can enter your own bid... exclusively number
        // input, remove the five or so set options." Prefilled with the scout's
        // recommended amount so a manager who doesn't want to think about it can still
        // just hit Submit. ---

        private GameObject bidDialogPanel;
        private TMP_InputField bidAmountInputField;
        private TextMeshProUGUI bidDialogStatusLabel;
        private PlayerAgent bidDialogTarget;
        private string bidDialogSourceTeam;

        private void ShowBidDialog(PlayerAgent target, AgentTeam sellingTeam, string sourceTeamDisplay)
        {
            if (bidDialogPanel != null)
            {
                Destroy(bidDialogPanel);
            }

            float recommended = ManagerTransferNegotiation.GetRecommendedBid(target, sellingTeam);
            bidDialogTarget = target;
            bidDialogSourceTeam = sourceTeamDisplay;

            Transform root = titlePanel.transform.parent;
            bidDialogPanel = new GameObject("BidDialogPanel", typeof(RectTransform), typeof(Image));
            bidDialogPanel.transform.SetParent(root, false);
            RectTransform panelRect = bidDialogPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            bidDialogPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            bidDialogPanel.transform.SetAsLastSibling();

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(bidDialogPanel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(760f, 300f);
            cardRect.anchoredPosition = Vector2.zero;
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(680f, 30f));
            ManagerUITheme.BuildLabel(titleObj.transform, $"MAKE A BID: {target.Name.ToUpperInvariant()}", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(subtitleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(680f, 24f));
            ManagerUITheme.BuildLabel(subtitleObj.transform, $"Market value ~£{ManagerClubFinance.GetMarketValue(target):F1}m   ·   scout's recommendation ~£{recommended:F1}m", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            GameObject inputLabelObj = new GameObject("InputLabel", typeof(RectTransform));
            inputLabelObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(inputLabelObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(680f, 22f));
            ManagerUITheme.BuildLabel(inputLabelObj.transform, "BID AMOUNT (£M)", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center);

            GameObject inputContainer = new GameObject("InputContainer", typeof(RectTransform));
            inputContainer.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(inputContainer.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(300f, 44f));

            bidAmountInputField = ManagerUITheme.BuildInputField(inputContainer.transform, "e.g. 45.5", 20, characterLimit: 9);
            bidAmountInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            bidAmountInputField.text = recommended.ToString("F1");

            // Prefilling .text this early (same frame the field is built, before Unity's
            // own Awake/OnEnable-driven placeholder-hide logic has run) doesn't hide the
            // placeholder label the normal typing path would - without this the default
            // amount renders on top of "e.g. 45.5" instead of replacing it (session 16
            // playtest screenshot: "94.3" overlapping "45.5"). BuildInputField's other
            // caller (Save Name) never prefills text, so this is scoped to here rather
            // than the shared helper.
            if (bidAmountInputField.placeholder != null)
            {
                bidAmountInputField.placeholder.gameObject.SetActive(false);
            }

            GameObject statusObj = new GameObject("Status", typeof(RectTransform));
            statusObj.transform.SetParent(card.transform, false);
            ManagerUITheme.SetPointAnchor(statusObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -194f), new Vector2(680f, 40f));
            bidDialogStatusLabel = ManagerUITheme.BuildLabel(statusObj.transform,
                careerCalendar.IsTransferWindowOpen ? "" : "TRANSFER WINDOW CLOSED — offers are currently disabled",
                13, careerCalendar.IsTransferWindowOpen ? ManagerUITheme.TextMuted : ManagerUITheme.Danger,
                TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);

            Button confirmButton = ManagerUITheme.BuildButton(card.transform, "SUBMIT BID", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-100f, 36f), new Vector2(180f, 48f));
            confirmButton.onClick.AddListener(OnConfirmBidClicked);

            Button cancelButton = ManagerUITheme.BuildButton(card.transform, "CANCEL", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 15);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(100f, 36f), new Vector2(180f, 48f));
            cancelButton.onClick.AddListener(CloseBidDialog);

            StartCoroutine(RecoverBlankLabelsNextFrame(bidDialogPanel.transform));
        }

        private void OnConfirmBidClicked()
        {
            if (bidDialogTarget == null)
            {
                CloseBidDialog();
                return;
            }

            PlayerAgent target = bidDialogTarget;

            string bidText = bidAmountInputField != null ? bidAmountInputField.text : "";
            bool parsed = float.TryParse(bidText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float amount)
                || float.TryParse(bidText, out amount);
            if (!parsed || amount <= 0f)
            {
                SetBidDialogStatus("Enter a valid bid amount above £0m.");
                return;
            }

            if (!careerCalendar.IsTransferWindowOpen)
            {
                SetBidDialogStatus("The transfer window is closed. Pre-window agreements are not implemented yet.");
                return;
            }

            if (transferNegotiation.TryPlaceBid(target, amount, bidDialogSourceTeam, careerCalendar.CurrentDayNumber, finance, managedTeamName))
            {
                SetTransferMarketStatus($"Bid of £{amount:F1}m submitted for {target.Name} - response expected in {ManagerTransferNegotiation.BidResponseDays} days.");
            }
            else
            {
                SetTransferMarketStatus($"Couldn't submit that bid for {target.Name} - check your budget or your {ManagerTransferNegotiation.MaxConcurrentBids}-bid pending limit.");
            }

            CloseBidDialog();
            RefreshTransferMarketUI();
        }

        private void SetBidDialogStatus(string message)
        {
            if (bidDialogStatusLabel != null) bidDialogStatusLabel.text = message;
        }

        private void CloseBidDialog()
        {
            if (bidDialogPanel != null)
            {
                Destroy(bidDialogPanel);
                bidDialogPanel = null;
            }

            bidDialogTarget = null;
            bidAmountInputField = null;
            bidDialogStatusLabel = null;
        }

        // Finalizes an accepted bid - moving the player onto the managed squad mirrors
        // the old OnBuyRowClicked's "remove from whichever source they actually came
        // from" logic exactly, just triggered from a Sign action instead of happening
        // automatically the instant a bid was accepted (Thomas's explicit "confirm and
        // sign" flow). Reachable both from the Buy row (see OnBuyRowClicked) and from
        // the Inbox message itself (see OnInboxSignClicked).
        private void OnSignPlayerClicked(PlayerAgent target)
        {
            if (!careerCalendar.IsTransferWindowOpen)
            {
                SetTransferMarketStatus("The transfer window is closed - this deal cannot be completed today.");
                return;
            }

            if (!transferNegotiation.TrySign(target, finance, managedTeamName, out ManagerTransferNegotiation.PendingBid resolvedBid))
            {
                return;
            }

            // Every Transfer Market bid target is a regular AI-squad player now (session
            // 13 Youth rework routes every scouted prospect through the Academy
            // instead, never through here) - remove from their real club's squad.
            if (resolvedBid.SourceTeamName != null && squadsByTeamName.TryGetValue(resolvedBid.SourceTeamName, out AgentTeam sourceSquad))
            {
                // Session 16 - Thomas: "starting players sold automatically get replaced
                // with suitable bench players." Same SubstitutePlayer swap the managed
                // team's own injury/loan backfill already uses (see
                // EnsureNoInjuredStarters/OnLoanOutClicked) - promotes the best-fit bench
                // cover into the exact formation slot the sold player vacated instead of
                // leaving a hole in the AI club's XI. ManagerTransferNegotiation.
                // WouldLeaveSquadTooThin already guarantees a same-position player exists
                // somewhere in the squad before a sale is ever accepted, so this should
                // always find real cover in practice - still defensively falls through to
                // a plain removal if it somehow doesn't.
                if (sourceSquad.StartingEleven.Contains(target))
                {
                    PlayerAgent replacement = FindBestFitBenchPlayer(sourceSquad, target.PrimaryPosition);

                    if (replacement != null)
                    {
                        sourceSquad.SubstitutePlayer(target, replacement);
                    }
                    else
                    {
                        sourceSquad.StartingEleven.Remove(target);
                    }
                }

                sourceSquad.RemovePlayer(target);
            }

            GetOrCreateAgentTeam(managedTeamName).AddSquadPlayer(target);

            MarkInboxMessagesResolvedForPlayer(target);
            SetTransferMarketStatus($"Signed {target.Name} for £{resolvedBid.BidAmount:F1}m!");
            RefreshTransferMarketUI();
            if (inboxContentContainer != null) RefreshInboxUI();
        }

        private void OnWalkAwayClicked(PlayerAgent target)
        {
            if (!transferNegotiation.TryWalkAway(target, finance, managedTeamName))
            {
                return;
            }

            MarkInboxMessagesResolvedForPlayer(target);
            SetTransferMarketStatus($"Walked away from the {target.Name} deal - your money's back in the budget.");
            RefreshTransferMarketUI();
            if (inboxContentContainer != null) RefreshInboxUI();
        }

        // Both Sign and Walk Away leave the triggering message (whichever screen it was
        // clicked from) without a live action to perform anymore - clears the pending-
        // action flag on every message still pointing at this player so ResolveAction's
        // save/load-safety guarantee holds (see ManagerInbox.BuildSaveList) and the
        // message reads as a closed, historical record instead of a dead button.
        private void MarkInboxMessagesResolvedForPlayer(PlayerAgent player)
        {
            foreach (InboxMessage message in inbox.Messages)
            {
                if (message.ActionPlayer == player)
                {
                    inbox.ResolveAction(message);
                }
            }
        }

        private void OnSellRowClicked(PlayerAgent target)
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team)
                || !team.Players.Contains(target))
            {
                return;
            }

            if (outgoingOffers.TryGetValue(target, out float offer))
            {
                ShowConfirmDialog($"Accept the £{offer:F1}m offer for {target.Name}? This permanently removes him from your squad.", "ACCEPT OFFER", () => CompleteOutgoingSale(target, offer), "KEEP PLAYER", null);
                return;
            }

            if (outgoingListedDay.ContainsKey(target))
            {
                ShowConfirmDialog($"Withdraw {target.Name} from the transfer list?", "WITHDRAW", () =>
                {
                    outgoingListedDay.Remove(target);
                    SetTransferMarketStatus($"{target.Name} is no longer transfer-listed.");
                    RefreshTransferMarketUI();
                }, "CANCEL", null);
                return;
            }

            string roleWarning = team.StartingEleven.Contains(target) ? " He is currently in your starting XI." : string.Empty;
            ShowConfirmDialog($"Transfer-list {target.Name}? Clubs will assess him over the next few days; no sale happens without your approval.{roleWarning}", "LIST PLAYER", () =>
            {
                outgoingListedDay[target] = careerCalendar.CurrentDayNumber;
                SetTransferMarketStatus($"{target.Name} has been placed on the transfer list.");
                RefreshTransferMarketUI();
            }, "CANCEL", null);
        }

        private void ResolveOutgoingTransferInterest(int currentDay)
        {
            foreach (KeyValuePair<PlayerAgent, int> listing in outgoingListedDay.ToList())
            {
                if (outgoingOffers.ContainsKey(listing.Key) || currentDay < listing.Value + 3) continue;
                // Stable per-player/day variation avoids every club offering the exact
                // same haircut while keeping the manager in control of the final sale.
                System.Random roll = new System.Random((listing.Key.PlayerId + currentDay).GetHashCode());
                float multiplier = 0.78f + (float)roll.NextDouble() * 0.22f;
                float offer = ManagerClubFinance.GetMarketValue(listing.Key) * multiplier;
                outgoingOffers[listing.Key] = offer;
                inbox.Add(InboxMessageType.TransferOffer, $"Offer received: {listing.Key.Name}",
                    $"A club has offered £{offer:F1}m for {listing.Key.Name}. Review it from Transfers > Sell.", currentDay);
            }
        }

        private void CompleteOutgoingSale(PlayerAgent target, float offer)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            if (!team.Players.Contains(target)) return;
            if (!careerCalendar.IsTransferWindowOpen)
            {
                SetTransferMarketStatus("The offer remains available, but the transfer cannot complete while the window is closed.");
                return;
            }
            if (team.Players.Count <= 18)
            {
                SetTransferMarketStatus("The board will not approve a sale that leaves fewer than 18 senior players.");
                return;
            }
            if (target.PrimaryPosition == PlayerPosition.GK && team.Players.Count(player => player.PrimaryPosition == PlayerPosition.GK) <= 1)
            {
                SetTransferMarketStatus("You cannot sell the club's only goalkeeper.");
                return;
            }
            team.RemovePlayer(target);
            outgoingListedDay.Remove(target);
            outgoingOffers.Remove(target);
            finance.AdjustBudget(managedTeamName, offer);
            finance.RecordTransferIncome(managedTeamName, offer);
            SetTransferMarketStatus($"Accepted £{offer:F1}m for {target.Name}.");
            RefreshTransferMarketUI();
        }

        private void SetTransferMarketStatus(string message)
        {
            if (transferMarketStatusLabelObj == null)
            {
                return;
            }

            TextMeshProUGUI statusTMP = transferMarketStatusLabelObj.GetComponentInChildren<TextMeshProUGUI>();
            if (statusTMP != null)
            {
                statusTMP.text = message;
            }
        }

    }
}
