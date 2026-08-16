using System.Collections.Generic;
using System.Linq;
using Sim;

namespace Manager
{
    // Shared best-available-XI selection, extracted from the managed team's own
    // Auto-Pick button (session 12) so AI-controlled clubs can use the exact same
    // algorithm for matchday rotation (roadmap: "AI squad evaluation and coherent
    // rotation across the full 30-player pool"). Greedy slot-by-slot assignment, not
    // a true combinatorial optimum - a strong practical XI. For each formation slot
    // in order, picks whichever eligible remaining candidate scores highest:
    // position-fit tier is strictly dominant (0.60/0.80/1.00, x1000), Condition-
    // adjusted Overall breaks ties within a tier. This is what makes rotation
    // "coherent" rather than random - with everyone fresh it reliably reselects the
    // same nominal strongest XI match after match, and only diverges when someone's
    // injured or meaningfully more tired than their replacement.
    public static class ManagerSquadAutoPicker
    {
        public static List<PlayerAgent> PickBestAvailableXI(
            List<PlayerAgent> eligiblePool,
            List<PlayerPosition> slots,
            ManagerSquadRoles roles)
        {
            List<PlayerAgent> bestXI = new List<PlayerAgent>();

            foreach (PlayerPosition slot in slots)
            {
                PlayerAgent best = null;
                float bestScore = float.MinValue;

                foreach (PlayerAgent candidate in eligiblePool)
                {
                    if (bestXI.Contains(candidate))
                    {
                        continue;
                    }

                    // GetPositionFit alone doesn't hard-block a keeper from an
                    // outfield slot or vice versa - guarded explicitly here.
                    bool candidateIsGK = candidate.PrimaryPosition == PlayerPosition.GK;
                    bool slotIsGK = slot == PlayerPosition.GK;
                    if (candidateIsGK != slotIsGK)
                    {
                        continue;
                    }

                    float fit = candidate.GetPositionFit(slot);
                    float conditionAdjustedOverall = candidate.GetOverallRating() * roles.GetConditionMultiplier(candidate);
                    float score = fit * 1000f + conditionAdjustedOverall;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null)
                {
                    bestXI.Add(best);
                }
            }

            // Fallback for a genuinely short-handed pool (mass injuries etc.) - fill
            // any remaining slot with whoever's left rather than leave a pin empty.
            if (bestXI.Count < slots.Count)
            {
                foreach (PlayerAgent candidate in eligiblePool)
                {
                    if (bestXI.Count >= slots.Count)
                    {
                        break;
                    }

                    if (!bestXI.Contains(candidate))
                    {
                        bestXI.Add(candidate);
                    }
                }
            }

            return bestXI;
        }

        // Picks the best available XI from the given pool (already filtered to
        // whoever's eligible - injured players excluded by the caller) and commits it
        // via AgentTeam.ChangeFormation, then rebuilds a healthy named bench (capped
        // at 9) from whoever's left, with the remainder falling to Reserves. Returns
        // false without mutating the team if the pool can't fill every slot.
        public static bool TryAutoPickAndApply(
            AgentTeam team,
            ManagerSquadRoles roles,
            List<PlayerPosition> slots,
            List<PlayerAgent> eligiblePool,
            int currentDayNumber)
        {
            List<PlayerAgent> pool = eligiblePool
                .Where(player => !roles.IsInjured(player, currentDayNumber))
                .ToList();

            List<PlayerAgent> bestXI = PickBestAvailableXI(pool, slots, roles);
            if (bestXI.Count < slots.Count)
            {
                return false;
            }

            // Bench-rebuild priority: previous bench, then previous reserves, then
            // dropped starters - matches the original human Auto-Pick ordering
            // exactly (a demoted starter falls behind an existing reserve for one of
            // the 9 named slots). Restricted to eligiblePool so, mid-match, a reserve
            // can never be smuggled onto the bench even though team.Reserves itself
            // still lists them - eligiblePool is a no-op filter outside that case,
            // since it's built from team.Players already.
            List<PlayerAgent> previousOrder = new List<PlayerAgent>(team.Bench);
            previousOrder.AddRange(team.Reserves);
            previousOrder.AddRange(team.StartingEleven);
            team.ChangeFormation(team.Formation, bestXI);

            // ChangeFormation preserves old list order but knows nothing about
            // injuries; rebuild the named bench from healthy eligible players so an
            // injured player cannot be reintroduced immediately after being excluded
            // from the XI.
            List<PlayerAgent> healthyRemainder = previousOrder
                .Where(player => !bestXI.Contains(player)
                    && !roles.IsInjured(player, currentDayNumber)
                    && eligiblePool.Contains(player))
                .Distinct()
                .ToList();
            team.Bench = healthyRemainder.Take(9).ToList();
            team.Reserves = team.Players.Where(player => !bestXI.Contains(player) && !team.Bench.Contains(player)).ToList();
            return true;
        }
    }
}
