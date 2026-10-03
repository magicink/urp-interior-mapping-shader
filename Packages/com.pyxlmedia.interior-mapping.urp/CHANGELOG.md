# Changelog

## [Unreleased]

- Renamed the package to `com.pyxlmedia.interior-mapping.urp` so its name states the render pipeline. If you installed it from git, update the key and `?path=` in `Packages/manifest.json` to the new name.
- Rooms have their own lamps. `Lit Room Fraction` sets how many are on, each at a random intensity between `Light Intensity Min` and `Max` and a random colour between `Warm Light Color` and `Cool Light Color` (`Warm Light Bias` skews the mix). Every room also picks up daylight from the ambient probe, so unlit rooms go dark at night.
- Lamps dim as the main light brightens (`Daylight Dimming`), so rooms bright enough to bloom at night don't glow at noon.
- Blinds are lit by the sun from outside and by their room's lamp from behind, instead of showing a flat colour.
- The cubemap baker now lights the room white, leaving the tint to the lamp colours. Re-bake existing cubemaps, or lit rooms will come out warmer than their lamp colour.
- The facade casts shadows and receives main-light shadows. Shadows dim only the sunlit brick and the sun glint, so lit interiors and sky reflections are unaffected.
- The facade receives SSAO.
- Added `DepthOnly` and `DepthNormals` passes, so the building now shows up in the camera depth texture and in SSAO.
- Facade Demo: the sun's arc can lean away from overhead (`Arc Tilt` on `DayNightCycle`, default -10°), so shadows swing across the facade during the morning.
- Facade Demo: `StructureScatter` rings the building with plain blocks in a new layout each Play, so shadows reach it from every side through the day.

## [0.1.0]

- First packaged release: the `InteriorMapping/SingleFile` shader, the interior cubemap baker, and the Facade Demo sample.
