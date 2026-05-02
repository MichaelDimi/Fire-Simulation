Shader "Hidden/Volumetrics/SmokeRenderer"
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

            Texture3D<float> SmokeTex;
            SamplerState samplerSmokeTex;
            Texture3D<float> TemperatureTex;
            SamplerState samplerTemperatureTex;
            Texture3D<float> ReactionTex;
            SamplerState samplerReactionTex;

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            float densityMultiplier;
            float smokeStepSize;
            float4 phaseParams;
            int numStepsLight;

            float3 boundsMin;
            float3 boundsMax;

            float lightAbsorptionTowardSun;
            float lightAbsorptionThroughCloud;
            float darknessThreshold;
            float4 _CustomLightDir;
            float4 _CustomLightCol;

            float fireEmissionStrength;
            float fireTemperatureThreshold;
            float fireTemperatureMax;
            float fireColorMidPoint;
            float fireColorHighPoint;
            int numStepsFireLight;
            float fireLightStrength;
            float fireLightRange;
            float fireLightAbsorption;
            float4 fireColorLow;
            float4 fireColorMid;
            float4 fireColorHigh;
            float4 _EmitterPosWS;

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

            float hg(float a, float g)
            {
                float g2 = g * g;
                return (1 - g2) / (4 * 3.1415 * pow(1 + g2 - 2 * g * a, 1.5));
            }

            float phase(float a)
            {
                float blend = 0.5;
                float hgBlend = hg(a, phaseParams.x) * (1 - blend) + hg(a, -phaseParams.y) * blend;
                return phaseParams.z + hgBlend * phaseParams.w;
            }

            float beer(float d)
            {
                return exp(-d);
            }

            float remap01(float v, float low, float high)
            {
                return (v - low) / max(high - low, 1e-5);
            }

            float sampleDensity(float3 rayPos)
            {
                float3 gridSize = boundsMax - boundsMin;
                float3 uvw = saturate((rayPos - boundsMin) / gridSize);
                return SmokeTex.SampleLevel(samplerSmokeTex, uvw, 0).r * densityMultiplier;
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

            float3 sampleLocalFireLight(float3 p)
            {
                float3 toEmitter = _EmitterPosWS.xyz - p;
                float dstToEmitter = length(toEmitter);
                if (dstToEmitter < 1e-4)
                    return 0;

                float dstToSample = min(dstToEmitter, fireLightRange);
                float stepSize = dstToSample / max(numStepsFireLight, 1);
                float3 dirToFire = toEmitter / dstToEmitter;
                float3 incomingFireLight = 0;
                float transmittance = 1;

                p += dirToFire * stepSize * 0.5;

                for (int step = 0; step < numStepsFireLight; step++)
                {
                    float density = sampleDensity(p);
                    float reaction = max(sampleReaction(p), 0.0);
                    float temperature = sampleTemperature(p);
                    float heatRaw = max(remap01(temperature, fireTemperatureThreshold, fireTemperatureMax), 0.0);
                    float heat = heatRaw / (1.0 + heatRaw);
                    float flame = reaction * heat;

                    if (flame > 0.01 && heat > 0.01)
                    {
                        float3 fireEmission = fireColorRamp(heat) * fireEmissionStrength * flame;
                        incomingFireLight += fireEmission * transmittance * stepSize;
                    }

                    transmittance *= exp(-density * stepSize * fireLightAbsorption);
                    if (transmittance < 0.01)
                        break;

                    p += dirToFire * stepSize;
                    if (any(p < boundsMin) || any(p > boundsMax))
                        break;
                }

                return incomingFireLight * fireLightStrength;
            }

            float lightmarch(float3 p)
            {
                float3 dirToLight = _CustomLightDir.xyz;
                float dstInsideBox = rayBoxDst(boundsMin, boundsMax, p, 1 / dirToLight).y;

                float stepSize = dstInsideBox / numStepsLight;
                p += dirToLight * stepSize * 0.5;
                float totalDensity = 0;

                for (int step = 0; step < numStepsLight; step++)
                {
                    float density = sampleDensity(p);
                    totalDensity += max(0, density * stepSize);
                    p += dirToLight * stepSize;
                }

                float transmittance = beer(totalDensity * lightAbsorptionTowardSun);
                return darknessThreshold + transmittance * (1 - darknessThreshold);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / viewLength;

                float nonlinDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float depth = LinearEyeDepth(nonlinDepth) * viewLength;
                float2 rayToContainerInfo = rayBoxDst(boundsMin, boundsMax, rayPos, 1 / rayDir);
                float dstToBox = rayToContainerInfo.x;
                float dstInsideBox = rayToContainerInfo.y;

                float3 entryPoint = rayPos + rayDir * dstToBox;
                float cosAngle = dot(rayDir, _CustomLightDir.xyz);
                float phaseVal = phase(cosAngle);

                float dstTravelled = 0;
                float dstLimit = min(depth - dstToBox, dstInsideBox);
                float transmittance = 1;
                float3 scatteredLight = 0;

                while (dstTravelled < dstLimit)
                {
                    rayPos = entryPoint + rayDir * dstTravelled;
                    float density = sampleDensity(rayPos);

                    if (density > 0)
                    {
                        float lightTransmittance = lightmarch(rayPos);
                        float3 sunLight = _CustomLightCol.rgb * (lightTransmittance * phaseVal);
                        float3 localFireLight = sampleLocalFireLight(rayPos);
                        scatteredLight += density * smokeStepSize * transmittance * (sunLight + localFireLight);
                        transmittance *= exp(-density * smokeStepSize * lightAbsorptionThroughCloud);

                        if (transmittance < 0.01)
                            break;
                    }

                    dstTravelled += smokeStepSize;
                }

                float3 backgroundCol = tex2D(_MainTex, i.uv);
                float3 col = backgroundCol * transmittance + scatteredLight;
                return float4(col, 1);
            }

            ENDCG
        }
    }
}
