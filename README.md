# Realistic Firing for Derail Valley

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
