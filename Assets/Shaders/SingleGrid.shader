Shader "Hidden/Volumetrics/SingleGrid"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}

        _GridBoundsMinWS ("Grid Bounds Min (WS)", Vector) = (-30,-30,-30,1)
        _GridBoundsMaxWS ("Grid Bounds Max (WS)", Vector) = ( 30, 30, 30,1)

        _Resolution ("Resolution", Float) = 128.0

        _SigmaA ("Absorbtion", Float) = 0.0
        _SigmaS ("Scattering", Float) = 0.0
        _PhaseG ("Phase g", Float) = 0.0
        _Density ("Density", Float) = 0.0

        _StepSize ("Step Size", Float) = 0.2

        _LightEnabled ("Light Enabled", Float) = 1
        _LightType ("Light Type (0=Dir, 1=Point)", Float) = 0
        _LightColor ("Light Color", Color) = (1, 1, 1, 1)
        _LightDirWS ("Light Dir To Source (WS)", Vector) = (0, 1, 0, 0)
        _LightPosWS ("Light Pos (WS)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float4 _GridBoundsMinWS;
            float4 _GridBoundsMaxWS;

            float  _SigmaA;
            float  _SigmaS;
            float  _PhaseG;
            float  _Density;

            float  _StepSize;

            float  _LightEnabled;
            float  _LightType;
            float4 _LightColor;
            float4 _LightDirWS;
            float4 _LightPosWS;

            float4x4 _InvProj;
            float4x4 _CustomCameraToWorld;
            float3 _CameraPosWS;

            static const float PI = 3.14159265359;

            // The Henyey-Greenstein phase function
            float PhaseHG(float3 view_dir, float3 light_dir, float g)
            {
                float cos_theta = dot(view_dir, light_dir);
                return 1 / (4 * PI) * (1 - g * g) / pow(1 + g * g - 2 * g * cos_theta, 1.5);
            }

            float3 LightDirToSourceWS(float3 samplePosWS)
            {
                // 0 = directional, 1 = point
                if (_LightType < 0.5)
                {
                    // MARK: Hard coding to match his
                    return float3(-0.315798, 0.719361, 0.618702);
                    return normalize(_LightDirWS.xyz);
                }
                else
                {
                    return normalize(_LightPosWS.xyz - samplePosWS);
                }
            }

            float3 GetRayDirWS(float2 uv)
            {
                float2 ndc = uv * 2.0 - 1.0; // Normalized Device Coordinates
                float4 clip = float4(ndc.x, ndc.y, 1.0, 1.0); // Far plane

                // Inverse projection gives a view-space point along the ray
                float4 view = mul(_InvProj, clip);
                float3 dirVS = normalize(view.xyz / max(view.w, 1e-6));

                // Rotate into world
                float3 dirWS = normalize(mul((float3x3)_CustomCameraToWorld, dirVS));
                return dirWS;
            }

            bool RayBoxIntersect(float3 ro, float3 rd, float3 bmin, float3 bmax, out float t0, out float t1)
            {
                float3 invD = 1.0 / (rd + sign(rd) * 1e-6); // avoid div0
                float3 tbot = (bmin - ro) * invD;
                float3 ttop = (bmax - ro) * invD;
                float3 tmin3 = min(tbot, ttop);
                float3 tmax3 = max(tbot, ttop);

                t0 = max(max(tmin3.x, tmin3.y), tmin3.z);
                t1 = min(min(tmax3.x, tmax3.y), tmax3.z);

                return t1 >= max(t0, 0.0);
            }

            void RayMarchForward(float3 roWS, float3 rdWS, float t0, float t1, out float3 volRgb, out float transparency)
            {
                volRgb = 0.0.xxx;
                transparency = 1.0;

                int ns = (int)ceil((t1 - t0) / _StepSize);
                float step = (t1 - t0) / ns;

                [loop]
                for (int n = 0; n < ns; n++)
                {
                    // Forward: t0 -> t1
                    float t = t0 + step * (n + 0.5);
                    float3 samplePosWS = roWS + t * rdWS;

                    // Attenuate volume object transparency by current sample transmission
                    float density = _Density;
                    float sampleAtten = exp(-step * density * (_SigmaA + _SigmaS));
                    transparency *= sampleAtten;

                    // In-Scatterning
                    float lt0, lt1;
                    float3 lgtDirWS = LightDirToSourceWS(samplePosWS);
                    if (density > 0 && 
                        RayBoxIntersect(samplePosWS, lgtDirWS, _GridBoundsMinWS.xyz, _GridBoundsMaxWS.xyz, lt0, lt1))
                    {
                        int numLightSteps = ceil(lt1 / step);
                        float lightStep = lt1 / numLightSteps;
                        float tau = 0;
                        // Ray-march along the light ray. Store the density values in the tau variable.
                        for (int nl = 0; nl < numLightSteps; ++nl) {
                            float tLight = lightStep * (nl + 0.5);
                            float3 lightSamplePos = samplePosWS + lgtDirWS * tLight;
                            tau += _Density;
                        }

                        // Attenuate in-scattering contribution by the transmission of all samples accumulated so far
                        float lightAtten = exp(-tau * lightStep * (_SigmaA + _SigmaS));
                        volRgb += _LightColor.rgb *
                                  lightAtten * 
                                  PhaseHG(-rdWS, lgtDirWS, _PhaseG) *
                                  _SigmaS * 
                                  transparency *
                                  step *
                                  density;
                    }
                }
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb;

                // Camera ray in VIEW space
                float3 roWS = _CameraPosWS;
                float3 rdWS = GetRayDirWS(i.uv);

                float t0, t1;
                if (!RayBoxIntersect(roWS, rdWS, _GridBoundsMinWS.xyz, _GridBoundsMaxWS.xyz, t0, t1))
                    return float4(scene, 1);

                // If the grid is behind the camera
                if (t1 <= 0.0)
                    return float4(scene, 1);

                // Clamp entry to camera near point
                t0 = max(t0, 0.0);

                float3 volRgb;
                float transparency;
                RayMarchForward(roWS, rdWS, t0, t1, volRgb, transparency);

                float3 outCol = scene * transparency + volRgb;
                return float4(outCol, 1.0);
            }
            ENDHLSL
        }
    }
}
