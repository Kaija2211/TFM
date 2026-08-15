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
        public void OnTitleNewCareerClicked()
        {
            if (titlePanel != null) titlePanel.SetActive(false);

            ShowTeamSelect();
        }

        public void OnTitleExitClicked()
        {
            Application.Quit();
        }

        // --- Team Select ---

        private List<string> BuildAvailableTeamNames()
        {
            SortedSet<string> names = new();

            foreach (OpenFootballMatch match in allSeasonFixtures)
            {
                names.Add(match.HomeTeam);
                names.Add(match.AwayTeam);
            }

            return new List<string>(names);
        }

        private void ShowTeamSelect()
        {
            if (!teamGridBuilt)
            {
                BuildTeamSelectChrome();
                BuildTeamSelectGrid();
                teamGridBuilt = true;
            }

            teamSelectStep = 1;

            if (teamSelectPanel != null) teamSelectPanel.SetActive(true);
            if (seasonHubPanel != null) seasonHubPanel.SetActive(false);
            if (matchdayPanel != null) matchdayPanel.SetActive(false);

            RefreshTeamSelectUI();
            RefreshTeamSelectStepUI();
        }

        // Header/footer bands (see ManagerUITheme.BuildAccentBand) plus the two captions
        // above the name field and the grid. Both bands are 90px tall - TeamGridContainer
        // and ManagerNameInput need matching Top/Pos Y offsets in the Editor so their
        // content doesn't sit underneath these.
        private void BuildTeamSelectChrome()
        {
            if (teamSelectPanel == null)
            {
                return;
            }

            const float bandHeight = 90f;

            // Mockup's body is a max-width:1500px column centered in the 1920-wide panel
            // (`margin:0 auto`), not edge-to-edge - contentLeft marks that centered
            // region's left bound, matching the panel's new width-wide 1920x1080 canvas
            // instead of the old 24px-from-edge layout tuned for 960x540.
            const float contentWidth = 1700f;
            const float contentLeft = (1920f - contentWidth) / 2f;
            const float nameColumnWidth = 340f;

            GameObject header = ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: true, height: bandHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(-2f * contentLeft, 40f);
            titleRect.anchoredPosition = new Vector2(contentLeft, -22f);
            ManagerUITheme.BuildLabel(titleObj.transform, "NEW CAREER", 30, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            GameObject subtitleObj = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObj.transform.SetParent(header.transform, false);
            RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0f, 1f);
            subtitleRect.sizeDelta = new Vector2(-2f * contentLeft, 24f);
            subtitleRect.anchoredPosition = new Vector2(contentLeft, -60f);
            // Placeholder text - RefreshTeamSelectStepUI overwrites this immediately
            // (ShowTeamSelect calls it right after this method) with the real per-step
            // "Step 1 of 2"/"Step 2 of 2" text.
            ManagerUITheme.BuildLabel(subtitleObj.transform, "Step 1 of 2 · Manager Name", 16, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);
            teamSelectSubtitleObj = subtitleObj;

            ManagerUITheme.BuildAccentBand(teamSelectPanel.transform, topBand: false, height: bandHeight);

            GameObject nameCaption = new GameObject("ManagerNameCaption", typeof(RectTransform));
            nameCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform nameCaptionRect = nameCaption.GetComponent<RectTransform>();
            nameCaptionRect.anchorMin = new Vector2(0f, 1f);
            nameCaptionRect.anchorMax = new Vector2(0f, 1f);
            nameCaptionRect.pivot = new Vector2(0f, 1f);
            nameCaptionRect.sizeDelta = new Vector2(nameColumnWidth, 22f);
            ManagerUITheme.BuildLabel(nameCaption.transform, "MANAGER NAME", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nameCaption.transform.SetAsFirstSibling();
            teamSelectNameCaption = nameCaption;

            // Save Name field (session 15) - own caption + code-built input, same
            // "MANAGER NAME"-style caption treatment, positioned by RefreshTeamSelectStepUI
            // right below the Manager Name field on step 1 and hidden entirely on step 2
            // (a save is created once per career, not re-named per club-select visit).
            GameObject saveNameCaption = new GameObject("SaveNameCaption", typeof(RectTransform));
            saveNameCaption.transform.SetParent(teamSelectPanel.transform, false);
            ManagerUITheme.BuildLabel(saveNameCaption.transform, "SAVE NAME", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            saveNameCaption.transform.SetAsFirstSibling();
            teamSelectSaveNameCaption = saveNameCaption;

            saveNameInput = ManagerUITheme.BuildInputField(teamSelectPanel.transform, "e.g. Rebuild Job", 22, characterLimit: 40);
            saveNameInput.transform.SetAsFirstSibling();

            GameObject clubCaption = new GameObject("SelectClubCaption", typeof(RectTransform));
            clubCaption.transform.SetParent(teamSelectPanel.transform, false);
            RectTransform clubCaptionRect = clubCaption.GetComponent<RectTransform>();
            clubCaptionRect.anchorMin = new Vector2(0f, 1f);
            clubCaptionRect.anchorMax = new Vector2(0f, 1f);
            clubCaptionRect.pivot = new Vector2(0f, 1f);
            // Full content width, not just the old clubColumnLeft..contentRight span -
            // on step 2 the grid no longer shares the row with a name column, so the
            // caption above it shouldn't either.
            clubCaptionRect.sizeDelta = new Vector2(contentWidth, 18f);
            ManagerUITheme.BuildLabel(clubCaption.transform, "SELECT CLUB · PREMIER LEAGUE", 12, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            clubCaption.transform.SetAsFirstSibling();
            teamSelectClubCaption = clubCaption;

            // managerNameInput and teamGridContainer are Editor-placed objects (an
            // InputField and a Scroll/Grid layout aren't worth rebuilding from scratch
            // in code), but their position/size/color was left to hand-dragging instead
            // of being set here - the exact failure mode this file's other screens
            // deliberately avoid. Margins below match the design mockup's proportions
            // (header-to-caption and caption-to-content gaps, not just a token few px).
            const float captionTop = bandHeight + 40f;

            nameCaptionRect.anchoredPosition = new Vector2(contentLeft, -captionTop);
            clubCaptionRect.anchoredPosition = new Vector2(contentLeft, -captionTop);

            if (managerNameInput != null)
            {
                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();
                // Positioned per-step by RefreshTeamSelectStepUI instead of fixed here -
                // step 1 wants it big and centered, step 2 hides it entirely.

                if (managerNameInput.TryGetComponent(out Image inputImage))
                {
                    inputImage.color = ManagerUITheme.PanelDark;
                }

                // The typed-text color was never set (only the box background was),
                // so it was still whatever Unity's default TMP Input Field text color
                // is - too dim to read against a dark box. Font was never set either.
                if (managerNameInput.textComponent != null)
                {
                    managerNameInput.textComponent.color = ManagerUITheme.TextPrimary;
                    managerNameInput.textComponent.fontSize = 18;
                    if (TMP_Settings.defaultFontAsset != null) managerNameInput.textComponent.font = TMP_Settings.defaultFontAsset;
                }

                if (managerNameInput.placeholder is TextMeshProUGUI placeholderLabel)
                {
                    placeholderLabel.color = ManagerUITheme.TextMuted;
                    placeholderLabel.fontSize = 18;
                    if (TMP_Settings.defaultFontAsset != null) placeholderLabel.font = TMP_Settings.defaultFontAsset;

                    // The Editor-authored input field still carries TMP's stock "Enter
                    // text..." placeholder copy - never intentionally set, just never
                    // cleared. The big centered box plus the MANAGER NAME caption above it
                    // already say what the field is for.
                    placeholderLabel.text = "";
                }

                GameObject inputAccent = new GameObject("LeftAccent", typeof(RectTransform), typeof(Image));
                inputAccent.transform.SetParent(inputRect, false);
                RectTransform inputAccentRect = inputAccent.GetComponent<RectTransform>();
                inputAccentRect.anchorMin = new Vector2(0f, 0f);
                inputAccentRect.anchorMax = new Vector2(0f, 1f);
                inputAccentRect.pivot = new Vector2(0f, 0.5f);
                inputAccentRect.sizeDelta = new Vector2(3f, 0f);
                inputAccentRect.anchoredPosition = Vector2.zero;
                inputAccent.GetComponent<Image>().color = ManagerUITheme.Accent;
            }

            // teamGridContainer's position is set per-step by RefreshTeamSelectStepUI
            // instead of fixed here - step 2 stretches it to the full content width now
            // that it no longer shares the row with the name column.

            // confirmTeamButton/teamSelectBackButton are Editor-placed and were never
            // explicitly positioned in code - their baked scene position was tuned
            // against the old 960x540 CanvasScaler reference resolution, so once that
            // changed to 1920x1080 their fixed pixel offset would have silently drifted
            // out of the mockup's intended footer position. Pinned explicitly here, same
            // pattern as every other screen's buttons.
            if (confirmTeamButton != null)
            {
                ManagerUITheme.SetPointAnchor(confirmTeamButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-contentLeft, 22f), new Vector2(200f, 48f));
            }

            if (teamSelectBackButton != null)
            {
                ManagerUITheme.SetPointAnchor(teamSelectBackButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-contentLeft - 200f - 12f, 22f), new Vector2(140f, 48f));
            }

            // Same TMP mesh-generation flakiness documented on the Title screen's
            // subtitle/Player Inspect's labels - a freshly AddComponent'd
            // TextMeshProUGUI can come out with correct text/color/position but zero
            // generated characters (confirmed live on this exact screen's "NEW CAREER"
            // title: characterCount=0 despite everything else about it being correct).
            // Not limited to any one label, so this sweeps everything under the panel
            // rather than guessing which one might be affected this time.
            StartCoroutine(RecoverBlankLabelsNextFrame(teamSelectPanel.transform));
        }

        // Real 20-club grid (5 columns), built once at runtime from availableTeamNames -
        // the actual clubs in the season file, not a hand-authored/fictional list.
        private void BuildTeamSelectGrid()
        {
            if (teamGridContainer == null)
            {
                return;
            }

            const int columns = 5;
            int rows = Mathf.Max(1, Mathf.CeilToInt(availableTeamNames.Count / (float)columns));

            teamGridButtons.Clear();

            for (int i = 0; i < availableTeamNames.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                GameObject cell = new GameObject($"Club_{availableTeamNames[i]}", typeof(RectTransform), typeof(Image), typeof(Button));
                cell.transform.SetParent(teamGridContainer, false);

                RectTransform cellRect = cell.GetComponent<RectTransform>();
                float colWidth = 1f / columns;
                float rowHeight = 1f / rows;
                cellRect.anchorMin = new Vector2(col * colWidth, 1f - (row + 1) * rowHeight);
                cellRect.anchorMax = new Vector2((col + 1) * colWidth, 1f - row * rowHeight);
                cellRect.offsetMin = new Vector2(4f, 4f);
                cellRect.offsetMax = new Vector2(-4f, -4f);

                Image image = cell.GetComponent<Image>();
                image.color = ManagerUITheme.CardNeutralAlt;

                Button button = cell.GetComponent<Button>();
                button.targetGraphic = image;

                ManagerUITheme.BuildLabel(cell.transform, availableTeamNames[i].ToUpperInvariant(), 16, ManagerUITheme.TextBody, TextAlignmentOptions.Center, FontStyles.Bold, noWrap: false);

                int capturedIndex = i;
                button.onClick.AddListener(() => OnTeamGridTileClicked(capturedIndex));

                teamGridButtons.Add(button);
            }
        }

        private void OnTeamGridTileClicked(int index)
        {
            selectedTeamIndex = index;
            RefreshTeamSelectUI();
        }

        private void RefreshTeamSelectUI()
        {
            for (int i = 0; i < teamGridButtons.Count; i++)
            {
                Button button = teamGridButtons[i];

                if (button == null || !button.TryGetComponent(out Image image))
                {
                    continue;
                }

                bool selected = i == selectedTeamIndex;
                image.color = selected ? ManagerUITheme.Accent : ManagerUITheme.CardNeutralAlt;

                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.color = selected ? ManagerUITheme.OnAccent : ManagerUITheme.TextBody;
                }
            }
        }

        // Drives the two-step New Career wizard: step 1 is a big centered manager name
        // field (blocks progression until non-empty - this is also what makes a manager
        // name required, which the old single-screen version never enforced at all),
        // step 2 is the club grid stretched to the full content width now that it isn't
        // sharing the row with a name column. Called on every step change and on every
        // keystroke in the name field (see the onValueChanged listener), so it has to
        // stay cheap - just RectTransform/active-state/text updates, no rebuilding.
        private void RefreshTeamSelectStepUI()
        {
            const float bandHeight = 90f;
            const float contentWidth = 1700f;
            const float contentLeft = (1920f - contentWidth) / 2f;
            const float nameColumnWidth = 340f;
            const float captionTop = bandHeight + 40f;
            const float captionHeight = 18f;
            const float contentTop = captionTop + captionHeight + 32f;

            bool isNameStep = teamSelectStep == 1;

            if (teamSelectSubtitleObj != null)
            {
                TextMeshProUGUI subtitleLabel = teamSelectSubtitleObj.GetComponentInChildren<TextMeshProUGUI>();
                if (subtitleLabel != null)
                {
                    subtitleLabel.text = isNameStep
                        ? "Step 1 of 2 · Manager Name"
                        : "Step 2 of 2 · Select Club";
                }
            }

            if (teamSelectClubCaption != null) teamSelectClubCaption.SetActive(!isNameStep);
            if (teamGridContainer != null) teamGridContainer.gameObject.SetActive(!isNameStep);

            if (teamSelectNameCaption != null)
            {
                teamSelectNameCaption.SetActive(isNameStep);

                if (isNameStep)
                {
                    // Re-anchored to sit centered directly above the big centered input
                    // box below, instead of its original top-left chrome position (which
                    // reads as orphaned once the input it labels is no longer nearby).
                    RectTransform captionRect = teamSelectNameCaption.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(captionRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(640f, 24f));

                    TextMeshProUGUI captionLabel = teamSelectNameCaption.GetComponentInChildren<TextMeshProUGUI>();
                    if (captionLabel != null) captionLabel.alignment = TextAlignmentOptions.Center;
                }
            }

            if (managerNameInput != null)
            {
                managerNameInput.gameObject.SetActive(isNameStep);

                RectTransform inputRect = managerNameInput.GetComponent<RectTransform>();

                if (isNameStep)
                {
                    // Big and centered in the body area between the header/footer bands -
                    // "a big text input thing in the middle", not squeezed into the old
                    // 340px side column that only existed to share space with the grid.
                    const float bigInputWidth = 640f;
                    const float bigInputHeight = 72f;
                    ManagerUITheme.SetPointAnchor(
                        inputRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(bigInputWidth, bigInputHeight));

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 34;
                    if (managerNameInput.placeholder is TextMeshProUGUI bigPlaceholder) bigPlaceholder.fontSize = 34;
                }
                else
                {
                    ManagerUITheme.SetPointAnchor(
                        inputRect, new Vector2(0f, 1f), new Vector2(contentLeft, -contentTop), new Vector2(nameColumnWidth, 56f));

                    if (managerNameInput.textComponent != null) managerNameInput.textComponent.fontSize = 18;
                    if (managerNameInput.placeholder is TextMeshProUGUI smallPlaceholder) smallPlaceholder.fontSize = 18;
                }
            }

            // Save Name field (session 15) - sits just below the Manager Name field on
            // step 1 only, same centered-column layout. Never shown on step 2 - a save
            // is created once per new career, not re-named while picking a club.
            if (teamSelectSaveNameCaption != null)
            {
                teamSelectSaveNameCaption.SetActive(isNameStep);

                if (isNameStep)
                {
                    RectTransform captionRect = teamSelectSaveNameCaption.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(captionRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -52f), new Vector2(500f, 20f));

                    TextMeshProUGUI captionLabel = teamSelectSaveNameCaption.GetComponentInChildren<TextMeshProUGUI>();
                    if (captionLabel != null) captionLabel.alignment = TextAlignmentOptions.Center;
                }
            }

            if (saveNameInput != null)
            {
                saveNameInput.gameObject.SetActive(isNameStep);

                if (isNameStep)
                {
                    RectTransform saveNameRect = saveNameInput.GetComponent<RectTransform>();
                    ManagerUITheme.SetPointAnchor(saveNameRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(500f, 56f));
                }
            }

            if (teamGridContainer != null && !isNameStep)
            {
                RectTransform gridRect = teamGridContainer.GetComponent<RectTransform>();
                gridRect.anchorMin = new Vector2(0f, 0f);
                gridRect.anchorMax = new Vector2(1f, 1f);
                gridRect.offsetMin = new Vector2(contentLeft, bandHeight + 47f);
                gridRect.offsetMax = new Vector2(-contentLeft, -contentTop);
            }

            if (confirmTeamButton != null)
            {
                TextMeshProUGUI confirmLabel = confirmTeamButton.GetComponentInChildren<TextMeshProUGUI>();
                if (confirmLabel != null) confirmLabel.text = isNameStep ? "CONTINUE" : "START CAREER";

                bool nameFilled = managerNameInput != null && !string.IsNullOrWhiteSpace(managerNameInput.text);
                confirmTeamButton.interactable = !isNameStep || nameFilled;
            }
        }

        public void OnTeamSelectBackClicked()
        {
            if (teamSelectStep == 2)
            {
                teamSelectStep = 1;
                RefreshTeamSelectStepUI();
                return;
            }

            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

            ShowTitleScreen();
        }

        // Session 16 - mirrors ApplySaveData's own clear block (see that method), just
        // for "starting fresh" instead of "restoring from a save". Every one of these
        // is a real thing Manager Mode accumulates over a career that has no other
        // reset point - a second career started in the same running session would
        // otherwise inherit all of it from whichever career ran before it.
        private void ResetSessionStateForNewCareer()
        {
            usesWorldGeneration = worldGenerationService != null;
            worldLeagueMeanOverall = 0f;
            worldLeagueMaxPositiveDelta = 0f;
            currentSeason = 1;
            currentFixtureIndex = 0;
            careerCalendar.StartSeason(FirstCareerSeasonStartYear);
            seasonEndRewardsAppliedForCurrentSeason = false;

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            recentFormByTeamId.Clear();

            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;
            tacticalSliders.Width = WidthSetting.Balanced;
            tacticalSliders.DefensiveDepth = DefensiveDepthSetting.Balanced;
            tacticalSliders.Tempo = TempoSetting.Balanced;

            squadsByTeamName.Clear();
            squadRolesByTeamName.Clear();
            simulatedMatchdays.Clear();
            loanTracker.Clear();
            academy.Clear();
            transferNegotiation.Clear();
            scouting.Clear();
            inbox.Clear();
            careerHistory.Clear();
            finance.Clear();

            // Live team strength (session 16) - restore every club's strength back to
            // the pure trained value BEFORE this new career generates any squads off it
            // (GetOrCreateAgentTeam reads strength.AttackStrength/DefenceStrength to
            // build a fresh squad) - otherwise a club that drifted during a previous
            // career this same session would hand that drift straight into the new one.
            baselineAverageOverallByTeam.Clear();
            foreach (string teamName in availableTeamNames)
            {
                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
                strength.AttackStrength = originalAttackStrengthByTeam[teamName];
                strength.DefenceStrength = originalDefenceStrengthByTeam[teamName];
            }
        }

        public void OnConfirmTeamClicked()
        {
            if (teamSelectStep == 1)
            {
                if (managerNameInput == null || string.IsNullOrWhiteSpace(managerNameInput.text))
                {
                    return;
                }

                managerName = managerNameInput.text.Trim();

                // Save Name is optional (unlike Manager Name, doesn't block CONTINUE) -
                // falls back to a sensible default built from what's already been typed
                // rather than forcing a second required field for what's really just a
                // cosmetic label in the Load Career browser.
                currentSaveName = saveNameInput != null && !string.IsNullOrWhiteSpace(saveNameInput.text)
                    ? saveNameInput.text.Trim()
                    : $"{managerName}'s Save";

                // A fresh GUID per new career (session 15) - this is what
                // ManagerSaveService actually writes to disk as, so every save this
                // session (and any future one) lands on the same file instead of
                // minting a new one each time. Must happen here, not lazily inside
                // ManagerSaveService.Save, since OnExitToTitleClicked's autosave has no
                // other moment to know "this is a brand new career."
                currentSaveId = Guid.NewGuid().ToString("N");

                teamSelectStep = 2;
                RefreshTeamSelectStepUI();
                return;
            }

            if (availableTeamNames.Count > 0)
            {
                managedTeamName = availableTeamNames[selectedTeamIndex];
            }

            if (managerNameInput != null && !string.IsNullOrWhiteSpace(managerNameInput.text))
            {
                managerName = managerNameInput.text.Trim();
            }

            // Session 16 - real bug Thomas hit live: starting a second career in the
            // same running session (Editor Play Mode or the built .exe) inherited the
            // first career's entire Inbox and squad ("everything is the same except i
            // have a new name"). Nothing here ever reset between careers - only the
            // Load Save path did. Must run before anything below reads/writes squad,
            // fixture, or Inbox state for the new career.
            ResetSessionStateForNewCareer();

            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            if (managedTeamFixtures.Count == 0)
            {
                Debug.LogWarning($"ManagerPrototypeController: no fixtures found for '{managedTeamName}' in {seasonFile.name}.");
            }

            SendCareerStartInboxMessages();

            if (teamSelectPanel != null) teamSelectPanel.SetActive(false);

            ShowSeasonHub();
        }

        // Tier 1 potentialemails.txt batch (session 14) - the three career-start
        // flavour messages (#1 welcome, #2 pre-season expectations, #30 recruitment
        // teaser) land together the moment a brand new career actually begins, not on
        // every load - OnConfirmTeamClicked only ever runs once per career, unlike
        // ShowSeasonHub which also fires on load/return-to-hub. All three are pure
        // flavour text (no live data to bake in beyond the club name), so there's no
        // harm sending them at once - Thomas can read them whenever he opens the Inbox.
        private void SendCareerStartInboxMessages()
        {
            inbox.Add(InboxMessageType.WelcomeCareer, $"Welcome to {managedTeamName}",
                $"Welcome to {managedTeamName}. The board is pleased to confirm your appointment ahead of the new season. " +
                "Our expectations are simple: establish a clear identity, manage the squad responsibly, and ensure the club remains competitive over the full campaign. " +
                "You'll have immediate access to the squad screen, upcoming fixtures, tactical setup, and matchday controls. Good luck - the season starts now.",
                0);

            inbox.Add(InboxMessageType.SeasonExpectations, "Season Expectations",
                "Before the season begins, the board wants to outline its expectations. " +
                "We'll primarily judge performance through league position, consistency of results, and squad development. Individual defeats won't define your future, but long poor runs of form will naturally increase pressure. " +
                "We expect tactical decisions that reflect the strength of the squad and the quality of upcoming opposition.",
                0);

            inbox.Add(InboxMessageType.RecruitmentTeaser, "Player Recruitment Report Available",
                "The recruitment department has begun compiling reports on potential squad improvements. " +
                "This system isn't currently active, but future versions could let you review targets, compare player attributes, and strengthen weak areas of the squad.",
                0);
        }

        private void TrainStatisticalModel()
        {
            if (trainingSeasonFiles == null || trainingSeasonFiles.Length == 0)
            {
                Debug.LogError("ManagerPrototypeController: no training season files assigned — expected goals predictions will be degenerate until this is fixed.");
                return;
            }

            List<OpenFootballMatch> trainingMatches = new();

            foreach (TextAsset file in trainingSeasonFiles)
            {
                if (file == null)
                {
                    continue;
                }

                trainingMatches.AddRange(OpenFootballTextParser.ParseSeasonFile(file.text, file.name));
            }

            if (trainingMatches.Count == 0)
            {
                Debug.LogError("ManagerPrototypeController: training season files produced no matches — expected goals predictions will be degenerate until this is fixed.");
                return;
            }

            statisticalModel.Train(trainingMatches);
        }

        // --- Mentality selection (Balanced default: no modifier applied). Renamed from
        // "Tactic" - mentality is the real football term for this attacking/balanced/
        // defensive spectrum; "tactic" more naturally implies formation/shape, which
        // this has nothing to do with (see the Tactics Board for that, a completely
        // separate screen). Selectable both pre-match (Hub/Matchday Prep) and now live
        // during a match too - see ApplyLiveMentalityChangeIfMatchInProgress. ---

    }
}
