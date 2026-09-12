Shader "InteriorMapping/SingleFile"
{
    Properties
    {
        [Header(Interior)]
        _InteriorCubemap("Interior Cubemap", Cube) = "" {}
        [MainColor] _BaseColor("Interior Tint", Color) = (1, 1, 1, 1)

        [Header(Blinds)]
        _BlindColor("Blind Color", Color) = (0.88, 0.86, 0.8, 1)
        _BlindDepth("Blind Depth", Range(0, 0.5)) = 0.12
        _BlindCoverage("Blind Coverage", Range(0, 1)) = 0.5
        [IntRange] _SlatCount("Slat Count", Range(0, 40)) = 14

        [Header(Facade)]
        [MainTexture] _BaseMap("Facade Map", 2D) = "white" {}
        _FacadeColor("Facade Color", Color) = (0.48, 0.27, 0.22, 1)
        _WindowsPerFace("Windows Per Face (X, Y)", Vector) = (3, 2, 0, 0)
        _WindowFrameWidth("Window Frame Width", Range(0, 0.49)) = 0.16
        _ArchHeight("Arch Height", Range(0, 1)) = 1
        _FrameColor("Frame Color", Color) = (0.86, 0.85, 0.82, 1)
        _FrameThickness("Frame Thickness", Range(0, 0.25)) = 0.06
        _RevealShading("Reveal Shading", Range(0, 1)) = 0.55

        [Header(Ground Floor)]
        [IntRange] _GroundFloorCount("Ground Floor Count", Range(0, 8)) = 1
        _GroundFloorFrameWidth("Ground Floor Frame Width", Range(0, 0.49)) = 0.06
        _GroundFloorArchHeight("Ground Floor Arch Height", Range(0, 1)) = 0
        _GroundFloorColor("Ground Floor Color", Color) = (0.62, 0.6, 0.56, 1)

        [Header(Glazing Bars)]
        _MuntinsPerPane("Muntins Per Pane (X, Y)", Vector) = (2, 2, 0, 0)
        _MuntinWidth("Muntin Width", Range(0, 0.05)) = 0.008
        _MullionWidth("Mullion Width", Range(0, 0.08)) = 0.016
        _CheckRailHeight("Check Rail Height", Range(0, 1)) = 0.5
        _CheckRailWidth("Check Rail Width", Range(0, 0.08)) = 0.022

        [Header(Glass)]
        _GlassRecessDepth("Glass Recess Depth", Range(0, 0.3)) = 0.09
        _GlassReflectivity("Glass Reflectivity", Range(0, 1)) = 0.9
        _SunGlintStrength("Sun Glint Strength", Range(0, 8)) = 2
        _SunGlintSharpness("Sun Glint Sharpness", Range(1, 512)) = 220
        _GlassFresnelPower("Glass Fresnel Power", Range(1, 8)) = 5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Core.hlsl declares unity_SpecCube0 but not the decode for it.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            struct Attributes
            {
                float4 positionObjectSpace : POSITION;
                float3 normalObjectSpace : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionClipSpace : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionObjectSpace : TEXCOORD1;
                float3 normalObjectSpace : TEXCOORD2;
                float3 positionWorldSpace : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURECUBE(_InteriorCubemap);
            SAMPLER(sampler_InteriorCubemap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FacadeColor;
                half4 _FrameColor;
                half4 _GroundFloorColor;
                half4 _BlindColor;
                float4 _BaseMap_ST;
                float4 _WindowsPerFace;
                float4 _MuntinsPerPane;
                float _WindowFrameWidth;
                float _ArchHeight;
                float _FrameThickness;
                float _RevealShading;
                float _GroundFloorCount;
                float _GroundFloorFrameWidth;
                float _GroundFloorArchHeight;
                float _MuntinWidth;
                float _MullionWidth;
                float _CheckRailHeight;
                float _CheckRailWidth;
                float _BlindDepth;
                float _BlindCoverage;
                float _SlatCount;
                float _GlassRecessDepth;
                float _GlassReflectivity;
                float _SunGlintStrength;
                float _SunGlintSharpness;
                float _GlassFresnelPower;
            CBUFFER_END

            // Distance past the edge of a window opening, negative inside. The max(.y, 0) collapses
            // the vertical term below the springing, leaving straight jambs under a circular head.
            float DistancePastOpening(float2 fromCentre, float2 halfExtents, float archHeight)
            {
                // The radius grows as the arch flattens, which keeps the crown at the top of the
                // opening and turns a low setting into a shallow segmental head.
                float headRadius = halfExtents.x / max(archHeight, 0.05);
                float springHeight = halfExtents.y - headRadius;

                float boxDistance = max(abs(fromCentre.x) - halfExtents.x,
                                        abs(fromCentre.y) - halfExtents.y);
                float headDistance =
                    length(float2(fromCentre.x, max(fromCentre.y - springHeight, 0.0))) - headRadius;
                return max(boxDistance, headDistance);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWorldSpace = TransformObjectToWorld(input.positionObjectSpace.xyz);
                output.positionClipSpace = TransformWorldToHClip(output.positionWorldSpace);
                output.uv = input.uv;
                output.positionObjectSpace = input.positionObjectSpace.xyz;
                output.normalObjectSpace = input.normalObjectSpace;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // One room per window, so a single grid drives both and they cannot drift apart.
                // Depth reuses the horizontal count; whole numbers only or the last room is clipped.
                float3 roomsPerAxis = float3(_WindowsPerFace.x, _WindowsPerFace.y, _WindowsPerFace.x);
                roomsPerAxis = max(1.0, floor(roomsPerAxis));

                // Grid space: the box stretched so every room is a unit cube on the positive side
                // of the origin, which makes the room a fragment belongs to a plain floor().
                float3 positionInGrid = (input.positionObjectSpace + 0.5) * roomsPerAxis;
                float3 roomIndex = clamp(floor(positionInGrid), 0.0, roomsPerAxis - 1.0);

                // Re-centred on its own room, giving back the same +/-0.5 box the slab method wants.
                float3 positionInRoom = positionInGrid - roomIndex - 0.5;

                // Direction only, so which room the camera sits in never matters - but the grid
                // stretch does, or non-square room counts skew the ray.
                float3 cameraPositionObjectSpace = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 viewRayDirection =
                    normalize((input.positionObjectSpace - cameraPositionObjectSpace) * roomsPerAxis);

                // Every room shares one cubemap, so mirror it per room or the block reads as
                // wallpaper. Flipping position and ray together keeps the reflection consistent.
                float3 roomNoise = frac(sin(float3(dot(roomIndex, float3(12.9898, 78.233, 37.719)),
                                                   dot(roomIndex, float3(39.346, 11.135, 83.155)),
                                                   dot(roomIndex, float3(73.156, 52.235, 9.1513))))
                                        * 43758.5453);
                float2 mirrorSigns = step(0.5, roomNoise.xy) * 2.0 - 1.0;
                float3 mirrorAxes = float3(mirrorSigns.x, 1.0, mirrorSigns.y);

                // The facade is not mirrored, so its recess needs the ray as it really is.
                float3 facadeRayDirection = viewRayDirection;
                positionInRoom *= mirrorAxes;
                viewRayDirection *= mirrorAxes;

                // Slab method: per axis, the coordinate of the wall the ray heads toward and how
                // far along the ray it sits. The nearest of the three is where the ray exits.
                float3 facingWallCoordinates = sign(viewRayDirection) * 0.5;
                float3 wallDistances = (facingWallCoordinates - positionInRoom) / viewRayDirection;
                float nearestWallDistance = min(min(wallDistances.x, wallDistances.y), wallDistances.z);

                float3 wallHitPosition = positionInRoom + viewRayDirection * nearestWallDistance;

                // The hit position doubles as a direction from the room centre, which is what a
                // cubemap lookup wants. Explicit LOD 0 stops mip selection reading the direction
                // jump at each corner as fine detail and blurring the seam.
                half3 interiorColor = SAMPLE_TEXTURECUBE_LOD(
                    _InteriorCubemap, sampler_InteriorCubemap, wallHitPosition, 0).rgb;

                // Window cells come off the same grid as the rooms, so each window lands dead
                // centre in its own room whatever the mesh UVs happen to do.
                float isFacingX = step(0.5, abs(input.normalObjectSpace.x));
                float2 cellUv = frac(float2(lerp(positionInGrid.x, positionInGrid.z, isFacingX),
                                            positionInGrid.y));

                // The same swap as cellUv, leaving the ray as across-the-face, up, into-the-wall.
                float3 rayInFaceSpace = lerp(facadeRayDirection.xyz, facadeRayDirection.zyx, isFacingX);
                float2 parallaxPerDepth = rayInFaceSpace.xy / max(abs(rayInFaceSpace.z), 1e-4);
                float2 glassCellUv = cellUv + parallaxPerDepth * _GlassRecessDepth;

                // The bottom rows are shopfronts, not flats. roomIndex.y is already the floor
                // number, so the band is one compare that everything below reads off.
                float isGroundFloor = step(roomIndex.y, _GroundFloorCount - 1.0);
                float frameWidth = lerp(_WindowFrameWidth, _GroundFloorFrameWidth, isGroundFloor);
                float archHeight = lerp(_ArchHeight, _GroundFloorArchHeight, isGroundFloor);

                // A circular head in cell space would be an ellipse on the wall, so square the cell
                // up from the room counts before measuring anything.
                float archAspect = roomsPerAxis.y / lerp(roomsPerAxis.x, roomsPerAxis.z, isFacingX);
                float2 openingHalfExtents = (0.5 - frameWidth) * float2(archAspect, 1.0);
                float2 wallFromCentre = (cellUv - 0.5) * float2(archAspect, 1.0);
                float2 glassFromCentre = (glassCellUv - 0.5) * float2(archAspect, 1.0);

                // Glass sits behind the wall, so the ray has to clear the opening at both ends or
                // it struck the frame. No frac() on the far end - the overshoot is the occlusion.
                float intoFrame = DistancePastOpening(wallFromCentre, openingHalfExtents, archHeight);
                float intoFrameAtGlass =
                    DistancePastOpening(glassFromCentre, openingHalfExtents, archHeight);
                float isPane = step(max(intoFrame, intoFrameAtGlass), 0.0);

                // Bars sit on the glass, so they parallax with the interior instead of sliding
                // across it. Everything here is a distance, so thin beats thick at every crossing.
                float2 fromOpeningCorner = glassFromCentre + openingHalfExtents;
                float2 muntinSpacing = (openingHalfExtents * 2.0) / max(_MuntinsPerPane.xy, 1.0);
                float2 toMuntin = abs(fromOpeningCorner -
                                      round(fromOpeningCorner / muntinSpacing) * muntinSpacing);

                // The check rail is where two sashes overlap, so it is the thick horizontal one.
                float toCheckRail = abs(glassFromCentre.y -
                                        (_CheckRailHeight * 2.0 - 1.0) * openingHalfExtents.y);

                // A shopfront is plate glass between posts, so the sash bars drop out down there
                // and only the mullion survives. The 1.0 is a distance no min() will ever pick.
                float sashBarDistance = min(min(toMuntin.x, toMuntin.y) - _MuntinWidth,
                                            toCheckRail - _CheckRailWidth);
                float barDistance = min(lerp(sashBarDistance, 1.0, isGroundFloor),
                                        abs(glassFromCentre.x) - _MullionWidth);
                float isBar = isPane * step(barDistance, 0.0);

                // Roof and underside stay solid, or the building reads as a greenhouse.
                float isUpwardFace = step(0.5, abs(input.normalObjectSpace.y));
                float isWindow = isPane * (1.0 - isBar) * (1.0 - isUpwardFace);

                float3 normalWorld = normalize(TransformObjectToWorldNormal(input.normalObjectSpace));
                half3 viewDirectionWorld = GetWorldSpaceNormalizeViewDir(input.positionWorldSpace);
                half lambert = saturate(dot(normalWorld, _MainLightPosition.xyz));

                // Glass turns mirror as the view flattens out, which is the same angle where the
                // interior ray skids along one wall and the parallax stops convincing. One dot
                // product drives both, so the glare peaks exactly where it is needed to hide it.
                half grazing = pow(1.0 - saturate(dot(normalWorld, viewDirectionWorld)), _GlassFresnelPower);
                half reflectionAmount = lerp(0.04, 1.0, grazing) * _GlassReflectivity;

                half4 encodedSky = SAMPLE_TEXTURECUBE_LOD(
                    unity_SpecCube0, samplerunity_SpecCube0, reflect(-viewDirectionWorld, normalWorld), 0);
                half3 skyColor = DecodeHDREnvironment(encodedSky, unity_SpecCube0_HDR);

                // Each face is flat, so this is constant across it and a whole wall of windows
                // flashes together as the sun lines up.
                float3 halfVector = normalize(viewDirectionWorld + _MainLightPosition.xyz);
                half sunGlint = pow(saturate(dot(normalWorld, halfVector)), _SunGlintSharpness);

                // A blind hangs deeper than the glass, so it slides against the interior rather
                // than with it. That difference in rate is the depth cue, not the blind itself.
                float2 blindCellUv = cellUv + parallaxPerDepth * (_GlassRecessDepth + _BlindDepth);

                // Coverage shifts the hash rather than scaling it, so the ends of the slider clamp
                // rooms to fully open or fully shut instead of settling everything on an average.
                float blindDrop = saturate(roomNoise.z + _BlindCoverage * 2.0 - 1.0);
                float isBlind = step(1.0 - blindDrop, blindCellUv.y);

                // Slats shade as a sawtooth: each one is shadowed at its lower edge by the one
                // above. A count of zero flattens the whole blind into a roller shade.
                float slatShade = lerp(0.72, 1.0, frac(blindCellUv.y * _SlatCount));
                half3 behindGlass = lerp(interiorColor * _BaseColor.rgb,
                                         _BlindColor.rgb * slatShade, isBlind);

                half3 glassColor = lerp(behindGlass, skyColor, reflectionAmount);
                glassColor += _MainLightColor.rgb * sunGlint * _SunGlintStrength * lambert;

                float2 facadeUv = TRANSFORM_TEX(input.uv, _BaseMap);
                half3 facadeTint = lerp(_FacadeColor.rgb, _GroundFloorColor.rgb, isGroundFloor);
                half3 wallColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, facadeUv).rgb * facadeTint;

                // intoFrame is negative inside the opening, so jamb and bar pixels land here too and
                // take frame colour at full strength, which is what both of them want.
                float isFrame = step(intoFrame, _FrameThickness) * (1.0 - isUpwardFace);

                // A reveal shades by which way it faces: dark under the lintel, bright on the sill,
                // neutral on the jambs. Ramped rather than flipped, or the jambs break at midheight.
                float revealShade = 1.0 - saturate(intoFrame / max(_FrameThickness, 1e-4));
                float facingUp = clamp(-wallFromCentre.y / max(openingHalfExtents.y, 1e-4), -1.0, 1.0);
                half3 frameColor = _FrameColor.rgb * (1.0 + revealShade * facingUp * _RevealShading);

                half3 facadeColor = lerp(wallColor, frameColor, isFrame);

                // The brick needs lighting to read as solid. The interior does not - it was lit
                // when the cubemap was baked.
                half3 litFacade = facadeColor * (_MainLightColor.rgb * lambert + 0.25);

                return half4(lerp(litFacade, glassColor, isWindow), 1);
            }
            ENDHLSL
        }
    }
}
