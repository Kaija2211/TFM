using System.Collections.Generic;
using Sim;

namespace Manager
{
    // AI-club matchday rotation (roadmap: "AI squad evaluation and coherent rotation
    // across the full 30-player pool"). Not a blind full best-XI re-pick every single
    // match (see ManagerSquadAutoPicker, which the managed team's own Auto-Pick button
    // still uses for that occasional, manual case) - an early version tried exactly
    // that for AI clubs and ManagerAiSquadRotationAudit caught it thrashing the XI on
    // essentially every fixture. A second version only reconsidered a starter once
    // their OWN Condition dropped below a rest threshold, which fixed the thrashing
    // but introduced an asymmetry the same audit also caught: once rotated out for
    // fatigue, a strong player only ever returns when whoever replaced them ALSO
    // crosses the threshold, so a recovered, clearly-better bench player could sit
    // unused indefinitely - measurably dragging goals/game down further than the
    // thrashing version had.
    //
    // Current design: every slot is reconsidered every match, but a replacement only
    // wins if it beats the incumbent's score by a real hysteresis margin (same fit-
    // tier-dominant, Condition-adjusted-Overall scoring used everywhere else squad
    // selection happens) - small enough to let genuine fatigue/quality differences
    // through, large enough that trivial noise never flips a decision. A fresh,
    // healthy incumbent's score is essentially unbeatable by a lower-baseline bench
    // player anyway (bench is generated weaker than the XI), so this stays "coherent"
    // (stable week to week) in practice without needing a separate threshold gate. An
    // injured incumbent is always replaced unconditionally, margin or not - they
    // cannot play at all, same rule ManagerPrototypeController.EnsureNoInjuredStarters
    // already used for the managed team.
    public static class ManagerAiSquadRotation
    {
        private const float HysteresisMargin = 3f;

        public static void Rotate(AgentTeam team, ManagerSquadRoles roles, List<PlayerPosition> slots, int currentDayNumber)
        {
            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent starter = team.StartingEleven[i];
                bool injured = roles.IsInjured(starter, currentDayNumber);
                PlayerPosition slot = i < slots.Count ? slots[i] : starter.PrimaryPosition;
                PlayerAgent replacement = FindBestReplacement(team, roles, slot, starter, currentDayNumber, forceReplace: injured);
                if (replacement == null)
                {
                    continue;
                }

                team.SubstitutePlayer(starter, replacement);

                if (injured)
                {
                    // SubstitutePlayer always sends the outgoing player to the named
                    // Bench - correct for a genuine live in-match sub, wrong here: this
                    // runs before kickoff, so an injured player shouldn't sit on the
                    // active matchday bench at all (mirrors OnAutoPickBestXIClicked's
                    // own "rebuild bench from healthy players" rule).
                    team.Bench.Remove(starter);
                    team.Reserves.Add(starter);
                }
            }
        }

        private static PlayerAgent FindBestReplacement(AgentTeam team, ManagerSquadRoles roles, PlayerPosition neededPosition, PlayerAgent incumbent, int currentDayNumber, bool forceReplace)
        {
            // An injured incumbent can't play at all, so any healthy cover counts as
            // an "upgrade" over them - forceReplace makes the incumbent's own score
            // (and the margin) irrelevant to the comparison rather than trying to rank
            // an unplayable option.
            float requiredScore = forceReplace ? float.MinValue : Score(incumbent, neededPosition, roles) + HysteresisMargin;

            PlayerAgent best = null;
            float bestScore = requiredScore;

            foreach (PlayerAgent candidate in team.Bench)
            {
                if (roles.IsInjured(candidate, currentDayNumber))
                {
                    continue;
                }

                float score = Score(candidate, neededPosition, roles);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
            {
                return best;
            }

            // No bench cover clears the bar - try calling up a reserve at this exact
            // position before giving up (mirrors
            // ManagerPrototypeController.CallUpReservePlayer).
            PlayerAgent reserveCall = CallUpBestReserve(team, roles, neededPosition, currentDayNumber);
            if (reserveCall == null)
            {
                return null;
            }

            if (forceReplace || Score(reserveCall, neededPosition, roles) > requiredScore)
            {
                return reserveCall;
            }

            // Called up but doesn't actually clear the bar - demote back to reserves
            // rather than leaving them stranded on the bench for no reason.
            team.Bench.Remove(reserveCall);
            team.Reserves.Add(reserveCall);
            return null;
        }

        // GetPositionFit alone doesn't hard-block a keeper from an outfield slot or
        // vice versa (it has no real notion of goalkeeping at all - GK deliberately
        // has no entry in its own AdjacentPositions table) - guarded explicitly here,
        // same as ManagerSquadAutoPicker.PickBestAvailableXI, so an outfield player can
        // never be scored in as a "replacement" goalkeeper or vice versa.
        private static float Score(PlayerAgent player, PlayerPosition position, ManagerSquadRoles roles)
        {
            bool playerIsGK = player.PrimaryPosition == PlayerPosition.GK;
            bool slotIsGK = position == PlayerPosition.GK;
            if (playerIsGK != slotIsGK)
            {
                return float.MinValue;
            }

            return player.GetPositionFit(position) * 1000f + player.GetOverallRating() * roles.GetConditionMultiplier(player);
        }

        private static PlayerAgent CallUpBestReserve(AgentTeam team, ManagerSquadRoles roles, PlayerPosition neededPosition, int currentDayNumber)
        {
            PlayerAgent best = null;
            float bestFit = -1f;

            foreach (PlayerAgent candidate in team.Reserves)
            {
                // A reserve can be sitting here mid-injury - an injured starter is
                // demoted straight to Reserves (see Rotate above), not removed from
                // the squad, so this pool isn't guaranteed injury-free the way it
                // normally would be for a player who's never been rotated out.
                if (roles.IsInjured(candidate, currentDayNumber))
                {
                    continue;
                }

                bool candidateIsGK = candidate.PrimaryPosition == PlayerPosition.GK;
                bool slotIsGK = neededPosition == PlayerPosition.GK;
                if (candidateIsGK != slotIsGK)
                {
                    continue;
                }

                float fit = candidate.GetPositionFit(neededPosition);
                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }

            if (best == null)
            {
                return null;
            }

            team.PromoteReserveToBench(best);
            return best;
        }
    }
}
