using System;
using System.Collections.Generic;
using Sim;
using UnityEngine;

namespace Manager
{
    // Post-match Condition decay/recovery and injury risk, extracted from the managed
    // team's own matchday pipeline (session 10-16) so AI-controlled clubs can share
    // the exact same fatigue/injury model (roadmap: "AI squad evaluation and coherent
    // rotation across the full 30-player pool" - previously AI clubs had no Condition
    // tracking at all, so nothing existed for rotation to actually respond to).
    // Deliberately does NOT touch Inbox notifications, the managed team's
    // injuredPlayersTracked list, or development progression - those are human-facing
    // or managed-team-only concerns the caller applies separately (see
    // ManagerPrototypeController.Matchday.cs's ApplyMatchdayConditionAndInjuries). The
    // human should only ever be told about their own club's injuries; AI player
    // development/aging is a later, not-yet-built stage of the Intelligent AI Clubs
    // epic, kept out of this slice to limit blast radius.
    public static class ManagerMatchdayCondition
    {
        public readonly struct InjuryEvent
        {
            public readonly PlayerAgent Player;
            public readonly int DurationWeeks;

            public InjuryEvent(PlayerAgent player, int durationWeeks)
            {
                Player = player;
                DurationWeeks = durationWeeks;
            }
        }

        // isAutoResolved (backlog item 10, session 11) - during a SIMULATE SEASON skip,
        // Condition decay and injury rolls are gated off entirely so an unattended
        // skip can't compound fatigue with zero manager mitigation. RecordAppearance
        // still runs unconditionally for anyone who played, matching the original.
        public static List<InjuryEvent> ApplyPostMatch(
            AgentTeam team,
            ManagerSquadRoles roles,
            Func<PlayerAgent, float> minutesPlayedLookup,
            int currentDayNumber,
            bool isAutoResolved)
        {
            List<InjuryEvent> newlyInjured = new List<InjuryEvent>();
            List<PlayerAgent> fullSquad = new List<PlayerAgent>(team.StartingEleven);
            fullSquad.AddRange(team.Bench);

            foreach (PlayerAgent player in fullSquad)
            {
                float minutesPlayed = minutesPlayedLookup(player);
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
                        InjuryEvent? injury = TryRollInjury(roles, player, preMatchCondition, currentDayNumber);
                        if (injury.HasValue)
                        {
                            newlyInjured.Add(injury.Value);
                        }
                    }
                }
            }

            return newlyInjured;
        }

        // Injury risk scales sharply as pre-match Condition drops - a manager who never
        // rests a player is directly trading long-term injury risk for short-term
        // selection convenience. Age adds a smaller, realistic aging-curve bump on top.
        // Recovery duration is a rough bell curve (two averaged Random.Range rolls),
        // matching the "bell curve not hard range" preference used everywhere else
        // stats/ages/heights are generated.
        private static InjuryEvent? TryRollInjury(ManagerSquadRoles roles, PlayerAgent player, float preMatchCondition, int currentDayNumber)
        {
            float fatigueRisk = Mathf.Clamp01((70f - preMatchCondition) / 70f);
            float ageRisk = Mathf.Clamp01((player.Age - 30f) / 15f);
            float injuryChance = 0.015f + (fatigueRisk * 0.09f) + (ageRisk * 0.02f);

            if (UnityEngine.Random.value >= injuryChance)
            {
                return null;
            }

            int durationWeeks = Mathf.Clamp(Mathf.RoundToInt((UnityEngine.Random.Range(1f, 6f) + UnityEngine.Random.Range(1f, 6f)) / 2f), 1, 8);
            int durationDays = durationWeeks * 7;
            roles.SetInjured(player, currentDayNumber + durationDays);
            return new InjuryEvent(player, durationWeeks);
        }
    }
}
