// The sand, ported from the web build's createBeachMaterial in world/sea.ts.
//
// A normal lit PBR surface - shadows, fog, ambient - with the colour and roughness replaced by the
// original's procedural sand: cheap value-noise grain, coarser blotches breaking up the flat gold,
// and a wet band keyed to the SAME seabed ramp the water shader and the mesh itself use, so the
// tide line lands exactly at the waterline rather than near it. No textures.
//
// Handedness: _ShoreX is Unity-space and the sea is at LARGER x. See TheBlock.World.SeaGeometry.
//
// ⚠ THE FOUR PASSES ARE NOT OPTIONAL, and this is the bug the user reported as "a black strip"
// (2026-08-18). This shader used to be ONE pass plus `UsePass` for ShadowCaster and DepthOnly, and
// it had no DepthNormals pass at all. The project's URP renderer runs SSAO with Source =
// DepthNormals, so the AO prepass builds `_CameraNormalsTexture` from whatever every DepthNormals
// pass writes - and a surface that writes nothing leaves garbage under its own pixels. URP then
// does, inside UniversalFragmentPBR:
//
//     mainLight.color *= aoFactor.directAmbientOcclusion;
//
// so the SUN was multiplied to nothing and the sand was lit by sky ambient alone. Measured: albedo
// (0.87, 0.78, 0.52) - correct gold - coming out as (0.345, 0.376, 0.318), a ratio of
// (0.14, 0.21, 0.37) that is blue-dominant because it IS the sky. The beach was never the wrong
// colour; it was the only lit surface in the game with no sun on it.
//
// `TheBlock/Water` never showed this because it does not declare _SCREEN_SPACE_OCCLUSION at all, so
// nothing multiplies its light. That is why the sea looked right next to sand that did not, and why
// this read as a beach problem for so long.
//
// The passes are also all written out - rather than `UsePass`ed from URP/Lit - so that every one of
// them declares the SAME UnityPerMaterial CBUFFER. That is what the SRP Batcher requires, and
// `UsePass` brought in URP/Lit's own layout, which never matched this one.
Shader "TheBlock/Beach"
{
    Properties
    {
        _DryColor ("Dry sand", Color) = (0.95, 0.85, 0.68, 1)
        _DryShadowColor ("Dry sand shadow", Color) = (0.79, 0.70, 0.55, 1)
        _WetColor ("Wet sand", Color) = (0.60, 0.51, 0.39, 1)

        _GrainScale ("Grain scale", Float) = 6
        _GrainStrength ("Grain strength", Float) = 0.08
        _BlotchScale ("Blotch scale", Float) = 26
        _BlotchStrength ("Blotch strength", Float) = 0.55

        _WetBandDry ("Wet band, dry side", Float) = 0.6
        _WetBandSea ("Wet band, sea side", Float) = 0.5
        _DryRoughness ("Dry roughness", Range(0, 1)) = 0.95
        _WetRoughness ("Wet roughness", Range(0, 1)) = 0.45

        _ShoreX ("Shore X (Unity space)", Float) = 430
        _WadeRun ("Wade run", Float) = 35
        _DeepY ("Seabed depth", Float) = -3
        _Level ("Sea level", Float) = 0

        // How far inland the sand stays wet. Flat dry sand puts `depth` at 0 everywhere, and the
        // config's own wet band reads 0.54 there - the whole beach half-wet, gold never drawn - so the
        // tide line is measured as a DISTANCE from the waterline. See TheBlock.World.SeaGeometry.
        _TideRun ("Tide line run (m inland)", Float) = 5

        // Diagnostic, and it stays because it is what found the SSAO fault above in one pass instead
        // of an afternoon. 0 in every shipped material - a branch on a uniform, which every GPU here
        // resolves for free. 1 = albedo only, 2 = lit but unfogged, 3 = the fog factor, 4 = the world
        // normal. Set it from the material inspector or `material.SetFloat("_Debug", n)`.
        _Debug ("Debug channel", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // ONE layout, shared by every pass below. The SRP Batcher compares these across passes and
        // drops the whole shader if they differ - which is what `UsePass "…/Lit/ShadowCaster"` did.
        CBUFFER_START(UnityPerMaterial)
            float4 _DryColor;
            float4 _DryShadowColor;
            float4 _WetColor;
            float _GrainScale;
            float _GrainStrength;
            float _BlotchScale;
            float _BlotchStrength;
            float _WetBandDry;
            float _WetBandSea;
            float _DryRoughness;
            float _WetRoughness;
            float _ShoreX;
            float _WadeRun;
            float _DeepY;
            float _Level;
            float _TideRun;
            float _Debug;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            float BeachHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float BeachNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(BeachHash(i + float2(0.0, 0.0)), BeachHash(i + float2(1.0, 0.0)), u.x),
                            lerp(BeachHash(i + float2(0.0, 1.0)), BeachHash(i + float2(1.0, 1.0)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;

                // Wet factor, measured as a DISTANCE inland from the waterline rather than from the
                // seabed's height. Dry sand is flat at y 0 and so is sea level, so a height-based band
                // reads 0.54 over the entire beach and the gold is never drawn anywhere - see
                // SeaGeometry.TideRun. `max(s, 0)` keeps everything at or past the waterline fully
                // wet, which makes the two sides continuous at the shore with no seam.
                float inland = _ShoreX - positionWS.x;
                float wet = 1.0 - smoothstep(0.0, max(0.0001, _TideRun), max(inland, 0.0));

                float blotch = BeachNoise(positionWS.xz / _BlotchScale);
                float3 drySand = lerp(_DryColor.rgb, _DryShadowColor.rgb, blotch * _BlotchStrength);
                float grain = (BeachNoise(positionWS.xz / _GrainScale) - 0.5) * _GrainStrength;
                drySand *= 1.0 + grain;
                float3 sand = lerp(drySand, _WetColor.rgb, wet);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = sand;
                surface.smoothness = 1.0 - lerp(_DryRoughness, _WetRoughness, wet);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = normalize(GetCameraPositionWS() - positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half4 color = UniversalFragmentPBR(inputData, surface);

                if (_Debug > 0.5)
                {
                    if (_Debug < 1.5) return half4(sand, 1);
                    if (_Debug < 2.5) return half4(color.rgb, 1);
                    if (_Debug < 3.5) return half4(input.fogFactor.xxx, 1);
                    return half4(inputData.normalWS * 0.5 + 0.5, 1);
                }

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // The pass whose absence made the sand dark. SSAO's DepthNormals source builds
        // `_CameraNormalsTexture` from these, and a surface that never writes one is occluded by
        // whatever was left in the buffer - which URP then multiplies the SUN by.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }

        // Written out rather than `UsePass`ed, so it shares the CBUFFER above - see the header.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct DepthOnlyAttributes { float4 positionOS : POSITION; };

            float4 DepthOnlyVert(DepthOnlyAttributes input) : SV_POSITION
            {
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half4 DepthOnlyFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
