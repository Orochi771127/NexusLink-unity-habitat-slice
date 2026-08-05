// Moonlake HD-2.5D waterfall -- V0.4 material-driven lookdev.
//
// ONE ribbon renderer, ONE material. All side strands, gaps and edge breakup
// live inside the animated flipbook alpha; no extra geometry, no side-stream
// meshes, no duplicate renderer, no extra render loop.
//
// Flipbook: 4x4 = 16 deterministic frames, seamless loop.
// Frame selection:   idx = floor(frac(_Time.y * _FPS / 16) * 16)
//                    uv  = (frameUV + float2(idx % 4, 3 - idx / 4)) / 4
// (row is flipped because texture V runs bottom-up while frame 0 is top-left.)
//
// Two-speed distortion: a low-frequency scroll of the sample UV, plus a second
// slower counter-scroll, gives non-repeating deformation without a noise
// texture lookup.
//
// THREE.JS EQUIVALENT (documented, NOT implemented here):
//   ShaderMaterial { transparent:true, depthWrite:false, side:DoubleSide }
//   uniform sampler2D uFlip; uniform float uTime, uFps, uCols, uRows;
//   float i = floor(fract(uTime*uFps/(uCols*uRows))*(uCols*uRows));
//   vec2 f = vec2(mod(i,uCols), (uRows-1.0)-floor(i/uCols));
//   vec2 uv = (vUv*vec2(1.0/uCols,1.0/uRows)) + f*vec2(1.0/uCols,1.0/uRows);
//   vec4 t = texture2D(uFlip, uv);
//   gl_FragColor = vec4(t.rgb*uTint, t.a*uOpacity);
//   glTF export equivalent: alphaMode BLEND (or MASK w/ cutoff 0.5).
Shader "NexusLink/MoonlakeWaterfallFlipbook"
{
    Properties
    {
        _FlipTex      ("Flipbook (RGBA)", 2D) = "white" {}
        _Cols         ("Flipbook Columns", Float) = 4
        _Rows         ("Flipbook Rows", Float) = 4
        _FPS          ("Frames Per Second", Float) = 14
        _Tint         ("Water Tint", Color) = (0.72, 0.84, 0.92, 1)
        _Opacity      ("Global Opacity", Range(0,1)) = 1.0
        _EdgeFade     ("Edge Fade (U)", Range(0,0.5)) = 0.16
        _TopFade      ("Top Fade (V)", Range(0,0.4)) = 0.06
        _Emission     ("Emission", Range(0,0.04)) = 0.03
        _DistortA     ("Distort Speed A", Float) = 0.07
        _DistortB     ("Distort Speed B", Float) = -0.031
        _DistortAmt   ("Distort Amount", Range(0,0.08)) = 0.022
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Toggle] _ZWriteOn ("ZWrite", Float) = 0
        // ---- QA only. Production default stays time-based (_UseManualFrame 0).
        [Toggle] _UseManualFrame ("QA: Use Manual Frame", Float) = 0
        _ManualFrameIndex ("QA: Manual Frame Index", Range(0,15)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "MoonlakeWaterfallForward"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha: stable against sorting and avoids the dark
            // fringing plain SrcAlpha blending gives on soft water edges.
            Blend One OneMinusSrcAlpha
            ZWrite [_ZWriteOn]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
            struct Varyings   { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
                                float3 normalWS:TEXCOORD1; float fogCoord:TEXCOORD2; };

            TEXTURE2D(_FlipTex); SAMPLER(sampler_FlipTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlipTex_ST;
                float4 _Tint;
                float  _Cols, _Rows, _FPS, _Opacity;
                float  _EdgeFade, _TopFade, _Emission;
                float  _DistortA, _DistortB, _DistortAmt;
                float  _UseManualFrame, _ManualFrameIndex;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.uv         = IN.uv;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float total = _Cols * _Rows;

                // ---- two-speed low-frequency distortion -------------------
                float2 uv = IN.uv;
                float d1 = sin((uv.y * 6.0) + _Time.y * _DistortA * 6.2831) * _DistortAmt;
                float d2 = sin((uv.y * 2.7) - _Time.y * _DistortB * 6.2831) * _DistortAmt * 0.6;
                uv.x = saturate(uv.x + d1 + d2);

                // ---- flipbook frame selection -----------------------------
                float autoIdx = floor(frac(_Time.y * _FPS / total) * total);
                float manIdx  = clamp(floor(_ManualFrameIndex + 0.5), 0.0, total - 1.0);
                float idx = lerp(autoIdx, manIdx, step(0.5, _UseManualFrame));
                float col = fmod(idx, _Cols);
                float row = (_Rows - 1.0) - floor(idx / _Cols);
                float2 cell = float2(1.0 / _Cols, 1.0 / _Rows);
                float2 fuv  = (uv * cell) + float2(col, row) * cell;

                half4 tex = SAMPLE_TEXTURE2D(_FlipTex, sampler_FlipTex, fuv);

                // ---- ribbon-space fades so the mesh border never shows ----
                float eu = smoothstep(0.0, _EdgeFade, IN.uv.x)
                         * smoothstep(0.0, _EdgeFade, 1.0 - IN.uv.x);
                float ev = smoothstep(0.0, _TopFade, 1.0 - IN.uv.y);
                float a  = saturate(tex.a * eu * ev * _Opacity);

                half3 rgb = tex.rgb * _Tint.rgb;

                // restrained scene response: keep the water in the Moonlake key
                Light mainLight = GetMainLight();
                half3 amb = SampleSH(normalize(IN.normalWS));
                half3 lit = saturate(amb * 0.55 + mainLight.color * 0.45 + 0.35);
                rgb *= lit;
                rgb += rgb * _Emission;

                rgb = MixFog(rgb, IN.fogCoord);
                return half4(rgb * a, a);   // premultiplied
            }
            ENDHLSL
        }
    }
    Fallback Off
}
