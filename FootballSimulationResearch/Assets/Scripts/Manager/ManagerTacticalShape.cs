using System;
using System.Collections.Generic;
using Sim;
using UnityEngine;

namespace Manager
{
    public static class ManagerTacticalShape
    {
        public sealed class RouteEffect
        {
            public float ThroughBall = 1f;
            public float Cross = 1f;
            public float Dribble = 1f;
            public float LongShot = 1f;
            public float SetPiece = 1f;
            public float CounterAttack = 1f;
            public IEnumerable<float> All
            {
                get
                {
                    yield return ThroughBall; yield return Cross; yield return Dribble;
                    yield return LongShot; yield return SetPiece; yield return CounterAttack;
                }
            }
        }

        public sealed class Matchup
        {
            public string HomeTeamName;
            public string AwayTeamName;
            public RouteEffect HomeAttack = new();
            public RouteEffect AwayAttack = new();
            public RouteEffect GetAttackEffect(string teamName) =>
                teamName == HomeTeamName ? HomeAttack : teamName == AwayTeamName ? AwayAttack : null;
        }

        private sealed class ShapeProfile
        {
            public readonly float[] Attack = new float[3];
            public readonly float[] Defence = new float[3];
            public float TransitionExposure;
            public float SettledTempo;
        }

        private const float MinimumRouteMultiplier = 0.82f;
        private const float MaximumRouteMultiplier = 1.18f;

        public static Matchup BuildMatchup(string homeTeamName, Formation homeFormation, ManagerTacticalSliders homeTactics,
            string awayTeamName, Formation awayFormation, ManagerTacticalSliders awayTactics,
            AgentTeam homeTeam = null, ManagerSquadRoles homeRoles = null,
            AgentTeam awayTeam = null, ManagerSquadRoles awayRoles = null)
        {
            ShapeProfile home = BuildProfile(homeFormation, homeTactics, homeTeam, homeRoles);
            ShapeProfile away = BuildProfile(awayFormation, awayTactics, awayTeam, awayRoles);
            return new Matchup
            {
                HomeTeamName = homeTeamName,
                AwayTeamName = awayTeamName,
                HomeAttack = BuildRouteEffect(home, away),
                AwayAttack = BuildRouteEffect(away, home)
            };
        }

        public static string DescribeForTeam(Matchup matchup, string teamName)
        {
            if (matchup == null) return "No tactical read available";
            RouteEffect own = matchup.GetAttackEffect(teamName);
            string opponentName = teamName == matchup.HomeTeamName ? matchup.AwayTeamName : matchup.HomeTeamName;
            RouteEffect opponent = matchup.GetAttackEffect(opponentName);
            if (own == null || opponent == null) return "No tactical read available";

            (string name, float value) edge = StrongestRoute(own);
            (string name, float value) risk = StrongestRoute(opponent);
            string edgeText = edge.value >= 1.015f ? $"EDGE: {edge.name}" : "EDGE: EVEN SHAPE";
            string riskText = risk.value >= 1.015f ? $"RISK: {risk.name}" : "RISK: NO CLEAR ROUTE";
            return $"{edgeText}   ·   {riskText}";
        }

        private static (string, float) StrongestRoute(RouteEffect effect)
        {
            (string name, float value) best = ("THROUGH BALLS", effect.ThroughBall);
            (string, float)[] routes =
            {
                ("CROSSES", effect.Cross), ("DRIBBLES", effect.Dribble),
                ("LONG SHOTS", effect.LongShot), ("SET PIECES", effect.SetPiece),
                ("COUNTERS", effect.CounterAttack)
            };
            foreach ((string name, float value) route in routes)
            {
                if (route.value > best.value) best = route;
            }
            return best;
        }

        private static ShapeProfile BuildProfile(Formation formation, ManagerTacticalSliders tactics,
            AgentTeam team, ManagerSquadRoles roles)
        {
            tactics ??= new ManagerTacticalSliders();
            IReadOnlyList<Vector2> pins = TacticsBoardLayout.GetPins(formation);
            ShapeProfile profile = new ShapeProfile();
            float defensiveLineTotal = 0f;
            int defensiveLinePlayers = 0;

            for (int index = 1; index < pins.Count; index++)
            {
                float x = ApplyWidth(pins[index].x, tactics.Width);
                float y = ApplyDefensiveDepth(pins[index].y, tactics.DefensiveDepth);
                if (team != null && roles != null && index < team.StartingEleven.Count)
                {
                    AttackDefendRole instruction = roles.GetRole(team.StartingEleven[index]);
                    if (instruction == AttackDefendRole.Attacking) y -= 0.035f;
                    else if (instruction == AttackDefendRole.Defensive) y += 0.035f;
                    y = Mathf.Clamp01(y);
                }
                float[] channels = GetChannelWeights(x);
                float attackingWeight = Mathf.Lerp(0.35f, 1.20f, 1f - y);
                float defensiveWeight = Mathf.Lerp(0.35f, 1.20f, y);
                for (int channel = 0; channel < 3; channel++)
                {
                    profile.Attack[channel] += channels[channel] * attackingWeight;
                    profile.Defence[channel] += channels[channel] * defensiveWeight;
                }

                if (pins[index].y >= 0.65f)
                {
                    defensiveLineTotal += y;
                    defensiveLinePlayers++;
                }
            }

            NormalizeChannels(profile.Attack);
            NormalizeChannels(profile.Defence);
            float averageDefensiveLine = defensiveLinePlayers > 0 ? defensiveLineTotal / defensiveLinePlayers : 0.72f;
            float depthExposure = Mathf.InverseLerp(0.80f, 0.62f, averageDefensiveLine);
            float tempoExposure = tactics.Tempo == TempoSetting.Fast ? 0.16f : tactics.Tempo == TempoSetting.Slow ? -0.10f : 0f;
            profile.TransitionExposure = Mathf.Clamp01(0.42f + depthExposure * 0.38f + tempoExposure);
            profile.SettledTempo = tactics.Tempo == TempoSetting.Slow ? 1f : tactics.Tempo == TempoSetting.Fast ? -1f : 0f;
            return profile;
        }

        private static RouteEffect BuildRouteEffect(ShapeProfile attack, ShapeProfile defence)
        {
            float leftEdge = attack.Attack[0] - defence.Defence[2];
            float centreEdge = attack.Attack[1] - defence.Defence[1];
            float rightEdge = attack.Attack[2] - defence.Defence[0];
            float wideEdge = (leftEdge + rightEdge) * 0.5f;
            float strongestLane = Mathf.Max(leftEdge, Mathf.Max(centreEdge, rightEdge));
            float attackWideShape = ((attack.Attack[0] + attack.Attack[2]) * 0.5f) - 1f;
            float attackCentralShape = attack.Attack[1] - 1f;
            float transitionSpace = defence.TransitionExposure - 0.5f;

            return new RouteEffect
            {
                Cross = Bound(1f + attackWideShape * 0.10f + wideEdge * 0.10f),
                ThroughBall = Bound(1f + attackCentralShape * 0.08f + centreEdge * 0.10f + transitionSpace * 0.08f),
                Dribble = Bound(1f + strongestLane * 0.08f - attack.SettledTempo * 0.03f),
                LongShot = Bound(1f + centreEdge * 0.04f - transitionSpace * 0.06f + attack.SettledTempo * 0.04f),
                SetPiece = Bound(1f + wideEdge * 0.04f + attack.SettledTempo * 0.03f),
                CounterAttack = Bound(1f + transitionSpace * 0.18f - attack.SettledTempo * 0.07f)
            };
        }

        private static float ApplyWidth(float x, WidthSetting width)
        {
            float factor = width == WidthSetting.Wide ? 1.15f : width == WidthSetting.Narrow ? 0.82f : 1f;
            return Mathf.Clamp01(0.5f + (x - 0.5f) * factor);
        }

        private static float ApplyDefensiveDepth(float y, DefensiveDepthSetting depth)
        {
            if (y < 0.62f) return y;
            float shift = depth == DefensiveDepthSetting.High ? -0.06f : depth == DefensiveDepthSetting.Deep ? 0.06f : 0f;
            return Mathf.Clamp01(y + shift);
        }

        private static float[] GetChannelWeights(float x)
        {
            float left = Mathf.Clamp01((0.55f - x) / 0.35f);
            float right = Mathf.Clamp01((x - 0.45f) / 0.35f);
            float centre = Mathf.Max(0f, 1f - Mathf.Max(left, right));
            float total = Mathf.Max(0.001f, left + centre + right);
            return new[] { left / total, centre / total, right / total };
        }

        private static void NormalizeChannels(float[] channels)
        {
            float total = Mathf.Max(0.001f, channels[0] + channels[1] + channels[2]);
            for (int index = 0; index < channels.Length; index++) channels[index] = channels[index] * 3f / total;
        }

        private static float Bound(float value) => Mathf.Clamp(value, MinimumRouteMultiplier, MaximumRouteMultiplier);
    }
}
