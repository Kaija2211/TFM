namespace Manager
{
    // Adjusts pre-match expected goals for Manager Mode only. Never touched by
    // ResearchEvaluationRunner, so it cannot affect the Statistical-vs-ABM comparison.
    public static class ManagerTacticModifier
    {
        public static void Apply(
            ManagerTactic tactic,
            ref float managedTeamExpectedGoals,
            ref float opponentExpectedGoals)
        {
            switch (tactic)
            {
                case ManagerTactic.Attacking:
                    managedTeamExpectedGoals *= 1.15f;
                    opponentExpectedGoals *= 1.08f;
                    break;

                case ManagerTactic.Defensive:
                    managedTeamExpectedGoals *= 0.90f;
                    opponentExpectedGoals *= 0.85f;
                    break;

                case ManagerTactic.Balanced:
                default:
                    break;
            }
        }
    }
}
