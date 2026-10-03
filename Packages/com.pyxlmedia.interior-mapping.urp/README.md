# Interior Mapping (URP)

A single-file URP shader that fakes building interiors behind flat facades. The rooms are looked up from a cubemap, so they show parallax without any extra geometry.

Requires Unity 6000.5 and URP 17.5.

## Install

In Package Manager, choose **+ → Install package from git URL…** and enter:

```
https://github.com/magicink/urp-interior-mapping-shader.git?path=/Packages/com.pyxlmedia.interior-mapping.urp
```

You can also add it to `Packages/manifest.json` directly. Append `#<tag>` to pin a release:

```json
"com.pyxlmedia.interior-mapping.urp": "https://github.com/magicink/urp-interior-mapping-shader.git?path=/Packages/com.pyxlmedia.interior-mapping.urp#v0.1.0"
```

## Use

1. Create a material that uses the **InteriorMapping/SingleFile** shader and assign it to your facade.
2. Select the material and run **Tools → Interior Mapping → Bake Interior Cubemap**. If the material already has a `.cubemap` asset, the baker re-bakes it in place. Otherwise it asks where to save a new one and assigns the result to the material.

## Samples

**Facade Demo** is a ready-made facade with an orbiting camera and a day/night cycle. To import it, select this package in Package Manager and open the **Samples** tab.
