using System.Collections.Generic;
using System.Linq;
using Data;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

// Regression coverage for ManagerAiTransferExecutor - the first AI-club work that
// actually completes a transfer, composing ManagerAiSquadDepthEvaluator,
// ManagerAiTransferTargetSearch and ManagerClubFinance together. Unit-level scenarios
// prove each safety gate (need threshold, affordability, seller-depth protection,
// starter exclusion) independently; an integration pass runs a real one-club-per-
// season-rollover pass across the full generated league.
public static class ManagerAiTransferExecutorAudit
{
    [MenuItem("TFM/Audits/AI Transfer Executor")]
    public static void Run()
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        try
        {
            AuditNoNeedMeansNoTransfer();
            AuditAffordableGenuineUpgradeCompletes();
            AuditCannotAffordAnyTargetMeansNoTransfer();
            AuditStarterIsNeverSold();
            AuditSaleIsBlockedIfItWouldLeaveSellerWithNoCover();
            AuditFullLeagueSeasonPass();
            Debug.Log("AI transfer executor audit passed: need threshold, affordability, starter exclusion and seller-depth protection all correct, and a full-league season pass completes cleanly.");
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    // A perfectly balanced squad (every position covered at equal, decent quality)
    // has nothing worth shopping for - TryExecuteTransfer should do nothing even
    // with rich, affordable targets sitting right there.
    private static void AuditNoNeedMeansNoTransfer()
    {
        UnityEngine.Random.InitState(310826);
        AgentTeam buyingClub = new AgentTeam("BalancedClub", Formation.FourThreeThree);
        AddBalancedSquad(buyingClub, attributeLevel: 78f);

        AgentTeam sellerClub = new AgentTeam("RichSellerClub", Formation.FourThreeThree);
        AddBalancedSquad(sellerClub, attributeLevel: 95f);

        ManagerClubFinance finance = new ManagerClubFinance();
        finance.GetOrSeedBudget(buyingClub.TeamName, 1f, 1f);
        finance.AdjustBudget(buyingClub.TeamName, 500f);

        List<PlayerPosition> relevant = AllOutfieldPlusGk();
        ManagerAiTransferExecutor.CompletedTransfer? result = ManagerAiTransferExecutor.TryExecuteTransfer(
            buyingClub, relevant, new List<AgentTeam> { sellerClub }, finance);

        Require(result == null, "a balanced squad with no real positional need should not attempt a transfer");
    }

    // A club with a genuine gap, a real affordable upgrade elsewhere, and a seller
    // with plenty of depth should complete the deal - proves the whole chain works
    // end to end: player moves, money moves both ways correctly.
    private static void AuditAffordableGenuineUpgradeCompletes()
    {
        UnityEngine.Random.InitState(310826 + 1);
        AgentTeam buyingClub = new AgentTeam("NeedyClub", Formation.FourThreeThree);
        AddBalancedSquad(buyingClub, attributeLevel: 70f);
        // GK deliberately has no adjacency crossover at all (see PlayerAgent.
        // AdjacentPositions' own comment - "GK deliberately has no entry"), so
        // weakening it is the one position guaranteed not to get cushioned back up
        // by adjacent-position cover the way CB/RB/LB/DM would - exactly the same
        // isolation trick ManagerAiSquadDepthEvaluatorAudit's first test already
        // relies on.
        foreach (PlayerAgent player in buyingClub.Players.Where(p => p.PrimaryPosition == PlayerPosition.GK))
        {
            SetUniformAttributes(player, 55f);
        }

        AgentTeam sellerClub = new AgentTeam("GenerousSeller", Formation.FourThreeThree);
        AddBalancedSquad(sellerClub, attributeLevel: 78f);
        // Give the seller three GK-capable bench players so selling one is safe.
        PlayerAgent targetCb = CreatePlayer("Target GK", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 25, attributeLevel: 85f);
        sellerClub.AddBenchPlayer(targetCb);
        sellerClub.AddBenchPlayer(CreatePlayer("Backup GK 1", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 24, attributeLevel: 75f));
        sellerClub.AddBenchPlayer(CreatePlayer("Backup GK 2", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 26, attributeLevel: 74f));

        ManagerClubFinance finance = new ManagerClubFinance();
        finance.GetOrSeedBudget(buyingClub.TeamName, 1f, 1f);
        finance.AdjustBudget(buyingClub.TeamName, 500f);
        finance.GetOrSeedBudget(sellerClub.TeamName, 1f, 1f);
        float buyerBudgetBefore = finance.GetBudget(buyingClub.TeamName);
        float sellerBudgetBefore = finance.GetBudget(sellerClub.TeamName);

        List<PlayerPosition> relevant = AllOutfieldPlusGk();
        ManagerAiTransferExecutor.CompletedTransfer? result = ManagerAiTransferExecutor.TryExecuteTransfer(
            buyingClub, relevant, new List<AgentTeam> { sellerClub }, finance);

        Require(result.HasValue, "a genuine, affordable upgrade with a safe seller should complete");
        ManagerAiTransferExecutor.CompletedTransfer transfer = result.Value;
        Require(buyingClub.Players.Contains(transfer.Player), "the bought player should now be on the buying club's books");
        Require(!sellerClub.Players.Contains(transfer.Player), "the sold player should no longer be on the selling club's books");
        Require(Mathf.Abs(finance.GetBudget(buyingClub.TeamName) - (buyerBudgetBefore - transfer.Fee)) < 0.01f, "the buyer's budget should drop by exactly the fee");
        Require(Mathf.Abs(finance.GetBudget(sellerClub.TeamName) - (sellerBudgetBefore + transfer.Fee)) < 0.01f, "the seller's budget should rise by exactly the fee");
    }

    // Same genuine-need scenario, but the buying club has no money at all - the
    // transfer must not happen just because a great target exists.
    private static void AuditCannotAffordAnyTargetMeansNoTransfer()
    {
        UnityEngine.Random.InitState(310826 + 2);
        AgentTeam buyingClub = new AgentTeam("BrokeClub", Formation.FourThreeThree);
        AddBalancedSquad(buyingClub, attributeLevel: 70f);
        // GK isolation trick - see AuditAffordableGenuineUpgradeCompletes' own comment.
        foreach (PlayerAgent player in buyingClub.Players.Where(p => p.PrimaryPosition == PlayerPosition.GK))
        {
            SetUniformAttributes(player, 55f);
        }

        AgentTeam sellerClub = new AgentTeam("ExpensiveSeller", Formation.FourThreeThree);
        AddBalancedSquad(sellerClub, attributeLevel: 78f);
        sellerClub.AddBenchPlayer(CreatePlayer("Expensive Target", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 24, attributeLevel: 90f));
        sellerClub.AddBenchPlayer(CreatePlayer("Backup GK", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 24, attributeLevel: 74f));

        ManagerClubFinance finance = new ManagerClubFinance();
        finance.GetOrSeedBudget(buyingClub.TeamName, 1f, 1f);
        finance.AdjustBudget(buyingClub.TeamName, -finance.GetBudget(buyingClub.TeamName));
        Require(finance.GetBudget(buyingClub.TeamName) <= 0f, "test setup failed to zero the buyer's budget");

        List<PlayerPosition> relevant = AllOutfieldPlusGk();
        ManagerAiTransferExecutor.CompletedTransfer? result = ManagerAiTransferExecutor.TryExecuteTransfer(
            buyingClub, relevant, new List<AgentTeam> { sellerClub }, finance);

        Require(result == null, "a club with zero budget should not complete a transfer regardless of how good the target is");
    }

    // The best available CB target happens to be the seller's own starter - the
    // executor must skip them (never shrinking a StartingEleven) and either find a
    // real bench alternative or do nothing, never selling the starter.
    private static void AuditStarterIsNeverSold()
    {
        UnityEngine.Random.InitState(310826 + 3);
        AgentTeam buyingClub = new AgentTeam("NeedyClub2", Formation.FourThreeThree);
        AddBalancedSquad(buyingClub, attributeLevel: 70f);
        // GK isolation trick - see AuditAffordableGenuineUpgradeCompletes' own comment.
        foreach (PlayerAgent player in buyingClub.Players.Where(p => p.PrimaryPosition == PlayerPosition.GK))
        {
            SetUniformAttributes(player, 55f);
        }

        AgentTeam sellerClub = new AgentTeam("StarterProtectedSeller", Formation.FourThreeThree);
        AddBalancedSquad(sellerClub, attributeLevel: 78f);
        // Best GK is a genuine starter (already placed by AddBalancedSquad's own
        // starters) - overwrite the starting GK to be the standout best option.
        PlayerAgent starterGk = sellerClub.StartingEleven.First(p => p.PrimaryPosition == PlayerPosition.GK);
        SetUniformAttributes(starterGk, 95f);

        ManagerClubFinance finance = new ManagerClubFinance();
        finance.GetOrSeedBudget(buyingClub.TeamName, 1f, 1f);
        finance.AdjustBudget(buyingClub.TeamName, 500f);

        List<PlayerPosition> relevant = AllOutfieldPlusGk();
        ManagerAiTransferExecutor.TryExecuteTransfer(buyingClub, relevant, new List<AgentTeam> { sellerClub }, finance);

        Require(sellerClub.StartingEleven.Contains(starterGk), "a current starter must never be sold, even as the objectively best-ranked target");
        Require(sellerClub.Players.Contains(starterGk), "the protected starter should still be on the seller's books at all");
    }

    // A seller with only one CB-capable player total (the target themselves) must
    // never sell - selling would leave zero cover at that position.
    private static void AuditSaleIsBlockedIfItWouldLeaveSellerWithNoCover()
    {
        UnityEngine.Random.InitState(310826 + 4);
        AgentTeam buyingClub = new AgentTeam("NeedyClub3", Formation.FourThreeThree);
        AddBalancedSquad(buyingClub, attributeLevel: 70f);
        // GK isolation trick - see AuditAffordableGenuineUpgradeCompletes' own comment.
        foreach (PlayerAgent player in buyingClub.Players.Where(p => p.PrimaryPosition == PlayerPosition.GK))
        {
            SetUniformAttributes(player, 55f);
        }

        AgentTeam sellerClub = new AgentTeam("ThinSeller", Formation.FourThreeThree);
        // GK has no adjacency crossover at all, so this really is the seller's only
        // possible GK cover of any kind - selling them leaves zero.
        PlayerAgent onlyGk = CreatePlayer("Only GK", PlayerRole.Goalkeeper, PlayerPosition.GK, age: 24, attributeLevel: 90f);
        sellerClub.AddBenchPlayer(onlyGk);
        sellerClub.AddSquadPlayer(CreatePlayer("Some ST", PlayerRole.Forward, PlayerPosition.ST, age: 24, attributeLevel: 78f));

        ManagerClubFinance finance = new ManagerClubFinance();
        finance.GetOrSeedBudget(buyingClub.TeamName, 1f, 1f);
        finance.AdjustBudget(buyingClub.TeamName, 500f);

        List<PlayerPosition> relevant = AllOutfieldPlusGk();
        ManagerAiTransferExecutor.CompletedTransfer? result = ManagerAiTransferExecutor.TryExecuteTransfer(
            buyingClub, relevant, new List<AgentTeam> { sellerClub }, finance);

        Require(result == null, "a sale that would leave the seller with zero cover at that position should be blocked");
        Require(sellerClub.Players.Contains(onlyGk), "the protected sole GK should still be on the seller's books");
    }

    // Real generated 20-club league (excluding the managed team, matching how
    // RunAiTransferWindow actually calls this), one pass - proves it holds up at
    // real scale: no crash, no NaN budgets, no player sold by more than one club in
    // the same pass, and a non-trivial (but not absurd) number of transfers complete.
    private static void AuditFullLeagueSeasonPass()
    {
        TextAsset historyAsset = Resources.Load<TextAsset>("World/football_world_history");
        if (historyAsset == null) throw new System.IO.FileNotFoundException("Runtime world history resource was not found.");
        FootballWorldHistory history = FootballWorldHistory.FromTextAsset(historyAsset);
        List<ClubWorldGenerationProfileRecord> clubs = history.Data.WorldGenerationProfiles
            .Where(profile => profile.CountryCode == "eng" && profile.Level == 1)
            .GroupBy(profile => profile.ReferenceSeason)
            .OrderByDescending(group => group.Key)
            .First()
            .ToList();

        UnityEngine.Random.InitState(310826 + 5);
        AgentSquadGenerator generator = new AgentSquadGenerator();
        ManagerClubFinance finance = new ManagerClubFinance();
        Dictionary<string, AgentTeam> teams = new Dictionary<string, AgentTeam>();

        foreach (ClubWorldGenerationProfileRecord club in clubs)
        {
            SquadQualityTarget target = new((float)club.FirstTeamOverall, (float)club.BenchOverall, (float)club.ReserveOverall);
            AgentTeam team = generator.GenerateSquad(club.ClubName, target);
            teams[club.ClubName] = team;
            finance.GetOrSeedBudget(club.ClubName, 1f, 1f);
        }

        int completedTransfers = 0;
        HashSet<PlayerAgent> soldThisPass = new HashSet<PlayerAgent>();

        foreach (string buyingTeamName in teams.Keys)
        {
            AgentTeam buyingClub = teams[buyingTeamName];
            List<AgentTeam> otherClubs = teams.Where(kv => kv.Key != buyingTeamName).Select(kv => kv.Value).ToList();
            List<PlayerPosition> relevant = generator.GetStartingPositions(buyingClub.Formation).Distinct().ToList();

            ManagerAiTransferExecutor.CompletedTransfer? result = ManagerAiTransferExecutor.TryExecuteTransfer(
                buyingClub, relevant, otherClubs, finance);

            if (!result.HasValue)
            {
                continue;
            }

            ManagerAiTransferExecutor.CompletedTransfer transfer = result.Value;
            Require(soldThisPass.Add(transfer.Player), $"{transfer.Player.Name} was sold more than once in the same pass");
            Require(!float.IsNaN(finance.GetBudget(buyingTeamName)), $"{buyingTeamName}: budget became NaN after a transfer");
            Require(!float.IsNaN(finance.GetBudget(transfer.SellingClubName)), $"{transfer.SellingClubName}: budget became NaN after a transfer");
            completedTransfers++;
        }

        Require(completedTransfers > 0, "not a single transfer completed across a full 20-club league - suspiciously dead");
        Require(completedTransfers < clubs.Count, $"{completedTransfers} of {clubs.Count} clubs completed a transfer in one pass - suspiciously close to every single club transacting");

        Debug.Log($"AI transfer executor full-league pass: {completedTransfers}/{clubs.Count} clubs completed a transfer.");
    }

    private static PlayerAgent CreatePlayer(string name, PlayerRole role, PlayerPosition position, int age, float attributeLevel)
    {
        PlayerAgent player = new PlayerAgent(name, role, position)
        {
            Age = age,
            Height = 180f,
            PlayerId = System.Guid.NewGuid().ToString()
        };
        SetUniformAttributes(player, attributeLevel);
        return player;
    }

    private static void SetUniformAttributes(PlayerAgent player, float attributeLevel)
    {
        player.Finishing = player.Passing = player.Dribbling = player.Crossing = player.Heading =
            player.LongShots = player.ThroughBalls = player.FreeKicks = player.Creativity =
            player.Positioning = player.Composure = player.OffTheBall = player.Leadership =
            player.Defending = player.Tackling = player.Marking = player.Pace = player.Strength =
            player.Stamina = player.Aerial = player.Goalkeeping = player.Reflexes = player.WeakFoot =
            player.FirstTouch = player.Technique = player.Corners = player.Penalties =
            player.Anticipation = player.Decisions = player.Vision = player.DefensivePositioning =
            player.WorkRate = player.Aggression = player.Acceleration = player.Agility =
            player.Balance = player.JumpingReach = player.Handling = player.OneOnOnes =
            player.AerialCommand = player.Distribution = player.GoalkeeperPositioning = attributeLevel;
    }

    // Fills every outfield position (two each) plus GK with decent, mid-career
    // players via AddSquadPlayer (first two per position land in the Starting
    // Eleven's own formation slots via AddStarter where called explicitly, but here
    // everything just goes through the generic squad add - good enough for these
    // unit tests, which only care about position-fit pools, not exact XI placement).
    private static void AddBalancedSquad(AgentTeam team, float attributeLevel)
    {
        PlayerPosition[] positions =
        {
            PlayerPosition.GK, PlayerPosition.RB, PlayerPosition.CB, PlayerPosition.LB,
            PlayerPosition.DM, PlayerPosition.CM, PlayerPosition.RW, PlayerPosition.LW, PlayerPosition.ST
        };

        int counter = 0;
        foreach (PlayerPosition position in positions)
        {
            for (int i = 0; i < 2; i++)
            {
                counter++;
                PlayerAgent player = CreatePlayer($"{position}_{counter}", PlayerRole.Midfielder, position, age: 25 + i, attributeLevel: attributeLevel - i * 2f);
                if (i == 0 && team.StartingEleven.Count < 11)
                {
                    team.AddStarter(player);
                }
                else
                {
                    team.AddSquadPlayer(player);
                }
            }
        }
    }

    private static List<PlayerPosition> AllOutfieldPlusGk()
    {
        return System.Enum.GetValues(typeof(PlayerPosition)).Cast<PlayerPosition>().ToList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
