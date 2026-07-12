namespace Sim
{
    public class PlayerAgent
    {
        public string Name;
        public PlayerRole Role;

        public float Finishing;
        public float Creativity;
        public float Defending;
        public float Goalkeeping;
        public float Stamina;

        public PlayerAgent(
            string name,
            PlayerRole role,
            float finishing,
            float creativity,
            float defending,
            float goalkeeping,
            float stamina)
        {
            Name = name;
            Role = role;
            Finishing = finishing;
            Creativity = creativity;
            Defending = defending;
            Goalkeeping = goalkeeping;
            Stamina = stamina;
        }

        public override string ToString()
        {
            return
                $"{Name} ({Role}) " +
                $"Fin:{Finishing:F1} " +
                $"Cre:{Creativity:F1} " +
                $"Def:{Defending:F1} " +
                $"GK:{Goalkeeping:F1} " +
                $"Sta:{Stamina:F1}";
        }
    }
}