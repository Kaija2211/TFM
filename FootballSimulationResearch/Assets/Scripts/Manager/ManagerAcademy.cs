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
        public const int AcademySlots = 11;
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
            "Finishing", "FirstTouch", "Passing", "Technique", "Dribbling", "Crossing",
            "Heading", "LongShots", "Tackling", "Marking", "Anticipation", "Decisions",
            "Composure", "Vision", "OffTheBall", "DefensivePositioning", "WorkRate",
            "Acceleration", "Pace", "Agility", "Balance", "Strength", "Stamina", "JumpingReach"
        };

        public static readonly string[] GoalkeeperFocusableAttributes =
        {
            "Handling", "Reflexes", "OneOnOnes", "AerialCommand", "Distribution",
            "GoalkeeperPositioning", "Decisions", "Composure", "Passing"
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
                academyPool.Add(GenerateProspect(i, generator, attackStrength, defenceStrength));
            }

            return academyPool;
        }

        // Shared by the initial pool fill above and ReleaseProspect below (backlog item
        // 8, session 11) - refactored out so the two generation paths can't drift apart,
        // same precedent as ManagerScouting.GenerateProspect.
        private PlayerAgent GenerateProspect(int slotIndex, AgentSquadGenerator generator, float attackStrength, float defenceStrength)
        {
            PlayerPosition position = AcademyPositionCycle[slotIndex % AcademyPositionCycle.Length];
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

            return prospect;
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

        // Manual release (backlog item 8, session 11; leaves a genuinely empty slot as
        // of session 13). Unlike promotion, which deliberately shrinks the pool
        // permanently (a graduated prospect earned their spot on the real squad),
        // releasing frees the slot for a manual "bring in a scouted player" action
        // (see PlaceProspectInSlot) rather than auto-backfilling with a fresh random
        // kid - Thomas's own call, session 13: an empty slot should be a deliberate
        // decision to fill, not something the game quietly does for you.
        public bool ReleaseProspect(PlayerAgent player)
        {
            if (academyPool == null)
            {
                return false;
            }

            int index = academyPool.IndexOf(player);

            if (index < 0)
            {
                return false;
            }

            // Same reasoning as ManagerScouting's poach-timer clearing a claimed
            // prospect's tracked state - the eventual replacement (if any) is a
            // genuinely new, unrelated PlayerAgent instance, so any focus picks tied to
            // the released player's specific object reference would otherwise leak
            // forever.
            focusAttributesByProspect.Remove(player);

            academyPool[index] = null;
            return true;
        }

        public bool HasEmptySlot()
        {
            if (academyPool == null) return false;
            foreach (PlayerAgent p in academyPool) if (p == null) return true;
            return false;
        }

        public IReadOnlyList<int> GetEmptySlotIndices()
        {
            List<int> empty = new List<int>();
            if (academyPool == null) return empty;

            for (int i = 0; i < academyPool.Count; i++)
            {
                if (academyPool[i] == null) empty.Add(i);
            }

            return empty;
        }

        // Fills an empty slot with an already-generated prospect from elsewhere (the
        // ManagerScouting discovery list, session 13's mission rework) rather than
        // generating a fresh one here - the caller is responsible for removing the
        // prospect from wherever it came from (see ManagerScouting.
        // RemoveDiscoveredProspect), this method only owns the academy side of the move.
        public bool PlaceProspectInSlot(int slotIndex, PlayerAgent prospect)
        {
            if (academyPool == null || slotIndex < 0 || slotIndex >= academyPool.Count || academyPool[slotIndex] != null)
            {
                return false;
            }

            academyPool[slotIndex] = prospect;
            return true;
        }

        // Season rollover ages every academy kid the same way every other pool does -
        // called from ManagerPrototypeController alongside the reserve pool/scouting
        // pool aging, using ManagerPlayerDevelopment.ApplySeasonProgression unchanged
        // (reuses the existing Potential/growth system exactly as agreed, so academy
        // kids visibly grow before they're even promotion-eligible). Empty slots
        // (session 13) are filtered out here - aging/progression/save only ever care
        // about real prospects, not the gaps between them.
        public IReadOnlyList<PlayerAgent> GetAcademyPoolForAging()
        {
            if (academyPool == null) return System.Array.Empty<PlayerAgent>();

            List<PlayerAgent> filled = new List<PlayerAgent>();
            foreach (PlayerAgent p in academyPool) if (p != null) filled.Add(p);
            return filled;
        }

        // Positional view INCLUDING empty (null) slots - used by the UI to render every
        // slot in order, and distinct from GetAcademyPoolForAging above precisely
        // because save/load and the UI both need to know WHICH index is empty, not just
        // how many real prospects exist.
        public IReadOnlyList<PlayerAgent> GetFullAcademySlots()
        {
            return academyPool ?? (IReadOnlyList<PlayerAgent>)System.Array.Empty<PlayerAgent>();
        }

        // Save/load restoration - same pattern as ManagerScouting.RestoreDiscoveredProspects.
        // Nulls in the incoming list are preserved as empty slots (see
        // ManagerSaveData.AcademySlots' own comment for how null is represented there).
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
