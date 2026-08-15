using System;
using System.Collections.Generic;
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
            AuditBothYouthScoutsAndAcademyIntake();
            AuditInboxExitState();
            Debug.Log("Manager career systems audit passed: tactics persistence, both youth scouts, academy intake and Inbox exit state.");
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
        Require(restored.SaveVersion == 4, "save version did not round-trip as v4");
        Require(restored.HasManagedTactics, "saved tactical state was lost");
        Require(restored.ManagedTactics.Width == WidthSetting.Wide, "Width did not round-trip");
        Require(restored.ManagedTactics.DefensiveDepth == DefensiveDepthSetting.High, "Defensive Depth did not round-trip");
        Require(restored.ManagedTactics.Tempo == TempoSetting.Fast, "Tempo did not round-trip");

        ManagerSaveData legacy = JsonUtility.FromJson<ManagerSaveData>("{}");
        Require(!legacy.HasManagedTactics, "legacy save incorrectly reports saved tactics");
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

        ManagerAcademy academy = new ManagerAcademy();
        List<PlayerAgent> slots = academy.GetOrCreateAcademyPool(generator, 1f, 1f);
        PlayerAgent prospect = slots[0];
        Require(academy.ReleaseProspect(prospect), "academy slot could not be emptied");
        Require(academy.PlaceProspectInSlot(0, prospect), "prospect could not be placed in an empty academy slot");
        Require(academy.GetFullAcademySlots()[0] == prospect, "academy intake did not retain the selected prospect");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Manager career systems audit failed: {message}.");
    }
}
