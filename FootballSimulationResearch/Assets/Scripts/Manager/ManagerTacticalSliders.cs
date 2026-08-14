namespace Manager
{
    public enum TempoSetting
    {
        Slow,
        Balanced,
        Fast
    }

    public enum WidthSetting
    {
        Narrow,
        Balanced,
        Wide
    }

    public enum DefensiveDepthSetting
    {
        Deep,
        Balanced,
        High
    }

    // Manager Mode-only, managed team only - unlike ManagerSquadRoles (per-player,
    // per-team), there's only ever one of these that matters: the manager's own current
    // tactical setup. Read by the ManagerSim fork's PickChanceType bias (see
    // AgentMatchSimulator.BuildChanceTypeBias) - Tempo/Width shape the managed team's own
    // attacking chance-type mix, DefensiveDepth shapes the opponent's mix when they
    // attack the managed team.
    public class ManagerTacticalSliders
    {
        public TempoSetting Tempo = TempoSetting.Balanced;
        public WidthSetting Width = WidthSetting.Balanced;
        public DefensiveDepthSetting DefensiveDepth = DefensiveDepthSetting.Balanced;
    }
}
