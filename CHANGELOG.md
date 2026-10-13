# Changelog

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
