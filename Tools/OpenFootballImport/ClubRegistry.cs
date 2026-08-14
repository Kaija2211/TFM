using System.Text.RegularExpressions;

sealed class RegistryClub
{
    public required string Id { get; init; }
    public required string CountryCode { get; init; }
    public required string CountryPath { get; init; }
    public required string Name { get; init; }
    public int? FoundedYear { get; init; }
    public string? Stadium { get; init; }
    public string? Locality { get; init; }
    public required string SourceFile { get; init; }
    public HashSet<string> Aliases { get; } = new(StringComparer.Ordinal);
}

sealed record RegistryCollision(string CountryCode, string Alias, string[] ClubIds);

sealed class ClubRegistry
{
    private static readonly Regex FoundedPattern = new(@"(?:^|,\s*)(?<year>1[6-9]\d{2}|20\d{2})(?:\s|,|$)", RegexOptions.Compiled);
    private static readonly Regex LifeSpanPattern = new(@"\s+\((?:1[6-9]\d{2}|20\d{2})(?:\s*[-–]\s*(?:1[6-9]\d{2}|20\d{2})?)?\)\s*$", RegexOptions.Compiled);

    private readonly Dictionary<string, RegistryClub> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RegistryClub>> byCountryAlias = new(StringComparer.Ordinal);

    public IReadOnlyCollection<RegistryClub> Clubs => byId.Values;
    public IReadOnlyList<RegistryCollision> Collisions { get; private set; } = Array.Empty<RegistryCollision>();
    public string SourceCommit { get; }

    private ClubRegistry(string sourceCommit) => SourceCommit = sourceCommit;

    public static ClubRegistry Load(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Club registry does not exist: {root}");
        ClubRegistry registry = new(ReadGitCommit(root));
        foreach (string file in Directory.EnumerateFiles(root, "*.clubs.txt", SearchOption.AllDirectories).Order())
            registry.ParseFile(root, file);
        registry.BuildAliasIndex();
        return registry;
    }

    public bool TryResolve(string countryCode, string rawName, out RegistryClub? club)
    {
        string key = AliasKey(countryCode, rawName);
        if (byCountryAlias.TryGetValue(key, out List<RegistryClub>? candidates) && candidates.Count == 1)
        {
            club = candidates[0];
            return true;
        }
        club = null;
        return false;
    }

    public bool TryResolve(IEnumerable<string> countryCodes, string rawName, out RegistryClub? club)
    {
        RegistryClub[] matches = countryCodes
            .Select(code => byCountryAlias.TryGetValue(AliasKey(code, rawName), out List<RegistryClub>? candidates) && candidates.Count == 1
                ? candidates[0]
                : null)
            .Where(candidate => candidate != null)
            .Cast<RegistryClub>()
            .Distinct()
            .ToArray();
        if (matches.Length == 1)
        {
            club = matches[0];
            return true;
        }
        club = null;
        return false;
    }

    public bool TryResolveGlobally(string rawName, out RegistryClub? club) =>
        TryResolve(byId.Values.Select(item => item.CountryCode).Distinct(StringComparer.Ordinal), rawName, out club);

    private void ParseFile(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        string countryCode = Path.GetFileName(file)[..^".clubs.txt".Length].ToLowerInvariant();
        string countryPath = Path.GetFileName(Path.GetDirectoryName(file))!.ToLowerInvariant();
        RegistryClub? current = null;

        foreach (string rawLine in File.ReadLines(file))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('=') || trimmed.StartsWith('-')) continue;
            if (trimmed.StartsWith('|'))
            {
                if (current == null) continue;
                foreach (string alias in trimmed[1..].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    string cleaned = StripComment(alias);
                    if (cleaned.Length > 0) current.Aliases.Add(cleaned);
                }
                continue;
            }

            // Canonical club records are flush-left. Indented non-alias rows contain
            // advisory addresses and notes in a handful of registry files.
            if (char.IsWhiteSpace(rawLine[0])) continue;

            string content = StripComment(trimmed);
            if (content.Length == 0 || content.StartsWith('@')) continue;
            string[] fields = content.Split(',', StringSplitOptions.TrimEntries);
            string[] inlineNames = fields[0].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string name = Regex.Replace(inlineNames[0].Trim(), @"^(?:ii|iii|iv|v)\)\s*", "", RegexOptions.IgnoreCase);
            if (name.Length == 0 || name.Contains("=>", StringComparison.Ordinal) || name.Contains('⇒')) continue;

            string stableName = LifeSpanPattern.Replace(name, "").Trim();
            string id = $"{countryCode}:{Slugify(stableName)}";
            if (byId.ContainsKey(id))
            {
                // Keep distinct same-name entries visible for review instead of silently merging clubs.
                int suffix = 2;
                while (byId.ContainsKey($"{id}-{suffix}")) suffix++;
                id = $"{id}-{suffix}";
            }

            int? founded = null;
            Match foundedMatch = FoundedPattern.Match(content);
            if (foundedMatch.Success) founded = int.Parse(foundedMatch.Groups["year"].Value);
            string? stadium = null;
            int stadiumMarker = content.IndexOf('@');
            if (stadiumMarker >= 0)
            {
                string tail = content[(stadiumMarker + 1)..].Trim();
                stadium = tail.Split(',', 2, StringSplitOptions.TrimEntries)[0];
            }
            string? locality = fields.LastOrDefault(field => field.Length > 0 && !field.StartsWith('@') && !FoundedPattern.IsMatch(", " + field));
            if (locality == fields[0]) locality = null;

            current = new RegistryClub
            {
                Id = id,
                CountryCode = countryCode,
                CountryPath = countryPath,
                Name = stableName,
                FoundedYear = founded,
                Stadium = stadium,
                Locality = locality,
                SourceFile = relative
            };
            current.Aliases.Add(stableName);
            if (stableName != name) current.Aliases.Add(name);
            foreach (string inlineAlias in inlineNames.Skip(1)) current.Aliases.Add(inlineAlias);
            byId.Add(id, current);
        }
    }

    private void BuildAliasIndex()
    {
        foreach (RegistryClub club in byId.Values)
        {
            foreach (string alias in club.Aliases)
            {
                string key = AliasKey(club.CountryCode, alias);
                if (!byCountryAlias.TryGetValue(key, out List<RegistryClub>? clubs)) byCountryAlias[key] = clubs = new();
                if (!clubs.Contains(club)) clubs.Add(club);
            }
        }
        Collisions = byCountryAlias
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => new RegistryCollision(
                pair.Key[..pair.Key.IndexOf('|')],
                pair.Key[(pair.Key.IndexOf('|') + 1)..],
                pair.Value.Select(club => club.Id).Order().ToArray()))
            .OrderBy(item => item.CountryCode).ThenBy(item => item.Alias).ToArray();
    }

    private static string AliasKey(string countryCode, string name) => $"{countryCode}|{ClubIdentityMap.IdentityKey(name)}";
    private static string Slugify(string value) => ClubIdentityMap.IdentityKey(value).Replace(' ', '-');
    private static string StripComment(string value) => value.Split('#', 2)[0].Trim();

    private static string ReadGitCommit(string source)
    {
        string headPath = Path.Combine(source, ".git", "HEAD");
        if (!File.Exists(headPath)) return "unknown";
        string value = File.ReadAllText(headPath).Trim();
        if (!value.StartsWith("ref: ")) return value;
        string reference = Path.Combine(source, ".git", value[5..].Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(reference) ? File.ReadAllText(reference).Trim() : "unknown";
    }
}
