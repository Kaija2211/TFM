using System.Collections.Generic;

namespace Sim
{
    public class AgentTeam
    {
        public string TeamName;
        public List<PlayerAgent> Players = new();

        public AgentTeam(string teamName)
        {
            TeamName = teamName;
        }

        public void AddPlayer(PlayerAgent player)
        {
            Players.Add(player);
        }
    }
}