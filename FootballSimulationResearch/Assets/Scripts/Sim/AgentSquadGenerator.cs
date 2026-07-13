using System.Collections.Generic;
using UnityEngine;

namespace Sim
{
    public class AgentSquadGenerator
    {
        private readonly string[] firstNames =
        {
            "Daniel", "Luca", "Mateo", "Jonas", "Ethan", "Noah",
            "Oscar", "Leo", "Max", "Samuel", "Rafael", "Milan",
            "Tomas", "Nico", "Julian", "Felix", "Adam", "Lucas",
            "Marco", "David", "Emil", "Ben", "Kai", "Andre",
            "Hugo", "Victor", "Tiago", "Gabriel", "Leon", "Isaac"
        };

        private readonly string[] lastNames =
        {
            "Mercer", "Brandt", "Hughes", "Keller", "Costa", "Bennett",
            "Silva", "Meyer", "Rossi", "Santos", "Walker", "Fischer",
            "Davies", "Martins", "Schneider", "Moreira", "Wilson", "Weber",
            "Reed", "Mendes", "Hart", "Carvalho", "Bauer", "Cole",
            "Wright", "Oliveira", "Mason", "Rocha", "Foster", "Nolan"
        };

        public AgentTeam GenerateSquad(
            string teamName,
            float attackStrength,
            float defenceStrength)
        {
            Formation formation = GetDefaultFormationForTeam(teamName);

            AgentTeam team = new AgentTeam(teamName, formation);

            HashSet<string> usedNames = new();

            List<PlayerPosition> startingPositions = GetStartingPositions(formation);

            foreach (PlayerPosition position in startingPositions)
            {
                PlayerAgent player = GeneratePlayer(
                    GenerateUniqueName(usedNames),
                    position,
                    attackStrength,
                    defenceStrength
                );

                team.AddStarter(player);
            }

            foreach (PlayerPosition position in GetBenchPositions(formation))
            {
                PlayerAgent player = GeneratePlayer(
                    GenerateUniqueName(usedNames),
                    position,
                    attackStrength,
                    defenceStrength
                );

                AddSecondaryPositions(player);
                team.AddBenchPlayer(player);
            }

            return team;
        }

        private Formation GetDefaultFormationForTeam(string teamName)
        {
            switch (teamName)
            {
                case "Manchester City":
                case "Liverpool":
                case "Arsenal":
                case "Brighton & Hove Albion":
                case "AFC Bournemouth":
                    return Formation.FourThreeThree;

                case "Chelsea":
                case "Manchester United":
                case "Tottenham Hotspur":
                case "Newcastle United":
                case "Aston Villa":
                case "West Ham United":
                case "Crystal Palace":
                    return Formation.FourTwoThreeOne;

                case "Everton":
                case "Burnley":
                case "Brentford":
                    return Formation.FourFourTwo;

                case "Nottingham Forest":
                case "Wolverhampton Wanderers":
                    return Formation.ThreeFiveTwo;

                default:
                    return Formation.FourTwoThreeOne;
            }
        }

        private List<PlayerPosition> GetStartingPositions(Formation formation)
        {
            switch (formation)
            {
                case Formation.FourThreeThree:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.RB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.LB,
                        PlayerPosition.DM,
                        PlayerPosition.CM,
                        PlayerPosition.CM,
                        PlayerPosition.RW,
                        PlayerPosition.ST,
                        PlayerPosition.LW
                    };

                case Formation.FourTwoThreeOne:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.RB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.LB,
                        PlayerPosition.DM,
                        PlayerPosition.DM,
                        PlayerPosition.RW,
                        PlayerPosition.AM,
                        PlayerPosition.LW,
                        PlayerPosition.ST
                    };

                case Formation.FourFourTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.RB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.LB,
                        PlayerPosition.RM,
                        PlayerPosition.CM,
                        PlayerPosition.CM,
                        PlayerPosition.LM,
                        PlayerPosition.ST,
                        PlayerPosition.ST
                    };

                case Formation.ThreeFiveTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.RWB,
                        PlayerPosition.CM,
                        PlayerPosition.DM,
                        PlayerPosition.CM,
                        PlayerPosition.LWB,
                        PlayerPosition.ST,
                        PlayerPosition.ST
                    };

                case Formation.ThreeFourThree:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.RM,
                        PlayerPosition.CM,
                        PlayerPosition.CM,
                        PlayerPosition.LM,
                        PlayerPosition.RW,
                        PlayerPosition.ST,
                        PlayerPosition.LW
                    };

                default:
                    return GetStartingPositions(Formation.FourTwoThreeOne);
            }
        }

        private List<PlayerPosition> GetBenchPositions(Formation formation)
        {
            // Seven-player bench: backup keeper, defensive cover, midfield cover, attacking cover.
            switch (formation)
            {
                case Formation.FourFourTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.RB,
                        PlayerPosition.CM,
                        PlayerPosition.LM,
                        PlayerPosition.RM,
                        PlayerPosition.ST
                    };

                case Formation.ThreeFiveTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.RWB,
                        PlayerPosition.LWB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.ST
                    };
                case Formation.ThreeFourTwoOne:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.RWB,
                        PlayerPosition.LWB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.ST
                    };

                default:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.LB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.RW,
                        PlayerPosition.ST
                    };
            }
        }

        private PlayerAgent GeneratePlayer(
            string playerName,
            PlayerPosition position,
            float attackStrength,
            float defenceStrength)
        {
            PlayerRole role = GetRoleFromPosition(position);

            PlayerAgent player = new PlayerAgent(playerName, role, position);

            float attackMultiplier = Mathf.Lerp(1f, attackStrength, 0.35f);
            float defenceMultiplier = Mathf.Lerp(1f, 1f / defenceStrength, 0.35f);

            ApplyBaseAttributes(player);

            switch (position)
            {
                case PlayerPosition.GK:
                    GenerateGoalkeeper(player, defenceMultiplier);
                    break;

                case PlayerPosition.CB:
                    GenerateCentreBack(player, defenceMultiplier);
                    break;

                case PlayerPosition.RB:
                case PlayerPosition.LB:
                    GenerateFullBack(player, attackMultiplier, defenceMultiplier);
                    break;

                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                    GenerateWingBack(player, attackMultiplier, defenceMultiplier);
                    break;

                case PlayerPosition.DM:
                    GenerateDefensiveMidfielder(player, attackMultiplier, defenceMultiplier);
                    break;

                case PlayerPosition.CM:
                    GenerateCentralMidfielder(player, attackMultiplier, defenceMultiplier);
                    break;

                case PlayerPosition.AM:
                    GenerateAttackingMidfielder(player, attackMultiplier);
                    break;

                case PlayerPosition.RM:
                case PlayerPosition.LM:
                    GenerateWideMidfielder(player, attackMultiplier, defenceMultiplier);
                    break;

                case PlayerPosition.RW:
                case PlayerPosition.LW:
                    GenerateWinger(player, attackMultiplier);
                    break;

                case PlayerPosition.ST:
                    GenerateStriker(player, attackMultiplier);
                    break;
            }

            ClampAttributes(player);
            AddSecondaryPositions(player);

            return player;
        }

        private void ApplyBaseAttributes(PlayerAgent player)
        {
            player.Finishing = Random.Range(35f, 60f);
            player.Passing = Random.Range(35f, 60f);
            player.Dribbling = Random.Range(35f, 60f);
            player.Crossing = Random.Range(35f, 60f);
            player.Heading = Random.Range(35f, 60f);

            player.Creativity = Random.Range(35f, 60f);
            player.Positioning = Random.Range(35f, 60f);
            player.Composure = Random.Range(35f, 60f);

            player.Defending = Random.Range(35f, 60f);
            player.Tackling = Random.Range(35f, 60f);

            player.Pace = Random.Range(45f, 75f);
            player.Strength = Random.Range(45f, 75f);
            player.Stamina = Random.Range(55f, 85f);
            player.Aerial = Random.Range(35f, 65f);

            player.Goalkeeping = Random.Range(1f, 10f);
            player.Reflexes = Random.Range(1f, 10f);

            player.WeakFoot = Random.Range(35f, 85f);
        }

        private void GenerateGoalkeeper(PlayerAgent player, float defenceMultiplier)
        {
            player.Goalkeeping = Random.Range(65f, 88f) * defenceMultiplier;
            player.Reflexes = Random.Range(65f, 90f) * defenceMultiplier;
            player.Positioning = Random.Range(55f, 80f) * defenceMultiplier;
            player.Passing = Random.Range(35f, 70f);
            player.Composure = Random.Range(50f, 80f);

            player.Finishing = Random.Range(1f, 8f);
            player.Dribbling = Random.Range(5f, 20f);
            player.Crossing = Random.Range(1f, 10f);
            player.Heading = Random.Range(5f, 20f);
            player.Defending = Random.Range(15f, 35f);
            player.Tackling = Random.Range(10f, 30f);
        }

        private void GenerateCentreBack(PlayerAgent player, float defenceMultiplier)
        {
            player.Defending = Random.Range(60f, 85f) * defenceMultiplier;
            player.Tackling = Random.Range(60f, 85f) * defenceMultiplier;
            player.Heading = Random.Range(60f, 85f) * defenceMultiplier;
            player.Aerial = Random.Range(65f, 90f) * defenceMultiplier;
            player.Strength = Random.Range(65f, 90f);
            player.Positioning = Random.Range(55f, 80f) * defenceMultiplier;
            player.Passing = Random.Range(35f, 65f);

            player.Finishing = Random.Range(5f, 25f);
            player.Dribbling = Random.Range(20f, 50f);
            player.Crossing = Random.Range(10f, 35f);
        }

        private void GenerateFullBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = Random.Range(50f, 75f) * defenceMultiplier;
            player.Tackling = Random.Range(50f, 75f) * defenceMultiplier;
            player.Crossing = Random.Range(50f, 78f) * attackMultiplier;
            player.Pace = Random.Range(60f, 88f);
            player.Stamina = Random.Range(65f, 90f);
            player.Passing = Random.Range(45f, 70f);
            player.Dribbling = Random.Range(45f, 72f);

            player.Finishing = Random.Range(10f, 35f);
            player.Heading = Random.Range(35f, 65f);
            player.Aerial = Random.Range(35f, 65f);
        }

        private void GenerateWingBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = Random.Range(45f, 70f) * defenceMultiplier;
            player.Tackling = Random.Range(45f, 72f) * defenceMultiplier;
            player.Crossing = Random.Range(55f, 82f) * attackMultiplier;
            player.Pace = Random.Range(65f, 90f);
            player.Stamina = Random.Range(70f, 92f);
            player.Dribbling = Random.Range(50f, 76f) * attackMultiplier;
            player.Passing = Random.Range(45f, 70f);

            player.Finishing = Random.Range(12f, 38f);
        }

        private void GenerateDefensiveMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = Random.Range(55f, 80f) * defenceMultiplier;
            player.Tackling = Random.Range(55f, 82f) * defenceMultiplier;
            player.Passing = Random.Range(55f, 80f) * attackMultiplier;
            player.Positioning = Random.Range(55f, 82f) * defenceMultiplier;
            player.Strength = Random.Range(55f, 80f);
            player.Stamina = Random.Range(65f, 90f);
            player.Creativity = Random.Range(40f, 70f) * attackMultiplier;

            player.Finishing = Random.Range(15f, 40f);
            player.Dribbling = Random.Range(35f, 65f);
            player.Heading = Random.Range(40f, 70f);
            player.Aerial = Random.Range(40f, 70f);
        }

        private void GenerateCentralMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Passing = Random.Range(60f, 85f) * attackMultiplier;
            player.Creativity = Random.Range(55f, 82f) * attackMultiplier;
            player.Positioning = Random.Range(50f, 78f);
            player.Composure = Random.Range(55f, 82f);
            player.Stamina = Random.Range(65f, 90f);
            player.Defending = Random.Range(40f, 70f) * defenceMultiplier;
            player.Tackling = Random.Range(40f, 70f) * defenceMultiplier;
            player.Dribbling = Random.Range(45f, 75f) * attackMultiplier;

            player.Finishing = Random.Range(25f, 55f) * attackMultiplier;
        }

        private void GenerateAttackingMidfielder(PlayerAgent player, float attackMultiplier)
        {
            player.Passing = Random.Range(60f, 85f) * attackMultiplier;
            player.Creativity = Random.Range(65f, 90f) * attackMultiplier;
            player.Dribbling = Random.Range(60f, 88f) * attackMultiplier;
            player.Composure = Random.Range(55f, 85f);
            player.Finishing = Random.Range(40f, 70f) * attackMultiplier;
            player.Positioning = Random.Range(50f, 78f);

            player.Defending = Random.Range(15f, 45f);
            player.Tackling = Random.Range(15f, 45f);
            player.Heading = Random.Range(25f, 55f);
            player.Aerial = Random.Range(25f, 55f);
        }

        private void GenerateWideMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Crossing = Random.Range(58f, 84f) * attackMultiplier;
            player.Dribbling = Random.Range(55f, 82f) * attackMultiplier;
            player.Pace = Random.Range(60f, 88f);
            player.Stamina = Random.Range(65f, 90f);
            player.Passing = Random.Range(48f, 74f) * attackMultiplier;
            player.Defending = Random.Range(35f, 65f) * defenceMultiplier;
            player.Tackling = Random.Range(35f, 65f) * defenceMultiplier;

            player.Finishing = Random.Range(25f, 55f) * attackMultiplier;
        }

        private void GenerateWinger(PlayerAgent player, float attackMultiplier)
        {
            player.Pace = Random.Range(68f, 94f);
            player.Dribbling = Random.Range(62f, 90f) * attackMultiplier;
            player.Crossing = Random.Range(55f, 84f) * attackMultiplier;
            player.Creativity = Random.Range(50f, 78f) * attackMultiplier;
            player.Passing = Random.Range(45f, 72f) * attackMultiplier;
            player.Finishing = Random.Range(35f, 68f) * attackMultiplier;
            player.Composure = Random.Range(45f, 75f);

            player.Defending = Random.Range(10f, 35f);
            player.Tackling = Random.Range(10f, 35f);
            player.Heading = Random.Range(20f, 55f);
            player.Aerial = Random.Range(20f, 55f);
        }

        private void GenerateStriker(PlayerAgent player, float attackMultiplier)
        {
            player.Finishing = Random.Range(62f, 90f) * attackMultiplier;
            player.Positioning = Random.Range(60f, 88f) * attackMultiplier;
            player.Composure = Random.Range(58f, 88f) * attackMultiplier;
            player.Heading = Random.Range(45f, 82f) * attackMultiplier;
            player.Aerial = Random.Range(45f, 82f) * attackMultiplier;
            player.Strength = Random.Range(45f, 82f);
            player.Pace = Random.Range(50f, 88f);
            player.Dribbling = Random.Range(40f, 75f) * attackMultiplier;

            player.Passing = Random.Range(30f, 60f);
            player.Creativity = Random.Range(25f, 60f);
            player.Defending = Random.Range(5f, 25f);
            player.Tackling = Random.Range(5f, 25f);
        }

        private void AddSecondaryPositions(PlayerAgent player)
        {
            player.SecondaryPositions.Clear();

            switch (player.PrimaryPosition)
            {
                case PlayerPosition.RB:
                    MaybeAdd(player, PlayerPosition.RWB, 0.65f);
                    MaybeAdd(player, PlayerPosition.CB, 0.20f);
                    break;

                case PlayerPosition.LB:
                    MaybeAdd(player, PlayerPosition.LWB, 0.65f);
                    MaybeAdd(player, PlayerPosition.CB, 0.20f);
                    break;

                case PlayerPosition.RWB:
                    MaybeAdd(player, PlayerPosition.RB, 0.75f);
                    MaybeAdd(player, PlayerPosition.RM, 0.45f);
                    break;

                case PlayerPosition.LWB:
                    MaybeAdd(player, PlayerPosition.LB, 0.75f);
                    MaybeAdd(player, PlayerPosition.LM, 0.45f);
                    break;

                case PlayerPosition.CB:
                    MaybeAdd(player, PlayerPosition.DM, 0.25f);
                    break;

                case PlayerPosition.DM:
                    MaybeAdd(player, PlayerPosition.CM, 0.70f);
                    MaybeAdd(player, PlayerPosition.CB, 0.25f);
                    break;

                case PlayerPosition.CM:
                    MaybeAdd(player, PlayerPosition.DM, 0.40f);
                    MaybeAdd(player, PlayerPosition.AM, 0.35f);
                    break;

                case PlayerPosition.AM:
                    MaybeAdd(player, PlayerPosition.CM, 0.55f);
                    MaybeAdd(player, PlayerPosition.RW, 0.30f);
                    MaybeAdd(player, PlayerPosition.LW, 0.30f);
                    break;

                case PlayerPosition.RM:
                    MaybeAdd(player, PlayerPosition.RW, 0.70f);
                    MaybeAdd(player, PlayerPosition.RB, 0.35f);
                    break;

                case PlayerPosition.LM:
                    MaybeAdd(player, PlayerPosition.LW, 0.70f);
                    MaybeAdd(player, PlayerPosition.LB, 0.35f);
                    break;

                case PlayerPosition.RW:
                    MaybeAdd(player, PlayerPosition.LW, 0.45f);
                    MaybeAdd(player, PlayerPosition.AM, 0.35f);
                    MaybeAdd(player, PlayerPosition.ST, 0.20f);
                    break;

                case PlayerPosition.LW:
                    MaybeAdd(player, PlayerPosition.RW, 0.45f);
                    MaybeAdd(player, PlayerPosition.AM, 0.35f);
                    MaybeAdd(player, PlayerPosition.ST, 0.20f);
                    break;

                case PlayerPosition.ST:
                    MaybeAdd(player, PlayerPosition.LW, 0.20f);
                    MaybeAdd(player, PlayerPosition.RW, 0.20f);
                    MaybeAdd(player, PlayerPosition.AM, 0.20f);
                    break;
            }
        }

        private void MaybeAdd(PlayerAgent player, PlayerPosition position, float chance)
        {
            if (Random.value < chance && !player.SecondaryPositions.Contains(position))
            {
                player.SecondaryPositions.Add(position);
            }
        }

        private PlayerRole GetRoleFromPosition(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK:
                    return PlayerRole.Goalkeeper;

                case PlayerPosition.RB:
                case PlayerPosition.CB:
                case PlayerPosition.LB:
                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                    return PlayerRole.Defender;

                case PlayerPosition.DM:
                case PlayerPosition.CM:
                case PlayerPosition.AM:
                case PlayerPosition.RM:
                case PlayerPosition.LM:
                    return PlayerRole.Midfielder;

                case PlayerPosition.RW:
                case PlayerPosition.LW:
                case PlayerPosition.ST:
                    return PlayerRole.Forward;

                default:
                    return PlayerRole.Midfielder;
            }
        }

        private string GenerateUniqueName(HashSet<string> usedNames)
        {
            for (int i = 0; i < 100; i++)
            {
                string firstName = firstNames[Random.Range(0, firstNames.Length)];
                string lastName = lastNames[Random.Range(0, lastNames.Length)];

                string fullName = $"{firstName} {lastName}";

                if (!usedNames.Contains(fullName))
                {
                    usedNames.Add(fullName);
                    return fullName;
                }
            }

            string fallback = $"Player {usedNames.Count + 1}";
            usedNames.Add(fallback);
            return fallback;
        }

        private void ClampAttributes(PlayerAgent player)
        {
            player.Finishing = Clamp(player.Finishing);
            player.Passing = Clamp(player.Passing);
            player.Dribbling = Clamp(player.Dribbling);
            player.Crossing = Clamp(player.Crossing);
            player.Heading = Clamp(player.Heading);

            player.Creativity = Clamp(player.Creativity);
            player.Positioning = Clamp(player.Positioning);
            player.Composure = Clamp(player.Composure);

            player.Defending = Clamp(player.Defending);
            player.Tackling = Clamp(player.Tackling);

            player.Pace = Clamp(player.Pace);
            player.Strength = Clamp(player.Strength);
            player.Stamina = Clamp(player.Stamina);
            player.Aerial = Clamp(player.Aerial);

            player.Goalkeeping = Clamp(player.Goalkeeping);
            player.Reflexes = Clamp(player.Reflexes);

            player.WeakFoot = Clamp(player.WeakFoot);
        }

        private float Clamp(float value)
        {
            return Mathf.Clamp(value, 1f, 100f);
        }
    }
}