using System.Linq;
using RealisticFiring.Core;
using Xunit;

namespace RealisticFiring.Core.Tests
{
    public sealed class FiringChecklistsTests
    {
        [Fact]
        public void ApplicableListsMatchFuelAndStoker()
        {
            var handCoal = FiringChecklists.Applicable(new ChecklistSnapshot { IsOil = false, HasStoker = false });
            Assert.Equal(new[]
            {
                ChecklistId.CoalColdStart,
                ChecklistId.InjectorsAndWater,
                ChecklistId.CleaningClinkerAndAsh,
                ChecklistId.DriftingAndLayover,
            }, handCoal);

            var stokerCoal = FiringChecklists.Applicable(new ChecklistSnapshot { IsOil = false, HasStoker = true });
            Assert.Contains(ChecklistId.StokerSetup, stokerCoal);
            Assert.Equal(5, stokerCoal.Count);

            var oil = FiringChecklists.Applicable(new ChecklistSnapshot { IsOil = true, HasStoker = true });
            Assert.Equal(new[]
            {
                ChecklistId.OilColdStart,
                ChecklistId.InjectorsAndWater,
                ChecklistId.DriftingAndLayover,
            }, oil);
        }

        [Fact]
    public void AshWantingAShakeSelectsCleaningAndItsShakeStep()
    {
        var ashy = new ChecklistSnapshot { IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.55f, WantsShake = true };
        Assert.Equal(ChecklistId.CleaningClinkerAndAsh, FiringChecklists.AutoSelect(ashy));
        var due = FiringChecklists.Evaluate(ashy, ChecklistId.CleaningClinkerAndAsh).Steps.Single(st => st.Title == "Shake ash through the grate");
        Assert.False(due.Done);
        ashy.WantsShake = false;
        var shaken = FiringChecklists.Evaluate(ashy, ChecklistId.CleaningClinkerAndAsh).Steps.Single(st => st.Title == "Shake ash through the grate");
        Assert.True(shaken.Done);
    }

    [Fact]
        public void AutoSelectChoosesMostRelevantChecklist()
        {
            // Cold coal -> CoalColdStart
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = false, GlassLevel = 0.5f,
            }));

            // Lit starter wood but coal not yet burning -> still CoalColdStart
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 0.1f, GlassLevel = 0.5f,
            }));

            // Cold oil -> OilColdStart
            Assert.Equal(ChecklistId.OilColdStart, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = true, FireOn = false, GlassLevel = 0.5f,
            }));

            // Lit fire, low glass -> InjectorsAndWater
            Assert.Equal(ChecklistId.InjectorsAndWater, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.2f,
            }));

            // Lit fire, good glass, clinker building -> CleaningClinkerAndAsh
            Assert.Equal(ChecklistId.CleaningClinkerAndAsh, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.55f, NeedsCleaning = true,
            }));

            // Lit fire, good glass, full ashpan -> CleaningClinkerAndAsh
            Assert.Equal(ChecklistId.CleaningClinkerAndAsh, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.55f, AshpanFull = true,
            }));

            // Lit fire, stoker jammed -> StokerSetup
            Assert.Equal(ChecklistId.StokerSetup, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.55f,
                HasStoker = true, StokerJammed = true, Throttle = 0.5f,
            }));

            // Lit fire, drifting hooked up -> DriftingAndLayover
            Assert.Equal(ChecklistId.DriftingAndLayover, FiringChecklists.AutoSelect(new ChecklistSnapshot
            {
                IsOil = false, FireOn = true, BurningCoalMass = 20f, GlassLevel = 0.55f,
                Throttle = 0f, ForwardSpeedMs = 12f, DriftingHookedUp = true,
            }));
        }

        [Fact]
        public void StepSelectionWrapsAcrossApplicableLists()
        {
            var s = new ChecklistSnapshot { IsOil = false, HasStoker = false };
            Assert.Equal(ChecklistId.InjectorsAndWater, FiringChecklists.StepSelection(s, ChecklistId.CoalColdStart, 1));
            Assert.Equal(ChecklistId.DriftingAndLayover, FiringChecklists.StepSelection(s, ChecklistId.CoalColdStart, -1));
            Assert.Equal(ChecklistId.CoalColdStart, FiringChecklists.StepSelection(s, ChecklistId.DriftingAndLayover, 1));
        }

        [Fact]
        public void CoalColdStartProgressesThroughAllSevenSteps()
        {
            var s = new ChecklistSnapshot
            {
                IsOil = false, HasDoor = true, DoorOpening = 1f,
                GlassLevel = 0.1f, Damper = 0f,
            };

            var c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.Equal(7, c.Steps.Count);
            Assert.Equal(0, c.CurrentIndex);
            Assert.Equal("Water in glass", c.Steps[0].Title);
            Assert.False(c.AllDone);

            // Step 1 done: water in glass -> step 2 Damper open
            s.GlassLevel = 0.5f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(c.Steps[0].Done);
            Assert.Equal(1, c.CurrentIndex);

            // Step 2 done: damper open -> step 3 4+ wood pieces
            s.Damper = 1f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.Equal(2, c.CurrentIndex);

            // Only 2 wood pieces is not enough yet
            s.WoodMass = 2f * StarterFuel.PieceMass;
            Assert.Equal(2, FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).CurrentIndex);

            // 4 wood pieces -> step 4 Lit oiled rag
            s.WoodMass = 4f * StarterFuel.PieceMass;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).CurrentIndex);

            // Lit rag in firebox -> step 5 Wood caught
            s.RagLit = true;
            Assert.Equal(4, FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).CurrentIndex);

            // Wood caught -> step 6 First coal lit
            s.WoodCaught = true;
            Assert.Equal(5, FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).CurrentIndex);

            // Coal lit, door still open -> step 7 Fire door shut
            s.FireOn = true;
            s.BurningCoalMass = 2f;
            Assert.Equal(6, FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).CurrentIndex);

            // Door shut -> all done
            s.DoorOpening = 0f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(c.AllDone);
            Assert.Equal(-1, c.CurrentIndex);
            Assert.Equal(7, c.CompletedCount);
        }

        [Fact]
        public void CoalColdStartWithLighterLightsCoalUsesStockLighterFlow()
        {
            var s = new ChecklistSnapshot
            {
                IsOil = false,
                LighterLightsCoal = true,
                HasDoor = true,
                DoorOpening = 1f,
                GlassLevel = 0.5f,
                Damper = 1f,
                WoodMass = 0f,
                TotalCoalMass = 0f,
            };

            var c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.Equal(5, c.Steps.Count);
            Assert.DoesNotContain(c.Steps, step => step.Title.Contains("wood") || step.Title.Contains("rag"));
            Assert.Equal(2, c.CurrentIndex);
            Assert.Equal("Shovel coal onto grate", c.Steps[2].Title);

            s.TotalCoalMass = 12f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(c.Steps[2].Done);
            Assert.Equal(3, c.CurrentIndex);
            Assert.Equal("Light coal with lighter", c.Steps[3].Title);

            s.FireOn = true;
            s.BurningCoalMass = 2f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(c.Steps[3].Done);
            Assert.Equal(4, c.CurrentIndex);
            Assert.Equal("Fire door shut", c.Steps[4].Title);

            s.DoorOpening = 0f;
            c = FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart);
            Assert.True(c.AllDone);
            Assert.Equal(5, c.CompletedCount);
        }

        [Fact]
        public void OilColdStartUnifiesWithExistingOilLightingSteps()
        {
            var oil = new OilReading
            {
                FireOn = false,
                GlassLevel = 0.5f,
                OnSteamLine = false,
                BoilerPressure = 1f,
                OilTemperature = 20f,
                Vapor = 0f,
                PooledOil = 0f,
                BlowerOn = false,
                AtomizerOpen = false,
            };
            var s = new ChecklistSnapshot
            {
                IsOil = true,
                FireOn = false,
                GlassLevel = 0.5f,
                Oil = oil,
            };
            var adviceSteps = FiringAdvice.Steps(oil);
            var c = FiringChecklists.Evaluate(s, ChecklistId.OilColdStart);
            Assert.Equal(adviceSteps.Count + 1, c.Steps.Count);
            Assert.True(c.Steps[0].Done);
            for (var i = 0; i < adviceSteps.Count; i++)
                Assert.Equal(adviceSteps[i].Title, c.Steps[i + 1].Title);
            Assert.Equal(1, c.CurrentIndex); // Steam available

            // Steam available + oil warm + box clear + blower/atomizer -> Light it
            s.Oil.OnSteamLine = true;
            s.Oil.OilTemperature = 55f;
            s.Oil.BlowerOn = true;
            s.Oil.AtomizerOpen = true;
            c = FiringChecklists.Evaluate(s, ChecklistId.OilColdStart);
            Assert.Equal(c.Steps.Count - 1, c.CurrentIndex);
            Assert.Equal("Light it", c.Steps[c.CurrentIndex].Title);

            // Lit -> all done
            s.FireOn = true;
            s.Oil.FireOn = true;
            c = FiringChecklists.Evaluate(s, ChecklistId.OilColdStart);
            Assert.True(c.AllDone);
        }

        [Fact]
        public void InjectorsAndWaterTracksPressureTenderWaterPrimeFeedAndGlass()
        {
            var s = new ChecklistSnapshot
            {
                HasSeparateSteamValve = true,
                HasTenderWaterValve = true,
                TenderWaterValveOpen = false,
                BoilerGaugeBar = 1.5f,
                InjectorWaterValve = 0f,
                GlassLevel = 0.25f,
            };
            var c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(6, c.Steps.Count);
            Assert.Equal(0, c.CurrentIndex); // Pressure

            s.BoilerGaugeBar = 4f;
            Assert.Equal(1, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Tender valve

            s.TenderWaterValveOpen = true;
            Assert.Equal(2, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Water valve

            s.InjectorWaterValve = 0.6f;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Prime

            // Still priming with low overflow: Prime must NOT tick yet.
            s.InjectorSteamPrime = true;
            s.InjectorState = InjectorState.Priming;
            s.InjectorPriming = true;
            s.OverflowWaterKgPerS = 0.05f;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Still Prime

            // Picked up while in Prime: Prime ticks, Feed becomes current.
            s.InjectorState = InjectorState.Working;
            s.InjectorPriming = false;
            s.InjectorWorking = true;
            Assert.Equal(4, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Feed

            s.InjectorSteamPrime = false;
            s.InjectorSteamFeed = true;
            Assert.Equal(5, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).CurrentIndex); // Glass ~half

            s.GlassLevel = 0.55f;
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).AllDone);

            // Without tender water valve: 5 steps
            s.HasTenderWaterValve = false;
            Assert.Equal(5, FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater).Steps.Count);
        }

        private sealed class TestSteamProperties : ISteamProperties
        {
            public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
            public float WaterEnthalpy(float temperature) => 4.19f * temperature;
            public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
            public float SaturationTemperature(float pressure) => 100f + 8f * (pressure - 1f);
        }

        [Fact]
        public void TwoValveSmallWaterLowOverflowDoesNotTickPrimeUntilInjectorPicksUp()
        {
            var steam = new TestSteamProperties();
            var spec = new InjectorSpec(maxDelivery: 2.7f, ratedPressure: 15f);
            var injector = new Injector(spec);
            var earlyFeedCheck = new Injector(spec);
            var primeInputs = new InjectorInputs
            {
                WaterValve = 0.08f,
                SteamValve = InjectorSpec.SteamPrime,
                BoilerPressure = 9f,
                TenderWater = 10_000f,
                BoilerSpace = 5_000f,
                TenderWaterTemperature = 15f,
            };

            // Step partway through PickUpTime (0.5 s < 1.5 s): small water opening keeps overflow < 0.25 kg/s.
            var primingFlow = default(InjectorFlow);
            for (var i = 0; i < 10; i++)
            {
                primingFlow = injector.Step(0.05f, primeInputs, steam);
                earlyFeedCheck.Step(0.05f, primeInputs, steam);
            }

            Assert.Equal(InjectorState.Priming, primingFlow.State);
            Assert.InRange(primingFlow.Overflow, 0.001f, 0.249f);

            // Moving to Feed at this point breaks the jet in Injector.cs.
            var feedInputs = primeInputs;
            feedInputs.SteamValve = 1f;
            Assert.Equal(InjectorState.Broken, earlyFeedCheck.Step(0.05f, feedInputs, steam).State);

            var snap = new ChecklistSnapshot { BoilerGaugeBar = 8f, GlassLevel = 0.35f };
            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = true,
                    WaterValve = primeInputs.WaterValve.Value,
                    SteamValve = primeInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = primingFlow.State,
                    OverflowWaterKgPerS = primingFlow.Overflow,
                },
            });

            var whilePriming = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, whilePriming.CurrentIndex);
            Assert.Equal("Steam to Prime until overflow quiets", whilePriming.Steps[2].Title);
            Assert.False(whilePriming.Steps[2].Done); // Prime must NOT tick while pickup timer still runs

            // Finish priming past PickUpTime -> Injector transitions to Working while still in Prime.
            var pickedUpFlow = primingFlow;
            for (var i = 0; i < 30; i++)
                pickedUpFlow = injector.Step(0.05f, primeInputs, steam);
            Assert.Equal(InjectorState.Working, pickedUpFlow.State);

            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = true,
                    WaterValve = primeInputs.WaterValve.Value,
                    SteamValve = primeInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = pickedUpFlow.State,
                    OverflowWaterKgPerS = pickedUpFlow.Overflow,
                },
            });
            var ready = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.True(ready.Steps[2].Done); // Prime ticked once picked up
            Assert.Equal(3, ready.CurrentIndex);
            Assert.Equal("Steam to Feed", ready.Steps[3].Title);
            Assert.False(ready.Steps[3].Done);

            // Moving to Feed now keeps the jet Working and ticks Feed.
            var feedingFlow = injector.Step(0.05f, feedInputs, steam);
            Assert.Equal(InjectorState.Working, feedingFlow.State);
            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = true,
                    WaterValve = feedInputs.WaterValve.Value,
                    SteamValve = feedInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = feedingFlow.State,
                    OverflowWaterKgPerS = feedingFlow.Overflow,
                },
            });
            var feeding = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.True(feeding.Steps[2].Done);
            Assert.True(feeding.Steps[3].Done);
            Assert.Equal(4, feeding.CurrentIndex); // Glass ~half
        }

        [Fact]
        public void SingleLeverCrackedAndWorkingDoesNotTickFeedUntilOpenedPastCrack()
        {
            var steam = new TestSteamProperties();
            var spec = new InjectorSpec(maxDelivery: 2.7f, ratedPressure: 15f); // DefaultCrackOpening = 0.2f
            var injector = new Injector(spec);
            var crackedInputs = new InjectorInputs
            {
                SteamValve = 0.15f,
                WaterValve = null,
                BoilerPressure = 9f,
                TenderWater = 10_000f,
                BoilerSpace = 5_000f,
                TenderWaterTemperature = 15f,
            };

            // Partway through priming with low overflow: Prime not ticked yet.
            var primingFlow = default(InjectorFlow);
            for (var i = 0; i < 10; i++)
                primingFlow = injector.Step(0.05f, crackedInputs, steam);
            Assert.Equal(InjectorState.Priming, primingFlow.State);
            Assert.True(primingFlow.Overflow < 0.25f);

            var snap = new ChecklistSnapshot { BoilerGaugeBar = 8f, GlassLevel = 0.35f };
            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = false,
                    WaterValve = crackedInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = primingFlow.State,
                    OverflowWaterKgPerS = primingFlow.Overflow,
                },
            });
            var primingList = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, primingList.CurrentIndex);
            Assert.False(primingList.Steps[2].Done);
            Assert.False(primingList.Steps[3].Done);

            // Picked up while still cracked: Prime ticks, "Open lever to feed" stays unticked.
            var workingCrackedFlow = primingFlow;
            for (var i = 0; i < 30; i++)
                workingCrackedFlow = injector.Step(0.05f, crackedInputs, steam);
            Assert.Equal(InjectorState.Working, workingCrackedFlow.State);

            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = false,
                    WaterValve = crackedInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = workingCrackedFlow.State,
                    OverflowWaterKgPerS = workingCrackedFlow.Overflow,
                },
            });
            var crackedWorking = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.True(crackedWorking.Steps[2].Done); // "Crack lever until it sings" done
            Assert.Equal(3, crackedWorking.CurrentIndex);
            Assert.Equal("Open lever to feed", crackedWorking.Steps[3].Title);
            Assert.False(crackedWorking.Steps[3].Done);

            // Still at exact CrackOpening boundary (including GWI's 0.25f crack): Feed stays unticked.
            snap.InjectorCrackOpening = 0.25f;
            snap.InjectorWaterValve = 0.25f;
            Assert.False(FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater).Steps[3].Done);

            // Open lever past crack -> stays Working and ticks "Open lever to feed".
            var feedInputs = crackedInputs;
            feedInputs.SteamValve = 0.55f;
            var feedingFlow = injector.Step(0.05f, feedInputs, steam);
            Assert.Equal(InjectorState.Working, feedingFlow.State);

            FiringChecklists.ApplyInjectorCircuit(ref snap, new[]
            {
                new InjectorCircuitSnapshot
                {
                    HasSeparateSteamValve = false,
                    WaterValve = feedInputs.SteamValve,
                    CrackOpening = spec.CrackOpening,
                    State = feedingFlow.State,
                    OverflowWaterKgPerS = feedingFlow.Overflow,
                },
            });
            var opened = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            Assert.True(opened.Steps[2].Done);
            Assert.True(opened.Steps[3].Done);
            Assert.Equal(4, opened.CurrentIndex); // Glass ~half
        }

        [Fact]
        public void InjectorBrokenJetDoesNotTickPrimeAndShowsRecoveryStep()
        {
            var s = new ChecklistSnapshot
            {
                FireOn = true,
                BurningCoalMass = 20f,
                HasSeparateSteamValve = true,
                BoilerGaugeBar = 8f,
                InjectorWaterValve = 0.6f,
                InjectorSteamPrime = false,
                InjectorSteamFeed = true,
                InjectorState = InjectorState.Broken,
                InjectorWorking = false,
                OverflowWaterKgPerS = 0.05f,
                GlassLevel = 0.5f,
            };

            Assert.Equal(ChecklistId.InjectorsAndWater, FiringChecklists.AutoSelect(s));
            var c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(6, c.Steps.Count);
            Assert.Equal(2, c.CurrentIndex);
            Assert.Contains("Jet broken", c.Steps[2].Title);
            Assert.Contains("Off", c.Steps[2].How);
            Assert.Contains("Prime", c.Steps[2].How);
            Assert.False(c.Steps[2].Done);
            Assert.False(c.Steps[3].Done); // Prime must NOT be ticked
            Assert.False(c.Steps[4].Done); // Feed must NOT be ticked

            // Even if a caller opened straight to Feed before Broken registered, Prime is not ticked
            s.InjectorState = InjectorState.Off;
            s.InjectorSteamPrime = true; // stale flag while Feed is set
            s.InjectorSteamFeed = true;
            c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, c.CurrentIndex);
            Assert.Equal("Steam to Prime until overflow quiets", c.Steps[2].Title);
            Assert.False(c.Steps[2].Done);
        }

        [Fact]
        public void InjectorHotStateShowsCoolingStepAndDoesNotTickPrime()
        {
            var s = new ChecklistSnapshot
            {
                FireOn = true,
                BurningCoalMass = 20f,
                HasSeparateSteamValve = true,
                BoilerGaugeBar = 8f,
                InjectorWaterValve = 0.6f,
                InjectorSteamPrime = true,
                InjectorSteamFeed = false,
                InjectorState = InjectorState.Hot,
                InjectorWorking = false,
                OverflowWaterKgPerS = 0.05f,
                GlassLevel = 0.5f,
            };

            Assert.Equal(ChecklistId.InjectorsAndWater, FiringChecklists.AutoSelect(s));
            var c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, c.CurrentIndex);
            Assert.Contains("hot", c.Steps[2].Title);
            Assert.Contains("Hold at Prime to cool", c.Steps[2].How);
            Assert.False(c.Steps[2].Done);
            Assert.False(c.Steps[3].Done); // Prime must NOT be ticked while Hot

            // Single-lever Hot state also shows cooling guidance
            s.HasSeparateSteamValve = false;
            c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, c.CurrentIndex);
            Assert.Contains("cool", c.Steps[2].How);
            Assert.False(c.Steps[3].Done);
        }

        [Fact]
        public void DualInjectorNeverMixesWaterFromOneSideWithSteamFromOther()
        {
            // Right has water open (steam off, overflowing); left has steam in Prime (water shut).
            var circuits = new[]
            {
                new InjectorCircuitSnapshot
                {
                    Name = "right",
                    HasSeparateSteamValve = true,
                    HasTenderWaterValve = true,
                    TenderWaterValveOpen = true,
                    WaterValve = 0.7f,
                    SteamValve = 0f,
                    State = InjectorState.Off,
                    OverflowWaterKgPerS = 0.5f,
                },
                new InjectorCircuitSnapshot
                {
                    Name = "left",
                    HasSeparateSteamValve = true,
                    HasTenderWaterValve = true,
                    TenderWaterValveOpen = false,
                    WaterValve = 0f,
                    SteamValve = 0.5f,
                    State = InjectorState.NoWater,
                    OverflowWaterKgPerS = 0f,
                },
            };

            var s = new ChecklistSnapshot { BoilerGaugeBar = 8f, GlassLevel = 0.5f };
            FiringChecklists.ApplyInjectorCircuit(ref s, circuits);

            Assert.Equal("left", s.InjectorName);
            Assert.Equal(0f, s.InjectorWaterValve);
            Assert.True(s.InjectorSteamPrime);
            Assert.False(s.InjectorSteamFeed);
            Assert.False(s.TenderWaterValveOpen);
            Assert.Equal(0f, s.OverflowWaterKgPerS);

            var c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal("Injectors & Water (left)", c.Name);
            Assert.False(c.Steps[1].Done); // left tender valve is shut
            Assert.False(c.Steps[2].Done); // left water valve is shut (NOT ticked from right!)

            // Even when left hasn't reported NoWater yet (state Off), steam-open left is picked without right's water
            circuits[1].State = InjectorState.Off;
            circuits[1].TenderWaterValveOpen = true;
            FiringChecklists.ApplyInjectorCircuit(ref s, circuits);
            Assert.Equal("left", s.InjectorName);
            Assert.Equal(0f, s.InjectorWaterValve);
            c = FiringChecklists.Evaluate(s, ChecklistId.InjectorsAndWater);
            Assert.Equal(2, c.CurrentIndex);
            Assert.False(c.Steps[2].Done); // Water valve open stays unticked

            // When both circuits are completely idle, fall back to circuit 0 ("right")
            circuits[0].WaterValve = 0f;
            circuits[1].SteamValve = 0f;
            Assert.Equal(0, FiringChecklists.SelectInjectorCircuit(circuits));
        }

        [Fact]
        public void CleaningChecklistProgressesAndHandlesAshpanOnly()
        {
            var s = new ChecklistSnapshot
            {
                FireOn = true,
                ForwardSpeedMs = 10f,
                Throttle = 0.5f,
                NeedsCleaning = true,
                ClinkerMass = 12f,
                FrontCoalKg = 15f,
                BackCoalKg = 15f,
                FrontClinkerKg = 2f,
                TotalCoalMass = 45f,
                ThinZone = -1,
                AshpanFill = 0.6f,
                AshpanFull = true,
            };
            Assert.Equal(0, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex);

            s.ForwardSpeedMs = 0f;
            s.Throttle = 0f;
            Assert.Equal(1, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Pull live fire back

            s.FrontCoalKg = 4f;
            Assert.Equal(2, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Slice

            s.SlicedRecently = true;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Push clinker forward

            s.FrontClinkerKg = 8f;
            Assert.Equal(4, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Drop grate

            s.DropGrateOpenedRecently = true;
            s.NeedsCleaning = false;
            s.ThinZone = 2; // Still needs spreading
            Assert.Equal(5, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Spread & fresh coal

            s.ThinZone = -1;
            // Ash to shake comes before emptying the pan (a shake fills it).
            s.WantsShake = true;
            Assert.Equal(6, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Shake ash through
            s.WantsShake = false;
            Assert.Equal(7, FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).CurrentIndex); // Empty ashpan

            s.AshpanFill = 0.05f;
            s.AshpanFull = false;
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.CleaningClinkerAndAsh).AllDone);
        }

        [Fact]
        public void StokerSetupProgressesAndDetectsJams()
        {
            var s = new ChecklistSnapshot
            {
                HasStoker = true,
                BoilerGaugeBar = 1f,
                StokerForward = false,
            };
            Assert.Equal(0, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.BoilerGaugeBar = 8f;
            Assert.Equal(1, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.StokerForward = true;
            Assert.Equal(2, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.StokerMasterBar = 1.4f;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.StokerFrontBar = 1.2f;
            s.StokerBackBar = 0.8f;
            s.StokerFinesBar = 0.7f;
            Assert.Equal(4, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.StokerSpeed = 0.4f;
            Assert.Equal(5, FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).CurrentIndex);

            s.BackCornersCovered = true;
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.StokerSetup).AllDone);

            // Jammed stoker reverts floor-lever step to current
            s.StokerJammed = true;
            var jammed = FiringChecklists.Evaluate(s, ChecklistId.StokerSetup);
            Assert.Equal(1, jammed.CurrentIndex);
            Assert.Contains("Reverse", jammed.Steps[1].How);
        }

        [Fact]
        public void DriftingAndLayoverTracksThrottleGearBlowerAndDamper()
        {
            var s = new ChecklistSnapshot
            {
                Throttle = 0.4f,
                ForwardSpeedMs = 15f,
                Cutoff = 0.3f,
                DriftingHookedUp = true,
                Blower = 0f,
                Damper = 1f,
            };
            Assert.Equal(0, FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover).CurrentIndex);

            s.Throttle = 0f;
            Assert.Equal(1, FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover).CurrentIndex);

            // Drifting valve or full gear stops drifting wear -> satisfies step 2
            s.HasDriftingValve = true;
            s.DriftingValveOpen = true;
            s.DriftingHookedUp = false;
            Assert.Equal(2, FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover).CurrentIndex);

            s.Blower = 0.2f;
            Assert.Equal(3, FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover).CurrentIndex);

            s.Damper = 0.4f;
            Assert.True(FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover).AllDone);
        }

        [Fact]
        public void DriftingFastWithByPassValvesHookedUpStaysUnchecked()
        {
            // S282 with by-pass valves drifting fast and hooked up: F6 says "Drifting hooked up",
            // so the protection step must NOT falsely tick until wear stops.
            var s = new ChecklistSnapshot
            {
                Throttle = 0f,
                ForwardSpeedMs = 25f,
                Cutoff = 0.3f,
                HasByPassValves = true,
                DriftingHookedUp = true,
                Blower = 0.2f,
                Damper = 0.3f,
            };
            var c = FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover);
            Assert.True(c.Steps[0].Done);
            Assert.False(c.Steps[1].Done);
            Assert.Equal(1, c.CurrentIndex);
            Assert.Contains("full gear", c.Steps[1].How);

            // Once no longer wearing (slowed down, full gear, or drifting valve open), step 2 ticks.
            s.DriftingHookedUp = false;
            c = FiringChecklists.Evaluate(s, ChecklistId.DriftingAndLayover);
            Assert.True(c.Steps[1].Done);
            Assert.True(c.AllDone);
        }
    }
}
