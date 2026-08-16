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

        // AI-club finance foundation (roadmap: prerequisite for ManagerAiTransferTargetSearch's
        // output to ever be acted on) - the exact seed-then-deduct sequence the
        // managed team's own season rollover always used, pulled out here so any club
        // (managed or AI) can pay its own annual wage bill through one call, and so it
        // has a deterministic Editor audit rather than only ever running inside a live
        // scene controller. Returns the total wage deducted so a caller can report it
        // (e.g. an Inbox message) without needing to recompute it.
        public float ApplyAnnualWageBill(AgentTeam team, float attackStrength, float defenceStrength)
        {
            float totalWage = 0f;
            foreach (PlayerAgent player in team.Players)
            {
                totalWage += GetAnnualWage(player);
            }

            GetOrSeedBudget(team.TeamName, attackStrength, defenceStrength);
            AdjustBudget(team.TeamName, -totalWage);

            return totalWage;
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
            return CalculateMarketValue(player.GetOverallRating(), player.Potential, player.Age);
        }

        // Pure valuation curve kept public so economic audits can test stable benchmark
        // profiles without manufacturing an entire PlayerAgent attribute sheet. This is
        // intrinsic value only: the selling club's depth/importance premium belongs in
        // ManagerTransferNegotiation, while reputation belongs in budgets, attraction
        // and bargaining power. Mixing those into this number made lower-club reserve
        // players liquidate for superstar fees.
        public static float CalculateMarketValue(float overall, float potential, int age)
        {
            overall = Mathf.Clamp(overall, 1f, 99f);
            potential = Mathf.Clamp(potential, overall, 99f);

            // Exponential ability curve: approximately £1.1m at 60, £6m at 70,
            // £31m at 80 and £165m at 90 before age/upside adjustments. This preserves
            // genuine scarcity at the elite end without pricing every 70-rated reserve
            // like a Premier League starter.
            float abilityValue = 0.5f * Mathf.Pow(1.18f, overall - 55f);

            // Potential is valuable only while there is realistic development runway.
            // A large gap produces a meaningful wonderkid premium, but does not make a
            // merely decent 24-year-old worth tens of millions for unrealised upside.
            float developmentRunway = Mathf.Clamp01((25f - age) / 8f);
            float potentialGap = Mathf.Max(potential - overall, 0f);
            float potentialPremium = Mathf.Pow(potentialGap, 1.25f) * 0.45f * developmentRunway;

            // Prime players retain full value through 27. Resale value then falls
            // progressively rather than waiting until retirement is imminent.
            float ageMultiplier;
            if (age <= 27) ageMultiplier = 1f;
            else if (age <= 30) ageMultiplier = Mathf.Lerp(1f, 0.70f, (age - 27f) / 3f);
            else if (age <= 33) ageMultiplier = Mathf.Lerp(0.70f, 0.32f, (age - 30f) / 3f);
            else ageMultiplier = Mathf.Lerp(0.32f, 0.12f, Mathf.Clamp01((age - 33f) / 4f));

            return Mathf.Max((abilityValue + potentialPremium) * ageMultiplier, 0.2f);
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
