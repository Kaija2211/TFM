using System.Collections.Generic;
using System.Text;

namespace Sim
{
    public class PlayerAgent
    {
        public string Name;
        public int Age;
        public float Height;

        // Manager Mode career-arc additions (progression/transfers/save-load) - stable
        // identity for a player across seasons and save files (Name alone can collide -
        // see the lastNames pool-size comment in AgentSquadGenerator), and a hidden
        // growth ceiling GetOverallRating() can climb toward over a career. Both inert,
        // generated once and read elsewhere - Potential only ever mutates existing
        // attribute fields towards itself, never itself.
        public string PlayerId;
        public float Potential;
        public string Archetype;

        // Attribute schema v2. Legacy saves omit this field (JsonUtility supplies 0),
        // allowing PlayerAttributeModel to reconstruct the richer profile from the
        // original attributes without invalidating an existing career.
        public int AttributeSchemaVersion;

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
        public float Leadership;

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

        // Technical detail (v2)
        public float FirstTouch;
        public float Technique;
        public float Corners;
        public float Penalties;

        // Mental detail (v2)
        public float Anticipation;
        public float Decisions;
        public float Vision;
        public float DefensivePositioning;
        public float WorkRate;
        public float Aggression;

        // Physical detail (v2)
        public float Acceleration;
        public float Agility;
        public float Balance;
        public float JumpingReach;

        // Goalkeeping detail (v2)
        public float Handling;
        public float OneOnOnes;
        public float AerialCommand;
        public float Distribution;
        public float GoalkeeperPositioning;

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
            return PlayerAttributeModel.CalculateOverall(this);
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
