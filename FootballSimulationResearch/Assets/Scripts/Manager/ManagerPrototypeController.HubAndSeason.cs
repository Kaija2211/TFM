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
        public void SelectAttackingMentality() => SetMentality(ManagerMentality.Attacking);
        public void SelectBalancedMentality() => SetMentality(ManagerMentality.Balanced);
        public void SelectDefensiveMentality() => SetMentality(ManagerMentality.Defensive);

        private void SetMentality(ManagerMentality mentality)
        {
            selectedMentality = mentality;

            HighlightSelectedMentalityButton(attackingButton, mentality == ManagerMentality.Attacking);
            HighlightSelectedMentalityButton(balancedButton, mentality == ManagerMentality.Balanced);
            HighlightSelectedMentalityButton(defensiveButton, mentality == ManagerMentality.Defensive);

            ApplyLiveMentalityChangeIfMatchInProgress();
        }

        // A mentality click during a live match now genuinely changes the rest of that
        // match instead of silently only affecting the *next* one (the old "scaffolded
        // mid-match control, v1 scope" limitation) - reuses the exact same resimulation
        // path substitutions already use (TriggerMidMatchResimulation). Recomputed from
        // the stored pre-mentality baseline (lastRawExpectedHomeGoals/AwayGoals, set in
        // SimulateFixture) rather than re-applying the modifier on top of
        // lastExpectedHomeGoals/AwayGoals, which already has whatever mentality was
        // selected at kickoff baked in - reapplying on top of that would compound two
        // modifiers instead of replacing one with the other.
        private void ApplyLiveMentalityChangeIfMatchInProgress()
        {
            // currentFixture is a struct (OpenFootballMatch), always populated by the
            // time isMatchCurrentlyLive can be true - both OnNextMatchdayClicked and
            // OnSimulateMatchClicked set it before a match ever starts - so no separate
            // null check is needed or possible here.
            if (!isMatchCurrentlyLive || lastSimulatedResult == null)
            {
                return;
            }

            float expectedHomeGoals = lastRawExpectedHomeGoals;
            float expectedAwayGoals = lastRawExpectedAwayGoals;

            if (currentFixture.HomeTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else if (currentFixture.AwayTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;

            TriggerMidMatchResimulation();
        }

        private static void HighlightSelectedMentalityButton(Button button, bool selected)
        {
            if (button == null || !button.TryGetComponent(out Image image))
            {
                return;
            }

            image.color = selected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutral;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = selected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
            }
        }

        // --- Season Hub ---

        private void ShowSeasonHub()
        {
            if (!hubChromeBuilt)
            {
                BuildHubChrome();
                hubChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(true);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            // Reached from both a brand-new career and a loaded one - the common signal
            // that real career state now exists in memory and is safe to save on quit
            // (see OnApplicationQuit).
            careerLoadedThisSession = true;

            RefreshHubUI();
        }

        // Header (crest, club name/byline, Simulate Season top-right) and the two-column
        // body (menu left, league table right), built once. The five reused buttons keep
        // their existing Editor wiring/onClick listeners - only their RectTransforms get
        // set here, via ManagerUITheme.SetPointAnchor, instead of being hand-dragged.
        private void BuildHubChrome()
        {
            if (seasonHubPanel == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(seasonHubPanel);
            ManagerUITheme.ApplyDiagonalGradientBackground(seasonHubPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

            // Left accent bar - this mockup uses a thin vertical edge bar instead of the
            // top/bottom bands used on Team Select/Matchday Prep/Squad/Player Detail.
            GameObject leftBar = new GameObject("LeftAccentBar", typeof(RectTransform), typeof(Image));
            leftBar.transform.SetParent(seasonHubPanel.transform, false);
            leftBar.transform.SetAsFirstSibling();
            RectTransform leftBarRect = leftBar.GetComponent<RectTransform>();
            leftBarRect.anchorMin = new Vector2(0f, 0f);
            leftBarRect.anchorMax = new Vector2(0f, 1f);
            leftBarRect.pivot = new Vector2(0f, 0.5f);
            leftBarRect.sizeDelta = new Vector2(6f, 0f);
            leftBarRect.anchoredPosition = Vector2.zero;
            leftBar.GetComponent<Image>().color = ManagerUITheme.Accent;

            // Content is a max-width:1700px column centered in the 1920-wide panel
            // (mockup's `padding:48px 80px; max-width:1700px; margin:0 auto`), not
            // edge-to-edge against the panel's own corners like the old layout.
            const float outerLeft = (1920f - 1700f) / 2f;
            const float contentLeft = outerLeft + 80f;
            const float contentRight = 1920f - contentLeft;
            const float headerTop = 48f;
            const float logoHeight = 48f;
            const float logoWidth = logoHeight * 700f / 220f;

            // The mockup's Hub header has no separate club-crest badge - just the
            // tfm-logo mark directly beside the club name/byline block, so this replaces
            // the old colored-initials crest badge rather than sitting alongside it.
            // Skipped entirely (rather than showing a blank white box) if tfm-logo.png
            // didn't come through as a loadable Sprite - see the Title screen's wordmark
            // for the same fallback reasoning. Club name just falls back to sitting where
            // the logo would have started.
            float nameLeft = contentLeft;

            if (tfmLogoSprite != null)
            {
                GameObject logoObj = new GameObject("Logo", typeof(RectTransform), typeof(Image));
                logoObj.transform.SetParent(seasonHubPanel.transform, false);
                ManagerUITheme.SetPointAnchor(logoObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -headerTop), new Vector2(logoWidth, logoHeight));
                Image logoImage = logoObj.GetComponent<Image>();
                logoImage.sprite = tfmLogoSprite;
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;

                nameLeft = contentLeft + logoWidth + 20f;
            }

            GameObject nameObj = new GameObject("ClubName", typeof(RectTransform));
            nameObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(nameObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(nameLeft, -headerTop), new Vector2(600f, 36f));
            ManagerUITheme.BuildLabel(nameObj.transform, "", 32, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject bylineObj = new GameObject("Byline", typeof(RectTransform));
            bylineObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(bylineObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(nameLeft, -(headerTop + 38f)), new Vector2(600f, 20f));
            ManagerUITheme.BuildLabel(bylineObj.transform, "", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            if (simulateSeasonButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateSeasonButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-contentLeft, -headerTop), new Vector2(220f, logoHeight));
                if (simulateSeasonButton.TryGetComponent(out Image simulateSeasonImage))
                {
                    simulateSeasonImage.color = ManagerUITheme.CardNeutral;
                }
                ManagerUITheme.NormalizeButtonLabel(simulateSeasonButton, "SIMULATE SEASON", ManagerUITheme.TextBody, 13);
            }

            // Left column (menu): Next Matchday / Squad / Transfers / Inbox / Settings /
            // Save & Exit. Row top offsets computed from the header block's own height
            // (headerTop + logoHeight + mockup's 40px margin-bottom below it) plus each
            // preceding row's own height and the mockup's 12px inter-row gap.
            const float menuWidth = 400f;
            const float menuTop = headerTop + logoHeight + 40f;
            const float rowGap = 12f;
            const float mainRowHeight = 64f;
            const float subRowHeight = 54f;

            if (playNextMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(playNextMatchButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -menuTop), new Vector2(menuWidth, mainRowHeight));
                if (playNextMatchButton.TryGetComponent(out Image playNextImage))
                {
                    playNextImage.color = ManagerUITheme.Accent;
                }
                ManagerUITheme.NormalizeButtonLabel(playNextMatchButton, "CONTINUE", ManagerUITheme.OnAccent, 20);
            }

            float squadTop = menuTop + mainRowHeight + rowGap;

            if (viewSquadButton != null)
            {
                ManagerUITheme.SetPointAnchor(viewSquadButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -squadTop), new Vector2(menuWidth, subRowHeight));
                StyleHubActionButton(viewSquadButton);
                ManagerUITheme.NormalizeButtonLabel(viewSquadButton, "SQUAD", ManagerUITheme.TextBody, 17);
            }

            float transfersTop = squadTop + subRowHeight + rowGap;

            if (transfersButton != null)
            {
                // Real now (career-arc addition, session 8, Phase 3) - was a disabled
                // placeholder with no backing system; StyleHubActionButton/
                // NormalizeButtonLabel match viewSquadButton's own normal (non-disabled)
                // styling instead of SetDisabledPlaceholder.
                ManagerUITheme.SetPointAnchor(transfersButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -transfersTop), new Vector2(menuWidth, subRowHeight));
                StyleHubActionButton(transfersButton);
                ManagerUITheme.NormalizeButtonLabel(transfersButton, "TRANSFERS", ManagerUITheme.TextBody, 17);
                transfersButton.onClick.AddListener(OnOpenTransferMarketClicked);
                transfersButton.onClick.AddListener(ManagerAudio.PlayClick);
            }

            // SCOUTING (career-arc addition, session 8, Phase 2) - real, unlike the
            // placeholders around it, so built with the same normal-button styling as
            // viewSquadButton rather than SetDisabledPlaceholder.
            float scoutingTop = transfersTop + subRowHeight + rowGap;

            GameObject scoutingObj = new GameObject("ScoutingButton", typeof(RectTransform), typeof(Image), typeof(Button));
            scoutingObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(scoutingObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -scoutingTop), new Vector2(menuWidth, subRowHeight));
            Button scoutingButton = scoutingObj.GetComponent<Button>();
            scoutingButton.targetGraphic = scoutingObj.GetComponent<Image>();
            // BuildLabel first - StyleHubActionButton/NormalizeButtonLabel only ever
            // UPDATE an existing label via GetComponentInChildren, they never create
            // one, unlike viewSquadButton/transfersButton which already had an
            // Editor-placed label to update. A brand-new code-built button has nothing
            // for them to find, so it rendered with no text at all.
            ManagerUITheme.BuildLabel(scoutingObj.transform, "SCOUTING", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(scoutingButton);
            ManagerUITheme.NormalizeButtonLabel(scoutingButton, "SCOUTING", ManagerUITheme.TextBody, 17);
            scoutingButton.onClick.AddListener(OnOpenScoutingClicked);
            scoutingButton.onClick.AddListener(ManagerAudio.PlayClick);

            // CAREER (career-arc addition, session 8, Phase 4; folded from a standalone
            // "Trophy Room" button into a tabbed Career screen - backlog item 2, session
            // 11) - real, same styling as Squad/Transfers/Scouting rather than a disabled
            // placeholder. Internal identifiers (trophyRoomPanel, OnOpenTrophyRoomClicked,
            // etc.) deliberately kept as-is below - this button is the only thing that
            // changed name-wise, renaming everything downstream wasn't worth the risk.
            float trophyRoomTop = scoutingTop + subRowHeight + rowGap;

            GameObject trophyRoomObj = new GameObject("TrophyRoomButton", typeof(RectTransform), typeof(Image), typeof(Button));
            trophyRoomObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(trophyRoomObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -trophyRoomTop), new Vector2(menuWidth, subRowHeight));
            Button trophyRoomButton = trophyRoomObj.GetComponent<Button>();
            trophyRoomButton.targetGraphic = trophyRoomObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(trophyRoomObj.transform, "CAREER", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(trophyRoomButton);
            ManagerUITheme.NormalizeButtonLabel(trophyRoomButton, "CAREER", ManagerUITheme.TextBody, 17);
            trophyRoomButton.onClick.AddListener(OnOpenTrophyRoomClicked);
            trophyRoomButton.onClick.AddListener(ManagerAudio.PlayClick);

            float inboxTop = trophyRoomTop + subRowHeight + rowGap;

            // Real now (session 13) - phase 3 of the manager influence arc, the last
            // unclaimed item from the original session 7 plan (captaincy/fitness/morale
            // all shipped already, see project_manager_influence_arc in memory). Same
            // real-button styling as Squad/Transfers/Scouting/Career rather than the
            // disabled placeholder this used to be.
            GameObject inboxObj = new GameObject("InboxButton", typeof(RectTransform), typeof(Image), typeof(Button));
            inboxObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(inboxObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -inboxTop), new Vector2(menuWidth, subRowHeight));
            inboxButton = inboxObj.GetComponent<Button>();
            inboxButton.targetGraphic = inboxObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(inboxObj.transform, "INBOX", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            StyleHubActionButton(inboxButton);
            ManagerUITheme.NormalizeButtonLabel(inboxButton, "INBOX", ManagerUITheme.TextBody, 17);
            inboxButton.onClick.AddListener(OnOpenInboxClicked);
            inboxButton.onClick.AddListener(ManagerAudio.PlayClick);

            float settingsTop = inboxTop + subRowHeight + rowGap;

            GameObject settingsObj = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsObj.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(settingsObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(contentLeft, -settingsTop), new Vector2(menuWidth, subRowHeight));
            Button settingsButton = settingsObj.GetComponent<Button>();
            settingsButton.targetGraphic = settingsObj.GetComponent<Image>();
            ManagerUITheme.BuildLabel(settingsObj.transform, "SETTINGS", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.UpperCase | FontStyles.Bold);
            // Real now (backlog item, session 12) - was a disabled placeholder with no
            // settings screen at all, same as Title's own Settings button above.
            StyleHubActionButton(settingsButton);
            settingsButton.onClick.AddListener(() => OnOpenSettingsClicked(seasonHubPanel));
            settingsButton.onClick.AddListener(ManagerAudio.PlayClick);

            if (exitToTitleButton != null)
            {
                // Anchored to the bottom of the panel (not the top, unlike the buttons
                // above) so it stays visible regardless of canvas height.
                ManagerUITheme.SetPointAnchor(exitToTitleButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(contentLeft, 24f), new Vector2(menuWidth, 44f));
                if (exitToTitleButton.TryGetComponent(out Image exitImage))
                {
                    exitImage.color = ManagerUITheme.PanelDark;
                }
                ManagerUITheme.NormalizeButtonLabel(exitToTitleButton, "SAVE & EXIT TO TITLE", ManagerUITheme.TextMuted, 15);
            }

            // Right column: league table caption + the table itself. The Scroll View is
            // an Editor object (leagueTableView) - its RectTransform is repositioned here
            // (full-stretch anchors, explicit pixel offsets) rather than left at whatever
            // it was baked to in the scene, since that baked offset was tuned against the
            // old 960x540 CanvasScaler reference resolution and would silently drift once
            // the reference resolution changed to 1920x1080.
            float tableColumnLeft = contentLeft + menuWidth + 60f;

            GameObject tableCaption = new GameObject("TableCaption", typeof(RectTransform));
            tableCaption.transform.SetParent(seasonHubPanel.transform, false);
            ManagerUITheme.SetPointAnchor(tableCaption.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(tableColumnLeft, -menuTop), new Vector2(contentRight - tableColumnLeft, 22f));
            ManagerUITheme.BuildLabel(tableCaption.transform, "PREMIER LEAGUE · TABLE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            if (leagueTableView != null && leagueTableView.TryGetComponent(out RectTransform tableRect))
            {
                tableRect.anchorMin = new Vector2(0f, 0f);
                tableRect.anchorMax = new Vector2(1f, 1f);
                tableRect.offsetMin = new Vector2(tableColumnLeft, 48f);
                tableRect.offsetMax = new Vector2(-(1920f - contentRight), -(menuTop + 36f));
            }

            // See BuildTeamSelectChrome's identical call for why - the same TMP
            // mesh-generation flakiness can hit any freshly-created label on this
            // screen too.
            StartCoroutine(RecoverBlankLabelsNextFrame(seasonHubPanel.transform));
        }

        // Session 9 bug fix: hubClubNameLabel/hubBylineLabel used to be cached fields,
        // assigned once in BuildHubChrome and reused here on every refresh. Confirmed
        // live to break permanently after the very first matchday - the byline got
        // stuck reading "Matchday 1" forever while the league table correctly advanced
        // to 21+ games played. Root cause: RecoverBlankLabelsNextFrame(seasonHubPanel.
        // transform), the general blank-label recovery sweep also called from this
        // screen, silently destroys and recreates any TMP label under the Hub panel
        // that came out blank (a real, if rare, TMP mesh-generation glitch - see that
        // method's own header comment) without knowing to update either cached field
        // to point at the new component. Once that happened, hubBylineLabel != null
        // simply failed forever after, silently skipping the update block entirely -
        // confirmed via a diagnostic log showing hubBylineLabel null on the very next
        // refresh after the first one. A previous fix attempt (an unconditional async
        // destroy+recreate coroutine every refresh, specifically to keep this field
        // valid) had exactly the failure this replaced: it just moved the same "who
        // recreates it last" race somewhere else instead of removing it. Looking these
        // two up fresh by path every refresh - cheap, this isn't a hot path - sidesteps
        // the whole "stale cached reference to something another mechanism can destroy"
        // problem class entirely, no coroutine required.
        private void RefreshHubUI()
        {
            TextMeshProUGUI clubNameLabel = seasonHubPanel != null
                ? seasonHubPanel.transform.Find("ClubName/Label")?.GetComponent<TextMeshProUGUI>()
                : null;
            TextMeshProUGUI bylineLabel = seasonHubPanel != null
                ? seasonHubPanel.transform.Find("Byline/Label")?.GetComponent<TextMeshProUGUI>()
                : null;

            if (clubNameLabel != null)
            {
                clubNameLabel.text = managedTeamName.ToUpperInvariant();
            }

            if (bylineLabel != null)
            {
                string window = careerCalendar.IsTransferWindowOpen ? "TRANSFER WINDOW OPEN" : "WINDOW CLOSED";
                bylineLabel.text = $"Manager {managerName}   ·   {careerCalendar.DisplayDate}   ·   {window}";
                bylineLabel.ForceMeshUpdate();
            }

            // Unread badge (session 13) - "INBOX (2)" style, same NormalizeButtonLabel
            // convention every other Hub button's label goes through. Playtest report
            // (2026-08-16, "no way of knowing an injury happened unless you go into
            // Make Changes"): the "(N)" count alone was easy to miss since it's just a
            // label-text change with no colour difference from every other Hub button -
            // the button itself now stands out (Warning amber) whenever anything's
            // unread, reverting to the normal Hub-button colour once it's all read.
            if (inboxButton != null)
            {
                int unread = inbox.UnreadCount;
                string inboxLabel = unread > 0 ? $"INBOX ({unread})" : "INBOX";
                ManagerUITheme.NormalizeButtonLabel(inboxButton, inboxLabel, ManagerUITheme.TextBody, 17);

                if (inboxButton.TryGetComponent(out Image inboxButtonImage))
                {
                    inboxButtonImage.color = unread > 0 ? ManagerUITheme.Warning : ManagerUITheme.CardNeutral;
                }
            }

            bool hasNextFixture = currentFixtureIndex < managedTeamFixtures.Count;

            if (playNextMatchButton != null)
            {
                playNextMatchButton.interactable = hasNextFixture;
            }

            if (simulateSeasonButton != null)
            {
                simulateSeasonButton.interactable = hasNextFixture;
            }

            if (!hasNextFixture)
            {
                ShowEndOfSeasonPanel();
                return;
            }

            if (leagueTableView != null)
            {
                int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
                leagueTableView.Populate(playableTable.Sorted(), teamRegistry.GetTeamName, managedTeamId, GetRecentFormString);

                // Rows are cleared and rebuilt fresh every refresh (every return to the
                // Hub) - same rapid destroy/recreate churn as the Tactics Board's
                // pins/bench, same TMP mesh-generation flakiness risk.
                StartCoroutine(RecoverBlankLabelsNextFrame(leagueTableView.transform));
            }
        }

        // --- End of Season (career-arc addition): shown automatically once
        // managedTeamFixtures runs out, in place of the old dead end where Next
        // Matchday/Simulate Season just quietly disabled forever. "Start New Season"
        // performs the full rollover - see OnStartNewSeasonClicked. Built the same
        // code-built-panel/chrome-built-guard/Refresh pattern as the Tactics screen. ---

        private void ShowEndOfSeasonPanel()
        {
            if (!endOfSeasonChromeBuilt)
            {
                BuildEndOfSeasonChrome();
                endOfSeasonChromeBuilt = true;
            }

            // Guarded so re-entering this panel (e.g. RefreshHubUI firing again before
            // Start New Season is clicked) can't pay out prize money/board boost twice
            // for the same season - applied exactly once, the first time the season
            // actually ends.
            if (!seasonEndRewardsAppliedForCurrentSeason)
            {
                ApplySeasonEndRewards();
                seasonEndRewardsAppliedForCurrentSeason = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (endOfSeasonPanel != null) endOfSeasonPanel.SetActive(true);

            RefreshEndOfSeasonUI();
        }

        // Career-arc addition, Phase 4 (session 8) - league finish prize money and a
        // separate board confidence budget boost, both position-scaled (see
        // ManagerCareerHistory), both land in the same transfer budget Phase 3 spends
        // from. Recorded as a SeasonRecord for the Trophy Room regardless of amount -
        // even a poor season's minimal prize money is worth a row in the history.
        private void ApplySeasonEndRewards()
        {
            List<LeagueTable.Entry> finalTable = playableTable.Sorted();
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            int finalPosition = finalTable.Count;
            LeagueTable.Entry managedEntry = null;

            for (int i = 0; i < finalTable.Count; i++)
            {
                if (finalTable[i].TeamId == managedTeamId)
                {
                    finalPosition = i + 1;
                    managedEntry = finalTable[i];
                    break;
                }
            }

            float prizeMoney = ManagerCareerHistory.GetPrizeMoney(finalPosition);
            float boardBoost = ManagerCareerHistory.GetBoardBoost(finalPosition);

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            finance.AdjustBudget(managedTeamName, prizeMoney + boardBoost);

            lastSeasonRecord = new SeasonRecord
            {
                Season = currentSeason,
                FinalPosition = finalPosition,
                IsChampion = finalPosition == 1,
                PrizeMoney = prizeMoney,
                BoardBoost = boardBoost,
                Wins = managedEntry?.Wins ?? 0,
                Draws = managedEntry?.Draws ?? 0,
                Losses = managedEntry?.Losses ?? 0,
                Points = managedEntry?.Points ?? 0
            };

            careerHistory.AddRecord(lastSeasonRecord);

            // Tier 1 potentialemails.txt batch (#28/#29, session 14) - a top-half finish
            // reads as season success, bottom-half as disappointment. Simple top-half
            // cutoff rather than anything tied to pre-season expectations (there's no
            // real "expected finish" concept in this prototype to compare against).
            bool isTopHalf = finalPosition <= Mathf.Max(1, finalTable.Count / 2);
            inbox.Add(InboxMessageType.EndOfSeason, "Season Review",
                isTopHalf
                    ? $"The season has concluded, and the board is pleased with the progress made. Finishing {finalPosition}{GetOrdinalSuffix(finalPosition)} reflects good management, tactical decision-making, and effective squad use. This has been a strong foundation to build on."
                    : $"The season has concluded, and results have fallen short of expectations. A {finalPosition}{GetOrdinalSuffix(finalPosition)}-place finish had positive moments, but not enough consistency across the campaign. The board will review the situation carefully before deciding the next steps.",
                careerCalendar.CurrentDayNumber);
        }

        private void BuildEndOfSeasonChrome()
        {
            if (seasonHubPanel == null || seasonHubPanel.transform.parent == null)
            {
                return;
            }

            endOfSeasonPanel = new GameObject("EndOfSeasonPanel", typeof(RectTransform));
            endOfSeasonPanel.transform.SetParent(seasonHubPanel.transform.parent, false);
            RectTransform panelRect = endOfSeasonPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(endOfSeasonPanel);
            ManagerUITheme.ApplyDiagonalGradientBackground(endOfSeasonPanel, ManagerUITheme.Background, ManagerUITheme.GradientEnd);

            GameObject header = ManagerUITheme.BuildAccentBand(endOfSeasonPanel.transform, topBand: true, height: 100f);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(700f, 40f));
            ManagerUITheme.BuildLabel(titleObj.transform, "SEASON COMPLETE", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(endOfSeasonPanel.transform, false);
            endOfSeasonContentContainer = contentObj.GetComponent<RectTransform>();
            endOfSeasonContentContainer.anchorMin = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.anchorMax = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.pivot = new Vector2(0.5f, 0.5f);
            endOfSeasonContentContainer.sizeDelta = new Vector2(700f, 400f);
            endOfSeasonContentContainer.anchoredPosition = new Vector2(0f, 40f);

            ManagerUITheme.BuildAccentBand(endOfSeasonPanel.transform, topBand: false, height: 110f);

            Button startNewSeasonButton = ManagerUITheme.BuildButton(endOfSeasonPanel.transform, "START NEW SEASON", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 17);
            ManagerUITheme.SetPointAnchor(startNewSeasonButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(320f, 52f));
            startNewSeasonButton.onClick.AddListener(OnStartNewSeasonClicked);

            endOfSeasonPanel.SetActive(false);
        }

        // Content (final position, and from Phase 4 onward prize money/board boost/
        // trophy) is rebuilt fresh every time this screen opens, same destroy/recreate
        // pattern as the Tactics screen's dropdown rows - cheap here since it only runs
        // once per season, not on a hot path.
        private void RefreshEndOfSeasonUI()
        {
            if (endOfSeasonContentContainer == null)
            {
                return;
            }

            foreach (GameObject element in spawnedEndOfSeasonElements)
            {
                if (element != null) Destroy(element);
            }
            spawnedEndOfSeasonElements.Clear();

            List<LeagueTable.Entry> finalTable = playableTable.Sorted();
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            int finalPosition = 0;
            for (int i = 0; i < finalTable.Count; i++)
            {
                if (finalTable[i].TeamId == managedTeamId)
                {
                    finalPosition = i + 1;
                    break;
                }
            }

            GameObject seasonLabelObj = new GameObject("SeasonLabel", typeof(RectTransform));
            seasonLabelObj.transform.SetParent(endOfSeasonContentContainer, false);
            RectTransform seasonLabelRect = seasonLabelObj.GetComponent<RectTransform>();
            seasonLabelRect.anchorMin = new Vector2(0.5f, 1f);
            seasonLabelRect.anchorMax = new Vector2(0.5f, 1f);
            seasonLabelRect.pivot = new Vector2(0.5f, 1f);
            seasonLabelRect.sizeDelta = new Vector2(700f, 30f);
            seasonLabelRect.anchoredPosition = Vector2.zero;
            ManagerUITheme.BuildLabel(seasonLabelObj.transform, $"SEASON {currentSeason}", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            spawnedEndOfSeasonElements.Add(seasonLabelObj);

            string positionSuffix = GetOrdinalSuffix(finalPosition);
            GameObject positionObj = new GameObject("Position", typeof(RectTransform));
            positionObj.transform.SetParent(endOfSeasonContentContainer, false);
            RectTransform positionRect = positionObj.GetComponent<RectTransform>();
            positionRect.anchorMin = new Vector2(0.5f, 1f);
            positionRect.anchorMax = new Vector2(0.5f, 1f);
            positionRect.pivot = new Vector2(0.5f, 1f);
            positionRect.sizeDelta = new Vector2(700f, 90f);
            positionRect.anchoredPosition = new Vector2(0f, -46f);
            Color positionColor = finalPosition == 1 ? ManagerUITheme.Accent : ManagerUITheme.TextPrimary;
            string positionText = finalPosition == 1
                ? $"CHAMPIONS! {managedTeamName.ToUpperInvariant()} WIN THE LEAGUE"
                : $"{managedTeamName.ToUpperInvariant()} FINISHED {finalPosition}{positionSuffix}";
            ManagerUITheme.BuildLabel(positionObj.transform, positionText, 26, positionColor, TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);
            spawnedEndOfSeasonElements.Add(positionObj);

            // Prize money and board boost (career-arc addition, Phase 4) - kept as two
            // explicitly separate lines, matching how Thomas framed these as distinct
            // mechanisms even though both land in the same transfer budget.
            if (lastSeasonRecord != null && lastSeasonRecord.Season == currentSeason)
            {
                GameObject prizeObj = new GameObject("PrizeMoney", typeof(RectTransform));
                prizeObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform prizeRect = prizeObj.GetComponent<RectTransform>();
                prizeRect.anchorMin = new Vector2(0.5f, 1f);
                prizeRect.anchorMax = new Vector2(0.5f, 1f);
                prizeRect.pivot = new Vector2(0.5f, 1f);
                prizeRect.sizeDelta = new Vector2(700f, 26f);
                prizeRect.anchoredPosition = new Vector2(0f, -130f);
                ManagerUITheme.BuildLabel(prizeObj.transform, $"Prize money:  £{lastSeasonRecord.PrizeMoney:F1}m", 17, ManagerUITheme.TextBody, TextAlignmentOptions.Center);
                spawnedEndOfSeasonElements.Add(prizeObj);

                GameObject boostObj = new GameObject("BoardBoost", typeof(RectTransform));
                boostObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform boostRect = boostObj.GetComponent<RectTransform>();
                boostRect.anchorMin = new Vector2(0.5f, 1f);
                boostRect.anchorMax = new Vector2(0.5f, 1f);
                boostRect.pivot = new Vector2(0.5f, 1f);
                boostRect.sizeDelta = new Vector2(700f, 26f);
                boostRect.anchoredPosition = new Vector2(0f, -160f);
                string boostText = lastSeasonRecord.BoardBoost > 0f
                    ? $"Board have boosted your transfer budget:  £{lastSeasonRecord.BoardBoost:F1}m"
                    : "Board: no additional backing this season";
                ManagerUITheme.BuildLabel(boostObj.transform, boostText, 17, lastSeasonRecord.BoardBoost > 0f ? ManagerUITheme.Accent : ManagerUITheme.TextMuted, TextAlignmentOptions.Center);
                spawnedEndOfSeasonElements.Add(boostObj);

                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
                float budget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

                GameObject budgetObj = new GameObject("BudgetTotal", typeof(RectTransform));
                budgetObj.transform.SetParent(endOfSeasonContentContainer, false);
                RectTransform budgetRect = budgetObj.GetComponent<RectTransform>();
                budgetRect.anchorMin = new Vector2(0.5f, 1f);
                budgetRect.anchorMax = new Vector2(0.5f, 1f);
                budgetRect.pivot = new Vector2(0.5f, 1f);
                budgetRect.sizeDelta = new Vector2(700f, 26f);
                budgetRect.anchoredPosition = new Vector2(0f, -196f);
                ManagerUITheme.BuildLabel(budgetObj.transform, $"Transfer budget:  £{budget:F1}m", 19, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
                spawnedEndOfSeasonElements.Add(budgetObj);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(endOfSeasonContentContainer));
        }

        private static string GetOrdinalSuffix(int n)
        {
            int lastTwo = n % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return "TH";
            }

            switch (n % 10)
            {
                case 1: return "ST";
                case 2: return "ND";
                case 3: return "RD";
                default: return "TH";
            }
        }

        public void OnStartNewSeasonClicked()
        {
            AdvanceCalendarTo(new DateTime(careerCalendar.SeasonStartYear + 1, 6, 1), stopForNewInboxMessage: false);
            currentSeason++;
            careerCalendar.StartSeason(FirstCareerSeasonStartYear + currentSeason - 1);
            seasonEndRewardsAppliedForCurrentSeason = false;

            AgeAndReloadFixturesForNewSeason();

            // Reads this season's now-final appearance counts (managed team only) before
            // ResetForNewSeason wipes them below - order matters here.
            ApplyPlayerDevelopmentAndRetirements();

            // Loan system (session 9) - fixed-duration loans (per Thomas: until end of
            // season, no manual recall), so every active loan returns right here.
            ReturnLoanedPlayersForNewSeason();

            DeductManagedTeamWageBill();

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            // Bug fix (session 9, live bug report): playableTable.Reset() above clears
            // PL/GD/PTS for the new season, but recentFormByTeamId was never cleared
            // alongside it - a club continuing into the new season kept showing last
            // season's Form strip (slowly overwritten 5 results at a time) while any
            // club new to this season's fixture file correctly showed blank, making the
            // mismatch obvious side-by-side.
            recentFormByTeamId.Clear();

            // Season-scoped Inbox-tick state (session 14) - same reasoning as
            // recentFormByTeamId just above: everything here is either "once per
            // season" (mid-season review) or keyed off a streak/cooldown that no longer
            // means anything once the fixture list and matchday count reset to 0.
            // injuredPlayersTracked specifically mirrors ManagerSquadRoles.ResetForNewSeason
            // clearing injuryReturnMatchday for every squad's roles right below.
            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();

            // A still-armed next-match-only override has nothing left to revert to once
            // the fixture list itself has rolled over - dropped rather than carried into
            // a season it was never meant for.
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;

            currentFixtureIndex = 0;
            simulatedMatchdays.Clear();
            transferNegotiation.ForceResolveAllPending(finance, managedTeamName, inbox, FindTeamContainingPlayer, careerCalendar.CurrentDayNumber);

            foreach (ManagerSquadRoles roles in squadRolesByTeamName.Values)
            {
                roles.ResetForNewSeason();
            }

            if (endOfSeasonPanel != null) endOfSeasonPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Ages every already-generated player (squads and reserve pools alike) by one
        // year, then reloads next season's real fixture calendar - cycling through
        // seasonFile + trainingSeasonFiles (both already real Premier League season
        // files used elsewhere in this controller) rather than replaying the exact same
        // 380 fixtures every year. Falls back to the career's original seasonFile if no
        // pool candidate actually features managedTeamName (a genuine historical season
        // it wasn't in the top flight for), since an empty managedTeamFixtures would
        // otherwise silently break the whole matchday loop.
        private void AgeAndReloadFixturesForNewSeason()
        {
            foreach (AgentTeam team in squadsByTeamName.Values)
            {
                foreach (PlayerAgent player in team.Players) player.Age += 1;
            }

            // Discovered-but-unclaimed youth prospects keep developing whether or not
            // you've brought them into the Academy yet (session 13 mission rework) - no
            // more age-out-and-replace, the 3-matchday poach timer already keeps this
            // list from accumulating indefinitely (see ManagerScouting).
            scouting.AgeDiscoveredProspects();

            // Youth academy (session 9) - same "keeps developing whether or not you're
            // watching" reasoning as the scouting pool above.
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                player.Age += 1;
            }

            // Session 16 - Thomas: "the premier league teams stay the same season to
            // season... no relegation for the MSc version." This used to cycle through
            // every real historical season file (trainingSeasonFiles), picking a
            // different one each rollover - since real Premier League rosters genuinely
            // differ year to year, that silently swapped which 20 clubs the career was
            // even about (a team relegated in real history, e.g. Huddersfield Town,
            // could reappear mid-career with zero relegation/promotion actually
            // simulated). Dishonest for a project whose whole premise is a trained,
            // real-data-backed league - now always reuses the exact same seasonFile
            // season 1 started with, so the 20 clubs (and their trained
            // StatisticalModel strength) never change for the rest of the career.
            // trainingSeasonFiles is untouched elsewhere (TrainStatisticalModel still
            // combines all of them for strength training) - this only affects which
            // file drives THIS career's own fixture list/roster.
            allSeasonFixtures = OpenFootballTextParser.ParseSeasonFile(seasonFile.text, seasonFile.name);
            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);
            availableTeamNames = BuildAvailableTeamNames();
        }

        // Applied league-wide (every already-generated club, not just managedTeamName) -
        // otherwise only the user's own squad would ever improve and the league would
        // go static, the opposite of how real football ages. Only the managed team's
        // appearances are actually tracked (see ManagerSquadRoles), so everyone else
        // gets a flat assumed playing-time factor rather than real per-player data -
        // still moves Overall in a realistic direction, just without the extra
        // precision that data doesn't exist for.
        private const float AssumedPlayingTimeFactorAiFirstTeam = 0.65f;
        private const float AssumedPlayingTimeFactorUncalledReserve = 0.15f;
        private const float AssumedPlayingTimeFactorYouthProspect = 0.1f;

        // Higher than the AI-first-team assumption above - the whole point of a loan
        // (session 9) is escaping a bench role for regular minutes elsewhere, which
        // this game doesn't simulate match-by-match, so the assumption reflects that
        // intent directly.
        private const float AssumedPlayingTimeFactorOnLoan = 0.8f;

        private void ApplyPlayerDevelopmentAndRetirements()
        {
            foreach (KeyValuePair<string, AgentTeam> entry in squadsByTeamName)
            {
                string teamName = entry.Key;
                AgentTeam team = entry.Value;
                bool isManagedTeam = teamName == managedTeamName;
                ManagerSquadRoles roles = isManagedTeam ? GetOrCreateSquadRoles(teamName) : null;

                foreach (PlayerAgent player in team.Players)
                {
                    if (isManagedTeam)
                    {
                        // Growth/decline already happened via ApplyMatchdayProgression
                        // ticks all season (see ApplyMatchdayConditionAndInjuries) - only
                        // erosion (a real season-end verdict, not something to tick per
                        // match, see ApplySeasonEndErosion's comment) and the delta-badge
                        // snapshot/finalize pair still happen here.
                        float seasonPlayingTimeFactor = Mathf.Clamp01(roles.GetAppearancesThisSeason(player) / 25f);

                        ManagerPlayerDevelopment.FinalizeSeasonDelta(player);
                        ManagerPlayerDevelopment.ApplySeasonEndErosion(player, seasonPlayingTimeFactor);
                        ManagerPlayerDevelopment.ApplySeasonEndNoiseIfPrimeAge(player);
                        ManagerPlayerDevelopment.SnapshotSeasonStart(player);
                    }
                    else
                    {
                        float playingTime = team.Reserves.Contains(player)
                            ? AssumedPlayingTimeFactorUncalledReserve
                            : AssumedPlayingTimeFactorAiFirstTeam;
                        ManagerPlayerDevelopment.ApplySeasonProgression(player, playingTime);
                    }
                }

                ApplyRetirementsForTeam(teamName, team);

                // Live team strength (session 16) - after growth/decline and retirement
                // replacements have both landed for this team, so the recalculation sees
                // this season's real final squad, not a stale one.
                RecalculateLiveTeamStrength(teamName, team);
            }

            // Discovered-but-unclaimed youth prospects (session 8, Phase 2; mission
            // rework session 13) - no real matches at all, so a low playing-time
            // assumption for growth-rate purposes, but exempted from neglect erosion
            // (see ApplySeasonProgression's own comment - they can't accrue real senior
            // appearances at this age, so a low factor here was never meant to read as
            // "being neglected").
            foreach (PlayerAgent player in scouting.DiscoveredProspects)
            {
                ManagerPlayerDevelopment.ApplySeasonProgression(player, AssumedPlayingTimeFactorYouthProspect, exemptFromErosion: true);
            }

            // Youth academy - growth moved to a per-matchday tick in session 16 (see
            // ApplyMatchdayAcademyProgression, called from SimulateFixture alongside the
            // managed team's own tick) - academy kids no longer get a season-end lump
            // sum here at all, matching how the managed squad itself works. Nothing left
            // to do for them at rollover: erosion was already exempt (they structurally
            // can't have real senior appearances at this age) and there's no delta badge
            // or prime-age noise that applies to a 14-16 year old.
        }

        // Loan system (session 9) - a loaned player isn't in ANY team's Players list
        // right now (removed from the squad entirely by OnLoanOutClicked), so they're
        // untouched by AgeAndReloadFixturesForNewSeason and the per-team loop above -
        // aged and developed here instead, then handed back to their origin squad's
        // Bench (not Starting XI - they need to earn that back, same as any other
        // returning/newly-available player).
        private void ReturnLoanedPlayersForNewSeason()
        {
            List<ManagerLoanTracker.LoanRecord> returned = loanTracker.ReturnAllLoansForNewSeason();

            foreach (ManagerLoanTracker.LoanRecord loan in returned)
            {
                loan.Player.Age += 1;
                ManagerPlayerDevelopment.ApplySeasonProgression(loan.Player, AssumedPlayingTimeFactorOnLoan);

                if (squadsByTeamName.TryGetValue(loan.OriginTeamName, out AgentTeam originTeam))
                {
                    originTeam.AddSquadPlayer(loan.Player);
                }
            }
        }

        // Replaces any retiree in place (whichever list/index they were in - starter,
        // bench, or Players) with a freshly generated player at the same position and
        // current team strength, rather than removing and leaving a hole. Preserves
        // StartingEleven slot order, which the Tactics Board relies on (see AgentTeam.
        // SubstitutePlayer's own comment on the same constraint).
        private void ApplyRetirementsForTeam(string teamName, AgentTeam team)
        {
            List<PlayerAgent> retirees = new List<PlayerAgent>();

            foreach (PlayerAgent player in team.Players)
            {
                if (ManagerPlayerDevelopment.RollRetirement(player))
                {
                    retirees.Add(player);
                }
            }

            if (retirees.Count == 0)
            {
                return;
            }

            // Retirement announcement (session 14, Thomas's own suggestion while this
            // batch was being wired) - managed team only, same scope limit as every
            // other Inbox trigger; an AI club's retirements are invisible replacements,
            // nothing the manager would ever be told about. Sent here at season
            // rollover, before currentFixtureIndex resets to 0 for the new season (see
            // OnStartNewSeasonClicked), so matchdayReceived reads as 0 like the other
            // new-season messages (Welcome/Season Expectations use the same convention).
            if (teamName == managedTeamName)
            {
                foreach (PlayerAgent retiree in retirees)
                {
                    inbox.Add(InboxMessageType.Retirement, $"{retiree.Name} Retires",
                        $"{retiree.Name} has announced their retirement from professional football at age {retiree.Age}, bringing the curtain down on their playing career. " +
                        "Everyone at the club thanks them for their contribution and wishes them well for the future.",
                        0);
                }
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);

            foreach (PlayerAgent retiree in retirees)
            {
                PlayerAgent replacement = squadGenerator.GenerateReservePlayer(retiree.PrimaryPosition, strength.AttackStrength, strength.DefenceStrength);

                int startingIndex = team.StartingEleven.IndexOf(retiree);
                int benchIndex = team.Bench.IndexOf(retiree);
                int reserveIndex = team.Reserves.IndexOf(retiree);
                int playersIndex = team.Players.IndexOf(retiree);

                if (startingIndex >= 0)
                {
                    team.StartingEleven[startingIndex] = replacement;
                    replacement.IsStartingEleven = true;
                }
                else if (benchIndex >= 0)
                {
                    team.Bench[benchIndex] = replacement;
                    replacement.IsStartingEleven = false;
                }
                else if (reserveIndex >= 0)
                {
                    team.Reserves[reserveIndex] = replacement;
                    replacement.IsStartingEleven = false;
                }

                if (playersIndex >= 0)
                {
                    team.Players[playersIndex] = replacement;
                }
            }
        }

        // Only the managed team's budget is ever spent or displayed (see the Transfer
        // Market screen below) - AI clubs never buy or sell anything (explicit scope
        // boundary, see HANDOFF), so there's no point maintaining an accurate wage bill
        // for squads nobody ever checks the finances of.
        private void DeductManagedTeamWageBill()
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team))
            {
                return;
            }

            float totalWage = 0f;
            foreach (PlayerAgent player in team.Players)
            {
                totalWage += ManagerClubFinance.GetAnnualWage(player);
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            finance.AdjustBudget(managedTeamName, -totalWage);

            // Playtest report (2026-08-16, "can't bid on a player in season 2") - this
            // deduction was previously invisible everywhere (not shown on the End-of-
            // Season screen, no Inbox message), so a wage bill big enough to zero out
            // the budget looked like bidding itself was broken. finance.AdjustBudget is
            // deliberately unclamped (a real overspend consequence, not a bug to hide),
            // but the player needs to be told it happened.
            inbox.Add(InboxMessageType.WageBill, "Annual Wage Bill Paid",
                $"The squad's annual wages of £{totalWage:F1}m have been deducted from the transfer budget. Remaining budget: £{finance.GetBudget(managedTeamName):F1}m.",
                careerCalendar.CurrentDayNumber);
        }

        // --- Save / load (career-arc addition, session 8, Phase 5) - see
        // Manager/Save/ManagerSaveData.cs for the deliberate scope limits (managed team
        // only; condition/injuries/appearances reset). BuildSaveData/ApplySaveData are
        // the only places that translate between live state and the DTOs - everywhere
        // else in this file is untouched by save/load existing at all. ---

    }
}
