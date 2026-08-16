using System.Collections.Generic;
using System.Linq;
using Data;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

// Regression coverage for the AI-club finance foundation (roadmap: prerequisite for
// ManagerAiTransferTargetSearch's output to ever be acted on). Exercises
// ManagerClubFinance.ApplyAnnualWageBill - the exact method the controller's season
// rollover now calls for every club, not just the managed team - directly, so this is
// a real audit of production code rather than a reimplemented copy of its logic.
public static class ManagerAiClubFinanceAudit
{
    [MenuItem("TFM/Audits/AI Club Finance")]
    public static void Run()
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        try
        {
            AuditStrongerClubSeedsBiggerBudget();
            AuditStrongerSquadPaysMoreWages();
            AuditRepeatedSeasonsDoNotProduceNaNOrCrash();
            AuditWholeGeneratedLeagueGetsSaneBudgetsAndWages();
            Debug.Log("AI club finance audit passed: budget seeding scales with strength, wage bills scale with squad quality, and repeated seasons stay numerically sane across a full generated league.");
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    // A clearly stronger club should seed a clearly bigger budget than a clearly
    // weaker one - proves GetOrSeedBudget's strength-based formula, now exercised for
    // any club rather than just the managed team, actually differentiates.
    private static void AuditStrongerClubSeedsBiggerBudget()
    {
        ManagerClubFinance finance = new ManagerClubFinance();
        float strongBudget = finance.GetOrSeedBudget("StrongClub", attackStrength: 1.6f, defenceStrength: 0.7f);
        float weakBudget = finance.GetOrSeedBudget("WeakClub", attackStrength: 0.7f, defenceStrength: 1.4f);

        Require(strongBudget > weakBudget, $"a clearly stronger club ({strongBudget:F1}) should seed a bigger budget than a clearly weaker one ({weakBudget:F1})");
        Require(strongBudget > 0f && weakBudget > 0f, "both seeded budgets should be positive");
    }

    // Two real generated squads at different SquadQualityTarget tiers - the stronger
    // one's total annual wage bill should be clearly higher, proving
    // ApplyAnnualWageBill's per-player summation actually reflects squad quality
    // rather than being roughly flat regardless of who's on the books.
    private static void AuditStrongerSquadPaysMoreWages()
    {
        UnityEngine.Random.InitState(300826);
        AgentSquadGenerator generator = new AgentSquadGenerator();
        AgentTeam strongTeam = generator.GenerateSquad("StrongSquad", new SquadQualityTarget(88f, 84f, 80f));
        AgentTeam weakTeam = generator.GenerateSquad("WeakSquad", new SquadQualityTarget(68f, 64f, 60f));

        ManagerClubFinance finance = new ManagerClubFinance();
        float strongWage = finance.ApplyAnnualWageBill(strongTeam, attackStrength: 1f, defenceStrength: 1f);
        float weakWage = finance.ApplyAnnualWageBill(weakTeam, attackStrength: 1f, defenceStrength: 1f);

        Require(strongWage > weakWage, $"a much stronger generated squad's wage bill ({strongWage:F1}) should clearly exceed a much weaker squad's ({weakWage:F1})");
        Require(strongWage > 0f && weakWage > 0f, "both wage bills should be positive - every squad has real players drawing real wages");
    }

    // Simulates several consecutive season rollovers for one club with no income ever
    // replenishing the budget (the honest current AI-club reality - they can't sell
    // anything yet either) - the budget should trend negative but stay a finite,
    // sane number, never NaN or an exception, matching the same unclamped-by-design
    // behaviour the managed team's own budget already has.
    private static void AuditRepeatedSeasonsDoNotProduceNaNOrCrash()
    {
        UnityEngine.Random.InitState(300826 + 1);
        AgentSquadGenerator generator = new AgentSquadGenerator();
        AgentTeam team = generator.GenerateSquad("LongCareerClub", new SquadQualityTarget(82f, 78f, 74f));
        ManagerClubFinance finance = new ManagerClubFinance();

        // GetOrSeedBudget only ever seeds once, so the original value has to be
        // captured before the wage-only loop below starts depleting it - calling it
        // again afterward would just return the already-reduced current budget, not
        // the seed, and silently pass regardless of what the loop actually did.
        float originalSeedBudget = finance.GetOrSeedBudget(team.TeamName, 1f, 1f);

        for (int season = 0; season < 10; season++)
        {
            float wage = finance.ApplyAnnualWageBill(team, attackStrength: 1f, defenceStrength: 1f);
            Require(!float.IsNaN(wage), $"season {season}: wage bill was NaN");
            Require(!float.IsNaN(finance.GetBudget(team.TeamName)), $"season {season}: budget became NaN");
        }

        Require(finance.GetBudget(team.TeamName) < originalSeedBudget,
            "ten consecutive wage-only seasons with no income should leave the budget below its original seed value");
    }

    // Real generated 20-club league, one AgentSquadGenerator instance, every club
    // seeded and wage-billed the same way GetOrCreateAgentTeam/DeductWageBillForAllClubs
    // do it live. Proves it holds up at the actual scale it will run at, and that the
    // stronger half of the league collectively draws a materially bigger wage bill
    // than the weaker half - the finance foundation tracking genuine squad-quality
    // variation, not just producing plausible-looking noise.
    private static void AuditWholeGeneratedLeagueGetsSaneBudgetsAndWages()
    {
        TextAsset historyAsset = Resources.Load<TextAsset>("World/football_world_history");
        if (historyAsset == null) throw new System.IO.FileNotFoundException("Runtime world history resource was not found.");
        FootballWorldHistory history = FootballWorldHistory.FromTextAsset(historyAsset);
        List<ClubWorldGenerationProfileRecord> clubs = history.Data.WorldGenerationProfiles
            .Where(profile => profile.CountryCode == "eng" && profile.Level == 1)
            .GroupBy(profile => profile.ReferenceSeason)
            .OrderByDescending(group => group.Key)
            .First()
            .OrderByDescending(c => c.FirstTeamOverall)
            .ToList();
        if (clubs.Count != 20) throw new System.IO.InvalidDataException($"Expected 20 latest English top-flight profiles, found {clubs.Count}.");

        UnityEngine.Random.InitState(300826 + 2);
        AgentSquadGenerator generator = new AgentSquadGenerator();
        ManagerClubFinance finance = new ManagerClubFinance();
        List<float> topHalfWages = new List<float>();
        List<float> bottomHalfWages = new List<float>();

        for (int i = 0; i < clubs.Count; i++)
        {
            ClubWorldGenerationProfileRecord club = clubs[i];
            SquadQualityTarget target = new((float)club.FirstTeamOverall, (float)club.BenchOverall, (float)club.ReserveOverall);
            AgentTeam team = generator.GenerateSquad(club.ClubName, target);

            float budget = finance.GetOrSeedBudget(club.ClubName, attackStrength: 1f, defenceStrength: 1f);
            Require(budget > 0f, $"{club.ClubName}: seeded budget was not positive ({budget:F1})");

            float wage = finance.ApplyAnnualWageBill(team, attackStrength: 1f, defenceStrength: 1f);
            Require(!float.IsNaN(wage) && wage > 0f, $"{club.ClubName}: wage bill was not a sane positive number ({wage})");

            (i < clubs.Count / 2 ? topHalfWages : bottomHalfWages).Add(wage);
        }

        float meanTopHalfWage = topHalfWages.Average();
        float meanBottomHalfWage = bottomHalfWages.Average();
        Require(meanTopHalfWage > meanBottomHalfWage,
            $"the league's stronger half's mean wage bill ({meanTopHalfWage:F1}) should exceed the weaker half's ({meanBottomHalfWage:F1})");

        Debug.Log($"AI club finance league pass: 20 clubs, mean wage bill top-half £{meanTopHalfWage:F1}m vs bottom-half £{meanBottomHalfWage:F1}m.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
