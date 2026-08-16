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
        public int AttributeSchemaVersion;
        public string PlayerId;
        public string Name;
        public string Archetype;
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
        public float FirstTouch, Technique, Corners, Penalties;
        public float Anticipation, Decisions, Vision, DefensivePositioning, WorkRate, Aggression;
        public float Acceleration, Agility, Balance, JumpingReach;
        public float Handling, OneOnOnes, AerialCommand, Distribution, GoalkeeperPositioning;
        public float Potential;

        public static PlayerAgentSaveData FromPlayer(PlayerAgent p)
        {
            return new PlayerAgentSaveData
            {
                AttributeSchemaVersion = PlayerAttributeModel.CurrentSchemaVersion,
                PlayerId = p.PlayerId,
                Name = p.Name,
                Archetype = p.Archetype,
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
                FirstTouch = p.FirstTouch,
                Technique = p.Technique,
                Corners = p.Corners,
                Penalties = p.Penalties,
                Anticipation = p.Anticipation,
                Decisions = p.Decisions,
                Vision = p.Vision,
                DefensivePositioning = p.DefensivePositioning,
                WorkRate = p.WorkRate,
                Aggression = p.Aggression,
                Acceleration = p.Acceleration,
                Agility = p.Agility,
                Balance = p.Balance,
                JumpingReach = p.JumpingReach,
                Handling = p.Handling,
                OneOnOnes = p.OneOnOnes,
                AerialCommand = p.AerialCommand,
                Distribution = p.Distribution,
                GoalkeeperPositioning = p.GoalkeeperPositioning,
                Potential = p.Potential
            };
        }

        public PlayerAgent ToPlayer()
        {
            PlayerAgent p = new PlayerAgent(Name, Role, PrimaryPosition)
            {
                PlayerId = PlayerId,
                Archetype = Archetype,
                AttributeSchemaVersion = AttributeSchemaVersion,
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
                FirstTouch = FirstTouch,
                Technique = Technique,
                Corners = Corners,
                Penalties = Penalties,
                Anticipation = Anticipation,
                Decisions = Decisions,
                Vision = Vision,
                DefensivePositioning = DefensivePositioning,
                WorkRate = WorkRate,
                Aggression = Aggression,
                Acceleration = Acceleration,
                Agility = Agility,
                Balance = Balance,
                JumpingReach = JumpingReach,
                Handling = Handling,
                OneOnOnes = OneOnOnes,
                AerialCommand = AerialCommand,
                Distribution = Distribution,
                GoalkeeperPositioning = GoalkeeperPositioning,
                Potential = Potential
            };

            PlayerAttributeModel.EnsureCurrent(p);

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
        public List<PlayerAgentSaveData> Reserves = new();

        public static AgentTeamSaveData FromTeam(AgentTeam team)
        {
            AgentTeamSaveData data = new AgentTeamSaveData { TeamName = team.TeamName, Formation = team.Formation };

            foreach (PlayerAgent p in team.StartingEleven) data.StartingEleven.Add(PlayerAgentSaveData.FromPlayer(p));
            foreach (PlayerAgent p in team.Bench) data.Bench.Add(PlayerAgentSaveData.FromPlayer(p));
            foreach (PlayerAgent p in team.Reserves) data.Reserves.Add(PlayerAgentSaveData.FromPlayer(p));

            return data;
        }

        public AgentTeam ToTeam()
        {
            AgentTeam team = new AgentTeam(TeamName, Formation);

            foreach (PlayerAgentSaveData dto in StartingEleven) team.AddStarter(dto.ToPlayer());
            foreach (PlayerAgentSaveData dto in Bench) team.AddBenchPlayer(dto.ToPlayer());
            foreach (PlayerAgentSaveData dto in Reserves) team.AddReservePlayer(dto.ToPlayer());

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

    // Academy slots (session 13 rework) - explicit IsEmpty flag rather than a raw
    // nullable list entry, since JsonUtility doesn't reliably round-trip a literal null
    // inside a List<T> of a reference type. Positional (index in the outer list ==
    // slot index), so an empty slot restores to the same place it was released from.
    [Serializable]
    public class AcademySlotSaveData
    {
        public bool IsEmpty;
        public PlayerAgentSaveData Prospect;
    }

    // Youth scouting missions (session 13 mission rework) - two fixed slots
    // (ManagerScouting.ScoutSlots), each briefed with up to 3 target positions.
    [Serializable]
    public class ScoutMissionSaveData
    {
        public List<PlayerPosition> TargetPositions = new();
        public int DaysWithoutDiscovery;
    }

    // A discovered-but-unclaimed prospect, paired with the matchday it was found on so
    // the poach-timer (ManagerScouting.MatchdaysUntilPoached) can resume correctly
    // after a load instead of silently resetting everyone's countdown.
    [Serializable]
    public class DiscoveredProspectSaveData
    {
        public PlayerAgentSaveData Prospect;
        public int DiscoveredMatchday;
    }

    // Inbox (session 13) - lives here, not in ManagerInbox.cs (namespace Manager), so
    // Manager.Save never has to depend upward on Manager (same one-directional
    // layering every other enum/DTO in this file already follows, e.g. PlayerRole/
    // PlayerPosition/Formation living in Sim rather than Manager). Deliberately just
    // Title/Body strings, not a live PlayerAgent reference - see InboxMessage's own
    // comment in ManagerInbox.cs for why a message is a baked snapshot, not a live view.
    // Session 14 additions (potentialemails.txt Tier 1 batch + playtest-requested
    // injury/recovery messages + retirement announcement) - all purely a display tag
    // today, same as the original three (see RefreshInboxUI/BuildInboxMessageRow in
    // ManagerPrototypeController, which don't branch on Type at all yet).
    public enum InboxMessageType
    {
        ScoutingReport, BidAccepted, BidDeclined, TransferOffer,
        WelcomeCareer, SeasonExpectations, RecruitmentTeaser,
        PostMatchReaction, FormStreak, MidSeasonReview, EndOfSeason,
        LowStamina, Injury, Recovery, Retirement
    }

    [Serializable]
    public class InboxMessageSaveData
    {
        public InboxMessageType Type;
        public string Title;
        public string Body;
        public int MatchdayReceived;
        public bool IsRead;
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
    public class OutgoingTransferSaveData
    {
        public string PlayerId;
        public int ListedDay;
        public bool HasOffer;
        public float OfferAmount;
    }

    [Serializable]
    public class ManagerTacticsSaveData
    {
        public WidthSetting Width = WidthSetting.Balanced;
        public DefensiveDepthSetting DefensiveDepth = DefensiveDepthSetting.Balanced;
        public TempoSetting Tempo = TempoSetting.Balanced;
    }

    [Serializable]
    public class TeamFormSaveData
    {
        public int TeamId;
        // Oldest to newest, containing at most five W/D/L characters.
        public string Results;
    }

    [Serializable]
    public class ManagerSaveData
    {
        public int SaveVersion = 5;
        // False for every pre-world-generation save because missing JSON boolean
        // fields deserialize to false. New careers opt in explicitly, preserving the
        // legacy squad/strength bootstrap for existing saves.
        public bool UsesWorldGeneration;

        // Multi-save support (session 15) - SaveId is a GUID generated once when a
        // career starts and never changes for that career's lifetime; it's the actual
        // filename ManagerSaveService writes to (career_{SaveId}.json), so every save
        // during a session overwrites the same file instead of creating a new one each
        // time. SaveName is purely the player-facing label shown in the Load Career
        // browser - deliberately separate from ManagerName/ManagedTeamName so a player
        // can tell two careers as the same club apart ("Rebuild Job" vs "Take the Prem
        // by Storm"). LastSavedUtc (ISO 8601, via DateTime.ToString("o")) is what the
        // Continue button and the browser's sort both key off - an ordinal string
        // compare on that format sorts chronologically without needing to parse it back
        // into a DateTime first.
        public string SaveId;
        public string SaveName;
        public string LastSavedUtc;

        public string ManagerName;
        public string ManagedTeamName;
        public int CurrentSeason;
        public int CurrentFixtureIndex;
        public string CurrentCareerDate;
        public int SeasonStartYear;
        public string ActiveSeasonFileName;

        public List<LeagueTableEntrySaveData> TableEntries = new();
        public List<TeamFormSaveData> RecentForm = new();
        public AgentTeamSaveData ManagedSquad;
        public List<PlayerAgentSaveData> ManagedReservePool = new();
        public ManagerSquadRolesSaveData ManagedRoles;
        // HasManagedTactics distinguishes legacy saves (where JsonUtility supplies an
        // empty/default DTO) from an intentional Narrow/Deep/Slow setup.
        public bool HasManagedTactics;
        public ManagerTacticsSaveData ManagedTactics;
        public float ManagedBudget;
        public float ManagedTotalTransferSpend;
        public float ManagedTotalTransferIncome;
        public List<SeasonRecordSaveData> CareerHistory = new();

        // Youth scouting missions + discoveries (session 13 rework) - replaces the old
        // region-keyed YouthPools/ScoutedPlayerIds entirely (no fixed pool exists
        // anymore to save). ScoutMissions is always exactly ManagerScouting.ScoutSlots
        // long, positional by slot index, same convention as AcademySlots below.
        public List<ScoutMissionSaveData> ScoutMissions = new();
        public List<DiscoveredProspectSaveData> DiscoveredProspects = new();

        // Loan system (session 9) - a loaned-out player is removed from ManagedSquad
        // entirely (they're not on your bench, they're playing elsewhere), so without
        // this list, saving mid-loan would silently lose that player forever on the
        // next load - neither the squad save nor anywhere else would have them.
        // Destination flavor text isn't preserved (re-rolled fresh on load) - cosmetic
        // only, not worth the extra DTO fields.
        public List<PlayerAgentSaveData> LoanedOutPlayers = new();

        // Youth academy (session 9; empty-slot rework session 13) - academy prospects
        // aren't in ManagedSquad/ManagedReservePool at all, so without this list they'd
        // be silently lost on save/load. Always exactly ManagerAcademy.AcademySlots
        // long, positional (see AcademySlotSaveData).
        public List<AcademySlotSaveData> AcademySlots = new();

        // Transfer negotiation + Inbox (session 13). InboxMessages round-trips fully -
        // every entry is a resolved, action-free historical record (see
        // ManagerInbox.BuildSaveList). Pending/awaiting-signature bids do NOT round-trip
        // by reference - both an AI club's own squad players AND the exact PlayerAgent
        // objects behind them regenerate fresh every session (same limitation
        // ManagedReservePool's own class comment already documents for AI squads more
        // generally), so there's no stable object to resume a live negotiation against.
        // Rather than silently losing the escrowed money, PendingBidRefundOnLoad carries
        // the total £m still committed to in-flight bids at save time, credited straight
        // back to ManagedBudget on load - the negotiations themselves are simply dropped
        // and would need to be started over.
        public List<InboxMessageSaveData> InboxMessages = new();
        public float PendingBidRefundOnLoad;
        public List<OutgoingTransferSaveData> OutgoingTransfers = new();
    }
}
