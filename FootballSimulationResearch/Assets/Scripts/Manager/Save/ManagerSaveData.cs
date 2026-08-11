using System;
using System.Collections.Generic;
using Sim;

namespace Manager.Save
{
    // Full career-persistence schema (career arc, Phase 5, session 8). Manual DTOs
    // mapped explicitly to/from the live PlayerAgent/Manager* classes (see
    // ManagerPrototypeController.BuildSaveData/ApplySaveData) rather than JsonUtility
    // auto-serializing those live gameplay classes directly - this means zero changes
    // to PlayerAgent.cs's class declaration (no [Serializable] attribute needed there),
    // fully sidestepping any risk to its "byte-for-byte unchanged" constraint.
    //
    // Deliberate scope limits, explicitly accepted rather than oversights: only the
    // MANAGED team's squad/reserves/roles are saved - the other 19 clubs' squads
    // regenerate fresh (same strength-seeded generation, different individual rolls)
    // the next time something needs them, since no AI-vs-AI transfer activity exists
    // for a saved roster to matter to. Condition/injuries/this-season's appearance
    // counts are NOT saved either - matchday/season-scoped ephemeral state, resets to a
    // clean slate on load (reads as "returned from a well-earned break").
    [Serializable]
    public class PlayerAgentSaveData
    {
        public string PlayerId;
        public string Name;
        public int Age;
        public float Height;
        public PlayerRole Role;
        public PlayerPosition PrimaryPosition;
        public List<PlayerPosition> SecondaryPositions = new();

        public float Finishing, Passing, Dribbling, Crossing, Heading, LongShots, ThroughBalls, FreeKicks;
        public float Creativity, Positioning, Composure, OffTheBall, Leadership;
        public float Defending, Tackling, Marking;
        public float Pace, Strength, Stamina, Aerial;
        public float Goalkeeping, Reflexes;
        public float WeakFoot;
        public float Potential;

        public static PlayerAgentSaveData FromPlayer(PlayerAgent p)
        {
            return new PlayerAgentSaveData
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Age = p.Age,
                Height = p.Height,
                Role = p.Role,
                PrimaryPosition = p.PrimaryPosition,
                SecondaryPositions = new List<PlayerPosition>(p.SecondaryPositions),
                Finishing = p.Finishing,
                Passing = p.Passing,
                Dribbling = p.Dribbling,
                Crossing = p.Crossing,
                Heading = p.Heading,
                LongShots = p.LongShots,
                ThroughBalls = p.ThroughBalls,
                FreeKicks = p.FreeKicks,
                Creativity = p.Creativity,
                Positioning = p.Positioning,
                Composure = p.Composure,
                OffTheBall = p.OffTheBall,
                Leadership = p.Leadership,
                Defending = p.Defending,
                Tackling = p.Tackling,
                Marking = p.Marking,
                Pace = p.Pace,
                Strength = p.Strength,
                Stamina = p.Stamina,
                Aerial = p.Aerial,
                Goalkeeping = p.Goalkeeping,
                Reflexes = p.Reflexes,
                WeakFoot = p.WeakFoot,
                Potential = p.Potential
            };
        }

        public PlayerAgent ToPlayer()
        {
            PlayerAgent p = new PlayerAgent(Name, Role, PrimaryPosition)
            {
                PlayerId = PlayerId,
                Age = Age,
                Height = Height,
                SecondaryPositions = new List<PlayerPosition>(SecondaryPositions),
                Finishing = Finishing,
                Passing = Passing,
                Dribbling = Dribbling,
                Crossing = Crossing,
                Heading = Heading,
                LongShots = LongShots,
                ThroughBalls = ThroughBalls,
                FreeKicks = FreeKicks,
                Creativity = Creativity,
                Positioning = Positioning,
                Composure = Composure,
                OffTheBall = OffTheBall,
                Leadership = Leadership,
                Defending = Defending,
                Tackling = Tackling,
                Marking = Marking,
                Pace = Pace,
                Strength = Strength,
                Stamina = Stamina,
                Aerial = Aerial,
                Goalkeeping = Goalkeeping,
                Reflexes = Reflexes,
                WeakFoot = WeakFoot,
                Potential = Potential
            };

            return p;
        }
    }

    [Serializable]
    public class AgentTeamSaveData
    {
        public string TeamName;
        public Formation Formation;
        public List<PlayerAgentSaveData> StartingEleven = new();
        public List<PlayerAgentSaveData> Bench = new();

        public static AgentTeamSaveData FromTeam(AgentTeam team)
        {
            AgentTeamSaveData data = new AgentTeamSaveData { TeamName = team.TeamName, Formation = team.Formation };

            foreach (PlayerAgent p in team.StartingEleven) data.StartingEleven.Add(PlayerAgentSaveData.FromPlayer(p));
            foreach (PlayerAgent p in team.Bench) data.Bench.Add(PlayerAgentSaveData.FromPlayer(p));

            return data;
        }

        public AgentTeam ToTeam()
        {
            AgentTeam team = new AgentTeam(TeamName, Formation);

            foreach (PlayerAgentSaveData dto in StartingEleven) team.AddStarter(dto.ToPlayer());
            foreach (PlayerAgentSaveData dto in Bench) team.AddBenchPlayer(dto.ToPlayer());

            return team;
        }
    }

    [Serializable]
    public class LeagueTableEntrySaveData
    {
        public int TeamId;
        public int Played;
        public int Wins;
        public int Draws;
        public int Losses;
        public int GoalsFor;
        public int GoalsAgainst;
        public int Points;
    }

    [Serializable]
    public class SeasonRecordSaveData
    {
        public int Season;
        public int FinalPosition;
        public bool IsChampion;
        public float PrizeMoney;
        public float BoardBoost;
        public int Wins;
        public int Draws;
        public int Losses;
        public int Points;
    }

    // TeamName renamed to Region (world-scattered scouting rework, session 9) - scouted
    // prospects are pooled by region now, not tied to a real club at all.
    [Serializable]
    public class YouthPoolSaveData
    {
        public string Region;
        public List<PlayerAgentSaveData> Prospects = new();
    }

    // Only the managed team's designations are saved (the only ManagerSquadRoles
    // instance that matters post-load) - condition/injuries/appearances deliberately
    // excluded, see the class-level comment above.
    [Serializable]
    public class ManagerSquadRolesSaveData
    {
        public string CaptainId;
        public string ViceCaptainId;
        public string PenaltyTakerId;
        public string FreeKickTakerId;
        public string LeftCornerTakerId;
        public string RightCornerTakerId;
        public List<string> AttackingRolePlayerIds = new();
        public List<string> DefensiveRolePlayerIds = new();
    }

    [Serializable]
    public class ManagerSaveData
    {
        public int SaveVersion = 1;
        public string ManagerName;
        public string ManagedTeamName;
        public int CurrentSeason;
        public int CurrentFixtureIndex;
        public string ActiveSeasonFileName;

        public List<LeagueTableEntrySaveData> TableEntries = new();
        public AgentTeamSaveData ManagedSquad;
        public List<PlayerAgentSaveData> ManagedReservePool = new();
        public ManagerSquadRolesSaveData ManagedRoles;
        public float ManagedBudget;
        public float ManagedTotalTransferSpend;
        public float ManagedTotalTransferIncome;
        public List<SeasonRecordSaveData> CareerHistory = new();
        public List<YouthPoolSaveData> YouthPools = new();
        public List<string> ScoutedPlayerIds = new();

        // Loan system (session 9) - a loaned-out player is removed from ManagedSquad
        // entirely (they're not on your bench, they're playing elsewhere), so without
        // this list, saving mid-loan would silently lose that player forever on the
        // next load - neither the squad save nor anywhere else would have them.
        // Destination flavor text isn't preserved (re-rolled fresh on load) - cosmetic
        // only, not worth the extra DTO fields.
        public List<PlayerAgentSaveData> LoanedOutPlayers = new();

        // Youth academy (session 9) - same reasoning as LoanedOutPlayers above: academy
        // prospects aren't in ManagedSquad/ManagedReservePool at all, so without this
        // list they'd be silently lost on save/load.
        public List<PlayerAgentSaveData> AcademyPool = new();
    }
}
