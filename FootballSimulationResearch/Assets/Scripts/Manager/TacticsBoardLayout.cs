using System.Collections.Generic;
using UnityEngine;
using Sim;

namespace Manager
{
    // Pin coordinates for the Tactics Board's pitch view, one entry per formation,
    // in the exact same order as AgentSquadGenerator.GetStartingPositions(formation)
    // returns - PinPositions(formation)[i] is where StartingEleven[i] renders.
    //
    // Coordinates are lifted directly from the Claude Design mockup ("Football
    // Manager UI Concepts.dc.html", SQUAD - TACTICS BOARD panels) as (x, topPercent)
    // pairs matching that file's own CSS left:/top: percentages - topPercent follows
    // CSS convention (0 = visual top of the pitch, near the opponent's goal; 1 = visual
    // bottom, GK's own goal). BuildPitchPin converts that to Unity's bottom-up anchor
    // fraction (anchorY = 1 - topPercent) rather than storing pre-converted values, so
    // this table stays a direct, auditable copy of the source design's own numbers -
    // ONE deliberate deviation: every formation's GK is nudged from the source's 0.90
    // to 0.95. The mockup's own pitch region was authored ~600px tall; ours tops out
    // around 350-400px once the header/bench bands are accounted for (960x540 canvas,
    // landscape, much wider/flatter than the mockup's own panel), so the source's 10%
    // GK-to-back-line gap compresses into a real label overlap here (confirmed live)
    // that BuildTacticsBoardPin's vertical compression factor alone couldn't fully
    // resolve. Pushing GK toward the pitch's bottom edge buys back separation without
    // touching every other position's already-working spacing.
    public static class TacticsBoardLayout
    {
        private static readonly Dictionary<Formation, Vector2[]> Pins = new()
        {
            // GK, RB, CB, CB, LB, DM, CM, CM, RW, ST, LW
            [Formation.FourThreeThree] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.85f, 0.72f),
                new Vector2(0.65f, 0.76f),
                new Vector2(0.35f, 0.76f),
                new Vector2(0.15f, 0.72f),
                new Vector2(0.50f, 0.55f),
                new Vector2(0.30f, 0.46f),
                new Vector2(0.70f, 0.48f),
                new Vector2(0.82f, 0.22f),
                new Vector2(0.50f, 0.16f),
                new Vector2(0.18f, 0.22f),
            },

            // GK, RB, CB, CB, LB, DM, DM, RW, AM, LW, ST
            [Formation.FourTwoThreeOne] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.85f, 0.72f),
                new Vector2(0.65f, 0.76f),
                new Vector2(0.35f, 0.76f),
                new Vector2(0.15f, 0.72f),
                new Vector2(0.35f, 0.58f),
                new Vector2(0.65f, 0.58f),
                new Vector2(0.82f, 0.35f),
                new Vector2(0.50f, 0.32f),
                new Vector2(0.18f, 0.35f),
                new Vector2(0.50f, 0.14f),
            },

            // GK, RB, CB, CB, LB, RM, CM, CM, LM, ST, ST
            [Formation.FourFourTwo] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.85f, 0.72f),
                new Vector2(0.65f, 0.76f),
                new Vector2(0.35f, 0.76f),
                new Vector2(0.15f, 0.72f),
                new Vector2(0.85f, 0.48f),
                new Vector2(0.38f, 0.50f),
                new Vector2(0.62f, 0.50f),
                new Vector2(0.15f, 0.48f),
                new Vector2(0.38f, 0.18f),
                new Vector2(0.62f, 0.18f),
            },

            // GK, CB, CB, CB, RWB, CM, DM, CM, LWB, ST, ST
            [Formation.ThreeFiveTwo] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.70f, 0.76f),
                new Vector2(0.50f, 0.80f),
                new Vector2(0.30f, 0.76f),
                new Vector2(0.88f, 0.50f),
                new Vector2(0.32f, 0.53f),
                new Vector2(0.50f, 0.56f),
                new Vector2(0.68f, 0.53f),
                new Vector2(0.12f, 0.50f),
                new Vector2(0.38f, 0.18f),
                new Vector2(0.62f, 0.18f),
            },

            // GK, CB, CB, CB, RM, CM, CM, LM, RW, ST, LW
            [Formation.ThreeFourThree] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.70f, 0.76f),
                new Vector2(0.50f, 0.80f),
                new Vector2(0.30f, 0.76f),
                new Vector2(0.85f, 0.50f),
                new Vector2(0.38f, 0.52f),
                new Vector2(0.62f, 0.52f),
                new Vector2(0.15f, 0.50f),
                new Vector2(0.82f, 0.20f),
                new Vector2(0.50f, 0.15f),
                new Vector2(0.18f, 0.20f),
            },

            // GK, CB, CB, CB, LM, CM, CM, RM, AM, AM, ST
            [Formation.ThreeFourTwoOne] = new[]
            {
                new Vector2(0.50f, 0.95f),
                new Vector2(0.70f, 0.76f),
                new Vector2(0.50f, 0.80f),
                new Vector2(0.30f, 0.76f),
                new Vector2(0.15f, 0.50f),
                new Vector2(0.38f, 0.52f),
                new Vector2(0.62f, 0.52f),
                new Vector2(0.85f, 0.50f),
                new Vector2(0.35f, 0.28f),
                new Vector2(0.65f, 0.28f),
                new Vector2(0.50f, 0.13f),
            },
        };

        public static IReadOnlyList<Vector2> GetPins(Formation formation)
        {
            return Pins.TryGetValue(formation, out Vector2[] pins) ? pins : Pins[Formation.FourTwoThreeOne];
        }

        public static string FormatFormation(Formation formation)
        {
            return formation switch
            {
                Formation.FourThreeThree => "4-3-3",
                Formation.FourTwoThreeOne => "4-2-3-1",
                Formation.FourFourTwo => "4-4-2",
                Formation.ThreeFiveTwo => "3-5-2",
                Formation.ThreeFourThree => "3-4-3",
                Formation.ThreeFourTwoOne => "3-4-2-1",
                _ => formation.ToString()
            };
        }
    }
}
