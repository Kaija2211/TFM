using UnityEngine;

namespace Sim
{
    public static class PlayerAttributeModel
    {
        public const int CurrentSchemaVersion = 2;

        public static void EnsureCurrent(PlayerAgent player)
        {
            if (player == null || player.AttributeSchemaVersion >= CurrentSchemaVersion) return;
            UpgradeLegacyProfile(player);
        }

        public static float CalculateOverall(PlayerAgent p)
        {
            EnsureCurrent(p);
            switch (p.PrimaryPosition)
            {
                case PlayerPosition.GK:
                    return Weighted((p.Handling, 22), (p.Reflexes, 20), (p.OneOnOnes, 15),
                        (p.GoalkeeperPositioning, 14), (p.AerialCommand, 10), (p.Distribution, 7),
                        (p.Decisions, 5), (p.Composure, 4), (p.JumpingReach, 2), (p.WeakFoot, 1));
                case PlayerPosition.CB:
                    return Weighted((p.Marking, 16), (p.Tackling, 15), (p.DefensivePositioning, 15),
                        (p.Anticipation, 11), (p.JumpingReach, 9), (p.Heading, 8), (p.Strength, 7),
                        (p.Decisions, 6), (p.Composure, 4), (p.Passing, 4), (p.Pace, 3), (p.Acceleration, 1), (p.WeakFoot, 1));
                case PlayerPosition.RB:
                case PlayerPosition.LB:
                    return Weighted((p.Tackling, 11), (p.DefensivePositioning, 10), (p.Pace, 10),
                        (p.Acceleration, 8), (p.Crossing, 11), (p.Stamina, 9), (p.WorkRate, 8),
                        (p.Passing, 7), (p.Dribbling, 6), (p.Marking, 5), (p.FirstTouch, 4),
                        (p.Decisions, 4), (p.Strength, 3), (p.WeakFoot, 4));
                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                    return Weighted((p.Crossing, 13), (p.Pace, 11), (p.Acceleration, 9),
                        (p.Stamina, 10), (p.WorkRate, 9), (p.Dribbling, 9), (p.Passing, 7),
                        (p.FirstTouch, 5), (p.Tackling, 6), (p.DefensivePositioning, 5),
                        (p.OffTheBall, 5), (p.Decisions, 4), (p.WeakFoot, 6));
                case PlayerPosition.DM:
                    return Weighted((p.DefensivePositioning, 14), (p.Tackling, 13), (p.Marking, 9),
                        (p.Passing, 12), (p.Decisions, 10), (p.Anticipation, 9), (p.Composure, 7),
                        (p.WorkRate, 7), (p.Stamina, 5), (p.Strength, 4), (p.FirstTouch, 4),
                        (p.Vision, 3), (p.Aggression, 2), (p.WeakFoot, 1));
                case PlayerPosition.CM:
                    return Weighted((p.Passing, 14), (p.FirstTouch, 10), (p.Decisions, 10),
                        (p.Vision, 10), (p.Composure, 8), (p.Technique, 8), (p.Stamina, 7),
                        (p.WorkRate, 7), (p.Dribbling, 5), (p.Anticipation, 5), (p.Tackling, 4),
                        (p.OffTheBall, 4), (p.LongShots, 3), (p.Balance, 2), (p.WeakFoot, 3));
                case PlayerPosition.AM:
                    return Weighted((p.Vision, 14), (p.FirstTouch, 11), (p.Passing, 10),
                        (p.Technique, 10), (p.Dribbling, 10), (p.Decisions, 9), (p.Composure, 8),
                        (p.OffTheBall, 7), (p.Finishing, 7), (p.Agility, 5), (p.LongShots, 4),
                        (p.Acceleration, 3), (p.WeakFoot, 2));
                case PlayerPosition.RM:
                case PlayerPosition.LM:
                    return Weighted((p.Crossing, 12), (p.Dribbling, 10), (p.Passing, 9),
                        (p.Stamina, 9), (p.WorkRate, 8), (p.Pace, 8), (p.FirstTouch, 7),
                        (p.Vision, 7), (p.Acceleration, 6), (p.Technique, 5), (p.Tackling, 5),
                        (p.DefensivePositioning, 4), (p.Decisions, 4), (p.WeakFoot, 6));
                case PlayerPosition.RW:
                case PlayerPosition.LW:
                    return Weighted((p.Dribbling, 13), (p.Acceleration, 11), (p.Pace, 10),
                        (p.FirstTouch, 9), (p.Technique, 9), (p.Crossing, 8), (p.Finishing, 8),
                        (p.OffTheBall, 7), (p.Vision, 6), (p.Agility, 6), (p.Composure, 5),
                        (p.Decisions, 4), (p.Passing, 2), (p.WeakFoot, 2));
                case PlayerPosition.ST:
                    return Weighted((p.Finishing, 16), (p.OffTheBall, 12), (p.Composure, 11),
                        (p.Anticipation, 9), (p.FirstTouch, 8), (p.Heading, 7), (p.Acceleration, 7),
                        (p.Pace, 6), (p.Technique, 5), (p.Strength, 5), (p.JumpingReach, 5),
                        (p.Dribbling, 4), (p.Decisions, 3), (p.WeakFoot, 2));
                default:
                    return Weighted((p.Passing, 12), (p.FirstTouch, 10), (p.Decisions, 10),
                        (p.Composure, 10), (p.Pace, 8), (p.Stamina, 8), (p.Technique, 8),
                        (p.DefensivePositioning, 8), (p.Finishing, 7), (p.Dribbling, 7),
                        (p.Strength, 5), (p.WeakFoot, 7));
            }
        }

        // Deterministic reconstruction for v1 saves and the old generator. The small
        // blends preserve the player's established strengths and weaknesses rather
        // than inventing a second, unrelated footballer during migration.
        public static void UpgradeLegacyProfile(PlayerAgent player)
        {
            player.FirstTouch = Blend(player.Dribbling, player.Passing, player.Composure);
            player.Technique = Blend(player.Dribbling, player.Passing, player.FreeKicks);
            player.Corners = Blend(player.Crossing, player.FreeKicks, player.Passing);
            player.Penalties = Blend(player.Finishing, player.Composure, player.FreeKicks);

            player.Anticipation = Blend(player.Positioning, player.OffTheBall, player.Composure);
            player.Decisions = Blend(player.Composure, player.Positioning, player.Passing);
            player.Vision = Blend(player.Creativity, player.ThroughBalls, player.Passing);
            player.DefensivePositioning = Blend(player.Defending, player.Marking, player.Positioning);
            player.WorkRate = Blend(player.Stamina, player.Positioning, player.Leadership);
            player.Aggression = Blend(player.Tackling, player.Strength, player.Defending);

            player.Acceleration = Blend(player.Pace, player.Agility > 0f ? player.Agility : player.Pace);
            player.Agility = Blend(player.Pace, player.Dribbling, player.Balance > 0f ? player.Balance : player.Strength);
            player.Balance = Blend(player.Strength, player.Dribbling, player.Aerial);
            player.JumpingReach = HeightAdjustedJumping(player);

            player.Handling = Blend(player.Goalkeeping, player.Composure, player.Reflexes);
            player.OneOnOnes = Blend(player.Reflexes, player.Goalkeeping, player.Composure);
            player.AerialCommand = Blend(player.Goalkeeping, player.Aerial, player.Strength);
            player.Distribution = Blend(player.Passing, player.Goalkeeping, player.Composure);
            player.GoalkeeperPositioning = Blend(player.Goalkeeping, player.Positioning, player.Composure);
            player.AttributeSchemaVersion = CurrentSchemaVersion;
            ClampAll(player);
        }

        // During the staged migration old fields remain compatibility mirrors for UI,
        // roles and simulators that have not yet moved to the richer model.
        public static void SyncLegacyDerivedFields(PlayerAgent player)
        {
            EnsureCurrent(player);
            player.Creativity = Blend(player.Vision, player.Technique);
            player.ThroughBalls = Blend(player.Vision, player.Passing, player.Decisions);
            player.Positioning = Blend(player.Anticipation, player.DefensivePositioning, player.OffTheBall);
            player.Defending = Blend(player.Tackling, player.Marking, player.DefensivePositioning);
            player.Aerial = Blend(player.JumpingReach, player.Heading, player.AerialCommand);
            if (player.PrimaryPosition == PlayerPosition.GK)
                player.Goalkeeping = Blend(player.Handling, player.OneOnOnes, player.AerialCommand, player.GoalkeeperPositioning);
            ClampAll(player);
        }

        public static void ClampAll(PlayerAgent p)
        {
            p.FirstTouch = Clamp(p.FirstTouch); p.Technique = Clamp(p.Technique);
            p.Corners = Clamp(p.Corners); p.Penalties = Clamp(p.Penalties);
            p.Anticipation = Clamp(p.Anticipation); p.Decisions = Clamp(p.Decisions);
            p.Vision = Clamp(p.Vision); p.DefensivePositioning = Clamp(p.DefensivePositioning);
            p.WorkRate = Clamp(p.WorkRate); p.Aggression = Clamp(p.Aggression);
            p.Acceleration = Clamp(p.Acceleration); p.Agility = Clamp(p.Agility);
            p.Balance = Clamp(p.Balance); p.JumpingReach = Clamp(p.JumpingReach);
            p.Handling = Clamp(p.Handling); p.OneOnOnes = Clamp(p.OneOnOnes);
            p.AerialCommand = Clamp(p.AerialCommand); p.Distribution = Clamp(p.Distribution);
            p.GoalkeeperPositioning = Clamp(p.GoalkeeperPositioning);
        }

        private static float HeightAdjustedJumping(PlayerAgent p)
        {
            float heightBonus = Mathf.Clamp((p.Height - 180f) * 0.45f, -8f, 8f);
            return Blend(p.Aerial, p.Heading, p.Strength) + heightBonus;
        }

        private static float Blend(params float[] values)
        {
            float total = 0f;
            foreach (float value in values) total += value;
            return values.Length == 0 ? 1f : total / values.Length;
        }

        private static float Weighted(params (float value, float weight)[] terms)
        {
            float total = 0f, weight = 0f;
            foreach ((float value, float termWeight) in terms)
            {
                total += value * termWeight;
                weight += termWeight;
            }
            return weight > 0f ? total / weight : 1f;
        }

        private static float Clamp(float value) => Mathf.Clamp(value, 1f, 99f);
    }
}
