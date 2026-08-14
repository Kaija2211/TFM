using System;
using Sim;

namespace Manager
{
    public sealed class ManagerTransferSearch
    {
        public string PlayerName = string.Empty;
        public string ClubName = string.Empty;
        public string Nationality = string.Empty;
        public PlayerPosition? Position;
        public int? MinimumAge;
        public int? MaximumAge;

        public bool HasCriteria =>
            !string.IsNullOrWhiteSpace(PlayerName) ||
            !string.IsNullOrWhiteSpace(ClubName) ||
            !string.IsNullOrWhiteSpace(Nationality) ||
            Position.HasValue || MinimumAge.HasValue || MaximumAge.HasValue;

        public bool Matches(PlayerAgent player, string clubName)
        {
            if (player == null) return false;
            if (!Contains(player.Name, PlayerName)) return false;
            if (!Contains(clubName, ClubName)) return false;
            if (!string.IsNullOrWhiteSpace(Nationality) &&
                !Contains(ManagerPlayerNationality.GetNationality(player).Name, Nationality)) return false;
            if (Position.HasValue && player.PrimaryPosition != Position.Value &&
                !player.SecondaryPositions.Contains(Position.Value)) return false;
            if (MinimumAge.HasValue && player.Age < MinimumAge.Value) return false;
            if (MaximumAge.HasValue && player.Age > MaximumAge.Value) return false;
            return true;
        }

        public void Clear()
        {
            PlayerName = string.Empty;
            ClubName = string.Empty;
            Nationality = string.Empty;
            Position = null;
            MinimumAge = null;
            MaximumAge = null;
        }

        private static bool Contains(string value, string query) =>
            string.IsNullOrWhiteSpace(query) ||
            (!string.IsNullOrEmpty(value) && value.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
