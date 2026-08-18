// The sand, ported from the web build's createBeachMaterial in world/sea.ts.
//
// A normal lit PBR surface - shadows, fog, ambient - with the colour and roughness replaced by the
// original's procedural sand: cheap value-noise grain, coarser blotches breaking up the flat gold,
// and a wet band keyed to the SAME seabed ramp the water shader and the mesh itself use, so the
// tide line lands exactly at the waterline rather than near it. No textures.
//
// Handedness: _ShoreX is Unity-space and the sea is at LARGER x. See TheBlock.World.SeaGeometry.
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

        // The dry-sand berm. Without it `depth` is 0 across every metre of dry sand and the wet band
        // resolves to 0.54 everywhere, so the gold above is never drawn. See TheBlock.World.SeaGeometry.
        _BermRun ("Berm run (dry width)", Float) = 25
        _BermHeight ("Berm crest height", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float _BermRun;
                float _BermHeight;
            CBUFFER_END

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

                // Wet factor: 0 a little landward of the shore, 1 once under water. The landward half
                // is the BERM, and it is what makes the dry side genuinely dry - a flat 0 there puts
                // `depth` at 0 over the whole beach, and smoothstep(-0.6, 0.5, 0) is 0.54, so all of
                // it renders half-wet. Mirrors SeaGeometry.SeabedHeight exactly; the two must agree
                // or the tide line does not sit on the mesh's own shape.
                float seabed = positionWS.x <= _ShoreX
                    ? _BermHeight * sin(saturate((_ShoreX - positionWS.x) / max(0.0001, _BermRun)) * 3.14159265)
                    : _DeepY * min((positionWS.x - _ShoreX) / _WadeRun, 1.0);
                float depth = _Level - seabed;
                float wet = smoothstep(-_WetBandDry, _WetBandSea, depth);

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
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // Sand takes shadows and casts them - the wall of a dune reads wrong without this.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
