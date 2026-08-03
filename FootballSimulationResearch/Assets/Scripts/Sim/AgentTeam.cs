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

        // Swaps one starter for one bench player. Both must already belong to this
        // squad (StartingEleven/Bench respectively) - returns false without changing
        // anything if either isn't found, so callers can no-op on a stale selection.
        public bool SubstitutePlayer(PlayerAgent playerOff, PlayerAgent playerOn)
        {
            if (!StartingEleven.Contains(playerOff) || !Bench.Contains(playerOn))
            {
                return false;
            }

            StartingEleven.Remove(playerOff);
            Bench.Remove(playerOn);

            playerOff.IsStartingEleven = false;
            playerOn.IsStartingEleven = true;

            Bench.Add(playerOff);
            StartingEleven.Add(playerOn);

            return true;
        }
    }
}