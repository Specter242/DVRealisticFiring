#!/usr/bin/env python3
"""Generate the starting fuel's meshes, textures, and icons for the Realistic Firing FX.

Meshes (Models/starting_fuel.json, read by PlumeBuilder into Mesh assets
and the StartingFuelFX prefab), all in metres, Unity's axes (x right, y up,
z forward), each with normals, UVs, vertex colours (baked occlusion), and
submeshes by material:

  wood 0-2   quarter and third splits of 12-15 cm logs, 0.31-0.35 m long:
             a convex bark back, two torn split faces meeting near the
             pith (broken off at the apex), sawn ends a few degrees out of
             square. Materials: split face, bark, end grain.
  rag 0-2    a 0.32 m square of cotton waste, frayed, folded and crumpled
             three ways (folded, balled, wadded); both sides drawn with
             their own normals.
  crate      an open slatted pine crate with corner posts, gapped boards,
             a hand-hold gap at each end, and iron corner angles; twelve
             slots for its splits, the top layer taken first.
  rag tin    a painted steel bin, tapered, with a rolled rim, wire
             handles, and dents; twelve slots for its rags.

Textures (Textures/), 512 x 512 RGBA, albedo with smoothness in alpha, and
tangent-space normal maps (_n):

  sf_split   the split face: fibre tears along the grain, growth rings as
             long bands, a few drying checks.
  sf_bark    furrowed grey-brown bark, its ridges cracked across, lichen.
  sf_end     the sawn end: rings about the pith at the texture's centre,
             heartwood and sapwood, radial checks, the bark band at 0.40-0.44.
  sf_cloth   plain-woven cotton, faded, stained with grease and soot.
  sf_crate   weathered pine boards along U, knots, nail holes.
  sf_strap   blackened, rusting iron.
  sf_tin     green-painted steel along its girth (U) and height (V),
             chipped to primer and rust at its edges and rim, a WASTE
             stencil on its front.

Icons: starting_{wood,rag,crate,tin}_icon.png, 128 x 128, the models drawn
by this script's own rasteriser in the game's dark shaded style, and
_dropped outlines in its light blue.

Everything comes from seeded noise and geometry here: our own art,
reproducible. Requires numpy and Pillow.

Usage: make_starting_fuel.py TEXTURES_DIR MODELS_DIR [--preview DIR]
"""

import argparse
import json
import math
import pathlib

import numpy as np
from PIL import Image

RNG_SEED = 588030 + 17
SIZE = 512


# ---------------------------------------------------------------- noise

def tile_noise(h, w, cy, cx, rng):
    """Smooth value noise over an h x w grid that tiles, in [-1, 1]."""
    lattice = rng.uniform(-1.0, 1.0, size=(cy, cx))

    def axis(n, cells):
        x = np.arange(n) * cells / n
        i = np.floor(x).astype(int)
        f = x - i
        return i % cells, (i + 1) % cells, f * f * f * (f * (f * 6 - 15) + 10)

    y0, y1, fy = axis(h, cy)
    x0, x1, fx = axis(w, cx)
    a = lattice[np.ix_(y0, x0)]
    b = lattice[np.ix_(y0, x1)]
    c = lattice[np.ix_(y1, x0)]
    d = lattice[np.ix_(y1, x1)]
    fy, fx = fy[:, None], fx[None, :]
    top = a + (b - a) * fx
    bottom = c + (d - c) * fx
    return top + (bottom - top) * fy


def fbm(h, w, cy, cx, octaves, rng, gain=0.5):
    total = np.zeros((h, w))
    amp, weight = 1.0, 0.0
    for o in range(octaves):
        total += amp * tile_noise(h, w, cy * 2 ** o, cx * 2 ** o, rng)
        weight += amp
        amp *= gain
    return total / weight


def normal_map(height, strength):
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    rgba = np.concatenate([n * 0.5 + 0.5, np.ones_like(height)[..., None]], axis=-1)
    return (np.clip(rgba, 0.0, 1.0) * 255).astype(np.uint8)


def pack(rgb, smooth):
    """Linear albedo and smoothness to sRGB RGBA bytes."""
    out = np.concatenate([np.clip(rgb, 0, 1) ** (1 / 2.2), np.clip(smooth, 0, 1)[..., None]], axis=-1)
    return (out * 255).astype(np.uint8)


def mix(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return np.asarray(a) * (1 - t) + np.asarray(b) * t


# ---------------------------------------------------------------- textures

def split_face(rng):
    """U along the grain (0.4 m), V out from the pith (0.1 m)."""
    h = w = SIZE
    v = np.linspace(0, 1, h, endpoint=False)[:, None]
    warp = 0.012 * fbm(h, w, 4, 3, 3, rng)
    radius = (v + warp) * 0.1
    ring = (radius / 0.0032 + 0.6 * fbm(h, w, 6, 2, 2, rng)) % 1.0
    latewood = np.clip((ring - 0.72) / 0.12, 0, 1) * np.clip((1.0 - ring) / 0.06, 0, 1)
    fibre = fbm(h, w, 96, 4, 3, rng)
    torn = fbm(h, w, 40, 3, 4, rng)
    # Long torn ridges and grooves: the split follows the fibre.
    height = 0.55 * torn + 0.25 * fibre - 0.25 * latewood
    checks = np.zeros((h, w))
    for _ in range(4):
        row = rng.uniform(0.1, 0.95)
        width = rng.uniform(0.002, 0.005)
        start, length = rng.uniform(0, 1), rng.uniform(0.25, 0.7)
        u = np.linspace(0, 1, w, endpoint=False)[None, :]
        along = ((u - start) % 1.0) < length
        wobble = 0.004 * np.sin(u * 13 + row * 40)
        checks += along * np.exp(-((v - row - wobble) / width) ** 2)
    checks = np.clip(checks, 0, 1)
    height -= 1.4 * checks
    # Linear albedo (sRGB is applied in pack): seasoned oak-like splits, a
    # season stacked outdoors. The first palette (0.30, 0.20, 0.11) read as
    # fresh-cut cream in the game (review 2026-11-11), and a third darker
    # still read pale (r20), but under lighting compiled for gamma space
    # (StartingFuel.shader): its ambient and reflections washed any albedo
    # out. Lit for linear space, this, the second palette, is a weathered
    # medium brown.
    early = np.array([0.20, 0.135, 0.075])
    late = np.array([0.115, 0.07, 0.035])
    rgb = mix(early, late, latewood * 0.85)
    rgb = rgb * (0.82 + 0.22 * fibre[..., None] + 0.10 * torn[..., None])
    # Weathered: silver-brown toward the old outside of the log, and grime
    # in the torn grooves from the stack and the crate.
    rgb = mix(rgb, np.array([0.15, 0.13, 0.105]), 0.45 * np.clip(v - 0.4, 0, 1) / 0.6)
    # Its own generator, so the other textures' noise stays as it was.
    grime = np.clip((fbm(h, w, 6, 3, 3, np.random.default_rng(RNG_SEED + 101)) - 0.1) * 2.5, 0, 1) * np.clip(0.6 - torn, 0, 1)
    rgb = rgb * (1.0 - 0.35 * grime[..., None])
    rgb = mix(rgb, np.array([0.08, 0.06, 0.05]), checks)
    smooth = 0.14 + 0.06 * latewood - 0.08 * checks
    return pack(rgb, smooth), normal_map(height, 7.0)


def bark(rng):
    """U along the log, V around it: long furrows along the log between
    corky ridges, the ridges broken across here and there, grit, lichen."""
    h = w = SIZE
    v = np.linspace(0, 1, h, endpoint=False)[:, None]
    # Long, slightly wandering furrows: low frequency along the log.
    around = v * 11 + 0.35 * fbm(h, w, 5, 2, 3, rng) + 0.08 * fbm(h, w, 24, 6, 2, rng)
    profile = np.abs(np.sin(np.pi * around))
    ridge = np.clip((profile - 0.25) / 0.6, 0, 1) ** 0.7
    # Breaks across a ridge every so often.
    breaks = np.clip((np.abs(fbm(h, w, 11, 26, 2, rng)) - 0.02) * 9, 0, 1)
    plates = ridge * breaks
    grit = fbm(h, w, 80, 80, 2, rng)
    flakes = fbm(h, w, 22, 40, 3, rng)
    height = 1.0 * plates + 0.25 * flakes * plates + 0.12 * grit
    lichen = np.clip((fbm(h, w, 9, 9, 3, rng) - 0.42) * 6, 0, 1) * plates
    deep = np.array([0.025, 0.019, 0.014])
    face = np.array([0.065, 0.05, 0.036])
    top = np.array([0.11, 0.092, 0.075])
    rgb = mix(deep, mix(face, top, np.clip(0.45 + 0.7 * flakes, 0, 1)), plates)
    rgb = rgb * (0.9 + 0.15 * grit[..., None])
    rgb = mix(rgb, np.array([0.20, 0.22, 0.17]), 0.45 * lichen)
    smooth = 0.05 + 0.04 * plates
    return pack(rgb, smooth), normal_map(height, 12.0)


def end_grain(rng):
    """Rings about the centre (the pith); bark from radius 0.40 to 0.44."""
    h = w = SIZE
    yy, xx = np.mgrid[0:h, 0:w].astype(float) / SIZE - 0.5
    r = np.hypot(xx, yy)
    theta = np.arctan2(yy, xx)
    wobble = 0.006 * np.sin(theta * 3 + 1.3) + 0.004 * fbm(h, w, 8, 8, 3, rng)
    rr = r + wobble
    # Wider rings near the pith, narrower toward the bark (a slowing tree).
    rings = 34 * rr + 26 * rr ** 2
    phase = rings % 1.0
    latewood = np.clip((phase - 0.7) / 0.14, 0, 1) * np.clip((1.0 - phase) / 0.06, 0, 1)
    heart = np.clip((0.28 - rr) / 0.03, 0, 1)
    pores = fbm(h, w, 90, 90, 2, rng)
    early_heart = np.array([0.20, 0.12, 0.062])
    early_sap = np.array([0.27, 0.195, 0.115])
    late = np.array([0.15, 0.09, 0.045])
    rgb = mix(mix(early_sap, early_heart, heart), late, latewood * 0.8)
    rgb = rgb * (0.9 + 0.12 * pores[..., None])
    # Drying checks: dark wedges out from the pith.
    checks = np.zeros((h, w))
    for _ in range(9):
        angle = rng.uniform(-math.pi, math.pi)
        reach = rng.uniform(0.12, 0.38)
        width = rng.uniform(0.004, 0.009)
        d = np.angle(np.exp(1j * (theta - angle)))
        taper = np.clip(1 - rr / reach, 0, 1)
        checks += np.exp(-((d * rr) / (width * taper + 1e-4)) ** 2) * (rr < reach) * (rr > 0.01)
    checks = np.clip(checks, 0, 1)
    # Chainsaw: faint arcs across the cut.
    saw = 0.04 * np.sin((xx * 0.8 + yy * 0.6) * 260 + 2 * fbm(h, w, 4, 4, 2, rng))
    rgb = rgb * (1 + saw[..., None])
    pith = np.exp(-(r / 0.008) ** 2)
    rgb = mix(rgb, np.array([0.18, 0.11, 0.07]), np.clip(pith + checks, 0, 1))
    # The bark band, and a cambium line inside it.
    bark_band = np.clip((r - 0.40) / 0.004, 0, 1)
    cambium = np.exp(-((r - 0.398) / 0.003) ** 2)
    rgb = mix(rgb, np.array([0.30, 0.19, 0.10]), 0.6 * cambium)
    bark_col = np.array([0.08, 0.06, 0.045]) * (0.8 + 0.3 * pores[..., None])
    rgb = mix(rgb, bark_col, bark_band)
    height = -0.6 * checks + 0.15 * pores - 0.3 * latewood + 0.2 * bark_band * pores
    smooth = 0.12 + 0.06 * latewood
    return pack(rgb, smooth), normal_map(height, 6.0)


def cloth(rng):
    h = w = SIZE
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    threads = 0.5 + 0.5 * np.sin(xx * np.pi / 2.0) * np.sin(yy * np.pi / 2.0)
    weave = np.where((np.floor(xx / 2) + np.floor(yy / 2)) % 2 == 0, np.sin(xx * np.pi / 2) ** 2, np.sin(yy * np.pi / 2) ** 2)
    fuzz = fbm(h, w, 64, 64, 2, rng)
    base = np.array([0.24, 0.22, 0.185])
    stripe = np.exp(-(((yy / SIZE * 3) % 1.0 - 0.5) / 0.05) ** 2)
    rgb = mix(base, np.array([0.10, 0.15, 0.24]), 0.55 * stripe)
    rgb = rgb * (0.82 + 0.18 * weave[..., None] + 0.06 * fuzz[..., None])
    stains = np.clip((fbm(h, w, 5, 5, 4, rng) - 0.05) * 3, 0, 1)
    soot = np.clip((fbm(h, w, 7, 7, 3, rng) - 0.25) * 4, 0, 1)
    rgb = mix(rgb, np.array([0.14, 0.11, 0.07]), 0.6 * stains)
    rgb = mix(rgb, np.array([0.08, 0.08, 0.08]), 0.5 * soot)
    height = 0.5 * weave + 0.2 * threads + 0.15 * fuzz
    smooth = 0.05 + 0.10 * stains
    return pack(rgb, smooth), normal_map(height, 2.5)


def crate_boards(rng):
    """U along the boards; each board takes a band of V."""
    h = w = SIZE
    v = np.linspace(0, 1, h, endpoint=False)[:, None]
    grain = fbm(h, w, 70, 3, 3, rng)
    rings = np.abs(np.sin((v * 40 + 0.8 * fbm(h, w, 8, 2, 3, rng)) * np.pi))
    weather = fbm(h, w, 12, 6, 4, rng)
    knots = np.zeros((h, w))
    yy, xx = np.mgrid[0:h, 0:w].astype(float) / SIZE
    for _ in range(5):
        cy, cx = rng.uniform(0, 1), rng.uniform(0, 1)
        dx = np.minimum(np.abs(xx - cx), 1 - np.abs(xx - cx))
        dy = np.minimum(np.abs(yy - cy), 1 - np.abs(yy - cy))
        knots += np.exp(-(dx / 0.018) ** 2 - (dy / 0.03) ** 2)
    knots = np.clip(knots, 0, 1)
    fresh = np.array([0.15, 0.10, 0.058])
    grey = np.array([0.085, 0.077, 0.066])
    rgb = mix(fresh, grey, np.clip(0.55 + 0.6 * weather, 0, 1))
    rgb = rgb * (0.85 + 0.15 * grain[..., None] - 0.1 * rings[..., None] ** 8)
    rgb = mix(rgb, np.array([0.09, 0.055, 0.03]), 0.8 * knots)
    dirt = np.clip((fbm(h, w, 6, 6, 3, rng) + 0.2) * 1.5, 0, 1)
    rgb = mix(rgb, np.array([0.06, 0.05, 0.04]), 0.35 * dirt)
    # Nail holes, rust-ringed, near the board ends.
    for end in (0.03, 0.97):
        for row in np.linspace(0.08, 0.92, 6):
            d = np.hypot((xx - end) * 4, (yy - row) * 4)
            rgb = mix(rgb, np.array([0.12, 0.08, 0.05]), np.exp(-(d / 0.01) ** 2))
            rgb = mix(rgb, np.array([0.40, 0.20, 0.08]), 0.4 * np.exp(-(d / 0.025) ** 2))
    height = 0.4 * grain - 0.4 * rings ** 6 + 0.3 * weather - 0.5 * knots
    smooth = 0.08 + 0.05 * (1 - dirt)
    return pack(rgb, smooth), normal_map(height, 5.0)


def iron_strap(rng):
    h = w = SIZE
    rust = np.clip((fbm(h, w, 8, 8, 4, rng) + 0.1) * 2, 0, 1)
    pits = fbm(h, w, 60, 60, 2, rng)
    black = np.array([0.07, 0.065, 0.06])
    brown = np.array([0.32, 0.17, 0.08])
    rgb = mix(black, brown, rust * 0.8) * (0.9 + 0.15 * pits[..., None])
    smooth = 0.35 - 0.25 * rust
    return pack(rgb, smooth), normal_map(0.4 * pits + 0.5 * rust, 4.0)


STENCIL = {
    # Block letters on a 5 x 7 grid, as strokes (x0, y0, x1, y1).
    "W": [(0, 0, 1, 7), (1, 7, 2.5, 3), (2.5, 3, 4, 7), (4, 7, 5, 0)],
    "A": [(0, 7, 2.5, 0), (2.5, 0, 5, 7), (1, 4.2, 4, 4.2)],
    "S": [(5, 0.5, 0, 0.5), (0, 0.5, 0, 3.5), (0, 3.5, 5, 3.5), (5, 3.5, 5, 6.5), (5, 6.5, 0, 6.5)],
    "T": [(0, 0.5, 5, 0.5), (2.5, 0.5, 2.5, 7)],
    "E": [(5, 0.5, 0, 0.5), (0, 0.5, 0, 6.5), (0, 6.5, 5, 6.5), (0, 3.5, 4, 3.5)],
}


def stencil_mask(h, w, text, u0, v0, height):
    """A stencil's coverage: letters 'height' tall (V units) from (u0, v0)."""
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    xx, yy = xx / w, yy / h
    mask = np.zeros((h, w))
    cell = height / 7.0
    stroke = cell * 0.75
    x = u0
    for ch in text:
        for (ax, ay, bx, by) in STENCIL[ch]:
            a = np.array([x + ax * cell * 0.62, v0 + ay * cell])
            b = np.array([x + bx * cell * 0.62, v0 + by * cell])
            ab = b - a
            t = np.clip(((xx - a[0]) * ab[0] + (yy - a[1]) * ab[1]) / max(ab @ ab, 1e-9), 0, 1)
            d = np.hypot(xx - (a[0] + t * ab[0]), (yy - (a[1] + t * ab[1])) * (h / w))
            mask = np.maximum(mask, np.clip((stroke / 2 - d) / 0.0015, 0, 1))
        x += cell * 0.62 * 6.5
    return mask


def tin(rng):
    """U around the girth (front panel 0.04-0.29), V up the side (rim at 1)."""
    h = w = SIZE
    v = np.linspace(1, 0, h, endpoint=False)[:, None] * np.ones((1, w))
    u = np.linspace(0, 1, w, endpoint=False)[None, :] * np.ones((h, 1))
    paint = np.array([0.035, 0.065, 0.04])
    noise = fbm(h, w, 16, 16, 4, rng)
    # Chips to primer and rust at the corners (panel edges in U) and rim.
    corner = np.zeros((h, w))
    for c in (0.0, 0.33, 0.5, 0.83, 1.0):
        corner = np.maximum(corner, np.exp(-((u - c) / 0.02) ** 2))
    edge = np.maximum(corner, np.exp(-((1 - v) / 0.05) ** 2))
    chip = np.clip((noise + edge * 0.9 - 0.55) * 6, 0, 1)
    rust = np.clip((fbm(h, w, 24, 24, 3, rng) + chip - 0.6) * 3, 0, 1)
    rgb = mix(paint * (0.9 + 0.2 * noise[..., None]), np.array([0.22, 0.21, 0.19]), chip)
    rgb = mix(rgb, np.array([0.17, 0.06, 0.02]), rust * chip)
    # Streaks of grime downward.
    streak = np.clip(fbm(h, w, 2, 40, 3, rng) + 0.2, 0, 1) * (1 - v)
    rgb = mix(rgb, np.array([0.03, 0.028, 0.022]), 0.35 * streak)
    letters = stencil_mask(h, w, "WASTE", 0.06, 0.38, 0.11)
    worn = np.clip(letters - np.clip(noise * 1.5, 0, 1) * 0.4, 0, 1)
    rgb = mix(rgb, np.array([0.42, 0.40, 0.33]), 0.85 * worn * (1 - chip))
    smooth = 0.32 - 0.22 * chip - 0.1 * streak
    height = 0.5 * chip + 0.2 * noise
    return pack(rgb, smooth), normal_map(height, 3.0)


# ---------------------------------------------------------------- meshes

class Mesh:
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials
        self.v, self.n, self.uv, self.c = [], [], [], []
        self.sub = [[] for _ in materials]

    def add(self, p, n, uv, c=1.0):
        self.v.append(np.asarray(p, float))
        self.n.append(np.asarray(n, float) / (np.linalg.norm(n) + 1e-12))
        self.uv.append(np.asarray(uv, float))
        self.c.append(float(c))
        return len(self.v) - 1

    def tri(self, sub, a, b, c, outward=None):
        """A triangle facing 'outward' (or its vertices' normals). Unity's
        front faces wind clockwise as seen, which in its left-handed axes
        puts cross(b - a, c - a) toward the viewer."""
        pa, pb, pc = self.v[a], self.v[b], self.v[c]
        face = np.cross(pb - pa, pc - pa)
        want = outward if outward is not None else self.n[a] + self.n[b] + self.n[c]
        if np.dot(face, want) < 0:
            b, c = c, b
        self.sub[sub].append((a, b, c))

    def grid(self, sub, rows, outward_fn=None):
        """Triangles over a grid of vertex indices rows[i][j]."""
        for i in range(len(rows) - 1):
            for j in range(len(rows[i]) - 1):
                a, b, c, d = rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]
                out = outward_fn(i, j) if outward_fn else None
                self.tri(sub, a, b, c, out)
                self.tri(sub, a, c, d, out)

    def transformed(self, matrix, offset):
        m = Mesh(self.name, self.materials)
        m.v = [matrix @ p + offset for p in self.v]
        m.n = [matrix @ n for n in self.n]
        m.uv, m.c, m.sub = list(self.uv), list(self.c), [list(s) for s in self.sub]
        return m

    def json(self):
        v = np.array(self.v)
        center = (v.min(axis=0) + v.max(axis=0)) / 2
        return {
            "name": self.name,
            "materials": self.materials,
            "vertices": np.round(v, 5).ravel().tolist(),
            "normals": np.round(np.array(self.n), 4).ravel().tolist(),
            "uv": np.round(np.array(self.uv), 5).ravel().tolist(),
            "colors": np.round(np.array(self.c), 3).tolist(),
            "submeshes": [{"triangles": np.array(s, int).ravel().tolist()} for s in self.sub],
            "bounds": {"center": np.round(center, 4).tolist(), "size": np.round(v.max(axis=0) - v.min(axis=0), 4).tolist()},
        }


def smooth_normals(mesh, sub_indices=None):
    """Area-weighted normals over each submesh's shared vertices."""
    acc = [np.zeros(3) for _ in mesh.v]
    for s, tris in enumerate(mesh.sub):
        if sub_indices is not None and s not in sub_indices:
            continue
        for a, b, c in tris:
            f = np.cross(mesh.v[b] - mesh.v[a], mesh.v[c] - mesh.v[a])
            for i in (a, b, c):
                acc[i] = acc[i] + f  # toward the front face (see Mesh.tri)
    for i, n in enumerate(acc):
        if np.linalg.norm(n) > 1e-12:
            mesh.n[i] = n / np.linalg.norm(n)


def split_wood(name, radius, angle_deg, length, seed):
    """A split of a log: the pith on the z axis at the wedge's apex."""
    rng = np.random.default_rng(seed)
    m = Mesh(name, ["StartingFuelSplit", "StartingFuelBark", "StartingFuelEnd"])
    half = math.radians(angle_deg) / 2
    slices = 12
    zs = np.linspace(-length / 2, length / 2, slices + 1)
    face_steps, arc_steps = 7, 12
    apex = rng.uniform(0.006, 0.012)
    tilt = rng.uniform(-0.05, 0.05, size=(2, 2))
    phase = rng.uniform(0, 10, size=8)

    def jag(r, z, k):
        # A split runs along the fibre and wanders: broad waves, a torn step, fine tearing.
        return (0.006 * math.sin(r * 45 + z * 7 + phase[k]) + 0.0035 * math.sin(r * 140 + z * 13 + phase[k + 1])
                + 0.0015 * math.sin(r * 420 + z * 37 + phase[k]) + (0.004 if r > radius * 0.55 else 0.0) * math.sin(z * 5 + phase[k + 1]))

    def outer(phi, z):
        return radius * (1 + 0.035 * math.sin(phi * 3 + phase[4]) + 0.02 * math.sin(z * 11 + phase[5]))

    def drift(z, k):
        return math.radians(4) * math.sin(z * 6 + phase[k])

    def section(z):
        """The section's outline at z: (point, region, s) from face A through the bark to face B."""
        pts = []
        aa = -half + drift(z, 0)
        ab = half + drift(z, 2)
        for i in range(face_steps + 1):
            r = apex + (outer(aa, z) - apex) * i / face_steps
            t = np.array([-math.sin(aa), math.cos(aa)])
            nrm = np.array([math.cos(aa), math.sin(aa)])
            p = np.array([math.cos(aa), math.sin(aa)]) * 0 + t * r + nrm * jag(r, z, 0)
            pts.append((p, 0, r))
        for i in range(arc_steps + 1):
            phi = aa + (ab - aa) * i / arc_steps
            r = outer(phi, z)
            pts.append((np.array([-math.sin(phi), math.cos(phi)]) * r, 1, (phi - aa) * radius))
        for i in range(face_steps, -1, -1):
            r = apex + (outer(ab, z) - apex) * i / face_steps
            t = np.array([-math.sin(ab), math.cos(ab)])
            nrm = np.array([-math.cos(ab), -math.sin(ab)])
            pts.append((t * r + nrm * jag(r, z, 2), 2, r))
        return pts

    sections = [section(z) for z in zs]
    count = len(sections[0])

    def at(k, i):
        p, region, s = sections[k][i]
        z = zs[k]
        # Sawn ends a few degrees out of square.
        if k == 0:
            z += tilt[0] @ p
        if k == slices:
            z += tilt[1] @ p
        return np.array([p[0], p[1], z]), region, s

    # Faces: split A (region 0), bark (1), split B (2), as strips along z.
    for region, sub in ((0, 0), (1, 1), (2, 0)):
        idx = [i for i in range(count) if sections[0][i][1] == region]
        rows = []
        for k in range(slices + 1):
            row = []
            for i in idx:
                p, _, s = at(k, i)
                u = (p[2] + length / 2) / 0.4 + seed * 0.13
                v = s / (0.1 if region != 1 else 0.3)
                row.append(m.add(p, [0, 0, 0], [u, v]))
            rows.append(row)
        centre = np.array([0.0, radius * 0.6, 0.0])
        m.grid(sub, rows, lambda i, j, rows=rows: (m.v[rows[i][j]] - centre) * np.array([1, 1, 0]))
    # The broken apex: a narrow face along the pith.
    rows = []
    for k in range(slices + 1):
        a, _, _ = at(k, 0)
        b, _, _ = at(k, count - 1)
        u = (a[2] + length / 2) / 0.4
        rows.append([m.add(a, [0, 0, 0], [u, 0.0]), m.add(b, [0, 0, 0], [u, 0.03])])
    m.grid(0, rows, lambda i, j: np.array([0.0, -1.0, 0.0]))
    smooth_normals(m, {0, 1})
    # The sawn ends, hard-edged: rings about the pith at the texture centre.
    scale = 0.44 / (radius * 1.04)
    for k, sign in ((0, -1.0), (slices, 1.0)):
        ring = []
        for i in range(count):
            p, _, _ = at(k, i)
            ring.append(m.add(p, [0, 0, sign], [0.5 + p[0] * scale, 0.5 + p[1] * scale]))
        hub_p = sum(m.v[i] for i in ring) / len(ring)
        hub = m.add(hub_p, [0, 0, sign], [0.5 + hub_p[0] * scale, 0.5 + hub_p[1] * scale])
        for i in range(count):
            m.tri(2, hub, ring[i], ring[(i + 1) % count], np.array([0, 0, sign]))
    # Centre the piece on its bounds.
    v = np.array(m.v)
    centre = (v.min(axis=0) + v.max(axis=0)) / 2
    m.v = [p - centre for p in m.v]
    # Occlusion: the apex and split faces a shade darker than the bark.
    return m


def rag(name, pose, seed):
    rng = np.random.default_rng(seed)
    n = 26
    size = 0.32
    us = np.linspace(0, 1, n)
    grid = np.zeros((n, n, 3))
    for i, v in enumerate(us):
        for j, u in enumerate(us):
            grid[i, j] = [(u - 0.5) * size, 0.0, (v - 0.5) * size]
    # Frayed, torn border: pull the edge in irregularly.
    for i in range(n):
        for j in range(n):
            edge = min(i, j, n - 1 - i, n - 1 - j)
            if edge == 0:
                grid[i, j, [0, 2]] *= 1 - rng.uniform(0.0, 0.08)

    def fold(points, origin, direction, angle, lift):
        d = direction / np.linalg.norm(direction)
        side = np.array([-d[1], d[0]])
        rel = points[..., [0, 2]] - origin
        mask = rel @ side > 0
        axis = np.array([d[0], 0.0, d[1]])
        c, s = math.cos(angle), math.sin(angle)
        o3 = np.array([origin[0], 0.0, origin[1]])
        p = points - o3
        k = axis
        rot = p * c + np.cross(k, p) * s + k * (p @ k)[..., None] * (1 - c)
        rot[..., 1] += lift
        out = points.copy()
        out[mask] = (rot + o3)[mask]
        return out

    pts = grid.copy()
    folds = {0: 2, 1: 3, 2: 2}[pose]
    for f in range(folds):
        origin = rng.uniform(-0.05, 0.05, size=2)
        direction = rng.normal(size=2)
        pts = fold(pts, origin, direction, math.radians(rng.uniform(150, 172)), 0.004 * (f + 1))
    centre = pts.reshape(-1, 3).mean(axis=0)
    rel = pts - centre
    squeeze = {0: 0.85, 1: 0.42, 2: 0.7}[pose]
    rel[..., [0, 2]] *= squeeze
    if pose == 2:
        # Wadded along one axis and twisted.
        rel[..., 2] *= 0.6
        rel[..., 0] *= 1.35
        twist = rel[..., 0] * 9.0
        y, z = rel[..., 1].copy(), rel[..., 2].copy()
        rel[..., 1] = y * np.cos(twist) - z * np.sin(twist)
        rel[..., 2] = y * np.sin(twist) + z * np.cos(twist)
    # Wrinkles where the cloth was pushed together.
    xs, zs = rel[..., 0], rel[..., 2]
    wr = np.zeros(xs.shape)
    for _ in range(7):
        a = rng.uniform(0, math.pi)
        freq = rng.uniform(40, 90)
        wr += rng.uniform(0.004, 0.009) * np.sin((xs * math.cos(a) + zs * math.sin(a)) * freq + rng.uniform(0, 6))
    rel[..., 1] += wr * (1.6 - squeeze)
    if pose == 1:
        # Balled: the middle pushed up into a dome, the edges tucked under.
        r2 = (xs ** 2 + zs ** 2) / 0.0035
        rel[..., 1] += 0.045 * np.exp(-r2) - 0.012 * np.clip(r2 - 1.0, 0, 2)
    pts = rel
    pts[..., 1] -= pts[..., 1].min()
    # Settle onto what it lies on: the lowest layer pressed flat.
    pts[..., 1] = np.where(pts[..., 1] < 0.006, pts[..., 1] * 0.4, pts[..., 1])

    m = Mesh(name, ["StartingFuelCloth"])
    front = [[m.add(pts[i, j], [0, 1, 0], [us[j] * 1.4, us[i] * 1.4]) for j in range(n)] for i in range(n)]
    m.grid(0, front, lambda i, j: np.array([0.0, 1.0, 0.0]))
    # Consistent winding first, then normals from it.
    m.sub[0] = []
    for i in range(n - 1):
        for j in range(n - 1):
            a, b, c, d = front[i][j], front[i][j + 1], front[i + 1][j + 1], front[i + 1][j]
            m.sub[0] += [(a, c, b), (a, d, c)]
    smooth_normals(m)
    # Make the front's normals face up on the whole: flip the winding if not.
    if np.mean([nn[1] for nn in m.n]) < 0:
        m.sub[0] = [(a, c, b) for a, b, c in m.sub[0]]
        smooth_normals(m)
    # The back: its own vertices, opposite normals and winding, a hair below,
    # a little darker (it is the side lying in shade).
    count = len(m.v)
    for i in range(count):
        m.add(m.v[i] - m.n[i] * 0.0008, -m.n[i], m.uv[i], 0.82)
    m.sub[0] += [(a + count, c + count, b + count) for a, b, c in m.sub[0][:]]
    # Occlusion by height: folds and the underside darker.
    ys = np.array([p[1] for p in m.v])
    span = max(ys.max(), 1e-3)
    m.c = [c * (0.6 + 0.4 * min(1.0, p[1] / span + 0.2)) for c, p in zip(m.c, m.v)]
    v = np.array(m.v)
    centre = (v.min(axis=0) + v.max(axis=0)) / 2
    m.v = [p - centre for p in m.v]
    return m


def box(m, sub, centre, size, uv_scale, uv_offset, rotation=None, shade=1.0, along=0):
    """A box with UVs along axis 'along' for each of its faces."""
    c = np.asarray(centre, float)
    h = np.asarray(size, float) / 2
    rot = rotation if rotation is not None else np.eye(3)
    for axis in range(3):
        for sign in (-1, 1):
            n = np.zeros(3); n[axis] = sign
            a1, a2 = [x for x in range(3) if x != axis]
            corners = []
            for s1, s2 in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                p = np.zeros(3)
                p[axis] = sign * h[axis]; p[a1] = s1 * h[a1]; p[a2] = s2 * h[a2]
                # U along the board's long axis.
                u_axis = along if along != axis else a1
                v_axis = [x for x in range(3) if x not in (axis, u_axis)][0]
                uv = [(p[u_axis] + h[u_axis]) * uv_scale + uv_offset[0], (p[v_axis] + h[v_axis]) * uv_scale * 1.0 + uv_offset[1]]
                corners.append(m.add(rot @ p + c, rot @ n, uv, shade))
            m.tri(sub, corners[0], corners[1], corners[2], rot @ n)
            m.tri(sub, corners[0], corners[2], corners[3], rot @ n)


def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def crate(seed):
    rng = np.random.default_rng(seed)
    m = Mesh("crate", ["StartingFuelCrate", "StartingFuelStrap"])
    W, H, D = 0.50, 0.30, 0.38
    t = 0.016
    y0 = -H / 2

    def board(centre, size, along, shade=1.0):
        jitter = rot_z(rng.uniform(-0.01, 0.01)) @ rot_y(rng.uniform(-0.008, 0.008))
        box(m, 0, centre, size, 2.0, (rng.uniform(0, 1), rng.uniform(0, 1)), jitter, shade, along)

    # Floor: boards along x.
    for i in range(4):
        z = -D / 2 + t + (i + 0.5) * (D - 2 * t) / 4
        board((0, y0 + t / 2, z), (W - 2 * t, t, (D - 2 * t) / 4 - 0.006), 0, 0.55)
    # Long sides: three gapped boards along x.
    bh, gap = 0.085, 0.012
    for side in (-1, 1):
        for row in range(3):
            y = y0 + t + 0.005 + bh / 2 + row * (bh + gap)
            board((0, y, side * (D / 2 - t / 2)), (W, bh, t), 0, 0.75 + 0.1 * row)
    # Ends: boards along z; the top board split for a hand-hold.
    for side in (-1, 1):
        x = side * (W / 2 - t / 2)
        for row in range(3):
            y = y0 + t + 0.005 + bh / 2 + row * (bh + gap)
            if row < 2:
                board((x, y, 0), (t, bh, D - 2 * t), 2, 0.75 + 0.1 * row)
            else:
                part = (D - 2 * t - 0.12) / 2
                for s in (-1, 1):
                    board((x, y, s * (0.06 + part / 2)), (t, bh, part), 2, 0.95)
                board((x, y - bh / 2 + 0.012, 0), (t, 0.024, 0.12), 2, 0.9)
    # Corner posts inside.
    for sx in (-1, 1):
        for sz in (-1, 1):
            board((sx * (W / 2 - t - 0.016), 0.0, sz * (D / 2 - t - 0.016)), (0.032, H - 0.01, 0.032), 1, 0.6)
    # Iron corner angles, bottom and top.
    for sx in (-1, 1):
        for sz in (-1, 1):
            for y in (y0 + 0.03, -y0 - 0.03):
                box(m, 1, (sx * (W / 2 + 0.001), y, sz * (D / 2 - 0.035)), (0.003, 0.05, 0.07), 3.0, (0, 0), None, 0.9)
                box(m, 1, (sx * (W / 2 - 0.035), y, sz * (D / 2 + 0.001)), (0.07, 0.05, 0.003), 3.0, (0, 0), None, 0.9)
    # Slots: three layers of four splits along x, the top layer last in the list.
    slots = []
    for layer in range(3):
        for j in range(4):
            pos = [rng.uniform(-0.03, 0.03), y0 + t + 0.045 + layer * 0.07, (j - 1.5) * 0.082]
            r = rot_y(math.pi / 2 + rng.uniform(-0.08, 0.08)) @ rot_z(math.pi * (j + layer) % 2 + rng.uniform(-0.25, 0.25))
            slots.append((pos, r))
    return m, slots


def tin_bin(seed):
    rng = np.random.default_rng(seed)
    m = Mesh("rag tin", ["StartingFuelTin"])
    W, H, D = 0.40, 0.27, 0.28
    flare = 0.012
    corner = 0.025
    rings = 10
    per_side = 6

    def outline(scale_w, scale_d):
        """A rounded rectangle, counter-clockwise from the front's left, with U along it."""
        w, d = W / 2 * scale_w, D / 2 * scale_d
        pts = []
        # Straight runs and corner arcs.
        segs = [((-w + corner, -d), (w - corner, -d)), ((w, -d + corner), (w, d - corner)),
                ((w - corner, d), (-w + corner, d)), ((-w, d - corner), (-w, -d + corner))]
        centres = [(w - corner, -d + corner), (w - corner, d - corner), (-w + corner, d - corner), (-w + corner, -d + corner)]
        starts = [-math.pi / 2, 0, math.pi / 2, math.pi]
        for k in range(4):
            (ax, az), (bx, bz) = segs[k]
            for i in range(per_side):
                t = i / per_side
                pts.append(np.array([ax + (bx - ax) * t, az + (bz - az) * t]))
            cx, cz = centres[k]
            for i in range(3):
                a = starts[k] + (math.pi / 2) * i / 3
                pts.append(np.array([cx + corner * math.cos(a), cz + corner * math.sin(a)]))
        return pts

    base = outline(1.0, 1.0)
    count = len(base)
    lengths = [0.0]
    for i in range(1, count + 1):
        lengths.append(lengths[-1] + np.linalg.norm(base[i % count] - base[i - 1]))
    total = lengths[-1]
    dents = [(rng.uniform(0, count), rng.uniform(0.2, 0.8), rng.uniform(0.004, 0.009)) for _ in range(4)]

    def ring(level, inset):
        k = level / rings
        y = -H / 2 + H * k
        grow = 1 + flare * k / (W / 2)
        out = []
        for i in range(count + 1):
            p = base[i % count] * grow
            nrm = p / np.linalg.norm(p)
            dent = sum(dd * math.exp(-((i - di) ** 2) / 4 - ((k - dk) / 0.15) ** 2) for di, dk, dd in dents)
            q = p - nrm * (dent + inset)
            out.append((np.array([q[0], y, q[1]]), np.array([nrm[0], 0.0, nrm[1]]), lengths[i] / total, k))
        return out

    # Outside wall.
    rows = []
    for level in range(rings + 1):
        rows.append([m.add(p, n, [u, 0.04 + 0.92 * k], 1.0) for p, n, u, k in ring(level, 0.0)])
    m.grid(0, rows)
    # Inside wall, darker.
    rows = []
    for level in range(rings + 1):
        rows.append([m.add(p, -n, [u, 0.04 + 0.92 * k], 0.45) for p, n, u, k in ring(level, 0.0015)])
    m.grid(0, rows, lambda i, j, rows=rows: m.n[rows[i][j]])
    # Bottom, outside and in.
    for y, nrm, shade in ((-H / 2, [0, -1, 0], 0.6), (-H / 2 + 0.0015, [0, 1, 0], 0.4)):
        hub = m.add([0, y, 0], nrm, [0.5, 0.02], shade)
        ringv = [m.add([p[0], y, p[1]], nrm, [0.5 + p[0], 0.02 + p[1] * 0.1], shade) for p in base]
        for i in range(count):
            m.tri(0, hub, ringv[i], ringv[(i + 1) % count], np.array(nrm, float))
    # The rolled rim: a tube over the top edge.
    top = ring(rings, 0.0)
    tube = 0.006
    segs = 6
    rows = []
    for s in range(segs + 1):
        a = math.pi * 2 * s / segs
        row = []
        for p, n, u, _ in top:
            c = p + n * tube * 0.3 + np.array([0, tube * 0.6, 0])
            off = n * math.cos(a) * tube + np.array([0, math.sin(a) * tube, 0])
            row.append(m.add(c + off, off, [u, 0.99], 0.9))
        rows.append(row)
    m.grid(0, rows, lambda i, j, rows=rows: m.n[rows[i][j]])
    # Wire handles at each end.
    for side in (-1, 1):
        x = side * (W / 2 + flare * 0.6 + 0.004)
        for (y, z, sy, sz) in ((0.06, -0.06, 0.008, 0.008), (0.06, 0.06, 0.008, 0.008)):
            box(m, 0, (x + side * 0.012, y, z), (0.03, sy, sz), 1.0, (0.97, 0.97), None, 0.6, 0)
        box(m, 0, (x + side * 0.026, 0.06, 0.0), (0.008, 0.008, 0.13), 1.0, (0.97, 0.97), None, 0.6, 2)
    slots = []
    for layer in range(3):
        for j in range(4):
            pos = [(j % 2 - 0.5) * 0.17 + rng.uniform(-0.02, 0.02), -H / 2 + 0.035 + layer * 0.05, (j // 2 - 0.5) * 0.12 + rng.uniform(-0.02, 0.02)]
            r = rot_y(rng.uniform(0, math.pi * 2)) @ rot_x(rng.uniform(-0.15, 0.15))
            slots.append((pos, r))
    return m, slots


def quaternion(r):
    """A rotation matrix to Unity's quaternion (x, y, z, w)."""
    t = np.trace(r)
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        w, x, y, z = 0.25 * s, (r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s, (r[1, 0] - r[0, 1]) / s
    elif r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = math.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2
        w, x, y, z = (r[2, 1] - r[1, 2]) / s, 0.25 * s, (r[0, 1] + r[1, 0]) / s, (r[0, 2] + r[2, 0]) / s
    elif r[1, 1] > r[2, 2]:
        s = math.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2
        w, x, y, z = (r[0, 2] - r[2, 0]) / s, (r[0, 1] + r[1, 0]) / s, 0.25 * s, (r[1, 2] + r[2, 1]) / s
    else:
        s = math.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2
        w, x, y, z = (r[1, 0] - r[0, 1]) / s, (r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s, 0.25 * s
    return [round(x, 5), round(y, 5), round(z, 5), round(w, 5)]


# ---------------------------------------------------------------- rendering (icons, previews)

class Scene:
    """A tiny software rasteriser: textured, normal-free Lambert shading
    with a key, fill, and rim light, enough to draw icons and previews."""

    def __init__(self, textures):
        self.textures = textures
        self.items = []

    def add(self, mesh, matrix=np.eye(3), offset=np.zeros(3), state=None):
        self.items.append((mesh, matrix, np.asarray(offset, float), state or {}))

    def render(self, size, eye, target, fov=30.0, background=None, ortho=None):
        eye, target = np.asarray(eye, float), np.asarray(target, float)
        f = target - eye; f /= np.linalg.norm(f)
        r = np.cross(np.array([0, 1.0, 0]), f); r /= np.linalg.norm(r)
        u = np.cross(f, r)
        depth = np.full((size, size), np.inf)
        color = np.zeros((size, size, 3))
        alpha = np.zeros((size, size))
        light = np.array([-0.5, 0.8, -0.35]); light /= np.linalg.norm(light)
        fill = np.array([0.6, 0.3, 0.5]); fill /= np.linalg.norm(fill)
        focal = 1 / math.tan(math.radians(fov) / 2)
        for mesh, matrix, offset, state in self.items:
            v = np.array(mesh.v) @ matrix.T + offset
            n = np.array(mesh.n) @ matrix.T
            rel = v - eye
            cx, cy, cz = rel @ r, rel @ u, rel @ f
            if ortho:
                sx = (cx / ortho * 0.5 + 0.5) * size
                sy = (0.5 - cy / ortho * 0.5) * size
            else:
                sx = (cx / cz * focal * 0.5 + 0.5) * size
                sy = (0.5 - cy / cz * focal * 0.5) * size
            uv = np.array(mesh.uv)
            col = np.array(mesh.c)
            for s, tris in enumerate(mesh.sub):
                tex = self.textures[mesh.materials[s]]
                th, tw = tex.shape[:2]
                for a, b, c in tris:
                    xs = np.array([sx[a], sx[b], sx[c]]); ys = np.array([sy[a], sy[b], sy[c]])
                    x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, size - 1)
                    y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, size - 1)
                    if x0 > x1 or y0 > y1:
                        continue
                    area = (xs[1] - xs[0]) * (ys[2] - ys[0]) - (xs[2] - xs[0]) * (ys[1] - ys[0])
                    if abs(area) < 1e-9:
                        continue
                    gy, gx = np.mgrid[y0:y1 + 1, x0:x1 + 1] + 0.5
                    w0 = ((xs[1] - gx) * (ys[2] - gy) - (xs[2] - gx) * (ys[1] - gy)) / area
                    w1 = ((xs[2] - gx) * (ys[0] - gy) - (xs[0] - gx) * (ys[2] - gy)) / area
                    w2 = 1 - w0 - w1
                    inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
                    if not inside.any():
                        continue
                    z = w0 * cz[a] + w1 * cz[b] + w2 * cz[c]
                    region = depth[y0:y1 + 1, x0:x1 + 1]
                    write = inside & (z < region)
                    if not write.any():
                        continue
                    tu = w0 * uv[a, 0] + w1 * uv[b, 0] + w2 * uv[c, 0]
                    tv = w0 * uv[a, 1] + w1 * uv[b, 1] + w2 * uv[c, 1]
                    texel = tex[((1 - tv % 1.0) * th).astype(int) % th, ((tu % 1.0) * tw).astype(int) % tw, :3]
                    nn = (w0[..., None] * n[a] + w1[..., None] * n[b] + w2[..., None] * n[c])
                    nn /= np.linalg.norm(nn, axis=-1, keepdims=True) + 1e-9
                    view = -f
                    facing = (nn @ view)
                    nn = np.where(facing[..., None] < 0, -nn, nn)
                    lam = np.clip(nn @ light, 0, 1) * 1.0 + np.clip(nn @ fill, 0, 1) * 0.35 + 0.22
                    rim = np.clip(1 - np.abs(nn @ view), 0, 1) ** 3 * 0.25
                    shade = (w0 * col[a] + w1 * col[b] + w2 * col[c])[..., None]
                    rgb = texel * shade * lam[..., None] + rim[..., None]
                    rgb = apply_state(rgb, state, gx, gy)
                    region[write] = z[write]
                    color[y0:y1 + 1, x0:x1 + 1][write] = rgb[write]
                    alpha[y0:y1 + 1, x0:x1 + 1][write] = 1.0
        if background is not None:
            color = color * alpha[..., None] + np.asarray(background) * (1 - alpha[..., None])
        return color, alpha


def apply_state(rgb, state, gx, gy):
    """The shader's look, roughly, for previews: char toward black, glow, oil."""
    char = state.get("char", 0.0)
    if char > 0:
        pattern = 0.5 + 0.5 * np.sin(gx * 0.9) * np.sin(gy * 0.7)
        covered = np.clip((char * 1.3 - pattern * 0.6), 0, 1)[..., None]
        rgb = rgb * (1 - covered) + np.array([0.035, 0.03, 0.028]) * covered
        glow = state.get("glow", 0.0)
        if glow > 0:
            crack = (np.abs(np.sin(gx * 0.5 + gy * 0.31)) < 0.15)[..., None]
            rgb = rgb + crack * covered * np.array([1.0, 0.35, 0.05]) * glow
    oil = state.get("oil", 0.0)
    if oil > 0:
        rgb = rgb * (1 - 0.55 * oil)
    return rgb


def srgb(img):
    return (np.clip(img, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8)


def icon(scene, eye, target, fov, scale=3, size=128):
    big = size * scale
    color, alpha = scene.render(big, eye, target, fov)
    # The game's icons are dark and shaded: keep the form, mute the colour.
    grey = color.mean(axis=-1, keepdims=True)
    tone = np.clip(0.45 * grey + 0.12 * color, 0, 1) * 0.55
    rgba = np.concatenate([tone, alpha[..., None]], axis=-1).reshape(size, scale, size, scale, 4).mean(axis=(1, 3))
    rgba[..., :3] = np.where(rgba[..., 3:4] > 0, rgba[..., :3] / np.maximum(rgba[..., 3:4], 1e-6), 0)
    mask = alpha > 0.5
    grown = mask.copy()
    reach = int(3.5 * scale)
    for dy in range(-reach, reach + 1, 2):
        for dx in range(-reach, reach + 1, 2):
            if dx * dx + dy * dy <= reach * reach:
                grown |= np.roll(np.roll(mask, dy, axis=0), dx, axis=1)
    outline = (grown & ~mask).astype(float).reshape(size, scale, size, scale).mean(axis=(1, 3))
    blue = np.array([131.0, 209.0, 236.0]) / 255.0
    dropped = np.concatenate([np.broadcast_to(blue, (size, size, 3)), (0.6 * outline)[..., None]], axis=-1)

    def to8(a):
        out = a.copy()
        out[..., :3] = np.clip(out[..., :3], 0, 1) ** (1 / 2.2)
        return (np.clip(out, 0, 1) * 255).astype(np.uint8)
    return to8(rgba), to8(dropped)


def fit_view(meshes_and_offsets, direction, fov):
    pts = np.concatenate([np.array(m.v) @ mat.T + off for m, mat, off in meshes_and_offsets])
    centre = (pts.min(axis=0) + pts.max(axis=0)) / 2
    radius = np.linalg.norm(pts - centre, axis=1).max()
    d = np.asarray(direction, float); d /= np.linalg.norm(d)
    distance = radius / math.sin(math.radians(fov) / 2) * 1.02
    return centre + d * distance, centre


# ---------------------------------------------------------------- main

def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("textures", type=pathlib.Path)
    parser.add_argument("models", type=pathlib.Path)
    parser.add_argument("--preview", type=pathlib.Path)
    args = parser.parse_args()
    args.textures.mkdir(parents=True, exist_ok=True)
    args.models.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(RNG_SEED)

    made = {
        "sf_split": split_face(rng), "sf_bark": bark(rng), "sf_end": end_grain(rng), "sf_cloth": cloth(rng),
        "sf_crate": crate_boards(rng), "sf_strap": iron_strap(rng), "sf_tin": tin(rng),
    }
    for name, (albedo, normal) in made.items():
        Image.fromarray(albedo, "RGBA").save(args.textures / f"{name}.png")
        Image.fromarray(normal, "RGBA").save(args.textures / f"{name}_n.png")
        print(f"wrote {args.textures / name}.png and _n.png")
    linear = {k: (v[0][..., :3].astype(float) / 255.0) ** 2.2 for k, v in made.items()}
    textures = {"StartingFuelSplit": linear["sf_split"], "StartingFuelBark": linear["sf_bark"], "StartingFuelEnd": linear["sf_end"],
                "StartingFuelCloth": linear["sf_cloth"], "StartingFuelCrate": linear["sf_crate"], "StartingFuelStrap": linear["sf_strap"],
                "StartingFuelTin": linear["sf_tin"]}

    woods = [split_wood("wood 0", 0.068, 90, 0.33, 11), split_wood("wood 1", 0.075, 100, 0.31, 12), split_wood("wood 2", 0.064, 76, 0.35, 13)]
    rags = [rag("rag 0", 0, 21), rag("rag 1", 1, 22), rag("rag 2", 2, 23)]
    crate_mesh, crate_slots = crate(31)
    tin_mesh, tin_slots = tin_bin(32)
    meshes = woods + rags + [crate_mesh, tin_mesh]
    model = {
        "meshes": [m.json() for m in meshes],
        "slots": [{"body": body, "position": [round(x, 4) for x in p], "rotation": quaternion(r)}
                  for body, slots in (("crate", crate_slots), ("rag tin", tin_slots)) for p, r in slots],
    }
    (args.models / "starting_fuel.json").write_text(json.dumps(model, separators=(",", ":")))
    print(f"wrote {args.models / 'starting_fuel.json'}: " + ", ".join(f"{m.name} {len(m.v)} v {sum(len(s) for s in m.sub)} t" for m in meshes))

    def contents(scene, slots, kind, count, scale=1.0):
        for i, (p, r) in enumerate(slots[:count]):
            piece = (woods if kind == "wood" else rags)[i * 7 % 3]
            scene.add(piece, r * scale if False else r, np.asarray(p))

    # Icons.
    icons = {}
    s = Scene(textures); s.add(woods[0], rot_y(0.5) @ rot_x(-0.2)); s.add(woods[1], rot_y(0.2) @ rot_x(-0.2) @ rot_z(math.pi), np.array([0.0, 0.05, 0.02]))
    eye, target = fit_view([(woods[0], rot_y(0.5) @ rot_x(-0.2), np.zeros(3)), (woods[1], rot_y(0.2), np.array([0.0, 0.05, 0.02]))], [-0.6, 0.7, -0.8], 30)
    icons["starting_wood_icon"] = icon(s, eye, target, 30)
    s = Scene(textures); s.add(rags[1])
    eye, target = fit_view([(rags[1], np.eye(3), np.zeros(3))], [-0.5, 0.9, -0.7], 30)
    icons["starting_rag_icon"] = icon(s, eye, target, 30)
    s = Scene(textures); s.add(crate_mesh); contents(s, crate_slots, "wood", 12)
    eye, target = fit_view([(crate_mesh, np.eye(3), np.zeros(3))], [-0.8, 0.75, -1.0], 30)
    icons["starting_crate_icon"] = icon(s, eye, target, 30)
    s = Scene(textures); s.add(tin_mesh); contents(s, tin_slots, "rag", 12)
    eye, target = fit_view([(tin_mesh, np.eye(3), np.zeros(3))], [-0.8, 0.75, -1.0], 30)
    icons["starting_tin_icon"] = icon(s, eye, target, 30)
    for name, (normal, dropped) in icons.items():
        Image.fromarray(normal, "RGBA").save(args.textures / f"{name}.png")
        Image.fromarray(dropped, "RGBA").save(args.textures / f"{name}_dropped.png")
        print(f"wrote {args.textures / name}.png and _dropped.png")

    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
        bg = np.array([0.22, 0.23, 0.25])

        def shot(name, scene, items, direction, size=640, fov=32):
            eye, target = fit_view(items, direction, fov)
            img, _ = scene.render(size, eye, target, fov, background=bg)
            Image.fromarray(srgb(img), "RGB").save(args.preview / f"{name}.png")
            print(f"wrote {args.preview / name}.png")

        for i, w in enumerate(woods):
            for view, d in (("side", [-1, 0.35, -0.25]), ("end", [-0.3, 0.4, -1]), ("above", [-0.2, 1, -0.3])):
                s = Scene(textures); s.add(w); shot(f"wood{i}-{view}", s, [(w, np.eye(3), np.zeros(3))], d)
        for i, rgm in enumerate(rags):
            for view, d in (("above", [-0.3, 1, -0.4]), ("side", [-1, 0.25, -0.3])):
                s = Scene(textures); s.add(rgm); shot(f"rag{i}-{view}", s, [(rgm, np.eye(3), np.zeros(3))], d)
        for count in (12, 6, 0):
            for view, d in (("above", [-0.35, 1, -0.5]), ("side", [-1, 0.3, -0.6])):
                s = Scene(textures); s.add(crate_mesh); contents(s, crate_slots, "wood", count)
                shot(f"crate{count}-{view}", s, [(crate_mesh, np.eye(3), np.zeros(3))], d)
                s = Scene(textures); s.add(tin_mesh); contents(s, tin_slots, "rag", count)
                shot(f"tin{count}-{view}", s, [(tin_mesh, np.eye(3), np.zeros(3))], d)
        # Burn stages, roughly as the shader draws them.
        for stage, state in (("fresh", {}), ("scorched", {"char": 0.25}), ("burning", {"char": 0.7, "glow": 0.5}), ("embers", {"char": 1.0, "glow": 1.0})):
            s = Scene(textures)
            items = []
            for k, w in enumerate(woods):
                off = np.array([0.0, 0.06 * (k // 2), (k - 1) * 0.1])
                mat = rot_y(0.2 * k)
                s.add(w, mat, off, state)
                items.append((w, mat, off))
            shot(f"stage-{stage}", s, items, [-0.6, 0.8, -0.7])
        sheet_names = ["wood0-side", "wood1-end", "wood2-above", "rag0-above", "rag1-side", "rag2-above", "crate12-above", "crate6-side", "crate0-above", "tin12-above", "tin6-side", "tin0-above"]
        tiles = [Image.open(args.preview / f"{n}.png").resize((320, 320)) for n in sheet_names]
        sheet = Image.new("RGB", (320 * 6, 640))
        for i, tile in enumerate(tiles):
            sheet.paste(tile, (320 * (i % 6), 320 * (i // 6)))
        sheet.save(args.preview / "sheet.png")
        print(f"wrote {args.preview / 'sheet.png'}")


if __name__ == "__main__":
    main()
