#!/usr/bin/env python3
"""Generate the chimney smoke flipbook textures for the Realistic Firing FX.

The textures carry density in alpha, tinted per particle by the mod (white
steam, grey haze, dark soot), so one set serves every color. The firebox's
puffs carry baked top-lit relief in RGB; the chimney's wisps and jet carry
their surface instead (RG its normal, B its thickness), which the chimney
smoke shader lights by the sun's and sky's directions.

  smoke_puff_flipbook.png    8 x 8 frames: a puff that swells and breaks up
                             (the firebox's smoke)
  smoke_wisp_flipbook.png    8 rows of 8 frames: eight wisps, each frayed and
                             drawn out over its life (the chimney plume),
                             billowed where they are thick
  smoke_wisp_baked.png       the same wisps with their relief baked in, lit
                             from above, as 1.13's plume drew them (the
                             wisps past the puffs, by choice)
  smoke_billow_flipbook.png  8 rows of 8 frames: eight clusters of round
                             lobes, loosening over their life (the plume's
                             dense, young steam, blended to the wisps)
  smoke_jet_flipbook.png     4 x 4 frames: streaky exhaust jet
  spark.png                  a cinder's glow: hot core, soft halo

Everything is generated from seeded noise, so the art is our own and the
output is reproducible. Requires numpy and Pillow.

Usage: make_smoke_textures.py OUTPUT_DIR [--preview PREVIEW.png]
"""

import argparse
import pathlib

import numpy as np
from PIL import Image

FRAME = 128
RNG_SEED = 588030  # Derail Valley's Steam app ID


def gradient_noise_3d(shape, frequency, rng):
    """Smooth 3D noise in [-1, 1] over a (depth, height, width) grid."""
    depth, height, width = shape
    cells = [max(2, int(np.ceil(frequency * s / max(shape)))) + 1 for s in shape]
    lattice = rng.uniform(-1.0, 1.0, size=cells)

    def axis(n, count):
        x = np.linspace(0.0, count - 1.001, n)
        i = np.floor(x).astype(int)
        f = x - i
        return i, f * f * f * (f * (f * 6 - 15) + 10)  # quintic fade

    (zi, zf), (yi, yf), (xi, xf) = (axis(n, c) for n, c in zip(shape, cells))
    zi, yi, xi = zi[:, None, None], yi[None, :, None], xi[None, None, :]
    zf, yf, xf = zf[:, None, None], yf[None, :, None], xf[None, None, :]

    def lerp(a, b, t):
        return a + (b - a) * t

    c = lambda dz, dy, dx: lattice[zi + dz, yi + dy, xi + dx]
    x00 = lerp(c(0, 0, 0), c(0, 0, 1), xf)
    x01 = lerp(c(0, 1, 0), c(0, 1, 1), xf)
    x10 = lerp(c(1, 0, 0), c(1, 0, 1), xf)
    x11 = lerp(c(1, 1, 0), c(1, 1, 1), xf)
    return lerp(lerp(x00, x01, yf), lerp(x10, x11, yf), zf)


def fbm(shape, base_frequency, octaves, rng, stretch=(1.0, 1.0, 1.0)):
    """Fractal noise in roughly [-1, 1]; stretch scales frequency per axis."""
    total = np.zeros(shape)
    amplitude, norm = 1.0, 0.0
    for octave in range(octaves):
        frequency = base_frequency * (2 ** octave)
        scaled = tuple(max(2, int(s * k)) for s, k in zip(shape, stretch))
        layer = gradient_noise_3d(scaled, frequency, rng)
        if scaled != shape:
            layer = resize_volume(layer, shape)
        total += amplitude * layer
        norm += amplitude
        amplitude *= 0.5
    return total / norm


def resize_volume(volume, shape):
    """Linear resize of a (d, h, w) volume: each frame resized, blended between frames in depth."""
    def resized(frame):
        image = Image.fromarray(frame.astype(np.float32)).resize((shape[2], shape[1]), Image.BILINEAR)
        return np.asarray(image)

    frames = []
    for z in np.linspace(0, volume.shape[0] - 1, shape[0]):
        lower = int(np.floor(z))
        upper = min(lower + 1, volume.shape[0] - 1)
        f = z - lower
        frames.append(resized(volume[lower]) * (1 - f) + resized(volume[upper]) * f)
    return np.stack(frames)


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def shade(density):
    """Light from above: brighter tops, darker undersides and thick cores, with relief."""
    dy, dx = np.gradient(density, axis=(-2, -1))
    nz = 0.09
    norm = np.sqrt(dx * dx + dy * dy + nz * nz)
    # Image rows grow downward, so a light from above has a negative y.
    light = np.array([-0.3, -0.8, 0.5])
    light /= np.linalg.norm(light)
    lambert = np.clip((-dx * light[0] - dy * light[1] + nz * light[2]) / norm, 0.0, 1.0)
    height = np.linspace(1.0, 0.0, density.shape[-2])[None, :, None]  # 1 at the top
    underside = 0.75 + 0.25 * height
    thickness = 1.0 - 0.2 * density ** 2
    return np.clip((0.55 + 0.45 * lambert) * underside * thickness, 0.0, 1.0)


def puff_frames(count, rng):
    """A puff that swells, softens, and breaks up late in its life."""
    t = np.linspace(0.0, 1.0, count)[:, None, None]
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    shape = (count, FRAME, FRAME)

    warp_x = fbm(shape, 3.0, 3, rng)
    warp_y = fbm(shape, 3.0, 3, rng)
    radius = np.sqrt((xs + 0.2 * warp_x) ** 2 + (ys + 0.2 * warp_y) ** 2)

    billows = fbm(shape, 3.0, 4, rng)
    edge = 0.8 + 0.1 * t + 0.22 * billows
    body = smoothstep(edge, edge - 0.45 - 0.15 * t, radius)
    detail = 0.7 + 0.3 * fbm(shape, 4.0, 3, rng)
    breakup = smoothstep(-0.35, 0.35, fbm(shape, 4.0, 3, rng) + 1.1 - 1.4 * t ** 2)
    density = np.clip(body * detail * breakup, 0.0, 1.0)
    return shade(density), density


def sample(volume, ty, tx):
    """Bilinear samples of each frame of a (frames, h, w) volume at fractional pixel coordinates."""
    count, h, w = volume.shape
    y0 = np.floor(ty).astype(int)
    x0 = np.floor(tx).astype(int)
    fy = ty - y0
    fx = tx - x0
    f = np.arange(count)[:, None, None]

    def at(y, x):
        return volume[f, np.clip(y, 0, h - 1), np.clip(x, 0, w - 1)]

    return (at(y0, x0) * (1 - fx) * (1 - fy) + at(y0, x0 + 1) * fx * (1 - fy)
            + at(y0 + 1, x0) * (1 - fx) * fy + at(y0 + 1, x0 + 1) * fx * fy)


def wisp_frames(variants, steps, rng):
    """Smoke wisps rather than puffs: ragged, swirled filaments drawn out
    along the frame's height, with no round outline. Each variant is a row
    of frames over its life, coherent from frame to frame, its filaments
    swirling further, spreading, and thinning as it goes."""
    lit_rows, density_rows = [], []
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    pixels_y, pixels_x = np.mgrid[0:FRAME, 0:FRAME].astype(float)
    for _ in range(variants):
        shape = (steps, FRAME, FRAME)
        t = np.linspace(0.0, 1.0, steps)[:, None, None]
        # Slow over the frames (a third of the detail along time), so a
        # wisp evolves rather than flickers.
        warp_x = fbm(shape, 2.0, 3, rng, stretch=(0.35, 1.0, 1.0))
        warp_y = fbm(shape, 2.0, 3, rng, stretch=(0.35, 1.0, 1.0))
        # Filaments: the ridges of noise stretched along the height, swirled by the warp.
        strands = fbm(shape, 3.0, 5, rng, stretch=(0.35, 0.4, 1.0))
        body = fbm(shape, 2.5, 4, rng, stretch=(0.35, 1.0, 1.0))
        reach = (0.16 + 0.12 * t) * FRAME
        ty = pixels_y[None] + reach * warp_y
        tx = pixels_x[None] + reach * warp_x
        ridges = 1.0 - np.abs(sample(strands, ty, tx))
        filaments = ridges ** (2.5 + 1.5 * t)
        soft = 0.5 + 0.5 * sample(body, ty, tx)
        texture = 0.55 * soft + 0.45 * filaments

        # A ragged, drawn-out outline, wider as it spreads.
        aspect = rng.uniform(1.3, 1.9)
        radius = np.sqrt((xs + 0.35 * warp_x) ** 2 + ((ys + 0.35 * warp_y) / aspect) ** 2)
        outline = smoothstep(0.85 + 0.15 * t + 0.15 * body, 0.15, radius)
        border = smoothstep(1.0, 0.86, np.maximum(np.abs(xs), np.abs(ys)))[None]
        density = texture * outline * border
        # Thinning over its life: the faint parts go first.
        density = np.clip((density - 0.04 - 0.06 * t) / (0.7 - 0.1 * t), 0.0, 1.0)
        density = density / np.maximum(density.max(axis=(1, 2), keepdims=True), 1e-6) * 0.95

        # Its surface, for the shader to light: the wisp's mass, smoothed,
        # rounded into soft lobes where it is thick (billows inside a
        # wisp's ragged outline), with its strands' relief on top.
        lit_rows.append(wisp_surface(density, np.random.default_rng(RNG_SEED + 1 + len(lit_rows))))
        density_rows.append(density)
    return np.concatenate(lit_rows), np.concatenate(density_rows)


def wisp_baked(density, steps):
    """The wisps' relief baked in, lit from above, as 1.13's plume drew
    them (its crinkled wisps looked far better): each wisp's
    density-weighted mean at the puffs' 0.63."""
    rows = []
    for start in range(0, density.shape[0], steps):
        row = density[start:start + steps]
        relief = shade(row)
        weighted = (relief * row).sum() / max(row.sum(), 1e-6)
        rows.append(np.clip(0.63 + 0.45 * (relief - weighted), 0.0, 1.0))
    return np.concatenate(rows)


def wisp_surface(density, rng):
    """A wisp's surface over its frames: its mass blurred into a body,
    soft lobes of a few sizes where the mass is (fewer and flatter as it
    frays), and the density's own fine relief; B its thickness."""
    steps = density.shape[0]
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    up = -ys
    mass = blur(density, 6)
    mass = mass / np.maximum(mass.max(axis=(1, 2), keepdims=True), 1e-6)
    # Lobes placed where the first frame's mass is, drifting out with the wisp.
    weights = mass[0].ravel() ** 2
    weights = weights / weights.sum()
    lobes = []
    for size, count, low, high in ((0, 6, 0.22, 0.34), (1, 16, 0.11, 0.18), (2, 24, 0.06, 0.09)):
        picks = rng.choice(weights.size, size=count, p=weights)
        for pick in picks:
            row, col = divmod(int(pick), FRAME)
            lobes.append((xs[row, col], up[row, col], rng.uniform(low, high), size))
    heights = []
    for step in range(steps):
        t = step / max(1, steps - 1)
        # Smooth bumps summed (no creases where lobes meet), each as high as it is wide.
        lobed = np.zeros((FRAME, FRAME))
        for x, y, r, size in lobes:
            r = r * (1.0 + 0.2 * t) * (1.0 - 0.45 * size * t)
            if r <= 0.01:
                continue
            spread = 1.0 + 0.2 * t
            d2 = (xs - x * spread) ** 2 + (up - y * spread) ** 2
            lobed += 0.6 * r * np.exp(-d2 / (0.5 * r * r))
        body = smoothstep(0.03, 0.35, mass[step])
        heights.append(0.3 * mass[step] + (1.0 - 0.5 * t) * blur(lobed[None], 2)[0] * body)
    return surface(np.stack(heights), mass)


def blur(image, passes=1):
    """A light box blur over the last two axes, edges held."""
    for _ in range(passes):
        padded = np.pad(image, [(0, 0)] * (image.ndim - 2) + [(1, 1), (1, 1)], mode="edge")
        image = sum(padded[..., 1 + dy:padded.shape[-2] - 1 + dy, 1 + dx:padded.shape[-1] - 1 + dx]
                    for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9.0
    return image


def surface(height, thickness, flat=0.0):
    """The shader's surface for a height field over the frame (y up): RG the
    normal's x and y (0.5 level), B the thickness through it. Flat leans
    the normal toward the viewer, 0 to 1."""
    step = 2.0 / FRAME
    dy, dx = np.gradient(blur(height), step, axis=(-2, -1))
    # Rows grow downward; the normal's y is up the frame.
    nx, ny, nz = -dx, dy, np.full_like(height, 1.0 + 4.0 * flat)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([0.5 + 0.5 * nx / norm, 0.5 + 0.5 * ny / norm, np.clip(thickness, 0.0, 1.0)], axis=-1)
    return np.clip(rgb, 0.0, 1.0)


def billow_frames(variants, steps, rng):
    """Billows for dense, young steam (real plumes are round, billowing
    shapes): a cluster of round lobes, a big one, middling
    ones on its sides and top, small ones on those (a cauliflower's lobes,
    their outline round but not one ball), blended softly where they meet
    and firm at the edge. Over its life each loosens, its small lobes
    smoothing away, and frays as it thins. The shader blends to the wisps
    as a particle thins, so these show only where the steam is thick. The
    lobes' fronts give the surface; their depth through, the thickness."""
    surface_rows, density_rows = [], []
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    up = -ys  # 1 at the top row
    for _ in range(variants):
        shape = (steps, FRAME, FRAME)
        warp_x = fbm(shape, 2.0, 3, rng, stretch=(0.35, 1.0, 1.0))
        warp_y = fbm(shape, 2.0, 3, rng, stretch=(0.35, 1.0, 1.0))
        rough = fbm(shape, 6.0, 3, rng, stretch=(0.35, 1.0, 1.0))
        holes = fbm(shape, 3.0, 4, rng, stretch=(0.35, 1.0, 1.0))
        # The lobes: (x, y, radius, size), a big one off the middle, then
        # each size on the outside of the ones before, more toward the top.
        lobes = [(rng.uniform(-0.06, 0.06), rng.uniform(-0.12, 0.02), rng.uniform(0.3, 0.36), 0)]
        for size, count, low, high in ((1, 9, 0.2, 0.3), (2, 26, 0.09, 0.15)):
            parents = [lobe for lobe in lobes if lobe[3] == size - 1]
            for _ in range(count):
                px, py, pr, _ = parents[rng.integers(len(parents))]
                angle = rng.uniform(-0.2 * np.pi, 1.2 * np.pi) if rng.random() < 0.7 else rng.uniform(0.0, 2.0 * np.pi)
                lobes.append((px + 0.92 * pr * np.cos(angle), py + 0.92 * pr * np.sin(angle), rng.uniform(low, high), size))
        heights, chords, densities = [], [], []
        for step in range(steps):
            t = step / max(1, steps - 1)
            reach = 0.03 + 0.12 * t
            wx = xs + reach * warp_x[step]
            wy = up + reach * warp_y[step]
            # Soft union: the lobes' fronts summed as exponentials, so they
            # blend where they meet and keep their round outline.
            blend = np.zeros((FRAME, FRAME))
            chord = np.zeros((FRAME, FRAME))
            for x, y, r, size in lobes:
                r = r * (1.0 + 0.2 * t) * (1.0 - 0.2 * size * t)
                if r <= 0.01:
                    continue
                spread = 1.0 + 0.15 * t
                cap = np.sqrt(np.maximum(0.0, r * r - (wx - x * spread) ** 2 - (wy - y * spread) ** 2))
                blend += np.where(cap > 0, np.exp(cap / 0.015), 0.0)
                chord += 2.0 * cap
            # Each lobe's front stands on its own, softened only where they meet.
            height = np.where(blend > 0, 0.015 * np.log(np.maximum(blend, 1.0)), 0.0)
            height = height + (0.02 - 0.012 * t) * rough[step] * (height > 0)
            # Firm at the edge, softened a little (crisp edges made the
            # billows' onset sharp); holes open as it thins late.
            body = smoothstep(0.0, 0.04 + 0.04 * t, height)
            thin = smoothstep(-0.3 - 0.4 * t, 0.15, holes[step] + 0.7 - 0.55 * t)
            border = smoothstep(1.0, 0.88, np.maximum(np.abs(xs), np.abs(ys)))
            density = np.clip(body * (0.8 + 0.2 * rough[step]) * thin * border, 0.0, 1.0)
            heights.append(height)
            chords.append(chord)
            densities.append(density)
        density = np.stack(densities)
        density = density / np.maximum(density.max(axis=(1, 2), keepdims=True), 1e-6) * 0.95
        chord = np.stack(chords)
        thickness = np.clip(chord / np.maximum(np.percentile(chord, 99), 1e-6), 0.0, 1.0)
        surface_rows.append(surface(np.stack(heights), thickness))
        density_rows.append(density)
    return np.concatenate(surface_rows), np.concatenate(density_rows)


def jet_frames(count, rng):
    """Streaks along the frame's height, for particles stretched along their motion."""
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    shape = (count, FRAME, FRAME)
    streaks = fbm(shape, 3.0, 4, rng, stretch=(1.0, 0.25, 1.0))
    width = 0.35 + 0.15 * streaks
    core = np.exp(-((xs + 0.1 * streaks) / width) ** 2)
    ends = smoothstep(1.0, 0.55, np.abs(ys))
    density = np.clip(core * ends * (0.6 + 0.4 * fbm(shape, 5.0, 3, rng)), 0.0, 1.0)
    # A streak is a soft tube: its density as its height, flattish.
    return surface(0.25 * blur(density, 2), density, flat=0.3), density


def spark_texture(size=64):
    """A hot point with a soft halo; the mod stretches it along the cinder's flight."""
    ys, xs = np.mgrid[-1:1:size * 1j, -1:1:size * 1j]
    r2 = xs * xs + ys * ys
    glow = np.clip(np.exp(-r2 / 0.012) + 0.45 * np.exp(-r2 / 0.09), 0.0, 1.0)
    rgba = np.zeros((size, size, 4), dtype=np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (glow * 255).astype(np.uint8)
    return Image.fromarray(rgba)


def atlas(lit, density, columns):
    """Frames into a sheet: RGB grayscale lit relief (frames × h × w) or a
    surface (frames × h × w × 3), A the density."""
    count = lit.shape[0]
    rows = int(np.ceil(count / columns))
    rgba = np.zeros((rows * FRAME, columns * FRAME, 4), dtype=np.uint8)
    for i in range(count):
        r, c = divmod(i, columns)
        tile = rgba[r * FRAME:(r + 1) * FRAME, c * FRAME:(c + 1) * FRAME]
        rgb = lit[i] if lit.ndim == 4 else lit[i][..., None]
        tile[..., :3] = np.round(rgb * 255).astype(np.uint8)
        tile[..., 3] = np.round(density[i] * 255).astype(np.uint8)
    return Image.fromarray(rgba)


def preview(images, path):
    """The atlases over a sky-blue background, as they would read in the sky."""
    width = sum(image.width for image in images)
    height = max(image.height for image in images)
    sheet = Image.new("RGBA", (width, height), (120, 160, 205, 255))
    x = 0
    for image in images:
        sheet.alpha_composite(image, (x, 0))
        x += image.width
    sheet.convert("RGB").save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--preview", type=pathlib.Path)
    args = parser.parse_args()

    rng = np.random.default_rng(RNG_SEED)
    args.output.mkdir(parents=True, exist_ok=True)
    puff = atlas(*puff_frames(64, rng), columns=8)
    jet = atlas(*jet_frames(16, rng), columns=4)
    wisp_lit, wisp_density = wisp_frames(8, 8, rng)
    wisp = atlas(wisp_lit, wisp_density, columns=8)
    wisp_baked_sheet = atlas(wisp_baked(wisp_density, 8), wisp_density, columns=8)
    # The billows from a stream of their own, so the others stay as they were.
    billow = atlas(*billow_frames(8, 8, np.random.default_rng(RNG_SEED + 100)), columns=8)
    puff.save(args.output / "smoke_puff_flipbook.png")
    billow.save(args.output / "smoke_billow_flipbook.png")
    jet.save(args.output / "smoke_jet_flipbook.png")
    wisp.save(args.output / "smoke_wisp_flipbook.png")
    wisp_baked_sheet.save(args.output / "smoke_wisp_baked.png")
    spark_texture().save(args.output / "spark.png")
    if args.preview:
        preview([wisp, puff, jet], args.preview)


if __name__ == "__main__":
    main()
