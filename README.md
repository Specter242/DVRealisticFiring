# Realistic Firing for Derail Valley

**1.19.0** (2026-11-13): the fire in the firebox redrawn (broken coal lumps, a bed glowing from its gaps, flames by what the fire is burning, walls lit by it), a brick arch in the S282's and S060's fireboxes, the chimney's exhaust let out beat by beat and attached at the stack's rim, the engine's steam drawn behind the smoke where it is behind it, an Auto smoke quality, shed stands that cost no frame time while you are away, flames no longer showing under the cab after the fire door was opened on a smoky fire, and the fixes prepared as 1.18.1, which was never published (wood and rags in VR, crates and tins for every player, the S282's injector in VR, the multiplayer hello message). The owner played private builds with the new fire, the arch, and the exhaust on the S282 (desktop). Isolated game runs on a clone of the game captured the S282's and S060's fireboxes in every state of the fire and timed their frames, captured the exhaust with Realistic Firing alone and with Realistic Stack Effects drawing, ran Auto stepping the smoke down and back up, and passed the shed stands' checks, each on its own branch; the firebox and exhaust work also ran together. The code of this release, with all of them merged, ran once more headless on the S282, the firebox in every state, without an error from the mod. The released zip itself then ran beside the build the owner played, filming the chimney's exhaust and the engine's steam on two starts and a slog: they look the same, and neither logged an error from the mod. Not run in game: Realistic Stack Effects' own zip, Auto and the resting shed stands in the merged build, the car packs' engines with the new fire, VR, and live multiplayer; the VR and multiplayer fixes were checked by unit tests and by the adapter checks against the game's and the Multiplayer mod's own code.

**1.18.0** (2026-11-11): lighting a cold fire with wood and an oiled rag, with free firewood crates and rag tins at engine sheds and steam spawn tracks; the engine's own steam drawn as the plume is, by the weather; better frame rates from the chimney smoke and the shed stands; smoke and steam no longer lit by a set sun; shed-hose hints that name each engine's socket; the VR lever fix; and a log error at quitting fixed. The owner played the fire-up on a standing S282 at Machine Factory (desktop) in two private previews, and the engine steam on the S282, with Realistic Stack Effects, in three. Isolated game runs on a clone of the game covered the S282's fire-up through the game's own interactions (reaching in, laying, throwing, oiling and lighting the rag, the wood catching, save and reload, Lost & Found), a rag lighting the 2901's burner once its boiler, valves, and oil were set by the harness, all 24 supply places on the base map, the wood's look and burn-down, the engine steam, and the clean quit. Another threw wood through each fitted cab's own firehole (S282, S060, JK3, FEF-3, S484-840, S464-730, both 8Fs, QJ) and a lit rag into the 2901's burner: thrown straight through the open door or from the firing position, it went in on every one; thrown at the plate beside or below the door, it stayed out; laid by use, it went in. The frame-rate work was timed in game against 1.17.9 on the code before it joined the engine steam and the fire-up; the joined build was checked by its builds and tests. Not run in game: VR (its throws, the oil can's and lighter's VR use, and the lever fix in a headset), live multiplayer, a moving engine, the supply places on custom maps, and the new hose hints by eye; those were checked offline.

**1.17.9** (2026-11-09): fixed GWI injector priming, an oil fire lost on reload, the unlimited oil heater and melted-plug damping; left unlisted steam locomotives stock by default; added the new-player notice, presets and shed-hose hint; and hardened multiplayer guests, save loading and failure handling. Headless game checks covered both GWI cabs' injectors. An isolated game run of most of the other work, on a clone of the game, loaded the mod with every patch, fitted the S282's cab, raised steam, saved and loaded, and contained a forced firebox failure. Not run in game: the oil-fire and heater fixes on the 2901, an unlisted locomotive, the presets and notices by eye, the older-save fix, the larger-screen fire sampling, VR, and live multiplayer; those were checked offline.

**1.17.8** (2026-11-08): improved access to the S282's brakes and stoker valves from across the cab, and changed pressure hints and firing instructions to bar. Isolated native game checks covered interaction rays, normal shoveling and rebuilt controls. The injector water wheel was also checked in the game; the fireman's blower pressure protection was checked offline. No physical mouse play, VR, live multiplayer or full replay of the reported cab-mod combination was run. The revised booklet was rebuilt and asset-checked; its readability was not checked in game.

**1.17.7** (2026-11-04): added LittleNiko36's S2102-870 QJ 1.0.2 and closed the injector steam valve when locomotive fees shut its water control. The owner fitted and accepted the QJ cab in game. Its final physical grate sizing and throttle circuit, and the fee-shutdown fix, were checked offline, without new in-game tests. No new loaded-road, save/reload, hose-filling, VR or live multiplayer check was run. The updated booklet was rebuilt and asset-checked; its revised pages were not checked for readability in game.

**1.17.6** (2026-11-03): quieter injector overflow, more reliable multiplayer stoker coal feed, the 2901's tender sandbox, town smoke fees, and clearer hose-location guidance. The owner tested the tender sandbox on desktop. The new injector sound, stoker fix, and smoke-fee behavior were checked offline and have not been tested in game; no new VR or live multiplayer test was run. The booklet page was rebuilt and asset-checked, but its in-game readability remains untested. Additional plume timings do not change smoke behavior or claim an FPS improvement.

A Unity Mod Manager mod with zoned coal fires, mechanical stokers, oil burners,
live-steam injectors, boiler water simulation, automatic firemen, and chimney
effects. Supports stock steam locomotives and selected Custom Car Loader
locomotives, with optional Multiplayer integration.

Requires Derail Valley and Unity Mod Manager. Install custom locomotives through
Custom Car Loader separately. Game and third-party mod assemblies are not included.

Download the installable ZIP from [GitHub Releases](https://github.com/Specter242/DVRealisticFiring/releases/latest).
Start with the [firing guide](FIRING-GUIDE.md): how to fire well, each engine,
the stoker, oil, and water, with a quick-reference table. The ZIP carries it as
`FIRING-GUIDE.html`, which opens in any web browser.
See the [changelog](CHANGELOG.md) for version history. Release tags contain
sanitized source snapshots; older tags describe the behavior of that version.

Build with the .NET 10 SDK and your own installed game assemblies:

```sh
dotnet build RealisticFiring.slnx -c Release -p:DvInstallDir="/path/to/Derail Valley"
dotnet test tests/RealisticFiring.Core.Tests -c Release
dotnet test tests/RealisticStackEffects.Core.Tests -c Release
```

The core tests run without the game. On Linux, `scripts/check-adapter.sh` also
checks game adapter compatibility under Mono; set `DV_INSTALL_DIR` for a custom
game location.

Build the effects bundle with Unity 2019.4.40f1, an activated Unity license, and
Python 3 with NumPy and Pillow installed; packaging also needs the Python
Markdown package for the firing guide:

```sh
UNITY_EDITOR="/path/to/Unity" scripts/build-fx.sh
DV_INSTALL_DIR="/path/to/Derail Valley" scripts/package.sh
```

The package is written to `dist/RealisticFiring-<version>.zip`. Extract its
`RealisticFiring` folder into the game's `Mods` directory.

Source code is available under the [MIT license](LICENSE). The S282 injector
model is by Lilla, supplied through Cruzer and used with permission and credit
to Lilla. Game textures and locomotive assets are not included.

## Illustrated firing manual

The handheld firing manual has diagrams, photographs, and a clickable table
of contents. Open the cover and select a subject to turn to its page; on
desktop use mouse mode while holding it. Normal booklet page controls still
work, and your page is saved. It comes with starting equipment that includes
the steam shovel. You can also use **Controls → Equipment → Give firing
manual** in the mod settings (F8); in multiplayer, ask the host for a copy.


Only the supported locomotive fits are included in this release.
