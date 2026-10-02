# URP Interior Mapping

This is the development project for the `com.pyxlmedia.interior-mapping` package. For installation and usage, see the [package README](Packages/com.pyxlmedia.interior-mapping/README.md).

## Layout

- `Packages/com.pyxlmedia.interior-mapping/` is the package, embedded here so you can edit it in place.
- `Assets/Samples/Facade Demo/` is the editable copy of the package's Facade Demo sample.
- `Assets/Settings/` holds this project's URP assets. They are not part of the package.

## Releasing

1. Run **Tools → Interior Mapping → Sync Facade Demo Into Package**. Unity can't open anything in `Samples~`, so the sample is edited in `Assets` and copied into the package by this step.
2. Bump `version` in `package.json` and add an entry to `CHANGELOG.md`.
3. Commit, then tag the commit `v<version>`.
