using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Youth prospect pools + scouting (career arc, Phase 2, session 8) - a hidden layer
    // beneath the reserve pool (see ManagerPrototypeController.reservePoolByTeamName):
    // a handful of age-16-19 players, generated the same way but never surfaced in
    // normal squad/transfer browsing. Discovery is the whole point - you don't already
    // see every hidden gem, you have to scout them. One instance for the whole career
    // (not per-team, unlike ManagerSquadRoles) since scouting knowledge is a manager-
    // level resource, not tied to any one squad.
    //
    // World-scattered rework (session 9) - prospects were originally cosmetically
    // tagged to a real Premier League club (buying one never touched that club's real
    // squad, so the tag was already a bit dishonest - see HANDOFF). Now pooled by
    // REGION instead (see ManagerPlayerNationality.AllRegions) - genuinely unaffiliated
    // free agents with a real nationality drawn from that region, not tied to any club
    // at all. Reuses the exact same Dictionary<string, List<PlayerAgent>> keyed-pool
    // shape the old per-club version had (region name is just a different kind of key),
    // so every caller's loop-over-keys pattern carries over unchanged.
    public class ManagerScouting
    {
        public const int ProspectsPerRegion = 10;
        private const int MinProspectAge = 16;
        private const int MaxProspectAge = 19;
        public const int MaxConcurrentAssignments = 2;

        private static readonly PlayerPosition[] ProspectPositionCycle =
        {
            PlayerPosition.CB, PlayerPosition.CM, PlayerPosition.ST,
            PlayerPosition.RW, PlayerPosition.LB, PlayerPosition.AM
        };

        private readonly Dictionary<string, List<PlayerAgent>> youthPoolByRegion = new();
        private readonly HashSet<PlayerAgent> scoutedPlayers = new();
        private readonly Dictionary<PlayerAgent, int> assignmentResolveMatchday = new();

        // Which regions are producing exceptional talent THIS career - rolled once,
        // lazily, the first time any pool is generated, then stable for the rest of
        // this ManagerScouting instance's life (same lifetime as the per-club pools
        // already had). Deliberately randomized per career rather than a fixed bias
        // baked into the code - see GetRegionalQualityMultiplier for why.
        private Dictionary<string, float> regionalQualityBiasByRegion;

        public List<PlayerAgent> GetOrCreateYouthPool(string region, AgentSquadGenerator generator)
        {
            if (youthPoolByRegion.TryGetValue(region, out List<PlayerAgent> pool))
            {
                return pool;
            }

            pool = new List<PlayerAgent>();

            for (int i = 0; i < ProspectsPerRegion; i++)
            {
                PlayerPosition position = ProspectPositionCycle[i % ProspectPositionCycle.Length];
                pool.Add(GenerateProspect(region, position, generator));
            }

            youthPoolByRegion[region] = pool;
            return pool;
        }

        // Shared by the pool's initial fill above and the expiry replacement below -
        // both need "roll a fresh 16-19-year-old for this region, at this region's
        // current quality bias," just at different times.
        private PlayerAgent GenerateProspect(string region, PlayerPosition position, AgentSquadGenerator generator)
        {
            float regionalQuality = GetRegionalQualityMultiplier(region);
            int prospectAge = Random.Range(MinProspectAge, MaxProspectAge + 1);

            // Softer than the senior reserve pool's own 0.85x (see
            // ManagerPrototypeController.GetOrCreateReservePool) - a raw 16-19-year-
            // old prospect being a clear step down from even a senior reserve is the
            // point, not a bug. Age-scaled rather than a flat factor: a 16-year-old
            // should look like a genuine long-term project, not already the same
            // quality step-down a 19-year-old gets. Live-sampled and re-tuned against
            // realistic club-strength inputs after fixing the DefenceStrength
            // direction bug below (see HANDOFF) - final numbers verified live.
            // Age is rolled before generation now (not after, like the old
            // RerollAgeAndPotentialForYouthProspect did) specifically so this
            // discount can depend on it.
            float ageDiscount = Mathf.Lerp(0.6f, 0.78f, (float)(prospectAge - MinProspectAge) / (MaxProspectAge - MinProspectAge));

            // No real club to scale off anymore (world-scattered rework) - baseline
            // 1.0 (average) combined with the age discount and this region's
            // quality bias for this career, same combined-factor role a real club's
            // AttackStrength/DefenceStrength used to play.
            float combinedFactor = ageDiscount * regionalQuality;

            // DefenceStrength is inverted in AgentSquadGenerator (defenceMultiplier =
            // 1/defenceStrength - lower DefenceStrength means a BETTER defence), so a
            // genuine discount divides it rather than multiplying like AttackStrength
            // does. See the same fix and its live-verified numbers in
            // ManagerPrototypeController.GetOrCreateReservePool.
            PlayerAgent prospect = generator.GenerateReservePlayer(position, 1f * combinedFactor, 1f / combinedFactor);
            ApplyProspectAgeAndPotential(prospect, prospectAge);

            ManagerPlayerNationality.SetNationality(prospect, ManagerPlayerNationality.GetRandomNationInRegion(region));

            return prospect;
        }

        // Expiry/refresh (backlog item, floated 2026-08-10 session 9 - Thomas: "I feel
        // like it should expire, or maybe they get snatched by other clubs if you're
        // too slow"). Went with age-out-and-replace over fake AI-club poaching, per the
        // reasoning already recorded when this was floated: poaching would mean
        // inventing an AI-vs-AI transfer economy from scratch just for this one screen,
        // when this whole system already has zero AI-vs-AI transfer activity by design.
        // ExpiryAge (22) is 3 years past MaxProspectAge (19) - long enough that a
        // prospect genuinely had multiple real chances to be scouted/signed before
        // aging out, not a hair-trigger churn. Called alongside the existing per-season
        // aging tick (see ManagerPrototypeController.AgeAndReloadFixturesForNewSeason)
        // rather than a new hook - reuses the exact cadence Thomas's own phrasing
        // ("expire") implies: a season-boundary event, not a per-matchday one.
        private const int ExpiryAge = 22;

        public void AgeAndExpireProspects(string region, AgentSquadGenerator generator)
        {
            if (!youthPoolByRegion.TryGetValue(region, out List<PlayerAgent> pool))
            {
                return;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                PlayerAgent prospect = pool[i];
                prospect.Age += 1;

                if (prospect.Age <= ExpiryAge)
                {
                    continue;
                }

                // Clears any scouting knowledge/in-flight assignment on the expiring
                // prospect - both are keyed by PlayerAgent reference (see scoutedPlayers/
                // assignmentResolveMatchday above), and the replacement is a genuinely
                // new, unscouted PlayerAgent instance, not this one with fields reset.
                scoutedPlayers.Remove(prospect);
                assignmentResolveMatchday.Remove(prospect);

                PlayerPosition position = ProspectPositionCycle[i % ProspectPositionCycle.Length];
                pool[i] = GenerateProspect(region, position, generator);
            }
        }

        public List<string> GetPoolRegions()
        {
            return new List<string>(youthPoolByRegion.Keys);
        }

        // Deliberately randomized per career rather than a fixed hierarchy baked into
        // the code - a hard-coded "Region X always produces better prospects" would be
        // a permanent real-world claim sitting in the software forever. Instead, 1-2
        // regions roll "hot" and 1-2 roll "quiet" fresh each career (everyone else sits
        // at baseline), giving the same "where's good to scout this time" strategic
        // layer Thomas asked for without ever fixing which real nations that is.
        private float GetRegionalQualityMultiplier(string region)
        {
            if (regionalQualityBiasByRegion == null)
            {
                RollRegionalHotbeds();
            }

            float bias = regionalQualityBiasByRegion.TryGetValue(region, out float b) ? b : 0f;
            return 1f + bias * 0.15f;
        }

        private void RollRegionalHotbeds()
        {
            regionalQualityBiasByRegion = new Dictionary<string, float>();
            List<string> shuffled = new List<string>(ManagerPlayerNationality.AllRegions);

            foreach (string region in shuffled)
            {
                regionalQualityBiasByRegion[region] = 0f;
            }

            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            int hotCount = Mathf.Min(2, shuffled.Count);
            int quietCount = Mathf.Min(2, Mathf.Max(0, shuffled.Count - hotCount));

            for (int i = 0; i < hotCount; i++)
            {
                regionalQualityBiasByRegion[shuffled[i]] = 1f;
            }

            for (int i = hotCount; i < hotCount + quietCount; i++)
            {
                regionalQualityBiasByRegion[shuffled[i]] = -1f;
            }
        }

        // Save/load restoration hooks (career arc, Phase 5) - injects a pool/scouted
        // status built from saved DTOs rather than freshly generating one, so a loaded
        // career keeps the exact prospects (and scouting progress) it had when saved.
        public void RestoreYouthPool(string region, List<PlayerAgent> pool)
        {
            youthPoolByRegion[region] = pool;
        }

        public void RestoreScoutedPlayer(PlayerAgent player)
        {
            scoutedPlayers.Add(player);
        }

        // Overrides the generator's own Age (GenerateAge() rolls a bell curve centred
        // in the mid-20s, wrong for a youth pool - the caller now rolls the real
        // prospect age itself, before generation, so the attribute discount above can
        // depend on it) and recomputes Potential to match. Deliberately a wider, more
        // generous headroom roll than the general population (see AgentSquadGenerator.
        // GenerateNewerAttributes for comparison) - a hidden youth pool exists
        // specifically to occasionally contain a real future star, not just more of the
        // same distribution you'd get anywhere else. This happens well outside any
        // Research Mode seeded-comparison context (deep in live Manager Mode play), so
        // it doesn't need the Random.State-preserving wrap that initial squad
        // generation does.
        private static void ApplyProspectAgeAndPotential(PlayerAgent prospect, int age)
        {
            prospect.Age = age;

            float youthFactor = Mathf.Clamp01((24f - age) / 6f);
            float currentOverall = prospect.GetOverallRating();

            // Bell-curved headroom (same shape as AgentSquadGenerator.RollAttribute)
            // instead of the old flat Random.Range - a huge headroom value should be a
            // rare tail, not evenly likely, matching every other stat roll in the
            // project (see feedback_generation_bell_curve_not_hard_range in memory).
            // Range live-tuned against real generated prospects (see HANDOFF) - the
            // first attempt at (5,45) kept the mean itself (25) roughly equal to the
            // gap needed to reach 90 Potential, so bell-curving the *shape* alone barely
            // moved the 90+ rate (still 50%+). (-10,25) drops the mean to 7.5, making
            // 90+ Potential a genuine minority outcome instead of a coin flip.
            float headroomRoll = RollHeadroom(-10f, 25f) * (0.5f + youthFactor * 0.5f);

            prospect.Potential = Mathf.Clamp(currentOverall + headroomRoll, currentOverall, 99f);
        }

        // Local bell-curve helper (Box-Muller), mirroring AgentSquadGenerator.
        // RollAttribute - duplicated rather than shared since that method is private
        // inside the protected Sim/ file, which doesn't get logic changes.
        private static float RollHeadroom(float min, float max)
        {
            float mean = (min + max) / 2f;
            float stdDev = (max - min) / 4f;
            float u1 = 1f - Random.value;
            float u2 = 1f - Random.value;
            float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return mean + (stdDev * standardNormal);
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
