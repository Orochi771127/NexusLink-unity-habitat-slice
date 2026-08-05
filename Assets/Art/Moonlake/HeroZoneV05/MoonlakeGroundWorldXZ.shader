// Moonlake HD-2.5D ground -- world-space XZ atlas sampling.
//
// ROOT CAUSE THIS FIXES
// ---------------------
// The Blender export packed the ground's per-face smart-project UVs into a
// 1/4 x 1/2 atlas cell. On a 46x46 ground grid that produced one small UV
// island per quad, so the grass tile was repeated once per quad -- a regular
// square grid across the whole surface.
//
// FIX
// ---
// Sample by WORLD XZ instead of mesh UV, so the texture no longer depends on
// stretched/packed object UVs and scale stays consistent between ground
// meshes. Sampling is clamped inside the grass cell with padding, and uses
// SAMPLE_TEXTURE2D_GRAD with derivatives taken from the CONTINUOUS world
// coordinate -- frac() would otherwise blow up the mip derivative at every
// wrap and bleed neighbouring atlas tiles.
//
// Repetition is broken by blending two rotated samples at different scales,
// plus restrained low-frequency world-space colour variation. No new texture,
// no package, no per-object texture.
Shader "NexusLink/MoonlakeGroundWorldXZ"
{
    Properties
    {
        _Atlas       ("Environment Atlas", 2D) = "white" {}
        // grass_moss occupies atlas cell (col 0, row 0); padded inward.
        _TileMin     ("Tile Min UV", Vector) = (0.020, 0.530, 0, 0)
        _TileMax     ("Tile Max UV", Vector) = (0.230, 0.970, 0, 0)
        _Scale       ("World Tiling (units per tile)", Float) = 1.35
        _Scale2      ("Second Layer Scale", Float) = 0.61
        _BlendScale  ("Layer Blend Noise Scale", Float) = 0.085
        _VarScale    ("Colour Variation Scale", Float) = 0.055
        _VarAmount   ("Colour Variation Amount", Range(0,0.35)) = 0.16
        _BaseColor   ("Tint", Color) = (0.88, 0.96, 0.86, 1)
        _Smoothness  ("Smoothness", Range(0,1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float3 normalWS:TEXCOORD1;
                float  fogCoord:TEXCOORD2;
            };

            TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _TileMin, _TileMax, _BaseColor;
                float  _Scale, _Scale2, _BlendScale, _VarScale, _VarAmount, _Smoothness;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1)), d = hash21(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }

            // Sample the grass cell from a CONTINUOUS world coord, with mip
            // derivatives taken before frac() so tiles never bleed.
            half3 SampleCell(float2 w)
            {
                float2 tmin = _TileMin.xy, tmax = _TileMax.xy;
                float2 span = tmax - tmin;
                float2 dwdx = ddx(w), dwdy = ddy(w);
                float2 uv = tmin + frac(w) * span;
                return SAMPLE_TEXTURE2D_GRAD(_Atlas, sampler_Atlas, uv,
                                             dwdx * span, dwdy * span).rgb;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;

                // layer A
                float2 wA = wxz / max(_Scale, 0.001);
                // layer B: rotated ~31 deg and a different scale, so the two
                // repetition lattices never line up
                float2x2 R = float2x2(0.857, -0.515, 0.515, 0.857);
                float2 wB = mul(R, wxz) / max(_Scale * _Scale2, 0.001) + 0.37;

                half3 cA = SampleCell(wA);
                half3 cB = SampleCell(wB);
                float bl = saturate(vnoise(wxz * _BlendScale) * 1.6 - 0.3);
                half3 albedo = lerp(cA, cB, bl);

                // restrained low-frequency variation (never blotchy)
                float v = vnoise(wxz * _VarScale) * 0.6
                        + vnoise(wxz * _VarScale * 2.7 + 11.3) * 0.4;
                albedo *= 1.0 + (v - 0.5) * 2.0 * _VarAmount;
                albedo *= _BaseColor.rgb;

                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 amb = SampleSH(N);
                half ndl = saturate(dot(N, mainLight.direction));
                half3 lit = amb + mainLight.color * ndl * mainLight.shadowAttenuation;

                half3 rgb = albedo * lit;
                rgb = MixFog(rgb, IN.fogCoord);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        // keep the ground receiving/casting consistently with the rest of URP
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    Fallback "Universal Render Pipeline/Lit"
}
