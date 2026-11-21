// Trees and bushes that move in the wind (Phase 6 look pass).
//
// A small URP shader: Lambert lighting from the sun with its shadows, the scene's ambient light,
// light let through the leaves when the sun is behind them, extra lights per vertex, and fog. Every
// pass bends the mesh the same way, so shadows sway with the tree:
//   - the whole tree leans with the wind, more the higher a point sits above the tree's base;
//   - leaves flutter a few centimetres on top of that (_Flutter, 0 for bark).
// The wind comes from the global _JagtiWind (x, z = direction, y = strength, w = gust), which
// WindDriver sets every frame; without it nothing moves. Each tree gets its own phase from its
// position, so a row of trees doesn't sway in step.
// Banner flags use the same shader with _Wave on: the sail ripples along its normal, more towards
// its free edge (uv.x runs 0 at the pole to 1 at the free edge), with no pivot needed, so static
// batching can't break it. _Cull is Off for them, since the sail is a single sheet.
// Leaf cards (Vegetation.cs, 2026-11-21): _Cutoff > 0 cuts the texture's alpha in every pass
// (so shadows have leaf holes too), and _BackFlip 0 keeps a card's normal on its back face, since
// the cards carry normals bent out from the crown's centre (the crown shades as one mass).
// CampusTerrainBuilder only uses this shader when it compiled without errors, so a problem here
// leaves the trees on URP/Lit (still, but drawn) rather than pink.
Shader "Jagti/Foliage"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Flutter ("Leaf flutter", Range(0, 1)) = 1
        _Bend ("Bend", Range(0, 3)) = 1
        _Translucency ("Light through leaves", Range(0, 1)) = 0.35
        _Wave ("Flag ripple", Range(0, 2)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        _BackFlip ("Flip normals on back faces", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Flutter;
            half _Bend;
            half _Translucency;
            half _Wave;
            half _Cull;
            half _Cutoff;
            half _BackFlip;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        float4 _JagtiWind;   // x, z: direction; y: strength (0-1); w: gust (0-1)

        float3 Sway(float3 positionWS)
        {
            float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
            float height = max(0.0, positionWS.y - pivot.y);
            float strength = _JagtiWind.y * (0.6 + 0.6 * _JagtiWind.w);
            float phase = dot(pivot.xz, float2(0.071, 0.113));
            float t = _Time.y;

            // Lean: a slow push with the wind and a gentler back-and-forth around it.
            float rock = 0.55 + 0.35 * sin(t * 1.1 + phase) + 0.15 * sin(t * 2.7 + phase * 1.7);
            float bend = height * height * 0.0035 * _Bend * strength * rock;
            float2 dir = _JagtiWind.xz;
            float3 moved = positionWS;
            moved.xz += dir * bend;
            moved.y -= bend * bend * 0.05;   // a bent tree is a little shorter

            // Flutter: quick small shakes that differ from leaf to leaf.
            float f = sin(t * 6.3 + dot(positionWS, float3(1.7, 1.1, 1.3))) * 0.6
                    + sin(t * 9.1 + dot(positionWS, float3(-1.3, 2.1, 0.7))) * 0.4;
            moved += float3(0.6, 0.35, 0.5) * f * 0.035 * _Flutter * strength * saturate(height * 0.5);
            return moved;
        }

        // A flag's sail ripples along its normal: a travelling wave from the pole to the free
        // edge, bigger in a strong wind. Does nothing when _Wave is 0 (trees).
        float3 FlagWave(float3 positionWS, float3 normalWS, float2 uv)
        {
            if (_Wave <= 0.0) return positionWS;
            float strength = 0.35 + _JagtiWind.y * (0.7 + 0.6 * _JagtiWind.w);
            float along = saturate(uv.x);
            float t = _Time.y;
            float phase = dot(positionWS.xz, float2(0.31, 0.23));
            float w = sin(t * 4.1 - along * 5.5 + uv.y * 1.1 + phase) * 0.65
                    + sin(t * 7.3 - along * 9.0 + uv.y * 2.3 + phase * 1.7) * 0.25;
            return positionWS + normalize(normalWS) * w * along * 0.17 * _Wave * strength;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 extraLight : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 positionWS = FlagWave(Sway(TransformObjectToWorld(v.positionOS.xyz)), o.normalWS, v.uv);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);

                // Point and spot lights (street lamps at night, the torch) per vertex: cheap and
                // soft enough for foliage.
                half3 extra = 0;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    #if !defined(_FORWARD_PLUS)
                    uint count = GetAdditionalLightsCount();
                    for (uint i = 0u; i < count; ++i)
                    {
                        Light light = GetAdditionalLight(i, positionWS);
                        extra += light.color * light.distanceAttenuation * light.shadowAttenuation
                               * saturate(dot(o.normalWS, light.direction) * 0.5 + 0.5);
                    }
                    #endif
                #endif
                o.extraLight = extra;
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                half3 n = normalize(i.normalWS);
                n = (IS_FRONT_VFACE(face, true, false) || _BackFlip < 0.5) ? n : -n;   // the back of a flag's sail

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half ndl = dot(n, sun.direction);
                // Wrapped Lambert: a crown is a cloud of leaves, so its shaded side isn't black.
                half diffuse = saturate(ndl * 0.75 + 0.25);
                // Light through the leaves when looking towards the sun.
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half through = pow(saturate(dot(view, -sun.direction)), 4.0) * saturate(-ndl + 0.3) * _Translucency;
                half3 lit = sun.color * sun.shadowAttenuation * (diffuse + through * half3(1.0, 1.1, 0.6));

                half3 ambient = SampleSH(n);
                half3 colour = albedo.rgb * (lit + ambient + i.extraLight);
                colour = MixFog(colour, i.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 positionWS = FlagWave(Sway(TransformObjectToWorld(v.positionOS.xyz)), normalWS, v.uv);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            Cull [_Cull]
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(FlagWave(Sway(TransformObjectToWorld(v.positionOS.xyz)), normalWS, v.uv));
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
