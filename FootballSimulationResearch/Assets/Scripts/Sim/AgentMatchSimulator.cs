using System.Collections.Generic;
using UnityEngine;

namespace Sim
{
    public class AgentMatchSimulator
    {
        public class AgentMatchEvent
        {
            public int Minute;
            public string Description;
        }

        public class AgentMatchResult
        {
            public string HomeTeamName;
            public string AwayTeamName;
            public int HomeGoals;
            public int AwayGoals;
            public List<AgentMatchEvent> Events = new();
        }

        public AgentMatchResult SimulateMatch(AgentTeam homeTeam, AgentTeam awayTeam)
        {
            AgentMatchResult result = new AgentMatchResult
            {
                HomeTeamName = homeTeam.TeamName,
                AwayTeamName = awayTeam.TeamName,
                HomeGoals = 0,
                AwayGoals = 0
            };

            for (int minute = 1; minute <= 90; minute++)
            {
                // Not every minute has a meaningful attacking event.
                if (Random.value > 0.18f)
                {
                    continue;
                }

                bool homeAttacks = Random.value < 0.52f;

                AgentTeam attackingTeam = homeAttacks ? homeTeam : awayTeam;
                AgentTeam defendingTeam = homeAttacks ? awayTeam : homeTeam;

                ResolveAttack(
                    minute,
                    attackingTeam,
                    defendingTeam,
                    homeAttacks,
                    result
                );
            }

            return result;
        }

        private void ResolveAttack(
            int minute,
            AgentTeam attackingTeam,
            AgentTeam defendingTeam,
            bool homeAttacks,
            AgentMatchResult result)
        {
            PlayerAgent creator = PickCreativePlayer(attackingTeam);
            PlayerAgent shooter = PickShooter(attackingTeam);
            PlayerAgent defender = PickDefender(defendingTeam);
            PlayerAgent goalkeeper = PickGoalkeeper(defendingTeam);

            float chanceCreation =
                creator.Creativity * 0.6f +
                shooter.Finishing * 0.4f;

            float defensiveResistance =
                defender.Defending * 0.6f +
                goalkeeper.Goalkeeping * 0.4f;

            float chanceScore =
                chanceCreation - defensiveResistance;

            float shotChance = Mathf.Clamp(0.45f + chanceScore / 200f, 0.05f, 0.85f);

            if (Random.value > shotChance)
            {
                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description =
                        $"{attackingTeam.TeamName} build an attack through {creator.Name}, " +
                        $"but {defender.Name} stops the chance."
                });

                return;
            }

            float goalChance = Mathf.Clamp(
                0.20f + (shooter.Finishing - goalkeeper.Goalkeeping) / 250f,
                0.03f,
                0.65f
            );

            if (Random.value < goalChance)
            {
                if (homeAttacks)
                {
                    result.HomeGoals++;
                }
                else
                {
                    result.AwayGoals++;
                }

                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description =
                        $"{attackingTeam.TeamName} goal! {creator.Name} creates the chance " +
                        $"and {shooter.Name} finishes."
                });
            }
            else
            {
                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description =
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots, " +
                        $"but {goalkeeper.Name} saves."
                });
            }
        }

        private PlayerAgent PickCreativePlayer(AgentTeam team)
        {
            List<PlayerAgent> candidates = team.Players.FindAll(p =>
                p.Role == PlayerRole.Midfielder ||
                p.Role == PlayerRole.Forward
            );

            return PickWeightedByAttribute(candidates, p => p.Creativity);
        }

        private PlayerAgent PickShooter(AgentTeam team)
        {
            List<PlayerAgent> candidates = team.Players.FindAll(p =>
                p.Role == PlayerRole.Forward ||
                p.Role == PlayerRole.Midfielder
            );

            return PickWeightedByAttribute(candidates, p => p.Finishing);
        }

        private PlayerAgent PickDefender(AgentTeam team)
        {
            List<PlayerAgent> candidates = team.Players.FindAll(p =>
                p.Role == PlayerRole.Defender ||
                p.Role == PlayerRole.Midfielder
            );

            return PickWeightedByAttribute(candidates, p => p.Defending);
        }

        private PlayerAgent PickGoalkeeper(AgentTeam team)
        {
            PlayerAgent goalkeeper = team.Players.Find(p => p.Role == PlayerRole.Goalkeeper);

            if (goalkeeper != null)
            {
                return goalkeeper;
            }

            return team.Players[0];
        }

        private PlayerAgent PickWeightedByAttribute(
            List<PlayerAgent> players,
            System.Func<PlayerAgent, float> attributeSelector)
        {
            if (players == null || players.Count == 0)
            {
                return null;
            }

            float totalWeight = 0f;

            foreach (PlayerAgent player in players)
            {
                totalWeight += Mathf.Max(1f, attributeSelector(player));
            }

            float roll = Random.Range(0f, totalWeight);
            float runningTotal = 0f;

            foreach (PlayerAgent player in players)
            {
                runningTotal += Mathf.Max(1f, attributeSelector(player));

                if (roll <= runningTotal)
                {
                    return player;
                }
            }

            return players[players.Count - 1];
        }
    }
}