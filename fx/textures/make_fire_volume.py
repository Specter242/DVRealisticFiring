#!/usr/bin/env python3
"""Generate the 3D noise for the Realistic Firing FX's firebox volume.

  fire_volume_noise.bytes  64 x 64 x 64 texels, RGBA8, x fastest, then y,
                           then z; tiles in every direction. The mod's
                           builder makes a Texture3D of it.
      R  ridged noise of 4 and 8 cycles a tile: mottling on the coal lumps.
      G  broadband noise of 2 to 20 cycles: irregular curls inside the sheets.
      B  band noise of 2 cycles: the big lobes a working fire rolls in.
      A  band noise of 3 cycles, another draw: the warp that curls the rest.

Each channel is white noise filtered to a spatial frequency spectrum
through the FFT, so it is periodic by construction, then spread evenly over
0..255 by rank. Seeded, so the output is reproducible. Requires numpy.

Usage: make_fire_volume.py OUTPUT_DIR
"""

import pathlib
import sys

import numpy as np

SIZE = 64


def band(rng, cycles, width):
    """Periodic noise whose energy sits around `cycles` a tile."""
    white = rng.standard_normal((SIZE, SIZE, SIZE))
    k = np.fft.fftfreq(SIZE) * SIZE
    kx, ky, kz = np.meshgrid(k, k, k, indexing="ij")
    radius = np.sqrt(kx * kx + ky * ky + kz * kz)
    envelope = np.exp(-0.5 * ((radius - cycles) / width) ** 2)
    envelope[0, 0, 0] = 0.0
    field = np.real(np.fft.ifftn(np.fft.fftn(white) * envelope))
    return field / field.std()


def ridged(field):
    """Crests where the noise crosses zero: thin sheets."""
    return 1.0 - np.minimum(np.abs(field) / 1.5, 1.0)


def broadband(rng):
    """Periodic curls at many scales, without a preferred ripple spacing."""
    white = rng.standard_normal((SIZE, SIZE, SIZE))
    k = np.fft.fftfreq(SIZE) * SIZE
    kx, ky, kz = np.meshgrid(k, k, k, indexing="ij")
    radius = np.sqrt(kx * kx + ky * ky + kz * kz)
    # Roughly equal variance per octave in 3D, tapered at both ends. A
    # narrow ring at ten cycles made alternating crests and troughs; the
    # airflow stretched those into nested stripes following each flame.
    envelope = np.maximum(radius, 2.0) ** -1.5
    envelope *= 1.0 - np.exp(-0.5 * (radius / 2.0) ** 4)
    envelope *= np.exp(-0.5 * (radius / 20.0) ** 4)
    field = np.real(np.fft.ifftn(np.fft.fftn(white) * envelope))
    return field / field.std()


def by_rank(field):
    """Spread over 0..255 evenly, whatever the field's distribution."""
    flat = field.ravel()
    ranks = np.empty(flat.size)
    ranks[np.argsort(flat, kind="stable")] = np.arange(flat.size)
    return np.round(ranks / (flat.size - 1) * 255.0).astype(np.uint8).reshape(field.shape)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__.strip().splitlines()[-1])
    out = pathlib.Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(20261024)
    sheets = ridged(band(rng, 4.0, 1.2)) ** 2 + 0.5 * ridged(band(rng, 8.0, 2.0)) ** 2
    curls = broadband(rng)
    lobes = band(rng, 2.0, 0.7)
    warp = band(rng, 3.0, 1.0)
    # Indexed [x, y, z]; written x fastest, as Texture3D's pixels run.
    rgba = np.stack([by_rank(sheets), by_rank(curls), by_rank(lobes), by_rank(warp)], axis=-1)
    data = np.ascontiguousarray(rgba.transpose(2, 1, 0, 3))
    path = out / "fire_volume_noise.bytes"
    path.write_bytes(data.tobytes())
    print(f"wrote {path} ({data.nbytes} bytes)")


if __name__ == "__main__":
    main()
