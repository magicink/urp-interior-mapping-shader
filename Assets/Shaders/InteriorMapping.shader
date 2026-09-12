Shader "InteriorMapping/SingleFile"
{
    Properties
    {
        [Header(Interior)]
        _InteriorCubemap("Interior Cubemap", Cube) = "" {}
        [MainColor] _BaseColor("Interior Tint", Color) = (1, 1, 1, 1)

        [Header(Facade)]
        [MainTexture] _BaseMap("Facade Map", 2D) = "white" {}
        _FacadeColor("Facade Color", Color) = (0.48, 0.27, 0.22, 1)
        _WindowsPerFace("Windows Per Face (X, Y)", Vector) = (3, 2, 0, 0)
        _WindowFrameWidth("Window Frame Width", Range(0, 0.49)) = 0.16

        [Header(Glass)]
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
                float4 _BaseMap_ST;
                float4 _WindowsPerFace;
                float _WindowFrameWidth;
                float _GlassReflectivity;
                float _SunGlintStrength;
                float _SunGlintSharpness;
                float _GlassFresnelPower;
            CBUFFER_END

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
                float2 roomNoise = frac(sin(float2(dot(roomIndex, float3(12.9898, 78.233, 37.719)),
                                                   dot(roomIndex, float3(39.346, 11.135, 83.155))))
                                        * 43758.5453);
                float2 mirrorSigns = step(0.5, roomNoise) * 2.0 - 1.0;
                float3 mirrorAxes = float3(mirrorSigns.x, 1.0, mirrorSigns.y);
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
                float2 withinPane = step(_WindowFrameWidth, cellUv) *
                                    step(cellUv, 1.0 - _WindowFrameWidth);

                // Roof and underside stay solid, or the building reads as a greenhouse.
                float isUpwardFace = step(0.5, abs(input.normalObjectSpace.y));
                float isWindow = withinPane.x * withinPane.y * (1.0 - isUpwardFace);

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

                half3 glassColor = lerp(interiorColor * _BaseColor.rgb, skyColor, reflectionAmount);
                glassColor += _MainLightColor.rgb * sunGlint * _SunGlintStrength * lambert;

                float2 facadeUv = TRANSFORM_TEX(input.uv, _BaseMap);
                half3 facadeColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, facadeUv).rgb * _FacadeColor.rgb;

                // The brick needs lighting to read as solid. The interior does not - it was lit
                // when the cubemap was baked.
                half3 litFacade = facadeColor * (_MainLightColor.rgb * lambert + 0.25);

                return half4(lerp(litFacade, glassColor, isWindow), 1);
            }
            ENDHLSL
        }
    }
}
