using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

record ParsedMatch(string Home, string Away, int HomeGoals, int AwayGoals, int Matchday);

record FileAudit(
    string Season,
    int Level,
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

sealed class ClubSeasonSummary
{
    public required string Season { get; init; }
    public required int Level { get; init; }
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
    public required string TargetSeason { get; init; }
    public required int TargetLevel { get; init; }
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
    public required string Season { get; init; }
    public required int Level { get; init; }
    public required string Competition { get; init; }
    public required int ParsedMatches { get; init; }
    public required string[] Clubs { get; init; }
}

sealed class ClubIdentityMap
{
    private readonly Dictionary<string, CanonicalClub> byAlias = new(StringComparer.Ordinal);
    private readonly ClubRegistry registry;
    private readonly string[] countryCodes;
    private readonly HashSet<string> unresolved = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UnresolvedNames => unresolved;

    public ClubIdentityMap(string path, ClubRegistry registry, params string[] countryCodes)
    {
        this.registry = registry;
        this.countryCodes = countryCodes;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement club in document.RootElement.GetProperty("clubs").EnumerateArray())
        {
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
        return new CanonicalClub($"{countryCodes[0]}:unresolved:{Slugify(cleaned)}", cleaned);
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
        value = Regex.Replace(value, @"\b(?:football club|f\.c\.|fc)\b", " ");
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
        string? publishHistory = null;
        string? publishClubs = null;

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--source" && index + 1 < args.Length) source = Path.GetFullPath(args[++index]);
            else if (args[index] == "--output" && index + 1 < args.Length) output = Path.GetFullPath(args[++index]);
            else if (args[index] == "--clubs-source" && index + 1 < args.Length) clubsSource = Path.GetFullPath(args[++index]);
            else if (args[index] == "--publish-history" && index + 1 < args.Length) publishHistory = Path.GetFullPath(args[++index]);
            else if (args[index] == "--publish-clubs" && index + 1 < args.Length) publishClubs = Path.GetFullPath(args[++index]);
            else return Fail($"Unknown or incomplete argument: {args[index]}");
        }

        if (!Directory.Exists(source)) return Fail($"OpenFootball source directory does not exist: {source}");
        if (!Directory.Exists(clubsSource)) return Fail($"OpenFootball clubs directory does not exist: {clubsSource}");

        ClubRegistry registry = ClubRegistry.Load(clubsSource);
        // Welsh clubs participate in the English pyramid but retain Welsh identities.
        ClubIdentityMap identities = new(Path.Combine(repositoryRoot, "Tools", "OpenFootballImport", "club_aliases.json"), registry, "eng", "wal");
        Dictionary<string, HashSet<string>> aliases = new(StringComparer.Ordinal);
        List<FileAudit> audits = new();
        List<FileAuditResult> results = new();
        Dictionary<string, CanonicalClub> clubs = new(StringComparer.Ordinal);

        foreach (string seasonDirectory in Directory.EnumerateDirectories(source).Where(path => SeasonPattern.IsMatch(Path.GetFileName(path))).Order())
        {
            foreach (string file in Directory.EnumerateFiles(seasonDirectory, "*.txt").Order())
            {
                if (FilePattern.IsMatch(Path.GetFileName(file)))
                {
                    FileAuditResult result = AuditFile(file, source, identities, aliases, clubs);
                    results.Add(result);
                    audits.Add(result.Audit);
                }
            }
        }

        Directory.CreateDirectory(output);
        string commit = ReadGitCommit(source);
        JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(Path.Combine(output, "archive_audit.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source,
            sourceCommit = commit,
            files = audits
        }, jsonOptions) + Environment.NewLine);
        File.WriteAllText(Path.Combine(output, "archive_audit.md"), RenderMarkdown(audits, source, commit));
        File.WriteAllText(Path.Combine(output, "club_alias_candidates.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            clubs = aliases.OrderBy(pair => pair.Key).Select(pair => new { id = pair.Key, aliases = pair.Value.Order().ToArray() })
        }, jsonOptions) + Environment.NewLine);
        WriteRegistryOutputs(output, clubsSource, registry, identities, aliases, jsonOptions);
        WriteWorldHistory(output, commit, results, identities, clubs, jsonOptions);
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
        ClubIdentityMap identities,
        Dictionary<string, HashSet<string>> observedAliases,
        JsonSerializerOptions jsonOptions)
    {
        var registryPayload = new
        {
            schemaVersion = 1,
            source = clubsSource,
            sourceCommit = registry.SourceCommit,
            clubs = registry.Clubs.OrderBy(club => club.Id).Select(club => new
            {
                id = club.Id,
                countryCode = club.CountryCode,
                countryPath = club.CountryPath,
                name = club.Name,
                foundedYear = club.FoundedYear,
                stadium = club.Stadium,
                locality = club.Locality,
                aliases = club.Aliases.Order().ToArray(),
                sourceFile = club.SourceFile
            }),
            collisions = registry.Collisions
        };
        File.WriteAllText(Path.Combine(output, "global_club_registry.json"), JsonSerializer.Serialize(registryPayload, jsonOptions) + Environment.NewLine);

        var reconciliation = new
        {
            schemaVersion = 1,
            countryCode = "eng",
            registryClubs = registry.Clubs.Count(club => club.CountryCode == "eng"),
            observedResolvedClubs = observedAliases.Keys.Count(id => !id.Contains(":unresolved:", StringComparison.Ordinal)),
            unresolvedNames = identities.UnresolvedNames.Order().ToArray(),
            registryAliasCollisions = registry.Collisions.Where(item => item.CountryCode == "eng").ToArray()
        };
        File.WriteAllText(Path.Combine(output, "england_identity_reconciliation.json"), JsonSerializer.Serialize(reconciliation, jsonOptions) + Environment.NewLine);
        StringBuilder report = new();
        report.AppendLine("# England club identity reconciliation").AppendLine();
        report.AppendLine($"- Global canonical clubs: {registry.Clubs.Count:N0}");
        report.AppendLine($"- English registry clubs: {reconciliation.registryClubs:N0}");
        report.AppendLine($"- Clubs observed in the audited English pyramid: {reconciliation.observedResolvedClubs:N0}");
        report.AppendLine($"- Unresolved observed names: {reconciliation.unresolvedNames.Length:N0}");
        report.AppendLine($"- Ambiguous English registry aliases: {reconciliation.registryAliasCollisions.Length:N0}").AppendLine();
        report.AppendLine("Welsh clubs participating in the English pyramid retain `wal:` identities.").AppendLine();
        report.AppendLine("## Unresolved observed names").AppendLine();
        if (reconciliation.unresolvedNames.Length == 0) report.AppendLine("None.");
        else foreach (string name in reconciliation.unresolvedNames) report.AppendLine($"- {name}");
        report.AppendLine().AppendLine("## Ambiguous registry aliases").AppendLine();
        foreach (RegistryCollision collision in reconciliation.registryAliasCollisions)
            report.AppendLine($"- `{collision.Alias}`: {string.Join(", ", collision.ClubIds.Select(id => $"`{id}`"))}");
        File.WriteAllText(Path.Combine(output, "england_identity_reconciliation.md"), report.ToString());
    }

    private static FileAuditResult AuditFile(
        string path,
        string sourceRoot,
        ClubIdentityMap identities,
        Dictionary<string, HashSet<string>> aliases,
        Dictionary<string, CanonicalClub> clubs)
    {
        Match filename = FilePattern.Match(Path.GetFileName(path));
        int level = int.Parse(filename.Groups["level"].Value);
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
            if (header.Success) { matchday = int.Parse(header.Groups[1].Value); postseason = false; continue; }
            header = NumberedRoundPattern.Match(line);
            if (header.Success) { matchday = int.Parse(header.Groups[1].Value); postseason = false; continue; }
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
        if (unparsedScoreLines > 0) errors.Add($"{unparsedScoreLines} score-like lines did not parse");

        FileAudit audit = new(
            Path.GetFileName(Path.GetDirectoryName(path))!, level, CompetitionNames[level],
            Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'), declaredTeams, declaredMatches,
            expectedRegularMatches, parsed.Count, postseasonMatches, uniqueTeams, unparsedScoreLines, errors.Count == 0, errors);
        return new FileAuditResult(audit, parsed);
    }

    private static void WriteWorldHistory(
        string output,
        string commit,
        List<FileAuditResult> results,
        ClubIdentityMap identities,
        Dictionary<string, CanonicalClub> clubs,
        JsonSerializerOptions jsonOptions)
    {
        List<ClubSeasonSummary> clubSeasons = new();
        foreach (FileAuditResult result in results.Where(result => result.Audit.Complete))
        {
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
            Season = result.Audit.Season,
            Level = result.Audit.Level,
            Competition = result.Audit.Competition,
            ParsedMatches = result.Audit.ParsedMatches,
            Clubs = result.Matches.SelectMany(match => new[] { identities.Resolve(match.Home).Id, identities.Resolve(match.Away).Id }).Distinct().Order().ToArray()
        }).OrderBy(item => item.Season).ThenBy(item => item.Level).ToArray();
        DivisionTransitionSummary[] divisionTransitions = BuildDivisionTransitions(clubSeasons);
        ClubGenerationPrior[] generationPriors = BuildGenerationPriors(clubSeasons, divisionTransitions, competitionSeasons);

        var payload = new
        {
            schemaVersion = 1,
            sourceCommit = commit,
            generatedFromCompleteFiles = results.Count(result => result.Audit.Complete),
            excludedFiles = results.Where(result => !result.Audit.Complete).Select(result => result.Audit.SourceFile).ToArray(),
            clubs = clubs.Values.OrderBy(club => club.Id).Select(club => new { id = club.Id, name = club.Name }).ToArray(),
            competitionSeasons,
            divisionTransitions,
            generationPriors,
            clubSeasons
        };
        File.WriteAllText(Path.Combine(output, "football_world_history.json"), JsonSerializer.Serialize(payload, jsonOptions) + Environment.NewLine);
    }

    private static DivisionTransitionSummary[] BuildDivisionTransitions(List<ClubSeasonSummary> clubSeasons)
    {
        var leagueBaselines = clubSeasons
            .GroupBy(row => (row.Season, row.Level))
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
        Dictionary<(int from, int to), List<(double attack, double defence, double points)>> samples = new();

        foreach (ClubSeasonSummary source in clubSeasons)
        {
            int sourceYear = SeasonStartYear(source.Season);
            if (!byClubYear.TryGetValue((source.ClubId, sourceYear + 1), out ClubSeasonSummary? target)) continue;
            if (Math.Abs(source.Level - target.Level) != 1) continue;

            var sourceLeague = leagueBaselines[(source.Season, source.Level)];
            var targetLeague = leagueBaselines[(target.Season, target.Level)];
            double sourceAttack = (source.GoalsFor / (double)source.Played) / sourceLeague.GoalsForPerGame;
            double targetAttack = (target.GoalsFor / (double)target.Played) / targetLeague.GoalsForPerGame;
            double sourceDefence = sourceLeague.GoalsAgainstPerGame / (source.GoalsAgainst / (double)source.Played);
            double targetDefence = targetLeague.GoalsAgainstPerGame / (target.GoalsAgainst / (double)target.Played);
            double sourcePoints = (source.Points / (double)source.Played) / sourceLeague.PointsPerGame;
            double targetPoints = (target.Points / (double)target.Played) / targetLeague.PointsPerGame;
            var key = (source.Level, target.Level);
            if (!samples.TryGetValue(key, out List<(double attack, double defence, double points)>? values))
                samples[key] = values = new();
            values.Add((targetAttack / sourceAttack, targetDefence / sourceDefence, targetPoints / sourcePoints));
        }

        return samples.OrderBy(pair => pair.Key.from).ThenBy(pair => pair.Key.to).Select(pair => new DivisionTransitionSummary
        {
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
            .GroupBy(row => (row.Season, row.Level))
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
        Dictionary<(int from, int to), DivisionTransitionSummary> transitionLookup = transitions
            .ToDictionary(item => (item.FromLevel, item.ToLevel), item => item);
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
                    var baseline = leagueBaselines[(row.Season, row.Level)];
                    double weight = Math.Pow(0.68d, age - 1);
                    evidence.Add((
                        (row.GoalsFor / (double)row.Played) / baseline.Attack,
                        baseline.Defence / (row.GoalsAgainst / (double)row.Played),
                        (row.Points / (double)row.Played) / baseline.Points,
                        weight));
                }

                if (latest != null && latest.Level != targetLevel && Math.Abs(latest.Level - targetLevel) == 1 &&
                    transitionLookup.TryGetValue((latest.Level, targetLevel), out DivisionTransitionSummary? transition))
                {
                    var baseline = leagueBaselines[(latest.Season, latest.Level)];
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
                    TargetSeason = targetSeason,
                    TargetLevel = targetLevel,
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

        return priors.OrderBy(item => item.TargetSeason).ThenBy(item => item.TargetLevel).ThenBy(item => item.ClubId).ToArray();
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
            Season = audit.Season,
            Level = audit.Level,
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
        text.AppendLine("# OpenFootball England archive audit").AppendLine();
        text.AppendLine($"- Source: `{source}`");
        text.AppendLine($"- Commit: `{commit}`");
        text.AppendLine($"- Competition files: {audits.Count}");
        text.AppendLine($"- Complete files: {audits.Count(item => item.Complete)}");
        text.AppendLine($"- Files requiring review: {audits.Count(item => !item.Complete)}");
        text.AppendLine($"- Parsed matches: {audits.Sum(item => item.ParsedMatches):N0}").AppendLine();
        text.AppendLine("| Season | Level | Competition | Matches | Teams | Status |");
        text.AppendLine("|---|---:|---|---:|---:|---|");
        foreach (FileAudit item in audits)
        {
            string status = item.Complete ? "OK" : string.Join("; ", item.Errors);
            text.AppendLine($"| {item.Season} | {item.Level} | {item.Competition} | {item.ParsedMatches}/{item.ExpectedRegularMatches?.ToString() ?? "?"} | {item.UniqueTeams}/{item.DeclaredTeams?.ToString() ?? "?"} | {status} |");
        }
        return text.ToString();
    }

    private static string ReadGitCommit(string source)
    {
        string git = Path.Combine(source, ".git");
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
