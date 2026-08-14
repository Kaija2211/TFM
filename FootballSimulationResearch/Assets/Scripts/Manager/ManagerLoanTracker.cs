using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Loan system (session 9 backlog item, motivated by the neglect-erosion mechanic -
    // Thomas: "perhaps we make loans a thing in that case", so a young player blocked
    // from the first XI has a real answer besides "start them anyway" or "accept the
    // erosion" - see project_player_development_glidepath_and_erosion in memory).
    //
    // Scope per Thomas's own decisions: any squad player can be loaned (not just
    // fringe/reserve players), the destination is picked automatically (no negotiation/
    // acceptance flow), duration is fixed to the end of the current season (auto-
    // returns at rollover, no manual recall), and it's a free loan (no fee/wage impact -
    // no need to model another club's budget for this).
    //
    // Deliberately just record-keeping + flavor text here - the actual squad-list
    // surgery (removing the player from StartingEleven/Bench, backfilling a starter's
    // slot) lives in ManagerPrototypeController.OnLoanOutClicked, since it needs
    // ManagerPrototypeController's own FindFitBenchReplacement/CallUpReservePlayer
    // helpers, which this class has no reason to duplicate.
    public class ManagerLoanTracker
    {
        public readonly struct LoanRecord
        {
            public readonly PlayerAgent Player;
            public readonly string OriginTeamName;
            public readonly string DestinationFlavorName;

            public LoanRecord(PlayerAgent player, string originTeamName, string destinationFlavorName)
            {
                Player = player;
                OriginTeamName = originTeamName;
                DestinationFlavorName = destinationFlavorName;
            }
        }

        private readonly List<LoanRecord> activeLoans = new();

        public IReadOnlyList<LoanRecord> ActiveLoans => activeLoans;

        public bool IsOnLoan(PlayerAgent player)
        {
            foreach (LoanRecord loan in activeLoans)
            {
                if (loan.Player == player)
                {
                    return true;
                }
            }

            return false;
        }

        // Returns the destination flavor name assigned, so the caller can show it in a
        // status message immediately.
        public string SendOnLoan(PlayerAgent player, string originTeamName)
        {
            string destination = LoanDestinationFlavorNames[Random.Range(0, LoanDestinationFlavorNames.Length)];
            activeLoans.Add(new LoanRecord(player, originTeamName, destination));
            return destination;
        }

        // Called once per season rollover - hands back every current loan record so the
        // caller can return each player to their origin squad's Bench and apply a
        // season's development credit, then clears this tracker for the new season.
        public List<LoanRecord> ReturnAllLoansForNewSeason()
        {
            List<LoanRecord> returned = new List<LoanRecord>(activeLoans);
            activeLoans.Clear();
            return returned;
        }

        // Loading a save should start from a clean slate, same as squadsByTeamName/
        // reservePoolByTeamName/etc. being cleared in ApplySaveData - otherwise a stale
        // loan from a different career started earlier in the same Play Mode session
        // could leak into the freshly loaded one.
        public void Clear()
        {
            activeLoans.Clear();
        }

        // Pure flavor text - no actual AI-club roster is touched (same "no AI-vs-AI
        // transfer activity" scope limit as the rest of the transfer market, see
        // PROJECT_CONTEXT_FOR_AI.md).
        private static readonly string[] LoanDestinationFlavorNames =
        {
            "a Championship club", "a League One club", "a lower-league side",
            "a European loan spell", "a rival Premier League club's reserves",
        };
    }
}
