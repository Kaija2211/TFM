using System.Collections.Generic;
using System.Linq;
using Data;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

// Regression coverage for ManagerAiTransferTargetSearch (roadmap: "Identify needs and
// search for tactically appropriate targets" - third stage of the Intelligent AI
// Clubs epic). Unit-level scenarios prove upgrade-only filtering, position-fit
// filtering and age-aware ranking all work independently; an integration pass wires
// it up to ManagerAiSquadDepthEvaluator against many real generated squads to prove
// the two services compose cleanly at scale.
public static class ManagerAiTransferTargetSearchAudit
{
    [MenuItem("TFM/Audits/AI Transfer Target Search")]
    public static void Run()
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        try
        {
            AuditOnlyGenuineUpgradesAreReturned();
            AuditPositionFitIsRespected();
            AuditYoungerPlayerRankedAboveOlderAtEqualQuality();
            AuditIntegratesWithDepthEvaluatorAcrossGeneratedSquads();
            Debug.Log("AI transfer target search audit passed: upgrade-only filtering, position-fit filtering and age-aware ranking all correct, and it composes cleanly with the depth evaluator at scale.");
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    // A pool containing one clearly better CB, one clearly worse CB and one CB at
    // exactly the same quality should return only the better one - proves this never
    // recommends a lateral move or a downgrade.
    private static void AuditOnlyGenuineUpgradesAreReturned()
    {
        UnityEngine.Random.InitState(280826);
        AgentTeam sellerClub = new AgentTeam("SellerClub", Formation.FourThreeThree);
        PlayerAgent better = CreatePlayer("Better CB", PlayerPosition.CB, age: 26, overallLevel: 85f);
        PlayerAgent worse = CreatePlayer("Worse CB", PlayerPosition.CB, age: 26, overallLevel: 70f);
        PlayerAgent equal = CreatePlayer("Equal CB", PlayerPosition.CB, age: 26, overallLevel: 78f);
        sellerClub.AddSquadPlayer(better);
        sellerClub.AddSquadPlayer(worse);
        sellerClub.AddSquadPlayer(equal);

        List<ManagerAiTransferTargetSearch.TransferTarget> targets = ManagerAiTransferTargetSearch.FindTargets(
            PlayerPosition.CB, currentBestOverall: 78f, candidateClubs: new[] { sellerClub });

        Require(targets.Count == 1, $"expected exactly one genuine upgrade to be returned, got {targets.Count}");
        Require(targets[0].Player == better, "the one returned target should be the clearly better player");
    }

    // A far better player at a totally unrelated position (ST) must never be
    // recommended as a CB target, however good they are overall - proves position fit
    // is a hard filter, not just a scoring input.
    private static void AuditPositionFitIsRespected()
    {
        UnityEngine.Random.InitState(280826 + 1);
        AgentTeam sellerClub = new AgentTeam("SellerClub", Formation.FourThreeThree);
        PlayerAgent wrongPositionStar = CreatePlayer("Star Striker", PlayerPosition.ST, age: 24, overallLevel: 95f);
        sellerClub.AddSquadPlayer(wrongPositionStar);

        List<ManagerAiTransferTargetSearch.TransferTarget> targets = ManagerAiTransferTargetSearch.FindTargets(
            PlayerPosition.CB, currentBestOverall: 70f, candidateClubs: new[] { sellerClub });

        Require(targets.Count == 0, $"a ST with no CB adjacency should never be recommended as a CB target, got {targets.Count} results");
    }

    // Two CBs of identical Overall, one young (24) and one old (33) - the younger one
    // should rank first, proving the age-aware suitability score is doing real work,
    // not just re-sorting by raw Overall.
    private static void AuditYoungerPlayerRankedAboveOlderAtEqualQuality()
    {
        UnityEngine.Random.InitState(280826 + 2);
        AgentTeam sellerClub = new AgentTeam("SellerClub", Formation.FourThreeThree);
        PlayerAgent young = CreatePlayer("Young CB", PlayerPosition.CB, age: 24, overallLevel: 82f);
        PlayerAgent old = CreatePlayer("Old CB", PlayerPosition.CB, age: 33, overallLevel: 82f);
        sellerClub.AddSquadPlayer(old);
        sellerClub.AddSquadPlayer(young);

        List<ManagerAiTransferTargetSearch.TransferTarget> targets = ManagerAiTransferTargetSearch.FindTargets(
            PlayerPosition.CB, currentBestOverall: 75f, candidateClubs: new[] { sellerClub });

        Require(targets.Count == 2, $"expected both equal-quality upgrades to be returned, got {targets.Count}");
        Require(targets[0].Player == young, "at equal Overall, the younger player should rank above the older one");
        Require(targets[0].SuitabilityScore > targets[1].SuitabilityScore, "the younger player's suitability score should be strictly higher");
    }

    // Real generated squads, many clubs, many worlds - wires the depth evaluator's
    // weakest-position output straight into the target search (the actual intended
    // usage pattern) and proves it runs cleanly at scale: no crash, no NaN, and every
    // returned target is a genuine fit-and-quality upgrade over the searching club's
    // own current best option.
    private static void AuditIntegratesWithDepthEvaluatorAcrossGeneratedSquads()
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

        UnityEngine.Random.InitState(280826 + 3);
        AgentSquadGenerator generator = new AgentSquadGenerator();
        List<AgentTeam> world = new List<AgentTeam>();
        foreach (ClubWorldGenerationProfileRecord club in clubs)
        {
            SquadQualityTarget target = new((float)club.FirstTeamOverall, (float)club.BenchOverall, (float)club.ReserveOverall);
            world.Add(generator.GenerateSquad(club.ClubName, target));
        }

        int searchesRun = 0;
        int searchesWithResults = 0;

        foreach (AgentTeam searchingClub in world)
        {
            List<PlayerPosition> relevantPositions = generator.GetStartingPositions(searchingClub.Formation).Distinct().ToList();
            ManagerAiSquadDepthEvaluator.SquadDepthReport depthReport = ManagerAiSquadDepthEvaluator.Evaluate(searchingClub, relevantPositions);
            ManagerAiSquadDepthEvaluator.PositionDepth weakest = depthReport.Positions.First(p => p.Position == depthReport.WeakestPosition);

            IEnumerable<AgentTeam> otherClubs = world.Where(c => c != searchingClub);
            List<ManagerAiTransferTargetSearch.TransferTarget> targets = ManagerAiTransferTargetSearch.FindTargets(
                depthReport.WeakestPosition, weakest.BestOverall, otherClubs);

            foreach (ManagerAiTransferTargetSearch.TransferTarget candidateTarget in targets)
            {
                Require(!float.IsNaN(candidateTarget.SuitabilityScore), $"{searchingClub.TeamName}: target search produced a NaN suitability score");
                Require(candidateTarget.Fit >= 0.80f, $"{searchingClub.TeamName}: a returned target had fit {candidateTarget.Fit:F2}, below the minimum threshold");
                Require(candidateTarget.OverallRating > weakest.BestOverall, $"{searchingClub.TeamName}: a returned target ({candidateTarget.OverallRating:F1}) was not actually better than the club's current best ({weakest.BestOverall:F1})");
            }

            searchesRun++;
            if (targets.Count > 0)
            {
                searchesWithResults++;
            }
        }

        Require(searchesWithResults > 0, "not a single generated club's weakest position turned up any target across the whole league - suspiciously dead for a 20-club world");

        Debug.Log($"AI transfer target search integration pass: {searchesRun} clubs searched, {searchesWithResults} found at least one genuine upgrade target for their weakest position.");
    }

    private static PlayerAgent CreatePlayer(string name, PlayerPosition position, int age, float overallLevel)
    {
        PlayerAgent player = new PlayerAgent(name, PlayerRole.Defender, position)
        {
            Age = age,
            Height = 180f,
            PlayerId = System.Guid.NewGuid().ToString()
        };

        player.Finishing = player.Passing = player.Dribbling = player.Crossing = player.Heading =
            player.LongShots = player.ThroughBalls = player.FreeKicks = player.Creativity =
            player.Positioning = player.Composure = player.OffTheBall = player.Leadership =
            player.Defending = player.Tackling = player.Marking = player.Pace = player.Strength =
            player.Stamina = player.Aerial = player.Goalkeeping = player.Reflexes = player.WeakFoot =
            player.FirstTouch = player.Technique = player.Corners = player.Penalties =
            player.Anticipation = player.Decisions = player.Vision = player.DefensivePositioning =
            player.WorkRate = player.Aggression = player.Acceleration = player.Agility =
            player.Balance = player.JumpingReach = player.Handling = player.OneOnOnes =
            player.AerialCommand = player.Distribution = player.GoalkeeperPositioning = overallLevel;

        return player;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
