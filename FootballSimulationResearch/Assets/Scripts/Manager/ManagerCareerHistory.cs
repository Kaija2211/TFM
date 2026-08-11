using System.Collections.Generic;
using UnityEngine;

namespace Manager
{
    // One season's end-of-year outcome (career arc, Phase 4, session 8) - the record
    // the Trophy Room lists season-by-season.
    public class SeasonRecord
    {
        public int Season;
        public int FinalPosition;
        public bool IsChampion;
        public float PrizeMoney;
        public float BoardBoost;

        // Backlog item 2 (session 11) - the Career screen's Record tab wants the actual
        // match record behind the final position, not just the position itself. Sourced
        // straight from the managed team's LeagueTable.Entry at the same point
        // ApplySeasonEndRewards already reads FinalPosition from.
        public int Wins;
        public int Draws;
        public int Losses;
        public int Points;
    }

    // Season-by-season history + the prize money/board boost formulas that fund it -
    // the "incentive to win the league" half of the career arc, alongside Phase 3's
    // transfer economy those funds actually get spent in. One instance for the whole
    // career, same idiom as ManagerScouting/ManagerClubFinance.
    public class ManagerCareerHistory
    {
        private readonly List<SeasonRecord> records = new();

        public IReadOnlyList<SeasonRecord> Records => records;

        public void AddRecord(SeasonRecord record)
        {
            records.Add(record);
        }

        // Loosely mirrors the real Premier League's merit-based prize pool shape - a
        // steep top-of-table premium over a long flatter tail, not a smooth linear
        // scale. finalPosition is 1-based (1 = champions).
        public static float GetPrizeMoney(int finalPosition)
        {
            float positionFactor = Mathf.Clamp01((21f - finalPosition) / 20f);
            return 15f + positionFactor * positionFactor * 130f;
        }

        // A separate, smaller line item from prize money - deliberately kept distinct
        // rather than folded into one number, since Thomas framed these as two
        // different mechanisms (merit prize money vs. board confidence backing a
        // strong season with extra transfer firepower). Only a genuinely good finish
        // (top 8) earns anything here at all.
        public static float GetBoardBoost(int finalPosition)
        {
            if (finalPosition > 8)
            {
                return 0f;
            }

            float positionFactor = Mathf.Clamp01((9f - finalPosition) / 8f);
            return positionFactor * positionFactor * 40f;
        }
    }
}
