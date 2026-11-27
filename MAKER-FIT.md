# Realistic Firing Maker-Fit JSON Standard (`schemaVersion: 1`)

Third-party Custom Car Loader (CCL) locomotive authors can ship a declarative
`realistic-firing.json` file alongside their locomotive mod so Realistic Firing
automatically configures the locomotive's firebox, boiler, draft, cylinders,
injectors, appliances, and cab/shed placements without requiring code changes in
Realistic Firing.

## Discovery & Precedence

At game startup, Realistic Firing scans two tiers of locations (in deterministic
alphabetical order within each tier):

1. **Mod-shipped fits** (lower external priority):
   - `Mods/<AnyMod>/realistic-firing.json`
   - `Mods/<AnyMod>/realistic-firing/*.json` (for car packs containing multiple locomotives)
2. **User-extensible fits** (higher external priority):
   - `Mods/RealisticFiring/Fits/*.json`

Precedence rules:

- **Excluded stock types**, which Realistic Firing leaves wholly stock, stay
  stock: no maker-fit file can enable or override them, whether it names them
  directly by type ID or through a reskin / second livery of one of them.
- **Built-in profiles** (stock `LocoS282A`, `LocoS060`, and flagship locomotives
  maintained directly by Realistic Firing) remain authoritative by default and
  cannot be silently overridden by external JSON files.
- **User fits** (`Mods/RealisticFiring/Fits/*.json`) override mod-shipped fits for
  custom liveries, letting players or authors iterate locally without editing
  another mod's folder.
- **Strict validation & size cap:** Files larger than 512 KB (524,288 bytes) are
  skipped with a warning before reading. Unknown keys, unsupported
  `schemaVersion` values, or out-of-range physics parameters cause that file to be
  skipped with clear log diagnostics; valid files continue loading without
  crashing the game.
- **Multiplayer session check:** A deterministic, order-independent digest of the
  winning loaded maker fits is included in the Multiplayer session handshake so a
  host and guest with mismatched `Fits/*.json` or mod fits are warned on join.

## Schema Reference (`schemaVersion: 1`)

All top-level fields except `schemaVersion` and `liveries` are optional; omit any
section to keep Realistic Firing's defaults or the locomotive maker's own CCL
definition values.

### Top-Level Fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `schemaVersion` | integer | Yes | Must be `1`. |
| `liveries` | string[] | Yes | Non-empty list of unique CCL livery IDs this profile applies to. |
| `fuel` | `"coal"` \| `"oil"` | No | Defaults to `"coal"`. Coal profiles replace the stock firebox with `RealisticFirebox`; oil profiles mark the livery listed for `RealisticOilFirebox`. |
| `summary` | string | No | One-line description logged when the firebox initializes. |
| `firebox` | object | No | Grate geometry, draft bias, coal quality, reversed door, and over-fire jets. |
| `boiler` | object | No | Maximum evaporation sizing, safety valves, and low-water guard. |
| `draft` | object | No | Period blower, natural draft, and steam air pumps. |
| `cylinders` | object | No | Cylinder steam consumption mode, superheater, steam path, and drifting fittings. |
| `injectors` | object | No | Lifting/non-lifting type, rated flow, steam valves (enabled by default even when `injectors` is omitted), water regulators, and two-valve graph. |
| `stoker` | object | No | Stoker capability flags, retrofit option, and distribution habits. |
| `feedPump` | object | No | As-built feedwater heater and pump parameters. |
| `booster` | object | No | As-built trailing-truck booster parameters. |
| `autoFireDoor` | bool \| `{ "fitted": bool }` | No | As-built pneumatic automatic fire door. |
| `cab` | object | No | Seated cab controls, firehole opening, and shed hose connections. |
| `stack` | object | No | Optional chimney geometry hints in metres (visuals only: lip height, bore radius, outside radius). |

### `firebox`

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `grateAreaM2` | number | `0.05` – `25` | Prototype grate area in m² (overrides measured coal mesh area). |
| `grateSlopeDeg` | number | `-30` – `30` | Prototype grate slope in degrees (negative falls toward the front). |
| `slopeOverPile` | bool | `false` | When `true`, `grateSlopeDeg` overrides even a tilted coal pile mesh. |
| `backRowDraft` | number | `0.2` – `3.0` | Relative draft through the back row under the door (default `1.12`). |
| `frontRowDraft` | number | `0.2` – `3.0` | Relative draft through the front row under the tube sheet (default `0.88`). |
| `sideDraft` | number | `0.2` – `3.0` | Relative draft along the firebox side sheets (default `1.07`). |
| `overFireMixing` | number | `0.1` – `3.0` | Secondary air mixing quality over the bed (default `1.0`). |
| `ashFraction` | number | `0.1` – `5.0` | Relative ash yield of the road's coal (`ashFactor` accepted as alias; default `1.0`). |
| `clinkerFactor` | number | `0.0` – `5.0` | Relative clinkering tendency (default `1.0`). |
| `reversedDoor` | bool | `false` | `true` for cab-forward locomotives fired from the front end. |
| `overFireJets` | object | `{ "count": 1..24, "tube": "small" \| "large" }` | As-built steam-air over-fire jets. |

### `boiler`

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `maxEvaporationKgPerS` | number | `0.05` – `40` | Maximum actual steam evaporation in kg/s (mutually exclusive with `maxEvaporationLbPerHr`). |
| `maxEvaporationLbPerHr` | number | `0.05` – `40` kg/s after conversion (~`397` – `317465` lb/h) | Maximum actual steam evaporation in lb/h (converted to kg/s and checked against the `0.05`–`40` kg/s bounds). |
| `safetyValves` | object | `{ "liftBarAbs": 3..40 \| "liftPsiGauge": ~28.9..565.4, "capacityKgPerS": 0.1..50 }` | Safety valve lift pressure (absolute bar or gauge psi; `liftPsiGauge` is converted to absolute bar and checked against the `3`–`40` bar abs bounds) and vent capacity in kg/s. |
| `lowWaterGuard` | string | `"none"` \| `"fusiblePlug"` \| `"lowWaterAlarm"` | Crown-sheet low-water protection. Convenience booleans `fusiblePlug` or `lowWaterAlarm` may be used instead (mutually exclusive). |

### `draft`

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `blowerMaxSteamKgPerS` | number | `0.005` – `10` | Wide-open blower steam flow at working pressure, kg/s (must be paired with `naturalDraftKgPerS`). |
| `naturalDraftKgPerS` | number | `0.005` – `10` | Standing natural draft air flow with no steam, kg/s. |
| `airPumps` | object | `{ "type": "nineAndHalf" \| "crossCompound", "count": 1..4, "mainReservoirL": 0..20000 }` | Period steam air pump(s) and optional main reservoir volume in L (`0` keeps the maker's reservoir). |

### `cylinders`

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `stockSteamPerStroke` | bool | `false` | Keeps the stock engine's steam consumption per stroke for small boilers balanced to stock admission. |
| `superheated` | bool | — | `false` marks a saturated engine (no superheater and native saturated throttle path). |
| `superheaterKind` | string | — | Display label such as `"Type A"`, `"Type E"`, or `"Schmidt"`. |
| `maxSuperheatC` | number | `10` – `350` | Full-rate superheat over saturation in °C (superheated engines only). |
| `halfSuperheatShare` | number | `0.3` – `0.98` | Fraction of `maxSuperheatC` achieved at half firing rate (default `0.72`). |
| `exemptFromSuperheater` | bool | `false` | Disables both superheater and wet-wall condensation. |
| `frontEndThrottle` | bool | — | `true` for a front-end throttle after the superheater; `false` for a dome regulator before it. |
| `steamCircuitVolumeL` | number | `10` – `5000` | Added superheater/dry-pipe circuit volume in L. |
| `driftingFittings` | object | `{ "snifting": bool, "driftingValve": bool, "byPass": bool }` | As-built drifting protection fittings. |

### `injectors`

Omitting the `injectors` section—or omitting `steamValves` within it—defaults
`steamValves` to `true`, enabling the two-valve injector setup (steam lever plus
water valve) whenever the session has injector steam valves enabled. Set
`"injectors": { "steamValves": false }` to leave the locomotive on single-lever
injector control. Omit `twoValveGraph` for a single-injector locomotive; provide
`twoValveGraph` to wire independent twin injectors.

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `type` | string | `"nonLifting"` \| `"lifting"` | Lifting or non-lifting injector behavior (default `"nonLifting"`). |
| `maxFlowKgPerS` | number | `0.05` – `50` | Total rated delivery across the locomotive's injectors at working pressure, kg/s. |
| `oneLeverForTwo` | bool | `false` | Single cab lever representing a pair of injectors (allows turning down to half). |
| `crackOpening` | number | `0.05` – `0.5` | Priming crack opening threshold for stepped levers. |
| `steamValves` | bool | `true` | Adds three-position steam levers (`Off` / `Prime` / `Feed`) beside water valves (defaults to `true` even when `injectors` is omitted). |
| `ownLeverIsSteam` | bool | `false` | British layout where the cab's native injector levers act as steam valves and added controls act as water regulators. |
| `waterRegulators` | object | `{ "mesh": "...", "rightTurnDeg": 10..720, "leftTurnDeg": 10..720 }` or `{ "inPlace": { "left": [x,y,z], "right": [x,y,z], "reachM": 0.02..1, "turnDeg": 10..360 } }` | Optional native cab water regulator integration. |
| `twoValveGraph` | object | `{ "add", "addA", "addB", "multiplier", "multiplierOut", "factor", "factorPort", "right", "left" }` | Verified CCL averaging-graph component/port IDs for independent twin injectors (presence enables two injectors). |
| `tenderLiveries` | string[] | — | Coupled tender livery IDs fitted with tender water shutoff valves. Requires a matching handle fit in `TenderWaterValves` (currently the built-in LMS Stanier tenders `em_lms_stanier_tender_1_black` and `em_lms_stanier_tender_1_grey`); custom tender liveries without a handle fit are skipped with a warning so inoperable shutoff ports are never added. |

### `stoker`, `feedPump`, `booster`, `autoFireDoor`

- **`stoker`**: `{ "fitted": bool, "retrofit": bool, "anchorPort": string, "backLanes": 0.1..2.0, "lumpsLeft": -0.5..0.5, "supplyPipeAtM": -1..1 }`.
- **`feedPump`**: `{ "fitted": bool, "type": "open" | "closed", "name": string, "capacityKgPerS": 0.1..40 | "capacityGph": ~95.1..38040, "onInjectorLever": bool }` (`capacityGph` is converted to kg/s and checked against `0.1`–`40` kg/s).
- **`booster`**: `{ "fitted": bool, "wheelDiameterM": 0.4..2.5, "trailerLoadKg": 1000..100000, "drivingAxles": 1..10 }`.
- **`autoFireDoor`**: `true` / `false`, or `{ "fitted": true }`.

### `cab` Placements

All coordinates use Derail Valley's car/firing frame (`+X` right, `+Y` up, `+Z` forward)
in metres unless noted (`offsetCm` and `reverserOffsetCm` are in centimetres;
rotations are Euler degrees `[x, y, z]`).

- **`firehole`**: `{ "middleM", "footM", "widthM", "heightM", "planeM" }`
- **`injectorSteam`**: `{ "position": [x,y,z], "rotation": [turn,tilt,twist], "fixedOffset"?, "sideOffset"? }`
- **`dropGrate`**: `{ "position": [x,y,z] }` (offset from fire-dump lever, car frame m)
- **`ashpan`**: `{ "position": [x,y,z], "noHanger"? }` (pull position in car frame m)
- **`stokerManifold`**: `{ "offsetCm": [x,y,z], "turnDeg", "tiltDeg" }`
- **`stokerLever`**: `{ "position": [x,y,z] }`
- **`feedPump`**: `{ "valveInCar"?: [x,y,z], "valveOffset"?: [x,y], "gaugeOffset"?: [x,y], "wallGauge"?: [along,up,out] }`
- **`booster`**: `{ "position"?: [x,y,z], "reverserOffsetCm"?: [x,y,z] }`
- **`jets`** / **`driftingValve`**: `{ "position": [x,y,z], "rotation": [x,y,z], "inCar"?: bool, "plateOffset"?: [x,y,z], "plateSpinDeg"?, "plateTurnDeg"?, "plateSquare"?, "ownHandle"? }`
- **`shedBlower`** / **`shedWater`**: `{ "enabled"?: bool, "position"?: [x,y,z], "swingDeg"?, "downDeg"?, "spinDeg"?, "outM"?, "landmark"? }`

### `stack`

Optional chimney geometry hints, in metres. This section affects **visuals only**
(plume lip confinement and stack mouth sizing) and has no effect on firing or
boiler physics. It is consumed by Realistic Stack Effects (contract API v3+, via
`StackSources.RegisterGeometry`) or by Realistic Firing's own built-in plume
when Realistic Stack Effects is not active.

| Field | Type | Range / Values | Description |
| --- | --- | --- | --- |
| `lipHeightM` | number | `0` – `3` | Height of the chimney lip above the smoke emitter along the stack's axis, in metres. |
| `boreRadiusM` | number | `0.08` – `0.5` | Inside bore radius of the chimney at the lip, in metres. Must satisfy `boreRadiusM <= outsideRadiusM` when both are specified. |
| `outsideRadiusM` | number | `0.1` – `0.5` | Outside radius of the chimney at the lip, in metres. Must satisfy `boreRadiusM <= outsideRadiusM` when both are specified. |

When present with at least one dimension, a `stack` hint overrides a readable
stack mesh. Any omitted dimension falls back individually to the readable stack
mesh measurement, a built-in offline measured lip (if any), or the plume's
defaults (`0.25` m bore radius, `0.32` m outside radius). An empty `"stack": {}`
object is accepted and treated as no hint.

## Exporting Placements with `SeatingBench`

In a development build, open the Seating Bench (`DevSeatingKey`), seat the cab
parts, firehole, and shed connections live with the sliders, and click
**Export maker-fit JSON**. Realistic Firing will:

1. Format a valid `schemaVersion: 1` JSON snippet containing every seated part
   for the active locomotive;
2. Write the snippet to the Unity Mod Manager log and copy it to the clipboard;
3. Save a copy to `Mods/RealisticFiring/SeatingExports/<livery>.realistic-firing.json`.

You can paste the `"cab"` block directly into your mod's `realistic-firing.json`
or copy the exported file into `Mods/RealisticFiring/Fits/` for local testing.

## Concrete Example

```json
{
  "schemaVersion": 1,
  "liveries": ["CustomPacific462A", "CustomPacific462A_Green"],
  "fuel": "coal",
  "summary": "A USRA-style Pacific: wide firebox with brick arch, Type A superheater, and Worthington feedwater heater",
  "firebox": {
    "grateAreaM2": 6.2,
    "grateSlopeDeg": -6.0,
    "backRowDraft": 1.12,
    "frontRowDraft": 0.88,
    "sideDraft": 1.07,
    "overFireMixing": 1.0,
    "ashFraction": 1.0,
    "clinkerFactor": 1.0,
    "reversedDoor": false,
    "overFireJets": {
      "count": 6,
      "tube": "large"
    }
  },
  "boiler": {
    "maxEvaporationLbPerHr": 48000,
    "safetyValves": {
      "liftPsiGauge": 200,
      "capacityKgPerS": 6.5
    },
    "lowWaterGuard": "fusiblePlug"
  },
  "draft": {
    "blowerMaxSteamKgPerS": 0.55,
    "naturalDraftKgPerS": 0.62,
    "airPumps": {
      "type": "crossCompound",
      "count": 1,
      "mainReservoirL": 1150
    }
  },
  "cylinders": {
    "stockSteamPerStroke": false,
    "superheated": true,
    "superheaterKind": "Type A",
    "maxSuperheatC": 140,
    "halfSuperheatShare": 0.72,
    "frontEndThrottle": false,
    "steamCircuitVolumeL": 320,
    "driftingFittings": {
      "snifting": true,
      "driftingValve": true,
      "byPass": false
    }
  },
  "injectors": {
    "type": "nonLifting",
    "maxFlowKgPerS": 7.2,
    "steamValves": true
  },
  "stoker": {
    "fitted": true,
    "backLanes": 0.85,
    "lumpsLeft": 0.05
  },
  "feedPump": {
    "fitted": true,
    "type": "open",
    "name": "Worthington 4-SA",
    "capacityGph": 6600,
    "onInjectorLever": false
  },
  "booster": {
    "fitted": true,
    "wheelDiameterM": 1.1176,
    "trailerLoadKg": 21500,
    "drivingAxles": 3
  },
  "autoFireDoor": true,
  "cab": {
    "firehole": {
      "middleM": 0.000,
      "footM": 1.850,
      "widthM": 0.520,
      "heightM": 0.410,
      "planeM": -0.180
    },
    "injectorSteam": {
      "position": [0.12, -0.08, 0.04],
      "rotation": [-180, 0, 2]
    },
    "dropGrate": {
      "position": [0.0, 0.0, -0.3]
    },
    "ashpan": {
      "position": [-0.95, 1.12, 0.45]
    },
    "stokerManifold": {
      "offsetCm": [18.0, -6.5, -4.0],
      "turnDeg": 5,
      "tiltDeg": 0
    },
    "stokerLever": {
      "position": [0.15, 0.0, -0.25]
    },
    "feedPump": {
      "valveOffset": [-0.14, -0.35],
      "gaugeOffset": [-0.14, -0.21]
    },
    "booster": {
      "position": [1.38, 1.65, -0.42]
    },
    "jets": {
      "position": [-0.12, 2.45, -0.14],
      "rotation": [0, 0, 0],
      "plateOffset": [0.09, 0.18, -0.04],
      "plateTurnDeg": 180,
      "plateSquare": true
    },
    "driftingValve": {
      "position": [0.68, 2.82, -0.10],
      "rotation": [0, -35, 0]
    },
    "shedBlower": {
      "position": [-0.62, 2.15, 9.85],
      "downDeg": 42,
      "landmark": "low on the left side of the smokebox"
    },
    "shedWater": {
      "position": [-0.82, 1.38, 0.72],
      "landmark": "low on the left side of the firebox, ahead of the cab"
    }
  },
  "stack": {
    "lipHeightM": 0.44,
    "boreRadiusM": 0.22,
    "outsideRadiusM": 0.30
  }
}
```
