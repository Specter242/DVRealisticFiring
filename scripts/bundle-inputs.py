#!/usr/bin/env python3
"""Record and check which sources a built asset bundle came from.

The bundles in fx/build are not tracked, so nothing else says whether one is
older than the shaders, editor scripts, texture generators, or booklet pages
it is built from. After a build, `write` saves <bundle>.inputs.json beside
each bundle: the bundle's own SHA-256 and the SHA-256 of every input file.
Before packaging, `check` recomputes both and fails when the record is
missing, the bundle is not the recorded one, or any input changed.

    bundle-inputs.py digest effects|manual
    bundle-inputs.py write [--built-from DIGEST] BUNDLE...
    bundle-inputs.py check BUNDLE...

`digest` prints one hash over a set's inputs. A build script takes it before
starting Unity and passes it to `write --built-from`, which refuses when an
input changed while the build ran.
"""

import argparse
import hashlib
import json
from pathlib import Path
import sys


REPO = Path(__file__).resolve().parent.parent
PROJECT = "fx/RealisticFiringFX"
ASSETS = PROJECT + "/Assets/RealisticFiring"
# What Unity itself reads for any bundle: its packages and project settings.
UNITY = [(PROJECT + "/Packages/*.json", True), (PROJECT + "/ProjectSettings/*", True)]
# Each set's inputs as (glob, required). The textures are generated on every
# build, so their generators stand in for them. One Unity run builds both
# effects bundles from the same assets, so they share a set.
INPUT_SETS = {
    "effects": UNITY + [
        ("scripts/build-fx.sh", True),
        ("fx/textures/*.py", True),
        (PROJECT + "/Assets/Editor/PlumeBuilder.cs", True),
        (ASSETS + "/Shaders/**/*.shader", True),
        (ASSETS + "/Shaders/**/*.cginc", True),
        # A source tree without the typeface builds a bundle without it.
        (ASSETS + "/Fonts/*.ttf", False),
    ],
    "manual": UNITY + [
        ("scripts/build-manual.sh", True),
        (PROJECT + "/Assets/Editor/FiringManualBuilder.cs", True),
        (ASSETS + "/Manual/manual_p*.png", True),
    ],
}
BUNDLE_SETS = {
    "realisticfiring_fx": "effects",
    "realisticstackeffects_fx": "effects",
    "realisticfiring_manual": "manual",
}
REBUILD = {
    "effects": "run scripts/build-fx.sh",
    "manual": "run scripts/build-fx.sh (or scripts/build-manual.sh for the booklet alone)",
}


class StaleBundle(Exception):
    """A bundle cannot be shown to match the current sources."""


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def input_hashes(set_name, repo=None):
    """Each input file's SHA-256 by its path from the repository root."""
    repo = Path(repo or REPO)
    hashes = {}
    for pattern, required in INPUT_SETS[set_name]:
        found = sorted(path for path in repo.glob(pattern) if path.is_file())
        if required and not found:
            raise StaleBundle(f"no input matches {pattern}")
        for path in found:
            hashes[path.relative_to(repo).as_posix()] = file_sha256(path)
    return hashes


def combined(hashes):
    """One hash over a set's inputs: every path and content, in path order."""
    text = "".join(f"{digest}  {path}\n" for path, digest in sorted(hashes.items()))
    return hashlib.sha256(text.encode()).hexdigest()


def set_of(bundle):
    name = Path(bundle).name
    if name not in BUNDLE_SETS:
        raise StaleBundle(f"{name} is not a bundle this project builds")
    return BUNDLE_SETS[name]


def record_path(bundle):
    bundle = Path(bundle)
    return bundle.with_name(bundle.name + ".inputs.json")


def write(bundle, built_from=None, repo=None):
    bundle = Path(bundle)
    set_name = set_of(bundle)
    if not bundle.is_file():
        raise StaleBundle(f"no bundle at {bundle}")
    hashes = input_hashes(set_name, repo)
    if built_from is not None and built_from != combined(hashes):
        raise StaleBundle(f"an input of {bundle.name} changed while it was built; build it again")
    record = {
        "bundle": bundle.name,
        "bundle_sha256": file_sha256(bundle),
        "input_set": set_name,
        "inputs_sha256": combined(hashes),
        "inputs": hashes,
    }
    record_path(bundle).write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    return record


def check(bundle, repo=None):
    """Raise StaleBundle unless the bundle is the one built from these sources."""
    bundle = Path(bundle)
    set_name = set_of(bundle)
    fix = REBUILD[set_name]
    if not bundle.is_file():
        raise StaleBundle(f"no bundle at {bundle}; {fix}")
    path = record_path(bundle)
    if not path.is_file():
        raise StaleBundle(f"{bundle.name} has no input record ({path.name}), so it may be stale; {fix}")
    try:
        record = json.loads(path.read_text())
        recorded = dict(record["inputs"])
        recorded_bundle = record["bundle_sha256"]
    except (ValueError, KeyError, TypeError):
        raise StaleBundle(f"{path.name} cannot be read; {fix}")
    if recorded_bundle != file_sha256(bundle):
        raise StaleBundle(f"{bundle.name} is not the bundle its input record describes; {fix}")
    current = input_hashes(set_name, repo)
    changed = sorted(name for name in current.keys() & recorded.keys() if current[name] != recorded[name])
    added = sorted(current.keys() - recorded.keys())
    removed = sorted(recorded.keys() - current.keys())
    if changed or added or removed:
        parts = [f"{label}: {', '.join(names)}"
                 for label, names in (("changed", changed), ("added", added), ("removed", removed)) if names]
        raise StaleBundle(f"{bundle.name} is older than its sources ({'; '.join(parts)}); {fix}")
    return record


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    digest = commands.add_parser("digest", help="print one hash over a set's inputs")
    digest.add_argument("input_set", choices=sorted(INPUT_SETS))
    writer = commands.add_parser("write", help="record the inputs beside each bundle just built")
    writer.add_argument("--built-from", help="the digest taken before the build started")
    writer.add_argument("bundles", nargs="+", type=Path)
    checker = commands.add_parser("check", help="fail unless each bundle matches the current inputs")
    checker.add_argument("bundles", nargs="+", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "digest":
            print(combined(input_hashes(args.input_set)))
        elif args.command == "write":
            for bundle in args.bundles:
                record = write(bundle, args.built_from)
                print(f"{bundle.name}: recorded {len(record['inputs'])} inputs", flush=True)
        else:
            for bundle in args.bundles:
                record = check(bundle)
                print(f"{bundle.name}: matches its {len(record['inputs'])} inputs", flush=True)
    except StaleBundle as error:
        print(f"bundle-inputs: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
