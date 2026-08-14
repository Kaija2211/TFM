using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Data
{
    // Versioned, generated output of Tools/OpenFootballImport. Runtime systems read
    // this compact model and never parse the raw OpenFootball archive directly.
    [Serializable]
    public sealed class FootballWorldHistoryData
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion;
        [JsonProperty("sourceCommit")] public string SourceCommit;
        [JsonProperty("sourceCommits")] public List<HistorySourceCommitRecord> SourceCommits = new();
        [JsonProperty("generatedFromCompleteFiles")] public int GeneratedFromCompleteFiles;
        [JsonProperty("excludedFiles")] public List<string> ExcludedFiles = new();
        [JsonProperty("clubs")] public List<HistoricalClubRecord> Clubs = new();
        [JsonProperty("competitionSeasons")] public List<CompetitionSeasonRecord> CompetitionSeasons = new();
        [JsonProperty("divisionTransitions")] public List<DivisionTransitionRecord> DivisionTransitions = new();
        [JsonProperty("generationPriors")] public List<ClubGenerationPriorRecord> GenerationPriors = new();
        [JsonProperty("clubSeasons")] public List<ClubSeasonRecord> ClubSeasons = new();
    }

    [Serializable]
    public sealed class HistorySourceCommitRecord
    {
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("commit")] public string Commit;
    }

    [Serializable]
    public sealed class DivisionTransitionRecord
    {
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("fromLevel")] public int FromLevel;
        [JsonProperty("toLevel")] public int ToLevel;
        [JsonProperty("samples")] public int Samples;
        [JsonProperty("meanAttackIndexRatio")] public double MeanAttackIndexRatio;
        [JsonProperty("medianAttackIndexRatio")] public double MedianAttackIndexRatio;
        [JsonProperty("meanDefenceQualityRatio")] public double MeanDefenceQualityRatio;
        [JsonProperty("medianDefenceQualityRatio")] public double MedianDefenceQualityRatio;
        [JsonProperty("meanPointsPerGameIndexRatio")] public double MeanPointsPerGameIndexRatio;
        [JsonProperty("medianPointsPerGameIndexRatio")] public double MedianPointsPerGameIndexRatio;
    }

    [Serializable]
    public sealed class ClubGenerationPriorRecord
    {
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("targetSeason")] public string TargetSeason;
        [JsonProperty("targetLevel")] public int TargetLevel;
        [JsonProperty("competitionId")] public string CompetitionId;
        [JsonProperty("clubId")] public string ClubId;
        [JsonProperty("clubName")] public string ClubName;
        [JsonProperty("attackIndex")] public double AttackIndex;
        [JsonProperty("defenceQualityIndex")] public double DefenceQualityIndex;
        [JsonProperty("pointsPerGameIndex")] public double PointsPerGameIndex;
        [JsonProperty("confidence")] public double Confidence;
        [JsonProperty("source")] public string Source;
    }

    [Serializable]
    public sealed class HistoricalClubRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
    }

    [Serializable]
    public sealed class CompetitionSeasonRecord
    {
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("season")] public string Season;
        [JsonProperty("level")] public int Level;
        [JsonProperty("competitionId")] public string CompetitionId;
        [JsonProperty("competition")] public string Competition;
        [JsonProperty("parsedMatches")] public int ParsedMatches;
        [JsonProperty("clubs")] public List<string> ClubIds = new();
    }

    [Serializable]
    public sealed class ClubSeasonRecord
    {
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("season")] public string Season;
        [JsonProperty("level")] public int Level;
        [JsonProperty("competitionId")] public string CompetitionId;
        [JsonProperty("competition")] public string Competition;
        [JsonProperty("clubId")] public string ClubId;
        [JsonProperty("clubName")] public string ClubName;
        [JsonProperty("played")] public int Played;
        [JsonProperty("won")] public int Won;
        [JsonProperty("drawn")] public int Drawn;
        [JsonProperty("lost")] public int Lost;
        [JsonProperty("goalsFor")] public int GoalsFor;
        [JsonProperty("goalsAgainst")] public int GoalsAgainst;
        [JsonProperty("goalDifference")] public int GoalDifference;
        [JsonProperty("points")] public int Points;
    }

    // Immutable lookup facade built once after deserialization. Stable string club IDs
    // are the cross-season/save boundary; display names are never used as identity.
    public sealed class FootballWorldHistory
    {
        private readonly FootballWorldHistoryData data;
        private readonly Dictionary<string, HistoricalClubRecord> clubsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClubSeasonRecord> clubSeasonsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CompetitionSeasonRecord> competitionsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClubGenerationPriorRecord> generationPriorsByKey = new(StringComparer.Ordinal);

        public FootballWorldHistoryData Data => data;

        private FootballWorldHistory(FootballWorldHistoryData source)
        {
            data = source;
            foreach (HistoricalClubRecord club in source.Clubs)
            {
                if (string.IsNullOrWhiteSpace(club.Id))
                    throw new InvalidOperationException("Football history contains a club with no stable ID.");
                if (!clubsById.TryAdd(club.Id, club))
                    throw new InvalidOperationException($"Duplicate historical club ID: {club.Id}");
            }

            foreach (CompetitionSeasonRecord competition in source.CompetitionSeasons)
            {
                string key = CompetitionKey(competition.CountryCode, competition.Season, competition.CompetitionId);
                if (!competitionsByKey.TryAdd(key, competition))
                    throw new InvalidOperationException($"Duplicate competition season: {key}");
            }

            foreach (ClubSeasonRecord clubSeason in source.ClubSeasons)
            {
                if (!clubsById.ContainsKey(clubSeason.ClubId))
                    throw new InvalidOperationException($"Club-season references unknown club ID: {clubSeason.ClubId}");
                string key = ClubSeasonKey(clubSeason.ClubId, clubSeason.Season, clubSeason.CompetitionId);
                if (!clubSeasonsByKey.TryAdd(key, clubSeason))
                    throw new InvalidOperationException($"Duplicate club-season record: {key}");
            }

            foreach (ClubGenerationPriorRecord prior in source.GenerationPriors)
            {
                string key = ClubSeasonKey(prior.ClubId, prior.TargetSeason, prior.CompetitionId);
                if (!generationPriorsByKey.TryAdd(key, prior))
                    throw new InvalidOperationException($"Duplicate club generation prior: {key}");
            }
        }

        public static FootballWorldHistory FromJson(string json)
        {
            FootballWorldHistoryData source = JsonConvert.DeserializeObject<FootballWorldHistoryData>(json);
            if (source == null) throw new InvalidOperationException("Football history JSON was empty or invalid.");
            if (source.SchemaVersion != 2)
                throw new InvalidOperationException($"Unsupported football history schema {source.SchemaVersion}; expected 2.");
            return new FootballWorldHistory(source);
        }

        public static FootballWorldHistory FromTextAsset(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return FromJson(asset.text);
        }

        public bool TryGetClub(string clubId, out HistoricalClubRecord club) => clubsById.TryGetValue(clubId, out club);

        public bool TryGetClubSeason(string clubId, string season, string competitionId, out ClubSeasonRecord clubSeason) =>
            clubSeasonsByKey.TryGetValue(ClubSeasonKey(clubId, season, competitionId), out clubSeason);

        public bool TryGetCompetitionSeason(string countryCode, string season, string competitionId, out CompetitionSeasonRecord competition) =>
            competitionsByKey.TryGetValue(CompetitionKey(countryCode, season, competitionId), out competition);

        public bool TryGetGenerationPrior(string clubId, string season, string competitionId, out ClubGenerationPriorRecord prior) =>
            generationPriorsByKey.TryGetValue(ClubSeasonKey(clubId, season, competitionId), out prior);

        private static string ClubSeasonKey(string clubId, string season, string competitionId) => $"{clubId}|{season}|{competitionId}";
        private static string CompetitionKey(string countryCode, string season, string competitionId) => $"{countryCode}|{season}|{competitionId}";
    }
}
