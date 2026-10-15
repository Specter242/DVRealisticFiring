# Changelog

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
