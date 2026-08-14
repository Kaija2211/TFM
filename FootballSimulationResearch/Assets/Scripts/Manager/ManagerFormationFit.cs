using System;
using System.Collections.Generic;
using Sim;

namespace Manager
{
    // Gives formation/position choices a real mechanical consequence without touching
    // AgentMatchSimulator.SimulateMatch itself (which must stay byte-for-byte unchanged
    // for Research Mode) - builds a throwaway, fit-penalized clone of a team's starting
    // XI and hands that to the simulator instead of the real squad data. The real
    // AgentTeam/PlayerAgent objects (and everything the rest of Manager Mode reads, like
    // Player Detail) are never touched.
    //
    // PlayerAgent.GetPositionFit already existed (1.0 primary / 0.85 secondary / 0.6
    // neither) but was previously only used to rank candidates when auto-filling a
    // formation change - never actually applied to a player's output. This applies it
    // for real: every skill attribute is scaled by the player's fit for the formation
    // slot they're occupying, so a CB played at RW plays like a worse RW (and, since
    // AgentMatchSimulator's event-candidate pools always filter by the player's true
    // PrimaryPosition, still won't get picked for RW-type events at all unless nobody
    // else on the pitch qualifies - the scaling matters for the cases where they do).
    //
    // Session 7 (fitness phase) extended this same clone with an optional Condition
    // multiplier (see ManagerSquadRoles.GetConditionMultiplier) - a second, independent
    // penalty stacked on top of position-fit, same mechanism, same safety guarantee (the
    // clone is still the only thing SimulateMatch ever sees). conditionLookup is null for
    // every AI-controlled team (they have no Condition tracking at all), so this is a
    // pure no-op there - fit alone, exactly as before.
    public static class ManagerFormationFit
    {
        public static AgentTeam BuildFitAdjustedTeam(AgentTeam team, List<PlayerPosition> formationSlots, Func<PlayerAgent, float> conditionLookup = null)
        {
            AgentTeam adjusted = new AgentTeam(team.TeamName, team.Formation);

            for (int i = 0; i < team.StartingEleven.Count; i++)
            {
                PlayerAgent original = team.StartingEleven[i];
                PlayerPosition slot = i < formationSlots.Count ? formationSlots[i] : original.PrimaryPosition;
                float fit = original.GetPositionFit(slot);
                float condition = conditionLookup?.Invoke(original) ?? 1f;

                adjusted.AddStarter(ClonePenalized(original, fit * condition));
            }

            return adjusted;
        }

        // Age/Height/WeakFoot are physical/biographical facts, not context-sensitive
        // footballing output, so they're copied as-is - every other attribute is a
        // skill that should suffer when played out of position or under-conditioned.
        private static PlayerAgent ClonePenalized(PlayerAgent original, float fit)
        {
            PlayerAgent clone = new PlayerAgent(original.Name, original.Role, original.PrimaryPosition)
            {
                SecondaryPositions = new List<PlayerPosition>(original.SecondaryPositions),
                Age = original.Age,
                Height = original.Height,
                WeakFoot = original.WeakFoot,
                AttributeSchemaVersion = PlayerAttributeModel.CurrentSchemaVersion,
                Archetype = original.Archetype,

                Finishing = original.Finishing * fit,
                Passing = original.Passing * fit,
                Dribbling = original.Dribbling * fit,
                Crossing = original.Crossing * fit,
                Heading = original.Heading * fit,
                LongShots = original.LongShots * fit,
                ThroughBalls = original.ThroughBalls * fit,
                FreeKicks = original.FreeKicks * fit,

                Creativity = original.Creativity * fit,
                Positioning = original.Positioning * fit,
                Composure = original.Composure * fit,
                OffTheBall = original.OffTheBall * fit,
                Leadership = original.Leadership * fit,

                Defending = original.Defending * fit,
                Tackling = original.Tackling * fit,
                Marking = original.Marking * fit,

                Pace = original.Pace * fit,
                Strength = original.Strength * fit,
                Stamina = original.Stamina * fit,
                Aerial = original.Aerial * fit,

                Goalkeeping = original.Goalkeeping * fit,
                Reflexes = original.Reflexes * fit,

                FirstTouch = original.FirstTouch * fit,
                Technique = original.Technique * fit,
                Corners = original.Corners * fit,
                Penalties = original.Penalties * fit,
                Anticipation = original.Anticipation * fit,
                Decisions = original.Decisions * fit,
                Vision = original.Vision * fit,
                DefensivePositioning = original.DefensivePositioning * fit,
                WorkRate = original.WorkRate * fit,
                Aggression = original.Aggression * fit,
                Acceleration = original.Acceleration * fit,
                Agility = original.Agility * fit,
                Balance = original.Balance * fit,
                JumpingReach = original.JumpingReach * fit,
                Handling = original.Handling * fit,
                OneOnOnes = original.OneOnOnes * fit,
                AerialCommand = original.AerialCommand * fit,
                Distribution = original.Distribution * fit,
                GoalkeeperPositioning = original.GoalkeeperPositioning * fit
            };

            return clone;
        }
    }
}

