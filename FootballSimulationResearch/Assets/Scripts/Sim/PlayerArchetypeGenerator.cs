namespace Sim
{
    // Shapes ability without deciding its level. Club/squad calibration still owns the
    // player's overall; this pass creates recognisable trade-offs within that budget.
    public static class PlayerArchetypeGenerator
    {
        public static void AssignAndApply(PlayerAgent p)
        {
            switch (p.PrimaryPosition)
            {
                case PlayerPosition.GK:
                    Choose(p, ("Shot Stopper", ShotStopper), ("Sweeper Keeper", SweeperKeeper), ("Commanding Keeper", CommandingKeeper)); break;
                case PlayerPosition.CB:
                    Choose(p, ("Stopper", Stopper), ("Ball-Playing Defender", BallPlayingDefender), ("Cover Defender", CoverDefender)); break;
                case PlayerPosition.RB: case PlayerPosition.LB:
                case PlayerPosition.RWB: case PlayerPosition.LWB:
                    Choose(p, ("Overlapping Full-Back", OverlappingFullBack), ("Defensive Full-Back", DefensiveFullBack), ("Inverted Full-Back", InvertedFullBack)); break;
                case PlayerPosition.DM:
                    Choose(p, ("Anchor", Anchor), ("Ball Winner", BallWinner), ("Deep-Lying Playmaker", DeepPlaymaker)); break;
                case PlayerPosition.CM:
                    Choose(p, ("Box-to-Box Midfielder", BoxToBox), ("Controller", Controller), ("Advanced Playmaker", Playmaker)); break;
                case PlayerPosition.AM:
                    Choose(p, ("Creator", Playmaker), ("Shadow Striker", ShadowStriker), ("Dribbling Playmaker", DribblingPlaymaker)); break;
                case PlayerPosition.RM: case PlayerPosition.LM:
                case PlayerPosition.RW: case PlayerPosition.LW:
                    Choose(p, ("Touchline Winger", TouchlineWinger), ("Inside Forward", InsideForward), ("Wide Playmaker", WidePlaymaker)); break;
                case PlayerPosition.ST:
                    Choose(p, ("Poacher", Poacher), ("Target Forward", TargetForward), ("Complete Forward", CompleteForward), ("Pressing Forward", PressingForward)); break;
            }
            PlayerAttributeModel.ClampAll(p);
        }

        private delegate void Shape(PlayerAgent p);
        private static void Choose(PlayerAgent p, params (string name, Shape shape)[] choices)
        {
            // Generated names follow Unity's seeded generation stream; GUIDs do not.
            // Name-based selection therefore keeps world-generation audits reproducible
            // while PlayerId remains the player's save-stable identity elsewhere.
            int index = StableIndex(p.Name, choices.Length);
            p.Archetype = choices[index].name;
            choices[index].shape(p);
        }

        // Do not consume UnityEngine.Random here: that stream also drives every later
        // generated player's base attributes. A stable per-player hash gives variety
        // without archetype selection silently rerolling the rest of the league.
        private static int StableIndex(string value, int count)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash % (uint)count);
            }
        }

        private static void N(ref float value, float delta) => value += delta;
        private static void ShotStopper(PlayerAgent p) { N(ref p.Reflexes, 6); N(ref p.OneOnOnes, 5); N(ref p.Handling, 3); N(ref p.Distribution, -5); N(ref p.AerialCommand, -3); }
        private static void SweeperKeeper(PlayerAgent p) { N(ref p.Distribution, 6); N(ref p.Decisions, 4); N(ref p.GoalkeeperPositioning, 3); N(ref p.OneOnOnes, 2); N(ref p.Handling, -3); N(ref p.AerialCommand, -2); }
        private static void CommandingKeeper(PlayerAgent p) { N(ref p.AerialCommand, 7); N(ref p.Handling, 4); N(ref p.JumpingReach, 3); N(ref p.Reflexes, -3); N(ref p.Distribution, -3); }
        private static void Stopper(PlayerAgent p) { N(ref p.Tackling, 6); N(ref p.Aggression, 5); N(ref p.Strength, 4); N(ref p.Marking, 3); N(ref p.Passing, -4); N(ref p.Technique, -4); N(ref p.Pace, -2); }
        private static void BallPlayingDefender(PlayerAgent p) { N(ref p.Passing, 7); N(ref p.FirstTouch, 5); N(ref p.Technique, 4); N(ref p.Composure, 4); N(ref p.Vision, 3); N(ref p.Aggression, -4); N(ref p.Strength, -3); }
        private static void CoverDefender(PlayerAgent p) { N(ref p.Pace, 6); N(ref p.Acceleration, 5); N(ref p.Anticipation, 5); N(ref p.DefensivePositioning, 3); N(ref p.Strength, -4); N(ref p.JumpingReach, -3); }
        private static void OverlappingFullBack(PlayerAgent p) { N(ref p.Crossing, 6); N(ref p.Stamina, 5); N(ref p.Pace, 4); N(ref p.WorkRate, 4); N(ref p.OffTheBall, 3); N(ref p.Marking, -4); N(ref p.Strength, -2); }
        private static void DefensiveFullBack(PlayerAgent p) { N(ref p.Marking, 6); N(ref p.Tackling, 5); N(ref p.DefensivePositioning, 5); N(ref p.Strength, 3); N(ref p.Crossing, -5); N(ref p.Dribbling, -4); }
        private static void InvertedFullBack(PlayerAgent p) { N(ref p.Passing, 6); N(ref p.FirstTouch, 5); N(ref p.Decisions, 4); N(ref p.Vision, 3); N(ref p.Crossing, -4); N(ref p.Pace, -2); }
        private static void Anchor(PlayerAgent p) { N(ref p.DefensivePositioning, 7); N(ref p.Marking, 5); N(ref p.Anticipation, 4); N(ref p.Strength, 3); N(ref p.Dribbling, -5); N(ref p.OffTheBall, -3); }
        private static void BallWinner(PlayerAgent p) { N(ref p.Tackling, 7); N(ref p.Aggression, 7); N(ref p.WorkRate, 5); N(ref p.Stamina, 4); N(ref p.Vision, -5); N(ref p.Technique, -3); }
        private static void DeepPlaymaker(PlayerAgent p) { N(ref p.Passing, 7); N(ref p.Vision, 7); N(ref p.Decisions, 5); N(ref p.Composure, 4); N(ref p.Tackling, -4); N(ref p.Aggression, -5); }
        private static void BoxToBox(PlayerAgent p) { N(ref p.Stamina, 7); N(ref p.WorkRate, 6); N(ref p.OffTheBall, 4); N(ref p.Tackling, 3); N(ref p.Passing, 2); N(ref p.Composure, -2); }
        private static void Controller(PlayerAgent p) { N(ref p.Passing, 7); N(ref p.Decisions, 6); N(ref p.Composure, 5); N(ref p.FirstTouch, 4); N(ref p.Pace, -4); N(ref p.Acceleration, -3); }
        private static void Playmaker(PlayerAgent p) { N(ref p.Vision, 8); N(ref p.Passing, 6); N(ref p.Technique, 5); N(ref p.FirstTouch, 4); N(ref p.WorkRate, -3); N(ref p.Tackling, -4); }
        private static void ShadowStriker(PlayerAgent p) { N(ref p.Finishing, 7); N(ref p.OffTheBall, 7); N(ref p.Acceleration, 4); N(ref p.Anticipation, 4); N(ref p.Vision, -4); N(ref p.Passing, -3); }
        private static void DribblingPlaymaker(PlayerAgent p) { N(ref p.Dribbling, 8); N(ref p.Agility, 6); N(ref p.Technique, 5); N(ref p.FirstTouch, 4); N(ref p.Strength, -4); N(ref p.Tackling, -4); }
        private static void TouchlineWinger(PlayerAgent p) { N(ref p.Crossing, 8); N(ref p.Pace, 5); N(ref p.Acceleration, 5); N(ref p.Dribbling, 4); N(ref p.Finishing, -5); N(ref p.Strength, -3); }
        private static void InsideForward(PlayerAgent p) { N(ref p.Finishing, 7); N(ref p.OffTheBall, 6); N(ref p.Dribbling, 5); N(ref p.Composure, 3); N(ref p.Crossing, -6); N(ref p.WorkRate, -2); }
        private static void WidePlaymaker(PlayerAgent p) { N(ref p.Vision, 8); N(ref p.Passing, 6); N(ref p.FirstTouch, 5); N(ref p.Technique, 4); N(ref p.Pace, -5); N(ref p.Crossing, -2); }
        private static void Poacher(PlayerAgent p) { N(ref p.Finishing, 8); N(ref p.OffTheBall, 7); N(ref p.Anticipation, 6); N(ref p.Composure, 4); N(ref p.Passing, -5); N(ref p.WorkRate, -4); N(ref p.Strength, -2); }
        private static void TargetForward(PlayerAgent p) { N(ref p.Strength, 8); N(ref p.Heading, 8); N(ref p.JumpingReach, 7); N(ref p.Balance, 4); N(ref p.Pace, -6); N(ref p.Acceleration, -5); N(ref p.Dribbling, -3); }
        private static void CompleteForward(PlayerAgent p) { N(ref p.FirstTouch, 4); N(ref p.Technique, 4); N(ref p.Passing, 4); N(ref p.Finishing, 4); N(ref p.OffTheBall, 4); N(ref p.Vision, 3); N(ref p.Aggression, -3); }
        private static void PressingForward(PlayerAgent p) { N(ref p.WorkRate, 8); N(ref p.Stamina, 7); N(ref p.Aggression, 6); N(ref p.Acceleration, 4); N(ref p.Composure, -4); N(ref p.FirstTouch, -3); }
    }
}
