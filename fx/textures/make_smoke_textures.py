#!/usr/bin/env python3
"""Generate the chimney smoke flipbook textures for the Realistic Firing FX.

The textures are grayscale lit density with alpha, tinted per particle by the
mod (white steam, grey haze, dark soot), so one set serves every color.

  smoke_puff_flipbook.png  8 x 8 frames: a puff that swells and breaks up
  smoke_jet_flipbook.png   4 x 4 frames: streaky exhaust jet
  spark.png                a cinder's glow: hot core, soft halo

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
    """Nearest-free linear resize of a (d, h, w) volume, frame by frame."""
    frames = []
    for z in np.linspace(0, volume.shape[0] - 1, shape[0]):
        frame = volume[int(round(z))]
        image = Image.fromarray(frame.astype(np.float32)).resize((shape[2], shape[1]), Image.BILINEAR)
        frames.append(np.asarray(image))
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


def jet_frames(count, rng):
    """Streaks along the frame's height, for particles stretched along their motion."""
    ys, xs = np.mgrid[-1:1:FRAME * 1j, -1:1:FRAME * 1j]
    shape = (count, FRAME, FRAME)
    streaks = fbm(shape, 3.0, 4, rng, stretch=(1.0, 0.25, 1.0))
    width = 0.35 + 0.15 * streaks
    core = np.exp(-((xs + 0.1 * streaks) / width) ** 2)
    ends = smoothstep(1.0, 0.55, np.abs(ys))
    density = np.clip(core * ends * (0.6 + 0.4 * fbm(shape, 5.0, 3, rng)), 0.0, 1.0)
    return shade(density), density


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
    count = lit.shape[0]
    rows = int(np.ceil(count / columns))
    rgba = np.zeros((rows * FRAME, columns * FRAME, 4), dtype=np.uint8)
    for i in range(count):
        r, c = divmod(i, columns)
        tile = rgba[r * FRAME:(r + 1) * FRAME, c * FRAME:(c + 1) * FRAME]
        tile[..., :3] = (lit[i] * 255).astype(np.uint8)[..., None]
        tile[..., 3] = (density[i] * 255).astype(np.uint8)
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
    puff.save(args.output / "smoke_puff_flipbook.png")
    jet.save(args.output / "smoke_jet_flipbook.png")
    spark_texture().save(args.output / "spark.png")
    if args.preview:
        preview([puff, jet], args.preview)


if __name__ == "__main__":
    main()
