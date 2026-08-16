using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using Sim;
using UnityEngine;

namespace Manager
{
    // Partial: world-profile loading, squad creation, live strength and
    // reserve/role lookup. See MANAGER_CONTROLLER_ARCHITECTURE.md for the full
    // partial-file ownership map.
    public partial class ManagerPrototypeController
    {
        private void InitializeWorldGenerationService()
        {
            try
            {
                TextAsset historyAsset = Resources.Load<TextAsset>("World/football_world_history");
                TextAsset registryAsset = Resources.Load<TextAsset>("World/football_club_registry");
                if (historyAsset == null || registryAsset == null)
                {
                    Debug.LogWarning("World generation data is unavailable; fresh careers will use the legacy bootstrap.");
                    worldGenerationService = null;
                    return;
                }
                worldGenerationService = new WorldClubGenerationService(
                    FootballClubRegistry.FromTextAsset(registryAsset),
                    FootballWorldHistory.FromTextAsset(historyAsset));
            }
            catch (Exception exception)
            {
                worldGenerationService = null;
                Debug.LogError($"World generation data failed to load; using legacy bootstrap. {exception.Message}");
            }
        }

        private static void EnsureMatchResultMatchesEvents(AgentMatchSimulator.AgentMatchResult result)
        {
            int homeGoalsFromEvents = result.Events.Count(evt => evt.IsGoal && evt.HomeTeamScored);
            int awayGoalsFromEvents = result.Events.Count(evt => evt.IsGoal && !evt.HomeTeamScored);
            if (result.HomeGoals == homeGoalsFromEvents && result.AwayGoals == awayGoalsFromEvents) return;

            Debug.LogWarning($"Match score/event mismatch corrected: {result.HomeGoals}-{result.AwayGoals} became {homeGoalsFromEvents}-{awayGoalsFromEvents}.");
            result.HomeGoals = homeGoalsFromEvents;
            result.AwayGoals = awayGoalsFromEvents;
        }

        private bool TryGetWorldTarget(string teamName, out SquadQualityTarget target)
        {
            if (worldGenerationService != null &&
                worldGenerationService.TryGetSquadQualityTarget("eng", teamName, out _, out target))
            {
                return true;
            }
            target = default;
            return false;
        }

        private float GetWorldLeagueMeanOverall()
        {
            if (worldLeagueMeanOverall > 0f) return worldLeagueMeanOverall;
            float total = 0f;
            int count = 0;
            foreach (string teamName in availableTeamNames)
            {
                if (!TryGetWorldTarget(teamName, out SquadQualityTarget target)) continue;
                total += target.FirstTeamOverall;
                count++;
            }
            worldLeagueMeanOverall = count > 0 ? total / count : 79.5f;
            if (count > 0)
            {
                foreach (string teamName in availableTeamNames)
                {
                    if (!TryGetWorldTarget(teamName, out SquadQualityTarget target)) continue;
                    worldLeagueMaxPositiveDelta = Mathf.Max(worldLeagueMaxPositiveDelta, target.FirstTeamOverall - worldLeagueMeanOverall);
                }
            }
            return worldLeagueMeanOverall;
        }

        private void ConfigureInitialWorldStrength(string teamName, float firstTeamOverall)
        {
            // Player quality remains the source of truth. These factors translate the
            // generated league-relative quality gap into the xG prior consumed by the
            // existing match simulator; reputation and historical results never enter.
            const float ratingToLogStrength = 0.24f;
            float delta = firstTeamOverall - GetWorldLeagueMeanOverall();
            if (delta > 0f && worldLeagueMaxPositiveDelta > 0f)
            {
                // A concave positive curve keeps the best club fixed while avoiding a
                // cliff immediately below the elite. Mid/high-table clubs remain
                // ordered by player quality but are not treated as relegation-level
                // opposition simply because they trail a capped historical outlier.
                delta = Mathf.Sqrt(delta / worldLeagueMaxPositiveDelta) * worldLeagueMaxPositiveDelta;
            }
            float attack = Mathf.Exp(delta * ratingToLogStrength);
            float defence = Mathf.Exp(-delta * ratingToLogStrength);
            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
            strength.AttackStrength = attack;
            strength.DefenceStrength = defence;
            originalAttackStrengthByTeam[teamName] = attack;
            originalDefenceStrengthByTeam[teamName] = defence;
        }

        private AgentTeam GetOrCreateAgentTeam(string teamName)
        {
            if (squadsByTeamName.TryGetValue(teamName, out AgentTeam existingTeam))
            {
                return existingTeam;
            }

            AgentTeam newTeam;
            if (usesWorldGeneration && TryGetWorldTarget(teamName, out SquadQualityTarget target))
            {
                newTeam = squadGenerator.GenerateSquad(teamName, target);
                ConfigureInitialWorldStrength(teamName, target.FirstTeamOverall);
            }
            else
            {
                StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
                newTeam = squadGenerator.GenerateSquad(teamName, strength.AttackStrength, strength.DefenceStrength);
            }
            ApplyDeveloperEasterEggPlayer(newTeam);

            squadsByTeamName[teamName] = newTeam;

            // Live team strength (session 16) baseline - captured once, the very first
            // time this team's squad exists, before anything ever mutates it. See
            // RecalculateLiveTeamStrength for how this gets used every season rollover.
            baselineAverageOverallByTeam[teamName] = GetAverageOverall(newTeam);

            // AI-club finance foundation - seed a budget the moment any club's squad
            // first exists, not just at season rollover (DeductWageBillForAllClubs
            // would eventually seed it anyway, but a club's budget should be a real
            // number from the start of a career, matching how the managed team's own
            // budget is already seeded well before its first rollover). Idempotent -
            // GetOrSeedBudget only ever seeds once per team name.
            StatisticalModel.TeamStrength budgetSeedStrength = statisticalModel.GetTeamStrength(teamName);
            finance.GetOrSeedBudget(teamName, budgetSeedStrength.AttackStrength, budgetSeedStrength.DefenceStrength);

            return newTeam;
        }

        // Live team strength (session 16) - Thomas: "team strength to be live... City
        // will just always win most seasons no matter what, but if they have player
        // decline... or if they lose the player, their performance should reflect that."
        // Manager Mode's own statisticalModel instance is completely separate from
        // Research Mode's (each instantiates its own, see ResearchEvaluationRunner.cs) -
        // mutating TeamStrength here never touches the trained historical baseline
        // Research Mode's own evaluation runs depend on.
        //
        // Driven by squad average Overall vs. the baseline captured at generation time
        // (Thomas's explicit choice over a transfers-only signal) - one number that
        // already reflects transfers in/out, retirements, and the aging/growth/decline
        // every AI first-team player gets via ApplySeasonProgression every season,
        // without needing separate bookkeeping for each cause. Recalculated from the
        // ORIGINAL baseline every time, not compounded onto last season's already-
        // adjusted value, so this can't drift or double-count across many seasons - it's
        // always "how different is this squad from where it started," full stop.
        // Clamped to 0.6x-1.5x - the sale-guard rules (WouldLeaveSquadTooThin) already
        // keep a squad from being hollowed out entirely, but the clamp is a second,
        // independent backstop against a pathological swing feeding back into
        // ApplyRetirementsForTeam's replacement generation (which reads this same
        // TeamStrength) and compounding.
        //
        // DefenceStrength is inverted (see feedback_defencestrength_inverted in memory -
        // lower DefenceStrength means fewer goals conceded, i.e. a BETTER defence), so a
        // stronger squad DIVIDES it rather than multiplying, same fix already applied to
        // the reserve-pool discount and confirmed live there.
        private const float LiveStrengthMinRatio = 0.6f;
        private const float LiveStrengthMaxRatio = 1.5f;

        private void RecalculateLiveTeamStrength(string teamName, AgentTeam team)
        {
            if (!baselineAverageOverallByTeam.TryGetValue(teamName, out float baselineAverage) || baselineAverage <= 0f)
            {
                return;
            }

            float currentAverage = GetAverageOverall(team);
            float ratio = Mathf.Clamp(currentAverage / baselineAverage, LiveStrengthMinRatio, LiveStrengthMaxRatio);

            StatisticalModel.TeamStrength strength = statisticalModel.GetTeamStrength(teamName);
            strength.AttackStrength = originalAttackStrengthByTeam[teamName] * ratio;
            strength.DefenceStrength = originalDefenceStrengthByTeam[teamName] / ratio;
        }

        private static float GetAverageOverall(AgentTeam team)
        {
            if (team.Players.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            foreach (PlayerAgent player in team.Players) total += player.GetOverallRating();
            return total / team.Players.Count;
        }

        private List<PlayerAgent> GetOrCreateReservePool(string teamName)
        {
            return GetOrCreateAgentTeam(teamName).Reserves;
        }

        // Promotes the best-fitting available reserve straight onto the real matchday
        // bench (AddBenchPlayer is already public on AgentTeam - no protected-file change
        // needed to do this) so they immediately show up everywhere the rest of the squad
        // does (Squad screen, Tactics Board substitute picker). Prefers an exact position
        // match; falls back to the reserve with the best position fit for the needed slot
        // (PlayerAgent.GetPositionFit, the same adjacency judgement formation-fit already
        // uses) rather than leaving a position with zero cover. Returns null if the pool
        // is completely exhausted - a real, visible squad crisis rather than silently
        // conjuring an infinite bench.
        private PlayerAgent CallUpReservePlayer(string teamName, PlayerPosition neededPosition)
        {
            List<PlayerAgent> pool = GetOrCreateReservePool(teamName);

            if (pool.Count == 0)
            {
                return null;
            }

            PlayerAgent best = pool[0];
            float bestFit = best.GetPositionFit(neededPosition);

            foreach (PlayerAgent candidate in pool)
            {
                float fit = candidate.GetPositionFit(neededPosition);
                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            GetOrCreateAgentTeam(teamName).PromoteReserveToBench(best);

            return best;
        }

        // Manager Mode-only side table (captaincy, set-piece takers, attack/defend role) -
        // see ManagerSquadRoles. Keyed by team name alongside squadsByTeamName; a team's
        // ManagerSquadRoles is created empty on first access and persists for the rest of
        // the play session, same lifetime as the AgentTeam it applies to.
        private ManagerSquadRoles GetOrCreateSquadRoles(string teamName)
        {
            if (!squadRolesByTeamName.TryGetValue(teamName, out ManagerSquadRoles roles))
            {
                roles = new ManagerSquadRoles();
                squadRolesByTeamName[teamName] = roles;
            }

            return roles;
        }

    }
}
