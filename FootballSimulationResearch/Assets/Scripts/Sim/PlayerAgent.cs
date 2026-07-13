using System.Collections.Generic;
using System.Text;

namespace Sim
{
    public class PlayerAgent
    {
        public string Name;

        public PlayerRole Role;
        public PlayerPosition PrimaryPosition;
        public List<PlayerPosition> SecondaryPositions = new();

        // Technical
        public float Finishing;
        public float Passing;
        public float Dribbling;
        public float Crossing;
        public float Heading;

        // Creative / mental
        public float Creativity;
        public float Positioning;
        public float Composure;

        // Defensive
        public float Defending;
        public float Tackling;

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

            return 0.60f;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($"{Name} ({PrimaryPosition}) ");

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