using System.Collections.Generic;
using UnityEngine;

namespace Sim
{
    public class AgentSquadGenerator
    {
        // Expanded from an original 30 to cut down on birthday-paradox collisions across
        // a ~380-player league (900 combos was producing ~80 expected duplicate names),
        // and to spread the pool across more of the regions real top-flight squads draw
        // from rather than skewing Western European. Kept to common, non-celebrity given
        // names/surnames throughout - deliberately avoided pairing any name with a
        // surname distinctive enough to reproduce one specific real, currently-active
        // player's full name by chance.
        private readonly string[] firstNames =
        {
            "Daniel", "Luca", "Mateo", "Jonas", "Ethan", "Noah",
            "Oscar", "Leo", "Max", "Samuel", "Rafael", "Milan",
            "Tomas", "Nico", "Julian", "Felix", "Adam", "Lucas",
            "Marco", "David", "Emil", "Ben", "Kai", "Andre",
            "Hugo", "Victor", "Tiago", "Gabriel", "Leon", "Isaac",
            "Mohamed", "Yusuf", "Ibrahim", "Omar", "Hakim", "Malik",
            "Karim", "Idris", "Amara", "Kwame", "Kofi", "Sekou",
            "Moussa", "Chidi", "Bukola", "Aleksandar", "Dusan", "Nemanja",
            "Stefan", "Filip", "Jakub", "Piotr", "Wojciech", "Kacper",
            "Martin", "Lars", "Bjorn", "Sven", "Henrik", "Rasmus",
            "Mikkel", "Andrei", "Radu", "Kenji", "Haruto", "Ren",
            "Minjun", "Jin", "Wei", "Arjun", "Rohan", "Dev",
            "Ravi", "Santiago", "Diego", "Pablo", "Alejandro", "Enzo",
            "Mathis", "Antoine", "Theo", "Nathan", "Yannick", "Cedric",
            "Sean", "Ryan", "Connor", "Finn", "Declan", "Conor"
        };

        // Doubled from 81 to ~183 (2026-08-09, session 6) - with ~20 players/squad, 81
        // names meant a shared surname within one squad was the *expected* outcome
        // (birthday-paradox math: ~90% of squads had at least one collision) rather than
        // occasional coincidence. Full names still can't collide league-wide (see
        // usedNames below) - this just makes surname-only collisions genuinely rare
        // instead of near-certain, without needing an impractically large list (getting
        // collisions down to ~20% within one squad alone would need ~800+ names).
        private readonly string[] lastNames =
        {
            "Mercer", "Brandt", "Hughes", "Keller", "Costa", "Bennett",
            "Silva", "Meyer", "Rossi", "Santos", "Walker", "Fischer",
            "Davies", "Martins", "Schneider", "Moreira", "Wilson", "Weber",
            "Reed", "Mendes", "Hart", "Carvalho", "Bauer", "Cole",
            "Wright", "Oliveira", "Mason", "Rocha", "Foster", "Nolan",
            "Adebayo", "Okafor", "Diallo", "Toure", "Traore", "Fofana",
            "Konate", "Camara", "Boateng", "Osei", "Mensah", "Nwosu",
            "Kovacic", "Petrovic", "Jovic", "Nowak", "Kowalski", "Wozniak",
            "Zielinski", "Nilsson", "Andersson", "Johansson", "Larsen", "Hansen",
            "Nielsen", "Sorensen", "Popescu", "Ionescu", "Georgescu", "Tanaka",
            "Sato", "Suzuki", "Yamamoto", "Kim", "Park", "Choi",
            "Sharma", "Patel", "Singh", "Kumar", "Fernandez", "Gonzalez",
            "Ramirez", "Herrera", "Dubois", "Lefevre", "Girard", "Moreau",
            "Laurent", "Alonso", "Navarro",
            "Baker", "Turner", "Palmer", "Hayes", "Marsh", "Pearce",
            "Stevens", "Grant", "Doyle", "Barrett",
            "Pereira", "Almeida", "Correia", "Barbosa", "Teixeira", "Machado",
            "Cardoso", "Vieira",
            "Romano", "Ferrari", "Bianchi", "Marino", "Greco", "Conti",
            "Barone", "Villa",
            "Bernard", "Petit", "Fontaine", "Renard", "Marchand", "Lambert",
            "Roy", "Blanc",
            "Schulz", "Wagner", "Becker", "Hoffmann", "Krause", "Richter",
            "Vogel", "Zimmer",
            "Berg", "Lund", "Holm", "Dahl", "Eriksen", "Karlsson",
            "Bergstrom", "Lindqvist",
            "Wojcik", "Kaminski", "Lewandowski", "Szymanski", "Dabrowski", "Kozlowski",
            "Mazur", "Wysocki",
            "Eze", "Bello", "Chukwu", "Abara", "Owusu", "Asante",
            "Appiah", "Danso",
            "Mwangi", "Otieno", "Kariuki", "Wanjiru", "Haile", "Bekele",
            "Hassan", "Farouk", "Karimi", "Rahimi", "Aziz", "Nasser",
            "Rana", "Malhotra", "Chowdhury", "Rahman", "Ahmed", "Gupta",
            "Watanabe", "Takahashi", "Nakamura", "Ito", "Wong", "Chen",
            "Liu", "Wang", "Lee", "Han",
            "Cruz", "Reyes", "Morales", "Jimenez", "Castillo", "Vargas",
            "Ortiz", "Delgado"
        };

        // Instance-level (not local to GenerateSquad) so names stay unique across the
        // whole league, not just within one team's 20-player squad - GenerateSquad gets
        // called once per team against this same AgentSquadGenerator instance (see
        // ManagerPrototypeController.squadGenerator), so this HashSet accumulates every
        // name handed out so far, league-wide.
        private readonly HashSet<string> usedNames = new();

        public AgentTeam GenerateSquad(
            string teamName,
            float attackStrength,
            float defenceStrength)
        {
            Formation formation = GetDefaultFormationForTeam(teamName);

            AgentTeam team = new AgentTeam(teamName, formation);

            List<PlayerPosition> startingPositions = GetStartingPositions(formation);

            foreach (PlayerPosition position in startingPositions)
            {
                PlayerAgent player = GeneratePlayer(
                    GenerateUniqueName(),
                    position,
                    attackStrength,
                    defenceStrength
                );

                team.AddStarter(player);
            }

            foreach (PlayerPosition position in GetBenchPositions(formation))
            {
                PlayerAgent player = GeneratePlayer(
                    GenerateUniqueName(),
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

        // Public so the Tactics Board (Manager Mode) can pair this same ordered shape
        // with pixel/percentage pin coordinates, and so its formation-switch
        // reassignment can build a new StartingEleven in matching slot order.
        public List<PlayerPosition> GetStartingPositions(Formation formation)
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

                case Formation.ThreeFourTwoOne:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.LM,
                        PlayerPosition.CM,
                        PlayerPosition.CM,
                        PlayerPosition.RM,
                        PlayerPosition.AM,
                        PlayerPosition.AM,
                        PlayerPosition.ST
                    };

                default:
                    return GetStartingPositions(Formation.FourTwoThreeOne);
            }
        }

        private List<PlayerPosition> GetBenchPositions(Formation formation)
        {
            // Nine-player bench (matches the current Premier League matchday-squad rule of
            // 9 named subs, though only 5 are usable per match - see
            // ManagerPrototypeController.MaxSubsPerMatch): backup keeper, defensive cover,
            // midfield cover, attacking cover.
            switch (formation)
            {
                case Formation.FourFourTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.RB,
                        PlayerPosition.LB,
                        PlayerPosition.CM,
                        PlayerPosition.LM,
                        PlayerPosition.RM,
                        PlayerPosition.AM,
                        PlayerPosition.ST
                    };

                case Formation.ThreeFiveTwo:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.RWB,
                        PlayerPosition.LWB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.RM,
                        PlayerPosition.ST
                    };
                case Formation.ThreeFourTwoOne:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.CB,
                        PlayerPosition.RWB,
                        PlayerPosition.LWB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.RM,
                        PlayerPosition.ST
                    };

                default:
                    return new List<PlayerPosition>
                    {
                        PlayerPosition.GK,
                        PlayerPosition.CB,
                        PlayerPosition.LB,
                        PlayerPosition.RB,
                        PlayerPosition.CM,
                        PlayerPosition.AM,
                        PlayerPosition.RW,
                        PlayerPosition.LW,
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

            ApplyAgeAndHeight(player);

            ClampAttributes(player);
            AddSecondaryPositions(player);

            return player;
        }

        // Age/height are rolled independently of team strength (attackMultiplier/
        // defenceMultiplier don't apply here - being tall or old isn't a mark of quality),
        // then nudge a few existing attributes so the numbers feel connected to the
        // physical profile instead of being pure flavour text. Applied after the
        // position-specific generator and before ClampAttributes so the usual [1,100]
        // clamp still catches anything pushed out of range.
        private void ApplyAgeAndHeight(PlayerAgent player)
        {
            player.Age = GenerateAge();

            // Bell curve around the position's typical band rather than a hard
            // Random.Range clamp - a 190cm winger or a genuinely tiny striker is rare
            // but real, so the per-position band is a center of gravity, not a wall.
            // The only hard wall is the 150/200cm floor/ceiling below.
            (float minHeight, float maxHeight) = GetHeightRangeForPosition(player.PrimaryPosition);
            float heightMidpoint = (minHeight + maxHeight) / 2f;
            float heightSpread = (maxHeight - minHeight) / 2f;
            float heightStdDev = heightSpread / 2f;

            player.Height = Mathf.Clamp(RandomGaussian(heightMidpoint, heightStdDev), 150f, 200f);

            float heightFactor = heightSpread > 0f
                ? Mathf.Clamp((player.Height - heightMidpoint) / heightSpread, -1f, 1f)
                : 0f;

            // 1 at 18 fading to 0 at 24+; 0 below 29 fading up to 1 at 37+.
            float youthFactor = Mathf.Clamp01((24f - player.Age) / 6f);
            float veteranFactor = Mathf.Clamp01((player.Age - 29f) / 8f);

            player.Aerial += heightFactor * 8f;
            player.Strength += heightFactor * 6f;
            player.Pace -= heightFactor * 4f;

            player.Composure += (veteranFactor * 8f) - (youthFactor * 6f);
            player.Positioning += (veteranFactor * 5f) - (youthFactor * 4f);
            player.Pace -= veteranFactor * 8f;
            player.Stamina -= veteranFactor * 5f;
        }

        // Averaging two rolls instead of one Random.Range gives a rough bell curve
        // centred in the mid-20s (peak career years) instead of a flat distribution
        // across 17-35, so most squads read as prime-age with a scatter of young/veteran
        // outliers rather than an even spread.
        private int GenerateAge()
        {
            float roll = (Random.Range(17f, 35f) + Random.Range(17f, 35f)) / 2f;
            return Mathf.RoundToInt(roll);
        }

        // Box-Muller transform - UnityEngine.Random only gives uniform distributions,
        // and a uniform roll can't produce a bell curve with rare tail outliers.
        private float RandomGaussian(float mean, float stdDev)
        {
            float u1 = 1f - Random.value;
            float u2 = 1f - Random.value;
            float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return mean + (stdDev * standardNormal);
        }

        // Rough real-world height bands per position, in cm.
        private (float min, float max) GetHeightRangeForPosition(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK:
                    return (186f, 199f);

                case PlayerPosition.CB:
                    return (182f, 196f);

                case PlayerPosition.RB:
                case PlayerPosition.LB:
                    return (170f, 185f);

                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                    return (170f, 186f);

                case PlayerPosition.DM:
                    return (178f, 190f);

                case PlayerPosition.CM:
                    return (174f, 186f);

                case PlayerPosition.AM:
                    return (168f, 182f);

                case PlayerPosition.RM:
                case PlayerPosition.LM:
                    return (168f, 182f);

                case PlayerPosition.RW:
                case PlayerPosition.LW:
                    return (165f, 180f);

                case PlayerPosition.ST:
                    return (172f, 192f);

                default:
                    return (170f, 190f);
            }
        }

        // Bell curve around the position-typical band instead of a hard Random.Range
        // wall, same reasoning as height/age: a centre-back with a Koeman-level shot is
        // rare but real, so the band below is a center of gravity, not a limit. The
        // existing ClampAttributes 1-100 wall is the only true clamp, no separate one
        // needed here (unlike height, which had no pre-existing wall to reuse).
        private float RollAttribute(float min, float max)
        {
            float mean = (min + max) / 2f;
            float stdDev = (max - min) / 4f;
            return RandomGaussian(mean, stdDev);
        }

        private void ApplyBaseAttributes(PlayerAgent player)
        {
            player.Finishing = RollAttribute(35f, 60f);
            player.Passing = RollAttribute(35f, 60f);
            player.Dribbling = RollAttribute(35f, 60f);
            player.Crossing = RollAttribute(35f, 60f);
            player.Heading = RollAttribute(35f, 60f);

            player.Creativity = RollAttribute(35f, 60f);
            player.Positioning = RollAttribute(35f, 60f);
            player.Composure = RollAttribute(35f, 60f);

            player.Defending = RollAttribute(35f, 60f);
            player.Tackling = RollAttribute(35f, 60f);

            player.Pace = RollAttribute(45f, 75f);
            player.Strength = RollAttribute(45f, 75f);
            player.Stamina = RollAttribute(55f, 85f);
            player.Aerial = RollAttribute(35f, 65f);

            player.Goalkeeping = RollAttribute(1f, 10f);
            player.Reflexes = RollAttribute(1f, 10f);

            player.WeakFoot = RollAttribute(35f, 85f);
        }

        private void GenerateGoalkeeper(PlayerAgent player, float defenceMultiplier)
        {
            player.Goalkeeping = RollAttribute(65f, 88f) * defenceMultiplier;
            player.Reflexes = RollAttribute(65f, 90f) * defenceMultiplier;
            player.Positioning = RollAttribute(55f, 80f) * defenceMultiplier;
            player.Passing = RollAttribute(35f, 70f);
            player.Composure = RollAttribute(50f, 80f);

            player.Finishing = RollAttribute(18f, 32f);
            player.Dribbling = RollAttribute(20f, 35f);
            player.Crossing = RollAttribute(16f, 28f);
            player.Heading = RollAttribute(20f, 35f);
            player.Defending = RollAttribute(22f, 40f);
            player.Tackling = RollAttribute(20f, 38f);
            player.Stamina = RollAttribute(35f, 60f);
        }

        private void GenerateCentreBack(PlayerAgent player, float defenceMultiplier)
        {
            player.Defending = RollAttribute(60f, 85f) * defenceMultiplier;
            player.Tackling = RollAttribute(60f, 85f) * defenceMultiplier;
            player.Heading = RollAttribute(60f, 85f) * defenceMultiplier;
            player.Aerial = RollAttribute(65f, 90f) * defenceMultiplier;
            player.Strength = RollAttribute(65f, 90f);
            player.Positioning = RollAttribute(55f, 80f) * defenceMultiplier;
            player.Passing = RollAttribute(35f, 65f);

            player.Finishing = RollAttribute(20f, 38f);
            player.Dribbling = RollAttribute(20f, 50f);
            player.Crossing = RollAttribute(22f, 42f);
            player.Stamina = RollAttribute(55f, 78f);
        }

        private void GenerateFullBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollAttribute(50f, 75f) * defenceMultiplier;
            player.Tackling = RollAttribute(50f, 75f) * defenceMultiplier;
            player.Crossing = RollAttribute(50f, 78f) * attackMultiplier;
            player.Pace = RollAttribute(60f, 88f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Passing = RollAttribute(45f, 70f);
            player.Dribbling = RollAttribute(45f, 72f);

            player.Finishing = RollAttribute(22f, 42f);
            player.Heading = RollAttribute(35f, 65f);
            player.Aerial = RollAttribute(35f, 65f);
        }

        private void GenerateWingBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollAttribute(45f, 70f) * defenceMultiplier;
            player.Tackling = RollAttribute(45f, 72f) * defenceMultiplier;
            player.Crossing = RollAttribute(55f, 82f) * attackMultiplier;
            player.Pace = RollAttribute(65f, 90f);
            player.Stamina = RollAttribute(70f, 92f);
            player.Dribbling = RollAttribute(50f, 76f) * attackMultiplier;
            player.Passing = RollAttribute(45f, 70f);

            player.Finishing = RollAttribute(24f, 44f);
        }

        private void GenerateDefensiveMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollAttribute(55f, 80f) * defenceMultiplier;
            player.Tackling = RollAttribute(55f, 82f) * defenceMultiplier;
            player.Passing = RollAttribute(55f, 80f) * attackMultiplier;
            player.Positioning = RollAttribute(55f, 82f) * defenceMultiplier;
            player.Strength = RollAttribute(55f, 80f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Creativity = RollAttribute(40f, 70f) * attackMultiplier;

            player.Finishing = RollAttribute(25f, 45f);
            player.Dribbling = RollAttribute(35f, 65f);
            player.Heading = RollAttribute(40f, 70f);
            player.Aerial = RollAttribute(40f, 70f);
        }

        private void GenerateCentralMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Passing = RollAttribute(60f, 85f) * attackMultiplier;
            player.Creativity = RollAttribute(55f, 82f) * attackMultiplier;
            player.Positioning = RollAttribute(50f, 78f);
            player.Composure = RollAttribute(55f, 82f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Defending = RollAttribute(40f, 70f) * defenceMultiplier;
            player.Tackling = RollAttribute(40f, 70f) * defenceMultiplier;
            player.Dribbling = RollAttribute(45f, 75f) * attackMultiplier;

            player.Finishing = RollAttribute(25f, 55f) * attackMultiplier;
        }

        private void GenerateAttackingMidfielder(PlayerAgent player, float attackMultiplier)
        {
            player.Passing = RollAttribute(60f, 85f) * attackMultiplier;
            player.Creativity = RollAttribute(65f, 90f) * attackMultiplier;
            player.Dribbling = RollAttribute(60f, 88f) * attackMultiplier;
            player.Composure = RollAttribute(55f, 85f);
            player.Finishing = RollAttribute(40f, 70f) * attackMultiplier;
            player.Positioning = RollAttribute(50f, 78f);

            player.Defending = RollAttribute(25f, 48f);
            player.Tackling = RollAttribute(25f, 48f);
            player.Heading = RollAttribute(25f, 55f);
            player.Aerial = RollAttribute(25f, 55f);
            player.Stamina = RollAttribute(58f, 82f);
        }

        private void GenerateWideMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Crossing = RollAttribute(58f, 84f) * attackMultiplier;
            player.Dribbling = RollAttribute(55f, 82f) * attackMultiplier;
            player.Pace = RollAttribute(60f, 88f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Passing = RollAttribute(48f, 74f) * attackMultiplier;
            player.Defending = RollAttribute(35f, 65f) * defenceMultiplier;
            player.Tackling = RollAttribute(35f, 65f) * defenceMultiplier;

            player.Finishing = RollAttribute(25f, 55f) * attackMultiplier;
        }

        private void GenerateWinger(PlayerAgent player, float attackMultiplier)
        {
            player.Pace = RollAttribute(68f, 94f);
            player.Dribbling = RollAttribute(62f, 90f) * attackMultiplier;
            player.Crossing = RollAttribute(55f, 84f) * attackMultiplier;
            player.Creativity = RollAttribute(50f, 78f) * attackMultiplier;
            player.Passing = RollAttribute(45f, 72f) * attackMultiplier;
            player.Finishing = RollAttribute(35f, 68f) * attackMultiplier;
            player.Composure = RollAttribute(45f, 75f);

            player.Defending = RollAttribute(22f, 42f);
            player.Tackling = RollAttribute(22f, 42f);
            player.Heading = RollAttribute(20f, 55f);
            player.Aerial = RollAttribute(20f, 55f);
            player.Stamina = RollAttribute(60f, 86f);
        }

        private void GenerateStriker(PlayerAgent player, float attackMultiplier)
        {
            player.Finishing = RollAttribute(62f, 90f) * attackMultiplier;
            player.Positioning = RollAttribute(60f, 88f) * attackMultiplier;
            player.Composure = RollAttribute(58f, 88f) * attackMultiplier;
            player.Heading = RollAttribute(45f, 82f) * attackMultiplier;
            player.Aerial = RollAttribute(45f, 82f) * attackMultiplier;
            player.Strength = RollAttribute(45f, 82f);
            player.Pace = RollAttribute(50f, 88f);
            player.Dribbling = RollAttribute(40f, 75f) * attackMultiplier;

            player.Passing = RollAttribute(30f, 60f);
            player.Creativity = RollAttribute(25f, 60f);
            player.Defending = RollAttribute(20f, 40f);
            player.Tackling = RollAttribute(20f, 40f);
            player.Stamina = RollAttribute(55f, 80f);
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

        private string GenerateUniqueName()
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