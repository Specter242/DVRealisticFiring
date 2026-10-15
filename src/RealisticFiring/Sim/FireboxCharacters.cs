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

            /// <summary>
            /// Whether the prototype's slope stands even where the pile is
            /// tilted: a pile tilted with the sloping front of a grate that
            /// runs level at the back is steeper than the grate averages.
            /// </summary>
            public bool PrototypeSlopeOverPile { get; set; }

            /// <summary>The stoker's back lanes against a typical stoker's: under 1, its back corners run short.</summary>
            public float StokerBackLanes { get; set; } = 1f;

            /// <summary>How much more lump coal the stoker sends left than right.</summary>
            public float StokerLumpsLeft { get; set; }
        }

        // A measured slope this close to level is a coal pile modeled flat.
        private const float LevelSlope = 1f;

        private static readonly Firebox Typical = new Firebox("a typical firebox", GrateCharacter.Typical);

        // LMS Stanier 8F: a narrow Belpaire box between the frames, 28.65 sq
        // ft, with a brick arch and no combustion chamber; its grate slopes
        // at the front and runs level at the back (the Black Five's, about
        // 55:45, Rowledge & Reed 1981 pp.53-54), hand-fired, fixed bars. The
        // firehole's deflector plate turns the secondary air down toward the
        // arch (BR handbook 1957, pp.32, 46). Fire thin and often; a thick
        // fire under the door is a fault (pp.27-33). Its arch sealed to the
        // tube plate, the back draws hardest, as British rules fire it ("keep
        // the fire thickest along the back of the box, that is, under the
        // door", L&YR Questions for Enginemen, c.1910); how much harder, and
        // the mixing, are inferred, and the slope averaged over the grate. The mod's grate and pile slope about 12° down to the
        // front their whole length (2026-10-14); the prototype's sloped at the
        // front alone, so its average stands over the pile's.
        private static readonly Firebox Lms8F = new Firebox("a narrow Belpaire box: sloping front, level back; brick arch and a deflector plate",
            new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, SideDraft = 1.03f, OverFireMixing = 0.95f })
        {
            PrototypeSlope = -5f,
            PrototypeSlopeOverPile = true,
        };

        private static readonly Dictionary<string, Firebox> ByLivery = new Dictionary<string, Firebox>
        {
            ["em_lms_8f_loc_black"] = Lms8F,
            ["em_lms_8f_loc_grey"] = Lms8F,

            // No prototype (Altfuture: "all ... locomotives in DV are fictive");
            // taken as a light Mikado's wide firebox over the trailing truck,
            // with a short combustion chamber and a brick arch (USRA Light
            // Mikado, RME Aug. 1918). Its grate slopes 6.9° to the front, as
            // measured from its coal pile, so the fire creeps forward off the
            // back corners at speed. The typical draft is a Mikado's measured
            // one: a Frisco 2-8-2 with a sealed arch pulled hardest at the back
            // (Acker, RME Apr. 1930).
            ["LocoS282A"] = new Firebox("a light Mikado's wide box: short chamber and arch; draws hardest at the back, and the fire creeps forward off the back corners",
                new GrateCharacter { OverFireMixing = 0.95f }),

            // A small switcher's narrow, level grate between the frames
            // (the USATC S100 the community likens it to: 19 sq ft, hand
            // fired). No combustion chamber; a short arch: it draws nearly
            // evenly, end to end, a little harder at the back, and smokes
            // readily after firing (inferred).
            ["LocoS060"] = new Firebox("a switcher's small, narrow box: draws nearly evenly, smokes readily",
                new GrateCharacter { BackRowDraft = 1.04f, FrontRowDraft = 0.96f, SideDraft = 1.04f, OverFireMixing = 0.8f }),

            // Lilla and Cruzer's fictional 1897-1912 shunter. Its modeled
            // grate is narrow, long, and level (0.897 by 1.593 m). With no arch
            // asserted, a box burns faster at the front (Acker, RME Apr. 1940):
            // a modest pull toward the tubes and limited over-fire mixing give
            // it a hand-fired small-box character. These are inferred firing
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
            // tubes through each side sheet, and a generous way over the arch
            // (125 % of the flue gas area, heavy cinder cutting), which eases
            // the front's longer path, so it draws more evenly than a typical
            // sealed arch (inferred: a gap at the tube plate evened it fully,
            // Acker 1930); the mud ring 10 in higher at the back, about 4°. Standard BK
            // stoker, whose back corners ran short (Lewis). Rock Springs and
            // Superior coal: "produces no clinker", 3 to 5 % ash (USGS
            // Bulletin 341; USBM Bulletin 209).
            ["FEF-3"] = new Firebox("the FEF-3's box: 90-in chamber, circulators, air tubes; slopes about 4°; Rock Springs coal; the BK stoker starves the back corners",
                new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, OverFireMixing = 1.25f, ClinkerFactor = 0.7f, AshFactor = 0.6f })
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
                new GrateCharacter { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, OverFireMixing = 1.25f })
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
            var firebox = For(car);
            var prototype = firebox.PrototypeSlope;
            return prototype.HasValue && (firebox.PrototypeSlopeOverPile || System.Math.Abs(measured) < LevelSlope) ? prototype.Value : measured;
        }
    }
}
