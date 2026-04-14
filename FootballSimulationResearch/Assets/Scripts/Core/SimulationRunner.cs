using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class SimulationRunner : MonoBehaviour
{
    [System.Serializable]
    private class Config { public string token; }

    private const string BaseUrl = "https://api.football-data.org/v4";

    private IEnumerator DownloadSeasons(string competitionCode, int startSeason, int endSeason)
{
    // Load token once
    var configAsset = Resources.Load<TextAsset>("football-data-config");
    if (configAsset == null)
    {
        Debug.LogError("Missing Assets/Resources/football-data-config.json");
        yield break;
    }

    var cfg = JsonUtility.FromJson<Config>(configAsset.text);
    if (cfg == null || string.IsNullOrWhiteSpace(cfg.token))
    {
        Debug.LogError("Token missing/invalid in football-data-config.json");
        yield break;
    }

    for (int season = startSeason; season <= endSeason; season++)
    {
        string path = Path.Combine(Application.persistentDataPath, $"{competitionCode}_{season}_matches.json");

        if (File.Exists(path))
        {
            Debug.Log($"✅ Cached already: {path}");
            continue;
        }

        string url = $"{BaseUrl}/competitions/{competitionCode}/matches?season={season}";
        Debug.Log($"⬇️ Downloading {competitionCode} season {season}...");

        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("X-Auth-Token", cfg.token);
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

           if (req.result != UnityWebRequest.Result.Success)
{
    Debug.LogError($"❌ Failed {season}: HTTP {req.responseCode} - {req.error}");

    // 403 = your subscription tier doesn't allow this season
    if (req.responseCode == 403)
    {
        Debug.LogWarning($"⚠️ No access to {competitionCode} season {season} on current plan. Skipping.");
        continue;
    }

    // Anything else: stop so you can see and fix it
    Debug.LogError(req.downloadHandler.text);
    yield break;
}

            string json = req.downloadHandler.text;
            File.WriteAllText(path, json);
            Debug.Log($"✅ Saved {season} to: {path} (len {json.Length})");
        }

        // Polite delay to avoid rate limits
        yield return new WaitForSeconds(1.5f);
    }
        // Quick parse test (uses 2023 file)
        string testPath = Path.Combine(Application.persistentDataPath, $"{competitionCode}_2023_matches.json");
        if (File.Exists(testPath))
        {
            string json = File.ReadAllText(testPath);
            var teamNames = new System.Collections.Generic.Dictionary<int, string>();
            var matches = Data.MatchParser.ParseMatches(json, teamNames);   
            Debug.Log($"✅ Parsed FINISHED matches from cache: {matches.Count}");
            var table = new Sim.LeagueTable();
            foreach (var m in matches)
                table.Apply(m);

            var sorted = table.Sorted();

            Debug.Log($"=== League Table (Top 5) for {competitionCode} 2023 ===");
            Debug.Log($"Using cached dataset: {testPath}");
            
            for (int i = 0; i < 5 && i < sorted.Count; i++)
            {
                var e = sorted[i];
                int gd = e.GoalsFor - e.GoalsAgainst;
                string name = teamNames.TryGetValue(e.TeamId, out var n) ? n : $"Team {e.TeamId}";
                Debug.Log($"{i + 1}. {name}  Pts:{e.Points}  P:{e.Played}  GD:{gd}  GF:{e.GoalsFor}");
            }
        }
            Debug.Log("🏁 All requested seasons downloaded/cached.");
        }

    void Start()
    {
        StartCoroutine(DownloadSeasons("PL", 2023, 2024));
    }   

}