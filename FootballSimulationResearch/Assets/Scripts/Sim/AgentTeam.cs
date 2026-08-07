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
        //
        // Replaces in place at playerOff's existing index rather than remove+append,
        // so StartingEleven[i] keeps corresponding to whatever formation slot it
        // started in (see AgentSquadGenerator.GetStartingPositions, which returns
        // positions in that same order) - the Tactics Board relies on this to know
        // which pin a substituted-in player renders at.
        public bool SubstitutePlayer(PlayerAgent playerOff, PlayerAgent playerOn)
        {
            int startingIndex = StartingEleven.IndexOf(playerOff);

            if (startingIndex < 0 || !Bench.Contains(playerOn))
            {
                return false;
            }

            StartingEleven[startingIndex] = playerOn;
            Bench.Remove(playerOn);

            playerOff.IsStartingEleven = false;
            playerOn.IsStartingEleven = true;

            Bench.Add(playerOff);

            return true;
        }

        // Reassigns the whole squad to a new formation/shape in one step (Tactics
        // Board formation switch) - newStartingEleven must already be in the same
        // order as GetStartingPositions(newFormation) returns, one player per slot.
        // Everyone else in the squad falls to the bench.
        public void ChangeFormation(Formation newFormation, List<PlayerAgent> newStartingEleven)
        {
            Formation = newFormation;

            List<PlayerAgent> newBench = new();

            foreach (PlayerAgent player in Players)
            {
                bool isStarting = newStartingEleven.Contains(player);
                player.IsStartingEleven = isStarting;

                if (!isStarting)
                {
                    newBench.Add(player);
                }
            }

            StartingEleven = new List<PlayerAgent>(newStartingEleven);
            Bench = newBench;
        }
    }
}