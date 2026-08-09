using System.Collections.Generic;
using System.Text;

namespace Sim
{
    public class PlayerAgent
    {
        public string Name;
        public int Age;
        public float Height;

        public PlayerRole Role;
        public PlayerPosition PrimaryPosition;
        public List<PlayerPosition> SecondaryPositions = new();

        // Technical
        public float Finishing;
        public float Passing;
        public float Dribbling;
        public float Crossing;
        public float Heading;
        public float LongShots;
        public float ThroughBalls;
        public float FreeKicks;

        // Creative / mental
        public float Creativity;
        public float Positioning;
        public float Composure;
        public float OffTheBall;

        // Defensive
        public float Defending;
        public float Tackling;
        public float Marking;

        // Physical
        public float Pace;
        public float Strength;
        public float Stamina;
        public float Aerial;

        // Goalkeeping
        public float Goalkeeping;
        public float Reflexes;

        // Two-footedness / versatility
        public float WeakFoot;

        public bool IsStartingEleven;

        public PlayerAgent(
            string name,
            PlayerRole role,
            PlayerPosition primaryPosition)
        {
            Name = name;
            Role = role;
            PrimaryPosition = primaryPosition;
        }

        // Symmetric "positional family" pairs - e.g. an LW who never happened to roll
        // LM as an actual secondary (see AgentSquadGenerator.AddSecondaryPositions,
        // which is probabilistic) should still play a lenient LM, not a totally foreign
        // one. Deliberately mirrors the exact relationships AddSecondaryPositions itself
        // draws its secondary rolls from - a position only ever gets randomly assigned
        // as a listed secondary if it was already "adjacent" in this sense, so this
        // reuses the same football-positional judgement rather than inventing a second,
        // possibly-inconsistent one. Made fully symmetric even where the probabilistic
        // roll wasn't (e.g. ST can roll AM as a secondary but not vice versa) - adjacency
        // is a looser, more generous relationship than "specifically trained for this".
        private static readonly Dictionary<PlayerPosition, PlayerPosition[]> AdjacentPositions = new()
        {
            [PlayerPosition.CB] = new[] { PlayerPosition.DM, PlayerPosition.RB, PlayerPosition.LB },
            [PlayerPosition.RB] = new[] { PlayerPosition.CB, PlayerPosition.RWB, PlayerPosition.RM },
            [PlayerPosition.LB] = new[] { PlayerPosition.CB, PlayerPosition.LWB, PlayerPosition.LM },
            [PlayerPosition.RWB] = new[] { PlayerPosition.RB, PlayerPosition.RM },
            [PlayerPosition.LWB] = new[] { PlayerPosition.LB, PlayerPosition.LM },
            [PlayerPosition.DM] = new[] { PlayerPosition.CB, PlayerPosition.CM },
            [PlayerPosition.CM] = new[] { PlayerPosition.DM, PlayerPosition.AM },
            [PlayerPosition.AM] = new[] { PlayerPosition.CM, PlayerPosition.RW, PlayerPosition.LW, PlayerPosition.ST },
            [PlayerPosition.RM] = new[] { PlayerPosition.RB, PlayerPosition.RWB, PlayerPosition.RW },
            [PlayerPosition.LM] = new[] { PlayerPosition.LB, PlayerPosition.LWB, PlayerPosition.LW },
            [PlayerPosition.RW] = new[] { PlayerPosition.RM, PlayerPosition.AM, PlayerPosition.ST, PlayerPosition.LW },
            [PlayerPosition.LW] = new[] { PlayerPosition.LM, PlayerPosition.AM, PlayerPosition.ST, PlayerPosition.RW },
            [PlayerPosition.ST] = new[] { PlayerPosition.RW, PlayerPosition.LW, PlayerPosition.AM }
            // GK deliberately has no entry - goalkeeping isn't "adjacent" to anything.
        };

        public float GetPositionFit(PlayerPosition position)
        {
            if (PrimaryPosition == position)
            {
                return 1.00f;
            }

            if (SecondaryPositions.Contains(position))
            {
                return 0.85f;
            }

            // Half the full penalty (1.00 -> 0.60 is -0.40; adjacent is -0.20) rather
            // than the full one, for a position close enough to PrimaryPosition even
            // without being an explicitly rolled secondary.
            if (AdjacentPositions.TryGetValue(PrimaryPosition, out PlayerPosition[] adjacent) && System.Array.IndexOf(adjacent, position) >= 0)
            {
                return 0.80f;
            }

            return 0.60f;
        }

        // Position-weighted overall, in the spirit of EA-style ratings: a centre-back's
        // rating leans almost entirely on Defending/Tackling/Aerial/Heading, so his poor
        // Finishing/Crossing never drag it down, while a flat-everywhere midfielder scores
        // slightly below his own simple average, since even out-of-position skills like
        // Goalkeeping/Reflexes still carry a small trace weight for every outfield role.
        public float GetOverallRating()
        {
            switch (PrimaryPosition)
            {
                case PlayerPosition.GK:
                    return WeightedAverage(
                        (Goalkeeping, 34), (Reflexes, 30), (Positioning, 12), (Composure, 9),
                        (Passing, 6), (Strength, 4), (Defending, 3), (WeakFoot, 2)
                    );

                case PlayerPosition.CB:
                    return WeightedAverage(
                        (Defending, 24), (Tackling, 18), (Aerial, 14), (Heading, 12),
                        (Strength, 10), (Positioning, 10), (Passing, 5), (Pace, 3),
                        (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.RB:
                case PlayerPosition.LB:
                    return WeightedAverage(
                        (Defending, 16), (Tackling, 13), (Pace, 14), (Crossing, 14),
                        (Stamina, 10), (Passing, 10), (Dribbling, 9), (Strength, 5),
                        (Heading, 3), (Finishing, 2), (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.RWB:
                case PlayerPosition.LWB:
                    return WeightedAverage(
                        (Pace, 16), (Crossing, 16), (Stamina, 13), (Dribbling, 11),
                        (Defending, 10), (Tackling, 9), (Passing, 9), (Finishing, 3),
                        (Strength, 3), (Aerial, 2), (Positioning, 2), (Heading, 2),
                        (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.DM:
                    return WeightedAverage(
                        (Defending, 17), (Tackling, 15), (Passing, 16), (Positioning, 12),
                        (Strength, 8), (Stamina, 8), (Creativity, 8), (Dribbling, 5),
                        (Heading, 3), (Aerial, 3), (Composure, 1), (Goalkeeping, 1),
                        (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.CM:
                    return WeightedAverage(
                        (Passing, 18), (Creativity, 13), (Positioning, 10), (Composure, 10),
                        (Stamina, 10), (Defending, 10), (Dribbling, 9), (Tackling, 6),
                        (Finishing, 6), (Strength, 3), (Pace, 1), (Goalkeeping, 1),
                        (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.AM:
                    return WeightedAverage(
                        (Creativity, 20), (Passing, 16), (Dribbling, 16), (Finishing, 14),
                        (Composure, 13), (Positioning, 6), (Defending, 3), (Tackling, 2),
                        (Pace, 3), (Stamina, 2), (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 3)
                    );

                case PlayerPosition.RM:
                case PlayerPosition.LM:
                    return WeightedAverage(
                        (Crossing, 16), (Dribbling, 14), (Pace, 13), (Passing, 13),
                        (Stamina, 11), (Defending, 12), (Tackling, 8), (Finishing, 9),
                        (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 2)
                    );

                case PlayerPosition.RW:
                case PlayerPosition.LW:
                    return WeightedAverage(
                        (Dribbling, 20), (Pace, 18), (Crossing, 13), (Creativity, 13),
                        (Finishing, 13), (Passing, 7), (Composure, 4), (Defending, 2),
                        (Tackling, 2), (Stamina, 3), (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 3)
                    );

                case PlayerPosition.ST:
                    return WeightedAverage(
                        (Finishing, 26), (Positioning, 16), (Composure, 14), (Heading, 9),
                        (Aerial, 8), (Pace, 9), (Dribbling, 9), (Passing, 3),
                        (Strength, 2), (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 2)
                    );

                default:
                    return WeightedAverage(
                        (Passing, 15), (Dribbling, 15), (Defending, 15), (Pace, 15),
                        (Stamina, 10), (Positioning, 10), (Composure, 10), (Finishing, 5),
                        (Goalkeeping, 1), (Reflexes, 1), (WeakFoot, 3)
                    );
            }
        }

        private static float WeightedAverage(params (float value, float weight)[] terms)
        {
            float weightedTotal = 0f;
            float totalWeight = 0f;

            foreach ((float value, float weight) in terms)
            {
                weightedTotal += value * weight;
                totalWeight += weight;
            }

            return weightedTotal / totalWeight;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($"{Name} ({PrimaryPosition}) Age:{Age} Ht:{Height:F0}cm ");

            if (SecondaryPositions.Count > 0)
            {
                sb.Append("Sec:");

                for (int i = 0; i < SecondaryPositions.Count; i++)
                {
                    sb.Append(SecondaryPositions[i]);

                    if (i < SecondaryPositions.Count - 1)
                    {
                        sb.Append("/");
                    }
                }

                sb.Append(" ");
            }

            sb.Append(
                $"Fin:{Finishing:F0} " +
                $"Pas:{Passing:F0} " +
                $"Dri:{Dribbling:F0} " +
                $"Cro:{Crossing:F0} " +
                $"Head:{Heading:F0} " +
                $"Cre:{Creativity:F0} " +
                $"Def:{Defending:F0} " +
                $"Tac:{Tackling:F0} " +
                $"Pac:{Pace:F0} " +
                $"Str:{Strength:F0} " +
                $"Sta:{Stamina:F0} " +
                $"Aer:{Aerial:F0} " +
                $"GK:{Goalkeeping:F0} " +
                $"Ref:{Reflexes:F0} " +
                $"WF:{WeakFoot:F0}"
            );

            return sb.ToString();
        }
    }
}