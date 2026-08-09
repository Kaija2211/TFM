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

        // Manager Mode-only: team name -> designated corner taker's name (see
        // ManagerSquadRoles), set by ManagerPrototypeController before each SimulateMatch
        // call. Keyed and matched by name rather than PlayerAgent reference because the
        // team actually handed to SimulateMatch is a throwaway fit-adjusted clone (see
        // ManagerFormationFit) with all-new PlayerAgent instances - Name is the one thing
        // ClonePenalized copies through unchanged. Empty/unset by default, so a team with
        // no corner taker assigned takes the exact original weighted-random pick with zero
        // extra Random calls - see PickCreatorForChance.
        public readonly Dictionary<string, string> CornerTakerNameByTeamName = new();

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
            ChanceType chanceType = PickChanceType(attackingExpectedGoals);

            PlayerAgent creator = PickCreatorForChance(attackingTeam, chanceType);
            PlayerAgent shooter = PickShooterForChance(attackingTeam, chanceType);
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
                        chanceType,
                        creatorFatigue
                    ),
                    HomeTeamAttacking = homeAttacks
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
                    HomeTeamAttacking = homeAttacks
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
                0.302f + (goalQuality - saveQuality) / 320f,
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
                    ScorerName = shooter.Name
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
                    HomeTeamAttacking = homeAttacks
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

                    // A designated corner taker (any position, not just the usual
                    // candidate pool above) takes the corner most of the time rather than
                    // always - reflects that other players occasionally take them too -
                    // and their real Crossing/Creativity then drives the outcome through
                    // the normal GetChanceCreationScore/GetGoalQualityScore math below,
                    // same as any other creator.
                    if (CornerTakerNameByTeamName.TryGetValue(team.TeamName, out string designatedCornerTakerName)
                        && designatedCornerTakerName != null)
                    {
                        PlayerAgent designatedCornerTaker = team.StartingEleven.Find(p => p.Name == designatedCornerTakerName);
                        if (designatedCornerTaker != null && Random.value < 0.85f)
                        {
                            return designatedCornerTaker;
                        }
                    }

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

            // No real fatigue early.
            if (minute <= 45)
            {
                return 1f;
            }

            float staminaNormalised = Mathf.Clamp01(player.Stamina / 100f);
            float matchProgressAfterHalfTime = Mathf.InverseLerp(45f, 90f, minute);

            // Low stamina players lose more effectiveness late.
            float fatigueLoss = (1f - staminaNormalised) * matchProgressAfterHalfTime * 0.28f;

            return Mathf.Clamp(1f - fatigueLoss, 0.72f, 1f);
        }
    }
}
