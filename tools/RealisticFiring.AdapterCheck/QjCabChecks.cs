using System;
using System.Linq;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;

namespace RealisticFiring.AdapterCheck
{
    internal static class QjCabChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var wheels = QjStokerCab.Wheels;
            check(wheels.Count == 6 && wheels.Select(w => w.Holder).Distinct().Count() == 6
                  && wheels.Select(w => w.Port).Distinct().Count() == 6
                  && wheels.Select(w => w.Jet).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 6)),
                "QJ bank preserves six distinct native functions and provides each of the six stoker jets once");
            var expected = new[]
            {
                ("Blowdown", "blowdown.EXT_IN", StokerJetControls.Master.Id),
                ("Dynamo", "dynamoControl.EXT_IN", "rfStokerJetLeftBack"),
                ("Injector", "injector.EXT_IN", "rfStokerJetRightBack"),
                ("Compressor", "compressorControl.EXT_IN", "rfStokerJetLeftFront"),
                ("Stoker", "stokerControl.EXT_IN", "rfStokerJetRightFront"),
                ("Blower", "blower.EXT_IN", "rfStokerJetFines"),
            };
            check(expected.All(e => wheels.Any(w => w.Holder == e.Item1 && w.Port == e.Item2 && StokerJetControls.Valves[w.Jet].Id == e.Item3)),
                "QJ triangle reads as the grate: master at the top middle, front outer pair, back middle pair and fines below");
            var overhead = wheels.Where(w => w.Seat == QjStokerCab.ControlSeat.Overhead).ToArray();
            check(overhead.Length == 4 && overhead.All(w => overhead.Where(other => other.Holder != w.Holder)
                      .All(other => (w.Overhead - other.Overhead).sqrMagnitude >= 0.14f * 0.14f)),
                "QJ four auxiliary overhead valve centers have at least 14 cm separation");
            check(wheels.Single(w => w.Holder == "Stoker").Seat == QjStokerCab.ControlSeat.Stoker
                  && wheels.Single(w => w.Holder == "Injector").Seat == QjStokerCab.ControlSeat.FeedPump,
                "QJ stoker engine and spare injector wheel belong to the fireman's stoker and feed-pump controls");
            check(QjStokerCab.Livery == "S2102-870" && QjStokerCab.NativeBank == "Controls/fireman/",
                "QJ cab rearrangement is restricted to the inspected S2102-870 fireman-side bank");
        }
    }
}
