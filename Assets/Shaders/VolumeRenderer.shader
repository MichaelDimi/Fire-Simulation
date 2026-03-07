Shader "Hidden/Volumetrics/VolumeRenderer"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // vertex input: position, UV
            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewVector : TEXCOORD1;
            };
            
            v2f vert (appdata v) {
                v2f output;
                output.pos = UnityObjectToClipPos(v.vertex);
                output.uv = v.uv;
                // Camera space matches OpenGL convention where cam forward is -z. In unity forward is positive z.
                // (https://docs.unity3d.com/ScriptReference/Camera-cameraToWorldMatrix.html)
                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
                output.viewVector = mul(unity_CameraToWorld, float4(viewVector,0));
                return output;
            }

            // Textures
            Texture3D<float4> SmokeTex;
            SamplerState samplerSmokeTex;

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            // Shape settings
            float densityMultiplier;
            float4 phaseParams;

            // March settings
            int numStepsLight;

            float3 boundsMin;
            float3 boundsMax;

            // Light settings
            float lightAbsorptionTowardSun;
            float lightAbsorptionThroughCloud;
            float darknessThreshold;
            float4 _CustomLightDir;
            float4 _CustomLightCol;

            float remap(float v, float minOld, float maxOld, float minNew, float maxNew) {
                return minNew + (v-minOld) * (maxNew - minNew) / (maxOld-minOld);
            }

            float2 squareUV(float2 uv) {
                float width = _ScreenParams.x;
                float height =_ScreenParams.y;
                //float minDim = min(width, height);
                float scale = 1000;
                float x = uv.x * width;
                float y = uv.y * height;
                return float2 (x/scale, y/scale);
            }

            // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero
            float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 invRaydir) {
                // Adapted from: http://jcgt.org/published/0007/03/04/
                float3 t0 = (boundsMin - rayOrigin) * invRaydir;
                float3 t1 = (boundsMax - rayOrigin) * invRaydir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                
                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));

                // CASE 1: ray intersects box from outside (0 <= dstA <= dstB)
                // dstA is dst to nearest intersection, dstB dst to far intersection

                // CASE 2: ray intersects box from inside (dstA < 0 < dstB)
                // dstA is the dst to intersection behind the ray, dstB is dst to forward intersection

                // CASE 3: ray misses box (dstA > dstB)

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            // Henyey-Greenstein
            float hg(float a, float g) {
                float g2 = g*g;
                return (1-g2) / (4*3.1415*pow(1+g2-2*g*(a), 1.5));
            }

            float phase(float a) {
                float blend = .5;
                float hgBlend = hg(a,phaseParams.x) * (1-blend) + hg(a,-phaseParams.y) * blend;
                return phaseParams.z + hgBlend*phaseParams.w;
            }

            float beer(float d) {
                float beer = exp(-d);
                return beer;
            }

            float remap01(float v, float low, float high) {
                return (v-low)/(high-low);
            }

            float sampleDensity(float3 rayPos) {
                float3 gridSize = boundsMax - boundsMin;
                float3 uvw = (rayPos - boundsMin) / gridSize; // [0,1] in each axis
                return SmokeTex.SampleLevel(samplerSmokeTex, uvw, 0).r * densityMultiplier;
            }

            // Calculate proportion of light that reaches the given point from the lightsource
            float lightmarch(float3 p) {
                float3 dirToLight = _CustomLightDir.xyz;
                float dstInsideBox = rayBoxDst(boundsMin, boundsMax, p, 1/dirToLight).y;
                
                float transmittance = 1;
                float stepSize = dstInsideBox/numStepsLight;
                p += dirToLight * stepSize * .5;
                float totalDensity = 0;

                for (int step = 0; step < numStepsLight; step ++) {
                    float density = sampleDensity(p);
                    totalDensity += max(0, density * stepSize);
                    p += dirToLight * stepSize;
                }

                transmittance = beer(totalDensity*lightAbsorptionTowardSun);

                float clampedTransmittance = darknessThreshold + transmittance * (1-darknessThreshold);
                return clampedTransmittance;
            }
          
            float4 frag (v2f i) : SV_Target
            {
                
                // Create ray
                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector / viewLength;
                
                // Depth and cloud container intersection info:
                float nonlin_depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float depth = LinearEyeDepth(nonlin_depth) * viewLength;
                float2 rayToContainerInfo = rayBoxDst(boundsMin, boundsMax, rayPos, 1/rayDir);
                float dstToBox = rayToContainerInfo.x;
                float dstInsideBox = rayToContainerInfo.y;

                // point of intersection with the cloud container
                float3 entryPoint = rayPos + rayDir * dstToBox;
                
                // Phase function makes clouds brighter around sun
                float cosAngle = dot(rayDir, _CustomLightDir.xyz);
                float phaseVal = phase(cosAngle);

                float dstTravelled = 0; // randomOffset;
                float dstLimit = min(depth-dstToBox, dstInsideBox);
                
                
                // March through volume:
                const float stepSize = 0.1;
                float transmittance = 1;
                float3 lightEnergy = 0;

                while (dstTravelled < dstLimit) {
                    rayPos = entryPoint + rayDir * dstTravelled;
                    float density = sampleDensity(rayPos);
                    
                    if (density > 0) {

                        float lightTransmittance = lightmarch(rayPos);
                        lightEnergy += density * stepSize * transmittance * lightTransmittance * phaseVal;
                        transmittance *= exp(-density * stepSize * lightAbsorptionThroughCloud);
                    
                        // Early exit
                        if (transmittance < 0.01) {
                            break;
                        }
                    }
                    
                    dstTravelled += stepSize;
                }

               
                // Composite sky + background
                float3 backgroundCol = tex2D(_MainTex,i.uv);
                
                // Add clouds
                float3 cloudCol = lightEnergy * _CustomLightCol;
                float3 col = backgroundCol * transmittance + cloudCol;
                return float4(col,0);
            }

            ENDCG
        }
    }
}
