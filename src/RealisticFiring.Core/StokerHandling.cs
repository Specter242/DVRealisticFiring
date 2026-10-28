using System;

namespace RealisticFiring.Core
{
    /// <summary>Additional, optional saved state; old stokers start empty and unpacked.</summary>
    public struct StokerState
    {
        public float TableCoal, TableFines, TableWater, TableWarmth, TableCake, Packing;
        public float EngineSpeed, Reversed, TurnsToClear;
        public bool Jammed, PackingJam, TableNeedsClearing;
    }

    /// <summary>Fuel mass, its actual fine-coal component, and rain water beyond delivered coal.</summary>
    internal struct StokerCoal
    {
        internal float Coal, Fines, Water;
        internal float FineShare => Coal > 1e-7f ? Fines / Coal : 0f;
        internal float Moisture => Coal > 1e-7f ? MoistureFromWater(Water / Coal) : CoalMoisture.Delivered;
        internal static float RainWater(float moisture) => Math.Max(0f,
            CoalMoisture.WaterPerKg(moisture) - CoalMoisture.WaterPerKg(CoalMoisture.Delivered));
        private static float MoistureFromWater(float water)
        {
            var ratio = water + CoalMoisture.WaterPerKg(CoalMoisture.Delivered);
            return ratio / (1f + ratio);
        }
        internal StokerCoal Scale(float share) => new StokerCoal { Coal = Coal * share, Fines = Fines * share, Water = Water * share };
        internal static StokerCoal Add(StokerCoal a, StokerCoal b) => new StokerCoal
            { Coal = a.Coal + b.Coal, Fines = a.Fines + b.Fines, Water = a.Water + b.Water };
    }

    /// <summary>
    /// The screw's packing and the table's retained mixture. Moisture binds
    /// fines; it never changes how much fine coal the crusher actually made.
    /// Numerical strengths are tunings, not measured HT performance curves.
    /// </summary>
    internal sealed class StokerHandling
    {
        private readonly StokerSpec spec;
        internal StokerCoal Table;
        internal float Packing, Warmth, Cake;
        internal bool NeedsClearing { get; private set; }
        internal void ReadTable()
        {
            if (Fill > .35f) NeedsClearing = true;
            else if (Fill < .2f) NeedsClearing = false;
        }
        internal StokerHandling(StokerSpec spec) { this.spec = spec; }
        internal float Capacity => spec.MaxFeedRate * spec.TableCapacitySeconds;
        internal float Fill => Table.Coal / Math.Max(.01f, Capacity);
        internal static float Wetness(float moisture) => Clamp((moisture - CoalMoisture.Delivered) / (CoalMoisture.Soaked - CoalMoisture.Delivered));
        internal static float Sticky(float moisture, float fineShare) => Wetness(moisture) * Math.Min(3f, Math.Max(0f, fineShare) / .25f);
        internal static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));
        internal static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);

        internal void Warm(float dt, bool fireOn)
        {
            Warmth += ((fireOn ? 1f : 0f) - Warmth) * (1f - (float)Math.Exp(-dt / (fireOn ? 90f : 300f)));
        }

        internal float ShaftLoad(float moisture, float fines) => Math.Max(.65f,
            1f + .35f * (.25f - fines) / .75f + .12f * Sticky(moisture, fines)
            + .9f * Packing + Math.Max(0f, Fill - .75f) * 4f);

        internal float Yield(float moisture, float fines)
        {
            var sticky = Sticky(moisture, fines);
            return Math.Max(.4f, 1f - .12f * sticky / (1f + sticky) - .3f * Packing);
        }

        internal void PassCoal(float drawn, float moisture, float fines, float rate)
        {
            if (!spec.PackingJams) { Packing = 0f; return; }
            if (!(spec.CoalToPack > 0f) || !(drawn > 0f)) return;
            var wet = Wetness(moisture);
            var fineLoad = Math.Min(4f, Math.Max(0f, fines) / .25f);
            var build = wet * wet * fineLoad * fineLoad * Math.Max(.25f, rate / spec.MaxFeedRate);
            var scour = 2f * (1f - wet);
            Packing = Clamp(Packing + drawn / spec.CoalToPack * (build - scour));
        }

        internal void Reverse(float turns) => Packing = Math.Max(0f, Packing - turns / 5f);

        internal StokerCoal Prepare(float dt, StokerCoal incoming, float jets)
        {
            var old = Table;
            Table = StokerCoal.Add(Table, incoming);
            var sticky = Sticky(Table.Moisture, Table.FineShare);
            // Once wet coal stays on a hot plate it can bake there. This is
            // a coating on fuel already counted in Table, not extra fuel.
            Cake = Clamp(Cake + dt / 180f * Warmth *
                (sticky * Math.Max(0f, Fill - .2f) - Math.Max(0f, 1f - sticky)));
            var loading = incoming.Coal / dt / spec.MaxFeedRate / Math.Max(.1f, jets);
            var residence = spec.TableResidenceSeconds *
                (sticky * (1f - .6f * Warmth) * Math.Max(0f, 1.1f - jets)
                 + .5f * Math.Max(0f, Math.Min(4f, loading) - 1f) + 2f * Cake);
            // With no jet steam or fresh coal pushing from the screw, the
            // stationary distributor has nothing to move its retained fuel.
            var transport = Math.Max(jets, incoming.Coal / dt / spec.MaxFeedRate);
            if (!(transport > 0f)) return default;
            // Weak transport takes longer for both old and incoming coal.
            // Applying a floor only to old fuel made the pile depend on fps.
            residence += .25f * spec.TableResidenceSeconds * Math.Max(0f, 1f / transport - 1f);
            // Analytic well-mixed hold-up for uniform intake during this tick:
            // the incoming coal has not spent a whole tick on the table.
            // The rate cap prevents a burst when residence falls to zero.
            // A clear table still passes new, dry fuel straight through.
            var exitOld = residence < 1e-5f ? 1d : 1d - Math.Exp(-dt / residence);
            if (old.Coal > 0f)
                exitOld = Math.Min(exitOld, spec.MaxFeedRate * Math.Max(1f, jets) * dt / old.Coal);
            var exitNew = residence < 1e-5f ? 1d : 1d - residence / dt * (1d - Math.Exp(-dt / residence));
            return StokerCoal.Add(old.Scale((float)exitOld), incoming.Scale((float)Math.Max(0d, exitNew)));
        }

        internal void Remove(StokerCoal coal)
        {
            Table.Coal = Math.Max(0f, Table.Coal - coal.Coal);
            Table.Fines = Math.Max(0f, Math.Min(Table.Coal, Table.Fines - coal.Fines));
            Table.Water = Math.Max(0f, Table.Water - coal.Water);
            if (Table.Coal < 1e-6f) Table = default;
            ReadTable();
        }

        internal void Restore(StokerState state)
        {
            var mass = Finite(state.TableCoal);
            var scale = mass > Capacity ? Capacity / mass : 1f;
            Table = new StokerCoal { Coal = mass * scale,
                Fines = Math.Min(mass, Finite(state.TableFines)) * scale,
                Water = Math.Min(mass * StokerCoal.RainWater(CoalMoisture.Soaked), Finite(state.TableWater)) * scale };
            Packing = spec.PackingJams ? Clamp(Finite(state.Packing)) : 0f; Warmth = Clamp(Finite(state.TableWarmth)); Cake = Clamp(Finite(state.TableCake));
            NeedsClearing = state.TableNeedsClearing;
            ReadTable();
        }
    }
}
