using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Data
{
    [Serializable]
    public sealed class FootballClubRegistryData
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion;
        [JsonProperty("sourceCommit")] public string SourceCommit;
        [JsonProperty("clubs")] public List<WorldClubRecord> Clubs = new();
    }

    [Serializable]
    public sealed class WorldClubRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("countryCode")] public string CountryCode;
        [JsonProperty("countryPath")] public string CountryPath;
        [JsonProperty("name")] public string Name;
        [JsonProperty("foundedYear")] public int? FoundedYear;
        [JsonProperty("stadium")] public string Stadium;
        [JsonProperty("locality")] public string Locality;
        [JsonProperty("aliases")] public List<string> Aliases = new();
        [JsonProperty("sourceFile")] public string SourceFile;
    }

    // Read-only identity lookup. It deliberately has no simulation or gameplay
    // behaviour; activation level belongs to the future world-simulation layer.
    public sealed class FootballClubRegistry
    {
        private readonly FootballClubRegistryData data;
        private readonly Dictionary<string, WorldClubRecord> clubsById = new(StringComparer.Ordinal);

        public FootballClubRegistryData Data => data;

        private FootballClubRegistry(FootballClubRegistryData source)
        {
            data = source;
            foreach (WorldClubRecord club in source.Clubs)
            {
                if (string.IsNullOrWhiteSpace(club.Id))
                    throw new InvalidOperationException("World club registry contains a club without an ID.");
                if (!clubsById.TryAdd(club.Id, club))
                    throw new InvalidOperationException($"Duplicate world club ID: {club.Id}");
            }
        }

        public static FootballClubRegistry FromJson(string json)
        {
            FootballClubRegistryData source = JsonConvert.DeserializeObject<FootballClubRegistryData>(json);
            if (source == null) throw new InvalidOperationException("World club registry JSON was empty or invalid.");
            if (source.SchemaVersion != 1)
                throw new InvalidOperationException($"Unsupported world club registry schema {source.SchemaVersion}; expected 1.");
            return new FootballClubRegistry(source);
        }

        public static FootballClubRegistry FromTextAsset(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return FromJson(asset.text);
        }

        public bool TryGetClub(string clubId, out WorldClubRecord club) => clubsById.TryGetValue(clubId, out club);
    }
}
