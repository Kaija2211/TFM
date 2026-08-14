# OpenFootball history and club-identity importer

This offline tool audits the upstream `openfootball/england` archive before any
historical data is allowed to influence TFM's world generation.

The raw archive deliberately lives outside Unity's `Assets` directory. By default
the script expects the sibling clone created by GitHub Desktop:

```text
../england
../clubs
```

Run from the FootballResearchProject repository root:

```powershell
dotnet run --project Tools/OpenFootballImport
```

Optional arguments:

```powershell
dotnet run --project Tools/OpenFootballImport -- `
  --source C:\path\to\openfootball\england `
  --clubs-source C:\path\to\openfootball\clubs `
  --output Temp\OpenFootballAudit `
  --publish-history FootballSimulationResearch\Assets\Data\Generated\football_world_history.json.txt `
  --publish-clubs FootballSimulationResearch\Assets\Data\Generated\football_club_registry.json.txt
```

Publishing is explicit. The `.json.txt` suffix lets Unity import the generated JSON
as a `TextAsset` and avoids the repository's broad historical `*.json` ignore rule.

The first phase writes audit artifacts only. It does not modify Unity data or the
legacy team-strength model:

- `archive_audit.json` — machine-readable seasons, competitions and validation;
- `archive_audit.md` — human-readable coverage and failures;
- `club_alias_candidates.json` — every raw club spelling grouped by normalized key.
- `global_club_registry.json` — canonical worldwide club identities and aliases from
  the `openfootball/clubs` repository. Stadium and locality fields are advisory.
- `england_identity_reconciliation.json` — unresolved English match-history names
  and ambiguous registry aliases that require human review.
- `england_identity_reconciliation.md` — human-readable version of that report.
- `football_world_history.json` — stable clubs, validated competition-season
  membership, and reconstructed club-season tables. Only files passing every audit
  check are included.

`club_aliases.json` is the checked-in authoritative identity map. Automatic
normalization is intentionally conservative. Ambiguous renames, phoenix clubs and
clubs with similar names must be reviewed rather than silently merged.
