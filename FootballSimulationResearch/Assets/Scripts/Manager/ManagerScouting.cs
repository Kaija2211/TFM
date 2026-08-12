using System.Collections.Generic;
using UnityEngine;
using Manager.Save;
using Sim;

namespace Manager
{
    // Youth scouting (career arc, Phase 2, session 8; mission-based rework session 13)
    // - Thomas's pitch, replacing the old fixed "10 pre-generated prospects per region,
    // assign a scout to reveal ONE existing entry's stats" model entirely: the Youth
    // page starts with nobody in it. Two scout slots, each briefed with up to 3 target
    // positions, run indefinitely - every matchday an active mission has a flat chance
    // to surface a batch of 2-3 brand-new prospects at its briefed positions (session
    // 16 - a single hit used to yield just one, which Thomas felt made finding a real
    // wonderkid feel like a multi-season grind), added to a single growing discovered
    // list. A discovery IS the scouting act (finding them and
    // knowing their real stats happen together, unlike Transfer Market's "reveal a
    // known AI player's hidden stats" scouting) - only Potential stays permanently
    // fuzzy (GetDisplayPotential, unchanged from before), since a scout can assess
    // current ability on sight but a ceiling is always somewhat speculative. High
    // Overalls are deliberately NOT re-biased rare here on top of the base generation -
    // the session 12 attribute-overhaul calibration already makes a genuinely elite
    // individual roll rare on its own (0/3000 rolls hit an unboosted ceiling in that
    // session's sample), so "a real treat" falls out of the existing math for free.
    //
    // Real stakes for sitting on a discovery (Thomas, session 13): a batch found on a
    // given matchday is poached by another club if left unclaimed for
    // MatchdaysUntilPoached matchdays - claiming means bringing them into an empty
    // Academy slot (see ManagerAcademy/ManagerPrototypeController.OnBringInScoutedPlayerClicked),
    // the only exit from this list. No AI-vs-AI transfer activity backs the "poached"
    // flavor (same explicit scope boundary as everywhere else in this project) - it's
    // just a countdown, not a simulated rival signing.
    public class ManagerScouting
    {
        public const int ScoutSlots = 2;
        public const int MaxTargetPositions = 3;
        private const int MinYouthAge = 14;
        private const int MaxYouthAge = 19;
        private const float DiscoveryChancePerActiveMissionPerMatchday = 0.3f;
        public const int MatchdaysUntilPoached = 3;

        private readonly List<PlayerPosition>[] missionPositions = new List<PlayerPosition>[ScoutSlots];
        private readonly List<PlayerAgent> discoveredProspects = new();
        private readonly Dictionary<PlayerAgent, int> discoveredMatchday = new();

        // Deliberately randomized per career rather than a fixed hierarchy - see the
        // original world-scattered rework's own reasoning (unchanged this session, just
        // now feeding mission discoveries instead of a fixed pool).
        private Dictionary<string, float> regionalQualityBiasByRegion;

        public ManagerScouting()
        {
            for (int i = 0; i < ScoutSlots; i++) missionPositions[i] = new List<PlayerPosition>();
        }

        public IReadOnlyList<PlayerPosition> GetMissionPositions(int slotIndex) => missionPositions[slotIndex];
        public bool IsMissionActive(int slotIndex) => missionPositions[slotIndex].Count > 0;

        public void SetMissionBrief(int slotIndex, List<PlayerPosition> positions)
        {
            List<PlayerPosition> trimmed = new List<PlayerPosition>();
            foreach (PlayerPosition p in positions)
            {
                if (trimmed.Count >= MaxTargetPositions) break;
                if (!trimmed.Contains(p)) trimmed.Add(p);
            }

            missionPositions[slotIndex] = trimmed;
        }

        public void CancelMission(int slotIndex)
        {
            missionPositions[slotIndex].Clear();
        }

        // Session 16 - a brand new career starting mid-session (OnConfirmTeamClicked)
        // never reset this, so a second career in the same Play Mode/app session opened
        // with the previous career's scout missions and discovered prospects still
        // attached. regionalQualityBiasByRegion is set back to null rather than cleared
        // in place - see its own comment ("deliberately randomized per career") - so it
        // re-randomizes fresh the next time it's lazily needed, instead of carrying the
        // old career's regional bias into the new one.
        public void Clear()
        {
            for (int i = 0; i < ScoutSlots; i++) missionPositions[i].Clear();
            discoveredProspects.Clear();
            discoveredMatchday.Clear();
            regionalQualityBiasByRegion = null;
        }

        public IReadOnlyList<PlayerAgent> DiscoveredProspects => discoveredProspects;

        public int GetDiscoveredMatchday(PlayerAgent prospect)
        {
            return discoveredMatchday.TryGetValue(prospect, out int md) ? md : 0;
        }

        public int GetMatchdaysUntilPoached(PlayerAgent prospect, int currentMatchdayIndex)
        {
            int deadline = GetDiscoveredMatchday(prospect) + MatchdaysUntilPoached;
            return Mathf.Max(0, deadline - currentMatchdayIndex);
        }

        // Called from the matchday-tick hooks - rolls each active mission for a new
        // discovery, then sweeps for anyone whose window has run out. inbox/
        // currentMatchdayIndex both passed rather than held, matching every other
        // system's "no held controller reference" convention.
        public void ResolveMatchdayTick(int currentMatchdayIndex, AgentSquadGenerator generator, ManagerInbox inbox)
        {
            for (int slot = 0; slot < ScoutSlots; slot++)
            {
                if (!IsMissionActive(slot)) continue;
                if (Random.value > DiscoveryChancePerActiveMissionPerMatchday) continue;

                List<PlayerPosition> positions = missionPositions[slot];

                // Session 16 - Thomas: "MAYBE one every few matchdays... I doubt anyone's
                // going to find a wonderkid in a save like that... every time a scout
                // finds one, you get a few?" A batch per successful roll rather than
                // raising DiscoveryChancePerActiveMissionPerMatchday itself - keeps the
                // same "does something happen this matchday" cadence, just makes each hit
                // worth more. 2-3 per hit, each independently rolled against the mission's
                // own briefed positions (so a batch can span more than one position).
                int batchSize = Random.Range(2, 4);

                for (int i = 0; i < batchSize; i++)
                {
                    PlayerPosition position = positions[Random.Range(0, positions.Count)];

                    PlayerAgent prospect = GenerateDiscovery(position, generator);
                    discoveredProspects.Add(prospect);
                    discoveredMatchday[prospect] = currentMatchdayIndex;

                    inbox.Add(InboxMessageType.ScoutingReport, $"Scout Find: {prospect.Name}",
                        $"One of your scouts has found {prospect.Name} ({prospect.PrimaryPosition}, age {prospect.Age}) while searching for a {position}. " +
                        $"True Overall {Mathf.RoundToInt(prospect.GetOverallRating())}, Potential {GetDisplayPotential(prospect)}. " +
                        $"Bring them into an empty Academy slot within {MatchdaysUntilPoached} matchdays or another club may snap them up.",
                        currentMatchdayIndex);
                }
            }

            List<PlayerAgent> poached = new List<PlayerAgent>();
            foreach (PlayerAgent prospect in discoveredProspects)
            {
                if (currentMatchdayIndex - GetDiscoveredMatchday(prospect) >= MatchdaysUntilPoached)
                {
                    poached.Add(prospect);
                }
            }

            foreach (PlayerAgent prospect in poached)
            {
                discoveredProspects.Remove(prospect);
                discoveredMatchday.Remove(prospect);

                inbox.Add(InboxMessageType.BidDeclined, $"Prospect Lost: {prospect.Name}",
                    $"{prospect.Name} has signed for another club - you didn't bring them into the Academy in time.",
                    currentMatchdayIndex);
            }
        }

        // Shared generation - regional quality bias/age discount curve unchanged from
        // the old pool-fill logic, just triggered per-discovery instead of up front.
        private PlayerAgent GenerateDiscovery(PlayerPosition position, AgentSquadGenerator generator)
        {
            int age = Random.Range(MinYouthAge, MaxYouthAge + 1);

            // Region rolled directly from AllRegions (world-scattered, unaffiliated -
            // same spirit as the original pool-fill's per-region generation) rather than
            // via a real PlayerAgent's own nationality roll - there's no existing
            // prospect yet at this point to roll one off.
            List<string> regions = new List<string>(ManagerPlayerNationality.AllRegions);
            string region = regions[Random.Range(0, regions.Count)];

            float regionalQuality = GetRegionalQualityMultiplier(region);
            float ageDiscount = Mathf.Lerp(0.55f, 0.78f, (float)(age - MinYouthAge) / (MaxYouthAge - MinYouthAge));
            float combinedFactor = ageDiscount * regionalQuality;

            // DefenceStrength is inverted in AgentSquadGenerator (lower = a better
            // defence), so a genuine discount divides it rather than multiplying like
            // AttackStrength does - see feedback_defencestrength_inverted in memory.
            PlayerAgent prospect = generator.GenerateReservePlayer(position, 1f * combinedFactor, 1f / combinedFactor);
            ApplyProspectAgeAndPotential(prospect, age);
            ManagerPlayerNationality.SetNationality(prospect, ManagerPlayerNationality.GetRandomNationInRegion(region));

            return prospect;
        }

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

            for (int i = 0; i < hotCount; i++) regionalQualityBiasByRegion[shuffled[i]] = 1f;
            for (int i = hotCount; i < hotCount + quietCount; i++) regionalQualityBiasByRegion[shuffled[i]] = -1f;
        }

        // Removes a claimed prospect from the discovered list - called when brought
        // into an empty Academy slot (the only real exit from this list besides being
        // poached). Also clears its poach-timer entry, same "no dangling per-reference
        // state on a claimed/replaced player" precedent as the old pool's expiry logic.
        public bool RemoveDiscoveredProspect(PlayerAgent prospect)
        {
            discoveredMatchday.Remove(prospect);
            return discoveredProspects.Remove(prospect);
        }

        // Same bell-curved headroom roll as before (see feedback_generation_bell_curve_
        // not_hard_range in memory) - a hidden discovery pool exists specifically to
        // occasionally contain a real future star, not just more of the same
        // distribution you'd get anywhere else.
        private static void ApplyProspectAgeAndPotential(PlayerAgent prospect, int age)
        {
            prospect.Age = age;

            float youthFactor = Mathf.Clamp01((24f - age) / 10f);
            float currentOverall = prospect.GetOverallRating();
            float headroomRoll = RollHeadroom(-10f, 25f) * (0.5f + youthFactor * 0.5f);

            prospect.Potential = Mathf.Clamp(currentOverall + headroomRoll, currentOverall, 99f);
        }

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
        // refresh, so a discovered prospect's fuzzy Potential band stays stable instead
        // of flickering a new range every redraw. A ceiling stays speculative even once
        // the player themselves has been found and their current ability is known.
        public string GetDisplayPotential(PlayerAgent player)
        {
            System.Random fuzzRandom = new System.Random(player.PlayerId.GetHashCode());
            float noise = (float)(fuzzRandom.NextDouble() * 16f) - 8f;
            float fuzzyCenter = player.Potential + noise;

            int lowerBand = Mathf.Clamp(Mathf.FloorToInt((fuzzyCenter - 7f) / 5f) * 5, 1, 95);
            int upperBand = Mathf.Clamp(lowerBand + 15, lowerBand + 5, 99);

            return $"{lowerBand}-{upperBand}";
        }

        // Season rollover - discovered-but-unclaimed prospects keep developing exactly
        // like the old pool did (same AssumedPlayingTimeFactorYouthProspect caller in
        // ManagerPrototypeController), just iterated off the flat list now instead of
        // per-region pools. No expiry-by-age logic anymore - the 3-matchday poach timer
        // already keeps this list from accumulating indefinitely, so aging out is no
        // longer needed on top of it.
        public void AgeDiscoveredProspects()
        {
            foreach (PlayerAgent prospect in discoveredProspects)
            {
                prospect.Age += 1;
            }
        }

        // Save/load restoration.
        public void RestoreDiscoveredProspects(List<PlayerAgent> prospects, List<int> matchdaysFound)
        {
            discoveredProspects.Clear();
            discoveredMatchday.Clear();

            for (int i = 0; i < prospects.Count; i++)
            {
                discoveredProspects.Add(prospects[i]);
                discoveredMatchday[prospects[i]] = i < matchdaysFound.Count ? matchdaysFound[i] : 0;
            }
        }

        public void RestoreMissionBrief(int slotIndex, List<PlayerPosition> positions)
        {
            missionPositions[slotIndex] = new List<PlayerPosition>(positions);
        }
    }
}
