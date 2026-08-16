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
        private ManagerSaveData BuildSaveData()
        {
            AgentTeam managedTeam = GetOrCreateAgentTeam(managedTeamName);
            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            float budget = finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);

            ManagerSaveData data = new ManagerSaveData
            {
                UsesWorldGeneration = usesWorldGeneration,
                SaveId = currentSaveId,
                SaveName = currentSaveName,
                ManagerName = managerName,
                ManagedTeamName = managedTeamName,
                CurrentSeason = currentSeason,
                CurrentFixtureIndex = currentFixtureIndex,
                CurrentCareerDate = careerCalendar.SerializeDate(),
                SeasonStartYear = careerCalendar.SeasonStartYear,
                ActiveSeasonFileName = allSeasonFixtures.Count > 0 ? allSeasonFixtures[0].Season : seasonFile.name,
                ManagedSquad = AgentTeamSaveData.FromTeam(managedTeam),
                ManagedBudget = budget,
                ManagedTotalTransferSpend = finance.GetTotalTransferSpend(managedTeamName),
                ManagedTotalTransferIncome = finance.GetTotalTransferIncome(managedTeamName),
                HasManagedTactics = true,
                ManagedTactics = new ManagerTacticsSaveData
                {
                    Width = tacticalSliders.Width,
                    DefensiveDepth = tacticalSliders.DefensiveDepth,
                    Tempo = tacticalSliders.Tempo
                },
                ManagedRoles = new ManagerSquadRolesSaveData
                {
                    CaptainId = roles.Captain?.PlayerId,
                    ViceCaptainId = roles.ViceCaptain?.PlayerId,
                    PenaltyTakerId = roles.PenaltyTaker?.PlayerId,
                    FreeKickTakerId = roles.FreeKickTaker?.PlayerId,
                    LeftCornerTakerId = roles.LeftCornerTaker?.PlayerId,
                    RightCornerTakerId = roles.RightCornerTaker?.PlayerId
                }
            };

            foreach (PlayerAgent player in managedTeam.Players)
            {
                AttackDefendRole role = roles.GetRole(player);
                if (role == AttackDefendRole.Attacking) data.ManagedRoles.AttackingRolePlayerIds.Add(player.PlayerId);
                else if (role == AttackDefendRole.Defensive) data.ManagedRoles.DefensiveRolePlayerIds.Add(player.PlayerId);
            }

            // Loan system (session 9) - see ManagerSaveData.LoanedOutPlayers' comment.
            // Only the managed team ever loans a player out in this scope, so no other
            // club's loans need saving.
            foreach (ManagerLoanTracker.LoanRecord loan in loanTracker.ActiveLoans)
            {
                if (loan.OriginTeamName == managedTeamName)
                {
                    data.LoanedOutPlayers.Add(PlayerAgentSaveData.FromPlayer(loan.Player));
                }
            }

            // Youth academy (session 9; empty-slot rework session 13) - positional, see
            // ManagerSaveData.AcademySlots' comment.
            foreach (PlayerAgent slot in academy.GetFullAcademySlots())
            {
                data.AcademySlots.Add(slot == null
                    ? new AcademySlotSaveData { IsEmpty = true }
                    : new AcademySlotSaveData { IsEmpty = false, Prospect = PlayerAgentSaveData.FromPlayer(slot) });
            }

            foreach (LeagueTable.Entry entry in playableTable.Sorted())
            {
                data.TableEntries.Add(new LeagueTableEntrySaveData
                {
                    TeamId = entry.TeamId,
                    Played = entry.Played,
                    Wins = entry.Wins,
                    Draws = entry.Draws,
                    Losses = entry.Losses,
                    GoalsFor = entry.GoalsFor,
                    GoalsAgainst = entry.GoalsAgainst,
                    Points = entry.Points
                });
            }

            foreach (SeasonRecord record in careerHistory.Records)
            {
                data.CareerHistory.Add(new SeasonRecordSaveData
                {
                    Season = record.Season,
                    FinalPosition = record.FinalPosition,
                    IsChampion = record.IsChampion,
                    PrizeMoney = record.PrizeMoney,
                    BoardBoost = record.BoardBoost,
                    Wins = record.Wins,
                    Draws = record.Draws,
                    Losses = record.Losses,
                    Points = record.Points
                });
            }

            // Youth scouting missions + discoveries (session 13 rework) - positional
            // mission briefs, plus every still-unclaimed discovery paired with the
            // matchday it was found on so the poach timer resumes correctly.
            for (int slot = 0; slot < ManagerScouting.ScoutSlots; slot++)
            {
                data.ScoutMissions.Add(new ScoutMissionSaveData
                {
                    TargetPositions = new List<PlayerPosition>(scouting.GetMissionPositions(slot)),
                    DaysWithoutDiscovery = scouting.GetDaysWithoutDiscovery(slot)
                });
            }

            foreach (PlayerAgent prospect in scouting.DiscoveredProspects)
            {
                data.DiscoveredProspects.Add(new DiscoveredProspectSaveData
                {
                    Prospect = PlayerAgentSaveData.FromPlayer(prospect),
                    DiscoveredMatchday = scouting.GetDiscoveredMatchday(prospect)
                });
            }

            // Inbox + transfer negotiation (session 13) - see ManagerSaveData's own
            // comment on PendingBidRefundOnLoad for why in-flight bids/transfer-scout
            // assignments don't round-trip by reference and are refunded instead.
            data.InboxMessages = inbox.BuildSaveList();
            data.PendingBidRefundOnLoad = transferNegotiation.GetTotalEscrowed();
            foreach (KeyValuePair<PlayerAgent, int> listing in outgoingListedDay)
            {
                data.OutgoingTransfers.Add(new OutgoingTransferSaveData
                {
                    PlayerId = listing.Key.PlayerId,
                    ListedDay = listing.Value,
                    HasOffer = outgoingOffers.TryGetValue(listing.Key, out float offer),
                    OfferAmount = offer
                });
            }

            foreach (KeyValuePair<int, List<char>> form in recentFormByTeamId)
            {
                data.RecentForm.Add(new TeamFormSaveData
                {
                    TeamId = form.Key,
                    Results = new string(form.Value.TakeLast(5).ToArray())
                });
            }

            return data;
        }

        // Rebuilds every piece of state BuildSaveData captured, then jumps straight to
        // the Season Hub - a loaded career resumes exactly where Save & Exit left it,
        // not back at team select.
        private void ApplySaveData(ManagerSaveData data)
        {
            // Multi-save support (session 15) - so any save made later this session
            // (OnExitToTitleClicked) overwrites the file this career was actually loaded
            // from instead of minting a new one.
            currentSaveId = data.SaveId;
            currentSaveName = data.SaveName;
            usesWorldGeneration = data.UsesWorldGeneration && worldGenerationService != null;
            worldLeagueMeanOverall = 0f;
            worldLeagueMaxPositiveDelta = 0f;

            managerName = data.ManagerName;
            managedTeamName = data.ManagedTeamName;
            currentSeason = data.CurrentSeason;
            currentFixtureIndex = data.CurrentFixtureIndex;
            int restoredSeasonStartYear = data.SeasonStartYear > 0
                ? data.SeasonStartYear
                : FirstCareerSeasonStartYear + Mathf.Max(0, currentSeason - 1);
            careerCalendar.Restore(restoredSeasonStartYear, data.CurrentCareerDate, currentFixtureIndex);

            TextAsset activeFile = FindSeasonFileAssetByName(data.ActiveSeasonFileName) ?? seasonFile;
            allSeasonFixtures = OpenFootballTextParser.ParseSeasonFile(activeFile.text, activeFile.name);
            availableTeamNames = BuildAvailableTeamNames();
            managedTeamFixtures = allSeasonFixtures.FindAll(m =>
                m.HomeTeam == managedTeamName || m.AwayTeam == managedTeamName);

            playableTable.Reset();
            foreach (string teamName in availableTeamNames)
            {
                playableTable.EnsureTeam(teamRegistry.GetTeamId(teamName));
            }

            recentFormByTeamId.Clear();
            if (data.RecentForm != null)
            {
                foreach (TeamFormSaveData savedForm in data.RecentForm)
                {
                    if (savedForm == null || string.IsNullOrEmpty(savedForm.Results)) continue;
                    List<char> validResults = savedForm.Results
                        .Where(result => result == 'W' || result == 'D' || result == 'L')
                        .TakeLast(5)
                        .ToList();
                    if (validResults.Count > 0) recentFormByTeamId[savedForm.TeamId] = validResults;
                }
            }

            // The notification/cooldown state below still doesn't survive save/load, so
            // it is reset here rather than left holding stale
            // pre-load state (a mid-season-review flag from a different season, an
            // injured-player tracked set for a squad about to be rebuilt fresh below).
            midSeasonReviewSentForCurrentSeason = false;
            lastPostMatchReactionMatchday = -PostMatchReactionMinGapMatchdays;
            lastLowStaminaWarningMatchday = -LowStaminaWarningCooldownMatchdays;
            poorRunMessageSentForCurrentStreak = false;
            strongRunMessageSentForCurrentStreak = false;
            injuredPlayersTracked.Clear();
            nextMatchOnlyOverrideActive = false;
            nextMatchOverrideDefaultStartingEleven = null;

            if (data.HasManagedTactics && data.ManagedTactics != null)
            {
                tacticalSliders.Width = data.ManagedTactics.Width;
                tacticalSliders.DefensiveDepth = data.ManagedTactics.DefensiveDepth;
                tacticalSliders.Tempo = data.ManagedTactics.Tempo;
            }
            else
            {
                tacticalSliders.Width = WidthSetting.Balanced;
                tacticalSliders.DefensiveDepth = DefensiveDepthSetting.Balanced;
                tacticalSliders.Tempo = TempoSetting.Balanced;
            }

            foreach (LeagueTableEntrySaveData entry in data.TableEntries)
            {
                playableTable.SetEntry(entry.TeamId, entry.Played, entry.Wins, entry.Draws, entry.Losses, entry.GoalsFor, entry.GoalsAgainst, entry.Points);
            }

            squadsByTeamName.Clear();
            squadRolesByTeamName.Clear();
            simulatedMatchdays.Clear();
            loanTracker.Clear();
            academy.Clear();
            transferNegotiation.Clear();
            outgoingListedDay.Clear();
            outgoingOffers.Clear();

            AgentTeam managedTeam = data.ManagedSquad.ToTeam();
            squadsByTeamName[managedTeamName] = managedTeam;
            Dictionary<string, PlayerAgent> outgoingPlayersById = managedTeam.Players
                .Where(player => !string.IsNullOrEmpty(player.PlayerId))
                .ToDictionary(player => player.PlayerId, player => player);
            foreach (OutgoingTransferSaveData outgoing in data.OutgoingTransfers ?? new List<OutgoingTransferSaveData>())
            {
                if (!outgoingPlayersById.TryGetValue(outgoing.PlayerId, out PlayerAgent player)) continue;
                outgoingListedDay[player] = outgoing.ListedDay;
                if (outgoing.HasOffer) outgoingOffers[player] = outgoing.OfferAmount;
            }

            // Live team strength (session 16) - this bypasses GetOrCreateAgentTeam
            // entirely (the managed squad is restored directly from save data, not
            // generated), so its average-Overall baseline would otherwise never get
            // captured and RecalculateLiveTeamStrength would silently no-op for the
            // player's own team for the rest of this session. Re-baselines the AVERAGE
            // to the just-loaded squad rather than trying to persist the original
            // career-start average across saves (would need new save-schema fields) - a
            // save/load "resets the clock" on live-strength drift for the managed team,
            // same real limitation AI clubs already have (their squads aren't persisted
            // at all, see squadsByTeamName.Clear() a few lines up - they regenerate
            // fresh, and fresh IS their new baseline too). originalAttackStrengthByTeam/
            // originalDefenceStrengthByTeam need no equivalent fix here - they're the
            // one-time-ever training snapshot (see Start), never mutated, so they're
            // already correct for any team including a freshly-loaded managed one.
            baselineAverageOverallByTeam[managedTeamName] = GetAverageOverall(managedTeam);
            if (usesWorldGeneration && TryGetWorldTarget(managedTeamName, out SquadQualityTarget loadedTarget))
            {
                ConfigureInitialWorldStrength(managedTeamName, loadedTarget.FirstTeamOverall);
            }

            Dictionary<string, PlayerAgent> managedPlayersById = new();
            foreach (PlayerAgent p in managedTeam.Players) managedPlayersById[p.PlayerId] = p;

            // Legacy saves kept a separate hidden emergency pool. New saves persist
            // reserves inside AgentTeamSaveData; import up to ten legacy players only
            // when that new list is absent, giving old careers the same 30-player shape.
            if (managedTeam.Reserves.Count == 0 && data.ManagedReservePool != null)
            {
                List<PlayerAgent> legacyPool = new List<PlayerAgent>();
                foreach (PlayerAgentSaveData dto in data.ManagedReservePool) legacyPool.Add(dto.ToPlayer());

                PlayerPosition[] migrationSlots =
                {
                PlayerPosition.GK, PlayerPosition.CB, PlayerPosition.CB,
                PlayerPosition.RB, PlayerPosition.LB, PlayerPosition.DM,
                PlayerPosition.CM, PlayerPosition.RW, PlayerPosition.LW,
                PlayerPosition.ST
            };

                foreach (PlayerPosition slot in migrationSlots)
                {
                    if (legacyPool.Count == 0) break;
                    PlayerAgent best = legacyPool[0];
                    float bestFit = best.GetPositionFit(slot);
                    foreach (PlayerAgent candidate in legacyPool)
                    {
                        float fit = candidate.GetPositionFit(slot);
                        if (fit > bestFit)
                        {
                            best = candidate;
                            bestFit = fit;
                        }
                    }

                    legacyPool.Remove(best);
                    managedTeam.AddReservePlayer(best);
                }
            }

            // Loan system (session 9) - re-register each restored player as on loan
            // (SendOnLoan rolls a fresh destination flavor name, harmless since it was
            // never saved - cosmetic only) rather than adding them back to
            // managedTeam.Players, since they're still out on loan in the loaded save.
            foreach (PlayerAgentSaveData dto in data.LoanedOutPlayers)
            {
                loanTracker.SendOnLoan(dto.ToPlayer(), managedTeamName);
            }

            // Youth academy (session 9; empty-slot rework session 13) - only restore if
            // the pool was actually generated before saving (data.AcademySlots.Count >
            // 0). If the player never opened the Academy tab this career, nothing was
            // ever generated to save - restoring an EMPTY list here would still mark the
            // pool as "already created" (GetOrCreateAcademyPool's null-check would never
            // trigger again), permanently freezing it at zero prospects instead of
            // lazily generating fresh ones the first time it's actually opened after
            // loading. Positional - a saved empty slot restores to the same index.
            if (data.AcademySlots.Count > 0)
            {
                List<PlayerAgent> restoredAcademy = new();
                foreach (AcademySlotSaveData slotData in data.AcademySlots)
                {
                    restoredAcademy.Add(slotData.IsEmpty ? null : slotData.Prospect.ToPlayer());
                }
                academy.RestoreAcademyPool(restoredAcademy);
            }

            ManagerSquadRoles roles = GetOrCreateSquadRoles(managedTeamName);
            ManagerSquadRolesSaveData rolesData = data.ManagedRoles;

            if (rolesData != null)
            {
                roles.Captain = ResolvePlayerById(managedPlayersById, rolesData.CaptainId);
                roles.ViceCaptain = ResolvePlayerById(managedPlayersById, rolesData.ViceCaptainId);
                roles.PenaltyTaker = ResolvePlayerById(managedPlayersById, rolesData.PenaltyTakerId);
                roles.FreeKickTaker = ResolvePlayerById(managedPlayersById, rolesData.FreeKickTakerId);
                roles.LeftCornerTaker = ResolvePlayerById(managedPlayersById, rolesData.LeftCornerTakerId);
                roles.RightCornerTaker = ResolvePlayerById(managedPlayersById, rolesData.RightCornerTakerId);

                foreach (string id in rolesData.AttackingRolePlayerIds)
                {
                    PlayerAgent p = ResolvePlayerById(managedPlayersById, id);
                    if (p != null) roles.SetRole(p, AttackDefendRole.Attacking);
                }

                foreach (string id in rolesData.DefensiveRolePlayerIds)
                {
                    PlayerAgent p = ResolvePlayerById(managedPlayersById, id);
                    if (p != null) roles.SetRole(p, AttackDefendRole.Defensive);
                }
            }

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(managedTeamName);
            finance.GetOrSeedBudget(managedTeamName, strength.AttackStrength, strength.DefenceStrength);
            // + PendingBidRefundOnLoad - any bid still pending at save time was dropped
            // above (transferNegotiation.Clear()), so its escrowed amount is credited
            // back here instead of being silently lost (see ManagerSaveData's comment).
            finance.AdjustBudget(managedTeamName, data.ManagedBudget + data.PendingBidRefundOnLoad - finance.GetBudget(managedTeamName));
            finance.SetTotalTransferSpend(managedTeamName, data.ManagedTotalTransferSpend);
            finance.SetTotalTransferIncome(managedTeamName, data.ManagedTotalTransferIncome);

            foreach (SeasonRecordSaveData recordData in data.CareerHistory)
            {
                careerHistory.AddRecord(new SeasonRecord
                {
                    Season = recordData.Season,
                    FinalPosition = recordData.FinalPosition,
                    IsChampion = recordData.IsChampion,
                    PrizeMoney = recordData.PrizeMoney,
                    BoardBoost = recordData.BoardBoost,
                    Wins = recordData.Wins,
                    Draws = recordData.Draws,
                    Losses = recordData.Losses,
                    Points = recordData.Points
                });
            }

            // Youth scouting missions + discoveries (session 13 rework).
            for (int slot = 0; slot < data.ScoutMissions.Count && slot < ManagerScouting.ScoutSlots; slot++)
            {
                scouting.RestoreMissionBrief(slot, data.ScoutMissions[slot].TargetPositions);
                scouting.RestoreMissionDrought(slot, data.ScoutMissions[slot].DaysWithoutDiscovery);
            }

            List<PlayerAgent> restoredDiscoveries = new();
            List<int> restoredDiscoveryMatchdays = new();
            foreach (DiscoveredProspectSaveData dto in data.DiscoveredProspects)
            {
                restoredDiscoveries.Add(dto.Prospect.ToPlayer());
                int discoveredDay = dto.DiscoveredMatchday;
                if (data.SaveVersion < 3 && discoveredDay > 0)
                {
                    DateTime legacyDate = careerCalendar.GetFixtureDate(Mathf.Max(0, discoveredDay - 1));
                    discoveredDay = careerCalendar.CurrentDayNumber + (int)(legacyDate.Date - careerCalendar.CurrentDate.Date).TotalDays;
                }
                restoredDiscoveryMatchdays.Add(discoveredDay);
            }
            scouting.RestoreDiscoveredProspects(restoredDiscoveries, restoredDiscoveryMatchdays);

            if (data.SaveVersion < 3)
            {
                foreach (InboxMessageSaveData message in data.InboxMessages)
                {
                    if (message.MatchdayReceived <= 0) continue;
                    DateTime legacyDate = careerCalendar.GetFixtureDate(Mathf.Max(0, message.MatchdayReceived - 1));
                    message.MatchdayReceived = careerCalendar.CurrentDayNumber + (int)(legacyDate.Date - careerCalendar.CurrentDate.Date).TotalDays;
                }
            }

            inbox.RestoreFromSave(data.InboxMessages);

            seasonEndRewardsAppliedForCurrentSeason = true;

            ShowSeasonHub();
        }

        private static PlayerAgent ResolvePlayerById(Dictionary<string, PlayerAgent> playersById, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            return playersById.TryGetValue(playerId, out PlayerAgent p) ? p : null;
        }

        private TextAsset FindSeasonFileAssetByName(string fileName)
        {
            if (seasonFile != null && seasonFile.name == fileName) return seasonFile;

            if (trainingSeasonFiles != null)
            {
                foreach (TextAsset file in trainingSeasonFiles)
                {
                    if (file != null && file.name == fileName) return file;
                }
            }

            return null;
        }

        // CONTINUE (session 15) - the most recently *saved* career, no picker.
        public void OnContinueClicked()
        {
            ManagerSaveData data = ManagerSaveService.GetMostRecentSave();
            if (data == null)
            {
                return;
            }

            if (titlePanel != null) titlePanel.SetActive(false);

            ApplySaveData(data);
        }

        // Called from a Save Browser row (session 15) - loads a specific career by
        // SaveId rather than just "whichever one's newest."
        private void OnLoadSpecificCareerClicked(string saveId)
        {
            ManagerSaveData data = ManagerSaveService.Load(saveId);
            if (data == null)
            {
                return;
            }

            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(false);
            if (titlePanel != null) titlePanel.SetActive(false);

            ApplySaveData(data);
        }

        // --- Save Browser (session 15, multi-save support) - reached from Title's LOAD
        // CAREER button. Same code-built-panel/scroll-view pattern as the Inbox screen -
        // a flat list of every save on disk, newest-saved first, each row a clickable
        // card loading that specific career. ---

        private bool saveBrowserChromeBuilt;
        private GameObject saveBrowserPanel;
        private RectTransform saveBrowserContentContainer;
        private readonly List<GameObject> spawnedSaveBrowserRows = new();

        public void OnOpenLoadCareerBrowserClicked()
        {
            if (!saveBrowserChromeBuilt)
            {
                BuildSaveBrowserChrome();
                saveBrowserChromeBuilt = true;
            }

            if (titlePanel != null) titlePanel.SetActive(false);
            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(true);

            RefreshSaveBrowserUI();
        }

        public void OnSaveBrowserBackClicked()
        {
            if (saveBrowserPanel != null) saveBrowserPanel.SetActive(false);

            ShowTitleScreen();
        }

        private void BuildSaveBrowserChrome()
        {
            if (titlePanel == null || titlePanel.transform.parent == null)
            {
                return;
            }

            const float headerHeight = 90f;

            saveBrowserPanel = new GameObject("SaveBrowserPanel", typeof(RectTransform));
            saveBrowserPanel.transform.SetParent(titlePanel.transform.parent, false);
            RectTransform panelRect = saveBrowserPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            ManagerUITheme.ApplyPanelBackground(saveBrowserPanel);

            GameObject header = ManagerUITheme.BuildAccentBand(saveBrowserPanel.transform, topBand: true, height: headerHeight);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(header.transform, false);
            ManagerUITheme.SetPointAnchor(titleObj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(60f, -22f), new Vector2(400f, 34f));
            ManagerUITheme.BuildLabel(titleObj.transform, "LOAD CAREER", 26, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            Button backButton = ManagerUITheme.BuildButton(header.transform, "BACK", ManagerUITheme.CardNeutral, ManagerUITheme.TextBody, 13);
            ManagerUITheme.SetPointAnchor(backButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-60f, -27f), new Vector2(160f, 36f));
            backButton.onClick.AddListener(OnSaveBrowserBackClicked);

            const float contentWidth = 1200f;
            const float sideMargin = (1920f - contentWidth) / 2f;

            GameObject scrollViewObj = new GameObject("SaveBrowserScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollViewObj.transform.SetParent(saveBrowserPanel.transform, false);
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
            saveBrowserContentContainer = contentObj.GetComponent<RectTransform>();
            saveBrowserContentContainer.anchorMin = new Vector2(0f, 1f);
            saveBrowserContentContainer.anchorMax = new Vector2(1f, 1f);
            saveBrowserContentContainer.pivot = new Vector2(0.5f, 1f);
            saveBrowserContentContainer.anchoredPosition = Vector2.zero;
            saveBrowserContentContainer.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = contentObj.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 10f;

            ContentSizeFitter fitter = contentObj.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            scrollRect.content = saveBrowserContentContainer;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = UiScrollSensitivity;

            StartCoroutine(RecoverBlankLabelsNextFrame(saveBrowserPanel.transform));
        }

        private void RefreshSaveBrowserUI()
        {
            if (saveBrowserContentContainer == null)
            {
                return;
            }

            foreach (GameObject row in spawnedSaveBrowserRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedSaveBrowserRows.Clear();

            List<ManagerSaveData> saves = ManagerSaveService.ListAllSaves();
            // Newest-saved first - same ordinal-string sort GetMostRecentSave uses, just
            // over the whole list instead of just picking the max.
            saves.Sort((a, b) => string.CompareOrdinal(b.LastSavedUtc, a.LastSavedUtc));

            if (saves.Count == 0)
            {
                GameObject emptyObj = new GameObject("EmptyState", typeof(RectTransform), typeof(LayoutElement));
                emptyObj.transform.SetParent(saveBrowserContentContainer, false);
                emptyObj.GetComponent<LayoutElement>().preferredHeight = 60f;
                ManagerUITheme.BuildLabel(emptyObj.transform, "No saved careers yet.", 18, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, noWrap: false);
                spawnedSaveBrowserRows.Add(emptyObj);
            }
            else
            {
                foreach (ManagerSaveData data in saves)
                {
                    spawnedSaveBrowserRows.Add(BuildSaveBrowserRow(data));
                }
            }

            StartCoroutine(RecoverBlankLabelsNextFrame(saveBrowserContentContainer));
        }

        private const float SaveBrowserRowHeight = 88f;

        private GameObject BuildSaveBrowserRow(ManagerSaveData data)
        {
            GameObject row = new GameObject($"Save_{data.SaveId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(saveBrowserContentContainer, false);
            row.GetComponent<LayoutElement>().preferredHeight = SaveBrowserRowHeight;
            Image rowImage = row.GetComponent<Image>();
            rowImage.color = ManagerUITheme.CardNeutralAlt;

            Button rowButton = row.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;
            string capturedSaveId = data.SaveId;
            rowButton.onClick.AddListener(() => OnLoadSpecificCareerClicked(capturedSaveId));

            GameObject nameObj = new GameObject("SaveName", typeof(RectTransform));
            nameObj.transform.SetParent(row.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(20f, -14f);
            nameRect.sizeDelta = new Vector2(-260f, 30f);
            ManagerUITheme.BuildLabel(nameObj.transform, data.SaveName, 20, ManagerUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);

            GameObject detailObj = new GameObject("Detail", typeof(RectTransform));
            detailObj.transform.SetParent(row.transform, false);
            RectTransform detailRect = detailObj.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0f, 1f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.pivot = new Vector2(0f, 1f);
            detailRect.anchoredPosition = new Vector2(20f, -48f);
            detailRect.sizeDelta = new Vector2(-260f, 26f);
            ManagerUITheme.BuildLabel(detailObj.transform, $"{data.ManagerName} · {data.ManagedTeamName} · Season {data.CurrentSeason}", 15, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineLeft);

            GameObject dateObj = new GameObject("LastSaved", typeof(RectTransform));
            dateObj.transform.SetParent(row.transform, false);
            RectTransform dateRect = dateObj.GetComponent<RectTransform>();
            dateRect.anchorMin = new Vector2(1f, 0.5f);
            dateRect.anchorMax = new Vector2(1f, 0.5f);
            dateRect.pivot = new Vector2(1f, 0.5f);
            dateRect.anchoredPosition = new Vector2(-110f, 0f);
            dateRect.sizeDelta = new Vector2(160f, 30f);
            ManagerUITheme.BuildLabel(dateObj.transform, FormatSaveTimestamp(data.LastSavedUtc), 14, ManagerUITheme.TextMuted, TextAlignmentOptions.MidlineRight);

            // Session 16 - Thomas: "an option to delete a save... a crap top now from
            // testing... a confirmation thing too before you actually delete so i dont
            // accidentally delete my main one." ManagerSaveService.Delete already existed
            // (session 15) but was never wired to any UI. A child Button drawn on top of
            // the row's own full-row load Button correctly intercepts its own clicks via
            // normal Unity UI raycasting - clicking DELETE never also fires the row's own
            // load-this-save handler underneath it.
            Button deleteButton = ManagerUITheme.BuildButton(row.transform, "DELETE", ManagerUITheme.Danger, ManagerUITheme.TextPrimary, 13);
            RectTransform deleteRect = deleteButton.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(1f, 0.5f);
            deleteRect.anchorMax = new Vector2(1f, 0.5f);
            deleteRect.pivot = new Vector2(1f, 0.5f);
            deleteRect.anchoredPosition = new Vector2(-20f, 0f);
            deleteRect.sizeDelta = new Vector2(80f, 36f);
            string capturedSaveName = data.SaveName;
            deleteButton.onClick.AddListener(() => OnDeleteSaveClicked(capturedSaveId, capturedSaveName));

            return row;
        }

        private void OnDeleteSaveClicked(string saveId, string saveName)
        {
            ShowConfirmDialog(
                $"Delete \"{saveName}\"? This can't be undone.",
                "DELETE", () =>
                {
                    ManagerSaveService.Delete(saveId);
                    RefreshSaveBrowserUI();
                    RefreshTitleScreenButtons();
                },
                "CANCEL", null);
        }

        // LastSavedUtc is stored as DateTime.ToString("o") (round-trip ISO 8601) purely
        // because that format sorts correctly as a plain string - reparsed here just for
        // a friendlier on-screen "12 Aug 2026, 14:03" instead of the raw ISO string.
        private static string FormatSaveTimestamp(string lastSavedUtc)
        {
            if (string.IsNullOrEmpty(lastSavedUtc))
            {
                return "";
            }

            return DateTime.TryParse(lastSavedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed.ToLocalTime().ToString("d MMM yyyy, HH:mm")
                : "";
        }
    }
}
