using System.Collections.Generic;

namespace Sim
{
    public class LeagueTable
    {
        public class Entry
        {
            public int TeamId;
            public int Played;
            public int Wins;
            public int Draws;
            public int Losses;
            public int GoalsFor;
            public int GoalsAgainst;
            public int Points;
        }

        private readonly Dictionary<int, Entry> _entries = new();

        private Entry Get(int teamId)
        {
            if (!_entries.TryGetValue(teamId, out var e))
            {
                e = new Entry { TeamId = teamId };
                _entries[teamId] = e;
            }
            return e;
        }

        // Registers a team at 0 played/0 points if it doesn't already have an entry,
        // without affecting anything already recorded. Lets a consumer (Manager Mode)
        // show the full league before any results exist, matching how a real table
        // looks on matchday 1 - purely additive, Apply/Sorted/Get are unchanged.
        public void EnsureTeam(int teamId)
        {
            Get(teamId);
        }

        // Clears every entry - used by Manager Mode's season rollover to start a new
        // season's table from a clean slate rather than carrying last season's Played/
        // Points forward. Purely additive; Apply/Sorted/Get/EnsureTeam are unchanged.
        public void Reset()
        {
            _entries.Clear();
        }

        // Directly sets an entry's full stat line - used by Manager Mode's save/load to
        // restore a mid-season table without replaying every match that produced it.
        // Purely additive; Apply/Sorted/Get/EnsureTeam are unchanged.
        public void SetEntry(int teamId, int played, int wins, int draws, int losses, int goalsFor, int goalsAgainst, int points)
        {
            Entry e = Get(teamId);
            e.Played = played;
            e.Wins = wins;
            e.Draws = draws;
            e.Losses = losses;
            e.GoalsFor = goalsFor;
            e.GoalsAgainst = goalsAgainst;
            e.Points = points;
        }

        public void Apply(Sim.MatchRecord m)
        {
            var home = Get(m.HomeTeamId);
            var away = Get(m.AwayTeamId);

            home.Played++; away.Played++;
            home.GoalsFor += m.HomeGoals; home.GoalsAgainst += m.AwayGoals;
            away.GoalsFor += m.AwayGoals; away.GoalsAgainst += m.HomeGoals;

            if (m.HomeGoals > m.AwayGoals)
            {
                home.Wins++; home.Points += 3;
                away.Losses++;
            }
            else if (m.HomeGoals < m.AwayGoals)
            {
                away.Wins++; away.Points += 3;
                home.Losses++;
            }
            else
            {
                home.Draws++; home.Points += 1;
                away.Draws++; away.Points += 1;
            }
        }

        public List<Entry> Sorted()
        {
            var list = new List<Entry>(_entries.Values);
            list.Sort((a, b) =>
            {
                int cmp = b.Points.CompareTo(a.Points);
                if (cmp != 0) return cmp;

                int gdA = a.GoalsFor - a.GoalsAgainst;
                int gdB = b.GoalsFor - b.GoalsAgainst;
                cmp = gdB.CompareTo(gdA);
                if (cmp != 0) return cmp;

                return b.GoalsFor.CompareTo(a.GoalsFor);
            });
            return list;
        }
    }
}