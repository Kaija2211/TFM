using System.Collections.Generic;
using UnityEngine;
using Data;

namespace Sim
{
    public class StatisticalModel
    {
        public class TeamStrength
        {
            public string TeamName;
            public int MatchesPlayed;
            public int GoalsFor;
            public int GoalsAgainst;

            public float GoalsForPerMatch;
            public float GoalsAgainstPerMatch;
            public float AttackStrength;
            public float DefenceStrength;
        }

        private readonly HashSet<string> warnedMissingTeams = new();

        public class ExpectedGoalsPrediction
        {
            public string HomeTeam;
            public string AwayTeam;
            public float ExpectedHomeGoals;
            public float ExpectedAwayGoals;
        }

        public class SimulatedMatchResult
        {
            public string HomeTeam;
            public string AwayTeam;
            public int HomeGoals;
            public int AwayGoals;
            public float ExpectedHomeGoals;
            public float ExpectedAwayGoals;
        }

        public TeamStrength GetTeamStrength(string teamName)
{
    return GetTeamStrengthOrAverage(teamName);
}

        private readonly Dictionary<string, TeamStrength> teamStrengths = new();

        private float averageHomeGoals;
        private float averageAwayGoals;
        private float leagueAverageGoalsForPerTeamPerMatch;

        public void Train(List<OpenFootballMatch> trainingMatches)
        {
            teamStrengths.Clear();
            warnedMissingTeams.Clear();

            int totalHomeGoals = 0;
            int totalAwayGoals = 0;



            int totalGoals = 0;
            int totalTeamAppearances = 0;

            foreach (OpenFootballMatch match in trainingMatches)
            {
                AddTeamMatch(match.HomeTeam, match.HomeGoals, match.AwayGoals);
                AddTeamMatch(match.AwayTeam, match.AwayGoals, match.HomeGoals);

                totalGoals += match.HomeGoals + match.AwayGoals;
                totalTeamAppearances += 2;

                totalHomeGoals += match.HomeGoals;
                totalAwayGoals += match.AwayGoals;
            }

            averageHomeGoals = (float)totalHomeGoals / trainingMatches.Count;
            averageAwayGoals = (float)totalAwayGoals / trainingMatches.Count;
            leagueAverageGoalsForPerTeamPerMatch = (float)totalGoals / totalTeamAppearances;

            Debug.Log($"Average home goals: {averageHomeGoals:F2}");
            Debug.Log($"Average away goals: {averageAwayGoals:F2}");

            foreach (TeamStrength team in teamStrengths.Values)
            {
                team.GoalsForPerMatch = (float)team.GoalsFor / team.MatchesPlayed;
                team.GoalsAgainstPerMatch = (float)team.GoalsAgainst / team.MatchesPlayed;

                team.AttackStrength = team.GoalsForPerMatch / leagueAverageGoalsForPerTeamPerMatch;
                team.DefenceStrength = team.GoalsAgainstPerMatch / leagueAverageGoalsForPerTeamPerMatch;
            }

            Debug.Log($"Statistical model trained on {trainingMatches.Count} matches.");
            Debug.Log($"League average goals per team per match: {leagueAverageGoalsForPerTeamPerMatch:F2}");
        }

        public void PrintTeamStrengths(int maxTeams = 10)
        {
            List<TeamStrength> sorted = new List<TeamStrength>(teamStrengths.Values);

            sorted.Sort((a, b) => b.AttackStrength.CompareTo(a.AttackStrength));

            Debug.Log("Top team attack strengths:");

            for (int i = 0; i < Mathf.Min(maxTeams, sorted.Count); i++)
            {
                TeamStrength team = sorted[i];

                Debug.Log(
                    $"{i + 1}. {team.TeamName} " +
                    $"P:{team.MatchesPlayed} " +
                    $"GF/Match:{team.GoalsForPerMatch:F2} " +
                    $"GA/Match:{team.GoalsAgainstPerMatch:F2} " +
                    $"Attack:{team.AttackStrength:F2} " +
                    $"Defence:{team.DefenceStrength:F2}"
                );
            }
        }

        private void AddTeamMatch(string teamName, int goalsFor, int goalsAgainst)
        {
            if (!teamStrengths.ContainsKey(teamName))
            {
                teamStrengths[teamName] = new TeamStrength
                {
                    TeamName = teamName
                };
            }

            TeamStrength team = teamStrengths[teamName];

            team.MatchesPlayed++;
            team.GoalsFor += goalsFor;
            team.GoalsAgainst += goalsAgainst;
        }

        public ExpectedGoalsPrediction PredictExpectedGoals(OpenFootballMatch fixture)
        {
            TeamStrength homeTeam = GetTeamStrengthOrAverage(fixture.HomeTeam);
            TeamStrength awayTeam = GetTeamStrengthOrAverage(fixture.AwayTeam);

            float expectedHomeGoals =
    averageHomeGoals *
    homeTeam.AttackStrength *
    awayTeam.DefenceStrength;

            float expectedAwayGoals =
                averageAwayGoals *
                awayTeam.AttackStrength *
                homeTeam.DefenceStrength;

            return new ExpectedGoalsPrediction
            {
                HomeTeam = fixture.HomeTeam,
                AwayTeam = fixture.AwayTeam,
                ExpectedHomeGoals = expectedHomeGoals,
                ExpectedAwayGoals = expectedAwayGoals
            };
        }
        private TeamStrength GetTeamStrengthOrAverage(string teamName)
        {
            if (teamStrengths.ContainsKey(teamName))
            {
                return teamStrengths[teamName];
            }

            if (!warnedMissingTeams.Contains(teamName))
            {
                Debug.LogWarning($"No training data found for {teamName}. Using average team strength.");
                warnedMissingTeams.Add(teamName);
            }

            return new TeamStrength
            {
                TeamName = teamName,
                MatchesPlayed = 0,
                GoalsForPerMatch = leagueAverageGoalsForPerTeamPerMatch,
                GoalsAgainstPerMatch = leagueAverageGoalsForPerTeamPerMatch,
                AttackStrength = 1f,
                DefenceStrength = 1f
            };

            
        }

        public void PrintExpectedGoalsSamples(List<OpenFootballMatch> evaluationMatches, int maxMatches = 10)
        {
            Debug.Log("Sample expected goals predictions:");

            for (int i = 0; i < Mathf.Min(maxMatches, evaluationMatches.Count); i++)
            {
                ExpectedGoalsPrediction prediction = PredictExpectedGoals(evaluationMatches[i]);

                Debug.Log(
                    $"{prediction.HomeTeam} vs {prediction.AwayTeam} " +
                    $"xG:{prediction.ExpectedHomeGoals:F2}-{prediction.ExpectedAwayGoals:F2}"
                );
            }
        }

        public SimulatedMatchResult SimulateMatch(OpenFootballMatch fixture)
        {
            ExpectedGoalsPrediction prediction = PredictExpectedGoals(fixture);

            int simulatedHomeGoals = SamplePoisson(prediction.ExpectedHomeGoals);
            int simulatedAwayGoals = SamplePoisson(prediction.ExpectedAwayGoals);

            return new SimulatedMatchResult
            {
                HomeTeam = prediction.HomeTeam,
                AwayTeam = prediction.AwayTeam,
                HomeGoals = simulatedHomeGoals,
                AwayGoals = simulatedAwayGoals,
                ExpectedHomeGoals = prediction.ExpectedHomeGoals,
                ExpectedAwayGoals = prediction.ExpectedAwayGoals
            };
        }

        private int SamplePoisson(float lambda)
        {
            float l = Mathf.Exp(-lambda);
            int k = 0;
            float p = 1f;

            do
            {
                k++;
                p *= Random.value;
            }
            while (p > l);

            return k - 1;
        }

        public void PrintSimulatedMatchSamples(List<OpenFootballMatch> evaluationMatches, int maxMatches = 10)
        {
            Debug.Log("Sample simulated match results:");

            for (int i = 0; i < Mathf.Min(maxMatches, evaluationMatches.Count); i++)
            {
                SimulatedMatchResult result = SimulateMatch(evaluationMatches[i]);

                Debug.Log(
                    $"{result.HomeTeam} {result.HomeGoals}-{result.AwayGoals} {result.AwayTeam} " +
                    $"xG:{result.ExpectedHomeGoals:F2}-{result.ExpectedAwayGoals:F2}"
                );
            }
        }

        public List<SimulatedMatchResult> SimulateSeason(List<OpenFootballMatch> fixtures, bool logResult = true)
        {
            List<SimulatedMatchResult> results = new();

            foreach (OpenFootballMatch fixture in fixtures)
            {
                SimulatedMatchResult result = SimulateMatch(fixture);
                results.Add(result);
            }

            if (logResult)
            {
                Debug.Log($"Simulated {results.Count} matches.");
            }

            return results;
        }
    }
}
