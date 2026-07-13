using System.Collections.Generic;
using UnityEngine;

namespace Sim
{
    public class AgentMatchSimulator
    {
        private enum ChanceType
        {
            ThroughBall,
            Cross,
            Dribble,
            LongShot,
            SetPiece,
            CounterAttack
        }

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
            return SimulateMatch(homeTeam, awayTeam, 1.45f, 1.20f);
        }

        public AgentMatchResult SimulateMatch(
            AgentTeam homeTeam,
            AgentTeam awayTeam,
            float expectedHomeGoals,
            float expectedAwayGoals)
        {
            AgentMatchResult result = new AgentMatchResult
            {
                HomeTeamName = homeTeam.TeamName,
                AwayTeamName = awayTeam.TeamName,
                HomeGoals = 0,
                AwayGoals = 0
            };

            float totalExpectedGoals = Mathf.Max(0.1f, expectedHomeGoals + expectedAwayGoals);

            float eventChancePerMinute = Mathf.Clamp(
                0.18f + totalExpectedGoals * 0.035f,
                0.18f,
                0.32f
            );

            float rawHomeAttackChance = expectedHomeGoals / totalExpectedGoals;

            float homeAttackChance = Mathf.Clamp(
                Mathf.Lerp(0.52f, rawHomeAttackChance, 0.45f),
                0.35f,
                0.65f
            );

            for (int minute = 1; minute <= 90; minute++)
            {
                if (Random.value > eventChancePerMinute)
                {
                    continue;
                }

                bool homeAttacks = Random.value < homeAttackChance;

                int goalDifference = result.HomeGoals - result.AwayGoals;

                if (homeAttacks && goalDifference >= 3 && Random.value < 0.60f)
                {
                    continue;
                }

                if (!homeAttacks && goalDifference <= -3 && Random.value < 0.60f)
                {
                    continue;
                }

                AgentTeam attackingTeam = homeAttacks ? homeTeam : awayTeam;
                AgentTeam defendingTeam = homeAttacks ? awayTeam : homeTeam;

                float attackingExpectedGoals = homeAttacks
                    ? expectedHomeGoals
                    : expectedAwayGoals;

                ResolveAttack(
                    minute,
                    attackingTeam,
                    defendingTeam,
                    homeAttacks,
                    attackingExpectedGoals,
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
            float attackingExpectedGoals,
            AgentMatchResult result)
        {
            ChanceType chanceType = PickChanceType(attackingExpectedGoals);

            PlayerAgent creator = PickCreatorForChance(attackingTeam, chanceType);
            PlayerAgent shooter = PickShooterForChance(attackingTeam, chanceType);
            PlayerAgent defender = PickDefenderForChance(defendingTeam, chanceType);
            PlayerAgent goalkeeper = PickGoalkeeper(defendingTeam);

            if (creator == null || shooter == null || defender == null || goalkeeper == null)
            {
                return;
            }

            float chanceCreation = GetChanceCreationScore(creator, shooter, chanceType);
            float defensiveResistance = GetDefensiveResistanceScore(defender, goalkeeper, chanceType);

            float chanceScore = chanceCreation - defensiveResistance;

            float shotChance = Mathf.Clamp(
                0.42f + chanceScore / 240f,
                0.08f,
                0.78f
            );

            if (Random.value > shotChance)
            {
                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description = BuildStoppedEventText(
                        attackingTeam,
                        creator,
                        defender,
                        chanceType
                    )
                });

                return;
            }

            float goalQuality = GetGoalQualityScore(creator, shooter, chanceType);
            float saveQuality = GetSaveQualityScore(goalkeeper, defender, chanceType);

            float goalChance = Mathf.Clamp(
                0.302f + (goalQuality - saveQuality) / 320f,
                0.08f,
                0.63f
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
                    Description = BuildGoalEventText(
                        attackingTeam,
                        creator,
                        shooter,
                        chanceType
                    )
                });
            }
            else
            {
                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description = BuildSavedEventText(
                        attackingTeam,
                        shooter,
                        goalkeeper,
                        chanceType
                    )
                });
            }
        }

        private ChanceType PickChanceType(float attackingExpectedGoals)
        {
            float roll = Random.value;

            if (attackingExpectedGoals >= 2.0f)
            {
                if (roll < 0.28f) return ChanceType.ThroughBall;
                if (roll < 0.50f) return ChanceType.Cross;
                if (roll < 0.68f) return ChanceType.Dribble;
                if (roll < 0.80f) return ChanceType.CounterAttack;
                if (roll < 0.92f) return ChanceType.LongShot;
                return ChanceType.SetPiece;
            }

            if (attackingExpectedGoals <= 1.0f)
            {
                if (roll < 0.20f) return ChanceType.CounterAttack;
                if (roll < 0.38f) return ChanceType.Cross;
                if (roll < 0.55f) return ChanceType.SetPiece;
                if (roll < 0.72f) return ChanceType.LongShot;
                if (roll < 0.88f) return ChanceType.ThroughBall;
                return ChanceType.Dribble;
            }

            if (roll < 0.24f) return ChanceType.ThroughBall;
            if (roll < 0.45f) return ChanceType.Cross;
            if (roll < 0.62f) return ChanceType.Dribble;
            if (roll < 0.77f) return ChanceType.CounterAttack;
            if (roll < 0.90f) return ChanceType.LongShot;
            return ChanceType.SetPiece;
        }

        private float GetChanceCreationScore(
            PlayerAgent creator,
            PlayerAgent shooter,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.ThroughBall:
                    return
                        creator.Passing * 0.35f +
                        creator.Creativity * 0.35f +
                        shooter.Positioning * 0.20f +
                        shooter.Pace * 0.10f;

                case ChanceType.Cross:
                    return
                        creator.Crossing * 0.40f +
                        creator.Pace * 0.15f +
                        shooter.Heading * 0.25f +
                        shooter.Aerial * 0.20f;

                case ChanceType.Dribble:
                    return
                        creator.Dribbling * 0.40f +
                        creator.Pace * 0.25f +
                        creator.Creativity * 0.15f +
                        shooter.Finishing * 0.20f;

                case ChanceType.LongShot:
                    return
                        shooter.Finishing * 0.30f +
                        shooter.Composure * 0.30f +
                        creator.Passing * 0.20f +
                        creator.Creativity * 0.20f;

                case ChanceType.SetPiece:
                    return
                        creator.Crossing * 0.35f +
                        creator.Creativity * 0.15f +
                        shooter.Heading * 0.25f +
                        shooter.Aerial * 0.25f;

                case ChanceType.CounterAttack:
                    return
                        creator.Passing * 0.25f +
                        creator.Pace * 0.25f +
                        shooter.Pace * 0.25f +
                        shooter.Finishing * 0.25f;

                default:
                    return creator.Creativity * 0.6f + shooter.Finishing * 0.4f;
            }
        }

        private float GetDefensiveResistanceScore(
            PlayerAgent defender,
            PlayerAgent goalkeeper,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.ThroughBall:
                    return
                        defender.Positioning * 0.35f +
                        defender.Defending * 0.35f +
                        goalkeeper.Goalkeeping * 0.30f;

                case ChanceType.Cross:
                    return
                        defender.Aerial * 0.35f +
                        defender.Heading * 0.25f +
                        defender.Defending * 0.20f +
                        goalkeeper.Reflexes * 0.20f;

                case ChanceType.Dribble:
                    return
                        defender.Tackling * 0.40f +
                        defender.Pace * 0.25f +
                        defender.Defending * 0.20f +
                        goalkeeper.Goalkeeping * 0.15f;

                case ChanceType.LongShot:
                    return
                        defender.Defending * 0.20f +
                        goalkeeper.Positioning * 0.35f +
                        goalkeeper.Reflexes * 0.45f;

                case ChanceType.SetPiece:
                    return
                        defender.Aerial * 0.35f +
                        defender.Heading * 0.25f +
                        goalkeeper.Reflexes * 0.25f +
                        goalkeeper.Goalkeeping * 0.15f;

                case ChanceType.CounterAttack:
                    return
                        defender.Pace * 0.30f +
                        defender.Tackling * 0.25f +
                        defender.Positioning * 0.25f +
                        goalkeeper.Reflexes * 0.20f;

                default:
                    return defender.Defending * 0.6f + goalkeeper.Goalkeeping * 0.4f;
            }
        }

        private float GetGoalQualityScore(
            PlayerAgent creator,
            PlayerAgent shooter,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.Cross:
                case ChanceType.SetPiece:
                    return
                        shooter.Heading * 0.35f +
                        shooter.Aerial * 0.25f +
                        shooter.Composure * 0.20f +
                        creator.Crossing * 0.20f;

                case ChanceType.LongShot:
                    return
                        shooter.Finishing * 0.40f +
                        shooter.Composure * 0.35f +
                        creator.Creativity * 0.25f;

                case ChanceType.Dribble:
                    return
                        shooter.Finishing * 0.35f +
                        shooter.Dribbling * 0.25f +
                        shooter.Composure * 0.25f +
                        shooter.Pace * 0.15f;

                case ChanceType.CounterAttack:
                    return
                        shooter.Finishing * 0.40f +
                        shooter.Pace * 0.20f +
                        shooter.Composure * 0.25f +
                        creator.Passing * 0.15f;

                case ChanceType.ThroughBall:
                default:
                    return
                        shooter.Finishing * 0.45f +
                        shooter.Positioning * 0.20f +
                        shooter.Composure * 0.20f +
                        creator.Creativity * 0.15f;
            }
        }

        private float GetSaveQualityScore(
            PlayerAgent goalkeeper,
            PlayerAgent defender,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.Cross:
                case ChanceType.SetPiece:
                    return
                        goalkeeper.Reflexes * 0.35f +
                        goalkeeper.Goalkeeping * 0.30f +
                        defender.Aerial * 0.20f +
                        defender.Positioning * 0.15f;

                case ChanceType.LongShot:
                    return
                        goalkeeper.Reflexes * 0.45f +
                        goalkeeper.Positioning * 0.35f +
                        goalkeeper.Goalkeeping * 0.20f;

                default:
                    return
                        goalkeeper.Goalkeeping * 0.45f +
                        goalkeeper.Reflexes * 0.35f +
                        defender.Defending * 0.20f;
            }
        }

        private PlayerAgent PickCreatorForChance(AgentTeam team, ChanceType chanceType)
        {
            List<PlayerAgent> candidates;

            switch (chanceType)
            {
                case ChanceType.Cross:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.RM ||
                        p.PrimaryPosition == PlayerPosition.LM ||
                        p.PrimaryPosition == PlayerPosition.RB ||
                        p.PrimaryPosition == PlayerPosition.LB ||
                        p.PrimaryPosition == PlayerPosition.RWB ||
                        p.PrimaryPosition == PlayerPosition.LWB
                    );
                    return PickWeightedByAttribute(candidates, p => p.Crossing + p.Pace * 0.3f);

                case ChanceType.ThroughBall:
                case ChanceType.LongShot:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CM ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.DM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW
                    );
                    return PickWeightedByAttribute(candidates, p => p.Passing + p.Creativity);

                case ChanceType.Dribble:
                case ChanceType.CounterAttack:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.ST
                    );
                    return PickWeightedByAttribute(candidates, p => p.Dribbling + p.Pace);

                case ChanceType.SetPiece:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CM ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.RB ||
                        p.PrimaryPosition == PlayerPosition.LB
                    );
                    return PickWeightedByAttribute(candidates, p => p.Crossing + p.Creativity);

                default:
                    return PickCreativePlayerFallback(team);
            }
        }

        private PlayerAgent PickShooterForChance(AgentTeam team, ChanceType chanceType)
        {
            List<PlayerAgent> candidates;

            switch (chanceType)
            {
                case ChanceType.Cross:
                case ChanceType.SetPiece:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.ST ||
                        p.PrimaryPosition == PlayerPosition.CB ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW
                    );
                    return PickWeightedByAttribute(candidates, p => p.Heading + p.Aerial + p.Finishing * 0.5f);

                case ChanceType.LongShot:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CM ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.ST
                    );
                    return PickWeightedByAttribute(candidates, p => p.Finishing + p.Composure);

                case ChanceType.CounterAttack:
                case ChanceType.ThroughBall:
                case ChanceType.Dribble:
                default:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.ST ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.AM
                    );
                    return PickWeightedByAttribute(candidates, p => p.Finishing + p.Positioning + p.Pace * 0.3f);
            }
        }

        private PlayerAgent PickDefenderForChance(AgentTeam team, ChanceType chanceType)
        {
            List<PlayerAgent> candidates;

            switch (chanceType)
            {
                case ChanceType.Cross:
                case ChanceType.SetPiece:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CB ||
                        p.PrimaryPosition == PlayerPosition.DM ||
                        p.PrimaryPosition == PlayerPosition.RB ||
                        p.PrimaryPosition == PlayerPosition.LB
                    );
                    return PickWeightedByAttribute(candidates, p => p.Aerial + p.Heading + p.Defending);

                case ChanceType.Dribble:
                case ChanceType.CounterAttack:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CB ||
                        p.PrimaryPosition == PlayerPosition.RB ||
                        p.PrimaryPosition == PlayerPosition.LB ||
                        p.PrimaryPosition == PlayerPosition.RWB ||
                        p.PrimaryPosition == PlayerPosition.LWB ||
                        p.PrimaryPosition == PlayerPosition.DM
                    );
                    return PickWeightedByAttribute(candidates, p => p.Tackling + p.Pace + p.Defending);

                case ChanceType.ThroughBall:
                case ChanceType.LongShot:
                default:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CB ||
                        p.PrimaryPosition == PlayerPosition.DM ||
                        p.PrimaryPosition == PlayerPosition.CM
                    );
                    return PickWeightedByAttribute(candidates, p => p.Positioning + p.Defending + p.Tackling);
            }
        }

        private PlayerAgent PickGoalkeeper(AgentTeam team)
        {
            PlayerAgent goalkeeper = team.StartingEleven.Find(p =>
                p.PrimaryPosition == PlayerPosition.GK ||
                p.Role == PlayerRole.Goalkeeper
            );

            if (goalkeeper != null)
            {
                return goalkeeper;
            }

            return team.StartingEleven[0];
        }

        private PlayerAgent PickCreativePlayerFallback(AgentTeam team)
        {
            List<PlayerAgent> candidates = team.StartingEleven.FindAll(p =>
                p.Role == PlayerRole.Midfielder ||
                p.Role == PlayerRole.Forward
            );

            return PickWeightedByAttribute(candidates, p => p.Creativity);
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

        private string BuildStoppedEventText(
            AgentTeam attackingTeam,
            PlayerAgent creator,
            PlayerAgent defender,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.Cross:
                    return $"{attackingTeam.TeamName} look for a cross from {creator.Name}, but {defender.Name} clears it.";

                case ChanceType.ThroughBall:
                    return $"{creator.Name} tries to slip a through ball in for {attackingTeam.TeamName}, but {defender.Name} reads it.";

                case ChanceType.Dribble:
                    return $"{creator.Name} drives forward for {attackingTeam.TeamName}, but {defender.Name} wins the duel.";

                case ChanceType.LongShot:
                    return $"{attackingTeam.TeamName} work space outside the box through {creator.Name}, but {defender.Name} closes it down.";

                case ChanceType.SetPiece:
                    return $"{attackingTeam.TeamName} deliver a set piece through {creator.Name}, but {defender.Name} deals with it.";

                case ChanceType.CounterAttack:
                    return $"{attackingTeam.TeamName} break quickly through {creator.Name}, but {defender.Name} stops the counter.";

                default:
                    return $"{attackingTeam.TeamName} build an attack through {creator.Name}, but {defender.Name} stops the chance.";
            }
        }

        private string BuildGoalEventText(
            AgentTeam attackingTeam,
            PlayerAgent creator,
            PlayerAgent shooter,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.Cross:
                    return $"{attackingTeam.TeamName} goal! {creator.Name} whips in the cross and {shooter.Name} finishes.";

                case ChanceType.ThroughBall:
                    return $"{attackingTeam.TeamName} goal! {creator.Name} splits the defence and {shooter.Name} slots it away.";

                case ChanceType.Dribble:
                    return $"{attackingTeam.TeamName} goal! {shooter.Name} beats his marker after work from {creator.Name} and scores.";

                case ChanceType.LongShot:
                    return $"{attackingTeam.TeamName} goal! {shooter.Name} finds space and fires in from range.";

                case ChanceType.SetPiece:
                    return $"{attackingTeam.TeamName} goal! {creator.Name} delivers the set piece and {shooter.Name} converts.";

                case ChanceType.CounterAttack:
                    return $"{attackingTeam.TeamName} goal! {creator.Name} launches the counter and {shooter.Name} finishes it.";

                default:
                    return $"{attackingTeam.TeamName} goal! {creator.Name} creates the chance and {shooter.Name} finishes.";
            }
        }

        private string BuildSavedEventText(
            AgentTeam attackingTeam,
            PlayerAgent shooter,
            PlayerAgent goalkeeper,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.Cross:
                    return $"{attackingTeam.TeamName} chance. {shooter.Name} meets the cross, but {goalkeeper.Name} saves.";

                case ChanceType.ThroughBall:
                    return $"{attackingTeam.TeamName} chance. {shooter.Name} gets in behind, but {goalkeeper.Name} saves.";

                case ChanceType.Dribble:
                    return $"{attackingTeam.TeamName} chance. {shooter.Name} shoots after a dribble, but {goalkeeper.Name} saves.";

                case ChanceType.LongShot:
                    return $"{attackingTeam.TeamName} chance. {shooter.Name} shoots from distance, but {goalkeeper.Name} saves.";

                case ChanceType.SetPiece:
                    return $"{attackingTeam.TeamName} chance from the set piece. {shooter.Name} gets the effort away, but {goalkeeper.Name} saves.";

                case ChanceType.CounterAttack:
                    return $"{attackingTeam.TeamName} chance on the counter. {shooter.Name} shoots, but {goalkeeper.Name} saves.";

                default:
                    return $"{attackingTeam.TeamName} chance. {shooter.Name} shoots, but {goalkeeper.Name} saves.";
            }
        }
    }
}