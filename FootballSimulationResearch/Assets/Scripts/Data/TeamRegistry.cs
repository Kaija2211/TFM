using System.Collections.Generic;

namespace Data
{
    public class TeamRegistry
    {
        private readonly Dictionary<string, int> teamIds = new();
        private readonly Dictionary<int, string> teamNames = new();
        private int nextTeamId = 1;

        public int GetTeamId(string teamName)
        {
            if (!teamIds.ContainsKey(teamName))
            {
                int id = nextTeamId;

                teamIds[teamName] = id;
                teamNames[id] = teamName;

                nextTeamId++;
            }

            return teamIds[teamName];
        }

        public string GetTeamName(int teamId)
        {
            return teamNames.ContainsKey(teamId)
                ? teamNames[teamId]
                : $"Team {teamId}";
        }
    }
}
