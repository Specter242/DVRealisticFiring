using DV.Simulation.Controllers;
using HarmonyLib;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// The comms radio's and the console's loco start-up fill the firebox with
    /// a huge coal delivery and light it. On a replaced firebox that would be
    /// a box of green coal smothering the fire, so once the start-up has run,
    /// lay the ready fire it stands for instead.
    /// </summary>
    [HarmonyPatch(typeof(StartupHelper), nameof(StartupHelper.Startup))]
    internal static class StartupPatch
    {
        private static void Postfix(TrainCar loco)
        {
            // Ready as from the shed: fresh water and a sound plug.
            if (BoilerWaterRig.TryGet(loco, out var water))
                water.Washout();
            if (FireboxRegistry.TryGet(loco, out var firebox))
                firebox.PrepareFire();
        }
    }
}
