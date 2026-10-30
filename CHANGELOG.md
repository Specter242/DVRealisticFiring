# Changelog

## 1.17.1 (2026-10-30)

- Fixed the fire showing through the locomotive body in external views.

## 1.17.0 (2026-10-30)

- Added an illustrated firing manual with a clickable contents page. Steam
  starting kits include it; in an existing save, use F8 → Controls → Equipment
  → Give firing manual.
- Shed hoses return after reloading a save. Empty, unfired boilers cool during
  play and sleep or fast travel, allowing a cooled boiler to refill from a hose.
- Added a low-water alarm volume slider. The whistle follows steam pressure;
  the low-water indication remains active when muted.
- Medium and Low firebox quality reduce coal geometry and surface updates.
  Unseen fireboxes do less work while keeping their firelight.
- The JK3 and 8F fireman can operate the right injector with realistic
  injectors turned off.
- Heat shimmer draws before smoke and shares TurboTurbo's screen copy.

## 1.16.3 (2026-10-28)

- Fixed duplicate chimney effects accumulating when a locomotive's particles were refreshed, adding smoke and slowing the game down.
- The fireman's water-level reading and holding target now match the cab glass and F6, including on the JK3.
- A warm afternoon's exhaust now keeps a short white steam at the stack instead of none.

## 1.16.2 (2026-10-27)

- Bailing off has moved to the Realistic Brakes mod.
- Coal, lumps, and embers transition more gradually into and out of white heat.
- The settings menu now uses tabs, clearer explanations, units, and restart labels.
- Fixed the tablet's VR orientation after inventory and distance grabs, and errors when touching its tabs.
- Added a Fireman page to the tablet with orders, engineer's calls, status, and warnings.
- Wet coal and fines affect stoker feed, load, packing, and jet throw together. Retained table coal is saved and can be cleared with the shovel or fire iron; the automatic fireman attends to it too.
- Between two locomotives, the tablet, overlay, and controls follow the engine you were last in.
- Smoke keeps its spread when a train slows. Smoke left behind is no longer pulled along as though it were still inside the stack.
- Medium and Low smoke quality fade out faint smoke sooner to reduce long trails; High keeps its existing appearance.

## 1.16.1 (2026-10-27)

- The hottest coal fires now reach white-hot brightness across the coal,
  embers, and flames. The layers beneath the lumps follow the same surface.
- Small bed flames are shorter and form more connected groups.
- Dense firebox smoke absorbs more light and obscures the fire and walls.

## 1.16.0 (2026-10-26)

- Optional Realistic Stack Effects integration hands chimney drawing to that mod and feeds it the realistic fires through a separately loaded add-on. Without it, Realistic Firing keeps its own plume.
- A held and mountable firing tablet uses the stock gadget, inventory, shop, and save systems. Fire, Water, and Engine pages share F6 readings while retaining each tablet's page independently.
- Starting kits containing the shovel include a tablet; the stock inventory safeguard checks all saved storage before adding a missing one.
- The tablet has a 16 mm shell, sliced button and grid borders, and a cached mipmapped screen released while out of view. Desktop held tabs use mouse mode; F6 remains optional.
- Independent firebox quality presets halve or quarter open-door volume sampling. High retains full detail; shut-door sampling, light, coal, and animation keep their existing budgets.
- Stoker front-jet gauge hands use red for left and blue for right, with matching face labels.

## 1.15.0 (2026-10-26)

- The firebox uses a volume for flames and smoke, a simulated airflow, and a heap of coal lumps. Flame sources follow gas burning over each grate zone; the bed and walls share the fire's palette. Small flames keep their roots and vary in height, with sharp edges and moving folds.
- A shut door retains a reduced fire render (24 ray steps, airflow and source uploads at 4 Hz), while the Unity point, fill, and bounce lights share the fire sources' radiance. Daylight reaches a cold box through the calibrated open firehole.
- Stoker steam uses five pressure-driven, continuously animated streams in one mesh (`StokerJetStream`, `StokerJet.shader`). Coal lumps, burning fines, and dust retain their separate effects.
- `SmokeQuality` lowers plume resolution and particle count; VR and forward rendering use fewer particles. The plume composes with nearby steam, and its billows hand off to wisps earlier at speed.
- The bed's mesh, relief, and coal-lump uploads wait while unseen; nearby shut doors keep their fire render and per-frame motion controls, with reduced source/airflow updates. The fire step, tended-car lookup, and offscreen plume enumeration avoid recurring allocations. Plume-owned materials are destroyed with their plumes, and coal stores use weak keys.
- Gear B keys control the 2901's atomizer. The S060's injector has the next larger capacity and a wider turn-down range.
- Shovelfuls always use the selected real mass; old stock-size settings reset to 3 kg. Extinguishing or dumping clears the fire's ignition state.
- The fireman accounts for auxiliary steam demand while standing. A hot boiler blown dry no longer bursts during an unpressurized refill. VR hands can take shed hose couplings without their stand attachment fighting the grab.

## 1.14.0 (2026-10-24)

- The 2901's firing valve takes a stop (`FiringValveStop`, `FiringValveStopClick`): a click sets it where the valve stands and the oil never drops under it (`OilFeed.FiringValve`); another click takes it out. The fireman works above it and takes it out to shut the valve. Saved with the oil firebox (`rfValveStop`); in multiplayer each machine keeps its own, and a client's feed joins its firebox whichever is built first.
- `Settings.SteamVentVolume` sets the volume of steam blowing off: a melted fusible plug, an injector's overflow, and a shed's open steam hose.
- A deleted tender's tank is dropped before the injectors read it (`InjectorRig`).
- Every heat shimmer draws after the smoke from one named screen copy (`HeatHaze`, `_SharedGrab_Transparent1`): one grabbed before the smoke cut a hole through the plume.
- Each puff leaves a frame's travel back from the lip: Unity moves a particle emitted now by its velocity before drawing it, so a moving train's plume led its stack.
- The plume is lit by the sun and the sky's ambient light (`ChimneySmoke`), as one volume (`PlumeVolume`: the particles' opacity gathered into a grid, its shade from the sun and its outward way), a moonlit gray at night, and fades within a few meters of the camera.
- Thick steam and dense black smoke are drawn as six-way lit rolling puffs (`make_rolling_steam.py`), upright to the world, handing off by age (sooner for a light beat) to 1.13's wisp flipbook with its relief baked in (`smoke_wisp_baked.png`), each wisp its own row and slowing turn. A puff's thin edge on the plume's silhouette frays into its wisp's strands (`PlumeAloft.ExtraOf`, measured across the plume at its own age).
- Smoke's opacity is shared by the particles overlapping it, by the emission rate as each left (`PlumeAloft.Drawn`), so a well-fired engine's light haze stays light. A thinning wisp grows only as much as its strands' reach shrinks (`PlumeAloft.Thinning`).
- The blower's jet makes white steam in cool or damp air (`PlumeModel`), and a standing column rises more slowly.

## 1.13.9 (2026-10-20)

- Superheated steam fills the cylinders by volume (`SteamCylinders.SuperheatedVolumeRatio`: Callendar's equation, fitted to IAPWS-IF97 within 0.54 % from 10 to 25 bar and up to 400 °C), after the walls take their share of the superheat (`SuperheatAtCutoff`); the ports pass in proportion to √(p·ρ).
- `BoilerFit`: the Hudson's, FEF-3's, S484-840's, 2901's, and 8F's burn times set so the fire's most heat makes the prototype's tested evaporation at 2.6 MJ a kg (`FireboxParameters.BurnTimeFor`); the box and its efficiency stay the maker's.
- `FireboxParameters.EfficiencyAt`: the efficiency falls in a straight line from `LightFiringEfficiency` (0.8) to the maker's at the most, for the coal fire, the oil fire, and `AutoFireman.RateFor`.
- `SafetyValveFit` capacities resized to the standing fires on the sized boilers and the new curve.
- An oil fire's beats take the tuning's haze share as a coal fire's do (`PlumeModel.BeatHaze`).
- The plume's cloud sorts far to near, with a dark puff's soot core drawn in it (`ChimneyPlume`).
- The fire iron keeps the shovel's `ShovelMountAnchor`, so the shovel mount's sliding snap point takes it.
- A seating bench for a new engine's added parts, development-only (`Settings.DevSeatingKey`, unbound, never in the menu).

## 1.13.8 (2026-10-19)

- The fireman judges the water as it would stand level (`FiremanReadings.GlassTilt`, the game's glass shift from the boiler's pitch, `BoilerWaterRig.GlassTiltNow`) and bounds his goal by the steepest climb and descent within the water's lead (`GlassTiltHigh`, `GlassTiltLow`): under `GlassTop` (0.9) on the climb, over `GlassLeast` on the descent.
- A shed hose hanging on its stand runs its rope only within 15 m once it has settled (10 s hanging); one in use keeps 150 m (`ShedStands.SleepFar`).
- `CarNames.LogName`: a car goes by its livery until the game numbers it, so the log no longer reads `TrainCar.ID` early.

## 1.13.7 (2026-10-18)

- The S060's engines take the stock engine's steam a stroke too (`EngineCylinders.MarkStockSteam`): already without wet walls, its clearance took 1.25 to 1.45 times it at 10 to 20 % cut-off.

## 1.13.6 (2026-10-18)

- The JK3's engines take the stock engine's steam a stroke (`SteamCylinders.StockAdmission`, `EngineCylinders.MarkStockSteam` by livery): the model's clearance and condensation took 1.5 to 1.9 times it at 10 to 20 % cut-off, more than its boiler was balanced for. Pressures, torque, and valve events stay the model's.

## 1.13.5 (2026-10-18)

- Working lightly (under 40 % of full work heard), the fireman closes the damper over his mark building for work ahead too (`AutoFireman.Blower`, `LightWork`); standing builds keep it open for the blower.

## 1.13.4 (2026-10-18)

- The plume tuning window that came along in 1.13.3 is off: no menu entry or default key (`Settings.DevPlumeTestBedKey`, unbound), and its saved tuning is not restored unless that key is bound.

## 1.13.3 (2026-10-18)

- The exhaust's white from the air's temperature and humidity (`SteamCloud`, `Psychrometrics`, `PlumeAir`): each dilution's heat and water balanced as the steam, a share of the fire's flue gas, and the air mix, its thickness seen across forming and evaporating as the air mixes in (`12 × U^(1/3) × t^1.3`); its thickest against a cool, damp reference scales the steam's depth (`PlumeModel.Weathered`).
- The air: a season setting's day at the game's hour, damp in the rain (`PlumeClimate`), or DV Seasons' temperature when it runs, read by reflection (`SeasonsMod`). The mod's ambient (`Ambient`) gives DV Seasons' temperature to a cold boiler's water and steel, the tender's water and oil (`OilFireInputs.AirTemperature`), and a shut injector; without it each keeps its value. The adapter check finds DV Seasons' state and the stock boiler's one outside temperature.
- A coal fire's beats carry its haze (`PlumeModel.CoalPuffHaze`, "haze in a coal fire's beats" 0.5).
- The stack's bore and outside radius read from its rim; particles at least the bore wide through the neck and spreading on (`ChimneyPlume.Least`), held from hanging beside the stack (`Widest`), narrowed while fast by their speed through the air, growing as they thin to keep their width through the shader's fraying (`PlumeAloft.Thinning`); smoke emitted in the stack shows at once.
- The train's wake 0.3 of its speed over 1.5 m (`PlumeTuning.WakeCarry`, `WakeDepth`); the plume's systems in a fixed draw order (`ChimneyPlume.DrawnBefore`); the jet's streaks colored by their age.
- The heat shimmer's bend set from code (2 % of the view), widening with the hot gas and weakening as it widens, living about 3 s.

## 1.13.2 (2026-10-17)

- The fire iron's lookup is redirected inside Skin Manager's item loaders too (`SkinManagerFireIronLoading`), which make every shop and saved item in its place of the game's own load; Realistic Firing loads after Skin Manager. A game method with no load to redirect is a note where Skin Manager covers it, otherwise a warning naming the other mods patching it. The adapter check patches an installed or given Skin Manager first and reads back the combined code.

## 1.13.1 (2026-10-17)

- A stoker whose tender port reads no coal takes the tender's coal pile's (`RealisticStoker`, its coal reached, source, and direction exposed).
- The oil fireman runs no more oil than the atomizer's steam atomizes (`OilFire.AtomizableOil`, `AutoOilFireman`), asks for at least one 2.5 % notch, and a hand counts a control it wants open that sits shut as stuck (`FiremanHand`).
- The fireman throws the stock shovelful with the setting at 0 (`FiremanRig.ShovelfulMass`).
- The fireman's state every half minute while the player is away, with the status log on (`FiremanRig.LogAway`); a yielded control is always logged.

## 1.13.0 (2026-10-17)

- Shed water hoses (`HoseKind.Water`): a water stand beside each shed stand (`ShedStands`, each shed's place in `WaterBySite`, single stands in `WaterByStand`), its 15 m hose's Boss coupling and clamp scaled half as big again (`HoseKind.Scale`); each engine's filling connection seated per livery (`ShedConnections.WaterSeated`, `WaterFittings`, its plug cock turning with the hose). The rig washes a cold boiler out once a plugging and fills it by the valve's opening (`InjectorRig.StepHose`); the F9 key and `WaterPoints` are gone. Water runs from an open hose (`HoseWater`).
- Direct steaming at the three big roundhouses' repair stands (`Roundhouse.RepairStall`): hot water at 180 °F and the plant's 150 psi steam through a second handwheel (`ShedStands.MakeRepairStand`, `SteamValveStem`), its flow choked by Napier's rule and falling to none at the plant's pressure, as fast as the start-up boost (`InjectorRig.StepDirectSteam`), held off with the glass at 95 %, its water no feed for the boiler's solids (`BoilerWaterRig.AfterFeed`); the plant's water and coal added to the loco's debt tracker (`PlantFee`).
- A shed hose in an engine's connection gets a trigger grip out past the engine's colliders (`HoseGrip`); the coupling's hose sleeve is gone.
- Valve-only stands in the multiplayer sync (`ShedNet.RegisterValve`); the hose's state in the client mirror.
- Patches applied a class at a time, failures named (`Main.PatchEachClass`).
- Oil: pooled oil drains from a cold pan and the box counts clear under half a flareback's fuel (`OilFire.SafeToLight`).
- The JK3's ashpan rod drawn without its hanger (`AshpanLever.DropHanger`).

## 1.12.0 (2026-10-17)

- Shed blower lines (`Shed/`): a stand at each of seven roundhouses' 48 stalls and at six engine sheds (`ShedStands`, set on the floor under it as the shed loads), each a copy of a service station's diesel hose (`ShedHoses`: its own rope mesh, 81 points over 12 m, the station's runtime parts stripped, its texture scaled with its length) with a handwheel valve; each fired engine's Barco fitting seated per livery (`ShedConnections`; the S060's on the right, none on the 8F); the fittings built as turned meshes with seamless grime (`FittingMesh`, `FittingGrime`, `BlowerFittings`: Barco body, Dixon Boss coupling, gooseneck clamp); points held on raised ground between the rope's job and its mesh's (`HoseGround`, a prefix on `RopeMeshGenerator.Schedule`); steam from an open line (`HoseSteam`); a plug fallen under a floor put back (`StandPlugWatch`); far hoses' solvers paused. House steam drives the blower (`BlowerFit.NozzleSteam`) and an oil burner's atomizer and heater (`RealisticOilFirebox.HouseLine`); the service track's steam line and its key are gone.
- The game's firebox sparks are off on every engine (`FireboxFlames.KeepSparksOff`).
- The fireman fires in step with the start-up boost (`AutoFireman`, `FiremanReadings.StartupMultiplier`) and keeps the feed pump turning light (`AutoFiremanBase.PumpLeast`).
- Each step of a car's setup is guarded and logged (`SetupGuard`).
- Smoke tints the steam (`PlumeModel.SmokeTint`), a clean coal fire clears its stack (`PlumeModel.CoalClearStack`), heat shimmer (`PlumeModel.Shimmer`, `HeatHaze.shader`), and the plume sorted as if at the stack (`ChimneyPlume.SortAtStack`).
- A renewed boiler's steel starts as hot as its water (`BoilerWaterRig`, `BoilerMetal.Settle`).
- Wet coal (`CoalMoisture`, `CoalStore`; the Hudson's bunker door keeps the rain off).
- The stoker manifold's plates hang from their valves (`StokerCab.HangLabel`).
- Unity Mod Manager's update feed (`repository.json`, `Info.json`'s HomePage and Repository).

## 1.11.2 (2026-10-16)

- Aim by the keypad or the grate (`Settings.Aim`, `ShovelAim`): the grate follows the look to the drawn bed (`ZonedCoalBed.TryAim`, the 1.8.1 walk), taking the keypad's zone without its keypad where the look never passes over the bed.
- Fit each fired engine's air pumps from its prototype (`AirPump`, `BlowerFit.FitAirPump`): two 8½-in. cross-compounds (124 L/s of free air, 0.38 kg/s of steam, full from 12.5 bar) on the Hudson, FEF-3, S484-840, and 2901, one 9½-in. (18.4 L/s, 0.18 kg/s, full from 10 bar) on the rest; the UP 4-8-4s' main reservoir 1,491 L, set on the brake system too.
- Fit each fired engine's safety valves from its prototype (`SafetyValveFit`): lifting at its working pressure (the FEF-3 and S484-840 at 300 psi, the 8F at 225), reseating 0.2 bar under, passing 1.25 times the steam of its standing fire.
- The fireman holds 1.5 bar under a damaged boiler's burst pressure (`FiremanReadings.BurstPressure`, `AutoFiremanBase.BurstMargin`); the overlay warns when it is under the safety valve (`FiringAdvice.Bursting`).
- The firing guide and the overlay say how to sand the flues: load the shovel at the sand box, throw it through the peep hole.

## 1.11.1 (2026-10-15)

- Draw the aiming keypad by default (`ShowAimKeypad`), and turn it on once for settings saved before (`DefaultsVersion`).

## 1.11.0 (2026-10-15)

- Aim the shovel, rake, and fire iron by where the look crosses each cab's fitted firehole (`FireholeOpening`), in thirds as a keypad, held to the hole's edges, while the look is on the game's door target; drop the 1.8.1 bed aim. A setting draws the keypad on the hole (`AimKeypad`).
- Aim VR lumps by their flight: before the fire's trigger counts a lump, follow it against the cab under gravity in the bed's frame to where it lands (`ThrownCoalPatch`).
- Size each engine's injector to feed its whole boiler (Baldwin's sizing by cylinder bore; Sellers' capacities): S282 No. 12, 6.0 kg/s, its least halved as one lever for two; S060 No. 7, 2.1; JK3 2.5 each; 2901 12.6.
- Soot the tubes from a coal fire's smoke and scale the boiler from the feed's solids (`BoilerFouling`), keeping their share of the heat from the water, charged as mechanical powertrain wear and cleaned by its repair; a washout loosens half the scale.
- Wear the engine drifting with the throttle shut and the reverser under three-quarter stroke (`Drifting`), with overlay advice.
- Lower a cold boiler's level by its water's shrink below 100 °C (`ColdWater`).
- Cut the injectors' water when a nearly empty tender's water runs off its outlet on a grade or under acceleration (`TenderOutlet`).
- Throw the stoker's coal from 0.093 m under the foot of each cab's firehole.
- Hand over a bought fire iron the shops' coroutine could not make, from the template at the shop's counter, rather than refunding it; rebuild the template if destroyed; warn when the purchase lookup finds no load to redirect.
- Let the cursor's ray pass the 2901's firing valve to the burner's valves behind it (`FiringValvePassThrough`), and size the loose-wheel valves' grip and hover spheres to the wheel drawn.
- Scale the feed pump's sounds by a volume setting.

## 1.10.0 (2026-10-15)

- Draw hardest at the back of the grate under a sealed arch (typical back 1.12, front 0.88 from 0.9, 1.12; Acker, RME Apr. 1930: the front pulled 70 % of the back); the S060, 8F, and FEF-3s milder, the JK3 unchanged. Lower the stoker's working front jets to 0.6 and the fireman's easing to 0.1.
- Keep a cold boiler's water at 25 °C when the game makes it (it made it saturated at 1 bar) and save it below boiling (`rfWaterTemp`); show the water's temperature on the overlay's boiler dial until it boils.
- Give the boiler its steel (`BoilerMetal`): 2.5 kg a kg of water at 490 J/(kg K), following the water over 20 s.
- Stand the glass above the water by the steam in it (`BoilerSwell`): up to 0.05 of the boiler at full steaming, less what the feed condenses, plus 0.2 per bar/s of falling pressure (at most 0.04 either way); the crown sheet goes by the water alone.

## 1.9.1 (2026-10-15)

- Hold the plume under a tunnel's roof: sample the game's per-track ceiling height (`RailTrack.SampleCeilingHeight`) every 2 m of the stack's travel into a grid in the cloud's space; under a roof a particle rises to it, spreads along the bore as a ceiling jet slowing over 20 s, and is held between assumed walls of a 5 m single-track bore, the bore's air moving with the train at 0.3 of its speed; the jet and cinders stop at the roof over the stack.
- Shorten the plume particles' life from 300 s to 240 s.

## 1.9.0 (2026-10-14)

- Draw the stoker's spray: procedural coal lumps lit by the fire, burning fines carried in by the draft, and the jets' steam, thrown from under the door into the firebox; stop it with the door shut and open on it in full flow. Add the stoker's operation to the firing guide.
- Draw the fireman's orders on the fire overlay's card (the overlay's type, plates, and boxes shared in `OverlayStyle`): order and call plates, rows read once a second, warnings in the next-step box, and a small card when put away.
- Fit each fired engine's steam air pump to period steam (0.08 to 0.26 kg/s flat out, not the game's 1 kg/s) and send its exhaust through the blast pipe at the exhaust's entrainment.
- Hide a CCL engine's stock flame, on a child of the fire object, under the bed's flame cards.

## 1.8.1 (2026-10-14)

- Aim the shovel, rake, and fire iron where the look meets the drawn coal (followed into the firebox in the zoned bed's frame), falling back to the door aim without a bed.
- Say the firebox is full when the shovelful does not fit, at the click and at the throw.

## 1.8.0 (2026-10-14)

- Support Emerald's LMS 8F 1.2.1: the zoned fire on its turned coal pile (the bed built in a frame of the pile pivot's axes taken across, up, and along the car; a mesh turned under its pivot measured by its corners), its two-valve injector graph, blower, superheater, and fusible plug. Its cab handles become the injector steam valves, stepping Off, Prime, Feed one notch a wheel click; the cab's under-seat regulators are cut from the cab mesh and turned as the water regulators; the tender-front handles work saved tender water valves.
- Show every pressure in bar.
- Move the default keys off every key the game binds (Ctrl crouch, F cab places): F6 overlay, F7 fireman, F8 settings, F9 hydrant, F11 steam line, Z grate shake, C hand pump; move keys still on the old defaults once.
- Read the overlay's glass over each cab's gauge-glass span.
- Lay the model's reference bed, burning, at loco start-up.
- Stand the flame cards on the drawn bed; fit the overlay's next step in its box; trim the settings menu; tell a multiplayer guest on another version why before disconnecting.

## 1.7.5 (2026-10-13)

- Give every shader a single-pass stereo variant on the mod's own keyword (Unity strips its own from a bundle built without VR), switched on for single-pass stereo cameras; ship the two surface shaders as patched generated code.

## 1.7.4 (2026-10-13)

- Superheat the steam: each fired engine's prototype superheater (Type A or E), building with the fire and the steam drawn and lagging by minutes; steam short of superheat condenses on the cylinder walls (Goss's tests, Heck's formula). The JK3 stays saturated and the S060 is exempt.
- Keep an injector feeding when its engine loads from a save.
- Seat the S282's injector steam lever at a fixed offset from its handle instead of by a ray; cast the other cabs' rays in the car's physics pose.
- Lay out the Hudson's booster procedure in the firing guide.

## 1.7.3 (2026-10-13)

- Free every shop register when the game fails to hand over a purchase: deliver what was made, restock and refund the rest, and log the exception.

## 1.7.2 (2026-10-12)

- Find the service stations once a scene, and check them only while shop steam is wanted, instead of searching the scene every second for every oil engine.
- Redraw only a dial's needle when it moves, not its whole face.
- Hook the fire iron's shelf tag up on registers that were already on.

## 1.7.1 (2026-10-12)

- Redesign the fire overlay as a card: fire, draft (with the smoke's color), glass, and boiler dials over Fire, Water, and Engine pages, with the next step in a fireman's words.
- Keep a cold, empty grate dark, and draw the bare grate where no coal lies.
- End the game's wagon pool cleanly when a wagon fails to build, so a save no longer hangs while loading.
- Sync physics before placing cab fittings, so the injector steam lever no longer lands inside the boiler.

## 1.7.0 (2026-10-11)

- Split the injector into a water valve and an Off / Prime / Feed steam lever on the S282, S060, JK3, Hudson, 844, S484-840, and 2901.
- Size each fired engine's blower and natural draft from period practice.
- Show each step of firing up a cold oil engine, and accept any service station with water, loco parking track, or roundhouse stall for shop steam, the hydrant hose, and washouts.
- Give the JK3 its own firebox character.
- Find Multiplayer 0.1.16 cab controls by their port feeders, including both JK3 injector handles, and ignore interior-unload notifications.
- Resolve fire-iron prefabs only when restoring saved items or spawning purchases.
- Keep effects and fire-iron assets intact through main-menu bundle cleanup.

## 1.6.1 (2026-10-11)

- Leave the B&O C-16 entirely stock, including its injectors, boiler water, and cylinders.

## 1.6.0 (2026-10-11)

- Add an automatic fireman for coal and oil engines.
- Support JK3 1.0.1 with independent left and right injectors.
- Size injectors and feed pumps for each locomotive.
- Renew melted fusible plugs through pit-stop repairs or settings.
- Keep smoke and firebox effects working after returning to the main menu.
- Fix oil fuel limits, saves, multiplayer state, hydrant hoses, and S282 coal-bed placement.
- Widen the exhaust jet at the stack.

## 1.5.0 (2026-10-10)

- Add realistic oil firing to the Santa Fe 2901, including atomization, oil heating, shop steam, flue sanding, and a feedwater heater.
- Add realistic cylinder valve events, expansion, compression, and back pressure.
- Replace round smoke puffs with wisps that rise, spread, thin, and drift with the weather.
- Validate multiplayer support in play.
- Keep the SP GS-4's firing stock; later versions leave the B&O C-16 entirely stock.
- Use bar on added cab pressure gauges and fix S060/S282 coal-bed visibility.

## 1.4.1 (2026-10-09)

- Introduce multiplayer synchronization for firing actions, boiler systems, and the fire iron.
- Introduce experimental oil-burner support, including flarebacks and flue sanding.
- This testing release predates the GS-4 and C-16 compatibility exclusions in later versions.

## 1.4.0 (2026-10-09)

- Add boiler-water impurities, foaming, carryover, blowdown, and washouts.
- Add fusible plugs and a Hudson low-water alarm.
- Tune firebox behavior and stoker distribution for individual locomotives.
- Sell fire irons in shops beside the shovel.
- Improve floor-lever steps, grate-shaker control, exhaust beats, and firebox-smoke output.
- Fix effects lost during world-origin shifts and effects running on cold boilers.

## 1.3.0 (2026-10-09)

- Add clinker formation, graded grate shaking, and a separate drop-grate lever.
- Add the fire iron for slicing clinker and moving the fire, with a heated-tip glow.
- Add ashpan filling, airflow restriction, cleaning, and glow.
- Show gray ash where the fire has burned down and red embers under ash-covered coal.

## 1.2.0 (2026-10-09)

- Add a continuous coal bed with visible fresh coal, embers, ash, and clinker.
- Add flames and firebox smoke that respond to draft, coal, and exhaust beats.
- Place the exhaust jet at the stack lip and prevent smoke showing through stack walls.

## 1.1.0 (2026-10-09)

- Add mechanical stokers, distribution jets, cab controls, and reversible jams.
- Support the NYC Dreyfuss Hudson, S484-840, and UP FEF-3 No. 844 with realistic firing and stokers.
- Add the Hudson's trailing-truck booster and locomotive-specific feedwater heaters and pumps.
- Add injector priming, temperature effects, automatic restarting, and overflow effects.
- Improve idle smoke, cinders, and smoke/steam sorting.

## 1.0.1 (2026-10-08)

- Start locomotives with a clean burning fire instead of a smothering load of fresh coal.
- Correct the overlay's last-shovelful display after startup.

## 1.0.0 (2026-10-08)

- Initial release with zoned hand-fired coal on the stock S282 and S060.
- Add realistic injectors, aimed shovelfuls, raking, grate shaking, boiler filling, and a fire overlay.
- Add chimney smoke, steam, and cinders that respond to firing and exhaust beats.
