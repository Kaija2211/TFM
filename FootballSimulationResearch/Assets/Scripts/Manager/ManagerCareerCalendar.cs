using System;
using System.Globalization;

namespace Manager
{
    // Career clock. Simulation advances one real date at a time, while the UI can jump
    // over quiet days and stop at the next fixture or actionable event.
    public sealed class ManagerCareerCalendar
    {
        private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-GB");
        private static readonly DateTime CareerEpoch = new DateTime(2026, 6, 1);

        public DateTime CurrentDate { get; private set; }
        public int SeasonStartYear { get; private set; }
        public DateTime SeasonStartDate => new DateTime(SeasonStartYear, 6, 1);
        public int CurrentDayNumber => (int)(CurrentDate.Date - CareerEpoch).TotalDays;

        public bool IsSummerTransferWindowOpen =>
            CurrentDate.Date >= new DateTime(SeasonStartYear, 6, 15) &&
            CurrentDate.Date <= new DateTime(SeasonStartYear, 8, 31);

        public bool IsWinterTransferWindowOpen =>
            CurrentDate.Date >= new DateTime(SeasonStartYear + 1, 1, 1) &&
            CurrentDate.Date <= new DateTime(SeasonStartYear + 1, 2, 1);

        public bool IsTransferWindowOpen => IsSummerTransferWindowOpen || IsWinterTransferWindowOpen;
        public string DisplayDate => CurrentDate.ToString("ddd d MMM yyyy", DisplayCulture);

        public void StartSeason(int seasonStartYear)
        {
            SeasonStartYear = seasonStartYear;
            CurrentDate = SeasonStartDate;
        }

        public void Restore(int seasonStartYear, string serializedDate, int legacyFixtureIndex)
        {
            SeasonStartYear = seasonStartYear;
            if (!DateTime.TryParseExact(serializedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime restored))
            {
                restored = legacyFixtureIndex <= 0
                    ? SeasonStartDate
                    : GetFixtureDate(legacyFixtureIndex - 1).AddDays(1);
            }

            CurrentDate = restored.Date;
        }

        public void AdvanceOneDay() => CurrentDate = CurrentDate.AddDays(1);

        public DateTime GetFixtureDate(int fixtureIndex)
        {
            // Current source data has rounds but no dates. A Saturday weekly baseline
            // gives the career a stable calendar now; cup/midweek scheduling can replace
            // this mapping once competitions provide exact fixture dates.
            DateTime augustFirst = new DateTime(SeasonStartYear, 8, 1);
            int daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)augustFirst.DayOfWeek + 7) % 7;
            DateTime openingDay = augustFirst.AddDays(daysUntilSaturday + 7);
            return openingDay.AddDays(fixtureIndex * 7);
        }

        public string SerializeDate() => CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string DisplayDateForDay(int careerDayNumber) =>
            CareerEpoch.AddDays(Math.Max(0, careerDayNumber)).ToString("d MMM yyyy", DisplayCulture);
    }
}
