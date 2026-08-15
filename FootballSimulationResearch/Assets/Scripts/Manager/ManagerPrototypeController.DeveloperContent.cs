using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using Sim;
using UnityEngine;

namespace Manager
{
    public partial class ManagerPrototypeController
    {
        // Developer easter egg (Manager Mode only, purely cosmetic) - deliberately kept
        // out of AgentSquadGenerator.cs entirely, since that generator is shared with
        // Research Mode's ResearchEvaluationRunner. Special-casing anything inside the
        // generation loop itself would shift the RNG draw sequence and silently change
        // every other generated player's stats too (the same risk flagged when GK stats
        // were discussed earlier this session). Applied strictly *after* GenerateSquad
        // returns, overwriting only Name/Age/Height/nationality on one already-generated
        // player - attributes/Overall are whatever normal generation rolled, untouched
        // (Thomas, session 16: "stats can be randomized just like before, but everything
        // else is fixed"). Three more friends added this session alongside Hidde -
        // Liverpool gets two (a CB and a DM), Tottenham gets one.
        private void ApplyDeveloperEasterEggPlayer(AgentTeam team)
        {
            if (team == null)
            {
                return;
            }

            switch (team.TeamName)
            {
                case "Arsenal":
                    ApplyEasterEggIdentity(team, PlayerPosition.ST, "Hidde Rietberg", 25, 183f, "Netherlands");

                    BoostStrikerEasterEgg(team, "Hidde Rietberg");
                    break;

                case "Liverpool":
                    ApplyEasterEggIdentity(team, PlayerPosition.CB, "Thomas Bernards", 25, 200f, "Germany");
                    ApplyEasterEggIdentity(team, PlayerPosition.DM, "Charles Herring", 25, 175f, "England");

                    BoostDefensiveMidfielderEasterEgg(team, "Charles Herring");
                    break;

                case "Tottenham Hotspur":
                    ApplyEasterEggIdentity(team, PlayerPosition.ST, "Victor Hamberg", 26, 195f, "Sweden");


                    BoostStrikerEasterEgg(team, "Victor Hamberg");
                    break;
            }
        }

        private void ApplyEasterEggIdentity(
            AgentTeam team,
            PlayerPosition position,
            string name,
            int age,
            float height,
            string nationName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent target = team.StartingEleven.Find(p => p.PrimaryPosition == position)
                ?? team.Bench.Find(p => p.PrimaryPosition == position);

            if (target == null)
            {
                return;
            }

            target.Name = name;
            target.Age = age;
            target.Height = height;
            ManagerPlayerNationality.SetNationality(
                target,
                new ManagerPlayerNationality.Nation(nationName, "Western Europe")
            );
        }

        private void BoostStrikerEasterEgg(AgentTeam team, string playerName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent player = team.Players.Find(p => p.Name == playerName);

            if (player == null)
            {
                return;
            }

            player.Finishing = Mathf.Max(player.Finishing, 88f);
            player.Pace = Mathf.Max(player.Pace, 85f);
            player.Dribbling = Mathf.Max(player.Dribbling, 84f);
            player.Composure = Mathf.Max(player.Composure, 82f);
            player.Positioning = Mathf.Max(player.Positioning, 81f);
            player.Heading = Mathf.Max(player.Heading, 87f);
            player.Strength = Mathf.Max(player.Strength, 78f);
            player.Aerial = Mathf.Max(player.Aerial, 82f);

            // These bespoke clamps were authored against the original attribute set;
            // rebuild the detailed profile so they remain real strengths under v2.
            player.AttributeSchemaVersion = 0;
            PlayerAttributeModel.EnsureCurrent(player);


            // Make sure the boost does not leave him with no development room.
            player.Potential = Mathf.Max(player.Potential, player.GetOverallRating() + 3f);
        }

        private void BoostDefensiveMidfielderEasterEgg(AgentTeam team, string playerName)
        {
            if (team == null)
            {
                return;
            }

            PlayerAgent player = team.Players.Find(p => p.Name == playerName);

            if (player == null)
            {
                return;
            }

            player.Passing = Mathf.Max(player.Passing, 82f);
            player.Positioning = Mathf.Max(player.Positioning, 82f);
            player.Composure = Mathf.Max(player.Composure, 81f);
            player.Defending = Mathf.Max(player.Defending, 81f);
            player.Tackling = Mathf.Max(player.Tackling, 81f);
            player.Marking = Mathf.Max(player.Marking, 80f);
            player.Stamina = Mathf.Max(player.Stamina, 83f);
            player.Strength = Mathf.Max(player.Strength, 76f);
            player.ThroughBalls = Mathf.Max(player.ThroughBalls, 78f);
            player.LongShots = Mathf.Max(player.LongShots, 81f);
            player.Dribbling = Mathf.Max(player.Dribbling, 85f);
            player.Pace = Mathf.Max(player.Pace, 77f);
            player.FreeKicks = Mathf.Max(player.FreeKicks, 89f);

            player.AttributeSchemaVersion = 0;
            PlayerAttributeModel.EnsureCurrent(player);

            // Make sure the boost does not leave him with no development room.
            player.Potential = Mathf.Max(player.Potential, player.GetOverallRating() + 3f);
        }
    }
}
