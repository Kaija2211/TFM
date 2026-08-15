using System;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

public static class ManagerTacticalShapeAudit
{
    [MenuItem("TFM/Audits/Tactical Shape Sensitivity")]
    public static void Run()
    {
        AuditBounds();
        AuditMirroring();
        AuditSliderSensitivity();
        AuditWideOverload();
        Debug.Log("Tactical shape sensitivity audit passed.");
    }

    private static void AuditBounds()
    {
        foreach (Formation home in Enum.GetValues(typeof(Formation)))
        foreach (Formation away in Enum.GetValues(typeof(Formation)))
        {
            ManagerTacticalShape.Matchup matchup = ManagerTacticalShape.BuildMatchup("Home", home, null, "Away", away, null);
            foreach (float multiplier in matchup.HomeAttack.All) Require(multiplier >= 0.82f && multiplier <= 1.18f, $"route bound failed for {home} v {away}");
            foreach (float multiplier in matchup.AwayAttack.All) Require(multiplier >= 0.82f && multiplier <= 1.18f, $"route bound failed for {away} v {home}");
        }
    }

    private static void AuditMirroring()
    {
        ManagerTacticalShape.Matchup first = ManagerTacticalShape.BuildMatchup("A", Formation.FourThreeThree, null, "B", Formation.ThreeFourThree, null);
        ManagerTacticalShape.Matchup reversed = ManagerTacticalShape.BuildMatchup("B", Formation.ThreeFourThree, null, "A", Formation.FourThreeThree, null);
        Require(Approximately(first.HomeAttack.Cross, reversed.AwayAttack.Cross), "swapping teams changed A's Cross effect");
        Require(Approximately(first.AwayAttack.ThroughBall, reversed.HomeAttack.ThroughBall), "swapping teams changed B's ThroughBall effect");
    }

    private static void AuditSliderSensitivity()
    {
        ManagerTacticalSliders wideHighFast = new()
        {
            Width = WidthSetting.Wide,
            DefensiveDepth = DefensiveDepthSetting.High,
            Tempo = TempoSetting.Fast
        };
        ManagerTacticalSliders narrowDeepSlow = new()
        {
            Width = WidthSetting.Narrow,
            DefensiveDepth = DefensiveDepthSetting.Deep,
            Tempo = TempoSetting.Slow
        };

        ManagerTacticalShape.Matchup wide = ManagerTacticalShape.BuildMatchup("A", Formation.FourThreeThree, wideHighFast, "B", Formation.FourTwoThreeOne, null);
        ManagerTacticalShape.Matchup narrow = ManagerTacticalShape.BuildMatchup("A", Formation.FourThreeThree, narrowDeepSlow, "B", Formation.FourTwoThreeOne, null);
        Require(wide.HomeAttack.Cross > narrow.HomeAttack.Cross, "Wide did not increase crossing routes");

        ManagerTacticalShape.Matchup versusHigh = ManagerTacticalShape.BuildMatchup("A", Formation.FourTwoThreeOne, null, "B", Formation.FourThreeThree, wideHighFast);
        ManagerTacticalShape.Matchup versusDeep = ManagerTacticalShape.BuildMatchup("A", Formation.FourTwoThreeOne, null, "B", Formation.FourThreeThree, narrowDeepSlow);
        Require(versusHigh.HomeAttack.CounterAttack > versusDeep.HomeAttack.CounterAttack, "High line did not expose more counter space than Deep");
    }

    private static void AuditWideOverload()
    {
        ManagerTacticalShape.Matchup matchup = ManagerTacticalShape.BuildMatchup(
            "FourThreeThree", Formation.FourThreeThree, new ManagerTacticalSliders { Width = WidthSetting.Wide },
            "ThreeFourThree", Formation.ThreeFourThree, null);
        Require(matchup.HomeAttack.Cross > 1f, "4-3-3 wide pairing did not create a positive crossing route against 3-4-3");
    }

    private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Tactical shape audit failed: {message}.");
    }
}
