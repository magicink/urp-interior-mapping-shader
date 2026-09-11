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
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURECUBE(_InteriorCubemap);
            SAMPLER(sampler_InteriorCubemap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Camera into object space, so the ray shares a ruler with the room box.
                float3 cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 rayDirOS = normalize(IN.positionOS - cameraOS);

                // Slab method: on each axis, the distance along the ray to the wall it is
                // heading toward. The nearest of the three is where the ray leaves the box.
                float3 tPlanes = (sign(rayDirOS) * 0.5 - IN.positionOS) / rayDirOS;
                float tExit = min(min(tPlanes.x, tPlanes.y), tPlanes.z);

                float3 hitOS = IN.positionOS + rayDirOS * tExit;

                // hitOS is already a direction from the box center, which is exactly what a
                // cubemap lookup wants. No conversion needed.
                //
                // Explicit LOD 0 matters: the direction jumps at every wall corner, and
                // automatic mip selection reads that jump as fine detail and blurs the seams.
                half3 interior = SAMPLE_TEXTURECUBE_LOD(_InteriorCubemap, sampler_InteriorCubemap, hitOS, 0).rgb;

                return half4(interior * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
