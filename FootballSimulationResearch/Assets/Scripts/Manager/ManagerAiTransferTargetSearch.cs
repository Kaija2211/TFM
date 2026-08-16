using System.Collections.Generic;
using System.Linq;
using Sim;

namespace Manager
{
    // AI transfer target search (roadmap: "Identify needs and search for tactically
    // appropriate targets" - third stage of the Intelligent AI Clubs epic, consuming
    // ManagerAiSquadDepthEvaluator's weakest-position output). Read-only: finds and
    // ranks genuine upgrades for a needed position from the wider generated world.
    // Deliberately does NOT execute any transfer, check budget, or touch finances -
    // AI clubs have no budget/finance tracking at all today (only the managed team's
    // budget is ever spent or displayed, see ManagerPrototypeController.HubAndSeason.
    // cs's own DeductManagedTeamWageBill comment), so an AI club actually buying a
    // player is a separate, larger piece of future work that needs that foundation
    // first. This service only answers "who out there would genuinely improve this
    // position," not "should we, or can we afford to, sign them."
    public static class ManagerAiTransferTargetSearch
    {
        // Same 0.80 "adjacent or better" fit tier ManagerAiSquadDepthEvaluator and the
        // rest of squad selection already treat as genuinely usable, not an emergency
        // mismatch.
        private const float MinimumFitToConsider = 0.80f;

        // A mild preference for players with more prime years ahead - the same
        // "closer to 30 costs more" shape used elsewhere (e.g. ManagerMatchdayCondition's
        // injury-risk age curve), not a hard cutoff. A slightly older but much better
        // player can still out-rank a barely-younger, weaker one.
        private const int PrimeAgeCeiling = 27;
        private const float AgePenaltyPerYearOverPrime = 0.6f;

        public readonly struct TransferTarget
        {
            public readonly PlayerAgent Player;
            public readonly string CurrentClubName;
            public readonly float Fit;
            public readonly float OverallRating;
            public readonly float SuitabilityScore;

            public TransferTarget(PlayerAgent player, string currentClubName, float fit, float overallRating, float suitabilityScore)
            {
                Player = player;
                CurrentClubName = currentClubName;
                Fit = fit;
                OverallRating = overallRating;
                SuitabilityScore = suitabilityScore;
            }
        }

        // candidateClubs should exclude the searching club itself (and, for now, the
        // human-managed club - AI recruitment deliberately doesn't touch the human's
        // own squad or transfer market yet). Only genuine upgrades over
        // currentBestOverall are returned - this never recommends a lateral move or a
        // downgrade, matching the epic's "rational... decisions" goal.
        public static List<TransferTarget> FindTargets(
            PlayerPosition neededPosition,
            float currentBestOverall,
            IEnumerable<AgentTeam> candidateClubs,
            int maxResults = 5)
        {
            List<TransferTarget> candidates = new List<TransferTarget>();

            foreach (AgentTeam club in candidateClubs)
            {
                foreach (PlayerAgent player in club.Players)
                {
                    float fit = player.GetPositionFit(neededPosition);
                    if (fit < MinimumFitToConsider)
                    {
                        continue;
                    }

                    float overall = player.GetOverallRating();
                    if (overall <= currentBestOverall)
                    {
                        continue;
                    }

                    float agePenalty = System.Math.Max(0, player.Age - PrimeAgeCeiling) * AgePenaltyPerYearOverPrime;
                    float suitability = overall - agePenalty;

                    candidates.Add(new TransferTarget(player, club.TeamName, fit, overall, suitability));
                }
            }

            return candidates
                .OrderByDescending(t => t.SuitabilityScore)
                .Take(maxResults)
                .ToList();
        }
    }
}
