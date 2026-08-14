using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Manager Mode's own fork of Sim.AgentMatchSimulator (Assets/Scripts/Sim/
    // AgentMatchSimulator.cs) - a byte-for-byte copy at the point this file was created,
    // free to diverge from here on. Exists so Manager Mode can get real new match-sim
    // mechanics (e.g. an actual on/off-target shot distinction) without ever touching
    // the original, which Research Mode's ResearchEvaluationRunner depends on staying
    // byte-for-byte unchanged for the Statistical-vs-ABM comparison.
    //
    // Deliberately kept in the `Manager` namespace (not a new `ManagerSim` namespace) -
    // ManagerPrototypeController.cs is itself declared `namespace Manager` and already
    // has `using Sim;`, so C#'s same-namespace type resolution makes this class shadow
    // Sim.AgentMatchSimulator there automatically, without needing to touch a single
    // `AgentMatchSimulator.X` reference in that file. ResearchEvaluationRunner.cs lives
    // in `namespace Data` with no `using Manager;`, so it's completely unaffected and
    // keeps resolving to the real Sim.AgentMatchSimulator as before.
    //
    // Whenever this diverges from the original in a way worth knowing about, note it
    // here rather than relying on a diff - the original can never carry a comment
    // pointing back at this fork, since "byte-for-byte unchanged" means literally that.
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

        // Manager Mode-only: team name -> (left, right) designated corner takers' names
        // (see ManagerSquadRoles), set by ManagerPrototypeController before each
        // SimulateMatch call. Keyed and matched by name rather than PlayerAgent reference
        // because the team actually handed to SimulateMatch is a throwaway fit-adjusted
        // clone (see ManagerFormationFit) with all-new PlayerAgent instances - Name is the
        // one thing ClonePenalized copies through unchanged. Not true left/right modeling
        // - there's no concept of which side a corner comes from in this sim - just an
        // alternation between two designated takers (see PickCreatorForChance) rather than
        // one, matching the real-football pattern of two corner specialists. Empty/unset
        // by default, so a team with neither corner taker assigned takes the exact
        // original weighted-random pick with zero extra Random calls.
        public readonly Dictionary<string, (string Left, string Right)> CornerTakerNamesByTeamName = new();

        // Manager Mode-only: tracks which minute each substitute actually entered the
        // match (session 9 bug fix - see feedback in HANDOFF). Without this,
        // GetFatigueMultiplier judged every player purely by the absolute match clock,
        // so a substitute brought on at minute 88 was fatigued identically to the
        // starter they replaced instead of getting the "fresh legs" benefit that's the
        // entire point of making the change - confirmed live (yellow low-stamina border
        // persisted on the incoming sub, not just cosmetically - the same multiplier
        // feeds the actual chance-creation math below). Keyed by PlayerAgent reference,
        // safe here specifically because SimulateFromMinute (the only path that can ever
        // populate this, via a genuine mid-match substitution) always runs against the
        // real GetOrCreateAgentTeam instances, never the throwaway fit-adjusted clones
        // SimulateFixture's very first full-match call uses - by the time any
        // substitution could exist, resimulation has already moved to the real
        // instances. Cleared once per match (see ClearSubstitutions, called from
        // ManagerPrototypeController.SimulateFixture) so a player subbed on late in one
        // match doesn't carry a stale entry minute into their next start.
        private readonly Dictionary<PlayerAgent, int> substituteEntryMinute = new();

        public void ClearSubstitutions()
        {
            substituteEntryMinute.Clear();
        }

        public void RegisterSubstitution(PlayerAgent player, int entryMinute)
        {
            substituteEntryMinute[player] = entryMinute;
        }

        public class AgentMatchEvent
        {
            public int Minute;
            public string Description;
            public bool IsGoal;
            public bool HomeTeamScored;
            public bool IsShot;

            // True only on a goal or a save - i.e. the shot actually reached/beat the
            // keeper. False (default) on a stopped-before-shot event and on a genuine
            // off-target attempt (see ResolveAttack's on-target roll) - fork-only
            // addition, doesn't exist in the protected Sim.AgentMatchSimulator this was
            // copied from. Lets Manager Mode show a real Shots-on-Target number instead
            // of it just being a copy of Shots.
            public bool IsOnTarget;

            // Which side is attacking - set on every event now (stopped/off-target/saved/
            // goal alike), not just goals, so Manager Mode can derive possession share
            // and "chances created" per team straight from the event list without any
            // separate running counters to keep in sync across a mid-match resimulation.
            public bool HomeTeamAttacking;

            // The scoring player's name, set only on goal events (from `shooter` at the
            // exact point IsGoal is set - the actual scorer, not parsed from Description).
            // Purely additive, same as HomeTeamAttacking above.
            public string ScorerName;

            // Live match ratings (session 10) - ResolveAttack already picks a creator/
            // shooter/defender/goalkeeper for every chance to compute chanceCreation/
            // defensiveResistance and build the event's Description text, then used to
            // just throw those identities away. These four fields preserve them on the
            // event instead, so ManagerMatchRatings can attribute a rating swing to the
            // right player without re-deriving anything from free text. CreatorName/
            // DefenderName/GoalkeeperName are set on every event (those three roles are
            // always resolved before any early-return in ResolveAttack); ShooterName is
            // only set once a shot is actually attempted (not on a chance stopped before
            // the shooter ever got to shoot) - see ResolveAttack for exactly which of the
            // four add-sites sets which.
            public string CreatorName;
            public string ShooterName;
            public string DefenderName;
            public string GoalkeeperName;
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

                ScoreStateModifier homeMentality = GetScoreStateModifier(
     result.HomeGoals,
     result.AwayGoals,
     minute
 );

                ScoreStateModifier awayMentality = GetScoreStateModifier(
                    result.AwayGoals,
                    result.HomeGoals,
                    minute
                );

                float adjustedHomeAttackWeight =
                    homeAttackChance * homeMentality.AttackShareMultiplier;

                float adjustedAwayAttackWeight =
                    (1f - homeAttackChance) * awayMentality.AttackShareMultiplier;

                float adjustedHomeAttackChance =
                    adjustedHomeAttackWeight /
                    Mathf.Max(0.01f, adjustedHomeAttackWeight + adjustedAwayAttackWeight);

                adjustedHomeAttackChance = Mathf.Clamp(
                    adjustedHomeAttackChance,
                    0.30f,
                    0.70f
                );

                bool homeAttacks = Random.value < adjustedHomeAttackChance;

                int goalDifference = result.HomeGoals - result.AwayGoals;

                // If a team is already well ahead, reduce low-value extra attacks.
                // This keeps extreme scorelines under control.
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

                ScoreStateModifier attackingMentality = homeAttacks
                    ? homeMentality
                    : awayMentality;

                ScoreStateModifier defendingMentality = homeAttacks
                    ? awayMentality
                    : homeMentality;

                float attackingExpectedGoals = homeAttacks
                    ? expectedHomeGoals
                    : expectedAwayGoals;

                float adjustedAttackingExpectedGoals =
                    attackingExpectedGoals * attackingMentality.AttackQualityMultiplier;

                ResolveAttack(
                    minute,
                    attackingTeam,
                    defendingTeam,
                    homeAttacks,
                    adjustedAttackingExpectedGoals,
                    defendingMentality.DefensiveMultiplier,
                    result
                );
            }

            return result;
        }

        // Manager Mode only: mirrors SimulateMatch's own loop exactly, but resuming from
        // a given minute/score instead of kickoff. Added so an in-match substitution can
        // genuinely change the remainder of the match (new XI feeds PickShooterForChance
        // etc. from here on) without touching SimulateMatch above, which Research Mode's
        // ResearchEvaluationRunner also calls and which must stay byte-for-byte unchanged.
        public AgentMatchResult SimulateFromMinute(
            AgentTeam homeTeam,
            AgentTeam awayTeam,
            float expectedHomeGoals,
            float expectedAwayGoals,
            int startMinute,
            int startHomeGoals,
            int startAwayGoals)
        {
            AgentMatchResult result = new AgentMatchResult
            {
                HomeTeamName = homeTeam.TeamName,
                AwayTeamName = awayTeam.TeamName,
                HomeGoals = startHomeGoals,
                AwayGoals = startAwayGoals
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

            for (int minute = Mathf.Max(1, startMinute); minute <= 90; minute++)
            {
                if (Random.value > eventChancePerMinute)
                {
                    continue;
                }

                ScoreStateModifier homeMentality = GetScoreStateModifier(
                    result.HomeGoals,
                    result.AwayGoals,
                    minute
                );

                ScoreStateModifier awayMentality = GetScoreStateModifier(
                    result.AwayGoals,
                    result.HomeGoals,
                    minute
                );

                float adjustedHomeAttackWeight =
                    homeAttackChance * homeMentality.AttackShareMultiplier;

                float adjustedAwayAttackWeight =
                    (1f - homeAttackChance) * awayMentality.AttackShareMultiplier;

                float adjustedHomeAttackChance =
                    adjustedHomeAttackWeight /
                    Mathf.Max(0.01f, adjustedHomeAttackWeight + adjustedAwayAttackWeight);

                adjustedHomeAttackChance = Mathf.Clamp(
                    adjustedHomeAttackChance,
                    0.30f,
                    0.70f
                );

                bool homeAttacks = Random.value < adjustedHomeAttackChance;

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

                ScoreStateModifier attackingMentality = homeAttacks
                    ? homeMentality
                    : awayMentality;

                ScoreStateModifier defendingMentality = homeAttacks
                    ? awayMentality
                    : homeMentality;

                float attackingExpectedGoals = homeAttacks
                    ? expectedHomeGoals
                    : expectedAwayGoals;

                float adjustedAttackingExpectedGoals =
                    attackingExpectedGoals * attackingMentality.AttackQualityMultiplier;

                ResolveAttack(
                    minute,
                    attackingTeam,
                    defendingTeam,
                    homeAttacks,
                    adjustedAttackingExpectedGoals,
                    defendingMentality.DefensiveMultiplier,
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
    float defendingMentalityMultiplier,
    AgentMatchResult result)
        {
            Dictionary<ChanceType, float> chanceTypeBias = BuildChanceTypeBias(attackingTeam.TeamName, defendingTeam.TeamName);
            ChanceType chanceType = PickChanceType(attackingExpectedGoals, chanceTypeBias);

            PlayerAgent creator = PickCreatorForChance(attackingTeam, chanceType);
            PlayerAgent shooter = PickShooterForChance(attackingTeam, chanceType, creator);
            PlayerAgent defender = PickDefenderForChance(defendingTeam, chanceType);
            PlayerAgent goalkeeper = PickGoalkeeper(defendingTeam);

            if (creator == null || shooter == null || defender == null || goalkeeper == null)
            {
                return;
            }

            float creatorFatigue = GetFatigueMultiplier(creator, minute);
            float shooterFatigue = GetFatigueMultiplier(shooter, minute);
            float defenderFatigue = GetFatigueMultiplier(defender, minute);
            float goalkeeperFatigue = GetFatigueMultiplier(goalkeeper, minute);

            float chanceCreation =
    GetChanceCreationScore(creator, shooter, chanceType) *
    ((creatorFatigue + shooterFatigue) / 2f);

            float defensiveResistance =
    GetDefensiveResistanceScore(defender, goalkeeper, chanceType) *
    ((defenderFatigue + goalkeeperFatigue) / 2f) *
    defendingMentalityMultiplier;

            float chanceScore = chanceCreation - defensiveResistance;

            float shotChance = Mathf.Clamp(
                // Attribute specialists now create a wider gap between a well-built
                // tactical matchup and a poor one. A lower baseline plus stronger
                // player-v-player differential preserves total shot volume while making
                // recruitment and tactical fit matter to who earns those shots.
                0.283f + chanceScore / 68f,
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
                        chanceType,
                        creatorFatigue
                    ),
                    HomeTeamAttacking = homeAttacks,
                    CreatorName = creator.Name,
                    DefenderName = defender.Name,
                    GoalkeeperName = goalkeeper.Name
                });

                return;
            }

            // Fork-only addition: a genuine on/off-target split for shots that get this
            // far, so "Shots on Target" is a real, separately-tracked number instead of
            // being identical to "Shots" (the protected original has no such concept -
            // every shot either scores or gets saved, both of which are inherently on
            // target). Baseline centred a little under real-world ~35-40% on-target
            // rates, nudged by the shooter's own composure/finishing rather than being a
            // flat coin flip.
            float onTargetChance = Mathf.Clamp(
                0.38f + ((shooter.Finishing + shooter.Composure) / 2f - 55f) / 300f,
                0.22f,
                0.62f
            );

            if (Random.value > onTargetChance)
            {
                result.Events.Add(new AgentMatchEvent
                {
                    Minute = minute,
                    Description = BuildOffTargetEventText(
                        attackingTeam,
                        shooter,
                        chanceType,
                        shooterFatigue
                    ),
                    IsShot = true,
                    HomeTeamAttacking = homeAttacks,
                    CreatorName = creator.Name,
                    ShooterName = shooter.Name,
                    DefenderName = defender.Name,
                    GoalkeeperName = goalkeeper.Name
                });

                return;
            }

            float goalQuality =
    GetGoalQualityScore(creator, shooter, chanceType) *
    ((creatorFatigue + shooterFatigue) / 2f);

            float saveQuality =
                GetSaveQualityScore(goalkeeper, defender, chanceType) *
                ((goalkeeperFatigue + defenderFatigue) / 2f) *
                defendingMentalityMultiplier;

            // This is the exact formula/clamp range the protected original uses for
            // "chance of scoring given any shot" - kept identical so it stays a faithful
            // baseline, not a second place to accidentally drift from Research Mode's
            // calibration.
            float unconditionalGoalChance = Mathf.Clamp(
                // Richer v2 attributes create more pronounced specialists than the flat
                // legacy profiles. Recalibrated intercept keeps the same player-v-player
                // differential while restoring league scoring to its historical band.
                0.111f + (goalQuality - saveQuality) / 220f,
                0.08f,
                0.63f
            );

            // Rescaled from "given any shot" to "given the shot is already on target" -
            // the on-target gate above is a fork-only addition (the protected original has
            // no such split) that would otherwise silently stack a second filter in front
            // of the same goal roll, roughly halving the overall goals/match rate.
            // Confirmed via a 200-match same-teams batch: 2.82 goals/match in the protected
            // original vs 1.21 in this fork before this fix - almost exactly explained by
            // onTargetChance's own typical range. Dividing by onTargetChance restores the
            // original's overall per-shot scoring rate exactly, since
            // P(goal | shot) = onTargetChance * (unconditionalGoalChance / onTargetChance)
            // = unconditionalGoalChance - while the 0.85 ceiling still leaves room for a
            // save even on a near-certain on-target effort.
            float goalChance = Mathf.Clamp(unconditionalGoalChance / onTargetChance, 0.08f, 0.85f);

            if (Random.value < goalChance)
            {
                // Real score-state check, taken before this goal is applied to the score -
                // "dramatic" only when the scoring team was level or behind and it's 80'+,
                // not just "any goal that happens to land late."
                bool isDramaticLateGoal = minute >= 80 &&
                    (homeAttacks ? result.HomeGoals - result.AwayGoals <= 0 : result.AwayGoals - result.HomeGoals <= 0);

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
                        chanceType,
                        isDramaticLateGoal
                    ),
                    IsGoal = true,
                    HomeTeamScored = homeAttacks,
                    IsShot = true,
                    IsOnTarget = true,
                    HomeTeamAttacking = homeAttacks,
                    ScorerName = shooter.Name,
                    CreatorName = creator.Name,
                    ShooterName = shooter.Name,
                    DefenderName = defender.Name,
                    GoalkeeperName = goalkeeper.Name
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
                    ),
                    IsShot = true,
                    IsOnTarget = true,
                    HomeTeamAttacking = homeAttacks,
                    CreatorName = creator.Name,
                    ShooterName = shooter.Name,
                    DefenderName = defender.Name,
                    GoalkeeperName = goalkeeper.Name
                });
            }
        }

        // Session 7 (tactical sliders) - the six fixed if/else thresholds this used to be
        // (still visible in git history) are now an explicit weighted table so
        // ManagerTacticalSliders can multiplicatively bias individual chance types
        // instead of just shifting the overall xG a match is built from (what Mentality
        // already does). With biasMultipliers null, this reproduces the exact original
        // odds: the same six weights summing to 1.0 per bracket, one Random.value draw
        // scaled by the (now 1.0) total weight - same call count, same distribution.
        // Iteration order differs from the original if/else chain, which reassigns which
        // specific roll *value* maps to which outcome - statistically inert, since the
        // probability mass per outcome (which is what goals/match, BTTS%, etc. actually
        // depend on) is unchanged and this isn't seeded for reproducibility the way
        // AgentSquadGenerator is.
        private static readonly ChanceType[] AllChanceTypes =
        {
            ChanceType.ThroughBall, ChanceType.Cross, ChanceType.Dribble,
            ChanceType.LongShot, ChanceType.SetPiece, ChanceType.CounterAttack
        };

        private static Dictionary<ChanceType, float> GetBaseChanceTypeWeights(float attackingExpectedGoals)
        {
            if (attackingExpectedGoals >= 2.0f)
            {
                return new Dictionary<ChanceType, float>
                {
                    [ChanceType.ThroughBall] = 0.28f,
                    [ChanceType.Cross] = 0.22f,
                    [ChanceType.Dribble] = 0.18f,
                    [ChanceType.CounterAttack] = 0.12f,
                    [ChanceType.LongShot] = 0.12f,
                    [ChanceType.SetPiece] = 0.08f
                };
            }

            if (attackingExpectedGoals <= 1.0f)
            {
                return new Dictionary<ChanceType, float>
                {
                    [ChanceType.CounterAttack] = 0.20f,
                    [ChanceType.Cross] = 0.18f,
                    [ChanceType.SetPiece] = 0.17f,
                    [ChanceType.LongShot] = 0.17f,
                    [ChanceType.ThroughBall] = 0.16f,
                    [ChanceType.Dribble] = 0.12f
                };
            }

            return new Dictionary<ChanceType, float>
            {
                [ChanceType.ThroughBall] = 0.24f,
                [ChanceType.Cross] = 0.21f,
                [ChanceType.Dribble] = 0.17f,
                [ChanceType.CounterAttack] = 0.15f,
                [ChanceType.LongShot] = 0.13f,
                [ChanceType.SetPiece] = 0.10f
            };
        }

        private ChanceType PickChanceType(float attackingExpectedGoals, Dictionary<ChanceType, float> biasMultipliers = null)
        {
            Dictionary<ChanceType, float> weights = GetBaseChanceTypeWeights(attackingExpectedGoals);

            if (biasMultipliers != null)
            {
                foreach (KeyValuePair<ChanceType, float> entry in biasMultipliers)
                {
                    if (weights.ContainsKey(entry.Key))
                    {
                        weights[entry.Key] *= entry.Value;
                    }
                }
            }

            float totalWeight = 0f;

            foreach (float weight in weights.Values)
            {
                totalWeight += weight;
            }

            float roll = Random.value * totalWeight;
            float cumulative = 0f;

            foreach (ChanceType type in AllChanceTypes)
            {
                if (!weights.TryGetValue(type, out float weight))
                {
                    continue;
                }

                cumulative += weight;

                if (roll < cumulative)
                {
                    return type;
                }
            }

            return ChanceType.SetPiece;
        }

        // Manager Mode-only: the managed team's current tactical slider settings, set by
        // ManagerPrototypeController before each SimulateMatch call - persists through any
        // later mid-match resimulation (subs, mentality changes) within the same match,
        // same lifetime as CornerTakerNamesByTeamName. Null/unset means every team plays
        // with the original, unbiased odds - a pure no-op for every AI opponent, which
        // never gets slider settings at all.
        public ManagerTacticalSliders ManagedTeamTacticalSliders;
        public string ManagedTeamName;

        private Dictionary<ChanceType, float> BuildChanceTypeBias(string attackingTeamName, string defendingTeamName)
        {
            if (ManagedTeamTacticalSliders == null || ManagedTeamName == null)
            {
                return null;
            }

            Dictionary<ChanceType, float> bias = null;

            if (attackingTeamName == ManagedTeamName)
            {
                bias = new Dictionary<ChanceType, float>();
                ApplyTempoBias(bias, ManagedTeamTacticalSliders.Tempo);
                ApplyWidthBias(bias, ManagedTeamTacticalSliders.Width);
            }

            if (defendingTeamName == ManagedTeamName)
            {
                if (bias == null)
                {
                    bias = new Dictionary<ChanceType, float>();
                }

                ApplyDefensiveDepthBias(bias, ManagedTeamTacticalSliders.DefensiveDepth);
            }

            return bias;
        }

        // Fast favors quick transitions (CounterAttack/Dribble) over patient buildup
        // (ThroughBall/SetPiece); Slow is the mirror image.
        private static void ApplyTempoBias(Dictionary<ChanceType, float> bias, TempoSetting tempo)
        {
            switch (tempo)
            {
                case TempoSetting.Fast:
                    MultiplyBias(bias, ChanceType.CounterAttack, 1.4f);
                    MultiplyBias(bias, ChanceType.Dribble, 1.25f);
                    MultiplyBias(bias, ChanceType.ThroughBall, 0.85f);
                    MultiplyBias(bias, ChanceType.SetPiece, 0.85f);
                    break;

                case TempoSetting.Slow:
                    MultiplyBias(bias, ChanceType.ThroughBall, 1.3f);
                    MultiplyBias(bias, ChanceType.SetPiece, 1.2f);
                    MultiplyBias(bias, ChanceType.CounterAttack, 0.7f);
                    MultiplyBias(bias, ChanceType.Dribble, 0.85f);
                    break;
            }
        }

        // Wide favors crosses/set pieces over central play (ThroughBall/Dribble); Narrow
        // is the mirror image.
        private static void ApplyWidthBias(Dictionary<ChanceType, float> bias, WidthSetting width)
        {
            switch (width)
            {
                case WidthSetting.Wide:
                    MultiplyBias(bias, ChanceType.Cross, 1.4f);
                    MultiplyBias(bias, ChanceType.SetPiece, 1.15f);
                    MultiplyBias(bias, ChanceType.ThroughBall, 0.8f);
                    MultiplyBias(bias, ChanceType.Dribble, 0.85f);
                    break;

                case WidthSetting.Narrow:
                    MultiplyBias(bias, ChanceType.ThroughBall, 1.3f);
                    MultiplyBias(bias, ChanceType.Dribble, 1.2f);
                    MultiplyBias(bias, ChanceType.Cross, 0.7f);
                    MultiplyBias(bias, ChanceType.SetPiece, 0.9f);
                    break;
            }
        }

        // Applied to the OPPONENT's chance-type mix when they attack the managed team -
        // a High line gives them more room in behind (CounterAttack) but less time to
        // pick a spot from range (LongShot); Deep is the mirror image.
        private static void ApplyDefensiveDepthBias(Dictionary<ChanceType, float> bias, DefensiveDepthSetting depth)
        {
            switch (depth)
            {
                case DefensiveDepthSetting.High:
                    MultiplyBias(bias, ChanceType.CounterAttack, 1.35f);
                    MultiplyBias(bias, ChanceType.LongShot, 0.8f);
                    break;

                case DefensiveDepthSetting.Deep:
                    MultiplyBias(bias, ChanceType.LongShot, 1.3f);
                    MultiplyBias(bias, ChanceType.CounterAttack, 0.7f);
                    break;
            }
        }

        private static void MultiplyBias(Dictionary<ChanceType, float> bias, ChanceType type, float factor)
        {
            bias[type] = bias.TryGetValue(type, out float existing) ? existing * factor : factor;
        }

        private float GetChanceCreationScore(
            PlayerAgent creator,
            PlayerAgent shooter,
            ChanceType chanceType)
        {
            switch (chanceType)
            {
                case ChanceType.ThroughBall:
                    // Passing's original 0.35 split with ThroughBalls (session 7) - the
                    // dedicated vision/incisive-passing stat, more specific than generic
                    // Passing for exactly this chance type. Still sums to 1.0.
                    return
                        creator.Passing * 0.20f + creator.Vision * 0.25f +
                        creator.Decisions * 0.15f + creator.Technique * 0.10f +
                        shooter.OffTheBall * 0.15f + shooter.Anticipation * 0.10f +
                        shooter.Acceleration * 0.05f;

                case ChanceType.Cross:
                    return
                        creator.Crossing * 0.32f + creator.Technique * 0.10f +
                        creator.Decisions * 0.08f + creator.Pace * 0.10f +
                        shooter.Heading * 0.22f + shooter.JumpingReach * 0.18f;

                case ChanceType.Dribble:
                    return
                        creator.Dribbling * 0.30f + creator.Agility * 0.18f +
                        creator.Acceleration * 0.17f + creator.Technique * 0.12f +
                        creator.Decisions * 0.08f + shooter.Finishing * 0.15f;

                case ChanceType.LongShot:
                    // Finishing's original 0.30 split with LongShots (session 7) - the
                    // dedicated shooting-from-distance stat. Still sums to 1.0.
                    return
                        shooter.LongShots * 0.28f + shooter.Technique * 0.16f +
                        shooter.Composure * 0.20f + shooter.Decisions * 0.10f +
                        creator.Passing * 0.10f + creator.Vision * 0.16f;

                case ChanceType.SetPiece:
                    return
                        creator.FreeKicks * 0.18f + creator.Corners * 0.17f +
                        creator.Crossing * 0.15f + creator.Technique * 0.10f +
                        shooter.Heading * 0.20f + shooter.JumpingReach * 0.20f;

                case ChanceType.CounterAttack:
                    return
                        creator.Passing * 0.16f + creator.Decisions * 0.12f +
                        creator.Acceleration * 0.17f + shooter.Acceleration * 0.20f +
                        shooter.OffTheBall * 0.15f + shooter.Finishing * 0.20f;

                default:
                    return creator.Vision * 0.35f + creator.Decisions * 0.25f + shooter.Finishing * 0.40f;
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
                    // Defending's original 0.35 split with Marking (session 7) - the
                    // dedicated positional-discipline stat. Still sums to 1.0.
                    return
                        defender.DefensivePositioning * 0.22f + defender.Anticipation * 0.16f +
                        defender.Marking * 0.17f + defender.Pace * 0.08f +
                        defender.Decisions * 0.07f + goalkeeper.OneOnOnes * 0.30f;

                case ChanceType.Cross:
                    return
                        defender.JumpingReach * 0.24f + defender.Heading * 0.18f +
                        defender.Marking * 0.14f + defender.DefensivePositioning * 0.12f +
                        goalkeeper.AerialCommand * 0.20f + goalkeeper.Handling * 0.12f;

                case ChanceType.Dribble:
                    return
                        defender.Tackling * 0.28f + defender.Agility * 0.14f +
                        defender.Acceleration * 0.15f + defender.Balance * 0.10f +
                        defender.DefensivePositioning * 0.13f + goalkeeper.OneOnOnes * 0.20f;

                case ChanceType.LongShot:
                    return
                        defender.DefensivePositioning * 0.14f + defender.Anticipation * 0.08f +
                        goalkeeper.GoalkeeperPositioning * 0.30f + goalkeeper.Reflexes * 0.32f +
                        goalkeeper.Handling * 0.16f;

                case ChanceType.SetPiece:
                    return
                        defender.JumpingReach * 0.22f + defender.Heading * 0.18f +
                        defender.Marking * 0.12f + goalkeeper.AerialCommand * 0.22f +
                        goalkeeper.Handling * 0.16f + goalkeeper.Reflexes * 0.10f;

                case ChanceType.CounterAttack:
                    return
                        defender.Pace * 0.17f + defender.Acceleration * 0.18f +
                        defender.Tackling * 0.18f + defender.Anticipation * 0.14f +
                        defender.Decisions * 0.08f + goalkeeper.OneOnOnes * 0.25f;

                default:
                    return defender.DefensivePositioning * 0.30f + defender.Tackling * 0.20f +
                        defender.Marking * 0.10f + goalkeeper.Handling * 0.20f + goalkeeper.Reflexes * 0.20f;
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
                        shooter.Heading * 0.25f + shooter.JumpingReach * 0.18f +
                        shooter.Anticipation * 0.12f + shooter.Composure * 0.15f +
                        creator.Crossing * 0.15f + creator.Technique * 0.15f;

                case ChanceType.LongShot:
                    return
                        shooter.LongShots * 0.32f + shooter.Technique * 0.20f +
                        shooter.Composure * 0.23f + shooter.Decisions * 0.10f + creator.Vision * 0.15f;

                case ChanceType.Dribble:
                    return
                        shooter.Finishing * 0.28f + shooter.Dribbling * 0.18f +
                        shooter.FirstTouch * 0.13f + shooter.Composure * 0.20f +
                        shooter.Agility * 0.09f + shooter.Acceleration * 0.12f;

                case ChanceType.CounterAttack:
                    return
                        shooter.Finishing * 0.32f + shooter.Acceleration * 0.16f +
                        shooter.OffTheBall * 0.15f + shooter.Composure * 0.20f +
                        creator.Passing * 0.09f + creator.Decisions * 0.08f;

                case ChanceType.ThroughBall:
                default:
                    // Positioning's original 0.20 split with OffTheBall (session 7) - the
                    // dedicated movement-into-space stat, most relevant for exactly this
                    // "gets in behind and finishes" scenario. Still sums to 1.0.
                    return
                        shooter.Finishing * 0.32f + shooter.FirstTouch * 0.10f +
                        shooter.OffTheBall * 0.14f + shooter.Anticipation * 0.10f +
                        shooter.Composure * 0.17f + shooter.Decisions * 0.07f +
                        creator.Vision * 0.10f;
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
                        goalkeeper.AerialCommand * 0.28f + goalkeeper.Handling * 0.22f +
                        goalkeeper.Reflexes * 0.15f + goalkeeper.GoalkeeperPositioning * 0.13f +
                        defender.JumpingReach * 0.12f + defender.DefensivePositioning * 0.10f;

                case ChanceType.LongShot:
                    return
                        goalkeeper.Reflexes * 0.38f + goalkeeper.GoalkeeperPositioning * 0.28f +
                        goalkeeper.Handling * 0.20f + goalkeeper.Decisions * 0.14f;

                default:
                    return
                        goalkeeper.OneOnOnes * 0.27f + goalkeeper.Reflexes * 0.24f +
                        goalkeeper.Handling * 0.22f + goalkeeper.GoalkeeperPositioning * 0.12f +
                        defender.DefensivePositioning * 0.15f;
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
                    return PickWeightedByAttribute(candidates, p => p.Passing + p.Vision + p.Decisions * 0.5f);

                case ChanceType.Dribble:
                case ChanceType.CounterAttack:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.ST
                    );
                    return PickWeightedByAttribute(candidates, p => p.Dribbling + p.Acceleration + p.Agility * 0.5f);

                case ChanceType.SetPiece:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CM ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.RB ||
                        p.PrimaryPosition == PlayerPosition.LB
                    );

                    // A designated corner taker (any position, not just the usual
                    // candidate pool above) takes the corner most of the time rather than
                    // always - reflects that other players occasionally take them too -
                    // and their real Crossing/Creativity then drives the outcome through
                    // the normal GetChanceCreationScore/GetGoalQualityScore math below,
                    // same as any other creator. With two designated takers (session 7),
                    // alternate 50/50 between whichever of the two are set - not true
                    // left/right modeling (this sim has no concept of which side a corner
                    // comes from), just two specialists sharing the duty instead of one.
                    if (CornerTakerNamesByTeamName.TryGetValue(team.TeamName, out (string Left, string Right) cornerTakerNames))
                    {
                        string chosenCornerTakerName = Random.value < 0.5f
                            ? (cornerTakerNames.Left ?? cornerTakerNames.Right)
                            : (cornerTakerNames.Right ?? cornerTakerNames.Left);

                        if (chosenCornerTakerName != null)
                        {
                            PlayerAgent designatedCornerTaker = team.StartingEleven.Find(p => p.Name == chosenCornerTakerName);
                            if (designatedCornerTaker != null && Random.value < 0.85f)
                            {
                                return designatedCornerTaker;
                            }
                        }
                    }

                    return PickWeightedByAttribute(candidates, p => p.Corners + p.Crossing + p.Technique * 0.5f);

                default:
                    return PickCreativePlayerFallback(team);
            }
        }

        private PlayerAgent PickShooterForChance(AgentTeam team, ChanceType chanceType, PlayerAgent excludePlayer)
        {
            List<PlayerAgent> candidates;
            System.Func<PlayerAgent, float> attributeSelector;

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
                    attributeSelector = p => p.Heading + p.JumpingReach + p.Anticipation * 0.5f + p.Finishing * 0.5f;
                    break;

                case ChanceType.LongShot:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CM ||
                        p.PrimaryPosition == PlayerPosition.AM ||
                        p.PrimaryPosition == PlayerPosition.RW ||
                        p.PrimaryPosition == PlayerPosition.LW ||
                        p.PrimaryPosition == PlayerPosition.ST
                    );
                    attributeSelector = p => p.LongShots + p.Technique + p.Composure + p.Decisions * 0.5f;
                    break;

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
                    attributeSelector = p => p.Finishing + p.Anticipation + p.OffTheBall + p.Acceleration * 0.4f;
                    break;
            }

            // Creator and shooter pools overlap for several chance types (e.g. Dribble),
            // so without this a thin squad could have the same player both "create" and
            // "score" a chance - reading like a self-assist in the event text. Only
            // exclude when it leaves at least one alternative; a squad with exactly one
            // eligible player for this chance type is a real (rare) case where they have
            // to be both.
            if (excludePlayer != null && candidates.Count > 1)
            {
                List<PlayerAgent> withoutCreator = candidates.FindAll(p => p != excludePlayer);
                if (withoutCreator.Count > 0)
                {
                    candidates = withoutCreator;
                }
            }

            return PickWeightedByAttribute(candidates, attributeSelector);
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
                    return PickWeightedByAttribute(candidates, p => p.JumpingReach + p.Heading + p.Marking + p.AerialCommand * 0.25f);

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
                    return PickWeightedByAttribute(candidates, p => p.Tackling + p.Acceleration + p.Agility * 0.5f + p.DefensivePositioning);

                case ChanceType.ThroughBall:
                case ChanceType.LongShot:
                default:
                    candidates = team.StartingEleven.FindAll(p =>
                        p.PrimaryPosition == PlayerPosition.CB ||
                        p.PrimaryPosition == PlayerPosition.DM ||
                        p.PrimaryPosition == PlayerPosition.CM
                    );
                    return PickWeightedByAttribute(candidates, p => p.DefensivePositioning + p.Anticipation + p.Marking + p.Tackling);
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

            return PickWeightedByAttribute(candidates, p => p.Vision + p.Decisions + p.Technique * 0.5f);
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

        // Picks one phrasing at random from a fixed set of variants for the same event
        // slot. Each slot mixes at least one short, punchy line in with the fuller
        // sentences - stopped/off-target events are the most frequent ones in a match,
        // so a single fixed template per slot (the pre-variant version of this file)
        // meant the same handful of sentences recurring constantly across 90 minutes.
        // Variety here is purely about phrasing, not new match information.
        private static string PickVariant(params string[] variants)
        {
            return variants[Random.Range(0, variants.Length)];
        }

        // Below this, an event's own real simulated state (fatigue) reads as tired -
        // GetFatigueMultiplier only drops below ~0.90 for a below-average-stamina player
        // well into the second half (see its own clamp, 0.72-1.0). Direction 2 from the
        // Match Log discussion (2026-08-09): let genuine sim state shape phrasing, not
        // just pick from a fixed pool - this is real derived data, not decorative
        // randomness.
        private const float TiredFatigueThreshold = 0.90f;

        private string BuildStoppedEventText(
            AgentTeam attackingTeam,
            PlayerAgent creator,
            PlayerAgent defender,
            ChanceType chanceType,
            float creatorFatigue)
        {
            if (creatorFatigue < TiredFatigueThreshold)
            {
                return PickVariant(
                    $"{creator.Name} looks leggy on the ball and {defender.Name} mops up.",
                    $"{creator.Name}'s legs are heavy chasing the move for {attackingTeam.TeamName}, and {defender.Name} intervenes.",
                    $"{attackingTeam.TeamName} push on through a tiring {creator.Name}, but {defender.Name} reads it easily."
                );
            }

            switch (chanceType)
            {
                case ChanceType.Cross:
                    return PickVariant(
                        $"{attackingTeam.TeamName} look for a cross from {creator.Name}, but {defender.Name} clears it.",
                        $"{defender.Name} cuts out the cross from {creator.Name}.",
                        $"{creator.Name} whips a ball into the box for {attackingTeam.TeamName}, but {defender.Name} gets there first."
                    );

                case ChanceType.ThroughBall:
                    return PickVariant(
                        $"{creator.Name} tries to slip a through ball in for {attackingTeam.TeamName}, but {defender.Name} reads it.",
                        $"{defender.Name} reads the pass and cuts it out.",
                        $"{creator.Name} looks to thread it through for {attackingTeam.TeamName}, but {defender.Name} is alert."
                    );

                case ChanceType.Dribble:
                    return PickVariant(
                        $"{creator.Name} drives forward for {attackingTeam.TeamName}, but {defender.Name} wins the duel.",
                        $"{defender.Name} shuts down {creator.Name} before he can get a shot away.",
                        $"{creator.Name} tries to beat his man for {attackingTeam.TeamName}, but {defender.Name} holds firm."
                    );

                case ChanceType.LongShot:
                    return PickVariant(
                        $"{attackingTeam.TeamName} work space outside the box through {creator.Name}, but {defender.Name} closes it down.",
                        $"{defender.Name} closes the space down before {creator.Name} can shoot.",
                        $"{attackingTeam.TeamName} probe from range through {creator.Name}, but {defender.Name} blocks the path."
                    );

                case ChanceType.SetPiece:
                    return PickVariant(
                        $"{attackingTeam.TeamName} deliver a set piece through {creator.Name}, but {defender.Name} deals with it.",
                        $"{defender.Name} heads the set piece clear.",
                        $"{attackingTeam.TeamName} work a routine from the set piece, but {defender.Name} deals with {creator.Name}'s delivery."
                    );

                case ChanceType.CounterAttack:
                    return PickVariant(
                        $"{attackingTeam.TeamName} break quickly through {creator.Name}, but {defender.Name} stops the counter.",
                        $"{defender.Name} snuffs out the counter.",
                        $"{attackingTeam.TeamName} break at pace behind {creator.Name}, but {defender.Name} recovers to stop it."
                    );

                default:
                    return PickVariant(
                        $"{attackingTeam.TeamName} build an attack through {creator.Name}, but {defender.Name} stops the chance.",
                        $"{defender.Name} deals with it.",
                        $"{attackingTeam.TeamName} probe through {creator.Name}, but {defender.Name} is equal to it."
                    );
            }
        }

        // Never include the word "goal"/"GOAL" in any of these variants - the caller
        // (AppendMatchEventRow in ManagerPrototypeController) already prepends a fixed,
        // bold "{minute}' GOAL ·" prefix to every goal event's row, so a variant that also
        // said "goal" read as a visible duplicate ("GOAL · GOAL! ..." - confirmed live,
        // user feedback). This method is only ever reached for events that ARE goals, so
        // the description itself never needs to say so again.
        private string BuildGoalEventText(
            AgentTeam attackingTeam,
            PlayerAgent creator,
            PlayerAgent shooter,
            ChanceType chanceType,
            bool isDramaticLateGoal)
        {
            // Real score-state, not decorative - set only when the scoring team was level
            // or behind before this goal, 80'+ (see the isDramaticLateGoal computation at
            // the call site in ResolveAttack). Deliberately not chanceType-specific - a
            // late leveller/winner reads the same regardless of how the chance itself
            // came about.
            if (isDramaticLateGoal)
            {
                return PickVariant(
                    $"DRAMA! {shooter.Name} finds a crucial strike deep into the closing stages for {attackingTeam.TeamName}!",
                    $"{attackingTeam.TeamName} snatch a huge winner in the closing stages through {shooter.Name}!",
                    $"With time running out, {shooter.Name} delivers when it matters most for {attackingTeam.TeamName}!"
                );
            }

            switch (chanceType)
            {
                case ChanceType.Cross:
                    return PickVariant(
                        $"{creator.Name} whips in the cross and {shooter.Name} finishes for {attackingTeam.TeamName}.",
                        $"{creator.Name}'s cross is met by {shooter.Name}, who makes no mistake.",
                        $"{shooter.Name} rises highest to head home {creator.Name}'s delivery."
                    );

                case ChanceType.ThroughBall:
                    return PickVariant(
                        $"{creator.Name} splits the defence and {shooter.Name} slots it away for {attackingTeam.TeamName}.",
                        $"{creator.Name} threads the perfect pass and {shooter.Name} finishes with ease.",
                        $"{shooter.Name} times his run to perfection and slots past the keeper."
                    );

                case ChanceType.Dribble:
                    return PickVariant(
                        $"{shooter.Name} beats his marker after work from {creator.Name} and scores for {attackingTeam.TeamName}.",
                        $"{shooter.Name} dances past his marker and buries it, {creator.Name} the provider.",
                        $"{shooter.Name} shows brilliant footwork before finishing clinically."
                    );

                case ChanceType.LongShot:
                    return PickVariant(
                        $"{shooter.Name} finds space and fires in from range for {attackingTeam.TeamName}.",
                        $"{shooter.Name} lets fly from range and it flies into the net.",
                        $"A stunning strike from {shooter.Name}, unstoppable from distance."
                    );

                case ChanceType.SetPiece:
                    return PickVariant(
                        $"{creator.Name} delivers the set piece and {shooter.Name} converts for {attackingTeam.TeamName}.",
                        $"{creator.Name}'s delivery is met perfectly by {shooter.Name}.",
                        $"{shooter.Name} rises above everyone to convert {creator.Name}'s set piece."
                    );

                case ChanceType.CounterAttack:
                    return PickVariant(
                        $"{creator.Name} launches the counter and {shooter.Name} finishes it for {attackingTeam.TeamName}.",
                        $"{attackingTeam.TeamName} sweep forward on the break and {shooter.Name} finishes it off.",
                        $"{creator.Name} picks out {shooter.Name} on the counter, who makes no mistake."
                    );

                default:
                    return PickVariant(
                        $"{creator.Name} creates the chance and {shooter.Name} finishes for {attackingTeam.TeamName}.",
                        $"{shooter.Name} finishes off {creator.Name}'s work.",
                        $"{shooter.Name} gets on the scoresheet."
                    );
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
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} meets the cross, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} rises to meet the cross, but {goalkeeper.Name} is equal to it.",
                        $"Great save! {goalkeeper.Name} keeps out {shooter.Name}'s header from the cross."
                    );

                case ChanceType.ThroughBall:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} gets in behind, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} is played clean through by {attackingTeam.TeamName}, but {goalkeeper.Name} rushes out to deny him.",
                        $"{goalkeeper.Name} stands tall to save from {shooter.Name} after the ball is played through."
                    );

                case ChanceType.Dribble:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots after a dribble, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} skips past a challenge and shoots, but {goalkeeper.Name} palms it away.",
                        $"{goalkeeper.Name} denies {shooter.Name} after a driving run."
                    );

                case ChanceType.LongShot:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots from distance, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} unleashes an effort from range, but {goalkeeper.Name} tips it over.",
                        $"{goalkeeper.Name} makes a smart stop from {shooter.Name}'s long-range effort."
                    );

                case ChanceType.SetPiece:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance from the set piece. {shooter.Name} gets the effort away, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} connects with the set piece, but {goalkeeper.Name} produces a fine save.",
                        $"{goalkeeper.Name} claws away {shooter.Name}'s effort from the set piece."
                    );

                case ChanceType.CounterAttack:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance on the counter. {shooter.Name} shoots, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} bears down on goal on the break, but {goalkeeper.Name} stands firm.",
                        $"{goalkeeper.Name} thwarts {shooter.Name} at the end of a rapid counter."
                    );

                default:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots, but {goalkeeper.Name} saves.",
                        $"{shooter.Name} tests {goalkeeper.Name}, who makes the save.",
                        $"{goalkeeper.Name} denies {shooter.Name}."
                    );
            }
        }

        // Fork-only addition (see the on-target roll in ResolveAttack) - the protected
        // original has no off-target concept at all, so this text has no counterpart
        // there.
        private string BuildOffTargetEventText(
            AgentTeam attackingTeam,
            PlayerAgent shooter,
            ChanceType chanceType,
            float shooterFatigue)
        {
            if (shooterFatigue < TiredFatigueThreshold)
            {
                return PickVariant(
                    $"{shooter.Name} is out on his feet and drags the effort well wide.",
                    $"Tired legs from {shooter.Name} - the shot never troubles the target.",
                    $"{shooter.Name} can't generate any power on a heavy touch and skies it."
                );
            }

            switch (chanceType)
            {
                case ChanceType.Cross:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} meets the cross but heads it wide.",
                        $"Wide! {shooter.Name} can't keep the header down.",
                        $"{shooter.Name} gets up well to meet the cross, but the header flashes wide."
                    );

                case ChanceType.ThroughBall:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} gets in behind but drags the shot wide.",
                        $"Off target! {shooter.Name} drags it wide after getting in behind.",
                        $"{shooter.Name} races onto the through ball but the finish lets him down."
                    );

                case ChanceType.Dribble:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots after a dribble but fires over the bar.",
                        $"Over the bar! {shooter.Name} can't find the target.",
                        $"{shooter.Name} jinks past a man and shoots, but it flies over."
                    );

                case ChanceType.LongShot:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots from distance but sends it well wide.",
                        $"Wide of the post! {shooter.Name} tries his luck from range.",
                        $"{shooter.Name} lets fly from distance, but it sails well wide."
                    );

                case ChanceType.SetPiece:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance from the set piece. {shooter.Name} can't keep the effort down.",
                        $"Off target from the set piece.",
                        $"{shooter.Name} gets a clean connection from the set piece, but it's over the bar."
                    );

                case ChanceType.CounterAttack:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance on the counter. {shooter.Name} shoots but drags it wide.",
                        $"Wasted! {shooter.Name} can't finish off the counter.",
                        $"{shooter.Name} breaks clear on the counter but drags the shot wide."
                    );

                default:
                    return PickVariant(
                        $"{attackingTeam.TeamName} chance. {shooter.Name} shoots wide.",
                        $"{shooter.Name} fires wide.",
                        $"{shooter.Name} can't find the target."
                    );
            }
        }

        private class ScoreStateModifier
        {
            public float AttackShareMultiplier = 1f;
            public float AttackQualityMultiplier = 1f;
            public float DefensiveMultiplier = 1f;
        }

        private ScoreStateModifier GetScoreStateModifier(
            int ownGoals,
            int opponentGoals,
            int minute)
        {
            ScoreStateModifier modifier = new ScoreStateModifier();

            int goalDifference = ownGoals - opponentGoals;

            // Keep the first half mostly driven by the pre-match model.
            if (minute < 55)
            {
                return modifier;
            }

            // Losing teams become more urgent, especially late.
            if (goalDifference < 0)
            {
                if (minute >= 75)
                {
                    modifier.AttackShareMultiplier = 1.12f;
                    modifier.AttackQualityMultiplier = 1.08f;
                    modifier.DefensiveMultiplier = 0.94f;
                }
                else
                {
                    modifier.AttackShareMultiplier = 1.07f;
                    modifier.AttackQualityMultiplier = 1.04f;
                    modifier.DefensiveMultiplier = 0.97f;
                }
            }

            // Winning teams protect leads, especially late.
            if (goalDifference > 0)
            {
                if (minute >= 75)
                {
                    modifier.AttackShareMultiplier = 0.92f;
                    modifier.AttackQualityMultiplier = 0.96f;
                    modifier.DefensiveMultiplier = 1.08f;
                }
                else
                {
                    modifier.AttackShareMultiplier = 0.96f;
                    modifier.AttackQualityMultiplier = 0.98f;
                    modifier.DefensiveMultiplier = 1.04f;
                }
            }

            // Drawn games become a little more open very late.
            if (goalDifference == 0 && minute >= 80)
            {
                modifier.AttackShareMultiplier = 1.04f;
                modifier.AttackQualityMultiplier = 1.03f;
                modifier.DefensiveMultiplier = 0.98f;
            }

            return modifier;
        }

        // Made public in this fork only (was private in the protected original) so
        // Manager Mode can surface a live per-player condition indicator on the Tactics
        // Board using the exact same formula the sim itself already plays matches
        // against, instead of a second guessed-at copy of the math living in UI code.
        public float GetFatigueMultiplier(PlayerAgent player, int minute)
        {
            if (player == null)
            {
                return 1f;
            }

            // A substitute's fatigue clock starts at their own entry minute, not
            // kickoff - see substituteEntryMinute above. Falls back to the raw match
            // minute for every starter (the never-substituted default), unchanged from
            // before this fix.
            int minutesOnPitch = substituteEntryMinute.TryGetValue(player, out int entryMinute)
                ? Mathf.Max(0, minute - entryMinute)
                : minute;

            // No real fatigue early.
            if (minutesOnPitch <= 45)
            {
                return 1f;
            }

            float staminaNormalised = Mathf.Clamp01(player.Stamina / 100f);
            float matchProgressAfterHalfTime = Mathf.InverseLerp(45f, 90f, minutesOnPitch);

            // Low stamina players lose more effectiveness late.
            float fatigueLoss = (1f - staminaNormalised) * matchProgressAfterHalfTime * 0.28f;

            return Mathf.Clamp(1f - fatigueLoss, 0.72f, 1f);
        }
    }
}
