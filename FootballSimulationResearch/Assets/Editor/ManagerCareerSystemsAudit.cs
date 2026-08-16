using System;
using System.Collections.Generic;
using System.Linq;
using Manager;
using Manager.Save;
using Sim;
using UnityEditor;
using UnityEngine;

public static class ManagerCareerSystemsAudit
{
    [MenuItem("TFM/Audits/Manager Career Systems")]
    public static void Run()
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        try
        {
            AuditTacticsSerialization();
            AuditRecentFormSerialization();
            AuditBothYouthScoutsAndAcademyIntake();
            AuditInboxExitState();
            AuditTransferValueCurve();
            AuditOutgoingTransferSerialization();
            AuditPositionFitTiers();
            AuditFormationSlotsAndDevelopmentFeedback();
            AuditScoutingRangeVariety();
            Debug.Log("Manager career systems audit passed: tactics persistence, youth scouting, academy intake, Inbox exit state and transfer valuation.");
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    private static void AuditTacticsSerialization()
    {
        ManagerSaveData source = new ManagerSaveData
        {
            HasManagedTactics = true,
            ManagedTactics = new ManagerTacticsSaveData
            {
                Width = WidthSetting.Wide,
                DefensiveDepth = DefensiveDepthSetting.High,
                Tempo = TempoSetting.Fast
            }
        };

        ManagerSaveData restored = JsonUtility.FromJson<ManagerSaveData>(JsonUtility.ToJson(source));
        Require(restored.SaveVersion == 5, "save version did not round-trip as v5");
        Require(restored.HasManagedTactics, "saved tactical state was lost");
        Require(restored.ManagedTactics.Width == WidthSetting.Wide, "Width did not round-trip");
        Require(restored.ManagedTactics.DefensiveDepth == DefensiveDepthSetting.High, "Defensive Depth did not round-trip");
        Require(restored.ManagedTactics.Tempo == TempoSetting.Fast, "Tempo did not round-trip");

        ManagerSaveData legacy = JsonUtility.FromJson<ManagerSaveData>("{}");
        Require(!legacy.HasManagedTactics, "legacy save incorrectly reports saved tactics");
    }

    private static void AuditRecentFormSerialization()
    {
        ManagerSaveData source = new ManagerSaveData();
        source.RecentForm.Add(new TeamFormSaveData { TeamId = 17, Results = "WDLWL" });
        ManagerSaveData restored = JsonUtility.FromJson<ManagerSaveData>(JsonUtility.ToJson(source));
        Require(restored.RecentForm != null && restored.RecentForm.Count == 1, "recent Form row did not round-trip");
        Require(restored.RecentForm[0].TeamId == 17 && restored.RecentForm[0].Results == "WDLWL",
            "recent Form identity/results changed on save");
    }

    private static void AuditBothYouthScoutsAndAcademyIntake()
    {
        UnityEngine.Random.InitState(150826);
        ManagerScouting scouting = new ManagerScouting();
        AgentSquadGenerator generator = new AgentSquadGenerator();
        ManagerInbox inbox = new ManagerInbox();
        scouting.SetMissionBrief(0, new List<PlayerPosition> { PlayerPosition.GK });
        scouting.SetMissionBrief(1, new List<PlayerPosition> { PlayerPosition.ST });

        for (int day = 1; day <= 500; day++)
        {
            scouting.ResolveDailyTick(day, generator, inbox);
        }

        int scoutOneFinds = 0;
        int scoutTwoFinds = 0;
        foreach (InboxMessage message in inbox.Messages)
        {
            if (message.Type != InboxMessageType.ScoutingReport) continue;
            if (message.Body.Contains("searching for a GK")) scoutOneFinds++;
            if (message.Body.Contains("searching for a ST")) scoutTwoFinds++;
        }

        Require(scoutOneFinds > 0, "youth scout one produced no discoveries");
        Require(scoutTwoFinds > 0, "youth scout two produced no discoveries");
        Require(scoutOneFinds >= 20, $"youth scout one discovery rate was implausibly low ({scoutOneFinds} finds in 500 days)");
        Require(scoutTwoFinds >= 20, $"youth scout two discovery rate was implausibly low ({scoutTwoFinds} finds in 500 days)");
        float scoutBalance = scoutOneFinds / (float)scoutTwoFinds;
        Require(scoutBalance >= 0.5f && scoutBalance <= 2f,
            $"youth scout slots were badly imbalanced ({scoutOneFinds} versus {scoutTwoFinds} finds)");
        Require(MaxReportGap(inbox.Messages, "searching for a GK", 500) <= 10, "youth scout one exceeded the guaranteed drought limit");
        Require(MaxReportGap(inbox.Messages, "searching for a ST", 500) <= 10, "youth scout two exceeded the guaranteed drought limit");

        int messagesBeforeBriefUpdate = inbox.Messages.Count;
        scouting.SetMissionBrief(0, new List<PlayerPosition> { PlayerPosition.CM });
        for (int day = 501; day <= 700; day++)
        {
            scouting.ResolveDailyTick(day, generator, inbox);
        }

        int redirectedFinds = 0;
        for (int i = messagesBeforeBriefUpdate; i < inbox.Messages.Count; i++)
        {
            InboxMessage message = inbox.Messages[i];
            if (message.Type != InboxMessageType.ScoutingReport) continue;
            Require(!message.Body.Contains("searching for a GK"), "updated scout brief continued returning the old position");
            if (message.Body.Contains("searching for a CM")) redirectedFinds++;
        }
        Require(redirectedFinds > 0, "updated scout brief produced no discoveries for its new position");

        ManagerAcademy academy = new ManagerAcademy();
        List<PlayerAgent> slots = academy.GetOrCreateAcademyPool(generator, 1f, 1f);
        PlayerAgent releasedProspect = slots[0];
        Require(academy.ReleaseProspect(releasedProspect), "academy slot could not be emptied");
        Require(scouting.DiscoveredProspects.Count > 0, "no live discovery remained for the academy intake test");
        PlayerAgent prospect = scouting.DiscoveredProspects[0];
        Require(scouting.TryClaimProspectToAcademy(prospect, academy, 0), "prospect could not be claimed into an empty academy slot");
        Require(academy.GetFullAcademySlots()[0] == prospect, "academy intake did not retain the selected prospect");
        Require(!scouting.DiscoveredProspects.Contains(prospect), "claimed prospect remained in the scouting list");
    }

    private static void AuditInboxExitState()
    {
        ManagerInbox inbox = new ManagerInbox();
        InboxMessage first = inbox.Add(InboxMessageType.WelcomeCareer, "One", "Body", 1);
        InboxMessage second = inbox.Add(InboxMessageType.RecruitmentTeaser, "Two", "Body", 2);
        first.IsExpanded = true;
        second.IsExpanded = true;
        inbox.MarkAllReadAndCollapse();

        Require(inbox.UnreadCount == 0, "Inbox exit left unread messages");
        Require(!first.IsExpanded && !second.IsExpanded, "Inbox exit left messages expanded");
    }

    private static void AuditTransferValueCurve()
    {
        float replacement = ManagerClubFinance.CalculateMarketValue(60f, 62f, 25);
        float established = ManagerClubFinance.CalculateMarketValue(70f, 73f, 25);
        float strongStarter = ManagerClubFinance.CalculateMarketValue(80f, 82f, 25);
        float elite = ManagerClubFinance.CalculateMarketValue(90f, 92f, 25);
        float wonderkid = ManagerClubFinance.CalculateMarketValue(65f, 85f, 18);
        float agingStarter = ManagerClubFinance.CalculateMarketValue(80f, 82f, 33);

        Require(replacement >= 0.8f && replacement <= 2f, $"60-rated benchmark was £{replacement:F1}m");
        Require(established >= 4f && established <= 9f, $"70-rated benchmark was £{established:F1}m");
        Require(strongStarter >= 25f && strongStarter <= 40f, $"80-rated benchmark was £{strongStarter:F1}m");
        Require(elite >= 140f && elite <= 190f, $"90-rated benchmark was £{elite:F1}m");
        Require(wonderkid > established * 2f, "high-potential youth did not receive a meaningful upside premium");
        Require(agingStarter < strongStarter * 0.4f, "33-year-old retained too much of a prime player's market value");
        Require(replacement < established && established < strongStarter && strongStarter < elite,
            "market value was not monotonic across ability benchmarks");
    }

    private static void AuditOutgoingTransferSerialization()
    {
        ManagerSaveData source = new ManagerSaveData();
        source.OutgoingTransfers.Add(new OutgoingTransferSaveData
        {
            PlayerId = "audit-player",
            ListedDay = 14,
            HasOffer = true,
            OfferAmount = 12.5f
        });
        ManagerSaveData restored = JsonUtility.FromJson<ManagerSaveData>(JsonUtility.ToJson(source));
        Require(restored.OutgoingTransfers.Count == 1, "outgoing transfer listing did not round-trip");
        OutgoingTransferSaveData listing = restored.OutgoingTransfers[0];
        Require(listing.PlayerId == "audit-player" && listing.ListedDay == 14,
            "outgoing transfer listing identity/timing changed on save");
        Require(listing.HasOffer && Mathf.Abs(listing.OfferAmount - 12.5f) < 0.01f,
            "outgoing transfer offer changed on save");
    }

    private static void AuditPositionFitTiers()
    {
        PlayerAgent midfielder = new PlayerAgent("Fit Audit", PlayerRole.Midfielder, PlayerPosition.CM);
        midfielder.SecondaryPositions.Add(PlayerPosition.DM);
        Require(Mathf.Approximately(midfielder.GetPositionFit(PlayerPosition.CM), 1f), "primary position was penalized");
        Require(Mathf.Approximately(midfielder.GetPositionFit(PlayerPosition.DM), 1f), "listed secondary position was penalized");
        Require(Mathf.Approximately(midfielder.GetPositionFit(PlayerPosition.AM), 0.8f), "adjacent position did not receive the modest penalty");
        Require(Mathf.Approximately(midfielder.GetPositionFit(PlayerPosition.ST), 0.6f), "unrelated position did not receive the full penalty");
    }

    private static int MaxReportGap(IReadOnlyList<InboxMessage> messages, string marker, int finalDay)
    {
        List<int> days = messages.Where(message => message.Type == InboxMessageType.ScoutingReport && message.Body.Contains(marker))
            .Select(message => message.MatchdayReceived).Distinct().OrderBy(day => day).ToList();
        int previous = 0;
        int maximum = 0;
        foreach (int day in days) { maximum = Mathf.Max(maximum, day - previous); previous = day; }
        return Mathf.Max(maximum, finalDay - previous);
    }

    private static void AuditFormationSlotsAndDevelopmentFeedback()
    {
        AgentSquadGenerator generator = new AgentSquadGenerator();
        List<PlayerPosition> slots = generator.GetStartingPositions(Formation.ThreeFourTwoOne);
        Require(slots.Contains(PlayerPosition.LM) && slots.Contains(PlayerPosition.RM), "3-4-2-1 did not use LM/RM");
        Require(!slots.Contains(PlayerPosition.LWB) && !slots.Contains(PlayerPosition.RWB), "3-4-2-1 retained wing-back starting slots");
        List<PlayerPosition> bench = generator.GetBenchPositions(Formation.ThreeFourTwoOne);
        Require(bench.Contains(PlayerPosition.LM) && bench.Contains(PlayerPosition.RM), "3-4-2-1 bench did not provide LM/RM cover");
        Require(!bench.Contains(PlayerPosition.LWB) && !bench.Contains(PlayerPosition.RWB), "3-4-2-1 retained wing-back bench slots");

        PlayerAgent player = generator.GenerateReservePlayer(PlayerPosition.CM, 1f, 1f);
        player.Age = 18;
        player.Potential = Mathf.Max(player.Potential, player.GetOverallRating() + 12f);
        ManagerPlayerDevelopment.SnapshotSeasonStart(player);
        for (int day = 0; day < 38; day++) ManagerPlayerDevelopment.ApplyMatchdayProgression(player, true);
        Require(ManagerPlayerDevelopment.GetCurrentSeasonAttributeChanges(player).Count > 0,
            "live development produced no visible attribute changes");
    }

    private static void AuditScoutingRangeVariety()
    {
        AgentSquadGenerator generator = new AgentSquadGenerator();
        ManagerScouting scouting = new ManagerScouting();
        HashSet<string> overallBands = new HashSet<string>();
        HashSet<string> potentialBands = new HashSet<string>();
        for (int i = 0; i < 120; i++)
        {
            PlayerAgent player = generator.GenerateReservePlayer(PlayerPosition.CM, 1f, 1f);
            overallBands.Add(ManagerTransferNegotiation.GetDisplayOverallBand(player));
            potentialBands.Add(scouting.GetDisplayPotential(player));
        }
        Require(overallBands.Count >= 20, $"senior Overall reports were too repetitive ({overallBands.Count} unique bands)");
        Require(potentialBands.Count >= 20, $"academy Potential reports were too repetitive ({potentialBands.Count} unique bands)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Manager career systems audit failed: {message}.");
    }
}
