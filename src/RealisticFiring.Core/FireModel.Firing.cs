using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    public sealed partial class FireModel
    {
        /// <summary>Live coal shaking has thrown up the stack as cinders, in kg, since the fire was made.</summary>
        public float ShakeCinders { get; private set; }

        /// <summary>
        /// The zones the drop grate covers: the front row unless the loco says
        /// otherwise ("the 'drop-grate'—the forward section of the grates", Baker).
        /// </summary>
        public IReadOnlyList<GrateZone> DropGrate { get; set; } = new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight };

        /// <summary>How far the drop grate stood open last tick, 0 to 1.</summary>
        public float DropGrateOpening { get; private set; }

        /// <summary>Ash and clinker the drop grate has dropped, in kg, since the fire was made.</summary>
        public float DroppedAshAndClinker { get; private set; }

        /// <summary>Loose ash and broken clinker hooked out through the door, in kg, since the fire was made.</summary>
        public float HookedOut { get; private set; }

        /// <summary>Ash, clinker, and burnt-out coal in the ashpan, in kg.</summary>
        public float Ashpan { get; private set; }

        /// <summary>What the ashpan holds when full, in kg: by grate area once measured, else by coal capacity.</summary>
        public float AshpanCapacity => GrateArea > 0f
            ? tuning.AshpanPerGrateArea * GrateArea
            : tuning.AshpanPerCoalCapacity * parameters.MaxCoalCapacity;

        /// <summary>How full the ashpan is, 0 to 1.</summary>
        public float AshpanFill => Smoothing.Clamp01(Ashpan / AshpanCapacity);

        /// <summary>Ashes let out of the ashpan's hopper doors, in kg, since the fire was made.</summary>
        public float AshpanEmptied { get; private set; }

        /// <summary>Live coal lost into the ashpan by shaking, in kg since creation.</summary>
        public float CoalDroppedThroughGrate { get; private set; }

        /// <summary>Coal removed by the fire dump, in kg since creation.</summary>
        public float CoalDumped { get; private set; }

        /// <summary>Whether anything is left for the dump to remove.</summary>
        public bool HasDumpableContents => StarterMass > NegligibleMass || CoalMass > 0f || AshMass + ClinkerMass > NegligibleMass;

        // How far past starting to choke the ashpan is, 0 to 1.
        private float AshpanOverfill => Smoothing.Clamp01((AshpanFill - tuning.AshpanChokeStart) / (1f - tuning.AshpanChokeStart));

        // The coal's ash, by its road's coal.
        private float AshFraction => tuning.AshFraction * character.AshFactor;

        // Ash and clinker sifted or shaken through go into the ashpan as far as
        // it has room; returns the share of <paramref name="mass"/> that went.
        private float IntoAshpan(float mass)
        {
            if (!(mass > 0f))
                return 1f;
            var room = Math.Max(0f, AshpanCapacity - Ashpan);
            var taken = Math.Min(mass, room);
            Ashpan += taken;
            return taken / mass;
        }

        // Fire falls into the ashpan regardless and burns down to its ash there.
        private void FireIntoAshpan(float coal) => Ashpan += Math.Max(0f, coal) * AshFraction;

        /// <summary>
        /// Holds the ashpan's hopper doors open, 0 to 1, for one tick: the
        /// ashes fall out. Returns the kg let out.
        /// </summary>
        public float OpenAshpan(float opening, float dt)
        {
            opening = Smoothing.Clamp01(opening);
            if (opening <= 0f || !(dt > 0f) || Ashpan <= 0f)
                return 0f;
            var emptied = Ashpan * (1f - (float)Math.Exp(-opening * dt / tuning.AshpanEmptyTime));
            if (Ashpan - emptied < NegligibleMass)
                emptied = Ashpan;
            Ashpan -= emptied;
            AshpanEmptied += emptied;
            return emptied;
        }

        /// <summary>Restores the ashpan's contents from a save.</summary>
        public void RestoreAshpan(float mass) => Ashpan = mass >= 0f && !float.IsInfinity(mass) ? mass : 0f;

        /// <summary>
        /// Adds fresh coal aimed at one zone; some spills onto its neighbors.
        /// Returns the mass accepted, which the capacity may limit.
        /// </summary>
        public float AddCoal(float mass, GrateZone target)
        {
            var accepted = Accept(mass);
            if (accepted <= 0f)
                return 0f;
            BeforeCoal();

            // Each of the four sides takes a quarter of the spill; against a
            // wall it stays in the aimed zone. That way a round that aims at
            // every zone once covers the grate evenly.
            var spillPerSide = accepted * tuning.ShovelSpill / 4f;
            var neighbors = GrateLayout.Neighbors(target);
            zones[(int)target].Fresh += accepted - spillPerSide * neighbors.Count;
            for (var k = 0; k < neighbors.Count; k++)
                zones[(int)neighbors[k]].Fresh += spillPerSide;
            AfterCoal();
            Wet(accepted);
            return accepted;
        }

        /// <summary>Adds fresh coal spread evenly. Returns the mass accepted.</summary>
        public float AddCoalEvenly(float mass)
        {
            var accepted = Accept(mass);
            BeforeCoal();
            foreach (var zone in zones)
                zone.Fresh += accepted * zoneArea;
            AfterCoal();
            Wet(accepted);
            return accepted;
        }

        /// <summary>
        /// Adds stoked coal by zone: lumps land as fresh coal, and fines catch
        /// at once, landing as burning coal. If the box can't take it all,
        /// every zone takes the same share. Returns the mass accepted.
        /// </summary>
        public float AddStoked(float[] lumps, float[] fines) => AddStoked(lumps, fines, FeedMoisture);

        /// <summary>Retained stoker fuel carries its own moisture, independent of a newly refilled tender.</summary>
        public float AddStoked(float[] lumps, float[] fines, float moisture)
        {
            if (lumps == null || fines == null || lumps.Length != zones.Length || fines.Length != zones.Length)
                throw new ArgumentException("Need one lump and one fines mass per zone.");

            var offered = 0f;
            for (var i = 0; i < zones.Length; i++)
                offered += Math.Max(0f, lumps[i]) + Math.Max(0f, fines[i]);
            var accepted = Accept(offered);
            if (accepted <= 0f)
                return 0f;

            var share = accepted / offered;
            var freshAdded = 0f;
            var caught = 0f;
            BeforeCoal();
            for (var i = 0; i < zones.Length; i++)
            {
                var lump = Math.Max(0f, lumps[i]) * share;
                var fine = Math.Max(0f, fines[i]) * share;
                zones[i].Fresh += lump + (FireOn ? 0f : fine);
                zones[i].Burning += FireOn ? fine : 0f;
                freshAdded += lump + (FireOn ? 0f : fine);
                caught += FireOn ? fine : 0f;
            }
            AfterCoal();
            Wet(freshAdded, moisture);
            // Fines that catch at once dry at once.
            flashWater += caught * RainWater(moisture);
            return accepted;
        }

        /// <summary>Takes coal from every zone in proportion to what it holds. Returns the mass removed.</summary>
        public float RemoveCoal(float mass)
        {
            var coal = CoalMass;
            if (!(mass > 0f) || coal <= 0f)
                return 0f;

            var removed = Math.Min(mass, coal);
            var keep = 1f - removed / coal;
            foreach (var zone in zones)
            {
                zone.Fresh *= keep;
                zone.Burning *= keep;
            }
            return removed;
        }

        /// <summary>
        /// Rocks the grate. A shake (half stroke) clears most loose ash,
        /// breaks up a little clinker, and drops some live coal into the
        /// ashpan; a wiggle clears less ash and loses almost no fire. Shaken
        /// while the draft pulls hard, more live coal falls and some goes up
        /// the stack as cinders.
        /// </summary>
        public void ShakeGrate(ShakeStroke stroke = ShakeStroke.Shake)
        {
            var shake = stroke == ShakeStroke.Shake;
            var drawing = Drawing;
            var working = 1f + (tuning.WorkingShakeLoss - 1f) * drawing * drawing;
            var lossShare = (shake ? tuning.ShakeBurningLoss : tuning.WiggleBurningLoss) * working;
            foreach (var zone in zones)
            {
                // Into the ashpan as far as it has room; a full pan takes none.
                var ash = zone.Ash * (shake ? tuning.ShakeAshRemoval : tuning.WiggleAshRemoval);
                var clinker = shake ? zone.Clinker * tuning.ShakeClinkerRemoval : 0f;
                var went = IntoAshpan(ash + clinker);
                zone.Ash -= ash * went;
                zone.Clinker -= clinker * went;
                var lost = zone.Burning * lossShare;
                zone.Burning -= lost;
                // The loss beyond a still fire's goes up the stack, half of it as cinders.
                var blown = 0.5f * lost * (1f - 1f / working);
                ShakeCinders += blown;
                CoalDroppedThroughGrate += lost - blown;
                FireIntoAshpan(lost - blown);
            }
        }

        /// <summary>
        /// Levels the fire around <paramref name="target"/> with a rake or an
        /// empty shovel: the zone and its neighbors each move part of the way
        /// toward their mean coal, coal going from piled zones to thin ones
        /// with its fresh and burning shares, and some of the raked zone's
        /// clinker breaks into loose ash. Total coal is unchanged.
        /// </summary>
        public void Rake(GrateZone target)
        {
            var group = new List<ZoneState> { zones[(int)target] };
            foreach (var neighbor in GrateLayout.Neighbors(target))
                group.Add(zones[(int)neighbor]);

            var mean = 0f;
            foreach (var zone in group)
                mean += zone.Coal;
            mean /= group.Count;

            var surplus = 0f;
            foreach (var zone in group)
                surplus += Math.Max(0f, zone.Coal - mean);

            if (surplus > NegligibleMass)
            {
                var moving = tuning.RakeShare * surplus;
                float fresh = 0f, burning = 0f;
                foreach (var zone in group)
                {
                    var excess = zone.Coal - mean;
                    if (excess <= 0f)
                        continue;
                    var share = moving * excess / surplus / zone.Coal;
                    fresh += zone.Fresh * share;
                    burning += zone.Burning * share;
                    zone.Fresh -= zone.Fresh * share;
                    zone.Burning -= zone.Burning * share;
                }

                // The mean splits surplus and deficit evenly, so the deficits sum to the surplus.
                foreach (var zone in group)
                {
                    var shortfall = mean - zone.Coal;
                    if (shortfall <= 0f)
                        continue;
                    var share = shortfall / surplus;
                    zone.Fresh += fresh * share;
                    zone.Burning += burning * share;
                }
            }

            var raked = zones[(int)target];
            var broken = raked.Clinker * tuning.RakeClinkerBreak;
            raked.Clinker -= broken;
            raked.Ash += broken;
            Stir(raked, tuning.RakeStir);
        }

        // Fire irons raise ash from the grate into the hot fire, where it fuses.
        private static void Stir(ZoneState zone, float amount) => zone.Stirred = Math.Min(1f, zone.Stirred + amount);

        /// <summary>
        /// Runs the slice bar under <paramref name="target"/>'s fire: half its
        /// clinker breaks loose, for a shake or the drop grate to take, and
        /// ash is stirred up into the fire, where it fuses again.
        /// </summary>
        public void Slice(GrateZone target)
        {
            var zone = zones[(int)target];
            var broken = zone.Clinker * tuning.SliceClinkerBreak;
            zone.Clinker -= broken;
            zone.Ash += broken;
            Stir(zone, tuning.SliceStir);
        }

        /// <summary>
        /// Works <paramref name="target"/> with the hoe toward
        /// <paramref name="direction"/>: part of its coal and loose ash (broken
        /// clinker with it) moves to the next zone. Pulled toward the door from
        /// the back row, the loose ash and clinker come out through it and the
        /// fire stays. Against a side wall nothing moves. Clinker that has not
        /// been broken stays where it is.
        /// </summary>
        public void Push(GrateZone target, GrateDirection direction)
        {
            var from = zones[(int)target];
            var row = GrateLayout.Row(target);
            var column = GrateLayout.Column(target);
            switch (direction)
            {
                case GrateDirection.Front: row++; break;
                case GrateDirection.Back: row--; break;
                case GrateDirection.Left: column--; break;
                default: column++; break;
            }
            Stir(from, tuning.PushStir);
            if (row < 0)
            {
                var hooked = from.Ash * tuning.PushShare;
                from.Ash -= hooked;
                HookedOut += hooked;
                return;
            }
            if (row >= GrateLayout.Rows || column < 0 || column >= GrateLayout.Columns)
                return;

            var to = zones[(int)GrateLayout.At(row, column)];
            var fresh = from.Fresh * tuning.PushShare;
            var burning = from.Burning * tuning.PushShare;
            var ash = from.Ash * tuning.PushShare;
            from.Fresh -= fresh;
            from.Burning -= burning;
            from.Ash -= ash;
            to.Fresh += fresh;
            to.Burning += burning;
            to.Ash += ash;
            Stir(to, tuning.PushStir);
        }

        /// <summary>
        /// Holds the drop grate open, 0 to 1, for one tick: whatever lies on its
        /// zones (fire, ash, clinker) falls into the ashpan, and cold air comes
        /// straight in through the opening.
        /// </summary>
        public void OpenDropGrate(float opening, float dt)
        {
            DropGrateOpening = Smoothing.Clamp01(opening);
            if (DropGrateOpening <= 0f || !(dt > 0f))
                return;
            var keep = (float)Math.Exp(-DropGrateOpening * dt / tuning.DropGrateTime);
            foreach (var target in DropGrate)
            {
                var zone = zones[(int)target];
                Ashpan += Starters[(int)target].Dump(1f - keep);
                CoalDumped += (zone.Fresh + zone.Burning) * (1f - keep);
                DroppedAshAndClinker += (zone.Ash + zone.Clinker) * (1f - keep);
                Ashpan += (zone.Ash + zone.Clinker) * (1f - keep);
                FireIntoAshpan((zone.Fresh + zone.Burning) * (1f - keep));
                zone.Fresh *= keep;
                zone.Burning *= keep;
                zone.Ash *= keep;
                zone.Clinker *= keep;
            }
        }

        /// <summary>
        /// Runs the fire dump at a normalized flow for one tick: coal falls out
        /// at the stock dump rate, and ash and clinker go with it.
        /// </summary>
        public void Dump(float flow, float dt)
        {
            flow = Smoothing.Clamp01(flow);
            if (flow <= 0f || !(dt > 0f))
                return;

            var dumped = RemoveCoal(flow * parameters.CoalDumpRate * dt);
            CoalDumped += dumped;
            FireIntoAshpan(dumped);
            var keep = (float)Math.Exp(-flow * dt / tuning.DumpClearTime);
            foreach (var starter in Starters) Ashpan += starter.Dump(1f - keep);
            foreach (var zone in zones)
            {
                Ashpan += (zone.Ash + zone.Clinker) * (1f - keep);
                zone.Ash *= keep;
                zone.Clinker *= keep;
            }
        }

        private float Accept(float mass)
        {
            if (!(mass > 0f))
                return 0f;
            return Math.Max(0f, Math.Min(mass, parameters.MaxCoalCapacity - CoalMass - StarterMass));
        }

        private bool IsDropGrate(GrateZone zone)
        {
            foreach (var covered in DropGrate)
                if (covered == zone)
                    return true;
            return false;
        }
    }
}
