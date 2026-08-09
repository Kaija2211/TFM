using System.Collections.Generic;
using Sim;

namespace Manager
{
    public enum AttackDefendRole
    {
        Defensive,
        Balanced,
        Attacking
    }

    // Manager Mode-only designations (captaincy, set-piece takers, per-player attack/
    // defend leaning) layered on top of a squad without touching Sim.PlayerAgent, which
    // must stay byte-for-byte unchanged for Research Mode. Held as a side structure, one
    // per team, alongside squadsByTeamName in ManagerPrototypeController - PlayerAgent
    // instances are generated once per team name and reused for the rest of the play
    // session (see GetOrCreateAgentTeam), so direct PlayerAgent references here stay
    // valid across screens and substitutions exactly like squadsByTeamName itself.
    //
    // Organizational only for now, except CornerTaker - see the ManagerSim fork of
    // AgentMatchSimulator (PickCreatorForChance) for the one place this actually feeds
    // into match resolution. Captain/ViceCaptain/PenaltyTaker/FreeKickTaker/attack-defend
    // role are stored and displayed but don't change sim math yet - there's no distinct
    // free-kick/penalty event in the sim to hook into today.
    public class ManagerSquadRoles
    {
        public PlayerAgent Captain;
        public PlayerAgent ViceCaptain;
        public PlayerAgent PenaltyTaker;
        public PlayerAgent FreeKickTaker;
        public PlayerAgent CornerTaker;

        private readonly Dictionary<PlayerAgent, AttackDefendRole> attackDefendRoles = new();

        public AttackDefendRole GetRole(PlayerAgent player)
        {
            return attackDefendRoles.TryGetValue(player, out AttackDefendRole role) ? role : AttackDefendRole.Balanced;
        }

        public void SetRole(PlayerAgent player, AttackDefendRole role)
        {
            attackDefendRoles[player] = role;
        }
    }
}
