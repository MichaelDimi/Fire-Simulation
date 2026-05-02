Shader "Hidden/Volumetrics/FireRenderer"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewVector : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(v.vertex);
                output.uv = v.uv;

                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
                output.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));
                return output;
            }

            Texture3D<float> TemperatureTex;
            SamplerState samplerTemperatureTex;
            Texture3D<float> ReactionTex;
            SamplerState samplerReactionTex;

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            float3 boundsMin;
            float3 boundsMax;

            float fireEmissionStrength;
            float fireTemperatureThreshold;
            float fireTemperatureMax;
            float fireOpacity;
            float fireStepSize;
            float fireColorMidPoint;
            float fireColorHighPoint;
            float fireDetailScale;
            float fireDetailStrength;
            float fireFlickerSpeed;
            float4 fireColorLow;
            float4 fireColorMid;
            float4 fireColorHigh;

            float2 rayBoxDst(float3 localBoundsMin, float3 localBoundsMax, float3 rayOrigin, float3 invRayDir)
            {
                float3 t0 = (localBoundsMin - rayOrigin) * invRayDir;
                float3 t1 = (localBoundsMax - rayOrigin) * invRayDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            float remap01(float v, float low, float high)
            {
                return (v - low) / max(high - low, 1e-5);
            }

            float sampleTemperature(float3 rayPos)
            {
                float3 gridSize = boundsMax - boundsMin;
                float3 uvw = saturate((rayPos - boundsMin) / gridSize);
                return TemperatureTex.SampleLevel(samplerTemperatureTex, uvw, 0).r;
            }

            float sampleReaction(float3 rayPos)
            {
                float3 gridSize = boundsMax - boundsMin;
                float3 uvw = saturate((rayPos - boundsMin) / gridSize);
                return ReactionTex.SampleLevel(samplerReactionTex, uvw, 0).r;
            }

            float3 fireColorRamp(float heat)
            {
                if (heat <= fireColorMidPoint)
                {
                    float lowToMidT = saturate(heat / max(fireColorMidPoint, 1e-5));
                    return lerp(fireColorLow.rgb, fireColorMid.rgb, lowToMidT);
                }

                float midToHighT = saturate((heat - fireColorMidPoint) / max(fireColorHighPoint - fireColorMidPoint, 1e-5));
                return lerp(fireColorMid.rgb, fireColorHigh.rgb, midToHighT);
            }

            float hash31(float3 p)
            {
                p = frac(p * float3(127.1, 311.7, 74.7));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x),
                         lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                         lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y),
                    f.z);
            }

            float fbm(float3 p)
            {
                float value = 0;
                float amplitude = 0.5;

                for (int octave = 0; octave < 3; octave++)
                {
                    value += noise3(p) * amplitude;
                    p = p * 2.03 + float3(17.1, 9.2, 13.7);
                    amplitude *= 0.5;
                }

                return value;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 backgroundCol = tex2D(_MainTex, i.uv);

                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / viewLength;

                float nonlinDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float depth = LinearEyeDepth(nonlinDepth) * viewLength;
                float2 rayToContainerInfo = rayBoxDst(boundsMin, boundsMax, rayPos, 1 / rayDir);
                float dstToBox = rayToContainerInfo.x;
                float dstInsideBox = rayToContainerInfo.y;
                float dstLimit = min(depth - dstToBox, dstInsideBox);

                if (dstLimit <= 0)
                    return float4(backgroundCol, 1);

                float3 entryPoint = rayPos + rayDir * dstToBox;
                float dstTravelled = 0;
                float3 emittedLight = 0;
                float fireTransmittance = 1.0;

                while (dstTravelled < dstLimit)
                {
                    rayPos = entryPoint + rayDir * dstTravelled;
                    float reaction = max(sampleReaction(rayPos), 0.0);
                    float temperature = sampleTemperature(rayPos);
                    float heatRaw = max(remap01(temperature, fireTemperatureThreshold, fireTemperatureMax), 0.0);
                    float heat = heatRaw / (1.0 + heatRaw);
                    float flame = reaction * heat;
                    float height01 = saturate((rayPos.y - boundsMin.y) / max(boundsMax.y - boundsMin.y, 1e-5));
                    float detailNoise = fbm(rayPos * fireDetailScale + float3(0, _Time.y * fireFlickerSpeed, 0));
                    float detailMask = lerp(1.0, detailNoise, fireDetailStrength * height01);
                    flame *= max(detailMask, 0.0);

                    if (flame > 0.01 && heat > 0.01)
                    {
                        float3 fireEmission = fireColorRamp(heat) * fireEmissionStrength * flame;
                        float extinction = fireOpacity * flame;
                        float sampleTransmittance = exp(-extinction * fireStepSize);
                        float sampleOpacity = 1.0 - sampleTransmittance;
                        emittedLight += fireEmission * sampleOpacity * fireTransmittance;
                        fireTransmittance *= sampleTransmittance;

                        if (fireTransmittance < 0.01)
                            break;
                    }

                    dstTravelled += fireStepSize;
                }

                float3 mappedFire = 1.0 - exp(-emittedLight);
                return float4(backgroundCol * fireTransmittance + mappedFire, 1);
            }

            ENDCG
        }
    }
}
