# StageZero — App Icon Assets

The **Ground Up** mark in StageZero's accent (`#34d399`) on a dark, softly glowing
tile: a cloud lifting off a launch platform, with the dashed ascent line the rest
of the CodeLifter icon family uses for motion.

## Layout

```
svg/                        Vector masters (edit these; rasters derive from them)
  stagezero-icon-dark.svg     Hero app tile: dark gradient + glow + colored mark
  stagezero-icon-white.svg    Tile on white (deep mark) for light contexts
  stagezero-mark-color.svg    Bare mark, accent, transparent
  stagezero-mark-white.svg    Bare mark, white knockout (dark surfaces / tinting)
  stagezero-mark-black.svg    Bare mark, near-black (mono / light surfaces)
```

Platform rasters (`ios/`, `macos/`, `android/`, `windows/`, `web/`) are not
generated yet. They derive from `svg/stagezero-icon-dark.svg` the same way the
other CodeLifter apps' sets do — see `Dockerizit/Assets/README.md` for the
target layout.

## Geometry

Shared across every CodeLifter app tile, so the set lines up at any size:

- 1024x1024, corner radius `229` (22.4%) — matches `rounded-[14px]` at 64px.
- Background: radial gradient, `#0e2b22` -> `#09090f`, centered at 32% / 22%.
- Glow: radial `#34d399` at 30% opacity, centered, radius 42%.
- Mark: drawn in a 0-100 space, placed with `translate(215.04, 215.04) scale(5.9392)`.

## Consumers

`CodeLifter.Net` serves `stagezero-icon-dark.svg` as `public/icons/apps/stagezero.svg`
on the homepage product card. Re-copy it there after editing the master.
