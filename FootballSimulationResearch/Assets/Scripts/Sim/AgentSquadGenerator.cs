using System.Collections.Generic;
using UnityEngine;

namespace Sim
{
    public sealed class SquadQualityTarget
    {
        public float FirstTeamOverall;
        public float BenchOverall;
        public float ReserveOverall;

        public SquadQualityTarget(float firstTeamOverall, float benchOverall, float reserveOverall)
        {
            FirstTeamOverall = firstTeamOverall;
            BenchOverall = benchOverall;
            ReserveOverall = reserveOverall;
        }
    }

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

        // Manager Mode reserve pool (session 7, injuries phase) - a thin public wrapper
        // around the existing private GeneratePlayer, exposing single-player generation
        // for a team's reserve depth beneath the real 20-man matchday squad. Reuses every
        // existing position-based range table and the Random.State-wrapped newer-
        // attributes pass unchanged - purely additive, no existing generation logic
        // touched.
        public PlayerAgent GenerateReservePlayer(PlayerPosition position, float attackStrength, float defenceStrength)
        {
            return GeneratePlayer(GenerateUniqueName(), position, attackStrength, defenceStrength);
        }

        public PlayerAgent GenerateReservePlayer(PlayerPosition position, SquadQualityTarget target)
        {
            PlayerAgent player = GeneratePlayer(GenerateUniqueName(), position, 1f, 1f);
            ShiftPlayerToOverall(player, target.ReserveOverall);
            return player;
        }

        // New-world generation boundary. Historical results choose only these squad
        // quality targets; neutral player generation plus calibration creates the
        // players. Match systems never read history or reputation from this method.
        public AgentTeam GenerateSquad(string teamName, SquadQualityTarget target)
        {
            AgentTeam team = GenerateSquad(teamName, 1f, 1f);
            ShiftGroupToAverage(team.StartingEleven, target.FirstTeamOverall);
            ShiftGroupToAverage(team.Bench, target.BenchOverall);
            return team;
        }

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

            // System.Guid doesn't touch UnityEngine.Random, so assigning it here (outside
            // the Random.State-wrapped block below) can't shift the shared RNG sequence
            // Research Mode's same-seed comparisons depend on. Fully-qualified rather
            // than `using System;` - this file uses bare `Random` everywhere to mean
            // UnityEngine.Random, and that using directive would make every one of those
            // an ambiguous CS0104 (see feedback_random_namespace_ambiguity).
            player.PlayerId = System.Guid.NewGuid().ToString();

            // Strengthened from 0.35 to 0.75 (session 11, explicit authorization) so
            // Liverpool's XI would honestly read 80+ under the old attribute generation.
            // Session 12: once the maxed-stat overhaul made attribute generation honest
            // (no more clamping at 100 doing the real work), that same uniform 0.75
            // reproduced a much bigger club-to-club Overall spread than intended - real
            // trained strengths span AttackStrength 0.70 (Burnley) to 1.66 (Man City), and
            // a single symmetric lerp punishes the weak end exactly as hard as it rewards
            // the strong end. Thomas's own reference point: EA FC's Career Mode has
            // Wolves (a real lower-table side) averaging ~75 against Man City's ~84 - a
            // ~9-point gap, not the ~23 a uniform 0.75 lerp was producing. Split
            // asymmetric: strong clubs (strength >= 1) keep the full 0.75 effect so the
            // ceiling stays where session 11 wanted it; weak clubs (strength < 1) get a
            // much gentler 0.3, softening how hard a bottom-table side gets pulled down -
            // the Premier League being competitive is mostly a "no one fields genuinely
            // bad players" thing, not a "everyone is equally elite" thing. Calibrated
            // empirically against real trained strengths (Man City/Wolves/Burnley) via
            // GenerateReservePlayer, isolated generator instances, no live-state side
            // effects - see session 12 memory for the full before/after numbers.
            const float strongFactor = 0.75f;
            const float weakFactor = 0.3f;

            float attackTarget = attackStrength;
            float attackFactor = attackTarget >= 1f ? strongFactor : weakFactor;
            float attackMultiplier = Mathf.Lerp(1f, attackTarget, attackFactor);

            float defenceTarget = 1f / defenceStrength;
            float defenceFactor = defenceTarget >= 1f ? strongFactor : weakFactor;
            float defenceMultiplier = Mathf.Lerp(1f, defenceTarget, defenceFactor);

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
            GenerateNewerAttributes(player, position, attackMultiplier, defenceMultiplier);

            ClampAttributes(player);
            AddSecondaryPositions(player);

            return player;
        }

        private static void ShiftGroupToAverage(List<PlayerAgent> players, float targetAverage)
        {
            if (players == null || players.Count == 0) return;
            for (int pass = 0; pass < 3; pass++)
            {
                float current = 0f;
                foreach (PlayerAgent player in players) current += player.GetOverallRating();
                float shift = targetAverage - current / players.Count;
                if (Mathf.Abs(shift) < 0.02f) break;
                foreach (PlayerAgent player in players) ShiftPlayerAttributes(player, shift);
            }
        }

        private static void ShiftPlayerToOverall(PlayerAgent player, float targetOverall)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                float shift = targetOverall - player.GetOverallRating();
                if (Mathf.Abs(shift) < 0.02f) break;
                ShiftPlayerAttributes(player, shift);
            }
        }

        private static void ShiftPlayerAttributes(PlayerAgent player, float shift)
        {
            player.Finishing = Shift(player.Finishing, shift);
            player.Passing = Shift(player.Passing, shift);
            player.Dribbling = Shift(player.Dribbling, shift);
            player.Crossing = Shift(player.Crossing, shift);
            player.Heading = Shift(player.Heading, shift);
            player.LongShots = Shift(player.LongShots, shift);
            player.ThroughBalls = Shift(player.ThroughBalls, shift);
            player.FreeKicks = Shift(player.FreeKicks, shift);
            player.Creativity = Shift(player.Creativity, shift);
            player.Positioning = Shift(player.Positioning, shift);
            player.Composure = Shift(player.Composure, shift);
            player.OffTheBall = Shift(player.OffTheBall, shift);
            player.Leadership = Shift(player.Leadership, shift * 0.5f);
            player.Defending = Shift(player.Defending, shift);
            player.Tackling = Shift(player.Tackling, shift);
            player.Marking = Shift(player.Marking, shift);
            player.Pace = Shift(player.Pace, shift);
            player.Strength = Shift(player.Strength, shift);
            player.Stamina = Shift(player.Stamina, shift);
            player.Aerial = Shift(player.Aerial, shift);
            player.WeakFoot = Shift(player.WeakFoot, shift * 0.5f);
            if (player.PrimaryPosition == PlayerPosition.GK)
            {
                player.Goalkeeping = Shift(player.Goalkeeping, shift);
                player.Reflexes = Shift(player.Reflexes, shift);
            }
            float newOverall = player.GetOverallRating();
            player.Potential = Mathf.Clamp(Mathf.Max(newOverall, player.Potential + shift), newOverall, 99f);
        }

        private static float Shift(float value, float amount) => Mathf.Clamp(value + amount, 1f, 99f);

        // Session 7 additions (LongShots/ThroughBalls/OffTheBall/Marking/FreeKicks) -
        // deliberately a single self-contained pass wrapped in a Random.State save/
        // restore, rather than rolled inline inside each position-specific generator
        // above. Every RollAttribute call consumes from the same shared UnityEngine.
        // Random stream that every other stat (and therefore every existing squad and
        // simulated match, under a given seed) depends on - inserting new rolls inline
        // would shift the sequence for everything generated afterward. Saving and
        // restoring Random.state around this method means it can still use the same
        // procedural Gaussian roll as everything else, but consumes zero budget from
        // the sequence - byte-for-byte identical existing-stat output before and after
        // this method existed, confirmed by same-seed regeneration (see HANDOFF).
        // FreeKicks isn't read by the match sim yet (no free-kick event exists there) -
        // generated anyway so the data's ready whenever that lands.
        private void GenerateNewerAttributes(PlayerAgent player, PlayerPosition position, float attackMultiplier, float defenceMultiplier)
        {
            Random.State savedState = Random.state;

            (float longShotsMin, float longShotsMax) = GetLongShotsRange(position);
            player.LongShots = RollBoostedAttribute(longShotsMin, longShotsMax, attackMultiplier);

            (float throughBallsMin, float throughBallsMax) = GetThroughBallsRange(position);
            player.ThroughBalls = RollBoostedAttribute(throughBallsMin, throughBallsMax, attackMultiplier);

            (float offTheBallMin, float offTheBallMax) = GetOffTheBallRange(position);
            player.OffTheBall = RollBoostedAttribute(offTheBallMin, offTheBallMax, attackMultiplier);

            (float markingMin, float markingMax) = GetMarkingRange(position);
            player.Marking = RollBoostedAttribute(markingMin, markingMax, defenceMultiplier);

            (float freeKicksMin, float freeKicksMax) = GetFreeKicksRange(position);
            player.FreeKicks = RollBoostedAttribute(freeKicksMin, freeKicksMax, attackMultiplier);

            // Leadership (session 7, captaincy pass) - deliberately flat across positions
            // and NOT scaled by attackMultiplier/defenceMultiplier, unlike every other stat
            // above: it's a personality trait, not a footballing skill tied to a role or to
            // team strength. Age is the one thing that should shift it - real captains skew
            // veteran - reusing the exact same youth/veteran factor shape ApplyAgeAndHeight
            // already applies to Composure/Positioning, rather than inventing a new curve.
            player.Leadership = RollAttribute(30f, 65f);
            float youthFactor = Mathf.Clamp01((24f - player.Age) / 6f);
            float veteranFactor = Mathf.Clamp01((player.Age - 29f) / 8f);
            player.Leadership += (veteranFactor * 12f) - (youthFactor * 10f);

            // Potential (career-arc progression) - a hidden ceiling GetOverallRating()
            // can climb toward over a career (see ManagerPlayerDevelopment). Every stat
            // GetOverallRating() actually reads for this position is already final by
            // this point (position generator + ApplyAgeAndHeight ran before this method),
            // so the live rating taken here is a genuine "current ability" baseline, not
            // an estimate. Headroom above it is skewed heavily by youthFactor - an 18-
            // year-old might have most of a 35-point ceiling above his current level, a
            // 32-year-old only a sliver - but never zero, since even a veteran's "true"
            // level is rarely exactly today's rolled number.
            float currentOverall = player.GetOverallRating();
            float headroomRoll = RollAttribute(0f, 35f) * (0.3f + (youthFactor * 0.7f));
            player.Potential = Mathf.Clamp(currentOverall + headroomRoll, currentOverall, 99f);

            Random.state = savedState;
        }

        // Best shooting-from-distance profile: creative central positions and strikers,
        // weakest for GK/CB who rarely test it.
        private (float min, float max) GetLongShotsRange(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK: return (15f, 30f);
                case PlayerPosition.CB: return (20f, 40f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return (25f, 48f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return (28f, 52f);
                case PlayerPosition.DM: return (30f, 55f);
                case PlayerPosition.CM: return (40f, 70f);
                case PlayerPosition.AM: return (48f, 78f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return (32f, 60f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return (35f, 65f);
                case PlayerPosition.ST: return (48f, 80f);
                default: return (30f, 55f);
            }
        }

        // Vision/incisive-passing profile: peaks in central midfield/AM, weakest for
        // wide defenders and out-and-out strikers who aren't asked to thread passes.
        private (float min, float max) GetThroughBallsRange(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK: return (15f, 30f);
                case PlayerPosition.CB: return (22f, 42f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return (35f, 60f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return (38f, 64f);
                case PlayerPosition.DM: return (45f, 72f);
                case PlayerPosition.CM: return (52f, 80f);
                case PlayerPosition.AM: return (60f, 88f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return (38f, 66f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return (40f, 68f);
                case PlayerPosition.ST: return (30f, 58f);
                default: return (35f, 60f);
            }
        }

        // Movement-into-space profile: peaks for attackers/AM whose job is finding the
        // right pocket of space, weakest for defenders/GK.
        private (float min, float max) GetOffTheBallRange(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK: return (15f, 30f);
                case PlayerPosition.CB: return (25f, 45f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return (30f, 55f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return (32f, 58f);
                case PlayerPosition.DM: return (30f, 55f);
                case PlayerPosition.CM: return (40f, 68f);
                case PlayerPosition.AM: return (55f, 85f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return (45f, 72f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return (50f, 78f);
                case PlayerPosition.ST: return (60f, 90f);
                default: return (35f, 60f);
            }
        }

        // Positional defensive discipline profile: peaks for CB/DM whose whole job is
        // this, weakest for attackers who aren't asked to defend structurally.
        private (float min, float max) GetMarkingRange(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK: return (20f, 40f);
                case PlayerPosition.CB: return (60f, 88f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return (50f, 78f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return (45f, 72f);
                case PlayerPosition.DM: return (55f, 82f);
                case PlayerPosition.CM: return (35f, 65f);
                case PlayerPosition.AM: return (20f, 45f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return (32f, 60f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return (20f, 42f);
                case PlayerPosition.ST: return (18f, 38f);
                default: return (30f, 55f);
            }
        }

        // Dead-ball specialist profile: technical/creative positions and free-kick-
        // taking fullbacks/wingers skew highest, weakest for pure penalty-box strikers
        // and defenders who rarely take them.
        private (float min, float max) GetFreeKicksRange(PlayerPosition position)
        {
            switch (position)
            {
                case PlayerPosition.GK: return (10f, 20f);
                case PlayerPosition.CB: return (20f, 40f);
                case PlayerPosition.RB:
                case PlayerPosition.LB: return (30f, 55f);
                case PlayerPosition.RWB:
                case PlayerPosition.LWB: return (32f, 58f);
                case PlayerPosition.DM: return (30f, 55f);
                case PlayerPosition.CM: return (40f, 68f);
                case PlayerPosition.AM: return (50f, 80f);
                case PlayerPosition.RM:
                case PlayerPosition.LM: return (35f, 62f);
                case PlayerPosition.RW:
                case PlayerPosition.LW: return (38f, 65f);
                case PlayerPosition.ST: return (35f, 62f);
                default: return (30f, 55f);
            }
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
        //
        // stdDev divisor tightened 4->6 (session 12, real bug report from Thomas's own
        // save - "quite a few stats at 100, even on low rated players"): /4 put the 100
        // ceiling as close as ~2.3 sigma away for some of the higher bands below, which
        // is a genuinely common outcome (~1%) once multiplied across ~20 attributes per
        // player x 25 players per squad x 20 clubs - not the rare "GOAT tier" event 100
        // is supposed to represent. /6 keeps the same center of gravity, just narrower,
        // so 100 is still reachable but meaningfully rarer.
        private float RollAttribute(float min, float max)
        {
            float mean = (min + max) / 2f;
            float stdDev = (max - min) / 6f;
            return RandomGaussian(mean, stdDev);
        }

        // Boosted variant for attributes that scale with attackMultiplier/
        // defenceMultiplier (session 12 fix). The old pattern - `RollAttribute(min,max) *
        // multiplier` - scaled the ALREADY-rolled value, which scales the Gaussian's mean
        // AND stdDev together by the same factor. At an elite club's ~1.4x multiplier
        // (strengthened last session for honest squad-strength calibration), several
        // bands' boosted MEAN already sat at or above 100 before any randomness was even
        // applied - e.g. GenerateWinger's Dribbling (baseline mean 76 x 1.4 ~ 106) - so
        // hitting the ceiling became the typical outcome for these specific attributes at
        // strong clubs, not a rare tail event (confirmed live against Thomas's real save:
        // a 74-rated CM with Passing=100, Through Balls=100, Tackling=99 all at once).
        //
        // First attempt (same session) shifted the whole band down by however much the
        // boosted max overshot a flat ceiling before rolling - preserved band width, but
        // for a wide band at a strong club the shift could be big enough to drag the
        // MEAN below what a weaker, unshifted club produced for the same attribute
        // (confirmed live: Liverpool's shifted Finishing band landed a lower mean than
        // mid-table's untouched one - the exact opposite of what attackMultiplier is
        // supposed to represent). A shift is fundamentally the wrong tool near a ceiling,
        // since it treats every point in the band identically instead of compressing
        // harder the further past the ceiling something lands.
        //
        // This version rolls the FULL boosted band exactly as before (mean and spread
        // both still scale with `multiplier`, so a stronger club still visibly produces a
        // stronger roll everywhere in the normal range), then only reshapes the result if
        // it lands above `softCeiling`: an exponential squash asymptotically approaching
        // (never quite reaching) 100. Strictly monotonic in the raw roll - a bigger raw
        // value always produces a bigger final value, so two clubs' relative strength can
        // never invert the way the shift-based version did - while still making 100
        // require a genuinely extreme roll rather than being the common outcome for any
        // band whose boosted mean happens to sit near the ceiling.
        private float RollBoostedAttribute(float min, float max, float multiplier)
        {
            float raw = RollAttribute(min * multiplier, max * multiplier);

            const float softCeiling = 85f;
            if (raw <= softCeiling)
            {
                return raw;
            }

            const float compressionScale = 25f;
            float excess = raw - softCeiling;
            float compressed = (100f - softCeiling) * (1f - Mathf.Exp(-excess / compressionScale));
            return softCeiling + compressed;
        }

        // Dampened boost for general footballing-quality attributes that were sitting at
        // a flat, unboosted band inside a role method even though they aren't that
        // player's specialty (session 12 - Thomas's own example: an 88-rated Liverpool
        // striker, Rohan Park, with Passing 42/Creativity 38, "embarrassing for a premier
        // league striker"). These aren't the role's PRIMARY skill (a striker's game is
        // still built on Finishing, not Passing), so they only get 40% of the club's full
        // multiplier effect - enough that a genuinely elite side fields players who are at
        // least competent outside their specialty, without homogenizing every player into
        // an equally-good-at-everything generalist. Deliberately NOT applied to
        // attributes that are genuinely irrelevant to a role regardless of club quality
        // (a striker's Tackling, a centre-back's Finishing) - those staying low is
        // realistic specialization, not the bug being fixed here.
        private float RollSecondaryAttribute(float min, float max, float multiplier)
        {
            float dampened = 1f + (multiplier - 1f) * 0.4f;
            return RollBoostedAttribute(min, max, dampened);
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
            player.Goalkeeping = RollBoostedAttribute(65f, 88f, defenceMultiplier);
            player.Reflexes = RollBoostedAttribute(65f, 90f, defenceMultiplier);
            player.Positioning = RollBoostedAttribute(55f, 80f, defenceMultiplier);
            player.Passing = RollSecondaryAttribute(35f, 70f, defenceMultiplier);
            player.Composure = RollSecondaryAttribute(50f, 80f, defenceMultiplier);

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
            player.Defending = RollBoostedAttribute(60f, 85f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(60f, 85f, defenceMultiplier);
            player.Heading = RollBoostedAttribute(60f, 85f, defenceMultiplier);
            player.Aerial = RollBoostedAttribute(65f, 90f, defenceMultiplier);
            player.Strength = RollAttribute(65f, 90f);
            player.Positioning = RollBoostedAttribute(55f, 80f, defenceMultiplier);
            player.Passing = RollSecondaryAttribute(35f, 65f, defenceMultiplier);
            player.Crossing = RollSecondaryAttribute(22f, 42f, defenceMultiplier);
            player.Composure = RollSecondaryAttribute(45f, 75f, defenceMultiplier);

            player.Finishing = RollAttribute(20f, 38f);
            player.Dribbling = RollAttribute(20f, 50f);
            player.Stamina = RollAttribute(55f, 78f);
        }

        private void GenerateFullBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollBoostedAttribute(50f, 75f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(50f, 75f, defenceMultiplier);
            player.Crossing = RollBoostedAttribute(50f, 78f, attackMultiplier);
            player.Pace = RollAttribute(60f, 88f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Passing = RollSecondaryAttribute(45f, 70f, attackMultiplier);
            player.Dribbling = RollSecondaryAttribute(45f, 72f, attackMultiplier);
            player.Composure = RollSecondaryAttribute(45f, 72f, defenceMultiplier);

            player.Finishing = RollAttribute(22f, 42f);
            player.Heading = RollAttribute(35f, 65f);
            player.Aerial = RollAttribute(35f, 65f);
        }

        private void GenerateWingBack(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollBoostedAttribute(45f, 70f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(45f, 72f, defenceMultiplier);
            player.Crossing = RollBoostedAttribute(55f, 82f, attackMultiplier);
            player.Pace = RollAttribute(65f, 90f);
            player.Stamina = RollAttribute(70f, 92f);
            player.Dribbling = RollBoostedAttribute(50f, 76f, attackMultiplier);
            player.Passing = RollSecondaryAttribute(45f, 70f, attackMultiplier);
            player.Composure = RollSecondaryAttribute(45f, 72f, defenceMultiplier);

            player.Finishing = RollAttribute(24f, 44f);
        }

        private void GenerateDefensiveMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Defending = RollBoostedAttribute(55f, 80f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(55f, 82f, defenceMultiplier);
            player.Passing = RollBoostedAttribute(55f, 80f, attackMultiplier);
            player.Positioning = RollBoostedAttribute(55f, 82f, defenceMultiplier);
            player.Strength = RollAttribute(55f, 80f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Creativity = RollBoostedAttribute(40f, 70f, attackMultiplier);
            player.Dribbling = RollSecondaryAttribute(35f, 65f, attackMultiplier);
            player.Composure = RollSecondaryAttribute(45f, 75f, defenceMultiplier);

            player.Finishing = RollAttribute(25f, 45f);
            player.Heading = RollAttribute(40f, 70f);
            player.Aerial = RollAttribute(40f, 70f);
        }

        private void GenerateCentralMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Passing = RollBoostedAttribute(60f, 85f, attackMultiplier);
            player.Creativity = RollBoostedAttribute(55f, 82f, attackMultiplier);
            player.Positioning = RollSecondaryAttribute(50f, 78f, defenceMultiplier);
            player.Composure = RollSecondaryAttribute(55f, 82f, attackMultiplier);
            player.Stamina = RollAttribute(65f, 90f);
            player.Defending = RollBoostedAttribute(40f, 70f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(40f, 70f, defenceMultiplier);
            player.Dribbling = RollBoostedAttribute(45f, 75f, attackMultiplier);

            player.Finishing = RollBoostedAttribute(25f, 55f, attackMultiplier);
        }

        private void GenerateAttackingMidfielder(PlayerAgent player, float attackMultiplier)
        {
            player.Passing = RollBoostedAttribute(60f, 85f, attackMultiplier);
            player.Creativity = RollBoostedAttribute(65f, 90f, attackMultiplier);
            player.Dribbling = RollBoostedAttribute(60f, 88f, attackMultiplier);
            player.Composure = RollSecondaryAttribute(55f, 85f, attackMultiplier);
            player.Finishing = RollBoostedAttribute(40f, 70f, attackMultiplier);
            player.Positioning = RollSecondaryAttribute(50f, 78f, attackMultiplier);

            player.Defending = RollAttribute(25f, 48f);
            player.Tackling = RollAttribute(25f, 48f);
            player.Heading = RollAttribute(25f, 55f);
            player.Aerial = RollAttribute(25f, 55f);
            player.Stamina = RollAttribute(58f, 82f);
        }

        private void GenerateWideMidfielder(PlayerAgent player, float attackMultiplier, float defenceMultiplier)
        {
            player.Crossing = RollBoostedAttribute(58f, 84f, attackMultiplier);
            player.Dribbling = RollBoostedAttribute(55f, 82f, attackMultiplier);
            player.Pace = RollAttribute(60f, 88f);
            player.Stamina = RollAttribute(65f, 90f);
            player.Passing = RollBoostedAttribute(48f, 74f, attackMultiplier);
            player.Defending = RollBoostedAttribute(35f, 65f, defenceMultiplier);
            player.Tackling = RollBoostedAttribute(35f, 65f, defenceMultiplier);

            player.Finishing = RollBoostedAttribute(25f, 55f, attackMultiplier);
        }

        private void GenerateWinger(PlayerAgent player, float attackMultiplier)
        {
            player.Pace = RollAttribute(68f, 94f);
            player.Dribbling = RollBoostedAttribute(62f, 90f, attackMultiplier);
            player.Crossing = RollBoostedAttribute(55f, 84f, attackMultiplier);
            player.Creativity = RollBoostedAttribute(50f, 78f, attackMultiplier);
            player.Passing = RollBoostedAttribute(45f, 72f, attackMultiplier);
            player.Finishing = RollBoostedAttribute(35f, 68f, attackMultiplier);
            player.Composure = RollSecondaryAttribute(45f, 75f, attackMultiplier);

            player.Defending = RollAttribute(22f, 42f);
            player.Tackling = RollAttribute(22f, 42f);
            player.Heading = RollAttribute(20f, 55f);
            player.Aerial = RollAttribute(20f, 55f);
            player.Stamina = RollAttribute(60f, 86f);
        }

        private void GenerateStriker(PlayerAgent player, float attackMultiplier)
        {
            player.Finishing = RollBoostedAttribute(62f, 90f, attackMultiplier);
            player.Positioning = RollBoostedAttribute(60f, 88f, attackMultiplier);
            player.Composure = RollBoostedAttribute(58f, 88f, attackMultiplier);
            player.Heading = RollBoostedAttribute(45f, 82f, attackMultiplier);
            player.Aerial = RollBoostedAttribute(45f, 82f, attackMultiplier);
            player.Strength = RollAttribute(45f, 82f);
            player.Pace = RollAttribute(50f, 88f);
            player.Dribbling = RollBoostedAttribute(40f, 75f, attackMultiplier);

            player.Passing = RollSecondaryAttribute(30f, 60f, attackMultiplier);
            player.Creativity = RollSecondaryAttribute(25f, 60f, attackMultiplier);
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
            player.LongShots = Clamp(player.LongShots);
            player.ThroughBalls = Clamp(player.ThroughBalls);
            player.FreeKicks = Clamp(player.FreeKicks);

            player.Creativity = Clamp(player.Creativity);
            player.Positioning = Clamp(player.Positioning);
            player.Composure = Clamp(player.Composure);
            player.OffTheBall = Clamp(player.OffTheBall);
            player.Leadership = Clamp(player.Leadership);

            player.Defending = Clamp(player.Defending);
            player.Tackling = Clamp(player.Tackling);
            player.Marking = Clamp(player.Marking);

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
