using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        private readonly Dictionary<string, WorldClubRecord> clubsByAlias = new(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguousAliases = new(StringComparer.Ordinal);

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
                AddAlias(club.CountryCode, club.Name, club);
                foreach (string alias in club.Aliases) AddAlias(club.CountryCode, alias, club);
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

        public bool TryResolveAlias(string countryCode, string name, out WorldClubRecord club)
        {
            string key = AliasKey(countryCode, name);
            if (!ambiguousAliases.Contains(key) && clubsByAlias.TryGetValue(key, out club)) return true;
            string withoutSuffix = Regex.Replace(Normalize(name), @"\s+(?:afc|fc)$", "");
            key = $"{countryCode}|{withoutSuffix}";
            if (!ambiguousAliases.Contains(key) && clubsByAlias.TryGetValue(key, out club)) return true;
            club = null;
            return false;
        }

        private void AddAlias(string countryCode, string alias, WorldClubRecord club)
        {
            AddAliasKey(AliasKey(countryCode, alias), club);
            string withoutSuffix = Regex.Replace(Normalize(alias), @"\s+(?:afc|fc)$", "");
            AddAliasKey($"{countryCode}|{withoutSuffix}", club);
        }

        private void AddAliasKey(string key, WorldClubRecord club)
        {
            if (ambiguousAliases.Contains(key)) return;
            if (clubsByAlias.TryGetValue(key, out WorldClubRecord existing) && existing.Id != club.Id)
            {
                clubsByAlias.Remove(key);
                ambiguousAliases.Add(key);
                return;
            }
            clubsByAlias[key] = club;
        }

        private static string AliasKey(string countryCode, string name) => $"{countryCode}|{Normalize(name)}";

        private static string Normalize(string value)
        {
            string decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
            StringBuilder text = new();
            foreach (char character in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) text.Append(character);
            return Regex.Replace(text.ToString().ToLowerInvariant().Replace("&", " and "), @"[^a-z0-9]+", " ").Trim();
        }
    }
}
