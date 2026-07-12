using UnityEngine;

namespace Sim
{
    public class AgentSquadGenerator
    {
        public AgentTeam GenerateSquad(string teamName, float attackStrength, float defenceStrength)
        {
            AgentTeam team = new AgentTeam(teamName);

            AddPlayer(team, "Goalkeeper 1", PlayerRole.Goalkeeper, attackStrength, defenceStrength);

            for (int i = 1; i <= 4; i++)
            {
                AddPlayer(team, $"Defender {i}", PlayerRole.Defender, attackStrength, defenceStrength);
            }

            for (int i = 1; i <= 3; i++)
            {
                AddPlayer(team, $"Midfielder {i}", PlayerRole.Midfielder, attackStrength, defenceStrength);
            }

            for (int i = 1; i <= 3; i++)
            {
                AddPlayer(team, $"Forward {i}", PlayerRole.Forward, attackStrength, defenceStrength);
            }

            return team;
        }

        private void AddPlayer(
            AgentTeam team,
            string playerName,
            PlayerRole role,
            float attackStrength,
            float defenceStrength)
        {
            float baseQuality = 50f;

            float finishing = baseQuality;
            float creativity = baseQuality;
            float defending = baseQuality;
            float goalkeeping = 10f;
            float stamina = Random.Range(55f, 85f);

            if (role == PlayerRole.Goalkeeper)
            {
                goalkeeping = Random.Range(60f, 85f) / defenceStrength;
                defending = Random.Range(35f, 55f) / defenceStrength;
                finishing = Random.Range(1f, 10f);
                creativity = Random.Range(10f, 30f);
            }
            else if (role == PlayerRole.Defender)
            {
                defending = Random.Range(55f, 80f) / defenceStrength;
                creativity = Random.Range(25f, 55f);
                finishing = Random.Range(10f, 35f);
            }
            else if (role == PlayerRole.Midfielder)
            {
                creativity = Random.Range(50f, 80f) * attackStrength;
                defending = Random.Range(35f, 65f) / defenceStrength;
                finishing = Random.Range(30f, 60f) * attackStrength;
            }
            else if (role == PlayerRole.Forward)
            {
                finishing = Random.Range(55f, 85f) * attackStrength;
                creativity = Random.Range(35f, 65f) * attackStrength;
                defending = Random.Range(10f, 35f);
            }
            finishing = Mathf.Clamp(finishing, 1f, 100f);
            creativity = Mathf.Clamp(creativity, 1f, 100f);
            defending = Mathf.Clamp(defending, 1f, 100f);
            goalkeeping = Mathf.Clamp(goalkeeping, 1f, 100f);
            stamina = Mathf.Clamp(stamina, 1f, 100f);
            
            PlayerAgent player = new PlayerAgent(
                $"{team.TeamName} {playerName}",
                role,
                finishing,
                creativity,
                defending,
                goalkeeping,
                stamina
            );


            team.AddPlayer(player);
        }
    }
}