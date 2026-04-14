using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sim;


namespace Data
{
    public static class MatchParser
    {
        public static List<MatchRecord> ParseMatches(string json)
        {
            var root = JObject.Parse(json);
            var matches = root["matches"] as JArray;

            var list = new List<MatchRecord>(matches?.Count ?? 0);
            if (matches == null) return list;

            foreach (var m in matches)
            {
                // Only finished matches
                var status = (string)m["status"];
                if (status != "FINISHED") continue;

                int matchday = (int)m["matchday"];
                int homeId = (int)m["homeTeam"]["id"];
                int awayId = (int)m["awayTeam"]["id"];
                int homeGoals = (int)m["score"]["fullTime"]["home"];
                int awayGoals = (int)m["score"]["fullTime"]["away"];

                list.Add(new MatchRecord
                {
                    Matchday = matchday,
                    HomeTeamId = homeId,
                    AwayTeamId = awayId,
                    HomeGoals = homeGoals,
                    AwayGoals = awayGoals
                });
            }

            return list;
        }

        public static List<MatchRecord> ParseMatches(string json, Dictionary<int, string> teamNames)
{
    var root = JObject.Parse(json);
    var matches = root["matches"] as JArray;

    var list = new List<MatchRecord>(matches?.Count ?? 0);
    if (matches == null) return list;

    foreach (var m in matches)
    {
        var status = (string)m["status"];
        if (status != "FINISHED") continue;

        int matchday = (int)m["matchday"];

        int homeId = (int)m["homeTeam"]["id"];
        int awayId = (int)m["awayTeam"]["id"];

        // 👇 Add names to dictionary (only first time seen)
        string homeName = (string)m["homeTeam"]["name"];
        string awayName = (string)m["awayTeam"]["name"];

        if (!teamNames.ContainsKey(homeId)) teamNames[homeId] = homeName;
        if (!teamNames.ContainsKey(awayId)) teamNames[awayId] = awayName;

        int homeGoals = (int)m["score"]["fullTime"]["home"];
        int awayGoals = (int)m["score"]["fullTime"]["away"];

        list.Add(new MatchRecord
        {
            Matchday = matchday,
            HomeTeamId = homeId,
            AwayTeamId = awayId,
            HomeGoals = homeGoals,
            AwayGoals = awayGoals
        });
    }

    return list;
}
    }
}