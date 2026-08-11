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
    // Organizational only for now, except LeftCornerTaker/RightCornerTaker - see the
    // ManagerSim fork of AgentMatchSimulator (PickCreatorForChance) for the one place
    // this actually feeds into match resolution. Captain/ViceCaptain/PenaltyTaker/
    // FreeKickTaker/attack-defend role are stored and displayed but don't change sim
    // math yet - there's no distinct free-kick/penalty event in the sim to hook into
    // today.
    //
    // Split into Left/Right corner taker (session 7, Tactics screen pass) rather than a
    // single CornerTaker - the sim has no actual concept of which side a corner comes
    // from, so this isn't true left/right modeling, but the match sim alternates 50/50
    // between whichever of the two is on the pitch (see AgentMatchSimulator.
    // CornerTakerNamesByTeamName), which is a genuinely better approximation than a
    // single designated taker and matches the real-football pattern of two corner
    // specialists rather than one.
    public class ManagerSquadRoles
    {
        public PlayerAgent Captain;
        public PlayerAgent ViceCaptain;
        public PlayerAgent PenaltyTaker;
        public PlayerAgent FreeKickTaker;
        public PlayerAgent LeftCornerTaker;
        public PlayerAgent RightCornerTaker;

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
        // minutesPlayed (0-90) rather than a binary played flag (session 10 fix -
        // Thomas: "what if you bench them, but still sub them on?"). The old bool
        // version judged everyone by final Starting XI membership only, so a substitute
        // brought on for the final 10 minutes took the exact same fatigue hit as a
        // 90-minute starter, while a starter subbed off after 70 minutes got treated as
        // if they'd never played at all (full rest-day recovery, zero fatigue) - the
        // same class of bug as the in-match GetFatigueMultiplier fix from session 9,
        // just in this separate matchday-to-matchday Condition tracker instead of the
        // live in-match one. minutesFraction blends linearly between the two existing
        // extremes (0 minutes = old "false" branch exactly, 90 minutes = old "true"
        // branch exactly), so this is a strict generalization, not a rebalance.
        public void ApplyPostMatchCondition(PlayerAgent player, float minutesPlayed, int age, float stamina)
        {
            float minutesFraction = Mathf.Clamp01(minutesPlayed / 90f);

            // Session 13 rebalance - Thomas: "Condition needs to recover way faster, I
            // cannot stop my players from getting injured as is." Root cause wasn't the
            // injury-chance formula itself (TryRollInjury's fatigueRisk term is 0 above
            // Condition 70 and caps at a real but sane +9% at Condition 0 - reasonably
            // tuned) - it was that the old linear recovery (+8 to +14/matchday) was far
            // smaller than the fatigue cost of actually playing (-12 to -25/matchday for
            // a full 90), so even a manager who DID rotate barely climbed out of the
            // danger zone (confirmed: reaching 100 from empty took 10-13 STRAIGHT rest
            // matchdays under the old formula). A genuinely unused bench week now fully
            // restores Condition - matches Thomas's own explicit ask and makes rotation
            // actually function as the escape valve it was always meant to be, while a
            // squad that's never rotated at all still wears down exactly as before (this
            // only changes what a real rest is worth, not what playing costs).
            if (minutesFraction <= 0f)
            {
                condition[player] = 100f;
                return;
            }

            float youthRecoveryFactor = Mathf.Clamp01((28f - age) / 12f);
            float recovery = 8f + youthRecoveryFactor * 6f;

            float staminaFactor = Mathf.Clamp01((100f - stamina) / 100f);
            float fullMatchFatigue = 12f + staminaFactor * 13f;

            // Goalkeepers barely move over 90 minutes compared to an outfield player and
            // are almost never rotated for fitness reasons in real football (Thomas,
            // session 13) - same reasoning applied here as the in-match fatigue system
            // already gives keepers implicitly via low positional workload, just made
            // explicit for this matchday-to-matchday tracker too.
            if (player.PrimaryPosition == PlayerPosition.GK)
            {
                fullMatchFatigue *= 0.35f;
            }

            float delta = recovery - fullMatchFatigue * minutesFraction;

            condition[player] = Mathf.Clamp(GetCondition(player) + delta, 0f, 100f);
        }

        // 0.7 at Condition 0, 1.0 at Condition 100 - a badly fatigued player performs
        // meaningfully worse for their whole match (see ManagerFormationFit, which this
        // feeds into as an extra multiplier alongside position-fit), not literally broken.
        public float GetConditionMultiplier(PlayerAgent player)
        {
            return 0.7f + 0.3f * (GetCondition(player) / 100f);
        }

        // Appearances this season (career-arc addition, Phase 1) - managed-team-only,
        // same limitation as Condition/injuries above (see ApplyMatchdayConditionAndInjuries,
        // which is the only place that calls RecordAppearance). Feeds a genuine playing-
        // time weighting into ManagerPlayerDevelopment.ApplySeasonProgression for the
        // one squad this data actually exists for.
        private readonly Dictionary<PlayerAgent, int> appearancesThisSeason = new();

        public int GetAppearancesThisSeason(PlayerAgent player)
        {
            return appearancesThisSeason.TryGetValue(player, out int count) ? count : 0;
        }

        public void RecordAppearance(PlayerAgent player)
        {
            appearancesThisSeason[player] = GetAppearancesThisSeason(player) + 1;
        }

        // Morale (session 10 - Thomas: doesn't affect match performance, affects
        // development instead). Same 0-100 shape as Condition, but a happy 70 default
        // rather than a maxed 100 - resets every season (Thomas: "assume the players
        // went off to a beach somewhere during the summer and are in good moods again"),
        // not a permanently-scarred-forever trait. 70 leaves headroom both directions:
        // a good season can push it up toward the 85-100 real-boost range, a bad one can
        // grind it down through neutral (50) into actual penalty territory.
        private const float DefaultMorale = 70f;
        private readonly Dictionary<PlayerAgent, float> morale = new();

        public float GetMorale(PlayerAgent player)
        {
            return morale.TryGetValue(player, out float value) ? value : DefaultMorale;
        }

        // Two cheap, already-available signals (same "no new tracking, reuse what a
        // match result already tells you" philosophy as ApplyMatchFormBonus) - playing
        // time and team result. A benched player drifts down a little regardless of
        // outcome (overlooked, not miserable); a player who actually featured swings
        // with the result - winning lifts morale more than a loss drags it down, and a
        // draw is a small net positive ("at least didn't lose") rather than neutral,
        // matching how a real dressing room reacts to a hard-fought point more warmly
        // than to inactivity.
        public void ApplyPostMatchMorale(PlayerAgent player, bool played, ManagerPlayerDevelopment.MatchFormOutcome outcome)
        {
            float delta;

            if (!played)
            {
                delta = -0.5f;
            }
            else
            {
                delta = outcome switch
                {
                    ManagerPlayerDevelopment.MatchFormOutcome.Win => 2f,
                    ManagerPlayerDevelopment.MatchFormOutcome.Loss => -2f,
                    _ => 0.3f
                };
            }

            morale[player] = Mathf.Clamp(GetMorale(player) + delta, 0f, 100f);
        }

        // 0.85x at Morale 0, 1.15x at Morale 100 - a happy player develops meaningfully
        // faster, a miserable one meaningfully slower, but neither extreme dominates the
        // existing playing-time-driven growth rate ApplyMatchdayProgression already
        // computes. Deliberately only plugged into the GROWTH side of development (see
        // ApplyMatchdayProgression's own moraleGrowthMultiplier parameter) - decline/
        // aging is a biological process, not a motivational one, so morale doesn't touch it.
        public float GetMoraleGrowthMultiplier(PlayerAgent player)
        {
            return 0.85f + (GetMorale(player) / 100f) * 0.3f;
        }

        // Season rollover (career-arc addition) - condition/injuries/appearances/morale
        // reset for the new season's fresh fixture list; Captain/ViceCaptain/set-piece
        // takers and the attack/defend leanings deliberately survive untouched, since
        // re-picking your captain every single season would be busywork, not a real
        // decision.
        public void ResetForNewSeason()
        {
            condition.Clear();
            injuryReturnMatchday.Clear();
            appearancesThisSeason.Clear();
            morale.Clear();
        }
    }
}
