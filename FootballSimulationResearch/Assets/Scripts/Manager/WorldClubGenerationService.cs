using System;
using Data;
using Sim;

namespace Manager
{
    // Controlled bridge between immutable imported world data and generated player
    // agents. Existing saves and the legacy Premier League bootstrap do not call this
    // yet; future new-save creation can migrate one boundary at a time.
    public sealed class WorldClubGenerationService
    {
        private readonly FootballClubRegistry registry;
        private readonly FootballWorldHistory history;

        public WorldClubGenerationService(FootballClubRegistry registry, FootballWorldHistory history)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public AgentTeam GenerateSquad(string clubId, AgentSquadGenerator generator)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            if (!registry.TryGetClub(clubId, out WorldClubRecord club))
                throw new InvalidOperationException($"Cannot generate unknown world club: {clubId}");

            SquadQualityTarget target = GetSquadQualityTarget(clubId, club.CountryCode);
            return generator.GenerateSquad(club.Name, target);
        }

        public SquadQualityTarget GetSquadQualityTarget(string clubId, string countryCode = null)
        {
            if (history.TryGetWorldGenerationProfile(clubId, out ClubWorldGenerationProfileRecord profile))
            {
                return new SquadQualityTarget(
                    (float)profile.FirstTeamOverall,
                    (float)profile.BenchOverall,
                    (float)profile.ReserveOverall);
            }

            // Identity-only background clubs remain instantiable. Their conservative
            // fallback is intentionally low-confidence and must not imply a researched
            // domestic-league rating.
            float firstTeam = BackgroundFallbackOverall(countryCode);
            return new SquadQualityTarget(firstTeam, firstTeam - 3.2f, firstTeam - 6.5f);
        }

        public float GetReputation(string clubId)
        {
            return history.TryGetWorldGenerationProfile(clubId, out ClubWorldGenerationProfileRecord profile)
                ? (float)profile.Reputation
                : 25f;
        }

        public bool TryResolveClubId(string countryCode, string displayName, out string clubId)
        {
            if (registry.TryResolveAlias(countryCode, displayName, out WorldClubRecord club))
            {
                clubId = club.Id;
                return true;
            }
            clubId = null;
            return false;
        }

        private static float BackgroundFallbackOverall(string countryCode)
        {
            switch (countryCode)
            {
                case "eng": case "de": case "es": case "it": case "fr": return 58f;
                default: return 54f;
            }
        }
    }
}
