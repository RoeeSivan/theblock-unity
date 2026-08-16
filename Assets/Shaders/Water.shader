// The Mediterranean, ported from the web build's world/sea-surface.ts.
//
// Same maths, same numbers (config.sea.surface feeds every property): three directional sine
// swells displaced in the vertex stage, two counter-scrolling normal-map layers for ripple, a
// fresnel mix toward a sky tint with a ceiling below 1 so distant water stays blue instead of
// going grey, a depth tint driven by the SAME seabed ramp the sand mesh is built from, one Blinn
// highlight, and a foam band in the shallows.
//
// Deliberately unlit: the original computed its own single-light response and putting URP's PBR
// under it would double the specular and light the water twice. It reads the scene's main light
// through _MainLightPosition/_MainLightColor, so the day/night sun still drives the glint.
//
// HANDEDNESS: _ShoreX is already in Unity space and the sea lies at LARGER x than the shore. The
// web version's comparison is the other way round because its sea is at negative x. See
// TheBlock.World.SeaGeometry - the flip happens there, once, and this shader just matches it.
Shader "TheBlock/Water"
{
    Properties
    {
        [NoScaleOffset] _NormalMap ("Ripple normal map", 2D) = "bump" {}

        [HDR] _DeepColor ("Deep colour", Color) = (0.02, 0.13, 0.24, 1)
        [HDR] _ShallowColor ("Shallow colour", Color) = (0.24, 0.60, 0.65, 1)
        [HDR] _SkyColor ("Sky colour", Color) = (0.29, 0.57, 0.78, 1)
        [HDR] _FoamColor ("Foam colour", Color) = (0.82, 0.87, 0.90, 1)

        _FresnelCeil ("Fresnel ceiling", Range(0, 1)) = 0.6
        _FoamStrength ("Foam strength", Range(0, 1)) = 0.32
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.35
        _SpecPower ("Specular power", Range(1, 512)) = 180

        // Shoreline ramp - must match SeaGeometry.SeabedHeight exactly.
        _ShoreX ("Shore X (Unity space)", Float) = 430
        _WadeRun ("Wade run", Float) = 35
        _DeepY ("Seabed depth", Float) = -3
        _Level ("Sea level", Float) = 0
        _SwellFadeDepth ("Swell fade depth", Float) = 1.5

        // Each wave: xy = direction, z = amplitude, w = wavelength. Speeds packed together.
        _Wave0 ("Wave 0", Vector) = (1, 0.3, 0.18, 22)
        _Wave1 ("Wave 1", Vector) = (0.8, -0.6, 0.12, 13)
        _Wave2 ("Wave 2", Vector) = (0.2, 1, 0.07, 7)
        _WaveSpeeds ("Wave speeds", Vector) = (3.2, 2.4, 1.8, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _SkyColor;
                float4 _FoamColor;
                float _FresnelCeil;
                float _FoamStrength;
                float _RippleStrength;
                float _SpecPower;
                float _ShoreX;
                float _WadeRun;
                float _DeepY;
                float _Level;
                float _SwellFadeDepth;
                float4 _Wave0;
                float4 _Wave1;
                float4 _Wave2;
                float4 _WaveSpeeds;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Seabed height at a Unity world X - the shader's copy of SeaGeometry.SeabedHeight.
            float Seabed(float x)
            {
                return x <= _ShoreX ? 0.0 : _DeepY * min((x - _ShoreX) / _WadeRun, 1.0);
            }

            float WaveHeight(float4 w, float speed, float2 xz, float time)
            {
                float2 dir = normalize(w.xy);
                float k = TWO_PI / w.w;
                return w.z * sin(dot(dir, xz) * k + time * speed * k);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Swells fade out in the shallows, so a wave never lifts above the beach sand.
                float fade = saturate((_Level - Seabed(positionWS.x)) / _SwellFadeDepth);
                float time = _Time.y;
                float y = WaveHeight(_Wave0, _WaveSpeeds.x, positionWS.xz, time)
                        + WaveHeight(_Wave1, _WaveSpeeds.y, positionWS.xz, time)
                        + WaveHeight(_Wave2, _WaveSpeeds.z, positionWS.xz, time);
                positionWS.y += y * fade;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float time = _Time.y;
                float3 positionWS = input.positionWS;

                // Two counter-scrolling layers at different scales → ripple that never repeats.
                float2 uv1 = positionWS.xz / 28.0 + time * float2(0.020, 0.016);
                float2 uv2 = positionWS.xz / 11.0 - time * float2(0.017, 0.026);
                float3 n1 = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1).xyz * 2.0 - 1.0;
                float3 n2 = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2).xyz * 2.0 - 1.0;
                // Tangent space (z up) → world space (y up); ripple strength scales the tilt.
                float3 n = normalize(float3((n1.x + n2.x) * _RippleStrength,
                                            1.0,
                                            (n1.y + n2.y) * _RippleStrength));

                float3 viewDir = normalize(GetCameraPositionWS() - positionWS);
                float fresnel = pow(1.0 - max(dot(viewDir, n), 0.0), 3.0);
                fresnel = lerp(0.04, _FresnelCeil, fresnel);

                float depth = _Level - Seabed(positionWS.x);
                float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb,
                                         saturate(depth / -_DeepY));
                float3 color = lerp(waterColor, _SkyColor.rgb, fresnel);

                // Sun glint. URP's main light, so the sky and the sea agree about where the sun is.
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDir);
                color += mainLight.color * pow(max(dot(n, halfDir), 0.0), _SpecPower);

                // Foam in the shallows, broken up by the ripple so it is not a hard stripe.
                float foam = smoothstep(1.2, 0.15, depth);
                foam *= 0.55 + 0.45 * sin(time * 1.4 + positionWS.x * 0.9 + n1.x * 6.0);
                color = lerp(color, _FoamColor.rgb, saturate(foam) * _FoamStrength);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
