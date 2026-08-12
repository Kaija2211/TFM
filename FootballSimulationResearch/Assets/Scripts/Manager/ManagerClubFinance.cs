using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Transfer market economy (career arc, Phase 3, session 8) - per-club transfer
    // budget plus live Wage/MarketValue formulas, both derived from Overall/Potential/
    // Age rather than stored fields (same reasoning as GetOverallRating() itself: no
    // need to cache a value that's cheap to recompute and would otherwise need
    // invalidating every time progression changes a player's underlying attributes).
    // One instance for the whole career (budgets keyed by team name), not per-team,
    // matching squadsByTeamName's own string-keyed idiom.
    public class ManagerClubFinance
    {
        private readonly Dictionary<string, float> budgetByTeamName = new();

        // Career screen Finance tab (backlog item 2, session 11) - lifetime totals,
        // separate from budgetByTeamName which only ever holds the current NET figure
        // (wages/prize money/transfers all mixed together, with no way to recover a
        // historical spend/income split from it after the fact). Only ever written from
        // the two Transfer Market buy/sell handlers, so only the managed team's entries
        // are ever meaningfully non-zero in practice - matches budgetByTeamName's own
        // team-name-keyed idiom for consistency, not because AI teams' totals are used.
        private readonly Dictionary<string, float> totalTransferSpendByTeamName = new();
        private readonly Dictionary<string, float> totalTransferIncomeByTeamName = new();

        public void RecordTransferSpend(string teamName, float amount)
        {
            totalTransferSpendByTeamName[teamName] = GetTotalTransferSpend(teamName) + amount;
        }

        public void RecordTransferIncome(string teamName, float amount)
        {
            totalTransferIncomeByTeamName[teamName] = GetTotalTransferIncome(teamName) + amount;
        }

        public float GetTotalTransferSpend(string teamName)
        {
            return totalTransferSpendByTeamName.TryGetValue(teamName, out float total) ? total : 0f;
        }

        public float GetTotalTransferIncome(string teamName)
        {
            return totalTransferIncomeByTeamName.TryGetValue(teamName, out float total) ? total : 0f;
        }

        // Save/load restores these as absolute totals (same idiom as AdjustBudget being
        // used with a computed delta in ApplySaveData) rather than exposing the backing
        // dictionaries directly.
        public void SetTotalTransferSpend(string teamName, float total)
        {
            totalTransferSpendByTeamName[teamName] = total;
        }

        public void SetTotalTransferIncome(string teamName, float total)
        {
            totalTransferIncomeByTeamName[teamName] = total;
        }

        // Seeded once per club from its own strength rating (already generated for
        // squad-strength purposes, see StatisticalModel.GetTeamStrength) - a top club
        // starts with meaningfully more to spend than a relegation-strength one, free
        // realism from data that already exists. DefenceStrength is inverted in its own
        // formula elsewhere (AgentSquadGenerator: lower = stronger defence), so 1/it is
        // the actual "how strong defensively" scalar here.
        public float GetOrSeedBudget(string teamName, float attackStrength, float defenceStrength)
        {
            if (budgetByTeamName.TryGetValue(teamName, out float existing))
            {
                return existing;
            }

            float defensiveQuality = defenceStrength > 0.01f ? 1f / defenceStrength : 1f;
            float combinedStrength = (attackStrength + defensiveQuality) * 0.5f;
            float budget = 30f + combinedStrength * 55f;

            budgetByTeamName[teamName] = budget;
            return budget;
        }

        public float GetBudget(string teamName)
        {
            return budgetByTeamName.TryGetValue(teamName, out float budget) ? budget : 0f;
        }

        public void AdjustBudget(string teamName, float delta)
        {
            budgetByTeamName[teamName] = GetBudget(teamName) + delta;
        }

        // Session 16 - a brand new career starting mid-session (OnConfirmTeamClicked)
        // never reset this, so a second career in the same Play Mode/app session opened
        // with every club still holding whatever budget/spend/income it had at the end
        // of the previous career - GetOrSeedBudget's "seed once, keep forever" idiom
        // means a club that already had a budgetByTeamName entry would never reseed,
        // silently carrying the old figure into the new career.
        public void Clear()
        {
            budgetByTeamName.Clear();
            totalTransferSpendByTeamName.Clear();
            totalTransferIncomeByTeamName.Clear();
        }

        // All figures in £m (Wage is £m/YEAR, not weekly) - deducted from the same
        // budget pool as transfer spend at each season rollover, so a club's wage bill
        // and its transfer activity draw from one number, same as real football
        // (prize money/board backing, see ManagerCareerHistory, is what replenishes it).
        public static float GetAnnualWage(PlayerAgent player)
        {
            float overall = player.GetOverallRating();
            float potential = player.Potential;
            float ageFactor = Mathf.Clamp01((player.Age - 18f) / 12f);

            // Roughly quadratic above a replacement-level baseline - a handful of
            // points of Overall separates a squad player from a genuine star in wage
            // terms far more than linearly.
            float overAllowance = Mathf.Max(overall - 45f, 0f);
            float baseWage = overAllowance * overAllowance * 0.0035f;

            // Unproven high-potential youth costs less than an equally-rated proven
            // veteran - a smaller premium that only fully applies once ageFactor rises.
            float potentialPremium = Mathf.Max(potential - overall, 0f) * 0.03f * (0.3f + ageFactor * 0.7f);

            return Mathf.Max(baseWage + potentialPremium, 0.05f);
        }

        public static float GetMarketValue(PlayerAgent player)
        {
            float overall = player.GetOverallRating();
            float potential = player.Potential;
            float youthFactor = Mathf.Clamp01((26f - player.Age) / 10f);

            // Ramps from 28 to a full discount at 35 (VeteranRetirementAge) rather than
            // 31-39 - playtest finding (session 16): the old curve only discounted a
            // 33-year-old by ~14%, letting an aging player price like they were still
            // mid-career. Most players don't play meaningfully past retirement age, so
            // the discount should be most of the way there well before it.
            float veteranDiscount = Mathf.Clamp01((player.Age - 28f) / 7f);

            float overAllowance = Mathf.Max(overall - 45f, 0f);
            float baseValue = overAllowance * overAllowance * 0.045f;

            // The wonderkid premium - a big Potential gap over current Overall is worth
            // real money on its own, more so the younger the player is (more years for
            // it to actually be realised, see ManagerPlayerDevelopment).
            float potentialUpside = Mathf.Max(potential - overall, 0f) * (1.2f + youthFactor * 1.8f);

            float value = (baseValue + potentialUpside) * (1f - veteranDiscount * 0.55f);
            return Mathf.Max(value, 0.2f);
        }

        // Your own player - no random refusal (you're not asking permission), just a
        // haircut off full MarketValue, since you already know their true stats with
        // certainty (no scouting-style uncertainty), unlike buying blind on an AI
        // squad's potential.
        public static float GetSellPrice(PlayerAgent player)
        {
            return GetMarketValue(player) * 0.9f;
        }
    }
}
