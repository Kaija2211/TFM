using System.Collections.Generic;

namespace Sim
{
    public class AgentTeam
    {
        public string TeamName;
        public Formation Formation;

        public List<PlayerAgent> StartingEleven = new();
        public List<PlayerAgent> Bench = new();
        public List<PlayerAgent> Reserves = new();

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

        public void AddReservePlayer(PlayerAgent player)
        {
            player.IsStartingEleven = false;
            Reserves.Add(player);
            Players.Add(player);
        }

        public void AddSquadPlayer(PlayerAgent player)
        {
            if (Bench.Count < 9) AddBenchPlayer(player);
            else AddReservePlayer(player);
        }

        public bool PromoteReserveToBench(PlayerAgent player)
        {
            if (!Reserves.Remove(player)) return false;

            player.IsStartingEleven = false;
            if (Bench.Count >= 9)
            {
                PlayerAgent demoted = Bench[Bench.Count - 1];
                Bench.RemoveAt(Bench.Count - 1);
                Reserves.Add(demoted);
            }

            Bench.Add(player);
            return true;
        }

        public bool SwapBenchAndReserve(PlayerAgent benchPlayer, PlayerAgent reservePlayer)
        {
            int benchIndex = Bench.IndexOf(benchPlayer);
            int reserveIndex = Reserves.IndexOf(reservePlayer);
            if (benchIndex < 0 || reserveIndex < 0) return false;

            Bench[benchIndex] = reservePlayer;
            Reserves[reserveIndex] = benchPlayer;
            benchPlayer.IsStartingEleven = false;
            reservePlayer.IsStartingEleven = false;
            return true;
        }

        public bool RemovePlayer(PlayerAgent player)
        {
            bool removed = StartingEleven.Remove(player);
            removed |= Bench.Remove(player);
            removed |= Reserves.Remove(player);
            removed |= Players.Remove(player);
            return removed;
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

        // Swaps two starters' positions within the XI (e.g. dragging the ST onto the LM
        // pin after a formation change scattered them) - unlike SubstitutePlayer, both
        // players stay on the pitch, nobody moves to/from the Bench. Manager Mode only,
        // same as SubstitutePlayer/ChangeFormation below.
        public bool SwapStartingPositions(PlayerAgent a, PlayerAgent b)
        {
            int indexA = StartingEleven.IndexOf(a);
            int indexB = StartingEleven.IndexOf(b);

            if (indexA < 0 || indexB < 0)
            {
                return false;
            }

            StartingEleven[indexA] = b;
            StartingEleven[indexB] = a;

            return true;
        }

        // Reassigns the whole squad to a new formation/shape in one step (Tactics
        // Board formation switch) - newStartingEleven must already be in the same
        // order as GetStartingPositions(newFormation) returns, one player per slot.
        // Everyone else in the squad falls to the bench.
        public void ChangeFormation(Formation newFormation, List<PlayerAgent> newStartingEleven)
        {
            Formation = newFormation;

            List<PlayerAgent> available = new();
            foreach (PlayerAgent player in StartingEleven)
                if (!newStartingEleven.Contains(player) && !available.Contains(player)) available.Add(player);
            foreach (PlayerAgent player in Bench)
                if (!newStartingEleven.Contains(player) && !available.Contains(player)) available.Add(player);
            foreach (PlayerAgent player in Reserves)
                if (!newStartingEleven.Contains(player) && !available.Contains(player)) available.Add(player);

            List<PlayerAgent> newBench = new();
            List<PlayerAgent> newReserves = new();

            foreach (PlayerAgent player in Players)
            {
                bool isStarting = newStartingEleven.Contains(player);
                player.IsStartingEleven = isStarting;

                if (!isStarting)
                {
                    if (available.Contains(player) && newBench.Count < 9) newBench.Add(player);
                    else newReserves.Add(player);
                }
            }

            StartingEleven = new List<PlayerAgent>(newStartingEleven);
            Bench = newBench;
            Reserves = newReserves;
        }
    }
}
