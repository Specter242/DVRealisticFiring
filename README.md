# Realistic Firing for Derail Valley

A Unity Mod Manager mod with zoned coal fires, mechanical stokers, oil burners,
live-steam injectors, boiler water simulation, automatic firemen, and chimney
effects. Supports stock steam locomotives and selected Custom Car Loader
locomotives, with optional Multiplayer integration.

Requires Derail Valley and Unity Mod Manager. Install custom locomotives through
Custom Car Loader separately. Game and third-party mod assemblies are not included.

Build with the .NET 10 SDK and your own installed game assemblies:

```sh
dotnet build RealisticFiring.slnx -c Release -p:DvInstallDir="/path/to/Derail Valley"
dotnet test tests/RealisticFiring.Core.Tests -c Release
```

The core tests run without the game. On Linux, `scripts/check-adapter.sh` also
checks game adapter compatibility under Mono; set `DV_INSTALL_DIR` for a custom
game location.

Build the effects bundle with Unity 2019.4.40f1, an activated Unity license, and
Python 3 with NumPy and Pillow installed:

```sh
UNITY_EDITOR="/path/to/Unity" scripts/build-fx.sh
DV_INSTALL_DIR="/path/to/Derail Valley" scripts/package.sh
```

The package is written to `dist/RealisticFiring-<version>.zip`. Extract its
`RealisticFiring` folder into the game's `Mods` directory.

Source code is available under the [MIT license](LICENSE).
