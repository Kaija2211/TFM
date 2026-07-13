using System.Collections.Generic;

namespace Sim
{
    public class AgentTeam
    {
        public string TeamName;
        public Formation Formation;

        public List<PlayerAgent> StartingEleven = new();
        public List<PlayerAgent> Bench = new();

        public List<PlayerAgent> Players = new();

        public AgentTeam(string teamName, Formation formation)
        {
            TeamName = teamName;
            Formation = formation;
        }

        public void AddStarter(PlayerAgent player)
        {
            player.IsStartingEleven = true;

            StartingEleven.Add(player);
            Players.Add(player);
        }

        public void AddBenchPlayer(PlayerAgent player)
        {
            player.IsStartingEleven = false;

            Bench.Add(player);
            Players.Add(player);
        }
    }
}