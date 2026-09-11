Shader "InteriorMapping/SingleFile"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _InteriorCubemap("Interior Cubemap", Cube) = "" {}
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

            struct Attributes
            {
                float4 positionObjectSpace : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionClipSpace : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionObjectSpace : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURECUBE(_InteriorCubemap);
            SAMPLER(sampler_InteriorCubemap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionClipSpace = TransformObjectToHClip(input.positionObjectSpace.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionObjectSpace = input.positionObjectSpace.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Object space so the ray shares a ruler with the room box.
                float3 cameraPositionObjectSpace = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 viewRayDirection = normalize(input.positionObjectSpace - cameraPositionObjectSpace);

                // Slab method: per axis, the coordinate of the wall the ray heads toward and how
                // far along the ray it sits. The nearest of the three is where the ray exits.
                float3 facingWallCoordinates = sign(viewRayDirection) * 0.5;
                float3 wallDistances = (facingWallCoordinates - input.positionObjectSpace) / viewRayDirection;
                float nearestWallDistance = min(min(wallDistances.x, wallDistances.y), wallDistances.z);

                float3 wallHitPosition = input.positionObjectSpace + viewRayDirection * nearestWallDistance;

                // The hit position doubles as a direction from the box centre, which is what a
                // cubemap lookup wants. Explicit LOD 0 stops mip selection reading the direction
                // jump at each corner as fine detail and blurring the seam.
                half3 interiorColor = SAMPLE_TEXTURECUBE_LOD(
                    _InteriorCubemap, sampler_InteriorCubemap, wallHitPosition, 0).rgb;

                return half4(interiorColor * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
