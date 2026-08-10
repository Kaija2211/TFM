using System.Collections.Generic;
using UnityEngine;

namespace Manager
{
    // Live in-match player ratings (session 10 backlog item - Thomas sketched a strip
    // of 11 player cards on the live match screen, each showing name + a live-updating
    // rating). Builds on the CreatorName/ShooterName/DefenderName/GoalkeeperName fields
    // just added to the ManagerSim fork's AgentMatchEvent (see its own comment) -
    // ResolveAttack already resolves exactly these four identities for every chance, so
    // this class just reacts to events as they're revealed instead of re-simulating
    // anything.
    //
    // Managed team only, same "no AI-vs-AI" precedent as Condition/appearances/form
    // bonus - keyed by player NAME (string), matching the existing precedent
    // ApplyMatchFormBonusForManagedTeam already uses for scorer attribution
    // (AgentMatchEvent only ever carries names, never PlayerAgent references, since
    // events need to survive past the throwaway fit-adjusted team clones used during a
    // match). Add() silently ignores any name not seeded via ResetForMatch, so an
    // opponent's players (whose names could theoretically collide, however unlikely)
    // never pollute the managed team's ratings.
    //
    // Not persisted through save/load - purely a live-match display concern, gone by
    // the time any save would happen (same already-precedented scope limit as
    // Condition/injuries/the OVR delta badge).
    public class ManagerMatchRatings
    {
        private const float BaseRating = 6.0f;
        private const float MinRating = 3.0f;
        private const float MaxRating = 10.0f;

        private readonly Dictionary<string, float> ratingByPlayerName = new();

        public void ResetForMatch(IEnumerable<string> playerNames)
        {
            ratingByPlayerName.Clear();

            foreach (string name in playerNames)
            {
                ratingByPlayerName[name] = BaseRating;
            }
        }

        // Called when a substitute enters the XI mid-match (see OnBenchPlayerDroppedOnPin)
        // - starts them at the same baseline everyone kicks off at, not whatever the
        // player they replaced had earned. No-ops if already tracked (e.g. re-adding the
        // same starter after an unrelated pin swap).
        public void EnsureTracked(string playerName)
        {
            if (!ratingByPlayerName.ContainsKey(playerName))
            {
                ratingByPlayerName[playerName] = BaseRating;
            }
        }

        public float GetRating(string playerName)
        {
            return ratingByPlayerName.TryGetValue(playerName, out float rating) ? rating : BaseRating;
        }

        public bool IsTracked(string playerName) => ratingByPlayerName.ContainsKey(playerName);

        // Rating deltas per event outcome - tuned to feel roughly FM-shaped rather than
        // derived from any formal model: a goal is the single biggest single-event swing
        // (+1.0), an assist-equivalent (creator on a goal) is a clear but smaller credit
        // (+0.5), and every other outcome is a light nudge rather than a big swing - a
        // single missed chance or stopped attack shouldn't tank a rating on its own, only
        // a genuine pattern across the full 90 minutes should.
        public void ApplyEvent(AgentMatchSimulator.AgentMatchEvent evt)
        {
            if (evt.IsGoal)
            {
                Add(evt.ScorerName, 1.0f);

                // Guard against double-crediting the same player when a goal has no
                // distinct creator (e.g. a solo run) - CreatorName == ScorerName in that
                // case, and a scorer shouldn't ALSO collect the separate creator bonus
                // for their own goal.
                if (evt.CreatorName != evt.ScorerName)
                {
                    Add(evt.CreatorName, 0.5f);
                }

                Add(evt.DefenderName, -0.3f);
                Add(evt.GoalkeeperName, -0.25f);
            }
            else if (evt.IsShot && evt.IsOnTarget)
            {
                // Saved - good goalkeeping, not a shooter failure (they hit the target).
                Add(evt.GoalkeeperName, 0.15f);
                Add(evt.DefenderName, 0.05f);
                Add(evt.CreatorName, 0.05f);
            }
            else if (evt.IsShot)
            {
                // Off target - the shooter's own inaccuracy, not the defence's doing.
                Add(evt.ShooterName, -0.05f);
                Add(evt.CreatorName, 0.05f);
            }
            else
            {
                // Stopped before a shot ever happened - the defender earns a small
                // credit for shutting it down; the creator gets nothing since the chance
                // never developed into anything.
                Add(evt.DefenderName, 0.05f);
            }
        }

        private void Add(string playerName, float delta)
        {
            if (string.IsNullOrEmpty(playerName) || !ratingByPlayerName.ContainsKey(playerName))
            {
                return;
            }

            ratingByPlayerName[playerName] = Mathf.Clamp(ratingByPlayerName[playerName] + delta, MinRating, MaxRating);
        }
    }
}
