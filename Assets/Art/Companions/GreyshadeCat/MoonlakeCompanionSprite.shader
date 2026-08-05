// Moonlake HD-2.5D companion sprite.
//
// A plain Sprites/Default billboard reads as an unlit sticker pasted over the
// diorama. This shader keeps the approved 2D artwork completely intact but
// lets it receive the scene's ambient and main light, so the companion sits
// inside the Moonlake lighting instead of on top of it.
//
// Deliberately minimal: no shadow casting, no normal maps, no extra passes.
// It preserves sprite colour and facial readability by flooring the light
// term, so the art never crushes to black.
Shader "NexusLink/MoonlakeCompanionSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color        ("Tint", Color) = (1,1,1,1)
        _MinLight     ("Minimum Light (readability floor)", Range(0,1)) = 0.62
        _LightWrap    ("Light Wrap", Range(0,1)) = 0.70
        _AmbientGain  ("Ambient Gain", Range(0,2)) = 0.85
        _RimColor     ("Rim Colour", Color) = (0.55,0.80,1.0,1)
        _RimStrength  ("Rim Strength", Range(0,1)) = 0.18
        _Cutoff       ("Alpha Cutoff", Range(0,1)) = 0.004
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "IgnoreProjector"  = "True"
            "RenderPipeline"   = "UniversalPipeline"
            "PreviewType"      = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            Name "MoonlakeCompanionForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _RimColor;
                float  _MinLight;
                float  _LightWrap;
                float  _AmbientGain;
                float  _RimStrength;
                float  _Cutoff;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _Color;
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(albedo.a - _Cutoff);

                float3 N = normalize(IN.normalWS);

                // Ambient from the scene's SH probe -- this is what makes the
                // sprite read as cool near the lake and warm by the campfire.
                half3 ambient = SampleSH(N) * _AmbientGain;

                Light mainLight = GetMainLight();
                // Wrapped diffuse: a camera-facing billboard has a nearly
                // constant N.L, so unwrapped lambert would look flat.
                half ndl = dot(N, mainLight.direction) * 0.5h + 0.5h;
                ndl = lerp(saturate(dot(N, mainLight.direction)), ndl, _LightWrap);
                half3 diffuse = mainLight.color * ndl;

                half3 lighting = ambient + diffuse;
                // Readability floor: the approved art must never crush to black.
                lighting = max(lighting, _MinLight.xxx);

                half3 rgb = albedo.rgb * lighting;

                // Subtle cool rim so the silhouette separates from foliage.
                half rim = pow(1.0h - saturate(N.z * 0.5h + 0.5h), 2.0h);
                rgb += _RimColor.rgb * rim * _RimStrength;

                rgb = MixFog(rgb, IN.fogCoord);
                return half4(rgb, albedo.a);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
