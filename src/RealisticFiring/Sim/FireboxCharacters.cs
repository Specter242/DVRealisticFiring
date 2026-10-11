using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class FireboxCharacters
    {
        internal sealed class Firebox
        {
            public Firebox(string summary, GrateCharacter grate)
            {
                Summary = summary;
                Grate = grate;
            }

            /// <summary>One line for the log: what the loco's firebox is like.</summary>
            public string Summary { get; }

            public GrateCharacter Grate { get; }

            /// <summary>The prototype's grate slope in degrees (negative falling to the front), used when the coal pile measures level.</summary>
            public float? PrototypeSlope { get; set; }

            /// <summary>The stoker's back lanes against a typical stoker's: under 1, its back corners run short.</summary>
            public float StokerBackLanes { get; set; } = 1f;

            /// <summary>How much more lump coal the stoker sends left than right.</summary>
            public float StokerLumpsLeft { get; set; }
        }

        // A measured slope this close to level is a coal pile modeled flat.
        private const float LevelSlope = 1f;

        private static readonly Firebox Typical = new Firebox("a typical firebox", GrateCharacter.Typical);

        private static readonly Dictionary<string, Firebox> ByLivery = new Dictionary<string, Firebox>
        {
            // No prototype (Altfuture: "all ... locomotives in DV are fictive");
            // taken as a light Mikado's wide firebox over the trailing truck,
            // with a short combustion chamber and a brick arch (USRA Light
            // Mikado, RME Aug. 1918). Its grate slopes 6.9° to the front, as
            // measured from its coal pile, so the fire creeps forward off the
            // back corners at speed.
            ["LocoS282A"] = new Firebox("a light Mikado's wide box: short chamber and arch; the fire creeps forward off the back corners",
                new GrateCharacter { OverFireMixing = 0.95f }),

            // A small switcher's narrow, level grate between the frames
            // (the USATC S100 the community likens it to: 19 sq ft, hand
            // fired). No combustion chamber: it draws evenly, end to end,
            // and smokes readily after firing (inferred).
            ["LocoS060"] = new Firebox("a switcher's small, narrow box: draws evenly, smokes readily",
                new GrateCharacter { BackRowDraft = 0.96f, FrontRowDraft = 1.04f, SideDraft = 1.04f, OverFireMixing = 0.8f }),

            // Lilla and Cruzer's fictional 1897-1912 shunter. Its modeled
            // grate is narrow, long, and level (0.897 by 1.593 m). A modest
            // pull toward the tubes and limited over-fire mixing give it
            // a hand-fired small-box character. These are inferred firing
            // traits, not documented fittings or a historical prototype.
            ["Jk3A"] = new Firebox("a small, long, level box: modest pull to the front; fresh rounds smoke readily",
                new GrateCharacter { BackRowDraft = 0.94f, FrontRowDraft = 1.08f, SideDraft = 1.03f, OverFireMixing = 0.85f }),

            // NYC J-3a (RME May 1938): a 43-in combustion chamber, four arch
            // tubes, four combustion tubes through each side sheet; the mud
            // ring 16 in higher at the back, about 7°. Standard HT stoker: the
            // screw sends fines right and lumps left, and the right back
            // corner is "often likely to be short of coal" (1944 New York
            // firing course pp. 45-48). Bituminous of varying quality
            // ("Indiana dirt", Hal Lewis, 1991): a little more ash and clinker.
            ["S464-730"] = new Firebox("the J-3a's box: 43-in chamber, arch tubes; slopes about 7°; the stoker runs the right back corner short",
                new GrateCharacter { ClinkerFactor = 1.1f, AshFactor = 1.1f })
            {
                PrototypeSlope = -7f,
                StokerLumpsLeft = 0.1f,
            },

            // UP FEF class (RME Feb. 1938, Nov. 1939; UP's FEF-3 diagram): a
            // 90-in combustion chamber, arch tubes and circulators, twelve air
            // tubes through each side sheet, and a strong pull to the throat
            // (125 % of the gas area above the arch, heavy cinder cutting);
            // the mud ring 10 in higher at the back, about 4°. Standard BK
            // stoker, whose back corners ran short (Lewis). Rock Springs and
            // Superior coal: "produces no clinker", 3 to 5 % ash (USGS
            // Bulletin 341; USBM Bulletin 209).
            ["FEF-3"] = new Firebox("the FEF-3's box: 90-in chamber, circulators, air tubes; slopes about 4°; Rock Springs coal; the BK stoker starves the back corners",
                new GrateCharacter { BackRowDraft = 0.88f, FrontRowDraft = 1.15f, OverFireMixing = 1.25f, ClinkerFactor = 0.7f, AshFactor = 0.6f })
            {
                PrototypeSlope = -4f,
                StokerBackLanes = 0.7f,
            },

            // LittleNiko36's "Black Arrow" is an FEF-3 too, in a made-up
            // road's livery. Same box; made different (invented): that road
            // burns a typical bituminous, with typical ash and clinker, and
            // fitted the MB stoker (as UP did on some FEF-2s, by Kratville),
            // which reaches the back corners.
            ["S484-840A"] = new Firebox("an FEF-3's box on another road: typical bituminous, an MB stoker that reaches the back corners",
                new GrateCharacter { BackRowDraft = 0.88f, FrontRowDraft = 1.15f, OverFireMixing = 1.25f })
            {
                PrototypeSlope = -4f,
            },
        };

        public static Firebox For(TrainCar car) =>
            ForLivery(car != null && car.carLivery != null ? car.carLivery.id : null);

        internal static Firebox ForLivery(string livery) =>
            livery != null && ByLivery.TryGetValue(livery, out var firebox) ? firebox : Typical;

        /// <summary>The grate's slope: as measured from the coal pile, or the prototype's where the pile is modeled level.</summary>
        public static float Slope(TrainCar car, float measured)
        {
            var prototype = For(car).PrototypeSlope;
            return prototype.HasValue && System.Math.Abs(measured) < LevelSlope ? prototype.Value : measured;
        }
    }
}
