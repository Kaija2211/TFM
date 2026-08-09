using System.Collections.Generic;
using UnityEngine;
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

        // Condition/injuries (session 7, fitness phase) - deliberately stored here rather
        // than as new PlayerAgent fields, unlike Leadership/LongShots/etc. Those are inert
        // generated traits, safe as plain fields; this is actively mutated every matchday
        // by Manager Mode's own fixture loop, so keeping it out of the protected class
        // entirely (rather than merely "unread by Research Mode") is the more conservative
        // choice - there's no functional need for it to live there.
        private readonly Dictionary<PlayerAgent, float> condition = new();
        private readonly Dictionary<PlayerAgent, int> injuryReturnMatchday = new();

        public float GetCondition(PlayerAgent player)
        {
            return condition.TryGetValue(player, out float value) ? value : 100f;
        }

        // currentMatchdayIndex is exclusive of the return matchday - a player set to
        // return at matchday 5 is available to select starting at matchday 5.
        public bool IsInjured(PlayerAgent player, int currentMatchdayIndex)
        {
            return injuryReturnMatchday.TryGetValue(player, out int returnMatchday) && currentMatchdayIndex < returnMatchday;
        }

        public int GetInjuryReturnMatchday(PlayerAgent player)
        {
            return injuryReturnMatchday.TryGetValue(player, out int returnMatchday) ? returnMatchday : -1;
        }

        public void SetInjured(PlayerAgent player, int returnMatchday)
        {
            injuryReturnMatchday[player] = returnMatchday;
        }

        // One combined delta rather than separate recovery/fatigue passes - every player
        // gets some baseline recovery every matchday regardless of whether they played
        // (the body recovers some even during a match week), and playing subtracts a
        // fatigue cost on top of that. A high-Stamina player who plays nets out close to
        // flat; a low-Stamina player who plays repeatedly without rest genuinely craters.
        // Age affects recovery specifically (younger bounces back faster between
        // matches) - Stamina already owns the in-match fatigue side via
        // AgentMatchSimulator.GetFatigueMultiplier, so this doesn't duplicate that, it
        // extends the same idea across matchdays instead of just within one match.
        public void ApplyPostMatchCondition(PlayerAgent player, bool played, int age, float stamina)
        {
            float youthRecoveryFactor = Mathf.Clamp01((28f - age) / 12f);
            float recovery = 8f + youthRecoveryFactor * 6f;

            float delta = recovery;

            if (played)
            {
                float staminaFactor = Mathf.Clamp01((100f - stamina) / 100f);
                float fatigue = 12f + staminaFactor * 13f;
                delta -= fatigue;
            }

            condition[player] = Mathf.Clamp(GetCondition(player) + delta, 0f, 100f);
        }

        // 0.7 at Condition 0, 1.0 at Condition 100 - a badly fatigued player performs
        // meaningfully worse for their whole match (see ManagerFormationFit, which this
        // feeds into as an extra multiplier alongside position-fit), not literally broken.
        public float GetConditionMultiplier(PlayerAgent player)
        {
            return 0.7f + 0.3f * (GetCondition(player) / 100f);
        }
    }
}
