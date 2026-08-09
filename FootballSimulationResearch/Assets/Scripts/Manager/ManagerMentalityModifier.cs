namespace Manager
{
    // Adjusts pre-match expected goals for Manager Mode only. Never touched by
    // ResearchEvaluationRunner, so it cannot affect the Statistical-vs-ABM comparison.
    // Renamed from ManagerTactic/ManagerTacticModifier - "mentality" is the real
    // football term for this attacking/balanced/defensive spectrum ("tactics" more
    // naturally means formation/shape/pressing, which this has nothing to do with).
    public static class ManagerMentalityModifier
    {
        public static void Apply(
            ManagerMentality mentality,
            ref float managedTeamExpectedGoals,
            ref float opponentExpectedGoals)
        {
            switch (mentality)
            {
                case ManagerMentality.Attacking:
                    managedTeamExpectedGoals *= 1.15f;
                    opponentExpectedGoals *= 1.08f;
                    break;

                case ManagerMentality.Defensive:
                    managedTeamExpectedGoals *= 0.90f;
                    opponentExpectedGoals *= 0.85f;
                    break;

                case ManagerMentality.Balanced:
                default:
                    break;
            }
        }
    }
}
