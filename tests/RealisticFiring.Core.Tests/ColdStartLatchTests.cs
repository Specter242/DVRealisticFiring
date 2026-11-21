using RealisticFiring.Core;
using Xunit;

namespace RealisticFiring.Core.Tests
{
    public sealed class ColdStartLatchTests
    {
        // A cold S282 being lit: water, damper, wood and rag done, the door open.
        private static ChecklistSnapshot Lighting(float burningCoal, float door = 1f) => new ChecklistSnapshot
        {
            IsOil = false, HasDoor = true, DoorOpening = door, GlassLevel = 0.5f, Damper = 1f,
            WoodMass = 4f * StarterFuel.PieceMass, RagLit = true, WoodCaught = true,
            FireOn = true, BurningCoalMass = burningCoal, TotalCoalMass = burningCoal + 2f, Throttle = 0f,
        };

        private static ChecklistSnapshot Read(ColdStartLatch latch, ChecklistSnapshot s, float now)
        {
            latch.Apply(ref s, now);
            return s;
        }

        [Fact]
        public void TheColdStartListHoldsUntilTheDoorIsShut()
        {
            var latch = new ColdStartLatch();
            Read(latch, new ChecklistSnapshot { IsOil = false, HasDoor = true, DoorOpening = 1f, GlassLevel = 0.5f }, 0f);

            // The coal catches with the door open: the list stays, at its last step.
            var s = Read(latch, Lighting(0.8f), 10f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
            var list = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(list.Steps[5].Done);
            Assert.Equal("Fire door shut", list.Steps[list.CurrentIndex].Title);

            // The burning coal dips: still the cold start, and "First coal lit" stays ticked.
            s = Read(latch, Lighting(0.3f), 20f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).Steps[5].Done);

            // The door shut: every step ticked and shown a while, then the list lets go.
            s = Read(latch, Lighting(1.2f, door: 0f), 30f);
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).AllDone);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
            s = Read(latch, Lighting(1.2f, door: 0f), 30f + ColdStartLatch.DoneSeconds - 1f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
            s = Read(latch, Lighting(1.2f, door: 0f), 30f + ColdStartLatch.DoneSeconds);
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));

            // A later dip doesn't bring it back.
            s = Read(latch, Lighting(0.2f, door: 0f), 50f);
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
        }

        [Fact]
        public void TheColdStartListLetsGoAfterTheCoalHasBurnedAMinute()
        {
            var latch = new ColdStartLatch();
            Read(latch, new ChecklistSnapshot { IsOil = false, GlassLevel = 0.5f }, 0f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(1f), 5f)));
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(1f), 5f + ColdStartLatch.HoldSeconds - 1f)));
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(1f), 5f + ColdStartLatch.HoldSeconds)));
        }

        [Fact]
        public void ARunningEngineIsNeverHeldAndAFireGoneOutStartsAgain()
        {
            var latch = new ColdStartLatch();
            var s = Read(latch, Lighting(20f, door: 0f), 0f);
            Assert.False(s.ColdStartHeld);
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
            // A thin fire's dip under half a kilogram isn't a cold start.
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(0.3f, door: 0f), 10f)));

            // The fire out: a cold start again, held once the coal catches.
            var cold = Read(latch, new ChecklistSnapshot { IsOil = false, GlassLevel = 0.5f }, 20f);
            Assert.True(cold.ColdStartHeld);
            Assert.False(cold.CoalCaught);
            s = Read(latch, Lighting(0.8f), 30f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(s));
        }

        [Fact]
        public void TheDoorOpenedAgainBeforeTheListLetsGoStartsItsWaitAgain()
        {
            var latch = new ColdStartLatch();
            Read(latch, new ChecklistSnapshot { IsOil = false, GlassLevel = 0.5f }, 0f);
            Read(latch, Lighting(1f), 5f);
            Read(latch, Lighting(1f, door: 0f), 10f);
            Read(latch, Lighting(1f, door: 1f), 15f);
            Read(latch, Lighting(1f, door: 0f), 18f);
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(1f, door: 0f), 18f + ColdStartLatch.DoneSeconds - 1f)));
            Assert.NotEqual(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(Read(latch, Lighting(1f, door: 0f), 18f + ColdStartLatch.DoneSeconds)));
        }

        [Fact]
        public void LowWaterTakesOverFromAHeldColdStart()
        {
            var latch = new ColdStartLatch();
            Read(latch, new ChecklistSnapshot { IsOil = false, GlassLevel = 0.5f }, 0f);
            var s = Lighting(0.8f);
            s.GlassLevel = FiringAdvice.LowGlass * 0.5f;
            s = Read(latch, s, 10f);
            Assert.True(s.ColdStartHeld);
            Assert.Equal(ChecklistId.InjectorsAndWater, FiringChecklists.AutoSelect(s));
        }
    }
}
