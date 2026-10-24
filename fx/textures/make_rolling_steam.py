#!/usr/bin/env python3
"""Generate the chimney plume's rolling steam: puffs simulated in 3D and lit
from six ways, as looping flipbooks: past the jet a real plume rolls in
place, its velocity turned to turbulence, where sprites slid up like a belt;
moving the particles in eddies only made wavy sprites.

A rising puff of steam rolls as a vortex ring does: up its middle, out over
its top, down its sides, and in at its bottom (Hill's spherical vortex in
its own frame), so its surface churns while the whole rises slowly. Each
puff here is a core with lobes of three sizes that bulge out at its top,
roll out over it and down its sides along that flow, and sink back in at
its bottom, with finer billows carried by the same flow. Every lobe makes
one trip a loop, so the loop is seamless. Each frame is lit from the six
ways along the frame's axes by marching the light through the puff (its
own shadows, softened over a few orders of scattering, as a cloud's are),
so the shader can light it from the sun's and sky's ways, and the puff is
matte and shadowed where its lobes hide one another.

  smoke_roll_a.png  RGB: light from the right, top, and front (the
                    viewer's side); A: opacity
  smoke_roll_b.png  RGB: light from the left, bottom, and back; A: how
                    thick it is through (its optical depth, eased), stored
                    as its square root: the shader thins a puff through
                    its depth, which magnified the texture compression's
                    error near nothing into a faint, streaky fringe across
                    each particle's empty margin

Both are 16 x 16 frames of 128 px: eight puffs, each a loop of 32 frames
over two rows. As a particle thins or ages past the puffs' stretch off the
stack, the shader draws it as the plume's own wisp flipbook instead
(make_smoke_textures.py). Everything is generated from seeded noise, so the
art is our own and the output is reproducible. Requires numpy and Pillow;
about ten minutes on 16 cores.

Usage: make_rolling_steam.py OUTPUT_DIR [--preview PREVIEW.png]
"""

import argparse
import multiprocessing
import pathlib

import numpy as np
from PIL import Image

GRID = 112       # voxels a side
FRAME = 128      # pixels a side
FRAMES = 32      # frames a loop
VARIANTS = 8
COLUMNS = 16
RNG_SEED = 588030  # Derail Valley's Steam app ID

# The light's ways: the axis of a (z, y, x) grid (z toward the viewer, y
# down the frame) and whether the light comes from its high end.
WAYS = {
    "right": (2, True), "top": (1, False), "front": (0, True),
    "left": (2, False), "bottom": (1, True), "back": (0, False),
}


def fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


class Noise:
    """Gradient noise over a repeating lattice, at any points (..., 3)."""

    def __init__(self, rng, period=32):
        g = rng.normal(size=(period, period, period, 3)).astype(np.float32)
        self.gradients = g / np.linalg.norm(g, axis=-1, keepdims=True)
        self.period = period

    def __call__(self, p):
        floor = np.floor(p)
        f = (p - floor).astype(np.float32)
        i = floor.astype(np.int64) % self.period
        w = fade(f)
        out = np.zeros(p.shape[:-1], np.float32)
        for dx in (0, 1):
            wx = w[..., 0] if dx else 1 - w[..., 0]
            ix = (i[..., 0] + dx) % self.period
            for dy in (0, 1):
                wy = w[..., 1] if dy else 1 - w[..., 1]
                iy = (i[..., 1] + dy) % self.period
                for dz in (0, 1):
                    wz = w[..., 2] if dz else 1 - w[..., 2]
                    g = self.gradients[ix, iy, (i[..., 2] + dz) % self.period]
                    out += wx * wy * wz * (g[..., 0] * (f[..., 0] - dx) + g[..., 1] * (f[..., 1] - dy)
                                           + g[..., 2] * (f[..., 2] - dz))
        return out * 1.6


def billows(noise, p, frequency, octaves, offsets):
    """Billowy fractal noise: |noise| summed, round lumps meeting in creases."""
    total, amplitude, norm = 0.0, 1.0, 0.0
    for octave in range(octaves):
        total = total + amplitude * np.abs(noise(p * (frequency * 2 ** octave) + offsets[octave]))
        norm += amplitude
        amplitude *= 0.5
    return total / norm


def hill(p, a):
    """Hill's spherical vortex of radius a, axis up, in its own frame: up the
    middle, out over the top, down the outside, in at the bottom; the air
    outside streams down past it."""
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    r2 = x * x + z * z
    R2 = r2 + y * y + 1e-6
    a2, a3 = a * a, a * a * a
    inside = np.stack([1.5 * x * y / a2, 1.5 * (1 - (2 * r2 + y * y) / a2), 1.5 * z * y / a2], -1)
    R5 = R2 ** 2.5
    outside = np.stack([1.5 * a3 * x * y / R5, -(1 + a3 / (2 * R2 ** 1.5) - 1.5 * a3 * y * y / R5),
                        1.5 * a3 * z * y / R5], -1)
    return np.where((R2 < a2)[..., None], inside, outside)


def back_trace(p, tau, a, step=0.04):
    """Where the fluid at p was tau ago (midpoint steps)."""
    if tau <= 0:
        return p
    n = max(1, int(np.ceil(tau / step)))
    h = tau / n
    for _ in range(n):
        p = p - h * hill(p - 0.5 * h * hill(p, a), a)
    return p


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0, 1)
    return t * t * (3 - 2 * t)


def puff(variant):
    """A puff's shape and its lobes: (azimuth, starting angle from the top,
    radius, phase, which core). The puffs differ in family as well as
    detail, so a long trail does not repeat one round scallop: wider or
    taller, leaning, and most with a second, smaller core merging into the
    first (one family made a trail's edge repeat the same scallop)."""
    rng = np.random.default_rng(RNG_SEED + 1000 + variant)
    a = rng.uniform(0.44, 0.56)
    core = 0.78 * a
    # Wider or taller, no wider than the frame holds.
    stretch = rng.uniform(0.85, min(1.4, 0.5 / core))
    second = None
    if variant % 4 != 0:
        # A second core off to a side and up, partly merged.
        side = rng.choice([-1.0, 1.0])
        radius = core * rng.uniform(0.55, 0.8)
        second = (side * rng.uniform(0.18, 0.3), rng.uniform(-0.05, 0.18), rng.uniform(-0.1, 0.1), radius)
    lobes = []
    plenty = rng.uniform(0.75, 1.3)
    for count, low, high in ((10, 0.28, 0.36), (26, 0.17, 0.24), (50, 0.08, 0.12)):
        for _ in range(int(round(count * plenty))):
            host = 1 if second is not None and rng.random() < 0.35 else 0
            lobes.append((rng.uniform(0, 2 * np.pi), rng.uniform(0.15, 0.6), a * rng.uniform(low, high), rng.uniform(0, 1), host))
    return dict(seed=RNG_SEED + 2000 + variant, a=a, squash=rng.uniform(0.72, 1.0), stretch=stretch,
                shear=rng.uniform(-0.3, 0.3), tilt=rng.uniform(-0.25, 0.25), second=second, lobes=lobes)


def density(shape, loop):
    """The puff's density over the grid (z, y, x) at its loop's fraction (0 to 1)."""
    rng = np.random.default_rng(shape["seed"])
    noise = Noise(rng)
    offsets_a = rng.uniform(0, 32, size=(4, 3)).astype(np.float32)
    offsets_b = rng.uniform(0, 32, size=(4, 3)).astype(np.float32)
    a, squash, stretch, tilt, second = shape["a"], shape["squash"], shape["stretch"], shape["tilt"], shape["second"]
    c = (np.arange(GRID) + 0.5) / GRID * 2 - 1
    Z, Y, X = np.meshgrid(c, -c, c, indexing="ij")
    p = np.stack([X, Y, Z], -1).astype(np.float32)
    q = p - np.array([0.0, -0.08, 0.0], np.float32)
    # Leaning: the higher, the further over.
    q[..., 0] -= shape["shear"] * q[..., 1]

    # The core and its lobes, soft-unioned (they blend into creases where they meet).
    blend = 0.022
    core = 0.78 * a
    union = np.exp((core - np.sqrt((q[..., 0] / stretch) ** 2 + (q[..., 1] / squash) ** 2 + q[..., 2] ** 2)) / blend)
    hosts = [(np.zeros(3), core)]
    if second is not None:
        middle = np.array(second[:3])
        hosts.append((middle, second[3]))
        union += np.exp((second[3] - np.sqrt((q[..., 0] - middle[0]) ** 2 + (q[..., 1] - middle[1]) ** 2
                                             + (q[..., 2] - middle[2]) ** 2)) / blend)
    axis = np.array([np.sin(tilt), np.cos(tilt), 0.0])
    side = np.array([np.cos(tilt), -np.sin(tilt), 0.0])
    depth = np.array([0.0, 0.0, 1.0])
    last = 0.8 * np.pi  # where a lobe has sunk back in, from the top
    for azimuth, first, radius, phase, host in shape["lobes"]:
        age = (loop + phase) % 1.0
        # Along the surface from the top: tan(θ/2) grows exponentially in Hill's flow.
        growth = np.log(np.tan(last / 2) / np.tan(first / 2))
        angle = 2 * np.arctan(np.tan(first / 2) * np.exp(growth * age))
        # Bulging out at the top, sinking back in at the bottom.
        r = radius * min(1.0, age / 0.12) ** 0.5 * min(1.0, (1 - age) / 0.3)
        if r < 0.01:
            continue
        way = np.cos(angle) * axis + np.sin(angle) * (np.cos(azimuth) * side + np.sin(azimuth) * depth)
        middle, size = hosts[host]
        at = way * (size + 0.35 * r)
        if host == 0:
            at[0] *= stretch
            at[1] *= squash
        at = at + middle
        union += np.exp((r - np.sqrt((q[..., 0] - at[0]) ** 2 + (q[..., 1] - at[1]) ** 2 + (q[..., 2] - at[2]) ** 2)) / blend)
    field = blend * np.log(union)

    # Finer billows carried by the same flow: two fields a half loop apart,
    # each faded out as it restarts, blended keeping their contrast.
    def fine(age, offsets):
        return billows(noise, back_trace(q, age * 0.25, a), 7.0, 3, offsets) - 0.36

    later = (loop + 0.5) % 1.0
    wa = 1 - abs(1 - 2 * loop)
    wb = 1 - wa
    detail = (wa * fine(loop, offsets_a) + wb * fine(later, offsets_b)) / np.sqrt(wa * wa + wb * wb)
    field = field + 0.05 * detail
    rho = smoothstep(-0.025, 0.025, field)
    # Firm above, looser below, where a rising puff trails.
    rho = rho * (1 - 0.6 * smoothstep(0.0, -0.6, q[..., 1]) * smoothstep(-0.1, 0.25, -detail))
    rho = rho * (0.75 + 0.5 * detail).clip(0.3, 1.2)
    border = smoothstep(0.98, 0.86, np.maximum(np.abs(p[..., 0]), np.maximum(np.abs(p[..., 1]), np.abs(p[..., 2]))))
    return (rho * border).astype(np.float32)


def through(depth, axis, from_high):
    """The optical depth from each cell out of the grid along an axis, to its high or low end."""
    if from_high:
        total = np.flip(np.cumsum(np.flip(depth, axis), axis), axis)
    else:
        total = np.cumsum(depth, axis)
    return total - 0.5 * depth


def lit(rho, extinction=16.0, light_share=0.6):
    """The puff seen from the front: its opacity, how thick it is through
    (eased, 0 to 1), and its light from each of the six ways (as the color
    where it shows, 0 to 1)."""
    cell = extinction * rho * (2.0 / GRID)
    seen = np.exp(-through(cell, 0, True)) * (1 - np.exp(-cell))
    opacity = 1 - np.exp(-cell.sum(0))
    thickness = 1 - np.exp(-cell.sum(0) / 6.0)

    def reaching(depth):
        # A few orders of scattering, roughly: shadows soften inside a cloud.
        return (np.exp(-depth) + 0.5 * np.exp(-0.25 * depth) + 0.25 * np.exp(-0.06 * depth)) / 1.75

    light = {}
    for way, (axis, from_high) in WAYS.items():
        light[way] = (seen * reaching(through(cell * light_share, axis, from_high))).sum(0) / np.maximum(opacity, 1e-4)
    light["thickness"] = thickness
    return opacity, light


def resized(image):
    return np.asarray(Image.fromarray(image.astype(np.float32)).resize((FRAME, FRAME), Image.BICUBIC))


def frame(job):
    variant, step = job
    opacity, light = lit(density(puff(variant), step / FRAMES))
    return np.clip(resized(opacity), 0, 1), {key: np.clip(resized(value), 0, 1) for key, value in light.items()}


def filled(light, opacity):
    """Light where the puff is not, from its nearest edge, so filtering does not darken its fringe."""
    light = light.copy()
    known = opacity > 0.004
    for _ in range(12):
        if known.all():
            break
        padded = np.pad(np.where(known, light, 0), 1, mode="edge")
        weight = np.pad(known.astype(np.float32), 1, mode="edge")
        total = sum(padded[1 + dy:FRAME + 1 + dy, 1 + dx:FRAME + 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        count = sum(weight[1 + dy:FRAME + 1 + dy, 1 + dx:FRAME + 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        grow = ~known & (count > 0)
        light[grow] = total[grow] / count[grow]
        known = known | grow
    light[~known] = 0.6
    return light


def atlas(frames, channels):
    rows = len(frames) // COLUMNS
    rgba = np.zeros((rows * FRAME, COLUMNS * FRAME, 4), dtype=np.uint8)
    for i, (opacity, light) in enumerate(frames):
        r, c = divmod(i, COLUMNS)
        tile = rgba[r * FRAME:(r + 1) * FRAME, c * FRAME:(c + 1) * FRAME]
        for k, channel in enumerate(channels):
            value = (opacity if channel == "opacity" else np.sqrt(light[channel]) if channel == "thickness"
                     else filled(light[channel], opacity))
            tile[..., k] = np.round(np.clip(value, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(rgba)


def preview(frames, path):
    """Each puff's first frame lit from the right and from the front, over a sky."""
    sky = np.array([0.42, 0.6, 0.85])
    tiles = []
    for variant in range(VARIANTS):
        opacity, light = frames[variant * FRAMES]
        for way in ("right", "front", "back"):
            rgb = light[way][..., None] * np.array([1.0, 0.96, 0.9]) * 0.95 + light["top"][..., None] * np.array([0.55, 0.66, 0.85]) * 0.5
            tiles.append(sky * (1 - opacity[..., None]) + np.clip(rgb, 0, 1) * opacity[..., None])
    rows = [np.concatenate(tiles[v * 3:(v + 1) * 3], axis=1) for v in range(VARIANTS)]
    Image.fromarray((np.concatenate(rows, axis=1) * 255).astype(np.uint8)).save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--preview", type=pathlib.Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    jobs = [(variant, step) for variant in range(VARIANTS) for step in range(FRAMES)]
    with multiprocessing.Pool() as pool:
        frames = pool.map(frame, jobs)
    atlas(frames, ("right", "top", "front", "opacity")).save(args.output / "smoke_roll_a.png")
    atlas(frames, ("left", "bottom", "back", "thickness")).save(args.output / "smoke_roll_b.png")
    if args.preview:
        preview(frames, args.preview)


if __name__ == "__main__":
    main()
