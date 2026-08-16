using System;
using System.Collections.Generic;
using System.Linq;
using Manager;
using Sim;
using UnityEditor;
using UnityEngine;

public static class ManagerLeadershipAudit
{
    private const int SquadCount = 200;

    [MenuItem("TFM/Audits/Leadership Distribution")]
    public static void Run()
    {
        UnityEngine.Random.State previous = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(150826);
            AgentSquadGenerator generator = new();
            List<PlayerAgent> players = new(SquadCount * 30);
            for (int i = 0; i < SquadCount; i++)
                players.AddRange(generator.GenerateSquad($"Leadership Audit {i}", new SquadQualityTarget(79f, 76f, 73f)).Players);

            float[] values = players.Select(player => player.Leadership).OrderBy(value => value).ToArray();
            float median = Percentile(values, 0.50f);
            float p90 = Percentile(values, 0.90f);
            float p95 = Percentile(values, 0.95f);
            int elite = values.Count(value => value >= 80f);
            int strong = values.Count(value => value >= 70f);
            float veteranMean = players.Where(player => player.Age >= 30).Average(player => player.Leadership);
            float youthMean = players.Where(player => player.Age <= 21).Average(player => player.Leadership);

            string summary = $"Leadership distribution across {players.Count:N0} players: " +
                $"median {median:F1}, P90 {p90:F1}, P95 {p95:F1}, 70+ {strong} ({strong * 100f / players.Count:F1}%), " +
                $"80+ {elite} ({elite * 100f / players.Count:F1}%), youth mean {youthMean:F1}, veteran mean {veteranMean:F1}.";
            Debug.Log(summary);

            Require(median >= 42f && median <= 58f, $"median {median:F1} escaped the ordinary-player band");
            Require(p90 >= 62f && p90 <= 78f, $"90th percentile {p90:F1} is implausible");
            Require(elite >= players.Count * 0.01f, $"only {elite}/{players.Count} players reached 80 Leadership");
            Require(elite <= players.Count * 0.10f, $"{elite}/{players.Count} players reached 80 Leadership");
            Require(veteranMean > youthMean + 8f, "veterans were not meaningfully stronger leaders than youth");

            Debug.Log("Leadership distribution audit passed.");
        }
        finally
        {
            UnityEngine.Random.state = previous;
        }
    }

    private static float Percentile(float[] values, float percentile)
    {
        int index = Mathf.Clamp(Mathf.RoundToInt((values.Length - 1) * percentile), 0, values.Length - 1);
        return values[index];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Leadership distribution audit failed: {message}.");
    }
}
