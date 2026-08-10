using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Player nationalities (career-arc backlog item, session 9) - pure cosmetic flavor
    // data. Deliberately NOT added to PlayerAgent.cs despite conceptually being inherent
    // player identity (like Age/Height) - keeping it in a Manager-side lookup instead
    // avoids needing fresh per-session authorization to touch the protected Sim/ files
    // (see feedback_protected_file_rng_safe_technique in memory: authorization is per-
    // session, doesn't carry forward automatically), and matches every other "new per-
    // player state" addition already made this session (delta tracking, scouting
    // status, etc. all live in Manager-namespace classes, never on PlayerAgent).
    //
    // Assigned LAZILY the first time any Manager Mode code asks for a player's
    // nationality, not at generation time - works uniformly across every pool (first
    // team, reserves, scouted prospects) without needing a hook at each of the several
    // generation call sites scattered across ManagerPrototypeController/ManagerScouting.
    // Stable once assigned (cached), not persisted through save/load - same accepted
    // scope limit as the delta badge and everything else Manager-only per-player state
    // this session (a loaded career re-rolling flavor-only nationalities is harmless,
    // unlike Condition/injuries which affect real gameplay).
    public static class ManagerPlayerNationality
    {
        public readonly struct Nation
        {
            public readonly string Name;
            public readonly string Region;

            public Nation(string name, string region)
            {
                Name = name;
                Region = region;
            }
        }

        // Broad, real footballing regions - genuinely global, not weighted toward any
        // one being "better" in the data itself (see GetRegionalQualityMultiplier for
        // where any bias actually lives, and why it's randomized per career rather than
        // fixed here).
        private static readonly Nation[] AllNations =
        {
            new("England", "Western Europe"), new("Wales", "Western Europe"), new("Scotland", "Western Europe"),
            new("Ireland", "Western Europe"), new("France", "Western Europe"), new("Germany", "Western Europe"),
            new("Netherlands", "Western Europe"), new("Belgium", "Western Europe"), new("Portugal", "Western Europe"),
            new("Spain", "Western Europe"), new("Italy", "Western Europe"),

            new("Poland", "Eastern Europe"), new("Serbia", "Eastern Europe"), new("Croatia", "Eastern Europe"),
            new("Ukraine", "Eastern Europe"), new("Czech Republic", "Eastern Europe"), new("Romania", "Eastern Europe"),

            new("Brazil", "South America"), new("Argentina", "South America"), new("Uruguay", "South America"),
            new("Colombia", "South America"), new("Chile", "South America"), new("Ecuador", "South America"),

            new("Nigeria", "Africa"), new("Ghana", "Africa"), new("Ivory Coast", "Africa"),
            new("Senegal", "Africa"), new("Cameroon", "Africa"), new("Morocco", "Africa"), new("Egypt", "Africa"),

            new("United States", "North America"), new("Canada", "North America"), new("Mexico", "North America"),

            new("Japan", "Asia-Pacific"), new("South Korea", "Asia-Pacific"), new("Australia", "Asia-Pacific"),
        };

        private static readonly Dictionary<PlayerAgent, Nation> nationalityByPlayer = new();

        public static Nation GetNationality(PlayerAgent player)
        {
            if (!nationalityByPlayer.TryGetValue(player, out Nation nation))
            {
                nation = AllNations[Random.Range(0, AllNations.Length)];
                nationalityByPlayer[player] = nation;
            }

            return nation;
        }

        // Distinct list of every region name, for anything that wants to enumerate
        // regions rather than individual nations (e.g. the scouting hotbed roll).
        public static IEnumerable<string> AllRegions
        {
            get
            {
                HashSet<string> seen = new();
                foreach (Nation nation in AllNations)
                {
                    if (seen.Add(nation.Region))
                    {
                        yield return nation.Region;
                    }
                }
            }
        }

        // Explicitly assigns a nation from within a given region - used by the
        // world-scattered scouting rework (ManagerScouting) so a prospect's nationality
        // and its regional quality tier are drawn from the same region, not two
        // independent rolls that could disagree.
        public static Nation GetRandomNationInRegion(string region)
        {
            List<Nation> matches = new();
            foreach (Nation nation in AllNations)
            {
                if (nation.Region == region)
                {
                    matches.Add(nation);
                }
            }

            return matches[Random.Range(0, matches.Count)];
        }

        public static void SetNationality(PlayerAgent player, Nation nation)
        {
            nationalityByPlayer[player] = nation;
        }
    }
}
