# Changelog

## [Unreleased]

- Renamed the package to `com.pyxlmedia.interior-mapping.urp` so its name states the render pipeline. If you installed it from git, update the key and `?path=` in `Packages/manifest.json` to the new name.
- The facade casts shadows and receives main-light shadows. Shadows dim only the sunlit brick and the sun glint, so lit interiors and sky reflections are unaffected.
- The facade receives SSAO.
- Added `DepthOnly` and `DepthNormals` passes, so the building now shows up in the camera depth texture and in SSAO.
- Facade Demo: the sun's arc can lean away from overhead (`Arc Tilt` on `DayNightCycle`, default -10°), so shadows swing across the facade during the morning.
- Facade Demo: `StructureScatter` rings the building with plain blocks in a new layout each Play, so shadows reach it from every side through the day.

## [0.1.0]

- First packaged release: the `InteriorMapping/SingleFile` shader, the interior cubemap baker, and the Facade Demo sample.
