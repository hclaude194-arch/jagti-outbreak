// The campus sky (look pass 2): a painted gradient sky instead of Unity's physical one, so it can
// look like the owner's photos from the academic porch (2026-11-20): an orange sunset under streaky
// cirrus fading to pale blue overhead, layered hazy hills on the horizon, and at night a violet
// dusk going pink at the horizon, with stars.
//
//   - gradient from _HorizonColor to _ZenithColor, warmed by _SunGlowColor on the sun's side;
//   - the sun disc and its halo (hidden once the sun is below the horizon);
//   - two layers of cloud read from _CloudTex (R: long cirrus streaks, G: broken patches), lit
//     warm on the sun's side and shaded on the far side, drifting slowly;
//   - stars from _StarTex, faded in by _Stars;
//   - two bands of far hills drawn in front of the sky at the horizon in _HillColor, the nearer
//     band darker, as the ridges in the photos.
// DayCycle sets every colour and direction at run time; AtmosphereBuilder makes the material and
// only uses it when this shader compiled (else it keeps Unity's procedural sky).
Shader "Jagti/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.2, 0.38, 0.72, 1)
        _HorizonColor ("Horizon", Color) = (0.63, 0.7, 0.79, 1)
        _GroundColor ("Below the horizon", Color) = (0.36, 0.36, 0.34, 1)
        _SunGlowColor ("Glow round the sun", Color) = (0.3, 0.22, 0.12, 1)
        _SunColor ("Sun disc", Color) = (8, 7, 6, 1)
        _SunDir ("Towards the sun", Vector) = (0.3, 0.55, -0.75, 0)
        _SunSize ("Sun radius (radians)", Range(0.005, 0.08)) = 0.022
        _GlowHeight ("Glow falloff upwards", Range(0.5, 12)) = 4
        _Gradient ("Horizon band (lower = thinner)", Range(0.15, 1)) = 0.55
        _CloudTex ("Clouds (R cirrus, G patches)", 2D) = "black" {}
        _CloudLit ("Cloud, sunny side", Color) = (1, 0.98, 0.95, 1)
        _CloudShade ("Cloud, far side", Color) = (0.62, 0.66, 0.72, 1)
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.45
        _CloudOpacity ("Cloud opacity", Range(0, 1)) = 0.85
        _CloudSpeed ("Cloud drift", Float) = 0.004
        _StarTex ("Stars", 2D) = "black" {}
        _Stars ("Stars", Range(0, 2)) = 0
        _HillColor ("Far hills", Color) = (0.5, 0.56, 0.64, 1)
        _Hills ("Far hills", Range(0, 1)) = 1
        _Exposure ("Exposure", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor, _SunGlowColor, _SunColor;
                float4 _SunDir;
                float _SunSize, _GlowHeight, _Gradient;
                half4 _CloudLit, _CloudShade;
                float _CloudCover, _CloudOpacity, _CloudSpeed;
                float _Stars;
                half4 _HillColor;
                float _Hills, _Exposure;
                float4 _CloudTex_ST, _StarTex_ST;
            CBUFFER_END

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            TEXTURE2D(_StarTex);
            SAMPLER(sampler_StarTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            // Height of a ridge line (radians above the horizon) at azimuth a.
            float Ridge(float a, float base, float seed)
            {
                return base + 0.011 * sin(a * 3.0 + seed) + 0.007 * sin(a * 7.0 + seed * 2.3)
                     + 0.0035 * sin(a * 17.0 + seed * 0.7) + 0.0015 * sin(a * 41.0 + seed * 3.1);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 s = normalize(_SunDir.xyz);
                float y = d.y;
                float cs = dot(d, s);

                // Gradient: a wide pale band at the horizon that deepens overhead.
                float up = saturate(y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, _Gradient));

                // The sun's side of the sky glows, most near the horizon; plus a halo round the disc.
                float2 dxz = d.xz / max(length(d.xz), 1e-4);
                float2 sxz = s.xz / max(length(s.xz), 1e-4);
                float side = saturate(dot(dxz, sxz) * 0.5 + 0.5);
                float glow = side * side * side * exp(-max(y, 0.0) * _GlowHeight);
                sky += _SunGlowColor.rgb * (glow + pow(saturate(cs), 24.0) * 0.8);

                // Below the horizon (seen from the hilltops): fade to the ground colour.
                sky = lerp(sky, _GroundColor.rgb, saturate(-y * 6.0));

                // Clouds on a flat layer overhead: long streaks broken up by patches.
                float density = 0.0;
                if (y > 0.0)
                {
                    float2 uv = d.xz / (y + 0.09);
                    float t = _Time.y * _CloudSpeed;
                    float c1 = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv * 0.18 + float2(t, t * 0.35)).r;
                    float c2 = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv.yx * 0.41 + float2(-t * 0.6, t * 1.3)).g;
                    float shape = c1 * (0.55 + 0.9 * c2);
                    density = saturate((shape - (1.0 - _CloudCover)) * 3.2);
                    density *= smoothstep(0.0, 0.22, y) * _CloudOpacity;
                    float3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, pow(saturate(cs * 0.5 + 0.5), 3.0));
                    cloud += _SunGlowColor.rgb * (pow(saturate(cs), 5.0) * 1.6 + glow * 0.6);
                    sky = lerp(sky, cloud, density);
                }

                // Sun disc, dimmed behind cloud, gone below the horizon.
                float edge = cos(_SunSize);
                float disc = smoothstep(edge - 0.00025, edge + 0.00025, cs) * smoothstep(-0.03, 0.0, s.y);
                sky += _SunColor.rgb * disc * (1.0 - density * 0.8);

                // Stars.
                if (_Stars > 0.001 && y > 0.0)
                {
                    float2 suv = float2(atan2(d.x, d.z) * 0.6366, y) * 1.5;
                    float star = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, suv).r;
                    sky += star * star * _Stars * saturate(y * 5.0) * (1.0 - density);
                }

                // Far hills: two hazy ridge bands in front of the sky, the nearer one darker.
                float a = atan2(d.x, d.z);
                float far = Ridge(a, 0.022, 1.3);
                float nearR = Ridge(a, 0.012, 4.1);
                float hillFar = smoothstep(far + 0.0015, far - 0.0015, y);
                float hillNear = smoothstep(nearR + 0.0015, nearR - 0.0015, y);
                sky = lerp(sky, lerp(sky, _HillColor.rgb, 0.55), hillFar * _Hills);
                sky = lerp(sky, _HillColor.rgb * 0.82, hillNear * _Hills * 0.85);

                return half4(sky * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
