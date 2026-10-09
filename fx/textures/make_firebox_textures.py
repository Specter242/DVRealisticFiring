#!/usr/bin/env python3
"""Generate the firebox textures for the Realistic Firing FX.

  coal_bed.png         512 x 512, tiles: coal lumps for the fire bed, big and
                       small. R each lump's own hotness (0-1), G height (0 deep in the
                       gaps between lumps, where a live fire glows first; 1
                       on a lump's rounded crown), B fine grain.
  coal_normal.png      512 x 512, tiles: the lumps' relief as a normal map,
                       from the same height.
  fire_noise.png       256 x 256, tiles: R broad and G fine fractal noise,
                       scrolled by the bed shader to move the glow.
  flame_flipbook.png   4 x 4 frames: a flame tongue licking up and breaking
                       off, white core in RGB, density in alpha; the mod tints
                       it per particle (yellow and smoky on green coal,
                       orange-white on a good fire).

Everything is generated from seeded noise, so the art is our own and the
output is reproducible. Requires numpy and Pillow.

Usage: make_firebox_textures.py OUTPUT_DIR [--preview PREVIEW.png]
"""

import argparse
import pathlib

import numpy as np
from PIL import Image

RNG_SEED = 588030  # Derail Valley's Steam app ID


def tile_noise(size, cells, rng):
    """Smooth value noise over a size x size grid that tiles, in [-1, 1]."""
    lattice = rng.uniform(-1.0, 1.0, size=(cells, cells))
    x = np.arange(size) * cells / size
    i = np.floor(x).astype(int)
    f = x - i
    f = f * f * f * (f * (f * 6 - 15) + 10)
    i0, i1 = i % cells, (i + 1) % cells
    a = lattice[np.ix_(i0, i0)]
    b = lattice[np.ix_(i0, i1)]
    c = lattice[np.ix_(i1, i0)]
    d = lattice[np.ix_(i1, i1)]
    fy, fx = f[:, None], f[None, :]
    top = a + (b - a) * fx
    bottom = c + (d - c) * fx
    return top + (bottom - top) * fy


def tile_fbm(size, base_cells, octaves, rng):
    total = np.zeros((size, size))
    amplitude, weight = 1.0, 0.0
    for octave in range(octaves):
        total += amplitude * tile_noise(size, base_cells * 2 ** octave, rng)
        weight += amplitude
        amplitude *= 0.5
    return total / weight


def voronoi(size, count, rng, warp):
    """Cells on a torus, their borders bent by noise so lumps are irregular:
    each pixel's cell and its distance from the border."""
    points = rng.uniform(0, size, size=(count, 2))
    yy, xx = np.mgrid[0:size, 0:size].astype(float)
    yy = yy + warp * tile_fbm(size, 6, 3, rng)
    xx = xx + warp * tile_fbm(size, 6, 3, rng)
    nearest = np.full((size, size), np.inf)
    second = np.full((size, size), np.inf)
    owner = np.zeros((size, size), dtype=int)
    for index, (py, px) in enumerate(points):
        dy = np.abs(yy - py) % size
        dx = np.abs(xx - px) % size
        dy = np.minimum(dy, size - dy)
        dx = np.minimum(dx, size - dx)
        distance = np.sqrt(dx * dx + dy * dy)
        closer = distance < nearest
        second = np.where(closer, nearest, np.minimum(second, distance))
        owner = np.where(closer, index, owner)
        nearest = np.where(closer, distance, nearest)
    return owner, second - nearest


def coal_bed(size, rng):
    """Big lumps with small ones lying in their gaps: rounded crowns, deep
    gaps. R is each lump's own hotness (some catch and glow before others)."""
    big_count, small_count = 110, 420
    big_owner, big_edge = voronoi(size, big_count, rng, warp=10.0)
    small_owner, small_edge = voronoi(size, small_count, rng, warp=5.0)
    big_hot = rng.uniform(0.0, 1.0, size=big_count)[big_owner]
    small_hot = rng.uniform(0.0, 1.0, size=small_count)[small_owner]
    # A lump's crown rises roundly from its edge; small lumps sit lower.
    big = np.sqrt(np.clip(big_edge / 16.0, 0.0, 1.0))
    small = 0.65 * np.sqrt(np.clip(small_edge / 7.0, 0.0, 1.0))
    lumpy = tile_fbm(size, 24, 3, rng) * 0.5 + 0.5
    height = np.maximum(big, small) * (0.8 + 0.2 * lumpy)
    hot = np.where(big >= small, big_hot, small_hot)
    grain = tile_fbm(size, 48, 2, rng) * 0.5 + 0.5
    rgba = np.stack([hot, height, grain, np.ones_like(height)], axis=-1)
    return (np.clip(rgba, 0.0, 1.0) * 255).astype(np.uint8), height


def normal_map(height, strength):
    """Tangent-space normals from a tiling height field, packed into RGB."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    rgba = np.concatenate([n * 0.5 + 0.5, np.ones_like(height)[..., None]], axis=-1)
    return (np.clip(rgba, 0.0, 1.0) * 255).astype(np.uint8)


def fire_noise(size, rng):
    broad = tile_fbm(size, 4, 4, rng) * 0.5 + 0.5
    fine = tile_fbm(size, 16, 3, rng) * 0.5 + 0.5
    rgba = np.stack([broad, fine, np.zeros_like(broad), np.ones_like(broad)], axis=-1)
    return (np.clip(rgba, 0.0, 1.0) * 255).astype(np.uint8)


def flame_frames(frame, count, rng):
    """A tongue that rises, licks sideways, thins, and breaks off: a crisp
    edge, a bright core, and ragged licks along its sides."""
    frames = []
    yy, xx = np.mgrid[0:frame, 0:frame].astype(float) / (frame - 1)
    wobble = [tile_noise(frame, 4, rng) for _ in range(3)]
    licks = tile_noise(frame, 12, rng)
    for k in range(count):
        t = k / (count - 1)
        up = 1.0 - yy  # 0 at the bottom, 1 at the top
        sway = 0.07 * np.sin(up * 5.0 + t * 6.0) + 0.05 * wobble[0] * up
        # Wide at the base, tapering to a point at its tip.
        height = 0.96 - 0.35 * t
        taper = np.clip(1.0 - up / height, 0.0, 1.0) ** 0.75
        width = (0.3 - 0.1 * t) * taper + 0.005
        ragged = width * (1.0 + 0.35 * licks * up)
        body = 1.0 - np.abs(xx - 0.5 - sway) / np.maximum(ragged, 1e-3)
        body *= np.clip(up / 0.05, 0.0, 1.0)
        tear = 0.7 + 0.3 * wobble[1 + (k % 2)]
        # A crisp edge: the density rises steeply just inside the outline.
        density = np.clip(body * tear * 2.2, 0.0, 1.0) ** 0.7 * (1.0 - 0.4 * t)
        core = np.clip(body * 1.8 - 0.55, 0.0, 1.0)
        rgb = np.clip(0.5 + 0.5 * core, 0.0, 1.0)
        frames.append((rgb, density))
    return frames


def atlas(frames, columns):
    frame = frames[0][0].shape[0]
    rows = (len(frames) + columns - 1) // columns
    out = np.zeros((rows * frame, columns * frame, 4))
    for index, (rgb, density) in enumerate(frames):
        r, c = divmod(index, columns)
        tile = out[r * frame:(r + 1) * frame, c * frame:(c + 1) * frame]
        tile[..., 0] = tile[..., 1] = tile[..., 2] = rgb
        tile[..., 3] = density
    return (np.clip(out, 0.0, 1.0) * 255).astype(np.uint8)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--preview", type=pathlib.Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(RNG_SEED + 1)

    coal, height = coal_bed(512, rng)
    images = {
        "coal_bed.png": coal,
        "coal_normal.png": normal_map(height, 24.0),
        "fire_noise.png": fire_noise(256, rng),
        "flame_flipbook.png": atlas(flame_frames(128, 16, rng), 4),
    }
    for name, pixels in images.items():
        Image.fromarray(pixels, "RGBA").save(args.output / name)
        print(f"wrote {args.output / name}")

    if args.preview:
        tiles = []
        for pixels in images.values():
            rgb = pixels[..., :3].astype(float) * (pixels[..., 3:4] / 255.0 if pixels.shape[-1] == 4 else 1.0)
            tiles.append(Image.fromarray(rgb.astype(np.uint8), "RGB").resize((256, 256)))
        sheet = Image.new("RGB", (256 * len(tiles), 256))
        for i, tile in enumerate(tiles):
            sheet.paste(tile, (256 * i, 0))
        sheet.save(args.preview)
        print(f"wrote {args.preview}")


if __name__ == "__main__":
    main()
