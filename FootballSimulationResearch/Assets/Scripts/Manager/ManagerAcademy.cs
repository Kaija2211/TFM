using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Youth academy (career arc backlog item, floated session 8, shipped session 9) -
    // Thomas's framing: "every club gets its own private academy pool... already
    // yours." Complementary to ManagerScouting's world-scattered pool, not a
    // replacement - academy is "grew them myself," scouting is "found them abroad."
    // Scoped to the MANAGED team only (same "no AI-vs-AI activity" precedent as the
    // rest of the transfer/scouting systems - an AI club's academy has no real roster
    // interaction to make it meaningful).
    //
    // Confirmed with Thomas: 5 slots, promotion age 16, manual "Promote to Reserves"
    // action (his own stated leaning from when this was first floated - "feels more
    // like a real decision" than an automatic promotion).
    public class ManagerAcademy
    {
        public const int AcademySlots = 5;
        private const int MinAcademyAge = 14;
        private const int MaxAcademyAge = 15;
        public const int PromotionAge = 16;

        private static readonly PlayerPosition[] AcademyPositionCycle =
        {
            PlayerPosition.GK, PlayerPosition.CB, PlayerPosition.CM, PlayerPosition.ST, PlayerPosition.RW
        };

        private List<PlayerAgent> academyPool;

        // Focus stats (backlog item floated session 9, shipped session 10) - pick up to
        // 3 attributes per prospect to double their growth rate (see
        // ManagerPlayerDevelopment.ApplySeasonProgression's focusAttributes parameter).
        // Keyed by PlayerAgent reference, same "new Manager-only per-player state lives
        // alongside the system that owns it" pattern as everywhere else in this file -
        // not persisted through save/load (same already-precedented scope limit as the
        // OVR delta badge: a fresh "nothing picked yet" state after loading a career is
        // an acceptable, low-stakes gap here, not worth a new save DTO field for).
        private const int MaxFocusAttributes = 3;
        private readonly Dictionary<PlayerAgent, List<string>> focusAttributesByProspect = new();

        public IReadOnlyList<string> GetFocusAttributes(PlayerAgent prospect)
        {
            return focusAttributesByProspect.TryGetValue(prospect, out List<string> focus)
                ? focus
                : (IReadOnlyList<string>)System.Array.Empty<string>();
        }

        // Silently no-ops once 3 are already picked, rather than evicting the oldest
        // choice to make room - an explicit deselect-then-reselect is a clearer,
        // more deliberate action for the player than a picker that quietly bumps a
        // prior choice they didn't ask to remove.
        public void ToggleFocusAttribute(PlayerAgent prospect, string attributeName)
        {
            if (!focusAttributesByProspect.TryGetValue(prospect, out List<string> focus))
            {
                focus = new List<string>();
                focusAttributesByProspect[prospect] = focus;
            }

            if (focus.Contains(attributeName))
            {
                focus.Remove(attributeName);
            }
            else if (focus.Count < MaxFocusAttributes)
            {
                focus.Add(attributeName);
            }
        }

        // Restricted to the attributes ManagerPlayerDevelopment's own growth pool
        // actually touches (see GrowOutfieldAttributes/GrowGoalkeeperAttributes) -
        // Leadership/FreeKicks/WeakFoot etc. are inert generated traits that growth
        // ticks never move at all, so offering them as a "focus" pick would silently
        // do nothing.
        public static readonly string[] OutfieldFocusableAttributes =
        {
            "Finishing", "Passing", "Dribbling", "Crossing", "Heading", "LongShots",
            "ThroughBalls", "Creativity", "Positioning", "Composure", "OffTheBall",
            "Defending", "Tackling", "Marking", "Pace", "Strength", "Stamina", "Aerial"
        };

        public static readonly string[] GoalkeeperFocusableAttributes =
        {
            "Goalkeeping", "Reflexes", "Positioning", "Composure", "Passing"
        };

        public static string[] GetFocusableAttributes(PlayerPosition position)
        {
            return position == PlayerPosition.GK ? GoalkeeperFocusableAttributes : OutfieldFocusableAttributes;
        }

        public List<PlayerAgent> GetOrCreateAcademyPool(AgentSquadGenerator generator, float attackStrength, float defenceStrength)
        {
            if (academyPool != null)
            {
                return academyPool;
            }

            academyPool = new List<PlayerAgent>();

            for (int i = 0; i < AcademySlots; i++)
            {
                PlayerPosition position = AcademyPositionCycle[i % AcademyPositionCycle.Length];
                int age = Random.Range(MinAcademyAge, MaxAcademyAge + 1);

                // Softer than even ManagerScouting's youth pool (0.6-0.78x for age 16-19)
                // - a genuine 14-15-year-old academy kid is a much rawer prospect than a
                // scouted 16-19-year-old. DefenceStrength divided rather than multiplied
                // for the same reason as everywhere else this discount pattern is used -
                // see feedback_defencestrength_inverted in memory.
                float ageSpan = Mathf.Max(1, MaxAcademyAge - MinAcademyAge);
                float ageDiscount = Mathf.Lerp(0.4f, 0.5f, (age - MinAcademyAge) / ageSpan);

                PlayerAgent prospect = generator.GenerateReservePlayer(position, attackStrength * ageDiscount, defenceStrength / ageDiscount);
                ApplyAcademyAgeAndPotential(prospect, age);

                academyPool.Add(prospect);
            }

            return academyPool;
        }

        public bool CanPromote(PlayerAgent player)
        {
            return player.Age >= PromotionAge;
        }

        public bool TryPromoteToReserves(PlayerAgent player)
        {
            if (academyPool == null || !academyPool.Contains(player) || player.Age < PromotionAge)
            {
                return false;
            }

            academyPool.Remove(player);
            return true;
        }

        // Season rollover ages every academy kid the same way every other pool does -
        // called from ManagerPrototypeController alongside the reserve pool/scouting
        // pool aging, using ManagerPlayerDevelopment.ApplySeasonProgression unchanged
        // (reuses the existing Potential/growth system exactly as agreed, so academy
        // kids visibly grow before they're even promotion-eligible).
        public IReadOnlyList<PlayerAgent> GetAcademyPoolForAging()
        {
            return academyPool ?? (IReadOnlyList<PlayerAgent>)System.Array.Empty<PlayerAgent>();
        }

        // Save/load restoration - same pattern as ManagerScouting.RestoreYouthPool.
        public void RestoreAcademyPool(List<PlayerAgent> pool)
        {
            academyPool = pool;
        }

        public void Clear()
        {
            academyPool = null;
        }

        // Mirrors ManagerScouting.ApplyProspectAgeAndPotential's bell-curve headroom
        // shape (see feedback_generation_bell_curve_not_hard_range in memory) - even
        // wider than the scouting pool's, since an academy kid is the rawest,
        // furthest-from-the-ceiling prospect in the whole game.
        private static void ApplyAcademyAgeAndPotential(PlayerAgent prospect, int age)
        {
            prospect.Age = age;

            float currentOverall = prospect.GetOverallRating();
            float headroomRoll = RollHeadroom(-5f, 30f);

            prospect.Potential = Mathf.Clamp(currentOverall + headroomRoll, currentOverall, 99f);
        }

        private static float RollHeadroom(float min, float max)
        {
            float mean = (min + max) / 2f;
            float stdDev = (max - min) / 4f;
            float u1 = 1f - Random.value;
            float u2 = 1f - Random.value;
            float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return mean + (stdDev * standardNormal);
        }
    }
}
