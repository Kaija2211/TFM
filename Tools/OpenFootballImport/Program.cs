using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

record ParsedMatch(string Home, string Away, int HomeGoals, int AwayGoals, int Matchday);

record FileAudit(
    string CountryCode,
    string Season,
    int Level,
    string CompetitionId,
    string Competition,
    string SourceFile,
    int? DeclaredTeams,
    int? DeclaredMatches,
    int? ExpectedRegularMatches,
    int ParsedMatches,
    int PostseasonMatches,
    int UniqueTeams,
    int UnparsedScoreLines,
    bool Complete,
    List<string> Errors);

record CanonicalClub(string Id, string Name);

record FileAuditResult(FileAudit Audit, List<ParsedMatch> Matches);

record SourceFileSpec(string CountryCode, string Season, int Level, string CompetitionId, string Competition, string Path, string SourceRoot);

record EuropeanClubSeason(
    string ClubId,
    string ClubName,
    string Season,
    string Competition,
    bool Qualifying,
    int Played,
    int Points,
    int KnockoutDepth);

sealed class EuropeanAccumulator
{
    public required string ClubId { get; init; }
    public required string ClubName { get; init; }
    public required string Season { get; init; }
    public required string Competition { get; init; }
    public required bool Qualifying { get; init; }
    public int Played { get; set; }
    public int Points { get; set; }
    public int KnockoutDepth { get; set; }
}

sealed class ClubSeasonSummary
{
    public required string CountryCode { get; init; }
    public required string Season { get; init; }
    public required int Level { get; init; }
    public required string CompetitionId { get; init; }
    public required string Competition { get; init; }
    public required string ClubId { get; init; }
    public required string ClubName { get; init; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public int Points => Won * 3 + Drawn;
}

sealed class DivisionTransitionSummary
{
    public required string CountryCode { get; init; }
    public required int FromLevel { get; init; }
    public required int ToLevel { get; init; }
    public required int Samples { get; init; }
    public required double MeanAttackIndexRatio { get; init; }
    public required double MedianAttackIndexRatio { get; init; }
    public required double MeanDefenceQualityRatio { get; init; }
    public required double MedianDefenceQualityRatio { get; init; }
    public required double MeanPointsPerGameIndexRatio { get; init; }
    public required double MedianPointsPerGameIndexRatio { get; init; }
}

sealed class ClubGenerationPrior
{
    public required string CountryCode { get; init; }
    public required string TargetSeason { get; init; }
    public required int TargetLevel { get; init; }
    public required string CompetitionId { get; init; }
    public required string ClubId { get; init; }
    public required string ClubName { get; init; }
    public required double AttackIndex { get; init; }
    public required double DefenceQualityIndex { get; init; }
    public required double PointsPerGameIndex { get; init; }
    public required double Confidence { get; init; }
    public required string Source { get; init; }
}

sealed class CompetitionSeasonSummary
{
    public required string CountryCode { get; init; }
    public required string Season { get; init; }
    public required int Level { get; init; }
    public required string CompetitionId { get; init; }
    public required string Competition { get; init; }
    public required int ParsedMatches { get; init; }
    public required string[] Clubs { get; init; }
}

sealed class ClubWorldGenerationProfile
{
    public required string ClubId { get; init; }
    public required string ClubName { get; init; }
    public required string CountryCode { get; init; }
    public required string ReferenceSeason { get; init; }
    public required string CompetitionId { get; init; }
    public required int Level { get; init; }
    public required double Reputation { get; init; }
    public required double FirstTeamOverall { get; init; }
    public required double BenchOverall { get; init; }
    public required double ReserveOverall { get; init; }
    public required double Confidence { get; init; }
    public required int EvidenceSeasons { get; init; }
    public required double HonoursScore { get; init; }
    public required double RecentEuropeanScore { get; init; }
    public required double EuropeanReputationBoost { get; init; }
    public required string ReputationSource { get; init; }
}

sealed record ClubHonoursEvidence(string ClubId, double HonoursScore, double ReputationFloor, string SourceUrl);
sealed record ClubEloComparison(string ClubId, string ClubName, string CountryCode, double GeneratedOverall, double Elo);

sealed class ClubIdentityMap
{
    private readonly Dictionary<string, CanonicalClub> byAlias = new(StringComparer.Ordinal);
    private readonly ClubRegistry registry;
    private readonly string[] countryCodes;
    private readonly HashSet<string> unresolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CanonicalClub> supplemental = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UnresolvedNames => unresolved;
    public IReadOnlyCollection<CanonicalClub> SupplementalClubs => supplemental.Values;

    public ClubIdentityMap(string path, ClubRegistry registry, bool includeManualOverrides, params string[] countryCodes)
    {
        this.registry = registry;
        this.countryCodes = countryCodes;
        if (!includeManualOverrides) return;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement club in document.RootElement.GetProperty("clubs").EnumerateArray())
        {
            string configuredCountry = club.TryGetProperty("countryCode", out JsonElement countryElement)
                ? countryElement.GetString()!
                : "eng";
            if (!countryCodes.Contains(configuredCountry, StringComparer.Ordinal)) continue;
            CanonicalClub configured = new(
                club.GetProperty("id").GetString()!,
                club.GetProperty("name").GetString()!);

            HashSet<string> aliases = new(StringComparer.Ordinal) { configured.Name };
            if (club.TryGetProperty("aliases", out JsonElement aliasesElement))
            {
                foreach (JsonElement alias in aliasesElement.EnumerateArray())
                {
                    aliases.Add(alias.GetString()!);
                }
            }

            RegistryClub? registryClub = aliases
                .Select(alias => registry.TryResolve(countryCodes, alias, out RegistryClub? match) ? match : null)
                .FirstOrDefault(match => match != null);
            CanonicalClub canonical = registryClub == null
                ? configured
                : new CanonicalClub(registryClub.Id, registryClub.Name);

            foreach (string alias in aliases)
            {
                string key = IdentityKey(alias);
                if (byAlias.TryGetValue(key, out CanonicalClub? existing) && existing.Id != canonical.Id)
                {
                    throw new InvalidDataException($"Alias collision for '{alias}': {existing.Id} vs {canonical.Id}");
                }
                byAlias[key] = canonical;
            }
        }
    }

    public CanonicalClub Resolve(string rawName)
    {
        string cleaned = Regex.Replace(rawName.Trim(), @"\s+", " ");
        string key = IdentityKey(cleaned);
        if (byAlias.TryGetValue(key, out CanonicalClub? manualClub)) return manualClub;
        if (registry.TryResolve(countryCodes, cleaned, out RegistryClub? registryClub))
            return new CanonicalClub(registryClub!.Id, registryClub.Name);
        unresolved.Add(cleaned);
        CanonicalClub provisional = new($"{countryCodes[0]}:{Slugify(cleaned)}", cleaned);
        supplemental.TryAdd(provisional.Id, provisional);
        return provisional;
    }

    public static string IdentityKey(string name)
    {
        string value = name.Normalize(NormalizationForm.FormD);
        StringBuilder ascii = new();
        foreach (char character in value)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                ascii.Append(character);
            }
        }
        value = ascii.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace("&", " and ");
        value = Regex.Replace(value, @"\bfootball club\b", " ");
        value = Regex.Replace(value, @"[^a-z0-9]+", " ");
        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string Slugify(string value) => IdentityKey(value).Replace(' ', '-');
}

static class Program
{
    private static readonly Regex SeasonPattern = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex FilePattern = new(@"^(?<level>[1-5])-(?<competition>premierleague|division1|division2|division3|championship|league1|league2|nationalleague)\.txt$", RegexOptions.Compiled);
    private static readonly Regex MatchdayPattern = new(@"^▪\s*(?:(?:Regular,\s*)?(?:Matchday|Round)\s+|Regular Season\s*-\s*)(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberedRoundPattern = new(@"^▪\s*(\d+)\.\s*Round", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NewMatchPattern = new(@"^(.+?)\s+v\s+(.+?)\s+(\d+)-(\d+)(?:\s+\(\d+-\d+\))?\s*$", RegexOptions.Compiled);
    private static readonly Regex OldMatchPattern = new(@"^(.+?)\s+(\d+)-(\d+)(?:\s+\(\d+-\d+\))?\s+(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex TeamsPattern = new(@"^#\s*Teams\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex MatchesPattern = new(@"^#\s*Matches\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex KickoffPattern = new(@"^\d{1,2}:\d{2}\s+", RegexOptions.Compiled);
    private static readonly Regex AnnotationPattern = new(@"\s+\[[^\]]+\]\s*$", RegexOptions.Compiled);
    private static readonly Regex UefaCountryPattern = new(@"\s+\((?<country>[A-Z]{3})\)\s*$", RegexOptions.Compiled);

    private static readonly Dictionary<int, string> CompetitionNames = new()
    {
        [1] = "Premier League",
        [2] = "Championship",
        [3] = "League One",
        [4] = "League Two",
        [5] = "National League"
    };

    public static int Main(string[] args)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string source = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "england"));
        string output = Path.Combine(repositoryRoot, "Temp", "OpenFootballAudit");
        string clubsSource = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "clubs"));
        string europeanSource = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "champions-league"));
        string? clubEloSnapshot = null;
        string? publishHistory = null;
        string? publishClubs = null;

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--source" && index + 1 < args.Length) source = Path.GetFullPath(args[++index]);
            else if (args[index] == "--output" && index + 1 < args.Length) output = Path.GetFullPath(args[++index]);
            else if (args[index] == "--clubs-source" && index + 1 < args.Length) clubsSource = Path.GetFullPath(args[++index]);
            else if (args[index] == "--publish-history" && index + 1 < args.Length) publishHistory = Path.GetFullPath(args[++index]);
            else if (args[index] == "--publish-clubs" && index + 1 < args.Length) publishClubs = Path.GetFullPath(args[++index]);
            else if (args[index] == "--club-elo-snapshot" && index + 1 < args.Length) clubEloSnapshot = Path.GetFullPath(args[++index]);
            else return Fail($"Unknown or incomplete argument: {args[index]}");
        }

        if (!Directory.Exists(source)) return Fail($"OpenFootball source directory does not exist: {source}");
        if (!Directory.Exists(clubsSource)) return Fail($"OpenFootball clubs directory does not exist: {clubsSource}");

        ClubRegistry registry = ClubRegistry.Load(clubsSource);
        string manualAliases = Path.Combine(repositoryRoot, "Tools", "OpenFootballImport", "club_aliases.json");
        Dictionary<string, ClubIdentityMap> identityMaps = new(StringComparer.Ordinal)
        {
            // Welsh clubs participate in the English pyramid but retain Welsh identities.
            ["eng"] = new ClubIdentityMap(manualAliases, registry, true, "eng", "wal"),
            ["de"] = new ClubIdentityMap(manualAliases, registry, true, "de"),
            // FC Andorra plays in Spain while retaining an Andorran identity.
            ["es"] = new ClubIdentityMap(manualAliases, registry, true, "es", "ad"),
            ["it"] = new ClubIdentityMap(manualAliases, registry, true, "it"),
            // Monaco competes in the French pyramid while retaining its own nation identity.
            ["fr"] = new ClubIdentityMap(manualAliases, registry, true, "fr", "mc")
        };
        Dictionary<string, HashSet<string>> aliases = new(StringComparer.Ordinal);
        List<FileAudit> audits = new();
        List<FileAuditResult> results = new();
        Dictionary<string, CanonicalClub> clubs = new(StringComparer.Ordinal);

        List<SourceFileSpec> sourceSpecs = DiscoverTopFiveSources(source).OrderBy(item => item.CountryCode).ThenBy(item => item.Season).ThenBy(item => item.Level).ThenBy(item => item.CompetitionId).ToList();
        Dictionary<string, string> sourceCommits = sourceSpecs.GroupBy(item => item.CountryCode).ToDictionary(group => group.Key, group => ReadGitCommit(group.First().SourceRoot));
        Dictionary<string, ClubHonoursEvidence> honoursEvidence = LoadHonoursEvidence(Path.Combine(repositoryRoot, "Tools", "OpenFootballImport", "club_honours.json"));
        EuropeanClubSeason[] europeanSeasons = Directory.Exists(europeanSource)
            ? LoadEuropeanSeasons(europeanSource, registry)
            : Array.Empty<EuropeanClubSeason>();
        sourceCommits["uefa"] = Directory.Exists(europeanSource) ? ReadGitCommit(europeanSource) : "missing";
        foreach (SourceFileSpec spec in sourceSpecs)
        {
            FileAuditResult result = AuditFile(spec, identityMaps[spec.CountryCode], aliases, clubs);
            results.Add(result);
            audits.Add(result.Audit);
        }

        Directory.CreateDirectory(output);
        string commit = ReadGitCommit(source);
        JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(Path.Combine(output, "archive_audit.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source,
            sourceCommit = commit,
            sourceCommits = sourceCommits.OrderBy(pair => pair.Key).Select(pair => new { countryCode = pair.Key, commit = pair.Value }).ToArray(),
            files = audits
        }, jsonOptions) + Environment.NewLine);
        File.WriteAllText(Path.Combine(output, "archive_audit.md"), RenderMarkdown(audits, source, commit));
        File.WriteAllText(Path.Combine(output, "club_alias_candidates.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            clubs = aliases.OrderBy(pair => pair.Key).Select(pair => new { id = pair.Key, aliases = pair.Value.Order().ToArray() })
        }, jsonOptions) + Environment.NewLine);
        WriteRegistryOutputs(output, clubsSource, registry, identityMaps, aliases, jsonOptions);
        ClubWorldGenerationProfile[] worldProfiles = WriteWorldHistory(output, commit, sourceCommits, honoursEvidence, europeanSeasons, results, identityMaps, clubs, jsonOptions);
        if (clubEloSnapshot is not null)
            WriteClubEloAudit(output, clubEloSnapshot, worldProfiles, registry);
        if (publishHistory is not null)
        {
            string? publishDirectory = Path.GetDirectoryName(publishHistory);
            if (!string.IsNullOrWhiteSpace(publishDirectory)) Directory.CreateDirectory(publishDirectory);
            File.Copy(Path.Combine(output, "football_world_history.json"), publishHistory, overwrite: true);
            Console.WriteLine($"Published runtime history: {publishHistory}");
        }
        if (publishClubs is not null)
        {
            string? publishDirectory = Path.GetDirectoryName(publishClubs);
            if (!string.IsNullOrWhiteSpace(publishDirectory)) Directory.CreateDirectory(publishDirectory);
            File.Copy(Path.Combine(output, "global_club_registry.json"), publishClubs, overwrite: true);
            Console.WriteLine($"Published runtime club registry: {publishClubs}");
        }

        int failed = audits.Count(item => !item.Complete);
        Console.WriteLine($"Audited {audits.Count} competition files and {audits.Sum(item => item.ParsedMatches):N0} matches.");
        Console.WriteLine($"{failed} files require review. Report: {Path.Combine(output, "archive_audit.md")}");
        return 0;
    }

    private static void WriteRegistryOutputs(
        string output,
        string clubsSource,
        ClubRegistry registry,
        Dictionary<string, ClubIdentityMap> identityMaps,
        Dictionary<string, HashSet<string>> observedAliases,
        JsonSerializerOptions jsonOptions)
    {
        HashSet<string> canonicalRegistryIds = registry.Clubs.Select(club => club.Id).ToHashSet(StringComparer.Ordinal);
        var registryPayload = new
        {
            schemaVersion = 1,
            source = clubsSource,
            sourceCommit = registry.SourceCommit,
            clubs = registry.Clubs.Select(club => new
                {
                    id = club.Id, countryCode = club.CountryCode, countryPath = club.CountryPath, name = club.Name,
                    foundedYear = club.FoundedYear, stadium = club.Stadium, locality = club.Locality,
                    aliases = club.Aliases.Order().ToArray(), sourceFile = club.SourceFile
                })
                .Concat(identityMaps.Values.SelectMany(map => map.SupplementalClubs).Where(club => !canonicalRegistryIds.Contains(club.Id)).DistinctBy(club => club.Id).Select(club => new
                {
                    id = club.Id, countryCode = club.Id.Split(':', 2)[0], countryPath = club.Id.Split(':', 2)[0], name = club.Name,
                    foundedYear = (int?)null, stadium = (string?)null, locality = (string?)null,
                    aliases = new[] { club.Name }, sourceFile = "supplemental:observed-match-history"
                }))
                .OrderBy(club => club.id),
            collisions = registry.Collisions
        };
        File.WriteAllText(Path.Combine(output, "global_club_registry.json"), JsonSerializer.Serialize(registryPayload, jsonOptions) + Environment.NewLine);

        var countries = identityMaps.OrderBy(pair => pair.Key).Select(pair => new
        {
            countryCode = pair.Key,
            registryClubs = registry.Clubs.Count(club => club.CountryCode == pair.Key),
            unresolvedNames = pair.Value.UnresolvedNames.Order().ToArray(),
            registryAliasCollisions = registry.Collisions.Where(item => item.CountryCode == pair.Key).ToArray()
        }).ToArray();
        var reconciliation = new
        {
            schemaVersion = 2,
            observedResolvedClubs = observedAliases.Keys.Count(id => !id.Contains(":unresolved:", StringComparison.Ordinal)),
            countries
        };
        File.WriteAllText(Path.Combine(output, "top_five_identity_reconciliation.json"), JsonSerializer.Serialize(reconciliation, jsonOptions) + Environment.NewLine);
        StringBuilder report = new();
        report.AppendLine("# Top-five club identity reconciliation").AppendLine();
        report.AppendLine($"- Global canonical clubs: {registry.Clubs.Count:N0}");
        report.AppendLine($"- Resolved clubs observed across audited archives: {reconciliation.observedResolvedClubs:N0}").AppendLine();
        report.AppendLine("Welsh clubs participating in the English pyramid retain `wal:` identities.").AppendLine();
        foreach (var country in countries)
        {
            report.AppendLine($"## {country.countryCode}").AppendLine();
            report.AppendLine($"- Registry clubs: {country.registryClubs:N0}");
            report.AppendLine($"- Unresolved observed names: {country.unresolvedNames.Length:N0}");
            report.AppendLine($"- Ambiguous registry aliases: {country.registryAliasCollisions.Length:N0}");
            foreach (string name in country.unresolvedNames) report.AppendLine($"  - Unresolved: {name}");
            foreach (RegistryCollision collision in country.registryAliasCollisions)
                report.AppendLine($"  - Ambiguous `{collision.Alias}`: {string.Join(", ", collision.ClubIds.Select(id => $"`{id}`"))}");
            report.AppendLine();
        }
        File.WriteAllText(Path.Combine(output, "top_five_identity_reconciliation.md"), report.ToString());
    }

    private static IEnumerable<SourceFileSpec> DiscoverTopFiveSources(string englandRoot)
    {
        string repositoriesRoot = Directory.GetParent(Path.GetFullPath(englandRoot))!.FullName;
        foreach (string seasonDirectory in Directory.EnumerateDirectories(englandRoot).Where(path => SeasonPattern.IsMatch(Path.GetFileName(path))))
        {
            string season = Path.GetFileName(seasonDirectory);
            foreach (string file in Directory.EnumerateFiles(seasonDirectory, "*.txt"))
            {
                Match match = FilePattern.Match(Path.GetFileName(file));
                if (!match.Success) continue;
                int level = int.Parse(match.Groups["level"].Value);
                yield return new SourceFileSpec("eng", season, level, $"eng-{level}", CompetitionNames[level], file, englandRoot);
            }
        }

        foreach (SourceFileSpec spec in DiscoverNumberedRepository(Path.Combine(repositoriesRoot, "deutschland"), "de", new Dictionary<string, string>
        {
            ["bundesliga"] = "Bundesliga", ["bundesliga2"] = "2. Bundesliga", ["liga3"] = "3. Liga",
            ["regionalliga-bayern"] = "Regionalliga Bayern", ["regionalliga-nord"] = "Regionalliga Nord",
            ["regionalliga-nordost"] = "Regionalliga Nordost", ["regionalliga-suedwest"] = "Regionalliga Südwest",
            ["regionalliga-west"] = "Regionalliga West"
        })) yield return spec;
        foreach (SourceFileSpec spec in DiscoverNumberedRepository(Path.Combine(repositoriesRoot, "espana"), "es", new Dictionary<string, string>
        { ["liga"] = "La Liga", ["liga2"] = "Segunda División" })) yield return spec;
        foreach (SourceFileSpec spec in DiscoverNumberedRepository(Path.Combine(repositoriesRoot, "italy"), "it", new Dictionary<string, string>
        {
            ["seriea"] = "Serie A", ["serieb"] = "Serie B", ["seriec_a"] = "Serie C Group A",
            ["seriec_b"] = "Serie C Group B", ["seriec_c"] = "Serie C Group C"
        })) yield return spec;

        string franceRoot = Path.Combine(repositoriesRoot, "europe", "france");
        Regex francePattern = new(@"^(?<season>\d{4}-\d{2})_fr(?<level>[12])\.txt$", RegexOptions.Compiled);
        foreach (string file in Directory.EnumerateFiles(franceRoot, "*.txt"))
        {
            Match match = francePattern.Match(Path.GetFileName(file));
            if (!match.Success) continue;
            int level = int.Parse(match.Groups["level"].Value);
            yield return new SourceFileSpec("fr", match.Groups["season"].Value, level, $"fr-{level}", level == 1 ? "Ligue 1" : "Ligue 2", file, franceRoot);
        }
    }

    private static IEnumerable<SourceFileSpec> DiscoverNumberedRepository(string root, string countryCode, Dictionary<string, string> competitions)
    {
        Regex pattern = new(@"^(?<level>\d+)-(?<competition>[a-z0-9_-]+)\.txt$", RegexOptions.Compiled);
        foreach (string seasonDirectory in Directory.EnumerateDirectories(root).Where(path => SeasonPattern.IsMatch(Path.GetFileName(path))))
        {
            string season = Path.GetFileName(seasonDirectory);
            foreach (string file in Directory.EnumerateFiles(seasonDirectory, "*.txt"))
            {
                Match match = pattern.Match(Path.GetFileName(file));
                if (!match.Success || !competitions.TryGetValue(match.Groups["competition"].Value, out string? name)) continue;
                int level = int.Parse(match.Groups["level"].Value);
                string competitionId = $"{countryCode}-{match.Groups["competition"].Value}";
                yield return new SourceFileSpec(countryCode, season, level, competitionId, name, file, root);
            }
        }
    }

    private static FileAuditResult AuditFile(
        SourceFileSpec spec,
        ClubIdentityMap identities,
        Dictionary<string, HashSet<string>> aliases,
        Dictionary<string, CanonicalClub> clubs)
    {
        string path = spec.Path;
        int? declaredTeams = null;
        int? declaredMatches = null;
        int matchday = 0;
        int unparsedScoreLines = 0;
        int postseasonMatches = 0;
        bool postseason = false;
        List<ParsedMatch> parsed = new();

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            Match header = TeamsPattern.Match(line);
            if (header.Success) { declaredTeams = int.Parse(header.Groups[1].Value); continue; }
            header = MatchesPattern.Match(line);
            if (header.Success) { declaredMatches = int.Parse(header.Groups[1].Value); continue; }
            header = MatchdayPattern.Match(line);
            if (header.Success) { matchday = int.Parse(header.Groups[1].Value); continue; }
            header = NumberedRoundPattern.Match(line);
            if (header.Success) { matchday = int.Parse(header.Groups[1].Value); continue; }
            if (line.StartsWith('▪')) { postseason = true; matchday = 0; continue; }
            if (line.StartsWith('#') || line.StartsWith('=') || line.StartsWith('-') || line.StartsWith('_')) continue;

            ParsedMatch? match = ParseMatchLine(line, matchday);
            if (match is not null)
            {
                if (postseason) { postseasonMatches++; continue; }
                parsed.Add(match);
                foreach (string rawName in new[] { match.Home, match.Away })
                {
                    CanonicalClub club = identities.Resolve(rawName);
                    clubs.TryAdd(club.Id, club);
                    if (!aliases.TryGetValue(club.Id, out HashSet<string>? values)) aliases[club.Id] = values = new(StringComparer.Ordinal);
                    values.Add(rawName.Trim());
                }
            }
            else if (Regex.IsMatch(line, @"\d+-\d+")) unparsedScoreLines++;
        }

        int uniqueTeams = parsed.SelectMany(match => new[] { match.Home, match.Away }).Select(name => identities.Resolve(name).Id).Distinct().Count();
        List<string> errors = new();
        int? expectedRegularMatches = declaredTeams.HasValue ? declaredTeams.Value * (declaredTeams.Value - 1) : null;
        if (declaredMatches is null) errors.Add("missing declared match count");
        if (expectedRegularMatches.HasValue && parsed.Count != expectedRegularMatches.Value)
            errors.Add($"parsed {parsed.Count} of {expectedRegularMatches} expected regular-season matches");
        if (declaredTeams is null) errors.Add("missing declared team count");
        else if (uniqueTeams != declaredTeams) errors.Add($"resolved {uniqueTeams} of {declaredTeams} declared teams");
        if (declaredTeams.HasValue)
        {
            int expectedAppearances = (declaredTeams.Value - 1) * 2;
            string[] unevenClubs = parsed
                .SelectMany(match => new[] { match.Home, match.Away })
                .Select(name => identities.Resolve(name).Id)
                .GroupBy(id => id)
                .Where(group => group.Count() != expectedAppearances)
                .Select(group => $"{group.Key}:{group.Count()}")
                .Order()
                .ToArray();
            if (unevenClubs.Length > 0)
                errors.Add($"clubs with non-round-robin appearance counts (expected {expectedAppearances}): {string.Join(", ", unevenClubs)}");
        }
        // Score-like annotations (aggregate scores, penalty notes and tables) are
        // reported for inspection but do not invalidate a proven round robin.

        FileAudit audit = new(
            spec.CountryCode, spec.Season, spec.Level, spec.CompetitionId, spec.Competition,
            Path.GetRelativePath(spec.SourceRoot, path).Replace('\\', '/'), declaredTeams, declaredMatches,
            expectedRegularMatches, parsed.Count, postseasonMatches, uniqueTeams, unparsedScoreLines, errors.Count == 0, errors);
        return new FileAuditResult(audit, parsed);
    }

    private static ClubWorldGenerationProfile[] WriteWorldHistory(
        string output,
        string commit,
        Dictionary<string, string> sourceCommits,
        Dictionary<string, ClubHonoursEvidence> honoursEvidence,
        EuropeanClubSeason[] europeanSeasons,
        List<FileAuditResult> results,
        Dictionary<string, ClubIdentityMap> identityMaps,
        Dictionary<string, CanonicalClub> clubs,
        JsonSerializerOptions jsonOptions)
    {
        List<ClubSeasonSummary> clubSeasons = new();
        foreach (FileAuditResult result in results.Where(result => result.Audit.Complete))
        {
            ClubIdentityMap identities = identityMaps[result.Audit.CountryCode];
            Dictionary<string, ClubSeasonSummary> table = new(StringComparer.Ordinal);
            foreach (ParsedMatch match in result.Matches)
            {
                CanonicalClub homeClub = identities.Resolve(match.Home);
                CanonicalClub awayClub = identities.Resolve(match.Away);
                ClubSeasonSummary home = GetOrCreateSummary(table, result.Audit, homeClub);
                ClubSeasonSummary away = GetOrCreateSummary(table, result.Audit, awayClub);

                home.Played++;
                away.Played++;
                home.GoalsFor += match.HomeGoals;
                home.GoalsAgainst += match.AwayGoals;
                away.GoalsFor += match.AwayGoals;
                away.GoalsAgainst += match.HomeGoals;
                if (match.HomeGoals > match.AwayGoals) { home.Won++; away.Lost++; }
                else if (match.HomeGoals < match.AwayGoals) { away.Won++; home.Lost++; }
                else { home.Drawn++; away.Drawn++; }
            }
            clubSeasons.AddRange(table.Values.OrderByDescending(team => team.Points).ThenByDescending(team => team.GoalDifference).ThenByDescending(team => team.GoalsFor));
        }

        CompetitionSeasonSummary[] competitionSeasons = results.Where(result => result.Audit.Complete).Select(result => new CompetitionSeasonSummary
        {
            CountryCode = result.Audit.CountryCode,
            Season = result.Audit.Season,
            Level = result.Audit.Level,
            CompetitionId = result.Audit.CompetitionId,
            Competition = result.Audit.Competition,
            ParsedMatches = result.Audit.ParsedMatches,
            Clubs = result.Matches.SelectMany(match => new[] { identityMaps[result.Audit.CountryCode].Resolve(match.Home).Id, identityMaps[result.Audit.CountryCode].Resolve(match.Away).Id }).Distinct().Order().ToArray()
        }).OrderBy(item => item.CountryCode).ThenBy(item => item.Season).ThenBy(item => item.Level).ThenBy(item => item.CompetitionId).ToArray();
        DivisionTransitionSummary[] divisionTransitions = BuildDivisionTransitions(clubSeasons);
        ClubGenerationPrior[] generationPriors = BuildGenerationPriors(clubSeasons, divisionTransitions, competitionSeasons);
        ClubWorldGenerationProfile[] worldGenerationProfiles = BuildWorldGenerationProfiles(clubSeasons, honoursEvidence, europeanSeasons);
        WriteWorldGenerationAudit(output, worldGenerationProfiles);

        var payload = new
        {
            schemaVersion = 2,
            sourceCommit = commit,
            sourceCommits = sourceCommits.OrderBy(pair => pair.Key).Select(pair => new { countryCode = pair.Key, commit = pair.Value }).ToArray(),
            generatedFromCompleteFiles = results.Count(result => result.Audit.Complete),
            excludedFiles = results.Where(result => !result.Audit.Complete).Select(result => result.Audit.SourceFile).ToArray(),
            clubs = clubs.Values.OrderBy(club => club.Id).Select(club => new { id = club.Id, name = club.Name }).ToArray(),
            competitionSeasons,
            divisionTransitions,
            generationPriors,
            europeanClubSeasons = europeanSeasons,
            worldGenerationProfiles,
            clubSeasons
        };
        File.WriteAllText(Path.Combine(output, "football_world_history.json"), JsonSerializer.Serialize(payload, jsonOptions) + Environment.NewLine);
        return worldGenerationProfiles;
    }

    private static ClubWorldGenerationProfile[] BuildWorldGenerationProfiles(
        List<ClubSeasonSummary> clubSeasons,
        Dictionary<string, ClubHonoursEvidence> honoursEvidence,
        EuropeanClubSeason[] europeanSeasons)
    {
        var leagueBaselines = clubSeasons
            .GroupBy(row => (row.CountryCode, row.Season, row.Level))
            .ToDictionary(group => group.Key, group => new
            {
                PointsPerGame = group.Sum(row => row.Points) / (double)group.Sum(row => row.Played),
                GoalsPerGame = group.Sum(row => row.GoalsFor) / (double)group.Sum(row => row.Played)
            });

        List<ClubWorldGenerationProfile> profiles = new();
        Dictionary<string, (double Score, double Boost)> europeanByClub = BuildEuropeanScores(europeanSeasons);
        foreach (IGrouping<string, ClubSeasonSummary> history in clubSeasons.GroupBy(row => row.ClubId))
        {
            ClubSeasonSummary[] ordered = history.OrderBy(row => SeasonStartYear(row.Season)).ToArray();
            ClubSeasonSummary latest = ordered[^1];
            ClubSeasonSummary[] recent = ordered.Reverse().Take(5).ToArray();
            double weightedPerformance = 0d;
            double totalWeight = 0d;
            for (int index = 0; index < recent.Length; index++)
            {
                ClubSeasonSummary row = recent[index];
                var baseline = leagueBaselines[(row.CountryCode, row.Season, row.Level)];
                double pointsIndex = (row.Points / (double)row.Played) / baseline.PointsPerGame;
                double goalDifferenceScale = Math.Max(0.8d, baseline.GoalsPerGame);
                double goalDifferenceIndex = (row.GoalDifference / (double)row.Played) / goalDifferenceScale;
                double seasonPerformance = (pointsIndex - 1d) * 0.72d + goalDifferenceIndex * 0.28d;
                double weight = Math.Pow(0.72d, index);
                weightedPerformance += seasonPerformance * weight;
                totalWeight += weight;
            }

            double performance = totalWeight > 0d ? weightedPerformance / totalWeight : 0d;
            double baseOverall = BaseSquadOverall(latest.CountryCode, latest.Level);
            double firstTeam = Math.Clamp(baseOverall + Math.Clamp(performance * 7.0d, -4.0d, 4.0d), 48d, 85d);
            double benchGap = firstTeam >= 78d ? 2.3d : firstTeam >= 68d ? 2.8d : 3.2d;
            double reserveGap = firstTeam >= 78d ? 5.2d : firstTeam >= 68d ? 5.8d : 6.5d;
            double longevity = Math.Min(8d, Math.Sqrt(ordered.Length) * 1.65d);
            int recentTopFlightSeasons = ordered.Reverse().Take(10).Count(row => row.Level == 1);
            double eliteBonus = Math.Max(0d, firstTeam - 81.5d) * 3d;
            double recentReputation = Math.Clamp(
                20d + (firstTeam - 50d) * 1.75d + longevity * 0.5d + Math.Min(5d, recentTopFlightSeasons * 0.5d) + performance * 7d + eliteBonus,
                10d, 94d);
            bool hasHonours = honoursEvidence.TryGetValue(latest.ClubId, out ClubHonoursEvidence? honours);
            bool hasEurope = europeanByClub.TryGetValue(latest.ClubId, out (double Score, double Boost) european);
            double europeanAttenuation = Math.Clamp((100d - recentReputation) / 20d, 0.15d, 1d);
            double reputation = Math.Min(97d, recentReputation + (hasEurope ? european.Boost * europeanAttenuation : 0d));
            if (hasHonours) reputation = Math.Max(reputation, honours!.ReputationFloor);
            double confidence = Math.Clamp(0.30d + ordered.Length * 0.065d, 0.30d, 0.95d);

            profiles.Add(new ClubWorldGenerationProfile
            {
                ClubId = latest.ClubId,
                ClubName = latest.ClubName,
                CountryCode = latest.CountryCode,
                ReferenceSeason = latest.Season,
                CompetitionId = latest.CompetitionId,
                Level = latest.Level,
                Reputation = Math.Round(reputation, 3),
                FirstTeamOverall = Math.Round(firstTeam, 3),
                BenchOverall = Math.Round(firstTeam - benchGap, 3),
                ReserveOverall = Math.Round(firstTeam - reserveGap, 3),
                Confidence = Math.Round(confidence, 3),
                EvidenceSeasons = ordered.Length,
                HonoursScore = hasHonours ? Math.Round(honours!.HonoursScore, 3) : 0d,
                RecentEuropeanScore = hasEurope ? Math.Round(european.Score, 3) : 0d,
                EuropeanReputationBoost = hasEurope ? Math.Round(european.Boost, 3) : 0d,
                ReputationSource = string.Join(" + ", new[]
                {
                    "recent domestic performance",
                    hasEurope ? "recent European performance" : null,
                    hasHonours ? "reviewed honours floor" : null
                }.Where(value => value != null))
            });
        }
        return profiles.OrderByDescending(profile => profile.Reputation).ThenBy(profile => profile.ClubId).ToArray();
    }

    private static Dictionary<string, ClubHonoursEvidence> LoadHonoursEvidence(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Dictionary<string, ClubHonoursEvidence> result = new(StringComparer.Ordinal);
        foreach (JsonElement club in document.RootElement.GetProperty("clubs").EnumerateArray())
        {
            double score =
                club.GetProperty("nationalLeague").GetInt32() * 2.0d +
                club.GetProperty("nationalCup").GetInt32() * 1.0d +
                club.GetProperty("nationalLeagueCup").GetInt32() * 0.5d +
                club.GetProperty("europeanCup").GetInt32() * 5.0d +
                club.GetProperty("uefaCup").GetInt32() * 2.5d;
            ClubHonoursEvidence evidence = new(
                club.GetProperty("clubId").GetString()!, score,
                club.GetProperty("reputationFloor").GetDouble(),
                club.GetProperty("sourceUrl").GetString()!);
            if (!result.TryAdd(evidence.ClubId, evidence)) throw new InvalidDataException($"Duplicate honours record: {evidence.ClubId}");
        }
        return result;
    }

    private static EuropeanClubSeason[] LoadEuropeanSeasons(string root, ClubRegistry registry)
    {
        Dictionary<string, string> countryCodes = new(StringComparer.Ordinal)
        {
            ["ENG"] = "eng", ["WAL"] = "wal", ["SCO"] = "sco", ["NIR"] = "nir", ["IRL"] = "ie",
            ["GER"] = "de", ["ESP"] = "es", ["ITA"] = "it", ["FRA"] = "fr", ["POR"] = "pt",
            ["NED"] = "nl", ["BEL"] = "be", ["AUT"] = "at", ["SUI"] = "ch", ["DEN"] = "dk",
            ["NOR"] = "no", ["SWE"] = "se", ["FIN"] = "fi", ["ISL"] = "is", ["POL"] = "pl",
            ["CZE"] = "cz", ["SVK"] = "sk", ["HUN"] = "hu", ["ROU"] = "ro", ["BUL"] = "bg",
            ["CRO"] = "hr", ["SRB"] = "rs", ["SVN"] = "si", ["BIH"] = "ba", ["MNE"] = "me",
            ["MKD"] = "mk", ["ALB"] = "al", ["GRE"] = "gr", ["TUR"] = "tr", ["CYP"] = "cy",
            ["ISR"] = "il", ["UKR"] = "ua", ["RUS"] = "ru", ["BLR"] = "by", ["MDA"] = "md",
            ["GEO"] = "ge", ["ARM"] = "am", ["AZE"] = "az", ["KAZ"] = "kz", ["LUX"] = "lu",
            ["LIE"] = "li", ["AND"] = "ad", ["SMR"] = "sm", ["MLT"] = "mt", ["KOS"] = "kos",
            ["FRO"] = "fo", ["GIB"] = "gi", ["EST"] = "ee", ["LVA"] = "lv", ["LTU"] = "lt"
        };
        Dictionary<string, EuropeanAccumulator> totals = new(StringComparer.Ordinal);
        HashSet<string> unresolved = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories).Order())
        {
            string season = Path.GetFileName(Path.GetDirectoryName(file))!;
            if (!SeasonPattern.IsMatch(season)) continue;
            string code = Path.GetFileNameWithoutExtension(file);
            bool qualifying = code.EndsWith('q');
            string competition = code.StartsWith("cl", StringComparison.Ordinal) ? "Champions League"
                : code.StartsWith("el", StringComparison.Ordinal) ? "Europa League"
                : code.StartsWith("conf", StringComparison.Ordinal) ? "Conference League"
                : string.Empty;
            if (competition.Length == 0) continue;
            int knockoutDepth = 0;

            foreach (string rawLine in File.ReadLines(file))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('▪'))
                {
                    knockoutDepth = EuropeanKnockoutDepth(line);
                    continue;
                }
                ParsedMatch? match = ParseMatchLine(line, 0);
                if (match == null) continue;
                RegistryClub? home = ResolveEuropeanClub(match.Home, registry, countryCodes);
                RegistryClub? away = ResolveEuropeanClub(match.Away, registry, countryCodes);
                if (home == null || away == null)
                {
                    if (home == null) unresolved.Add(match.Home);
                    if (away == null) unresolved.Add(match.Away);
                    continue;
                }
                AddEuropeanResult(home, season, competition, qualifying, match.HomeGoals, match.AwayGoals, knockoutDepth, totals);
                AddEuropeanResult(away, season, competition, qualifying, match.AwayGoals, match.HomeGoals, knockoutDepth, totals);
            }
        }

        if (unresolved.Count > 0)
        {
            Console.WriteLine($"UEFA identity audit: {unresolved.Count} unresolved names; unresolved matches were excluded.");
            Console.WriteLine($"UEFA unresolved: {string.Join(" | ", unresolved.Order())}");
        }
        return totals.Values.Select(item => new EuropeanClubSeason(
                item.ClubId, item.ClubName, item.Season, item.Competition, item.Qualifying,
                item.Played, item.Points, item.KnockoutDepth))
            .OrderBy(item => item.Season).ThenBy(item => item.Competition).ThenBy(item => item.ClubId).ToArray();
    }

    private static RegistryClub? ResolveEuropeanClub(string rawName, ClubRegistry registry, Dictionary<string, string> countryCodes)
    {
        string cleaned = Regex.Replace(rawName.Trim(), @"\s+", " ");
        Match country = UefaCountryPattern.Match(cleaned);
        if (country.Success)
        {
            cleaned = UefaCountryPattern.Replace(cleaned, "").Trim();
            cleaned = cleaned switch
            {
                "AEK Athen" => "AEK Athens",
                "APOEL Nikosia" => "APOEL Nicosia",
                "Arda Kardzhali" => "FC Arda Kardzhali",
                "Egnatia Rrogozhine" => "KF Egnatia Rrogozhinë",
                "FC DAC 1904" => "FC DAC 1904 Dunajská Streda",
                "FK Isloch Minsk" => "FC Isloch",
                "FK Oleksandriya" => "FC Oleksandriya",
                "FK Ordabasy" => "Ordabasy",
                "KF Ballkani" => "KF Ballkani Suhareka",
                "Olympiakos Piraeus" => "Olympiacos Piraeus",
                "Omonia Nikosia" => "Omonia Nicosia",
                "Sumgayıt FK" => "Sumgayit FK",
                "Torpedo-BelAZ Zhodino" => "FC Torpedo-Belaz Zhodino",
                "Zorya Lugansk" => "Zorya Luhansk",
                _ => cleaned
            };
            if (countryCodes.TryGetValue(country.Groups["country"].Value, out string? code) && registry.TryResolve(code, cleaned, out RegistryClub? tagged))
                return tagged;
        }
        return registry.TryResolveGlobally(cleaned, out RegistryClub? global) ? global : null;
    }

    private static void AddEuropeanResult(
        RegistryClub club, string season, string competition, bool qualifying,
        int goalsFor, int goalsAgainst, int knockoutDepth,
        Dictionary<string, EuropeanAccumulator> totals)
    {
        string key = $"{club.Id}|{season}|{competition}|{qualifying}";
        if (!totals.TryGetValue(key, out EuropeanAccumulator? total))
        {
            total = new EuropeanAccumulator
            {
                ClubId = club.Id, ClubName = club.Name, Season = season,
                Competition = competition, Qualifying = qualifying
            };
            totals[key] = total;
        }
        total.Played++;
        total.Points += goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0;
        total.KnockoutDepth = Math.Max(total.KnockoutDepth, knockoutDepth);
    }

    private static int EuropeanKnockoutDepth(string heading)
    {
        string value = heading.ToLowerInvariant();
        if (value.Contains("quarter")) return 3;
        if (value.Contains("semi")) return 4;
        if (Regex.IsMatch(value, @"\bfinal\b") && !value.Contains("finals,")) return 5;
        if (value.Contains("round of 16") || value.Contains("last 16")) return 2;
        if (value.Contains("knockout") || value.Contains("play-off") || value.Contains("playoff")) return 1;
        return 0;
    }

    private static Dictionary<string, (double Score, double Boost)> BuildEuropeanScores(EuropeanClubSeason[] seasons)
    {
        if (seasons.Length == 0) return new(StringComparer.Ordinal);
        int latestYear = seasons.Max(item => SeasonStartYear(item.Season));
        Dictionary<string, (double Score, double Boost)> result = new(StringComparer.Ordinal);
        foreach (IGrouping<string, EuropeanClubSeason> club in seasons.GroupBy(item => item.ClubId))
        {
            double weighted = 0d;
            double weights = 0d;
            foreach (EuropeanClubSeason item in club.Where(item => latestYear - SeasonStartYear(item.Season) <= 4))
            {
                double competitionWeight = item.Competition switch
                {
                    "Champions League" => 1d,
                    "Europa League" => 0.62d,
                    _ => 0.40d
                };
                if (item.Qualifying) competitionWeight *= 0.25d;
                double recency = Math.Pow(0.72d, latestYear - SeasonStartYear(item.Season));
                double resultRate = item.Played > 0 ? item.Points / (item.Played * 3d) : 0d;
                double participation = Math.Min(1d, item.Played / (item.Qualifying ? 6d : 8d));
                double signal = 2d + resultRate * 4d + participation * 2d + item.KnockoutDepth / 5d * 2d;
                weighted += signal * competitionWeight * recency;
                weights += recency;
            }
            if (weights <= 0d) continue;
            double score = weighted / weights;
            result[club.Key] = (score, Math.Clamp(score * 0.8d, 0d, 8d));
        }
        return result;
    }

    private static double BaseSquadOverall(string countryCode, int level) => (countryCode, level) switch
    {
        ("eng", 1) => 79.5d, ("eng", 2) => 71.5d, ("eng", 3) => 66.0d, ("eng", 4) => 62.0d, ("eng", 5) => 58.0d,
        ("de", 1) => 79.0d, ("de", 2) => 71.0d, ("de", 3) => 65.0d, ("de", 4) => 60.0d,
        ("es", 1) => 78.5d, ("es", 2) => 70.0d,
        ("it", 1) => 78.0d, ("it", 2) => 69.5d, ("it", 3) => 63.0d,
        ("fr", 1) => 77.0d, ("fr", 2) => 69.0d,
        _ => 55.0d
    };

    private static void WriteClubEloAudit(
        string output,
        string snapshotPath,
        ClubWorldGenerationProfile[] profiles,
        ClubRegistry registry)
    {
        if (!File.Exists(snapshotPath)) throw new FileNotFoundException("Club Elo snapshot does not exist.", snapshotPath);
        Dictionary<string, string> countryCodes = new(StringComparer.Ordinal)
        {
            ["ENG"] = "eng", ["GER"] = "de", ["ESP"] = "es", ["ITA"] = "it", ["FRA"] = "fr"
        };
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bayern"] = "Bayern München",
            ["Paris SG"] = "Paris Saint-Germain",
            ["Man City"] = "Manchester City",
            ["Man United"] = "Manchester United",
            ["Inter"] = "FC Internazionale Milano",
            ["Atletico"] = "Atlético Madrid",
            ["Forest"] = "Nottingham Forest",
            ["Bilbao"] = "Athletic Club",
            ["Monaco"] = "AS Monaco",
            ["Gladbach"] = "Borussia Mönchengladbach",
            ["Koeln"] = "1. FC Köln",
            ["Werder"] = "Werder Bremen"
        };
        Dictionary<string, ClubWorldGenerationProfile> currentTopFlight = profiles
            .Where(profile => profile.Level == 1)
            .GroupBy(profile => profile.CountryCode)
            .SelectMany(country =>
            {
                int latest = country.Max(profile => SeasonStartYear(profile.ReferenceSeason));
                return country.Where(profile => SeasonStartYear(profile.ReferenceSeason) == latest);
            })
            .ToDictionary(profile => profile.ClubId, StringComparer.Ordinal);
        List<ClubEloComparison> comparisons = new();
        List<string> unresolved = new();

        foreach (string line in File.ReadLines(snapshotPath).Skip(1))
        {
            string[] fields = line.Split(',');
            if (fields.Length < 5 || !countryCodes.TryGetValue(fields[2], out string? countryCode)) continue;
            if (!int.TryParse(fields[3], out int level) || level != 1) continue;
            string name = aliases.TryGetValue(fields[1], out string? replacement) ? replacement : fields[1];
            string identityCountryCode = fields[2] == "FRA" && fields[1] == "Monaco" ? "mc" : countryCode;
            if (!registry.TryResolve(identityCountryCode, name, out RegistryClub? club) || club == null ||
                !currentTopFlight.TryGetValue(club.Id, out ClubWorldGenerationProfile? profile))
            {
                unresolved.Add($"{fields[2]}:{fields[1]}");
                continue;
            }
            if (!double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double elo)) continue;
            comparisons.Add(new ClubEloComparison(club.Id, profile.ClubName, profile.CountryCode, profile.FirstTeamOverall, elo));
        }

        double pearson = Pearson(comparisons.Select(item => item.GeneratedOverall).ToArray(), comparisons.Select(item => item.Elo).ToArray());
        Dictionary<string, int> generatedRanks = comparisons.OrderByDescending(item => item.GeneratedOverall).Select((item, index) => (item.ClubId, Rank: index + 1)).ToDictionary(item => item.ClubId, item => item.Rank);
        Dictionary<string, int> eloRanks = comparisons.OrderByDescending(item => item.Elo).Select((item, index) => (item.ClubId, Rank: index + 1)).ToDictionary(item => item.ClubId, item => item.Rank);
        double spearman = Pearson(comparisons.Select(item => (double)generatedRanks[item.ClubId]).ToArray(), comparisons.Select(item => (double)eloRanks[item.ClubId]).ToArray());

        StringBuilder report = new();
        report.AppendLine("# Development-only Club Elo calibration audit").AppendLine();
        report.AppendLine("Club Elo is external calibration evidence and is not published to Unity runtime assets.").AppendLine();
        report.AppendLine($"- Snapshot: `{Path.GetFileName(snapshotPath)}`");
        report.AppendLine($"- Matched current top-flight clubs: {comparisons.Count}");
        report.AppendLine($"- Unresolved or out-of-season clubs: {unresolved.Distinct().Count()}");
        report.AppendLine($"- Pearson correlation (generated overall vs Elo): {pearson:F3}");
        report.AppendLine($"- Spearman rank correlation: {spearman:F3}").AppendLine();
        report.AppendLine("| Country | Clubs | Generated mean | Generated spread | Elo mean | Elo spread |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (IGrouping<string, ClubEloComparison> country in comparisons.GroupBy(item => item.CountryCode).OrderBy(group => group.Key))
        {
            ClubEloComparison[] rows = country.ToArray();
            report.AppendLine($"| {country.Key} | {rows.Length} | {rows.Average(item => item.GeneratedOverall):F2} | {rows.Max(item => item.GeneratedOverall) - rows.Min(item => item.GeneratedOverall):F2} | {rows.Average(item => item.Elo):F1} | {rows.Max(item => item.Elo) - rows.Min(item => item.Elo):F1} |");
        }
        report.AppendLine().AppendLine("## Largest rank disagreements").AppendLine();
        report.AppendLine("| Club | Generated overall | Elo | Generated rank | Elo rank | Difference |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (ClubEloComparison item in comparisons.OrderByDescending(item => Math.Abs(generatedRanks[item.ClubId] - eloRanks[item.ClubId])).Take(20))
        {
            int difference = generatedRanks[item.ClubId] - eloRanks[item.ClubId];
            report.AppendLine($"| {item.ClubName} | {item.GeneratedOverall:F2} | {item.Elo:F1} | {generatedRanks[item.ClubId]} | {eloRanks[item.ClubId]} | {difference:+#;-#;0} |");
        }
        if (unresolved.Count > 0)
            report.AppendLine().AppendLine($"Unresolved: {string.Join(", ", unresolved.Distinct().Order())}");
        File.WriteAllText(Path.Combine(output, "club_elo_audit.md"), report.ToString());
        Console.WriteLine($"Club Elo audit: {comparisons.Count} matches, Pearson {pearson:F3}, Spearman {spearman:F3}.");
    }

    private static double Pearson(double[] left, double[] right)
    {
        if (left.Length != right.Length || left.Length < 2) return 0d;
        double leftMean = left.Average();
        double rightMean = right.Average();
        double numerator = 0d;
        double leftSquares = 0d;
        double rightSquares = 0d;
        for (int index = 0; index < left.Length; index++)
        {
            double leftDelta = left[index] - leftMean;
            double rightDelta = right[index] - rightMean;
            numerator += leftDelta * rightDelta;
            leftSquares += leftDelta * leftDelta;
            rightSquares += rightDelta * rightDelta;
        }
        return leftSquares <= 0d || rightSquares <= 0d ? 0d : numerator / Math.Sqrt(leftSquares * rightSquares);
    }

    private static void WriteWorldGenerationAudit(string output, ClubWorldGenerationProfile[] profiles)
    {
        const int simulatedSeasons = 1000;
        Random random = new(221104);
        StringBuilder report = new();
        report.AppendLine("# World-generation target audit").AppendLine();
        report.AppendLine($"Deterministic target-only calibration: {simulatedSeasons:N0} double round-robin seasons per latest top division.");
        report.AppendLine("This does not claim to test the live match engine; it checks whether generated squad targets imply sane league-scale ranges.").AppendLine();
        report.AppendLine("| Country | Season | Clubs | Quality spread | Reputation spread | Goals/game | Champion points | Bottom points | Best GD | Worst GD | Status |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (IGrouping<string, ClubWorldGenerationProfile> country in profiles.Where(profile => profile.Level == 1).GroupBy(profile => profile.CountryCode).OrderBy(group => group.Key))
        {
            string latestSeason = country.Max(profile => profile.ReferenceSeason)!;
            ClubWorldGenerationProfile[] clubs = country.Where(profile => profile.ReferenceSeason == latestSeason).OrderBy(profile => profile.ClubId).ToArray();
            if (clubs.Length < 10) continue;
            List<double> goalsPerGame = new(), championPoints = new(), bottomPoints = new(), bestGoalDifference = new(), worstGoalDifference = new();
            for (int simulation = 0; simulation < simulatedSeasons; simulation++)
            {
                int[] points = new int[clubs.Length];
                int[] goalDifference = new int[clubs.Length];
                int totalGoals = 0, matches = 0;
                for (int home = 0; home < clubs.Length; home++)
                {
                    for (int away = 0; away < clubs.Length; away++)
                    {
                        if (home == away) continue;
                        double difference = clubs[home].FirstTeamOverall - clubs[away].FirstTeamOverall;
                        int homeGoals = SamplePoisson(random, 1.48d * Math.Exp(difference * 0.100d));
                        int awayGoals = SamplePoisson(random, 1.20d * Math.Exp(-difference * 0.100d));
                        totalGoals += homeGoals + awayGoals;
                        matches++;
                        goalDifference[home] += homeGoals - awayGoals;
                        goalDifference[away] += awayGoals - homeGoals;
                        if (homeGoals > awayGoals) points[home] += 3;
                        else if (awayGoals > homeGoals) points[away] += 3;
                        else { points[home]++; points[away]++; }
                    }
                }
                goalsPerGame.Add(totalGoals / (double)matches);
                championPoints.Add(points.Max());
                bottomPoints.Add(points.Min());
                bestGoalDifference.Add(goalDifference.Max());
                worstGoalDifference.Add(goalDifference.Min());
            }

            double qualitySpread = clubs.Max(club => club.FirstTeamOverall) - clubs.Min(club => club.FirstTeamOverall);
            double reputationSpread = clubs.Max(club => club.Reputation) - clubs.Min(club => club.Reputation);
            double meanGoals = goalsPerGame.Average();
            double meanBestGd = bestGoalDifference.Average();
            bool sane = qualitySpread <= 10d && meanGoals is >= 2.4d and <= 3.4d && meanBestGd <= 70d;
            report.AppendLine($"| {country.Key} | {latestSeason} | {clubs.Length} | {qualitySpread:F2} | {reputationSpread:F2} | {meanGoals:F2} | {championPoints.Average():F1} | {bottomPoints.Average():F1} | {meanBestGd:F1} | {worstGoalDifference.Average():F1} | {(sane ? "PASS" : "REVIEW")} |");
        }
        File.WriteAllText(Path.Combine(output, "world_generation_audit.md"), report.ToString());
    }

    private static int SamplePoisson(Random random, double lambda)
    {
        double limit = Math.Exp(-Math.Clamp(lambda, 0.05d, 8d));
        int count = 0;
        double product = 1d;
        do { count++; product *= random.NextDouble(); } while (product > limit);
        return count - 1;
    }

    private static DivisionTransitionSummary[] BuildDivisionTransitions(List<ClubSeasonSummary> clubSeasons)
    {
        var leagueBaselines = clubSeasons
            .GroupBy(row => (row.CountryCode, row.Season, row.Level))
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    GoalsForPerGame = group.Sum(row => row.GoalsFor) / (double)group.Sum(row => row.Played),
                    GoalsAgainstPerGame = group.Sum(row => row.GoalsAgainst) / (double)group.Sum(row => row.Played),
                    PointsPerGame = group.Sum(row => row.Points) / (double)group.Sum(row => row.Played)
                });
        Dictionary<(string club, int startYear), ClubSeasonSummary> byClubYear = clubSeasons.ToDictionary(
            row => (row.ClubId, SeasonStartYear(row.Season)), row => row);
        Dictionary<(string country, int from, int to), List<(double attack, double defence, double points)>> samples = new();

        foreach (ClubSeasonSummary source in clubSeasons)
        {
            int sourceYear = SeasonStartYear(source.Season);
            if (!byClubYear.TryGetValue((source.ClubId, sourceYear + 1), out ClubSeasonSummary? target)) continue;
            if (Math.Abs(source.Level - target.Level) != 1) continue;

            var sourceLeague = leagueBaselines[(source.CountryCode, source.Season, source.Level)];
            var targetLeague = leagueBaselines[(target.CountryCode, target.Season, target.Level)];
            double sourceAttack = (source.GoalsFor / (double)source.Played) / sourceLeague.GoalsForPerGame;
            double targetAttack = (target.GoalsFor / (double)target.Played) / targetLeague.GoalsForPerGame;
            double sourceDefence = sourceLeague.GoalsAgainstPerGame / (source.GoalsAgainst / (double)source.Played);
            double targetDefence = targetLeague.GoalsAgainstPerGame / (target.GoalsAgainst / (double)target.Played);
            double sourcePoints = (source.Points / (double)source.Played) / sourceLeague.PointsPerGame;
            double targetPoints = (target.Points / (double)target.Played) / targetLeague.PointsPerGame;
            var key = (source.CountryCode, source.Level, target.Level);
            if (!samples.TryGetValue(key, out List<(double attack, double defence, double points)>? values))
                samples[key] = values = new();
            values.Add((targetAttack / sourceAttack, targetDefence / sourceDefence, targetPoints / sourcePoints));
        }

        return samples.OrderBy(pair => pair.Key.country).ThenBy(pair => pair.Key.from).ThenBy(pair => pair.Key.to).Select(pair => new DivisionTransitionSummary
        {
            CountryCode = pair.Key.country,
            FromLevel = pair.Key.from,
            ToLevel = pair.Key.to,
            Samples = pair.Value.Count,
            MeanAttackIndexRatio = pair.Value.Average(value => value.attack),
            MedianAttackIndexRatio = Median(pair.Value.Select(value => value.attack)),
            MeanDefenceQualityRatio = pair.Value.Average(value => value.defence),
            MedianDefenceQualityRatio = Median(pair.Value.Select(value => value.defence)),
            MeanPointsPerGameIndexRatio = pair.Value.Average(value => value.points),
            MedianPointsPerGameIndexRatio = Median(pair.Value.Select(value => value.points))
        }).ToArray();
    }

    private static ClubGenerationPrior[] BuildGenerationPriors(
        List<ClubSeasonSummary> clubSeasons,
        DivisionTransitionSummary[] transitions,
        CompetitionSeasonSummary[] competitionSeasons)
    {
        var leagueBaselines = clubSeasons
            .GroupBy(row => (row.CountryCode, row.Season, row.Level))
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Attack = group.Sum(row => row.GoalsFor) / (double)group.Sum(row => row.Played),
                    Defence = group.Sum(row => row.GoalsAgainst) / (double)group.Sum(row => row.Played),
                    Points = group.Sum(row => row.Points) / (double)group.Sum(row => row.Played)
                });
        Dictionary<string, List<ClubSeasonSummary>> histories = clubSeasons
            .GroupBy(row => row.ClubId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => SeasonStartYear(row.Season)).ToList());
        Dictionary<(string country, int from, int to), DivisionTransitionSummary> transitionLookup = transitions
            .ToDictionary(item => (item.CountryCode, item.FromLevel, item.ToLevel), item => item);
        List<ClubGenerationPrior> priors = new();

        foreach (CompetitionSeasonSummary competition in competitionSeasons)
        {
            string targetSeason = competition.Season;
            int targetLevel = competition.Level;
            int targetYear = SeasonStartYear(targetSeason);
            foreach (string clubId in competition.Clubs)
            {
                List<ClubSeasonSummary> priorHistory = histories.TryGetValue(clubId, out List<ClubSeasonSummary>? fullHistory)
                    ? fullHistory.Where(row => SeasonStartYear(row.Season) < targetYear).ToList()
                    : new List<ClubSeasonSummary>();
                ClubSeasonSummary? latest = priorHistory.LastOrDefault();
                List<(double attack, double defence, double points, double weight)> evidence = new();
                string source = "division-average fallback";
                double confidence = 0.20d;

                foreach (ClubSeasonSummary row in priorHistory.Where(row => row.Level == targetLevel))
                {
                    int age = targetYear - SeasonStartYear(row.Season);
                    if (age < 1 || age > 5) continue;
                    var baseline = leagueBaselines[(row.CountryCode, row.Season, row.Level)];
                    double weight = Math.Pow(0.68d, age - 1);
                    evidence.Add((
                        (row.GoalsFor / (double)row.Played) / baseline.Attack,
                        baseline.Defence / (row.GoalsAgainst / (double)row.Played),
                        (row.Points / (double)row.Played) / baseline.Points,
                        weight));
                }

                if (latest != null && latest.Level != targetLevel && Math.Abs(latest.Level - targetLevel) == 1 &&
                    transitionLookup.TryGetValue((competition.CountryCode, latest.Level, targetLevel), out DivisionTransitionSummary? transition))
                {
                    var baseline = leagueBaselines[(latest.CountryCode, latest.Season, latest.Level)];
                    evidence.Add((
                        (latest.GoalsFor / (double)latest.Played) / baseline.Attack * transition.MedianAttackIndexRatio,
                        baseline.Defence / (latest.GoalsAgainst / (double)latest.Played) * transition.MedianDefenceQualityRatio,
                        (latest.Points / (double)latest.Played) / baseline.Points * transition.MedianPointsPerGameIndexRatio,
                        1.35d));
                    source = latest.Level > targetLevel ? "promoted-club transition" : "relegated-club transition";
                    confidence = Math.Min(0.85d, 0.55d + transition.Samples / 250d);
                }
                else if (evidence.Count > 0)
                {
                    source = "recent same-division history";
                    confidence = Math.Min(0.95d, 0.55d + evidence.Sum(item => item.weight) * 0.12d);
                }

                double totalWeight = evidence.Sum(item => item.weight);
                double attack = totalWeight > 0d ? evidence.Sum(item => item.attack * item.weight) / totalWeight : 1d;
                double defence = totalWeight > 0d ? evidence.Sum(item => item.defence * item.weight) / totalWeight : 1d;
                double points = totalWeight > 0d ? evidence.Sum(item => item.points * item.weight) / totalWeight : 1d;
                ClubSeasonSummary? nameSource = latest ?? fullHistory?.LastOrDefault();
                priors.Add(new ClubGenerationPrior
                {
                    CountryCode = competition.CountryCode,
                    TargetSeason = targetSeason,
                    TargetLevel = targetLevel,
                    CompetitionId = competition.CompetitionId,
                    ClubId = clubId,
                    ClubName = nameSource?.ClubName ?? clubId,
                    AttackIndex = Math.Clamp(attack, 0.55d, 1.45d),
                    DefenceQualityIndex = Math.Clamp(defence, 0.55d, 1.45d),
                    PointsPerGameIndex = Math.Clamp(points, 0.55d, 1.45d),
                    Confidence = confidence,
                    Source = source
                });
            }
        }

        return priors.OrderBy(item => item.CountryCode).ThenBy(item => item.TargetSeason).ThenBy(item => item.TargetLevel).ThenBy(item => item.CompetitionId).ThenBy(item => item.ClubId).ToArray();
    }

    private static int SeasonStartYear(string season) => int.Parse(season.AsSpan(0, 4));

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        if (values.Length == 0) return 0d;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }

    private static ClubSeasonSummary GetOrCreateSummary(Dictionary<string, ClubSeasonSummary> table, FileAudit audit, CanonicalClub club)
    {
        if (table.TryGetValue(club.Id, out ClubSeasonSummary? existing)) return existing;
        ClubSeasonSummary created = new()
        {
            CountryCode = audit.CountryCode,
            Season = audit.Season,
            Level = audit.Level,
            CompetitionId = audit.CompetitionId,
            Competition = audit.Competition,
            ClubId = club.Id,
            ClubName = club.Name
        };
        table[club.Id] = created;
        return created;
    }

    private static ParsedMatch? ParseMatchLine(string line, int matchday)
    {
        string candidate = AnnotationPattern.Replace(KickoffPattern.Replace(line.Trim(), ""), "");
        candidate = Regex.Replace(candidate, @"\s+(?:a\.e\.t\.|pen\.).*$", "", RegexOptions.IgnoreCase);
        Match match = NewMatchPattern.Match(candidate);
        if (match.Success) return new(match.Groups[1].Value, match.Groups[2].Value, int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value), matchday);
        match = OldMatchPattern.Match(candidate);
        return match.Success
            ? new(match.Groups[1].Value, match.Groups[4].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value), matchday)
            : null;
    }

    private static string RenderMarkdown(List<FileAudit> audits, string source, string commit)
    {
        StringBuilder text = new();
        text.AppendLine("# OpenFootball top-five archive audit").AppendLine();
        text.AppendLine($"- Source: `{source}`");
        text.AppendLine($"- Commit: `{commit}`");
        text.AppendLine($"- Competition files: {audits.Count}");
        text.AppendLine($"- Complete files: {audits.Count(item => item.Complete)}");
        text.AppendLine($"- Files requiring review: {audits.Count(item => !item.Complete)}");
        text.AppendLine($"- Parsed matches: {audits.Sum(item => item.ParsedMatches):N0}").AppendLine();
        text.AppendLine("| Country | Season | Level | Competition | Matches | Teams | Status |");
        text.AppendLine("|---|---|---:|---|---:|---:|---|");
        foreach (FileAudit item in audits)
        {
            string status = item.Complete ? "OK" : string.Join("; ", item.Errors);
            text.AppendLine($"| {item.CountryCode} | {item.Season} | {item.Level} | {item.Competition} | {item.ParsedMatches}/{item.ExpectedRegularMatches?.ToString() ?? "?"} | {item.UniqueTeams}/{item.DeclaredTeams?.ToString() ?? "?"} | {status} |");
        }
        return text.ToString();
    }

    private static string ReadGitCommit(string source)
    {
        string? repository = Path.GetFullPath(source);
        while (repository != null && !Directory.Exists(Path.Combine(repository, ".git")))
            repository = Directory.GetParent(repository)?.FullName;
        if (repository == null) return "unknown";
        string git = Path.Combine(repository, ".git");
        string headPath = Path.Combine(git, "HEAD");
        if (!File.Exists(headPath)) return "unknown";
        string value = File.ReadAllText(headPath).Trim();
        if (!value.StartsWith("ref: ")) return value;
        string reference = Path.Combine(git, value[5..].Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(reference) ? File.ReadAllText(reference).Trim() : "unknown";
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
