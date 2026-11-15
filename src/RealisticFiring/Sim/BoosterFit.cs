using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The locos fitted with a trailing-truck booster, as their prototypes
    /// were, and the booster's cab controls.
    /// </summary>
    internal static class BoosterFit
    {
        public const string BoosterId = "rfBooster";

        /// <summary>The booster latch: down 0, raised 1.</summary>
        public static readonly StokerJetControls.Valve Latch = new StokerJetControls.Valve("rfBoosterLatch", "Booster latch", null, 0f);

        /// <summary>The idling valve: running 0, idling 1.</summary>
        public static readonly StokerJetControls.Valve Idle = new StokerJetControls.Valve("rfBoosterIdle", "Booster idling valve", null, 0f);

        internal sealed class Fit
        {
            public Fit(float wheelDiameter, float trailerLoad, int drivingAxles, bool upgrade = false)
            {
                WheelDiameter = wheelDiameter;
                TrailerLoad = trailerLoad;
                DrivingAxles = drivingAxles;
                Upgrade = upgrade;
            }

            /// <summary>Bought at a career manager (<see cref="Upgrades.Fittings.Booster"/>): built in, idle until fitted.</summary>
            public bool Upgrade { get; }

            /// <summary>The trailer wheels the booster drives, m.</summary>
            public float WheelDiameter { get; }

            /// <summary>Weight on the booster axle, kg.</summary>
            public float TrailerLoad { get; }

            /// <summary>Driving axles, without the booster's.</summary>
            public int DrivingAxles { get; }
        }

        private static readonly Dictionary<string, Fit> Fits = new Dictionary<string, Fit>
        {
            // NYC J-3a: a Franklin C-2 on the 51-in rear trailer wheels, whose
            // axle carries 55,500 lb; three driving axles (official NYC diagram).
            ["S464-730"] = new Fit(1.2954f, 25174f, 3),
            ["LocoS282A"] = new Fit(1.0922f, 20412f, 4, upgrade: true),
        };

        public static bool TryGet(string liveryId, out Fit fit)
        {
            fit = null;
            if (liveryId == null || !Fits.TryGetValue(liveryId, out var found))
                return false;
            if (!(found.Upgrade ? Main.Session.S282Booster : Main.Session.FitBoosters))
                return false;
            fit = found;
            return true;
        }

        public static BoosterSpec SpecFor(TrainCar car)
        {
            var spec = new BoosterSpec();
            if (car != null && TryGet(car.carLivery.id, out var fit))
                spec.WheelDiameter = fit.WheelDiameter;
            return spec;
        }
    }
}
