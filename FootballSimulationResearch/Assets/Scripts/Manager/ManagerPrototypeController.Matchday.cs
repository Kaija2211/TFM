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
        // --- Matchday Prep (opponent scouting, Mentality, pre-match Subs - shown before
        // every match instead of simulating straight from the Hub) ---

        public void OnNextMatchdayClicked()
        {
            if (currentFixtureIndex >= managedTeamFixtures.Count)
            {
                return;
            }

            DateTime fixtureDate = careerCalendar.GetFixtureDate(currentFixtureIndex);
            if (careerCalendar.CurrentDate.Date < fixtureDate.Date)
            {
                // CONTINUE means one calendar day. Reaching the next fixture can take
                // several presses and may be interrupted by Inbox events; only a press
                // made on the actual fixture date opens Matchday Prep.
                DateTime nextDay = careerCalendar.CurrentDate.Date.AddDays(1);
                bool interrupted = AdvanceCalendarTo(nextDay, stopForNewInboxMessage: true);
                RefreshHubUI();
                return;
            }

            currentFixture = managedTeamFixtures[currentFixtureIndex];

            ShowMatchdayPrep();
        }

        private bool AdvanceCalendarTo(DateTime targetDate, bool stopForNewInboxMessage)
        {
            while (careerCalendar.CurrentDate.Date < targetDate.Date)
            {
                int messageCountBefore = inbox.Messages.Count;
                bool windowWasOpen = careerCalendar.IsTransferWindowOpen;
                careerCalendar.AdvanceOneDay();
                int currentDay = careerCalendar.CurrentDayNumber;

                scouting.ResolveDailyTick(currentDay, squadGenerator, inbox);
                transferNegotiation.ResolveDueTransferScoutAssignments(currentDay, inbox, FindTeamContainingPlayer);
                transferNegotiation.ResolveDueBids(currentDay, finance, managedTeamName, inbox, FindTeamContainingPlayer);
                ResolveOutgoingTransferInterest(currentDay);
                transferNegotiation.ResolveExpiredSignatures(currentDay, finance, managedTeamName, inbox);
                ResolveDailyInjuryRecoveries();

                if (windowWasOpen != careerCalendar.IsTransferWindowOpen)
                {
                    bool opened = careerCalendar.IsTransferWindowOpen;
                    inbox.Add(InboxMessageType.RecruitmentTeaser,
                        opened ? "Transfer Window Open" : "Transfer Window Closed",
                        opened
                            ? "The transfer window is now open. Registered transfers can be completed until the deadline."
                            : "The transfer window has closed. New registered transfers must wait for the next window.",
                        currentDay);
                }

                if (stopForNewInboxMessage && inbox.Messages.Count > messageCountBefore)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveDailyInjuryRecoveries()
        {
            if (injuredPlayersTracked.Count == 0) return;

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            List<PlayerAgent> recovered = new List<PlayerAgent>();
            foreach (PlayerAgent player in injuredPlayersTracked)
            {
                if (!roles.IsInjured(player, careerCalendar.CurrentDayNumber)) recovered.Add(player);
            }

            foreach (PlayerAgent player in recovered)
            {
                injuredPlayersTracked.Remove(player);
                inbox.Add(InboxMessageType.Recovery, $"{player.Name} Fit Again",
                    $"{player.Name} has recovered from injury and is available for selection again.",
                    careerCalendar.CurrentDayNumber);
            }
        }

        private void ShowMatchdayPrep()
        {
            if (!matchdayPrepChromeBuilt)
            {
                BuildMatchdayPrepChrome();
                matchdayPrepChromeBuilt = true;
            }

            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(true);

            // Mentality pills belong to live Match Day now, not scouting - but they're the
            // same shared Button instances Match Day reparents into its own footer, and
            // that reparenting only happens lazily the first time BuildMatchdayChrome runs
            // (first Simulate Match click). Until then they're still sitting wherever they
            // started (originally hand-placed under MatchdayPrepPanel), so explicitly hide
            // them here rather than relying on that lazy reparent to have already happened.
            if (attackingButton != null) attackingButton.gameObject.SetActive(false);
            if (balancedButton != null) balancedButton.gameObject.SetActive(false);
            if (defensiveButton != null) defensiveButton.gameObject.SetActive(false);

            RefreshMatchdayPrepUI();
        }

        // Header/footer accent bands + the two title labels, built once. The labels'
        // actual text is filled in per-fixture by RefreshMatchdayPrepUI.
        private void BuildMatchdayPrepChrome()
        {
            if (matchdayPrepContentContainer == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(matchdayPrepPanel);

            const float bandHeight = 90f;

            GameObject header = ManagerUITheme.BuildAccentBand(matchdayPrepContentContainer, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-120f, 34f);
            titleRect.anchoredPosition = new Vector2(60f, -22f);
            ManagerUITheme.BuildLabel(titleObj.transform, "", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            matchdayPrepTitleLabel = titleObj;

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-120f, 34f);
            subtitleRect.anchoredPosition = new Vector2(60f, -52f);
            ManagerUITheme.BuildLabel(subtitleObj.transform, "", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            matchdayPrepSubtitleLabel = subtitleObj;

            ManagerUITheme.BuildAccentBand(matchdayPrepContentContainer, topBand: false, height: bandHeight);

            // Footer action pair, right-aligned per the design mockup ("Back to Hub" /
            // "Simulate Match ->"). These two were never positioned - both sat stacked at
            // (0,0), so the unstyled Back button (still showing its default Editor label)
            // rendered on top of and completely hid the correctly-styled Simulate Match button.
            if (simulateMatchButton != null)
            {
                ManagerUITheme.SetPointAnchor(simulateMatchButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-60f, 22f), new Vector2(220f, 50f));
            }

            if (matchdayPrepBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(matchdayPrepBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-292f, 22f), new Vector2(170f, 50f));
            }

            // Two-column body: opponent scout list (left, flexible width) beside a
            // read-only opponent-formation pitch (right, 620px, matching the mockup).
            // Both fill the row between the header/footer bands.
            const float sideMargin = 60f;
            const float columnGap = 48f;
            const float opponentPitchWidth = 620f;
            const float rowMargin = bandHeight + 24f;

            if (opponentSquadListView != null)
            {
                RectTransform opponentListRect = opponentSquadListView.GetComponent<RectTransform>();
                opponentListRect.anchorMin = new Vector2(0f, 0f);
                opponentListRect.anchorMax = new Vector2(1f, 1f);
                opponentListRect.offsetMin = new Vector2(sideMargin, rowMargin);
                // Expressed as a fixed offset from the RIGHT anchor (reserving exactly
                // "pitch + gap + margin" worth of space) rather than a literal 1920-based
                // left position - see the pitch's own comment below for why that
                // distinction actually matters here, not just style.
                opponentListRect.offsetMax = new Vector2(-(opponentPitchWidth + columnGap + sideMargin), -rowMargin);
                opponentSquadListView.gameObject.SetActive(true);

                // This ScrollView's own background Image was never recolored - it was
                // always retired/hidden before now, so its default Unity light-grey
                // "Background" sprite was never actually visible on screen. Now that
                // it's shown for the first time, that unstyled default shows through as
                // a plain grey/white box behind the rows.
                if (opponentSquadListView.TryGetComponent(out Image opponentListImage))
                {
                    opponentListImage.color = ManagerUITheme.PanelDark;
                }
            }

            // Right-anchored (point anchor at the container's top-right corner, pivot to
            // match) rather than a fixed left-relative "pitchLeft" computed from a
            // literal 1920 container width. CanvasScaler's actual effective canvas width
            // only equals the 1920 reference when the window's aspect ratio is exactly
            // 16:9 - in any other window size/aspect (i.e. not maximized/fullscreen) the
            // real container came out 2117 units wide in one live measurement, not 1920.
            // The scout list's own offsetMax above is expressed relative to the RIGHT
            // anchor, so it already scales correctly with the container's true width -
            // but this pitch was anchored from the LEFT at a fixed literal-1920-derived
            // offset, which does NOT scale, so the two drifted apart and the list
            // visibly overlapped the pitch's left edge (confirmed live, exactly the
            // "tactic board is behind the list view" report - not a z-order/sibling-index
            // bug as originally assumed, a genuine position mismatch that just happened
            // to look like a z-order issue).
            GameObject pitchColumnCaption = new GameObject("OpponentShapeCaption", typeof(RectTransform));
            pitchColumnCaption.transform.SetParent(matchdayPrepContentContainer, false);
            RectTransform pitchCaptionRect = pitchColumnCaption.GetComponent<RectTransform>();
            pitchCaptionRect.anchorMin = new Vector2(1f, 1f);
            pitchCaptionRect.anchorMax = new Vector2(1f, 1f);
            pitchCaptionRect.pivot = new Vector2(1f, 1f);
            pitchCaptionRect.anchoredPosition = new Vector2(-sideMargin, -rowMargin);
            pitchCaptionRect.sizeDelta = new Vector2(opponentPitchWidth, 20f);
            ManagerUITheme.BuildLabel(pitchColumnCaption.transform, "OPPONENT SHAPE", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject pitchObj = new GameObject("OpponentPitch", typeof(RectTransform), typeof(Image));
            pitchObj.transform.SetParent(matchdayPrepContentContainer, false);
            matchdayPrepPitchContainer = pitchObj.GetComponent<RectTransform>();
            // Vertically stretched (anchorMin/Max.y = 0/1) with top/bottom offsets,
            // exactly like opponentListRect right above - NOT a point anchor with a
            // manually-snapshotted sizeDelta.height (what this used to be). That snapshot
            // was computed once, from matchdayPrepContentContainer.rect.height, at chrome-
            // build time only - correct for whatever window size was active the very
            // first time Matchday Prep was ever shown, but frozen forever after and wrong
            // for any other window size, including the same window resized later in the
            // same session (confirmed live, 2026-08-09 session 7: fine on Matchday 1,
            // visibly elongated past the footer buttons by Matchday 2 after a window
            // resize in between - real recurrence of the exact drift class already
            // documented in the comment above for width/position, this time for height).
            // Stretch anchors recompute automatically on every layout pass, so this can't
            // go stale again regardless of when or how many times the window resizes.
            matchdayPrepPitchContainer.anchorMin = new Vector2(1f, 0f);
            matchdayPrepPitchContainer.anchorMax = new Vector2(1f, 1f);
            matchdayPrepPitchContainer.pivot = new Vector2(1f, 1f);
            matchdayPrepPitchContainer.offsetMax = new Vector2(-sideMargin, -(rowMargin + 30f));
            matchdayPrepPitchContainer.offsetMin = new Vector2(-(sideMargin + opponentPitchWidth), rowMargin);
            pitchObj.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            BuildPitchMarkings(matchdayPrepPitchContainer);

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPrepContentContainer));
        }

        private void RefreshMatchdayPrepUI()
        {
            bool managedIsHome = currentFixture.HomeTeam == managedTeamName;
            string opponentName = managedIsHome ? currentFixture.AwayTeam : currentFixture.HomeTeam;
            AgentTeam opponentTeam = GetOrCreateAgentTeam(opponentName);
            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            ManagerTacticalSliders opponentTactics = ManagerAiTacticalPlanner.Choose(
                opponentName, opponentTeam.Formation, managedTeamName, managedTeam.Formation, !managedIsHome);
            ManagerTacticalShape.Matchup tacticalRead = managedIsHome
                ? ManagerTacticalShape.BuildMatchup(managedTeamName, managedTeam.Formation, tacticalSliders,
                    opponentName, opponentTeam.Formation, opponentTactics, managedTeam, GetOrCreateSquadRoles(managedTeamName), opponentTeam, null)
                : ManagerTacticalShape.BuildMatchup(opponentName, opponentTeam.Formation, opponentTactics,
                    managedTeamName, managedTeam.Formation, tacticalSliders, opponentTeam, null, managedTeam, GetOrCreateSquadRoles(managedTeamName));

            if (matchdayPrepTitleLabel != null)
            {
                TextMeshProUGUI titleTMP = matchdayPrepTitleLabel.GetComponentInChildren<TextMeshProUGUI>();
                if (titleTMP != null)
                {
                    titleTMP.text = managedIsHome
                        ? $"{managedTeamName} vs {opponentName} (Home)"
                        : $"{managedTeamName} vs {opponentName} (Away)";
                }
            }

            if (matchdayPrepSubtitleLabel != null)
            {
                TextMeshProUGUI subtitleTMP = matchdayPrepSubtitleLabel.GetComponentInChildren<TextMeshProUGUI>();
                if (subtitleTMP != null)
                {
                    subtitleTMP.text = $"Matchday {currentFixture.Matchday}   ·   Opponent Formation: {TacticsBoardLayout.FormatFormation(opponentTeam.Formation)}\n" +
                        ManagerTacticalShape.DescribeForTeam(tacticalRead, managedTeamName);
                }
            }

            // Read-only scouting list - Starting XI + Bench, no row click handler (null
            // onRowClicked means SquadListView.AddPlayerGridRow builds no Button at all).
            if (opponentSquadListView != null)
            {
                opponentSquadListView.Clear();
                opponentSquadListView.AddGridHeaderRow();
                opponentSquadListView.AddSectionHeader("Starting XI");

                List<PlayerPosition> slots = squadGenerator.GetStartingPositions(opponentTeam.Formation);

                for (int i = 0; i < opponentTeam.StartingEleven.Count; i++)
                {
                    PlayerAgent player = opponentTeam.StartingEleven[i];
                    PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                    opponentSquadListView.AddPlayerGridRow(player, slot.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), null);
                }

                opponentSquadListView.AddSectionHeader($"Bench ({opponentTeam.Bench.Count})");

                foreach (PlayerAgent player in opponentTeam.Bench)
                {
                    opponentSquadListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), null);
                }

                opponentSquadListView.AddSectionHeader($"Reserves ({opponentTeam.Reserves.Count})");
                foreach (PlayerAgent player in opponentTeam.Reserves)
                {
                    opponentSquadListView.AddPlayerGridRow(player, player.PrimaryPosition.ToString(), GetDisplayRating(player.GetOverallRating()), GetRatingPercent(player), null);
                }
            }

            RefreshMatchdayPrepOpponentPitch(opponentTeam);

            // Scout list rows and opponent pitch pins are both cleared and rebuilt
            // fresh every refresh - same rapid destroy/recreate churn as the Tactics
            // Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPrepContentContainer));
        }

        // Read-only opponent-formation pitch - shares BuildPitchMarkings and the
        // BuildPitchPinVisual helper with the interactive Tactics Board, but never adds
        // a TacticsBoardPlayerCard (no drag, no drop, no tap), and uses the Danger red
        // border color instead of Accent green to read as "not yours to touch".
        private void RefreshMatchdayPrepOpponentPitch(AgentTeam opponentTeam)
        {
            if (matchdayPrepPitchContainer == null)
            {
                return;
            }

            for (int i = matchdayPrepPitchContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = matchdayPrepPitchContainer.GetChild(i);

                if (child.name.StartsWith("OpponentPin_"))
                {
                    Destroy(child.gameObject);
                }
            }

            IReadOnlyList<Vector2> pins = TacticsBoardLayout.GetPins(opponentTeam.Formation);
            List<PlayerPosition> slots = squadGenerator.GetStartingPositions(opponentTeam.Formation);

            for (int i = 0; i < opponentTeam.StartingEleven.Count && i < pins.Count; i++)
            {
                PlayerAgent player = opponentTeam.StartingEleven[i];
                PlayerPosition slot = i < slots.Count ? slots[i] : player.PrimaryPosition;
                Vector2 anchor = new Vector2(pins[i].x, 1f - pins[i].y);

                // labelFontSize bumped 10->12 - below that read as genuinely too small to
                // make out a name at a glance (confirmed live, Thomas couldn't read them).
                // Tactics Board's own pins use circleSize 68/labelFontSize 14 for
                // comparison - this pitch is deliberately smaller (shares the screen with
                // the OVR/Rating list), so it gets a smaller but still legible size rather
                // than matching exactly.
                ManagerUITheme.BuildPitchPinVisual(
                    matchdayPrepPitchContainer,
                    $"OpponentPin_{player.Name}",
                    anchor,
                    // circleSize stays at the original 48 (not bumped alongside the font
                    // sizes) - it directly drives labelWidth (circleSize + 70), and a
                    // wider box was enough to tip two closely-spaced CM pins into visual
                    // overlap in some formations (confirmed live). Keeping the same box
                    // width and only growing the text inside it gets the readability win
                    // without the collision risk.
                    circleSize: 48f,
                    borderColor: ManagerUITheme.Danger,
                    ratingText: GetDisplayRating(player.GetOverallRating()).ToString(),
                    ratingFontSize: 14,
                    labelText: $"{player.Name} · {slot}",
                    labelFontSize: 14);
            }
        }

        public void OnMatchdayPrepBackClicked()
        {
            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Header (toolbar + score flanked by team names), footer (tactic readout +
        // Continue), and the Key Moments/Match Stats body columns - built once. The old
        // fixtureTitleText/matchStatsText free-text fields are retired in favor of this
        // (kept assigned in the Inspector, just hidden - see HeaderText/etc. precedent
        // on the Hub). No possession stat: this project never implemented one (no real
        // data source for it - see HANDOFF.md), so unlike the design mockup, only shots
        // (a real tracked stat) get a bar here.
        private void BuildMatchdayChrome()
        {
            if (matchdayPanel == null)
            {
                return;
            }

            ManagerUITheme.ApplyPanelBackground(matchdayPanel);

            // Grown from 110 to 170 - at 110 the enlarged score/clock (52pt/16pt, up from
            // the pre-1920x1080-pass 32pt/13pt) no longer fit inside the band at all; the
            // clock specifically rendered ~30px below the header's own bottom divider,
            // spilling into the body content underneath it (confirmed by the numbers:
            // old clock offset -122 with height 20 spans down to -142, well past a
            // 110-tall band). 170 gives the mockup's own header proportions room to
            // breathe (its own header is a content-sized flex column, not a fixed-height
            // band, but works out to roughly this tall once padding/gaps are accounted
            // for).
            const float headerHeight = 170f;
            const float footerHeight = 90f;

            // Live ratings grid (session 10) - a full-width band of 11 player cards
            // sitting just above the footer. The gap it lives in wasn't actually free
            // space - it was the Match Log's own reserved scroll area, which simply
            // hadn't filled up with enough events yet to visually reach the bottom early
            // in a match. Genuinely claiming this height (shrinking the event feed mask
            // below) rather than just drawing on top of it, so the two never overlap
            // once the log grows.
            const float ratingsGridHeight = 108f;
            const float ratingsGridGap = 16f;
            const float ratingsGridBottomOffset = footerHeight + ratingsGridGap;

            ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: true, height: headerHeight);
            GameObject footerBand = ManagerUITheme.BuildAccentBand(matchdayPanel.transform, topBand: false, height: footerHeight);

            if (fixtureTitleText != null) fixtureTitleText.gameObject.SetActive(false);
            if (matchStatsText != null) matchStatsText.gameObject.SetActive(false);

            // --- Toolbar: Skip to Results (existing, repositioned) / Pause ---
            // No more "Tactics / Subs" placeholder here - real, working Mentality pills and
            // a Substitutions section are now directly on this screen (see below), so a
            // separate disabled button pointing at the same functionality would just be
            // redundant/confusing.
            if (skipToResultsButton != null)
            {
                ManagerUITheme.SetPointAnchor(skipToResultsButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-30f, -14f), new Vector2(150f, 30f));
                StyleHubActionButton(skipToResultsButton);
                ManagerUITheme.NormalizeButtonLabel(skipToResultsButton, "SKIP TO RESULTS", ManagerUITheme.TextBody, 12);
            }

            pauseButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "PAUSE", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-188f, -14f), new Vector2(90f, 30f));
            pauseButton.onClick.AddListener(OnPauseClicked);

            // Session 16 - moved here from its old spot beneath the Subs Made log
            // (Thomas's follow-up: even after that relocation, a busy match with 5-6
            // subs still grew tall enough to reach it). A fixed-position button below a
            // list that can grow arbitrarily tall was always going to collide again
            // eventually - the header toolbar is a position nothing else ever grows
            // into, so it can't recur here. Left of Pause with the same 8px gap Pause
            // itself keeps from Skip to Results.
            Button makeChangesButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(makeChangesButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-286f, -14f), new Vector2(140f, 30f));
            makeChangesButton.onClick.AddListener(OnOpenTacticsBoardDuringMatchClicked);

            // --- Score row: team names flank a centered score + minute/LIVE tag ---
            // Vertical rhythm redone against the new 170px header: names/score both
            // start around the same top offset so their centers roughly line up (the
            // score box is taller to fit its much bigger digits), clock sits in the gap
            // below the score, all comfortably inside the band now instead of spilling
            // past its bottom edge.
            if (scoreText != null)
            {
                ManagerUITheme.SetPointAnchor(scoreText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(260f, 68f));
                scoreText.fontSize = 52;
                scoreText.alignment = TextAlignmentOptions.Center;
                scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            }

            // Bumped from 14 to 16 ("the ticker could be bigger") and repositioned to
            // sit directly under the score within the taller header.
            if (clockText != null)
            {
                ManagerUITheme.SetPointAnchor(clockText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(220f, 26f));
                clockText.alignment = TextAlignmentOptions.Center;
                clockText.fontSize = 16;
            }

            GameObject homeNameObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform homeNameRect = homeNameObj.GetComponent<RectTransform>();
            homeNameRect.anchorMin = new Vector2(0.5f, 1f);
            homeNameRect.anchorMax = new Vector2(0.5f, 1f);
            homeNameRect.pivot = new Vector2(1f, 1f);
            homeNameRect.anchoredPosition = new Vector2(-150f, -64f);
            homeNameRect.sizeDelta = new Vector2(300f, 40f);
            matchHomeNameLabel = ManagerUITheme.BuildLabel(homeNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayNameObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayNameObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform awayNameRect = awayNameObj.GetComponent<RectTransform>();
            awayNameRect.anchorMin = new Vector2(0.5f, 1f);
            awayNameRect.anchorMax = new Vector2(0.5f, 1f);
            awayNameRect.pivot = new Vector2(0f, 1f);
            awayNameRect.anchoredPosition = new Vector2(150f, -64f);
            awayNameRect.sizeDelta = new Vector2(300f, 40f);
            matchAwayNameLabel = ManagerUITheme.BuildLabel(awayNameObj.transform, "", 24, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject fullTimeCaptionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            fullTimeCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(fullTimeCaptionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 20f));
            matchFullTimeCaptionLabel = ManagerUITheme.BuildLabel(fullTimeCaptionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            matchFullTimeCaptionGroup = fullTimeCaptionObj;
            matchFullTimeCaptionGroup.SetActive(false);

            // Goals: full-time only, built from the real ScorerName on each goal event (see
            // AgentMatchSimulator), never fabricated or parsed out of the free-text event
            // description. Previously a tiny pair of labels (12pt, autosizing down to a 7pt
            // floor) flanking dead-center below the header - illegible at a glance
            // (confirmed live, user feedback). Redesigned again after the first left/right-
            // half pass left a large empty band below both halves for a typical (non-8-goal)
            // scoreline (confirmed live, user feedback, screenshot) - compact team-labeled
            // scorer lists stay up top, and a large, full-width goal timeline (spanning the
            // whole screen, not just the left half) with minute labels on each marker now
            // fills that space instead of sitting cramped and small next to it.
            const float goalsBlockTop = -(headerHeight + 20f);
            const float halfMargin = 40f;
            // Left half is anchor x 0-0.5 (960px at 1920-wide); this is its usable width
            // with halfMargin on both the true left edge and the center-side, matching the
            // right half's own rightHalfWidth in the full-time repositioning block below.
            const float scorersWidth = 880f;

            GameObject goalsCaptionObj = new GameObject("GoalsCaption", typeof(RectTransform));
            goalsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(goalsCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, goalsBlockTop), new Vector2(400f, 20f));
            goalsCaptionObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            ManagerUITheme.BuildLabel(goalsCaptionObj.transform, "GOALS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Side-by-side columns (not stacked) - each list is self-labeled with its own
            // team name (see PopulateGoalScorerLists) since position alone doesn't imply
            // which team the way left-vs-right flanking across the whole screen used to.
            // Kept compact (not the full available height) now that the big timeline below
            // is the section's main visual - this is scannable detail, not the centerpiece.
            const float scorersTop = goalsBlockTop - 34f;
            const float scorersColumnGap = 40f;
            const float scorersColumnWidth = (scorersWidth - scorersColumnGap) / 2f;
            const float scorersBlockHeight = 140f;

            GameObject homeScorersObj = new GameObject("HomeScorers", typeof(RectTransform));
            homeScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(homeScorersObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, scorersTop), new Vector2(scorersColumnWidth, scorersBlockHeight));
            homeScorersObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            matchHomeScorersLabel = ManagerUITheme.BuildLabel(homeScorersObj.transform, "", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchHomeScorersLabel.spriteAsset = footballIconSpriteAsset;
            // An unusually long one-sided scoreline can still need more lines than this box
            // allows - autosizing shrinks the font to fit rather than overflowing into the
            // timeline below.
            matchHomeScorersLabel.enableAutoSizing = true;
            matchHomeScorersLabel.fontSizeMin = 13;
            matchHomeScorersLabel.fontSizeMax = 20;

            GameObject awayScorersObj = new GameObject("AwayScorers", typeof(RectTransform));
            awayScorersObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(awayScorersObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin + scorersColumnWidth + scorersColumnGap, scorersTop), new Vector2(scorersColumnWidth, scorersBlockHeight));
            awayScorersObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            matchAwayScorersLabel = ManagerUITheme.BuildLabel(awayScorersObj.transform, "", 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, noWrap: false);
            if (footballIconSpriteAsset != null) matchAwayScorersLabel.spriteAsset = footballIconSpriteAsset;
            matchAwayScorersLabel.enableAutoSizing = true;
            matchAwayScorersLabel.fontSizeMin = 13;
            matchAwayScorersLabel.fontSizeMax = 20;

            // Big full-width timeline - starts below BOTH the scorer lists above (left
            // half) and Match Stats (right half, see the full-time repositioning block
            // below, which ends around -550) so it never runs underneath either at any
            // x-position, then has the whole rest of the panel down to the footer to work
            // with. Bigger markers (26px, was 14) and a minute label on each one now that
            // there's room, plus it spans corner-to-corner instead of being confined to the
            // left half.
            const float bigTimelineWidth = 1840f; // full 1920 width minus halfMargin each side
            const float bigTimelineY = -730f;
            matchGoalTimelineWidth = bigTimelineWidth;

            GameObject timelineTrackObj = new GameObject("GoalTimelineTrack", typeof(RectTransform), typeof(Image));
            timelineTrackObj.transform.SetParent(matchdayPanel.transform, false);
            ManagerUITheme.SetPointAnchor(timelineTrackObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(halfMargin, bigTimelineY), new Vector2(bigTimelineWidth, 4f));
            timelineTrackObj.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            timelineTrackObj.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            GameObject timelineMarkersObj = new GameObject("GoalTimelineMarkers", typeof(RectTransform));
            timelineMarkersObj.transform.SetParent(matchdayPanel.transform, false);
            matchGoalTimelineContainer = timelineMarkersObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(matchGoalTimelineContainer, new Vector2(0f, 1f), new Vector2(halfMargin, bigTimelineY), new Vector2(bigTimelineWidth, 4f));
            matchGoalTimelineContainer.pivot = new Vector2(0f, 0.5f);

            matchFullTimeOnlyElements = new List<GameObject> { goalsCaptionObj, timelineTrackObj, timelineMarkersObj, homeScorersObj, awayScorersObj };
            goalsCaptionObj.SetActive(false);
            timelineTrackObj.SetActive(false);
            timelineMarkersObj.SetActive(false);
            homeScorersObj.SetActive(false);
            awayScorersObj.SetActive(false);

            matchLiveOnlyElements = new[] { pauseButton.gameObject, makeChangesButton.gameObject, skipToResultsButton != null ? skipToResultsButton.gameObject : null, clockText != null ? clockText.gameObject : null };

            // --- Body: Key Moments (left) / Match Stats (right) ---
            GameObject keyMomentsCaptionObj = new GameObject("MatchLogCaption", typeof(RectTransform));
            keyMomentsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            matchKeyMomentsCaptionRect = keyMomentsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(matchKeyMomentsCaptionRect, new Vector2(0f, 1f), new Vector2(40f, -(headerHeight + 28f)), new Vector2(400f, 20f));
            ManagerUITheme.BuildLabel(keyMomentsCaptionObj.transform, "MATCH LOG", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            if (eventFeedText != null)
            {
                // RectMask2D only clips CHILDREN of the GameObject it's on, not a
                // Graphic on that same object - putting it directly on eventFeedText
                // (as a previous pass here did) clips nothing. It needs to be a real
                // parent, with eventFeedText reparented inside it and stretched to fill.
                GameObject maskObj = new GameObject("EventFeedMask", typeof(RectTransform), typeof(RectMask2D));
                maskObj.transform.SetParent(matchdayPanel.transform, false);
                RectTransform maskRect = maskObj.GetComponent<RectTransform>();
                maskRect.anchorMin = new Vector2(0f, 0f);
                maskRect.anchorMax = new Vector2(0.55f, 1f);
                // Bottom edge raised from footerHeight+24 to clear the ratings grid band
                // now sitting between this and the footer - see ratingsGridBottomOffset.
                maskRect.offsetMin = new Vector2(40f, ratingsGridBottomOffset + ratingsGridHeight + ratingsGridGap);
                maskRect.offsetMax = new Vector2(-20f, -(headerHeight + 56f));

                eventFeedText.transform.SetParent(maskRect, false);
                RectTransform eventRect = eventFeedText.GetComponent<RectTransform>();
                eventRect.anchorMin = Vector2.zero;
                eventRect.anchorMax = Vector2.one;
                eventRect.offsetMin = Vector2.zero;
                eventRect.offsetMax = Vector2.zero;
                // Bumped from 15 with added line spacing - the log's own mask has
                // plenty of vertical room (footerHeight+24 to headerHeight+56, hundreds
                // of px), so there's no risk of maxVisibleEventLines worth of lines
                // overflowing the hard RectMask2D clip at this size.
                eventFeedText.fontSize = 19;
                eventFeedText.lineSpacing = 14f;

                // Live feed is now row-based (see AppendMatchEventRow) so each event gets
                // its own bottom divider, matching the mockup's per-line
                // "border-bottom:1px solid #1e2a3d" - eventFeedText itself is a
                // pre-existing Inspector-wired SerializeField, kept in the hierarchy
                // (still reparented/sized above) but disabled and never given text again,
                // rather than touching its scene wiring.
                eventFeedText.gameObject.SetActive(false);

                GameObject feedRowsObj = new GameObject("EventFeedRows", typeof(RectTransform));
                feedRowsObj.transform.SetParent(maskRect, false);
                matchEventFeedContainer = feedRowsObj.GetComponent<RectTransform>();
                matchEventFeedContainer.anchorMin = Vector2.zero;
                matchEventFeedContainer.anchorMax = Vector2.one;
                matchEventFeedContainer.offsetMin = Vector2.zero;
                matchEventFeedContainer.offsetMax = Vector2.zero;

                VerticalLayoutGroup feedLayout = feedRowsObj.AddComponent<VerticalLayoutGroup>();
                feedLayout.childForceExpandWidth = true;
                feedLayout.childForceExpandHeight = false;
                feedLayout.childControlWidth = true;
                feedLayout.childControlHeight = true;
                feedLayout.spacing = 0f;

                // Hidden entirely at full-time - the design moves the full event list to
                // its own separate "Match Events" screen instead of showing it inline here.
                matchLogGroup = maskObj;
            }

            viewMatchEventsButton = ManagerUITheme.BuildButton(matchdayPanel.transform, "VIEW MATCH EVENTS", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            viewMatchEventsButton.onClick.AddListener(OnViewMatchEventsClicked);
            viewMatchEventsButton.gameObject.SetActive(false);
            matchFullTimeOnlyElements.Add(viewMatchEventsButton.gameObject);

            // --- Right column: Match Stats (unchanged position, x=0.55) ---
            // --- Far-right column: Substitutions (top) then Make Changes (below) ---
            // Session 16 (Thomas, screenshot with two drawn boxes) - Subs Made used to
            // stack directly above Match Stats in the SAME 0.55-anchored column, with
            // Make Changes and the Stats caption pinned at fixed y-offsets below it. Its
            // ContentSizeFitter grows the log taller as more subs get made, but nothing
            // below it ever moved to make room - by the 2nd-3rd sub the growing list
            // physically overlapped Make Changes and Match Stats. Moved to its own
            // right-edge-anchored column instead, clear of both. Right-edge anchor
            // (anchorMax=anchorMin=pivot=(1,1)) means SetPointAnchor's pivot==anchor
            // behavior is already correct here (unlike the old x=0.55 "left edge
            // reference" usage, which needed an explicit pivot.x=0 override to stop the
            // element straddling its own anchor point) - the column's right edge sits at
            // the anchor, growing left/down from there, exactly what a right-margin-
            // flush column needs.
            GameObject subsCaptionObj = new GameObject("SubsMadeCaption", typeof(RectTransform));
            subsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsCaptionRect = subsCaptionObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsCaptionRect, new Vector2(1f, 1f), new Vector2(-halfMargin, -(headerHeight + 28f)), new Vector2(300f, 20f));
            ManagerUITheme.BuildLabel(subsCaptionObj.transform, "SUBS MADE  ·  MANAGE VIA TACTICS BOARD", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            // Read-only log of subs made this match (see matchSubsLog) - populated by
            // RefreshMatchSubsMadeList, one row per entry. Subs themselves happen on the
            // Tactics Board via "Make Changes" below, not here - no picker on this screen.
            GameObject subsLogObj = new GameObject("SubsLog", typeof(RectTransform));
            subsLogObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform subsLogRect = subsLogObj.GetComponent<RectTransform>();
            ManagerUITheme.SetPointAnchor(subsLogRect, new Vector2(1f, 1f), new Vector2(-halfMargin, -(headerHeight + 54f)), new Vector2(300f, 76f));
            matchSubsLogContainer = subsLogRect;

            VerticalLayoutGroup subsLogLayout = subsLogObj.AddComponent<VerticalLayoutGroup>();
            subsLogLayout.childForceExpandWidth = true;
            subsLogLayout.childForceExpandHeight = false;
            subsLogLayout.childControlWidth = true;
            subsLogLayout.childControlHeight = true;
            subsLogLayout.spacing = 6f;

            ContentSizeFitter subsLogFitter = subsLogObj.AddComponent<ContentSizeFitter>();
            subsLogFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Subs Made is a live-match-only concept - the design's Full-Time Summary has
            // no equivalent section at all, so this whole column needs to disappear at
            // full-time exactly like the tactic pills do. Make Changes moved up to the
            // header toolbar (see pauseButton's own block above) - no longer built here.
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = subsLogObj;

            // Session 16 - top-aligned to the same y-offset as Match Log/Subs Made
            // (Thomas's explicit ask) rather than starting partway down the panel -
            // there's nothing above it in this column anymore now that Subs Made has
            // its own column and Make Changes lives in the header.
            GameObject statsCaptionObj = new GameObject("MatchStatsCaption", typeof(RectTransform));
            statsCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform statsCaptionRect2 = statsCaptionObj.GetComponent<RectTransform>();
            matchStatsCaptionRect = statsCaptionRect2;
            ManagerUITheme.SetPointAnchor(statsCaptionRect2, new Vector2(0.55f, 1f), new Vector2(20f, -(headerHeight + 28f)), new Vector2(360f, 20f));
            statsCaptionRect2.pivot = new Vector2(0f, 1f);
            matchStatsCaptionLabel = ManagerUITheme.BuildLabel(statsCaptionObj.transform, "MATCH STATS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject statsBarsObj = new GameObject("MatchStatsBars", typeof(RectTransform));
            statsBarsObj.transform.SetParent(matchdayPanel.transform, false);
            matchStatsBarsContainer = statsBarsObj.GetComponent<RectTransform>();
            matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
            matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
            // 28px below the caption's new top-aligned start (headerHeight + 28), same
            // internal caption-to-bars gap as before this section moved up.
            matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 56f));
            // Grown from 140 (1 row: Shots) to fit 4 rows (Possession/Chances Created/
            // Shots/Shots on Target) at 36px pitch each.
            matchStatsBarsContainer.sizeDelta = new Vector2(360f, 190f);

            // --- Live ratings grid: full-width strip of 11 player cards, bottom-anchored
            // just above the footer (see ratingsGridHeight/ratingsGridBottomOffset at the
            // top of this method). Live-only (session 10 fix, live bug report) - the
            // original design kept this visible through Full Time as a "final ratings"
            // readout, but Full Time's own layout (goal timeline + scorer lists) uses
            // that same bottom region and the two overlapped in practice. Added to
            // matchLiveOnlyElements below, same as Subs Made/Make Changes, so it's gone
            // by the time Full Time's layout takes over.
            GameObject ratingsGridCaptionObj = new GameObject("RatingsGridCaption", typeof(RectTransform));
            ratingsGridCaptionObj.transform.SetParent(matchdayPanel.transform, false);
            RectTransform ratingsGridCaptionRect = ratingsGridCaptionObj.GetComponent<RectTransform>();
            ratingsGridCaptionRect.anchorMin = new Vector2(0f, 0f);
            ratingsGridCaptionRect.anchorMax = new Vector2(0f, 0f);
            ratingsGridCaptionRect.pivot = new Vector2(0f, 0f);
            ratingsGridCaptionRect.anchoredPosition = new Vector2(halfMargin, ratingsGridBottomOffset + ratingsGridHeight + 6f);
            ratingsGridCaptionRect.sizeDelta = new Vector2(400f, 20f);
            ManagerUITheme.BuildLabel(ratingsGridCaptionObj.transform, "PLAYER RATINGS", 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject ratingsGridObj = new GameObject("RatingsGrid", typeof(RectTransform));
            ratingsGridObj.transform.SetParent(matchdayPanel.transform, false);
            matchRatingsGridContainer = ratingsGridObj.GetComponent<RectTransform>();
            matchRatingsGridContainer.anchorMin = new Vector2(0f, 0f);
            matchRatingsGridContainer.anchorMax = new Vector2(1f, 0f);
            matchRatingsGridContainer.pivot = new Vector2(0f, 0f);
            matchRatingsGridContainer.offsetMin = new Vector2(halfMargin, ratingsGridBottomOffset);
            matchRatingsGridContainer.offsetMax = new Vector2(-halfMargin, ratingsGridBottomOffset + ratingsGridHeight);

            HorizontalLayoutGroup ratingsGridLayout = ratingsGridObj.AddComponent<HorizontalLayoutGroup>();
            ratingsGridLayout.childForceExpandWidth = true;
            ratingsGridLayout.childForceExpandHeight = true;
            ratingsGridLayout.childControlWidth = true;
            ratingsGridLayout.childControlHeight = true;
            ratingsGridLayout.spacing = 8f;

            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = ratingsGridCaptionObj;
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = ratingsGridObj;

            // --- Footer: live Mentality pills (left, real - reused from Matchday Prep,
            // which no longer needs them since it's scouting-only now; now genuinely
            // live too, see ApplyLiveMentalityChangeIfMatchInProgress) + Continue (right) ---
            GameObject mentalityLabelObj = new GameObject("MentalityFooterCaption", typeof(RectTransform));
            mentalityLabelObj.transform.SetParent(footerBand.transform, false);
            RectTransform mentalityLabelRect = mentalityLabelObj.GetComponent<RectTransform>();
            mentalityLabelRect.anchorMin = new Vector2(0f, 0.5f);
            mentalityLabelRect.anchorMax = new Vector2(0f, 0.5f);
            mentalityLabelRect.pivot = new Vector2(0f, 0.5f);
            mentalityLabelRect.anchoredPosition = new Vector2(40f, 0f);
            mentalityLabelRect.sizeDelta = new Vector2(90f, 26f);
            ManagerUITheme.BuildLabel(mentalityLabelObj.transform, "MENTALITY", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
            matchLiveOnlyElements[^1] = mentalityLabelObj;

            // Repositioning alone isn't enough - these three are still children of
            // MatchdayPrepPanel (their original parent from before Matchday Prep was
            // simplified), so their visibility was still tied to THAT panel's active
            // state regardless of where their anchors pointed: visible on Matchday Prep
            // (wrong), invisible on Match Day (also wrong). Reparenting to footerBand
            // (matching tacticLabelObj above, so the anchor(0,0.5) math means the same
            // thing for all four) fixes both at once.
            if (attackingButton != null)
            {
                attackingButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(attackingButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(120f, 0f), new Vector2(120f, 44f));
                // Hand-placed Editor buttons, never routed through BuildButton - their
                // labels kept the Editor's original alignment/font/weight until now
                // (confirmed live: top-left aligned, non-bold, visibly different from
                // every other button in the app).
                ManagerUITheme.NormalizeButtonLabel(attackingButton, "ATTACKING", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = attackingButton.gameObject;
            }

            if (balancedButton != null)
            {
                balancedButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(balancedButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(250f, 0f), new Vector2(120f, 44f));
                ManagerUITheme.NormalizeButtonLabel(balancedButton, "BALANCED", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = balancedButton.gameObject;
            }

            if (defensiveButton != null)
            {
                defensiveButton.transform.SetParent(footerBand.transform, false);
                ManagerUITheme.SetPointAnchor(defensiveButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(380f, 0f), new Vector2(120f, 44f));
                ManagerUITheme.NormalizeButtonLabel(defensiveButton, "DEFENSIVE", ManagerUITheme.TextBody, 13);
                System.Array.Resize(ref matchLiveOnlyElements, matchLiveOnlyElements.Length + 1);
                matchLiveOnlyElements[^1] = defensiveButton.gameObject;
            }

            if (fullTimeContinueButton != null)
            {
                ManagerUITheme.SetPointAnchor(fullTimeContinueButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-40f, 20f), new Vector2(220f, 50f));
                if (fullTimeContinueButton.TryGetComponent(out Image continueImage))
                {
                    continueImage.color = ManagerUITheme.Accent;
                }

                ManagerUITheme.NormalizeButtonLabel(fullTimeContinueButton, "CONTINUE", ManagerUITheme.OnAccent, 15);
            }

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchdayPanel.transform));
        }

        public void OnPauseClicked()
        {
            matchPaused = !matchPaused;
            Time.timeScale = matchPaused ? 0f : 1f;

            if (pauseButton != null)
            {
                ManagerUITheme.NormalizeButtonLabel(pauseButton, matchPaused ? "RESUME" : "PAUSE", ManagerUITheme.TextBody, 12);
            }
        }

        // Rebuilds the "Subs Made" list from matchSubsLog - one row per entry, oldest
        // first. Called after ReplayMatchCoroutine starts (empty log) and again whenever
        // a sub lands via the Tactics Board mid-match (see
        // OnOpenTacticsBoardDuringMatchClicked/OnBenchPlayerDroppedOnPin).
        private void RefreshMatchSubsMadeList()
        {
            if (matchSubsLogContainer == null)
            {
                return;
            }

            foreach (Transform child in matchSubsLogContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (var entry in matchSubsLog)
            {
                GameObject row = new GameObject("SubRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                row.transform.SetParent(matchSubsLogContainer, false);

                LayoutElement layoutElement = row.GetComponent<LayoutElement>();
                layoutElement.preferredHeight = 32f;
                layoutElement.flexibleWidth = 1f;

                row.GetComponent<Image>().color = ManagerUITheme.PanelDark;

                // Oswald SDF has no arrow glyph (same reason ">" already stands in for
                // it elsewhere in this file, e.g. "NEXT >") - a literal "→" here just
                // silently prints as a blank space instead of erroring.
                string rowText = $"OUT {entry.offName} ({entry.offPosition})  >  IN {entry.onName} ({entry.onPosition})  {entry.minute}'";
                GameObject labelObj = new GameObject("Label", typeof(RectTransform));
                labelObj.transform.SetParent(row.transform, false);
                RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
                ManagerUITheme.BuildLabel(labelObj.transform, rowText, 13, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft);
            }

            // Rows are cleared and rebuilt fresh every time this runs - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchSubsLogContainer));
        }

        // Live ratings grid (session 10) - one card per CURRENT managed-team starter
        // (so a mid-match substitution swaps the card, not just the number inside it),
        // rebuilt fresh on every call same as RefreshMatchSubsMadeList above - called
        // once per revealed event during ReplayMatchCoroutine, so this genuinely does
        // run a lot over the course of a match; destroy/recreate at this frequency is
        // already the established pattern for this screen's other live lists, and 11
        // small cards is cheap.
        private void RefreshMatchRatingsGrid()
        {
            if (matchRatingsGridContainer == null)
            {
                return;
            }

            foreach (Transform child in matchRatingsGridContainer)
            {
                Destroy(child.gameObject);
            }

            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);

            foreach (PlayerAgent player in managedTeam.StartingEleven)
            {
                float rating = matchRatings.GetRating(player.Name);

                GameObject card = new GameObject($"RatingCard_{player.Name}", typeof(RectTransform), typeof(Image));
                card.transform.SetParent(matchRatingsGridContainer, false);
                card.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

                GameObject nameObj = new GameObject("Name", typeof(RectTransform));
                nameObj.transform.SetParent(card.transform, false);
                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.pivot = new Vector2(0.5f, 1f);
                nameRect.offsetMin = new Vector2(4f, -36f);
                nameRect.offsetMax = new Vector2(-4f, -6f);
                TextMeshProUGUI nameLabel = ManagerUITheme.BuildLabel(nameObj.transform, player.Name, 12, ManagerUITheme.TextMuted, TextAlignmentOptions.Top, FontStyles.Bold);
                nameLabel.enableAutoSizing = true;
                nameLabel.fontSizeMin = 9;
                nameLabel.fontSizeMax = 12;
                nameLabel.textWrappingMode = TextWrappingModes.NoWrap;

                GameObject ratingObj = new GameObject("Rating", typeof(RectTransform));
                ratingObj.transform.SetParent(card.transform, false);
                RectTransform ratingRect = ratingObj.GetComponent<RectTransform>();
                ratingRect.anchorMin = new Vector2(0f, 0f);
                ratingRect.anchorMax = new Vector2(1f, 1f);
                ratingRect.offsetMin = new Vector2(4f, 8f);
                ratingRect.offsetMax = new Vector2(-4f, -38f);
                ManagerUITheme.BuildLabel(ratingObj.transform, rating.ToString("F1"), 26, ManagerUITheme.RatingColor(rating * 10f), TextAlignmentOptions.Center, FontStyles.Bold);
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(matchRatingsGridContainer));
        }

        // Opens the Tactics Board mid-match so subs can be made via the same drag-drop
        // path used pre-match, uncapped (no MaxSubsPerMatch-style limit here, matching
        // pre-match behaviour) - replaces the old separate off-then-on picker flow.
        // Auto-pauses via the existing Pause toggle rather than a new mechanism.
        public void OnOpenTacticsBoardDuringMatchClicked()
        {
            if (!matchPaused)
            {
                OnPauseClicked();
            }

            if (!tacticsBoardChromeBuilt)
            {
                BuildTacticsBoardChrome();
                tacticsBoardChromeBuilt = true;
            }

            if (matchdayPanel != null) matchdayPanel.SetActive(false);
            if (tacticsBoardPanel != null) tacticsBoardPanel.SetActive(true);

            BeginMidMatchTacticsDraft();

            RefreshTacticsBoardUI();
        }

        private void ShowHalfTimePanel(int homeShots, int awayShots, int homeShotsOnTarget, int awayShotsOnTarget, int homeAttackEvents, int awayAttackEvents)
        {
            if (halfTimePanel == null)
            {
                halfTimePanel = new GameObject("HalfTimePanel", typeof(RectTransform), typeof(Image));
                halfTimePanel.transform.SetParent(matchdayPanel.transform, false);
                RectTransform panelRect = halfTimePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                halfTimePanel.GetComponent<Image>().color = ManagerUITheme.Background;

                GameObject title = new GameObject("Title", typeof(RectTransform));
                title.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(title.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(500f, 44f));
                ManagerUITheme.BuildLabel(title.transform, "HALF TIME", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

                GameObject score = new GameObject("Score", typeof(RectTransform));
                score.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(score.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -155f), new Vector2(500f, 70f));
                halfTimeScoreLabel = ManagerUITheme.BuildLabel(score.transform, "0 - 0", 52, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

                GameObject stats = new GameObject("Stats", typeof(RectTransform));
                stats.transform.SetParent(halfTimePanel.transform, false);
                ManagerUITheme.SetPointAnchor(stats.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(680f, 260f));
                halfTimeStatsLabel = ManagerUITheme.BuildLabel(stats.transform, string.Empty, 18, ManagerUITheme.TextBody, TextAlignmentOptions.Center);

                Button changes = ManagerUITheme.BuildButton(halfTimePanel.transform, "MAKE CHANGES", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 14);
                ManagerUITheme.SetPointAnchor(changes.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-120f, 80f), new Vector2(210f, 48f));
                changes.onClick.AddListener(OnOpenTacticsBoardDuringMatchClicked);

                Button resume = ManagerUITheme.BuildButton(halfTimePanel.transform, "START SECOND HALF", ManagerUITheme.Accent, Color.white, 14);
                ManagerUITheme.SetPointAnchor(resume.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(120f, 80f), new Vector2(210f, 48f));
                resume.onClick.AddListener(OnResumeFromHalfTimeClicked);
            }

            int totalAttacks = Mathf.Max(1, homeAttackEvents + awayAttackEvents);
            int homePossession = Mathf.RoundToInt(homeAttackEvents * 100f / totalAttacks);
            halfTimeScoreLabel.text = $"{liveHomeGoalsSoFar} - {liveAwayGoalsSoFar}";
            halfTimeStatsLabel.text =
                $"POSSESSION      {homePossession}%     {100 - homePossession}%\n\n" +
                $"CHANCES CREATED      {homeAttackEvents}     {awayAttackEvents}\n\n" +
                $"SHOTS      {homeShots}     {awayShots}\n\n" +
                $"SHOTS ON TARGET      {homeShotsOnTarget}     {awayShotsOnTarget}";
            halfTimePanel.SetActive(true);
            halfTimePanel.transform.SetAsLastSibling();
            waitingAtHalfTime = true;
            matchPaused = true;
            Time.timeScale = 0f;
        }

        private void OnResumeFromHalfTimeClicked()
        {
            waitingAtHalfTime = false;
            matchPaused = false;
            Time.timeScale = 1f;
            if (halfTimePanel != null) halfTimePanel.SetActive(false);
            if (pauseButton != null) ManagerUITheme.NormalizeButtonLabel(pauseButton, "PAUSE", ManagerUITheme.TextBody, 12);
        }

        private void BeginMidMatchTacticsDraft()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            midMatchDraftFormation = team.Formation;
            midMatchDraftStartingEleven = new List<PlayerAgent>(team.StartingEleven);
            midMatchDraftBench = new List<PlayerAgent>(team.Bench);
            midMatchDraftReserves = new List<PlayerAgent>(team.Reserves);
            tacticsBoardOpenedMidMatch = true;
        }

        private bool TryCommitMidMatchTacticsDraft()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            if (midMatchDraftStartingEleven == null) return true;

            List<PlayerAgent> incoming = team.StartingEleven.Where(player => !midMatchDraftStartingEleven.Contains(player)).ToList();
            List<PlayerAgent> outgoing = midMatchDraftStartingEleven.Where(player => !team.StartingEleven.Contains(player)).ToList();
            int remainingSubs = MaxSubsPerMatch - matchSubsLog.Count;
            if (incoming.Count > remainingSubs || incoming.Count != outgoing.Count)
            {
                ShowTacticsBoardWarning($"You can make {Mathf.Max(0, remainingSubs)} more substitution{(remainingSubs == 1 ? "" : "s")}");
                return false;
            }

            bool changed = team.Formation != midMatchDraftFormation
                || !team.StartingEleven.SequenceEqual(midMatchDraftStartingEleven);

            for (int i = 0; i < incoming.Count; i++)
            {
                PlayerAgent playerOn = incoming[i];
                PlayerAgent playerOff = outgoing[i];
                matchSubsLog.Add((playerOff.Name, playerOff.PrimaryPosition.ToString(), playerOn.Name, playerOn.PrimaryPosition.ToString(), currentMatchMinute));
                playersSubbedOffThisMatch.Add(playerOff);
                matchSimulator.RegisterSubstitution(playerOn, currentMatchMinute);
                matchRatings.EnsureTracked(playerOn.Name);
            }

            if (changed)
            {
                TriggerMidMatchResimulation();
                RefreshMatchSubsMadeList();
                RefreshMatchRatingsGrid();
            }

            midMatchDraftStartingEleven = null;
            midMatchDraftBench = null;
            midMatchDraftReserves = null;
            return true;
        }

        // Single proportional bar showing the home team's share of total shots (no
        // possession bar - see BuildMatchdayChrome comment on why).
        // Expanded from a single live Shots row to the same four real, derived stats the
        // full-time panel shows (see ShowFullTimeMatchStats) - Possession/Chances
        // Created update on every event now (not just shots), matching how
        // HomeTeamAttacking is set on every event in the ManagerSim fork.
        private void RefreshLiveMatchStats(
            int homeShots,
            int awayShots,
            int homeShotsOnTarget,
            int awayShotsOnTarget,
            int homeAttackEvents,
            int awayAttackEvents)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            int totalAttackEvents = homeAttackEvents + awayAttackEvents;
            int homePossessionPct = totalAttackEvents > 0
                ? Mathf.RoundToInt(100f * homeAttackEvents / totalAttackEvents)
                : 50;
            int awayPossessionPct = 100 - homePossessionPct;

            float y = 0f;
            y = BuildLiveStatRow("POSSESSION", homePossessionPct, awayPossessionPct, y, "%");
            y = BuildLiveStatRow("CHANCES CREATED", homeAttackEvents, awayAttackEvents, y);
            y = BuildLiveStatRow("SHOTS", homeShots, awayShots, y);
            BuildLiveStatRow("SHOTS ON TARGET", homeShotsOnTarget, awayShotsOnTarget, y);
        }

        private float BuildLiveStatRow(string label, int homeValue, int awayValue, float y, string valueSuffix = "")
        {
            int total = homeValue + awayValue;
            float homeSharePct = total > 0 ? homeValue / (float)total : 0.5f;

            GameObject row = new GameObject($"{label}Row", typeof(RectTransform));
            row.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -y);
            rowRect.sizeDelta = new Vector2(0f, 40f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.6f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = $"{label}   {homeValue}{valueSuffix} / {awayValue}{valueSuffix}";
            labelText.fontSize = 16;
            labelText.color = ManagerUITheme.TextBody;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject barObj = new GameObject("Bar", typeof(RectTransform));
            barObj.transform.SetParent(row.transform, false);
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            // Real two-team comparison (green/red split at the home team's actual share),
            // not just a single-color fill - managed-team-relative like the rest of this
            // session's coloring work, not simply home=green/away=red.
            bool managedIsHome = RequireActiveMatchFixture().HomeTeam == managedTeamName;
            Color homeColor = managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger;
            Color awayColor = managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent;
            ManagerUITheme.BuildSplitBar(barRect, homeSharePct, homeColor, awayColor, 6f);

            return y + 36f;
        }

        // Decorative equal-split bars (matching the design - the numbers carry the real
        // information, the bar underneath is just a visual accent) for shots and goals,
        // plus the tactic actually used, once the match has finished.
        //
        // Possession/Chances Created/Shots on Target are all real, derived numbers, not
        // invented ones - see the ManagerSim fork of AgentMatchSimulator for how. They
        // needed a genuine model change (an on/off-target split, and setting
        // HomeTeamAttacking on every event instead of just shots) that the protected
        // Sim.AgentMatchSimulator can't take, hence the fork.
        private void ShowFullTimeMatchStats(
            int homeShots,
            int awayShots,
            int homeShotsOnTarget,
            int awayShotsOnTarget,
            int homeAttackEvents,
            int awayAttackEvents,
            int homeGoals,
            int awayGoals)
        {
            if (matchStatsBarsContainer == null)
            {
                return;
            }

            foreach (Transform child in matchStatsBarsContainer)
            {
                Destroy(child.gameObject);
            }

            int totalAttackEvents = homeAttackEvents + awayAttackEvents;
            int homePossessionPct = totalAttackEvents > 0
                ? Mathf.RoundToInt(100f * homeAttackEvents / totalAttackEvents)
                : 50;
            int awayPossessionPct = 100 - homePossessionPct;

            float y = 0f;
            y = BuildFullTimeStatRow("POSSESSION", homePossessionPct, awayPossessionPct, y, "%");
            y = BuildFullTimeStatRow("CHANCES CREATED", homeAttackEvents, awayAttackEvents, y);
            y = BuildFullTimeStatRow("SHOTS", homeShots, awayShots, y);
            y = BuildFullTimeStatRow("SHOTS ON TARGET", homeShotsOnTarget, awayShotsOnTarget, y);
            y = BuildFullTimeStatRow("GOALS", homeGoals, awayGoals, y);
        }

        private float BuildFullTimeStatRow(string label, int homeValue, int awayValue, float y, string valueSuffix = "")
        {
            GameObject row = new GameObject($"{label}Row", typeof(RectTransform));
            row.transform.SetParent(matchStatsBarsContainer, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -y);
            rowRect.sizeDelta = new Vector2(0f, 40f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.6f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
            text.text = $"{homeValue}{valueSuffix}   {label}   {awayValue}{valueSuffix}";
            text.fontSize = 19;
            text.color = ManagerUITheme.TextBody;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject barsObj = new GameObject("Bars", typeof(RectTransform));
            barsObj.transform.SetParent(row.transform, false);
            RectTransform barsRect = barsObj.GetComponent<RectTransform>();
            barsRect.anchorMin = new Vector2(0f, 0f);
            barsRect.anchorMax = new Vector2(1f, 0.5f);
            barsRect.offsetMin = Vector2.zero;
            barsRect.offsetMax = Vector2.zero;

            // Real two-team comparison (e.g. 60/40 possession -> a 60% green / 40% red
            // split, not a decorative always-full bar) - was previously hardcoded to
            // pct=1f regardless of the actual values (confirmed live, user feedback).
            // Managed-team-relative like the rest of this session's coloring work, not
            // simply home=green/away=red.
            int total = homeValue + awayValue;
            float homeSharePct = total > 0 ? homeValue / (float)total : 0.5f;
            bool managedIsHome = RequireActiveMatchFixture().HomeTeam == managedTeamName;
            Color homeColor = managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger;
            Color awayColor = managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent;
            ManagerUITheme.BuildSplitBar(barsRect, homeSharePct, homeColor, awayColor, 6f);

            return y + 44f;
        }

        // --- Reusable confirm dialog (backlog items 13 + 15, session 11) - both "you
        // haven't set a Captain yet" and "are you sure you want to auto-skip the
        // season" are the same shape: a message + Confirm/Cancel. Built fresh each
        // time rather than chrome-cached, since content varies per call. ---

        private GameObject confirmDialogPanel;

        private void ShowConfirmDialog(string message, string confirmLabel, System.Action onConfirm, string cancelLabel, System.Action onCancel)
        {
            if (confirmDialogPanel != null)
            {
                Destroy(confirmDialogPanel);
            }

            Transform root = titlePanel.transform.parent;
            confirmDialogPanel = new GameObject("ConfirmDialogPanel", typeof(RectTransform), typeof(Image));
            confirmDialogPanel.transform.SetParent(root, false);
            RectTransform panelRect = confirmDialogPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            confirmDialogPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            // Last sibling so it renders above whatever screen is currently active,
            // regardless of that screen's own build order - same z-order technique as
            // the Tactics Board warning label.
            confirmDialogPanel.transform.SetAsLastSibling();

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(confirmDialogPanel.transform, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(640f, 260f);
            cardRect.anchoredPosition = Vector2.zero;
            card.GetComponent<Image>().color = ManagerUITheme.PanelDark;

            GameObject messageObj = new GameObject("Message", typeof(RectTransform));
            messageObj.transform.SetParent(card.transform, false);
            RectTransform messageRect = messageObj.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = new Vector2(0f, -40f);
            messageRect.sizeDelta = new Vector2(-80f, 140f);
            ManagerUITheme.BuildLabel(messageObj.transform, message, 17, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Normal, noWrap: false);

            Button confirmButton = ManagerUITheme.BuildButton(card.transform, confirmLabel, ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-90f, 36f), new Vector2(160f, 48f));
            confirmButton.onClick.AddListener(() => { CloseConfirmDialog(); onConfirm?.Invoke(); });

            Button cancelButton = ManagerUITheme.BuildButton(card.transform, cancelLabel, ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 15);
            ManagerUITheme.SetPointAnchor(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(90f, 36f), new Vector2(160f, 48f));
            cancelButton.onClick.AddListener(() => { CloseConfirmDialog(); onCancel?.Invoke(); });

            StartCoroutine(RecoverBlankLabelsNextFrame(confirmDialogPanel.transform));
        }

        private void CloseConfirmDialog()
        {
            if (confirmDialogPanel != null)
            {
                Destroy(confirmDialogPanel);
                confirmDialogPanel = null;
            }
        }

        // Session 16 - squad roles (captain/vice/penalty/FK/corner takers) were made
        // cosmetic-only (Thomas's explicit scope call: real mechanical effects tied to
        // assigning/not-assigning them were more headache than the feature was worth).
        // The pre-first-match "you haven't assigned a Captain" warning (backlog item 13,
        // session 11) no longer has anything real to warn about, so it's gone.
        public void OnSimulateMatchButtonClicked()
        {
            OnSimulateMatchClicked();
        }

        // Backlog item 15 (session 11) - Thomas: an accidental click currently costs the
        // whole rest of the season with no way back (see backlog item 10's collapse
        // finding, which this pairs with). Straightforward confirm-before-irreversible
        // pattern, same shape as item 13's dialog above.
        public void OnSimulateSeasonButtonClicked()
        {
            ShowConfirmDialog(
                "Simulate the rest of the season automatically? This can't be undone.",
                "SIMULATE SEASON", OnSimulateSeasonClicked,
                "CANCEL", null);
        }

        public void OnSimulateMatchClicked()
        {
            // Mentality has no pre-match picker (the Attacking/Balanced/Defensive buttons
            // only exist in the live match footer, see matchLiveOnlyElements) - so whatever
            // was left active at the end of the previous match (including a live change
            // made late on) would otherwise silently carry into this match's expected-goals
            // calc below, before the manager gets any chance to see or choose it again.
            // Reset here, before SimulateFixture uses selectedMentality, not after (where
            // matchSubsLog.Clear() sits below) - resetting after would still let this match
            // kick off on the stale value and only take effect from the match after.
            selectedMentality = ManagerMentality.Balanced;

            activeMatchFixture = currentFixture;
            hasActiveMatchFixture = true;
            AgentMatchSimulator.AgentMatchResult result = SimulateFixture(activeMatchFixture);

            lastSimulatedResult = result;

            SimulateOtherFixturesInMatchday(currentFixture.Matchday);

            if (!matchdayChromeBuilt)
            {
                BuildMatchdayChrome();
                matchdayChromeBuilt = true;
            }

            if (matchdayPrepPanel != null) matchdayPrepPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(true);

            matchPaused = false;
            Time.timeScale = 1f;

            if (matchHomeNameLabel != null) { matchHomeNameLabel.text = activeMatchFixture.HomeTeam.ToUpperInvariant(); matchHomeNameLabel.fontSize = 24; }
            if (matchAwayNameLabel != null) { matchAwayNameLabel.text = activeMatchFixture.AwayTeam.ToUpperInvariant(); matchAwayNameLabel.fontSize = 24; }
            // scoreText.fontSize isn't reset by ResetMatchStatsPanelToLiveLayout below (that
            // only touches the stats panel's position/size) - without resetting it here too,
            // matchday 2+ would inherit the full-time-sized 56pt score from the previous
            // match instead of the live view's 52pt, same class of bug that motivated
            // ResetMatchStatsPanelToLiveLayout in the first place.
            if (scoreText != null) scoreText.fontSize = 52;
            SetMentality(selectedMentality); // re-highlights the correct footer pill for this screen
            matchSubsLog.Clear();
            playersSubbedOffThisMatch.Clear();
            RefreshMatchSubsMadeList();
            if (matchFullTimeCaptionGroup != null) matchFullTimeCaptionGroup.SetActive(false);

            // Live ratings (session 10) - seeded with whichever XI SimulateFixture just
            // locked in (post EnsureNoInjuredStarters) for the managed team specifically,
            // same managed-team-only scope as Condition/appearances/form bonus.
            List<string> ratingsPlayerNames = new List<string>();
            foreach (PlayerAgent p in GetOrCreateAgentTeam(managedTeamName).StartingEleven) ratingsPlayerNames.Add(p.Name);
            matchRatings.ResetForMatch(ratingsPlayerNames);
            RefreshMatchRatingsGrid();

            // Undo everything ShowFullTimeResults did to these shared elements for the
            // previous match - without this, the second matchday inherited the first
            // match's full-time layout (centered/repositioned stats panel, hidden match
            // log, full-time-only scorer lists and View Match Events button still
            // visible) and rendered as an overlapping mess (confirmed live: fine on
            // matchday 1, badly broken on matchday 2).
            if (matchKeyMomentsCaptionRect != null) matchKeyMomentsCaptionRect.gameObject.SetActive(true);
            if (matchLogGroup != null) matchLogGroup.SetActive(true);
            if (matchFullTimeOnlyElements != null)
            {
                foreach (GameObject fullTimeElement in matchFullTimeOnlyElements)
                {
                    if (fullTimeElement != null) fullTimeElement.SetActive(false);
                }
            }
            ResetMatchStatsPanelToLiveLayout();

            foreach (GameObject liveElement in matchLiveOnlyElements)
            {
                if (liveElement != null) liveElement.SetActive(true);
            }

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = false;
            }

            StartCoroutine(ReplayMatchCoroutine(result));
        }

        // Mirrors the anchor/position/size BuildMatchdayChrome originally gave these two
        // elements for the live two-column layout - must match those values exactly,
        // since ShowFullTimeResults overwrites them in place (same RectTransforms,
        // reused rather than rebuilt) to get the full-time centered layout.
        private void ResetMatchStatsPanelToLiveLayout()
        {
            // Must match BuildMatchdayChrome's own headerHeight/offsets for these two
            // exactly - this was left at stale pre-redesign values (110/152/180) after
            // this session's header rework (170/210/238), which meant every match
            // silently reset the stats panel to the wrong position immediately after
            // BuildMatchdayChrome had just built it correctly, since this runs
            // unconditionally in OnSimulateMatchClicked on every match, not just the
            // first one.
            const float headerHeight = 170f;

            if (matchStatsCaptionRect != null)
            {
                matchStatsCaptionRect.anchorMin = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.anchorMax = new Vector2(0.55f, 1f);
                matchStatsCaptionRect.pivot = new Vector2(0f, 1f);
                matchStatsCaptionRect.anchoredPosition = new Vector2(20f, -(headerHeight + 210f));
                matchStatsCaptionRect.sizeDelta = new Vector2(360f, 20f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.55f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(20f, -(headerHeight + 238f));
                matchStatsBarsContainer.sizeDelta = new Vector2(360f, 190f);
            }
        }

        // Instantly plays out every remaining fixture with no matchday replay, applying
        // each result straight to the season table. Uses whichever tactic is currently
        // selected for every remaining match, since there's no per-match UI step here.
        public void OnSimulateSeasonClicked()
        {
            while (currentFixtureIndex < managedTeamFixtures.Count)
            {
                AdvanceCalendarTo(careerCalendar.GetFixtureDate(currentFixtureIndex), stopForNewInboxMessage: false);
                OpenFootballMatch fixture = managedTeamFixtures[currentFixtureIndex];

                // isAutoResolved: true (backlog item 10) - see ApplyMatchdayConditionAndInjuries
                // and SimulateFixture's own comments for why only Condition/injury are
                // neutralized here, not morale/form/development.
                ApplyFixtureResult(fixture, SimulateFixture(fixture, isAutoResolved: true));
                SimulateOtherFixturesInMatchday(fixture.Matchday);

                currentFixtureIndex++;
                ResolveMatchdayInboxTicks();
                ResolveNextMatchOnlyOverride();
            }

            RefreshHubUI();
        }

        // Simulates and applies every other fixture sharing the given matchday (i.e.
        // every match the managed club isn't part of), so the table reflects a full
        // division rather than just the managed club's own results. Guarded so each
        // matchday's other fixtures are only ever resolved once, however you reach it.
        private void SimulateOtherFixturesInMatchday(int matchday)
        {
            if (simulatedMatchdays.Contains(matchday))
            {
                return;
            }

            simulatedMatchdays.Add(matchday);

            foreach (OpenFootballMatch fixture in allSeasonFixtures)
            {
                if (fixture.Matchday != matchday)
                {
                    continue;
                }

                if (fixture.HomeTeam == managedTeamName || fixture.AwayTeam == managedTeamName)
                {
                    continue;
                }

                ApplyFixtureResult(fixture, SimulateFixture(fixture));
            }
        }

        private void ApplyFixtureResult(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            int homeTeamId = teamRegistry.GetTeamId(fixture.HomeTeam);
            int awayTeamId = teamRegistry.GetTeamId(fixture.AwayTeam);

            MatchRecord record = new MatchRecord
            {
                Matchday = fixture.Matchday,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                HomeGoals = result.HomeGoals,
                AwayGoals = result.AwayGoals
            };

            playableTable.Apply(record);

            if (result.HomeGoals > result.AwayGoals)
            {
                RecordFormResult(homeTeamId, 'W');
                RecordFormResult(awayTeamId, 'L');
            }
            else if (result.HomeGoals < result.AwayGoals)
            {
                RecordFormResult(homeTeamId, 'L');
                RecordFormResult(awayTeamId, 'W');
            }
            else
            {
                RecordFormResult(homeTeamId, 'D');
                RecordFormResult(awayTeamId, 'D');
            }

            ApplyMatchFormBonusForManagedTeam(fixture, result);
            ApplyMatchMoraleForManagedTeam(fixture, result);
            SendPostMatchReactionForManagedTeam(fixture, result);
        }

        // Tier 1 potentialemails.txt batch, #6-10 (session 14) - post-match reaction,
        // gated to avoid flooding the Inbox over a 38-match season (explicitly flagged
        // as an open decision in the session 13 handoff). A notable result (margin 3+)
        // always gets a message immediately - that's exactly the kind of result worth
        // reacting to on its own. An ordinary win/draw/loss only sends once the gap
        // since the last post-match message has reached PostMatchReactionMinGapMatchdays,
        // so routine results still surface periodically without one landing after
        // literally every fixture.
        private const int PostMatchReactionMinGapMatchdays = 2;
        private int lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;

        private void SendPostMatchReactionForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            bool isManagedHome = fixture.HomeTeam == managedTeamName;
            bool isManagedAway = fixture.AwayTeam == managedTeamName;

            if (!isManagedHome && !isManagedAway)
            {
                return;
            }

            int managedGoals = isManagedHome ? result.HomeGoals : result.AwayGoals;
            int opponentGoals = isManagedHome ? result.AwayGoals : result.HomeGoals;
            int margin = managedGoals - opponentGoals;
            string opponentName = isManagedHome ? fixture.AwayTeam : fixture.HomeTeam;

            bool isNotable = Mathf.Abs(margin) >= 3;
            bool gapElapsed = currentFixtureIndex - lastPostMatchReactionMatchday >= PostMatchReactionMinGapMatchdays;

            if (!isNotable && !gapElapsed)
            {
                CheckFormStreakMessages();
                return;
            }

            lastPostMatchReactionMatchday = currentFixtureIndex;

            string title;
            string body;

            if (margin >= 3)
            {
                title = "Excellent Performance";
                body = $"That was an excellent result against {opponentName}. The players looked confident and the scoreline will give the dressing room a real lift. Let's make sure we build on it rather than treating it as a one-off.";
            }
            else if (margin > 0)
            {
                title = "Good Result";
                body = $"Congratulations on the result against {opponentName}. The performance has helped strengthen our position in the league table and should give the squad confidence going into the next fixture. Keep the standards high.";
            }
            else if (margin == 0)
            {
                title = "Points Shared";
                body = $"The draw against {opponentName} leaves us with mixed feelings. There were positives in the performance, but also moments where the match could have slipped away. There is still room to improve.";
            }
            else if (margin > -3)
            {
                title = "Disappointing Result";
                body = $"The result against {opponentName} was disappointing. Setbacks are part of a long season, but we expect a response in the next match. Consistency will be important if we are to meet our objectives.";
            }
            else
            {
                title = "Performance Concerns";
                body = $"The defeat to {opponentName} has raised concerns. It was not simply the result, but the manner of the performance that disappointed us. We expect you to review the tactical approach, squad selection, and mentality ahead of the next fixture.";
            }

            inbox.Add(InboxMessageType.PostMatchReaction, title, body, careerCalendar.CurrentDayNumber);

            CheckFormStreakMessages();
        }

        // Tier 1 potentialemails.txt batch, #11-12 (session 14) - fires once when the
        // managed team's recent-form strip (see recentFormByTeamId/GetRecentFormString,
        // last 5 results) first reaches a 3-result streak, not on every single match
        // still inside that streak - a flag per direction, reset the moment the streak
        // breaks, keeps this to one message per streak rather than one per match.
        private const int FormStreakLength = 3;
        private bool poorRunMessageSentForCurrentStreak;
        private bool strongRunMessageSentForCurrentStreak;

        private void CheckFormStreakMessages()
        {
            int managedTeamId = teamRegistry.GetTeamId(managedTeamName);
            if (!recentFormByTeamId.TryGetValue(managedTeamId, out List<char> history) || history.Count < FormStreakLength)
            {
                return;
            }

            bool allLossesRecently = true;
            bool allWinsRecently = true;
            for (int i = history.Count - FormStreakLength; i < history.Count; i++)
            {
                if (history[i] != 'L') allLossesRecently = false;
                if (history[i] != 'W') allWinsRecently = false;
            }

            if (allLossesRecently)
            {
                if (!poorRunMessageSentForCurrentStreak)
                {
                    poorRunMessageSentForCurrentStreak = true;
                    inbox.Add(InboxMessageType.FormStreak, "Recent Form",
                        "Recent results have not met expectations. The board still supports your work, but we need to see signs of improvement soon. The squad has enough quality to be more competitive than recent performances suggest.",
                        careerCalendar.CurrentDayNumber);
                }
            }
            else
            {
                poorRunMessageSentForCurrentStreak = false;
            }

            if (allWinsRecently)
            {
                if (!strongRunMessageSentForCurrentStreak)
                {
                    strongRunMessageSentForCurrentStreak = true;
                    inbox.Add(InboxMessageType.FormStreak, "Momentum Building",
                        "The squad is starting to build momentum. Recent performances have improved confidence around the club, and the league table is beginning to reflect that. The challenge now is maintaining standards when the fixture list becomes more difficult.",
                        careerCalendar.CurrentDayNumber);
                }
            }
            else
            {
                strongRunMessageSentForCurrentStreak = false;
            }
        }

        // Remaining Tier 1 potentialemails.txt triggers that only make sense checked
        // once per matchday tick rather than at a specific single call site - mid-season
        // review (#27), low-stamina warning (#18), and injury recovery (playtest
        // backlog item, paired with the injury message TryRollInjury sends directly).
        // Called from both places currentFixtureIndex actually advances (the Simulate
        // Season loop and OnFullTimeContinueClicked), same as every other per-matchday
        // resolver above (ResolveDueBids etc.).
        private bool midSeasonReviewSentForCurrentSeason;
        private const float LowStaminaWarningThreshold = 60f;
        private const int LowStaminaWarningCooldownMatchdays = 5;
        private int lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
        private readonly HashSet<PlayerAgent> injuredPlayersTracked = new();

        private void ResolveMatchdayInboxTicks()
        {
            if (!squadsByTeamName.TryGetValue(managedTeamName, out AgentTeam team))
            {
                return;
            }

            // Mid-season review (#27) - fires once, the first matchday the season is at
            // least half played out. Reset alongside every other season-scoped flag in
            // OnStartNewSeasonClicked.
            if (!midSeasonReviewSentForCurrentSeason && managedTeamFixtures.Count > 0 &&
                currentFixtureIndex >= managedTeamFixtures.Count / 2)
            {
                midSeasonReviewSentForCurrentSeason = true;
                inbox.Add(InboxMessageType.MidSeasonReview, "Mid-Season Review",
                    "We have reached the midpoint of the season. The board has reviewed our league position, recent form, and overall squad performance. There is still time to improve, but the second half of the campaign will be important. Continue to make decisions that serve the long-term interests of the club.",
                    careerCalendar.CurrentDayNumber);
            }

            // Low-stamina warning (#18) - cooldown-gated so a squad that stays fatigued
            // for a long stretch doesn't get the same warning every single matchday.
            if (currentFixtureIndex - lastLowStaminaWarningMatchday >= LowStaminaWarningCooldownMatchdays)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                bool anyLowStamina = false;
                foreach (PlayerAgent player in team.Players)
                {
                    if (roles.GetCondition(player) < LowStaminaWarningThreshold)
                    {
                        anyLowStamina = true;
                        break;
                    }
                }

                if (anyLowStamina)
                {
                    lastLowStaminaWarningMatchday = currentFixtureIndex;
                    inbox.Add(InboxMessageType.LowStamina, "Fitness Concern",
                        "A few players are showing signs of fatigue. Heavy minutes can reduce sharpness late in matches, especially for players with lower stamina. Rotating the squad or using substitutions earlier may help avoid performance drops.",
                        careerCalendar.CurrentDayNumber);
                }
            }

            // Injury recovery (playtest backlog) - diffs the tracked injured set against
            // ManagerSquadRoles.IsInjured (a threshold check, not an event) to catch
            // whoever's return matchday just passed.
            if (injuredPlayersTracked.Count > 0)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                List<PlayerAgent> recovered = null;

                foreach (PlayerAgent player in injuredPlayersTracked)
                {
                    if (!roles.IsInjured(player, careerCalendar.CurrentDayNumber))
                    {
                        recovered ??= new List<PlayerAgent>();
                        recovered.Add(player);
                    }
                }

                if (recovered != null)
                {
                    foreach (PlayerAgent player in recovered)
                    {
                        injuredPlayersTracked.Remove(player);
                        inbox.Add(InboxMessageType.Recovery, $"{player.Name} Fit Again",
                            $"{player.Name} has recovered from injury and is available for selection again.",
                            careerCalendar.CurrentDayNumber);
                    }
                }
            }
        }

        // Form-based development bonus (session 9 backlog item) - has to live here,
        // post-match, rather than in ApplyMatchdayConditionAndInjuries (which runs
        // pre-match, before the result exists at all - see SimulateFixture's ordering).
        // Managed team only, same scope limit as every other per-player development
        // signal this session (AI clubs only get a flat assumed playing-time factor,
        // no real per-match tracking). Matches scorer names against the managed
        // Starting XI specifically (not the whole league) to minimise the same-name
        // collision risk that AgentMatchEvent.ScorerName already carries everywhere
        // else it's used (see the match log's own goal list) - a small squad is a much
        // narrower name-collision surface than 20 clubs' worth of players.
        private void ApplyMatchFormBonusForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            bool isManagedHome = fixture.HomeTeam == managedTeamName;
            bool isManagedAway = fixture.AwayTeam == managedTeamName;

            if (!isManagedHome && !isManagedAway)
            {
                return;
            }

            int managedGoals = isManagedHome ? result.HomeGoals : result.AwayGoals;
            int opponentGoals = isManagedHome ? result.AwayGoals : result.HomeGoals;

            ManagerPlayerDevelopment.MatchFormOutcome outcome = managedGoals > opponentGoals
                ? ManagerPlayerDevelopment.MatchFormOutcome.Win
                : managedGoals < opponentGoals
                    ? ManagerPlayerDevelopment.MatchFormOutcome.Loss
                    : ManagerPlayerDevelopment.MatchFormOutcome.Draw;

            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            HashSet<PlayerAgent> playedThisMatch = new HashSet<PlayerAgent>(managedTeam.StartingEleven);

            Dictionary<string, int> goalsByScorerName = new Dictionary<string, int>();
            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal || string.IsNullOrEmpty(evt.ScorerName))
                {
                    continue;
                }

                goalsByScorerName[evt.ScorerName] = goalsByScorerName.TryGetValue(evt.ScorerName, out int count) ? count + 1 : 1;
            }

            foreach (PlayerAgent player in playedThisMatch)
            {
                int goalsThisMatch = goalsByScorerName.TryGetValue(player.Name, out int goals) ? goals : 0;
                ManagerPlayerDevelopment.ApplyMatchFormBonus(player, goalsThisMatch, outcome);
            }
        }

        // Morale (session 10 - Thomas: doesn't affect performance, affects development
        // instead - see ManagerSquadRoles.ApplyPostMatchMorale/GetMoraleGrowthMultiplier).
        // Deliberately loops the WHOLE squad (team.Players, StartingEleven + Bench), not
        // just playedThisMatch like the form bonus above - a benched player's morale
        // needs to react to being overlooked, which means iterating players who did NOT
        // play, not just the ones who did.
        private void ApplyMatchMoraleForManagedTeam(OpenFootballMatch fixture, AgentMatchSimulator.AgentMatchResult result)
        {
            bool isManagedHome = fixture.HomeTeam == managedTeamName;
            bool isManagedAway = fixture.AwayTeam == managedTeamName;

            if (!isManagedHome && !isManagedAway)
            {
                return;
            }

            int managedGoals = isManagedHome ? result.HomeGoals : result.AwayGoals;
            int opponentGoals = isManagedHome ? result.AwayGoals : result.HomeGoals;

            ManagerPlayerDevelopment.MatchFormOutcome outcome = managedGoals > opponentGoals
                ? ManagerPlayerDevelopment.MatchFormOutcome.Win
                : managedGoals < opponentGoals
                    ? ManagerPlayerDevelopment.MatchFormOutcome.Loss
                    : ManagerPlayerDevelopment.MatchFormOutcome.Draw;

            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            HashSet<PlayerAgent> playedThisMatch = new HashSet<PlayerAgent>(managedTeam.StartingEleven);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            foreach (PlayerAgent player in managedTeam.Players)
            {
                roles.ApplyPostMatchMorale(player, playedThisMatch.Contains(player), outcome);
            }
        }

        // Manager-Mode-only last-5-results tracker backing the Hub league table's Form
        // column (e.g. "W D W W D") - deliberately NOT part of LeagueTable.Entry, which
        // stays untouched and is still what Research Mode's evaluation output reads.
        // Oldest result first, newest last (capped at 5), matching how "form" reads
        // left-to-right in real football coverage.
        private readonly Dictionary<int, List<char>> recentFormByTeamId = new();

        private void RecordFormResult(int teamId, char result)
        {
            if (!recentFormByTeamId.TryGetValue(teamId, out List<char> history))
            {
                history = new List<char>();
                recentFormByTeamId[teamId] = history;
            }

            history.Add(result);

            if (history.Count > 5)
            {
                history.RemoveAt(0);
            }
        }

        private string GetRecentFormString(int teamId)
        {
            if (!recentFormByTeamId.TryGetValue(teamId, out List<char> history) || history.Count == 0)
            {
                return string.Empty;
            }

            // TMP's <mspace> forces every character (letters and the join space alike) to
            // advance by the same fixed width - without it, "W" is visibly wider than "L"
            // or "D" in a proportional font, so consecutive results didn't line up at a
            // consistent rhythm (confirmed live: "WWDWW" reads noticeably tighter/looser
            // than "LWLWL" despite both being 5 characters).
            return $"<mspace=1.4em>{string.Join(" ", history)}</mspace>";
        }

        // Applies the mentality modifier only when the managed club is actually playing
        // in this fixture - other clubs' matches against each other use the plain
        // predicted expected goals with no modifier.
        private AgentMatchSimulator.AgentMatchResult SimulateFixture(OpenFootballMatch fixture, bool isAutoResolved = false)
        {
            AgentTeam homeTeam = GetOrCreateAgentTeam(fixture.HomeTeam);
            AgentTeam awayTeam = GetOrCreateAgentTeam(fixture.AwayTeam);

            // Swap any still-injured starter for the best fit bench cover (or call up a
            // reserve if the bench has none - see CallUpReservePlayer) before this match's
            // XI is finalized. Managed team only - AI opponents have no injury tracking.
            if (fixture.HomeTeam == managedTeamName)
            {
                EnsureNoInjuredStarters(homeTeam, fixture.HomeTeam);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                EnsureNoInjuredStarters(awayTeam, fixture.AwayTeam);
            }

            // Throwaway fit-adjusted clones, not the real squad data - see
            // ManagerFormationFit. A no-op for AI teams (never touched by the user, so
            // every starter is already a perfect fit for their slot, and conditionLookup
            // stays null since AI teams have no Condition tracking); only matters once
            // the managed team's XI has anyone out of position or under-conditioned.
            // isAutoResolved (backlog item 10) - during a SIMULATE SEASON skip, lean on
            // team-strength alone rather than feeding possibly-stale, un-recoverable
            // Condition into this match's fit-adjusted strength - see
            // ApplyMatchdayConditionAndInjuries's own comment for the full reasoning.
            Func<PlayerAgent, float> homeConditionLookup = (fixture.HomeTeam == managedTeamName && !isAutoResolved)
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p))
                : null;
            Func<PlayerAgent, float> awayConditionLookup = (fixture.AwayTeam == managedTeamName && !isAutoResolved)
                ? (p => GetOrCreateSquadRoles(managedTeamName).GetConditionMultiplier(p))
                : null;

            AgentTeam fitAdjustedHomeTeam = ManagerFormationFit.BuildFitAdjustedTeam(homeTeam, squadGenerator.GetStartingPositions(homeTeam.Formation), homeConditionLookup);
            AgentTeam fitAdjustedAwayTeam = ManagerFormationFit.BuildFitAdjustedTeam(awayTeam, squadGenerator.GetStartingPositions(awayTeam.Formation), awayConditionLookup);

            // Condition decay/recovery + injury rolls - managed team only (see
            // ManagerSquadRoles). Snapshotting team.StartingEleven here, before
            // SimulateMatch/replay ever runs, captures exactly the pre-kickoff XI -
            // substitutions during replay mutate StartingEleven/Bench in place, so
            // capturing any later would blur "who actually started" with "who's on at
            // full-time." Subs who come on mid-match aren't counted as "played" for
            // fatigue purposes in this pass - a deliberate v1 simplification, not an
            // oversight (see HANDOFF).
            if (fixture.HomeTeam == managedTeamName)
            {
                ApplyMatchdayConditionAndInjuries(homeTeam, isAutoResolved);
                ApplyMatchdayAcademyProgression();
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ApplyMatchdayConditionAndInjuries(awayTeam, isAutoResolved);
                ApplyMatchdayAcademyProgression();
            }

            ManagerPlayerDerivedStrength.Profile homeProfile = ManagerPlayerDerivedStrength.Calculate(
                fitAdjustedHomeTeam, squadGenerator.GetStartingPositions(fitAdjustedHomeTeam.Formation));
            ManagerPlayerDerivedStrength.Profile awayProfile = ManagerPlayerDerivedStrength.Calculate(
                fitAdjustedAwayTeam, squadGenerator.GetStartingPositions(fitAdjustedAwayTeam.Formation));
            ManagerPlayerDerivedStrength.MatchupPrediction prediction =
                ManagerPlayerDerivedStrength.PredictMatchup(homeProfile, awayProfile);

            // Kept before the mentality modifier touches anything - see
            // ApplyLiveMentalityChangeIfMatchInProgress, which needs this exact
            // pre-mentality baseline to recompute cleanly from if mentality changes
            // again mid-match.
            lastRawExpectedHomeGoals = prediction.ExpectedHomeGoals;
            lastRawExpectedAwayGoals = prediction.ExpectedAwayGoals;

            float expectedHomeGoals = prediction.ExpectedHomeGoals;
            float expectedAwayGoals = prediction.ExpectedAwayGoals;

            if (fixture.HomeTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedHomeGoals, ref expectedAwayGoals);
            }
            else if (fixture.AwayTeam == managedTeamName)
            {
                ManagerMentalityModifier.Apply(selectedMentality, ref expectedAwayGoals, ref expectedHomeGoals);
            }

            // Captain/vice/set-piece assignments remain organizational, so neither the captaincy
            // expected-goals modifier nor the corner-taker name wiring runs anymore.
            // ManagerCaptaincyModifier/CornerTakerNamesByTeamName are left in place
            // (unused) rather than deleted, in case this gets revisited later.
            lastExpectedHomeGoals = expectedHomeGoals;
            lastExpectedAwayGoals = expectedAwayGoals;

            matchSimulator.ManagedTeamName = managedTeamName;
            matchSimulator.ManagedTeamTacticalSliders = tacticalSliders;
            matchSimulator.TacticalShapeMatchup = ManagerTacticalShape.BuildMatchup(
                homeTeam.TeamName, homeTeam.Formation, ResolveFixtureTactics(homeTeam, awayTeam, true),
                awayTeam.TeamName, awayTeam.Formation, ResolveFixtureTactics(awayTeam, homeTeam, false),
                homeTeam, homeTeam.TeamName == managedTeamName ? GetOrCreateSquadRoles(managedTeamName) : null,
                awayTeam, awayTeam.TeamName == managedTeamName ? GetOrCreateSquadRoles(managedTeamName) : null);

            // Fresh match, fresh substitution clock - see AgentMatchSimulator.
            // ClearSubstitutions' own comment. SimulateFixture runs exactly once per
            // match (mid-match resimulation calls SimulateFromMinute directly, never
            // this method again), so this is the one correct place to reset it.
            matchSimulator.ClearSubstitutions();

            return matchSimulator.SimulateMatch(fitAdjustedHomeTeam, fitAdjustedAwayTeam, expectedHomeGoals, expectedAwayGoals);
        }

        private ManagerTacticalSliders ResolveFixtureTactics(AgentTeam team, AgentTeam opponent, bool isHome)
        {
            return team.TeamName == managedTeamName
                ? tacticalSliders
                : ManagerAiTacticalPlanner.Choose(team.TeamName, team.Formation, opponent.TeamName, opponent.Formation, isHome);
        }

        private void EnsureNoInjuredStarters(AgentTeam team, string teamName)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(teamName);

            // Snapshot before iterating - SubstitutePlayer mutates StartingEleven in
            // place, so walking the live list while swapping into it would skip entries.
            foreach (PlayerAgent starter in new List<PlayerAgent>(team.StartingEleven))
            {
                if (!roles.IsInjured(starter, careerCalendar.CurrentDayNumber))
                {
                    continue;
                }

                PlayerAgent replacement = FindFitBenchReplacement(team, roles, starter.PrimaryPosition)
                    ?? CallUpReservePlayer(teamName, starter.PrimaryPosition);

                if (replacement != null)
                {
                    team.SubstitutePlayer(starter, replacement);
                }

                // If replacement is still null here, the bench and reserve pool are both
                // out of fit cover for this position - a real, visible squad crisis
                // rather than one silently papered over. The injured starter plays
                // anyway (better than fielding ten men).
            }
        }

        private PlayerAgent FindFitBenchReplacement(AgentTeam team, ManagerSquadRoles roles, PlayerPosition neededPosition)
        {
            PlayerAgent best = null;
            float bestFit = -1f;

            foreach (PlayerAgent candidate in team.Bench)
            {
                if (roles.IsInjured(candidate, careerCalendar.CurrentDayNumber))
                {
                    continue;
                }

                float fit = candidate.GetPositionFit(neededPosition);

                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            return best;
        }

        // Same best-fit search as FindFitBenchReplacement, minus the injury filter - AI
        // clubs have no ManagerSquadRoles/injury tracking at all (that's a managed-team-
        // only system), so there's nothing to skip. Used only for backfilling an AI
        // club's XI after a transfer sale (session 16, see OnSignPlayerClicked).
        private PlayerAgent FindBestFitBenchPlayer(AgentTeam team, PlayerPosition neededPosition)
        {
            PlayerAgent best = null;
            float bestFit = -1f;

            foreach (PlayerAgent candidate in team.Bench)
            {
                float fit = candidate.GetPositionFit(neededPosition);

                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            return best;
        }

        // Loan system (session 9) - "any squad player" per Thomas's own answer, so a
        // starter can be loaned out too. If they were starting, backfills the slot the
        // same way an injury already does (FindFitBenchReplacement, falling back to
        // CallUpReservePlayer) rather than leaving a hole in the XI - a genuine squad
        // crisis only if no cover exists anywhere, same as the injury path.
        private void OnLoanOutClicked(PlayerAgent player)
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            bool wasStarting = team.StartingEleven.Contains(player);

            if (wasStarting)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                PlayerAgent replacement = FindFitBenchReplacement(team, roles, player.PrimaryPosition)
                    ?? CallUpReservePlayer(managedTeamName, player.PrimaryPosition);

                if (replacement != null)
                {
                    team.SubstitutePlayer(player, replacement);
                    team.Bench.Remove(player);
                    team.Reserves.Remove(player);
                }
                else
                {
                    team.StartingEleven.Remove(player);
                }
            }
            else
            {
                team.Bench.Remove(player);
                team.Reserves.Remove(player);
            }

            team.Players.Remove(player);

            // No cross-screen status label to report into here (Player Detail is
            // reachable from several different origin screens, each with its own
            // status mechanism or none at all) - the player disappearing from the
            // squad list on return is the confirmation for this first version. A
            // proper toast/confirmation would be a follow-up polish item.
            loanTracker.SendOnLoan(player, managedTeamName);

            OnInspectBackClicked();
        }

        // isAutoResolved (backlog item 10, session 11) - Thomas's real test: won his
        // first 3 individually-played matches with Liverpool, hit SIMULATE SEASON for
        // the rest, finished 15th. Root cause traced here: Condition decay and injury
        // rolls compound every auto-resolved match with zero manager mitigation (no
        // rest, no rotation, no tactical response - none of that is even possible
        // during a skip), and SimulateFixture's homeConditionLookup/awayConditionLookup
        // then feeds that same un-recovered fatigue into THIS match's fit-adjusted
        // strength, producing a genuine unrealistic performance spiral, not just a
        // cosmetic number. Only Condition/injury are gated here - development
        // (RecordAppearance, ApplyMatchdayProgression) stays unconditional, since a
        // skipped season should still let players grow normally; morale/form are
        // deliberately left alone too (see OnSimulateSeasonClicked's own comment on
        // why - they only ever affect development speed, never match performance, so
        // neutralizing them wouldn't touch the actual collapse symptom at all).
        private void ApplyMatchdayConditionAndInjuries(AgentTeam team, bool isAutoResolved = false)
        {
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);

            List<PlayerAgent> fullSquad = new List<PlayerAgent>(team.StartingEleven);
            fullSquad.AddRange(team.Bench);

            foreach (PlayerAgent player in fullSquad)
            {
                float minutesPlayed = ComputeMinutesPlayed(player, team);
                bool played = minutesPlayed > 0f;
                float preMatchCondition = roles.GetCondition(player);

                if (!isAutoResolved)
                {
                    roles.ApplyPostMatchCondition(player, minutesPlayed, player.Age, player.Stamina);
                }

                if (played)
                {
                    roles.RecordAppearance(player);

                    if (!isAutoResolved)
                    {
                        TryRollInjury(roles, player, preMatchCondition);
                    }
                }

                // Per-matchday development tick (session 9 backlog item) - same hook
                // Condition already uses, same played/not-played signal computed above.
                // Whole squad, not just starters - a benched player still ticks (at the
                // 0.7x floor rate), same as the old season-lump version's playing-time
                // floor. Deliberately still the binary `played` flag here, not
                // minutesPlayed - growth ticks were never the reported issue, only
                // Condition was, so left unchanged to keep this fix minimal. Morale
                // multiplier (session 10) rides along on this same call.
                ManagerPlayerDevelopment.ApplyMatchdayProgression(player, played, roles.GetMoraleGrowthMultiplier(player));
            }
        }

        // Academy growth moved off the once-a-season lump sum (session 16 - Thomas:
        // "do our youth players stats only move after the year, and not necessarily
        // real time? My GK hasn't changed at all in my academy at matchday 22" - a real
        // design gap confirmed by investigation, not a bug: academy had no per-matchday
        // hook at all before this). Mirrors ApplyMatchdayConditionAndInjuries's call
        // site exactly - fires once per matchday, alongside the managed team's own
        // tick, not once per fixture (academy isn't tied to a specific match).
        // playedThisMatchday: true for every tick, standing in for "always training" -
        // academy prospects don't play senior matches to have a real played/not-played
        // signal, and full-intensity coaching every matchday is close enough to the old
        // AssumedPlayingTimeFactorAcademyProspect (0.8) lump-sum pace without adding a
        // second continuous-factor overload just for this one caller. Focus stats
        // (session 10) still ride along exactly as they did in the old season-end call.
        private void ApplyMatchdayAcademyProgression()
        {
            foreach (PlayerAgent player in academy.GetAcademyPoolForAging())
            {
                ManagerPlayerDevelopment.ApplyMatchdayProgression(player, playedThisMatchday: true, focusAttributes: academy.GetFocusAttributes(player));
            }
        }

        private void CloseAcademyIntakeDropdown()
        {
            if (academyIntakeDropdown != null)
            {
                Destroy(academyIntakeDropdown);
                academyIntakeDropdown = null;
            }
        }

        // Real per-player minutes for this match (session 10 fix, see
        // ManagerSquadRoles.ApplyPostMatchCondition's own comment for the bug this
        // replaces). matchSubsLog only ever gets an entry for a genuine MID-match
        // substitution (see OnBenchPlayerDroppedOnPin's tacticsBoardOpenedMidMatch
        // gate) - a pre-match team-sheet edit made before kickoff isn't logged at all,
        // but doesn't need to be: team.StartingEleven by kickoff already reflects
        // whoever the manager actually chose to start, so anyone not touched by a
        // mid-match sub either played the full 90 (if they started) or didn't feature
        // at all (if they didn't). Doesn't handle a player being subbed on and then
        // subbed off again in the same match - the UI has no path to re-introduce a
        // player who's already come off, so that combination can't happen today.
        private float ComputeMinutesPlayed(PlayerAgent player, AgentTeam team)
        {
            const float matchLengthMinutes = 90f;

            foreach (var sub in matchSubsLog)
            {
                if (sub.onName == player.Name) return matchLengthMinutes - sub.minute;
                if (sub.offName == player.Name) return sub.minute;
            }

            return team.StartingEleven.Contains(player) ? matchLengthMinutes : 0f;
        }

        // Injury risk scales sharply as pre-match Condition drops - a manager who never
        // rests a player is directly trading long-term injury risk for short-term
        // selection convenience, which was the whole point of this system. Age adds a
        // smaller, realistic aging-curve bump on top. Recovery duration is a rough bell
        // curve (two averaged Random.Range rolls, same cheap-Gaussian-ish trick
        // GenerateAge uses) - mostly short knocks, occasional longer absences, matching
        // the "bell curve not hard range" preference used everywhere else stats/ages/
        // heights are generated.
        private void TryRollInjury(ManagerSquadRoles roles, PlayerAgent player, float preMatchCondition)
        {
            float fatigueRisk = Mathf.Clamp01((70f - preMatchCondition) / 70f);
            float ageRisk = Mathf.Clamp01((player.Age - 30f) / 15f);

            float injuryChance = 0.015f + (fatigueRisk * 0.09f) + (ageRisk * 0.02f);

            if (UnityEngine.Random.value >= injuryChance)
            {
                return;
            }

            int durationWeeks = Mathf.Clamp(Mathf.RoundToInt((UnityEngine.Random.Range(1f, 6f) + UnityEngine.Random.Range(1f, 6f)) / 2f), 1, 8);
            int durationDays = durationWeeks * 7;
            roles.SetInjured(player, careerCalendar.CurrentDayNumber + durationDays);
            injuredPlayersTracked.Add(player);

            // Playtest backlog item (session 14) - injury Inbox message. Recovery is
            // handled separately (see ResolveMatchdayInboxTicks) since there's no single
            // call site for "a player's return matchday just passed" - it's a threshold
            // crossed silently by IsInjured, not a discrete event like this roll is.
            inbox.Add(InboxMessageType.Injury, $"Injury: {player.Name}",
                $"{player.Name} has picked up an injury and is expected to be out for approximately {durationWeeks} week{(durationWeeks == 1 ? "" : "s")}.",
                careerCalendar.CurrentDayNumber);
        }

        // Lets the running replay coroutine finish out its remaining minutes without
        // waiting between them, so it lands on the same full-time state almost
        // instantly instead of skipping/discarding any of the match.
        public void OnSkipToResultsClicked()
        {
            skipToResultsRequested = true;
        }

        // Simulates the full match instantly, then replays the pre-computed events
        // against an accelerated clock so it reads as if live. Mentality buttons stay
        // interactable during replay and now genuinely affect the match in progress -
        // see ApplyLiveMentalityChangeIfMatchInProgress and isMatchCurrentlyLive below.
        private IEnumerator ReplayMatchCoroutine(AgentMatchSimulator.AgentMatchResult result)
        {
            skipToResultsRequested = false;
            tacticsBoardOpenedMidMatch = false;
            waitingAtHalfTime = false;
            if (halfTimePanel != null) halfTimePanel.SetActive(false);
            CapturePreMatchTeamSheet();
            currentMatchMinute = 0;
            liveHomeGoalsSoFar = 0;
            liveAwayGoalsSoFar = 0;
            matchSubsLog.Clear();
            playersSubbedOffThisMatch.Clear();
            RefreshMatchSubsMadeList();

            // Explicit flag rather than inferring "live" from panel active-states -
            // SetMentality also gets called during match *setup* (OnSimulateMatchClicked,
            // purely to re-highlight the footer pill) before this coroutine has even
            // reset currentMatchMinute to 0, so a state-inferred check could fire a bogus
            // resimulation against stale leftover data from the previous match.
            isMatchCurrentlyLive = true;

            if (matchEventFeedContainer != null)
            {
                foreach (Transform child in matchEventFeedContainer)
                {
                    Destroy(child.gameObject);
                }

                matchEventFeedRows.Clear();
            }

            if (scoreText != null) scoreText.text = "0 - 0";
            if (clockText != null) clockText.text = "0' LIVE";

            RefreshLiveMatchStats(0, 0, 0, 0, 0, 0);

            float secondsPerMinute = matchReplayDurationSeconds / 90f;

            int homeShots = 0;
            int awayShots = 0;
            int homeShotsOnTarget = 0;
            int awayShotsOnTarget = 0;
            int homeAttackEvents = 0;
            int awayAttackEvents = 0;
            int eventIndex = 0;

            for (int minute = 1; minute <= 90; minute++)
            {
                // Tracked on a field (not just the loop variable) so a mid-match
                // substitution made via the Tactics Board - which happens entirely
                // outside this coroutine while it's frozen at Time.timeScale=0 - knows
                // which minute to resimulate from and log against.
                currentMatchMinute = minute;

                if (!skipToResultsRequested)
                {
                    // Was a single blocking WaitForSeconds(secondsPerMinute) - since
                    // that's scaled-time, it (correctly) freezes solid while paused
                    // (timeScale=0). Polling per-frame instead lets Skip to Results
                    // interrupt immediately even while paused, without changing the
                    // normal (unpaused) per-minute pacing at all. The Tactics Board
                    // itself needs no special handling here - opening it mid-match just
                    // pauses (Time.timeScale=0), which freezes this wait solid exactly
                    // like a manual Pause does, and it resumes correctly on its own once
                    // timeScale goes back to 1.
                    float elapsed = 0f;

                    while (elapsed < secondsPerMinute)
                    {
                        if (skipToResultsRequested)
                        {
                            break;
                        }

                        yield return null;
                        elapsed += Time.deltaTime;
                    }
                }

                if (clockText != null) clockText.text = $"{minute}' LIVE";

                while (eventIndex < result.Events.Count && result.Events[eventIndex].Minute == minute)
                {
                    AgentMatchSimulator.AgentMatchEvent matchEvent = result.Events[eventIndex];
                    eventIndex++;

                    if (matchEvent.IsGoal)
                    {
                        if (matchEvent.HomeTeamScored) liveHomeGoalsSoFar++; else liveAwayGoalsSoFar++;

                        if (scoreText != null)
                        {
                            scoreText.text = $"{liveHomeGoalsSoFar} - {liveAwayGoalsSoFar}";
                        }
                    }

                    // Every event now carries HomeTeamAttacking (see the ManagerSim fork -
                    // the protected original only set it on shots), so "chances created"/
                    // possession share can come straight from the full event list instead
                    // of needing separate running totals threaded through the mid-match
                    // resimulation splice.
                    if (matchEvent.HomeTeamAttacking) homeAttackEvents++; else awayAttackEvents++;

                    if (matchEvent.IsShot)
                    {
                        if (matchEvent.HomeTeamAttacking) homeShots++; else awayShots++;
                        if (matchEvent.IsOnTarget)
                        {
                            if (matchEvent.HomeTeamAttacking) homeShotsOnTarget++; else awayShotsOnTarget++;
                        }
                    }

                    // Refreshed on every event, not just shots - Possession/Chances
                    // Created should tick up on a stopped-before-shot attack too.
                    RefreshLiveMatchStats(homeShots, awayShots, homeShotsOnTarget, awayShotsOnTarget, homeAttackEvents, awayAttackEvents);

                    AppendMatchEventRow(minute, matchEvent);

                    // Live ratings (session 10) - ApplyEvent silently no-ops for any name
                    // not seeded into this match's tracked set (i.e. every opponent
                    // player), so it's safe to call for every event regardless of which
                    // side was attacking. A mid-match resimulation (TriggerMidMatchResimulation)
                    // mutates this same result.Events list in place, so replayed/regenerated
                    // tail events flow through this exact loop and get rated normally -
                    // no special-casing needed.
                    matchRatings.ApplyEvent(matchEvent);
                    RefreshMatchRatingsGrid();
                }

                // Ambient drift (session 11, backlog item 7) - Thomas: a player sitting at
                // the same rating for a full 90 minutes reads as broken, not calm. Every 5
                // match-minutes regardless of whether an event happened this minute, so a
                // player who's on the pitch but never directly involved in a discrete
                // chance still shows some natural movement. See ManagerMatchRatings.
                // ApplyAmbientTick's own comment for the tuning reasoning.
                if (minute % 5 == 0)
                {
                    bool managedTeamIsHome = currentFixture.HomeTeam == managedTeamName;

                    int managedGoalsSoFar = managedTeamIsHome ? liveHomeGoalsSoFar : liveAwayGoalsSoFar;
                    int opponentGoalsSoFar = managedTeamIsHome ? liveAwayGoalsSoFar : liveHomeGoalsSoFar;

                    int managedShotsSoFar = managedTeamIsHome ? homeShots : awayShots;
                    int opponentShotsSoFar = managedTeamIsHome ? awayShots : homeShots;

                    matchRatings.ApplyAmbientTick();

                    matchRatings.ApplyTeamPerformanceTick(
                        managedGoalsSoFar,
                        opponentGoalsSoFar,
                        managedShotsSoFar,
                        opponentShotsSoFar
                    );

                    RefreshMatchRatingsGrid();
                }

                if (minute == 45 && !skipToResultsRequested)
                {
                    ShowHalfTimePanel(homeShots, awayShots, homeShotsOnTarget, awayShotsOnTarget, homeAttackEvents, awayAttackEvents);
                    while (waitingAtHalfTime)
                    {
                        yield return null;
                    }
                }
            }

            EnsureMatchResultMatchesEvents(result);

            // Match is resolved - any further mentality clicks should only affect the
            // *next* match again, not trigger a resimulation against a finished match.
            isMatchCurrentlyLive = false;

            // Switch from the live layout to the full-time one: hide the toolbar/clock/
            // mentality readout, show the "FULL TIME" caption, enlarge the score, and
            // swap the stats panel from the live single shots bar to the full-time
            // breakdown.
            foreach (GameObject liveElement in matchLiveOnlyElements)
            {
                if (liveElement != null) liveElement.SetActive(false);
            }

            if (matchFullTimeCaptionGroup != null) matchFullTimeCaptionGroup.SetActive(true);

            if (scoreText != null)
            {
                scoreText.fontSize = 56;
                scoreText.text = $"{result.HomeGoals} - {result.AwayGoals}";
            }

            if (matchHomeNameLabel != null) matchHomeNameLabel.fontSize = 30;
            if (matchAwayNameLabel != null) matchAwayNameLabel.fontSize = 30;

            // The fontSize bump above hits the same TMP mesh-generation failure as fresh
            // label creation (confirmed live: characterCount=0 with the text still
            // correctly assigned, silently rendering neither team name at full-time).
            StartCoroutine(RecoverBlankMatchTeamNameLabelsNextFrame());

            // Match Log is removed entirely at full-time (moved to its own "Match
            // Events" screen, see OnViewMatchEventsClicked) rather than staying visible
            // inline - Match Stats gets the freed-up space and is centered instead of
            // sharing a column with the log.
            if (matchKeyMomentsCaptionRect != null) matchKeyMomentsCaptionRect.gameObject.SetActive(false);
            if (matchLogGroup != null) matchLogGroup.SetActive(false);

            foreach (GameObject fullTimeElement in matchFullTimeOnlyElements)
            {
                if (fullTimeElement != null) fullTimeElement.SetActive(true);
            }

            PopulateGoalScorerLists(result);
            PopulateGoalTimeline(result);
            lastMatchEvents = new List<AgentMatchSimulator.AgentMatchEvent>(result.Events);

            // Match Stats + View Match Events move to the right half now that the goal
            // timeline/scorer lists (see BuildMatchdayChrome) own the whole left half -
            // previously both were recentered into one narrow column sharing the same
            // dead-center space the (then-tiny) scorer labels also used. Right half starts
            // right under the header since nothing else occupies that half above it anymore.
            const float rightHalfMargin = 40f;
            const float rightHalfTop = -(170f + 20f); // matches BuildMatchdayChrome's headerHeight (170) + goalsBlockTop's own offset
                                                      // Right half is anchor x 0.5-1.0 (960px at 1920-wide); this is its usable width
                                                      // with rightHalfMargin on both the center-side and the true right edge.
            const float rightHalfWidth = 880f;
            // Button's right edge lines up with the stats bars' own right edge below it.
            const float rightEdgeOffset = rightHalfMargin + rightHalfWidth;

            if (viewMatchEventsButton != null)
            {
                RectTransform viewEventsRect = viewMatchEventsButton.GetComponent<RectTransform>();
                viewEventsRect.anchorMin = new Vector2(0.5f, 1f);
                viewEventsRect.anchorMax = new Vector2(0.5f, 1f);
                viewEventsRect.pivot = new Vector2(1f, 1f);
                viewEventsRect.anchoredPosition = new Vector2(rightEdgeOffset, rightHalfTop);
                viewEventsRect.sizeDelta = new Vector2(220f, 32f);
            }

            if (matchStatsCaptionRect != null)
            {
                // Must be matchStatsCaptionRect (the "MatchStatsCaption" container that's
                // actually parented to the canvas), not matchStatsCaptionLabel.rectTransform
                // (BuildLabel's inner "Label" child, whose anchors/position are relative to
                // that container instead) - repositioning the child left the container
                // behind at its original column position and produced a nonsense on-screen
                // spot for the text, nowhere near the intended position.
                RectTransform captionRect = matchStatsCaptionRect;
                captionRect.anchorMin = new Vector2(0.5f, 1f);
                captionRect.anchorMax = new Vector2(0.5f, 1f);
                captionRect.pivot = new Vector2(0f, 1f);
                captionRect.anchoredPosition = new Vector2(rightHalfMargin, rightHalfTop - 50f);
                captionRect.sizeDelta = new Vector2(360f, 20f);
            }

            if (matchStatsBarsContainer != null)
            {
                matchStatsBarsContainer.anchorMin = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.anchorMax = new Vector2(0.5f, 1f);
                matchStatsBarsContainer.pivot = new Vector2(0f, 1f);
                matchStatsBarsContainer.anchoredPosition = new Vector2(rightHalfMargin, rightHalfTop - 80f);
                // Widened from 520 to the right half's real available width now that it's
                // not sharing a centered column - BuildFullTimeStatRow's rows anchor
                // (0,1)-(1,1) within this container, so they scale with it automatically,
                // no other change needed. Height fits 5 rows (Possession/Chances Created/
                // Shots/Shots on Target/Goals) at 44px each.
                matchStatsBarsContainer.sizeDelta = new Vector2(rightHalfWidth, 280f);
            }

            ShowFullTimeMatchStats(
                homeShots,
                awayShots,
                homeShotsOnTarget,
                awayShotsOnTarget,
                homeAttackEvents,
                awayAttackEvents,
                result.HomeGoals,
                result.AwayGoals);

            if (fullTimeContinueButton != null)
            {
                fullTimeContinueButton.interactable = true;
            }
        }

        // One row per live event, each with its own bottom divider - matches the mockup's
        // Match Log treatment (per-line "border-bottom:1px solid #1e2a3d") instead of the
        // old single text block with only line-spacing between events. Oldest row is
        // dropped once matchEventFeedRows exceeds maxVisibleEventLines, same cap the old
        // text-line queue used - since row count never exceeds that cap, total content
        // height never exceeds what the feed's mask was already sized to fit, so newest is
        // always visible without needing any scrolling logic.
        private void AppendMatchEventRow(int minute, AgentMatchSimulator.AgentMatchEvent matchEvent)
        {
            if (matchEventFeedContainer == null)
            {
                return;
            }

            // Only the "N' GOAL" prefix is green for a goal - the description itself
            // never mentions "goal" (see BuildGoalEventText), so an inline <color> tag
            // around just the prefix, not the row's own base color, keeps the rest of the
            // line in normal text color instead of washing the whole row green.
            string line = matchEvent.IsGoal
                ? $"<b><color=#3ddc84>{minute}' GOAL</color></b> · {matchEvent.Description}"
                : $"{minute}' {matchEvent.Description}";

            GameObject row = new GameObject("EventRow", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(matchEventFeedContainer, false);

            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.preferredHeight = 44f;
            rowLayout.flexibleWidth = 1f;

            TextMeshProUGUI rowLabel = ManagerUITheme.BuildLabel(row.transform, line, 19, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
            RectTransform rowLabelRect = rowLabel.GetComponent<RectTransform>();
            rowLabelRect.offsetMin = new Vector2(0f, 6f);
            rowLabelRect.offsetMax = Vector2.zero;

            GameObject divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(row.transform, false);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.sizeDelta = new Vector2(0f, 1f);
            dividerRect.anchoredPosition = Vector2.zero;
            divider.GetComponent<Image>().color = ManagerUITheme.BarTrack;

            matchEventFeedRows.Enqueue(row);

            while (matchEventFeedRows.Count > maxVisibleEventLines)
            {
                GameObject oldRow = matchEventFeedRows.Dequeue();
                if (oldRow != null) Destroy(oldRow);
            }
        }

        // Real scorer names/minutes from AgentMatchEvent.ScorerName (see AgentMatchSimulator) -
        // not fabricated. Newest-first isn't required by the design, so kept in match order.
        private void PopulateGoalScorerLists(AgentMatchSimulator.AgentMatchResult result)
        {
            string homeList = "";
            string awayList = "";

            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal || string.IsNullOrEmpty(evt.ScorerName))
                {
                    continue;
                }

                // Football icon TMP Sprite Asset, not the "·" placeholder this used before
                // real art existed - matchHomeScorersLabel/matchAwayScorersLabel have
                // footballIconSpriteAsset assigned where they're built.
                string line = $"<size=60%><sprite name=\"football-icon\"></size> {evt.ScorerName}  {evt.Minute}'\n";

                if (evt.HomeTeamScored)
                {
                    homeList += line;
                }
                else
                {
                    awayList += line;
                }
            }

            // Each block is now self-labeled with its own team name - now that the two
            // lists are stacked (not flanking left/right of center), position alone no
            // longer tells you which team is which. A team with no goals gets no block at
            // all rather than an empty header, rather than inventing a "no goals" line.
            // Header color is managed-team-relative (green) vs opponent (red), not simply
            // home/away - same `currentFixture.HomeTeam == managedTeamName` check used
            // elsewhere (e.g. OnConfirmTeamClicked, matchStatsBarsContainer's possession
            // math) so it stays correct on the (roughly half the time) away fixtures too.
            bool managedIsHome = RequireActiveMatchFixture().HomeTeam == managedTeamName;
            string homeTeamName = matchHomeNameLabel != null ? matchHomeNameLabel.text : "";
            string awayTeamName = matchAwayNameLabel != null ? matchAwayNameLabel.text : "";
            string homeHex = ColorUtility.ToHtmlStringRGB(managedIsHome ? ManagerUITheme.Accent : ManagerUITheme.Danger);
            string awayHex = ColorUtility.ToHtmlStringRGB(managedIsHome ? ManagerUITheme.Danger : ManagerUITheme.Accent);

            if (matchHomeScorersLabel != null)
            {
                matchHomeScorersLabel.text = homeList.Length > 0
                    ? $"<b><color=#{homeHex}>{homeTeamName}</color></b>\n{homeList.TrimEnd('\n')}"
                    : "";
            }

            if (matchAwayScorersLabel != null)
            {
                matchAwayScorersLabel.text = awayList.Length > 0
                    ? $"<b><color=#{awayHex}>{awayTeamName}</color></b>\n{awayList.TrimEnd('\n')}"
                    : "";
            }
        }

        // Rebuilt per match (goal count varies) into the chrome-built matchGoalTimelineContainer
        // (see BuildMatchdayChrome) - a marker per goal placed proportionally along the 0-90'
        // track by real Minute, home above the line / away below it (mirrors the old
        // left/right-flanking convention for scorer lists, just turned 90 degrees), now with
        // the real minute printed next to each marker - the timeline is big enough for that to
        // read cleanly since it moved to its own full-width band below everything else.
        private void PopulateGoalTimeline(AgentMatchSimulator.AgentMatchResult result)
        {
            if (matchGoalTimelineContainer == null)
            {
                return;
            }

            for (int i = matchGoalTimelineContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(matchGoalTimelineContainer.GetChild(i).gameObject);
            }

            const float markerSize = 26f;
            // Marker sits a bit off the line, label sits further out beyond the marker -
            // same above(home)/below(away) split as the marker itself.
            const float markerOffset = 26f;
            const float labelOffset = 54f;
            bool managedIsHome = RequireActiveMatchFixture().HomeTeam == managedTeamName;

            foreach (AgentMatchSimulator.AgentMatchEvent evt in result.Events)
            {
                if (!evt.IsGoal)
                {
                    continue;
                }

                float minuteFraction = Mathf.Clamp01(evt.Minute / 90f);
                float x = minuteFraction * matchGoalTimelineWidth;
                float sign = evt.HomeTeamScored ? 1f : -1f;

                // Green for the managed team's own goals, red for the opponent's - same
                // managed-team-relative convention as PopulateGoalScorerLists' headers,
                // not simply home=green/away=red.
                bool scoredByManagedTeam = evt.HomeTeamScored == managedIsHome;
                Color markerColor = scoredByManagedTeam ? ManagerUITheme.Accent : ManagerUITheme.Danger;

                GameObject marker = new GameObject($"GoalMarker_{evt.Minute}", typeof(RectTransform), typeof(Image));
                marker.transform.SetParent(matchGoalTimelineContainer, false);

                RectTransform markerRect = marker.GetComponent<RectTransform>();
                markerRect.anchorMin = new Vector2(0f, 0.5f);
                markerRect.anchorMax = new Vector2(0f, 0.5f);
                markerRect.pivot = new Vector2(0.5f, 0.5f);
                markerRect.sizeDelta = new Vector2(markerSize, markerSize);
                markerRect.anchoredPosition = new Vector2(x, sign * markerOffset);
                marker.GetComponent<Image>().color = markerColor;

                GameObject label = new GameObject($"GoalMarkerMinute_{evt.Minute}", typeof(RectTransform));
                label.transform.SetParent(matchGoalTimelineContainer, false);

                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.5f);
                labelRect.anchorMax = new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = new Vector2(70f, 24f);
                labelRect.anchoredPosition = new Vector2(x, sign * labelOffset);
                ManagerUITheme.BuildLabel(label.transform, $"{evt.Minute}'", 15, markerColor, TextAlignmentOptions.Center, FontStyles.Bold);
            }
        }

        public void OnFullTimeContinueClicked()
        {
            RestorePreMatchTeamSheet();
            ApplyFixtureResult(RequireActiveMatchFixture(), lastSimulatedResult);

            currentFixtureIndex++;
            ResolveMatchdayInboxTicks();
            ResolveNextMatchOnlyOverride();

            matchPaused = false;
            Time.timeScale = 1f;

            // The Match Events screen has its own Continue button wired to this same
            // method - without hiding it here, clicking Continue from there did switch
            // to the Hub underneath, but this still-active panel stayed on top and
            // masked the change completely, making the button look dead from that
            // screen (confirmed live).
            if (matchEventsPanel != null) matchEventsPanel.SetActive(false);

            ShowSeasonHub();
            hasActiveMatchFixture = false;
        }

        private void CapturePreMatchTeamSheet()
        {
            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            preMatchFormation = team.Formation;
            preMatchStartingEleven = new List<PlayerAgent>(team.StartingEleven);
            preMatchBench = new List<PlayerAgent>(team.Bench);
            preMatchReserves = new List<PlayerAgent>(team.Reserves);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            preMatchAttackDefendRoles = team.Players.ToDictionary(player => player, player => roles.GetRole(player));
            preMatchWidth = tacticalSliders.Width;
            preMatchDefensiveDepth = tacticalSliders.DefensiveDepth;
            preMatchTempo = tacticalSliders.Tempo;
        }

        private void RestorePreMatchTeamSheet()
        {
            if (preMatchStartingEleven == null) return;

            AgentTeam team = GetOrCreateAgentTeam(managedTeamName);
            team.Formation = preMatchFormation;
            team.StartingEleven = new List<PlayerAgent>(preMatchStartingEleven);
            team.Bench = new List<PlayerAgent>(preMatchBench);
            team.Reserves = new List<PlayerAgent>(preMatchReserves);
            foreach (PlayerAgent player in team.Players)
            {
                player.IsStartingEleven = team.StartingEleven.Contains(player);
            }

            if (preMatchAttackDefendRoles != null)
            {
                ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
                foreach (KeyValuePair<PlayerAgent, AttackDefendRole> entry in preMatchAttackDefendRoles)
                    roles.SetRole(entry.Key, entry.Value);
            }

            tacticalSliders.Width = preMatchWidth;
            tacticalSliders.DefensiveDepth = preMatchDefensiveDepth;
            tacticalSliders.Tempo = preMatchTempo;

            preMatchStartingEleven = null;
            preMatchBench = null;
            preMatchReserves = null;
            preMatchAttackDefendRoles = null;
        }

        // --- Full-Time Summary -> Match Events (new screen, no Editor-placed panel to
        // wire - built entirely in code the first time it's opened, same as everything
        // else this reskin builds fresh). ---

        public void OnViewMatchEventsClicked()
        {
            if (!matchEventsChromeBuilt)
            {
                BuildMatchEventsPanel();
                matchEventsChromeBuilt = true;
            }

            OpenFootballMatch displayedFixture = RequireActiveMatchFixture();
            if (matchEventsHomeNameLabel != null) matchEventsHomeNameLabel.text = displayedFixture.HomeTeam.ToUpperInvariant();
            if (matchEventsAwayNameLabel != null) matchEventsAwayNameLabel.text = displayedFixture.AwayTeam.ToUpperInvariant();
            if (matchEventsScoreText != null && lastSimulatedResult != null)
            {
                matchEventsScoreText.text = $"{lastSimulatedResult.HomeGoals} - {lastSimulatedResult.AwayGoals}";
            }

            PopulateMatchEventsList();

            if (matchdayPanel != null) matchdayPanel.SetActive(false);
            if (matchEventsPanel != null) matchEventsPanel.SetActive(true);
        }

        public void OnBackToSummaryClicked()
        {
            if (matchEventsPanel != null) matchEventsPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(true);
        }

        private OpenFootballMatch RequireActiveMatchFixture()
        {
            if (!hasActiveMatchFixture)
                throw new InvalidOperationException("Match UI requested without an immutable active-fixture snapshot.");
            return activeMatchFixture;
        }

        private void BuildMatchEventsPanel()
        {
            if (matchdayPanel == null || matchdayPanel.transform.parent == null)
            {
                return;
            }

            // Bumped from 90 to 170, matching Match Day/Full-Time Summary's own header -
            // this screen already deliberately uses their 56/30pt score/name sizing ("the
            // mockup uses the identical header block for both screens", below), which
            // needs the same taller band; at 90 the caption sat only 16px from the very
            // top of the screen with almost no headroom (confirmed live: "on the border").
            const float headerHeight = 170f;
            const float footerHeight = 90f;

            matchEventsPanel = new GameObject("MatchEventsPanel", typeof(RectTransform));
            matchEventsPanel.transform.SetParent(matchdayPanel.transform.parent, false);
            RectTransform panelRect = matchEventsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(matchEventsPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(matchEventsPanel.transform, topBand: true, height: headerHeight);
            GameObject footer = ManagerUITheme.BuildAccentBand(matchEventsPanel.transform, topBand: false, height: footerHeight);

            GameObject captionObj = new GameObject("FullTimeCaption", typeof(RectTransform));
            captionObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(captionObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 18f));
            ManagerUITheme.BuildLabel(captionObj.transform, "FULL TIME", 13, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);

            // Score/team-name sizes match the Full-Time Summary header exactly - the
            // mockup uses the identical header block for both screens.
            GameObject scoreObj = new GameObject("Score", typeof(RectTransform));
            scoreObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(scoreObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(200f, 68f));
            matchEventsScoreText = ManagerUITheme.BuildLabel(scoreObj.transform, "", 56, ManagerUITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

            GameObject homeObj = new GameObject("HomeTeamName", typeof(RectTransform));
            homeObj.transform.SetParent(header.transform, false);
            RectTransform homeRect = homeObj.GetComponent<RectTransform>();
            homeRect.anchorMin = new Vector2(0.5f, 1f);
            homeRect.anchorMax = new Vector2(0.5f, 1f);
            homeRect.pivot = new Vector2(1f, 1f);
            homeRect.anchoredPosition = new Vector2(-110f, -64f);
            homeRect.sizeDelta = new Vector2(260f, 32f);
            matchEventsHomeNameLabel = ManagerUITheme.BuildLabel(homeObj.transform, "", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            GameObject awayObj = new GameObject("AwayTeamName", typeof(RectTransform));
            awayObj.transform.SetParent(header.transform, false);
            RectTransform awayRect = awayObj.GetComponent<RectTransform>();
            awayRect.anchorMin = new Vector2(0.5f, 1f);
            awayRect.anchorMax = new Vector2(0.5f, 1f);
            awayRect.pivot = new Vector2(0f, 1f);
            awayRect.anchoredPosition = new Vector2(110f, -64f);
            awayRect.sizeDelta = new Vector2(260f, 32f);
            matchEventsAwayNameLabel = ManagerUITheme.BuildLabel(awayObj.transform, "", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject eventsCaptionObj = new GameObject("MatchEventsCaption", typeof(RectTransform));
            eventsCaptionObj.transform.SetParent(matchEventsPanel.transform, false);
            ManagerUITheme.SetPointAnchor(eventsCaptionObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(40f, -(headerHeight + 24f)), new Vector2(300f, 20f));
            ManagerUITheme.BuildLabel(eventsCaptionObj.transform, "MATCH EVENTS", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(matchEventsPanel.transform, "BACK TO SUMMARY", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 12);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-40f, -(headerHeight + 18f)), new Vector2(200f, 32f));
            backButton.onClick.AddListener(OnBackToSummaryClicked);

            // Scrollable list: ScrollRect + masked Viewport + Content (VerticalLayoutGroup
            // + ContentSizeFitter), same pattern as SquadListView/LeagueTableView.
            GameObject scrollViewObj = new GameObject("MatchEventsScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(matchEventsPanel.transform, false);
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(1f, 1f);
            scrollViewRect.offsetMin = new Vector2(40f, footerHeight + 24f);
            // Right margin widened from 40 to 56 to make room for the scrollbar added
            // below - same lesson as the Tactics Board bench row earlier: the list was
            // already genuinely scrollable (mouse wheel confirmed working), but with no
            // visible affordance it read as "broken/missing events" rather than
            // "scroll for more" (confirmed live).
            scrollViewRect.offsetMax = new Vector2(-56f, -(headerHeight + 56f));

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(scrollViewObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            matchEventsListContainer = contentObj.GetComponent<RectTransform>();
            matchEventsListContainer.anchorMin = new Vector2(0f, 1f);
            matchEventsListContainer.anchorMax = new Vector2(1f, 1f);
            matchEventsListContainer.pivot = new Vector2(0.5f, 1f);
            matchEventsListContainer.anchoredPosition = Vector2.zero;
            matchEventsListContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layoutGroup = contentObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.spacing = 2f;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = matchEventsListContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Settled by direct measurement rather than another guess: simulated a wheel
            // event via ExecuteEvents and read scrollRect.verticalNormalizedPosition
            // before/after. At sensitivity=-1, a simulated "scroll down" event tried to
            // move the list toward its already-at-the-top position (wrong direction,
            // clamped with no visible movement); at sensitivity=+1 (Unity's own default)
            // the same event correctly moved it toward later content. Every prior report
            // that +1 "still felt backwards" was against a continuously-running Play
            // Mode session that started before that build's fix was ever compiled in -
            // this screen's chrome is only built once per session (see the
            // matchEventsChromeBuilt-style guard), so a session that predates a fix will
            // never show it no matter how long it keeps running.
            // +1 was the direction fix (negative sensitivity scrolled backwards - see the
            // Match Events scroll view's own comment for the measured proof of that), but
            // magnitude 1 itself is Unity's stock default and reads as sluggish on this
            // project's 1920x1080-reference-resolution lists (backlog item, session 12,
            // Thomas: "list scrolling feels terribly slow"). 25 keeps the same sign (same
            // correct direction) while moving content a comfortable amount per wheel notch.
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            // Slim vertical scrollbar in the 16px gap freed up above - see the comment
            // on scrollViewRect.offsetMax.
            GameObject scrollbarObj = new GameObject("MatchEventsScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObj.transform.SetParent(matchEventsPanel.transform, false);
            RectTransform scrollbarRect = scrollbarObj.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            // offsetMin/offsetMax, not sizeDelta - mirrors scrollViewRect's own margins
            // exactly (footerHeight+24 bottom, headerHeight+56 top) so the two line up,
            // and avoids the sizeDelta-under-stretched-anchors trap entirely.
            scrollbarRect.offsetMin = new Vector2(-46f, footerHeight + 24f);
            scrollbarRect.offsetMax = new Vector2(-40f, -(headerHeight + 56f));
            scrollbarObj.GetComponent<Image>().color = ManagerUITheme.CardNeutralAlt;

            GameObject scrollHandleAreaObj = new GameObject("SlidingArea", typeof(RectTransform));
            scrollHandleAreaObj.transform.SetParent(scrollbarObj.transform, false);
            RectTransform scrollHandleAreaRect = scrollHandleAreaObj.GetComponent<RectTransform>();
            scrollHandleAreaRect.anchorMin = Vector2.zero;
            scrollHandleAreaRect.anchorMax = Vector2.one;
            scrollHandleAreaRect.offsetMin = Vector2.zero;
            scrollHandleAreaRect.offsetMax = Vector2.zero;

            GameObject scrollHandleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            scrollHandleObj.transform.SetParent(scrollHandleAreaObj.transform, false);
            RectTransform scrollHandleRect = scrollHandleObj.GetComponent<RectTransform>();
            scrollHandleRect.anchorMin = Vector2.zero;
            scrollHandleRect.anchorMax = new Vector2(1f, 0.3f);
            // Must be zeroed explicitly - a fresh RectTransform's default sizeDelta is
            // (100,100), which under stretched anchors ADDS 100px to the computed size
            // rather than being ignored (confirmed live on the bench scrollbar earlier).
            scrollHandleRect.sizeDelta = Vector2.zero;
            scrollHandleRect.offsetMin = Vector2.zero;
            scrollHandleRect.offsetMax = Vector2.zero;
            scrollHandleObj.GetComponent<Image>().color = ManagerUITheme.Accent;

            Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
            // BottomToTop, not the seemingly-obvious TopToBottom - ScrollRect's
            // verticalNormalizedPosition convention is 1=viewing the top of the content,
            // 0=viewing the bottom, and it drives the linked Scrollbar's .value directly.
            // Confirmed empirically (not guessed): with TopToBottom, value=1 (viewing the
            // list's top) rendered the handle at the BOTTOM of the track and vice versa -
            // exactly backwards, matching the reported "scroll to the bottom of the
            // scrollbar to see the top of the list" symptom.
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = scrollHandleRect;
            scrollbar.targetGraphic = scrollHandleObj.GetComponent<Image>();

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            Button continueButton = ManagerUITheme.BuildButton(footer.transform, "CONTINUE", ManagerUITheme.Accent, ManagerUITheme.OnAccent, 15);
            ManagerUITheme.SetPointAnchor(continueButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(220f, 50f));
            continueButton.onClick.AddListener(OnFullTimeContinueClicked);

            // See BuildTeamSelectChrome's identical call for why.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchEventsPanel.transform));
        }

        private void PopulateMatchEventsList()
        {
            if (matchEventsListContainer == null || lastMatchEvents == null)
            {
                return;
            }

            foreach (Transform child in matchEventsListContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (AgentMatchSimulator.AgentMatchEvent evt in lastMatchEvents)
            {
                GameObject row = new GameObject($"Event_{evt.Minute}", typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(matchEventsListContainer, false);

                LayoutElement layoutElement = row.GetComponent<LayoutElement>();
                layoutElement.preferredHeight = 38f;
                layoutElement.flexibleWidth = 1f;

                // Only the minute prefix is green for a goal - the description itself
                // never mentions "goal" (see BuildGoalEventText), so wrapping the whole
                // line in green read as over-highlighted (same fix as AppendMatchEventRow).
                string text = evt.IsGoal
                    ? $"<b><color=#3ddc84>{evt.Minute}'</color></b>   {evt.Description}"
                    : $"{evt.Minute}'   {evt.Description}";

                ManagerUITheme.BuildLabel(row.transform, text, 19, ManagerUITheme.TextBody, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
            }

            // Rows are cleared and rebuilt fresh every time this runs - same rapid
            // destroy/recreate churn as the Tactics Board's pins/bench.
            StartCoroutine(RecoverBlankLabelsNextFrame(matchEventsListContainer));
        }

        private AgentMatchSimulator.AgentMatchResult lastSimulatedResult;
    }
}
