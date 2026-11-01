# Realistic Firing for Derail Valley

**1.17.3** (2026-11-01): the fireman backs off near the safety valve, an empty oil pan accepts the lighter, faint chimney smoke completes its fade, and hosts can hide RF's on-screen helpers. Verification is offline: model tests, adapter checks, builds and package checks. None of these fixes have been run in game, in VR or in a live multiplayer session. No FPS improvement is claimed.

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

Source code is available under the [MIT license](LICENSE).

## Illustrated firing manual

The handheld firing manual has diagrams, photographs, and a clickable table
of contents. Open the cover and select a subject to turn to its page; on
desktop use mouse mode while holding it. Normal booklet page controls still
work, and your page is saved. It comes with starting equipment that includes
the steam shovel. You can also use **Controls → Equipment → Give firing
manual** in the mod settings (F8); in multiplayer, ask the host for a copy.
