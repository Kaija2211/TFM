using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Youth prospect pools + scouting (career arc, Phase 2, session 8) - a hidden layer
    // beneath the reserve pool (see ManagerPrototypeController.reservePoolByTeamName):
    // a handful of age-16-19 players per club, generated the same way but never
    // surfaced in normal squad/transfer browsing. Discovery is the whole point - you
    // don't already see every club's hidden gems, you have to scout them. One instance
    // for the whole career (not per-team, unlike ManagerSquadRoles) since scouting
    // knowledge is a manager-level resource, not tied to any one squad.
    public class ManagerScouting
    {
        public const int ProspectsPerTeam = 3;
        private const int MinProspectAge = 16;
        private const int MaxProspectAge = 19;
        public const int MaxConcurrentAssignments = 2;

        private static readonly PlayerPosition[] ProspectPositionCycle =
        {
            PlayerPosition.CB, PlayerPosition.CM, PlayerPosition.ST,
            PlayerPosition.RW, PlayerPosition.LB, PlayerPosition.AM
        };

        private readonly Dictionary<string, List<PlayerAgent>> youthPoolByTeamName = new();
        private readonly HashSet<PlayerAgent> scoutedPlayers = new();
        private readonly Dictionary<PlayerAgent, int> assignmentResolveMatchday = new();

        public List<PlayerAgent> GetOrCreateYouthPool(string teamName, AgentSquadGenerator generator, float attackStrength, float defenceStrength)
        {
            if (youthPoolByTeamName.TryGetValue(teamName, out List<PlayerAgent> pool))
            {
                return pool;
            }

            pool = new List<PlayerAgent>();

            for (int i = 0; i < ProspectsPerTeam; i++)
            {
                PlayerPosition position = ProspectPositionCycle[i % ProspectPositionCycle.Length];

                // Softer than the senior reserve pool's own 0.85x (see
                // ManagerPrototypeController.GetOrCreateReservePool) - a raw 16-19-year-
                // old prospect being a clear step down from even a senior reserve is the
                // point, not a bug. RerollAgeAndPotentialForYouthProspect below then
                // overrides the generator's own bell-curved mid-20s age roll and
                // recomputes Potential to actually match it.
                PlayerAgent prospect = generator.GenerateReservePlayer(position, attackStrength * 0.7f, defenceStrength * 0.7f);
                RerollAgeAndPotentialForYouthProspect(prospect);

                pool.Add(prospect);
            }

            youthPoolByTeamName[teamName] = pool;
            return pool;
        }

        public List<string> GetTeamNamesWithPools()
        {
            return new List<string>(youthPoolByTeamName.Keys);
        }

        // Save/load restoration hooks (career arc, Phase 5) - injects a pool/scouted
        // status built from saved DTOs rather than freshly generating one, so a loaded
        // career keeps the exact prospects (and scouting progress) it had when saved.
        public void RestoreYouthPool(string teamName, List<PlayerAgent> pool)
        {
            youthPoolByTeamName[teamName] = pool;
        }

        public void RestoreScoutedPlayer(PlayerAgent player)
        {
            scoutedPlayers.Add(player);
        }

        // Regenerates Age (the generator's own GenerateAge() rolls a bell curve centred
        // in the mid-20s, wrong for a youth pool) and, since GenerateNewerAttributes
        // already computed Potential using that wrong age's youthFactor, Potential too -
        // otherwise a genuinely 17-year-old prospect would carry the unremarkable
        // headroom of a mid-20s player. Deliberately a wider, more generous headroom
        // roll than the general population (see AgentSquadGenerator.
        // GenerateNewerAttributes for comparison) - a hidden youth pool exists
        // specifically to occasionally contain a real future star, not just more of the
        // same distribution you'd get anywhere else. This happens well outside any
        // Research Mode seeded-comparison context (deep in live Manager Mode play), so
        // it doesn't need the Random.State-preserving wrap that initial squad
        // generation does.
        private static void RerollAgeAndPotentialForYouthProspect(PlayerAgent prospect)
        {
            prospect.Age = Random.Range(MinProspectAge, MaxProspectAge + 1);

            float youthFactor = Mathf.Clamp01((24f - prospect.Age) / 6f);
            float currentOverall = prospect.GetOverallRating();
            float headroomRoll = Random.Range(5f, 45f) * (0.5f + youthFactor * 0.5f);

            prospect.Potential = Mathf.Clamp(currentOverall + headroomRoll, currentOverall, 99f);
        }

        // Deterministic per player (seeded from PlayerId, via System.Random - never
        // touches the shared UnityEngine.Random stream) rather than re-rolled every UI
        // refresh, so an unscouted prospect's fuzzy band stays stable instead of
        // flickering a new range every time the Scouting screen redraws.
        public string GetDisplayPotential(PlayerAgent player)
        {
            if (scoutedPlayers.Contains(player))
            {
                return Mathf.RoundToInt(player.Potential).ToString();
            }

            System.Random fuzzRandom = new System.Random(player.PlayerId.GetHashCode());
            float noise = (float)(fuzzRandom.NextDouble() * 16f) - 8f;
            float fuzzyCenter = player.Potential + noise;

            int lowerBand = Mathf.Clamp(Mathf.FloorToInt((fuzzyCenter - 7f) / 5f) * 5, 1, 95);
            int upperBand = Mathf.Clamp(lowerBand + 15, lowerBand + 5, 99);

            return $"{lowerBand}-{upperBand}";
        }

        public bool IsScouted(PlayerAgent player) => scoutedPlayers.Contains(player);
        public bool IsAssigned(PlayerAgent player) => assignmentResolveMatchday.ContainsKey(player);
        public int ActiveAssignmentCount => assignmentResolveMatchday.Count;

        // Resolves one matchday later (currentMatchdayIndex is the same "next matchday
        // to be played" index ManagerSquadRoles.IsInjured reads) - same cadence as
        // injury return timing, a familiar established pattern rather than a new one.
        public bool TryAssignScout(PlayerAgent target, int currentMatchdayIndex)
        {
            if (scoutedPlayers.Contains(target) || assignmentResolveMatchday.ContainsKey(target))
            {
                return false;
            }

            if (assignmentResolveMatchday.Count >= MaxConcurrentAssignments)
            {
                return false;
            }

            assignmentResolveMatchday[target] = currentMatchdayIndex + 1;
            return true;
        }

        // Called whenever the matchday index advances (every fixture, not just the
        // managed team's own - scouting reports come in on the game's calendar, not
        // gated on whether you personally played that week).
        public void ResolveDueAssignments(int currentMatchdayIndex)
        {
            List<PlayerAgent> resolved = new List<PlayerAgent>();

            foreach (KeyValuePair<PlayerAgent, int> entry in assignmentResolveMatchday)
            {
                if (currentMatchdayIndex >= entry.Value)
                {
                    resolved.Add(entry.Key);
                }
            }

            foreach (PlayerAgent player in resolved)
            {
                assignmentResolveMatchday.Remove(player);
                scoutedPlayers.Add(player);
            }
        }

        // Season rollover resets currentFixtureIndex back to 0, which would otherwise
        // strand any assignment made near the end of a season with a now-unreachable
        // resolve matchday - the report simply comes in over the off-season instead.
        public void ForceResolveAllPending()
        {
            foreach (PlayerAgent player in assignmentResolveMatchday.Keys)
            {
                scoutedPlayers.Add(player);
            }

            assignmentResolveMatchday.Clear();
        }
    }
}
