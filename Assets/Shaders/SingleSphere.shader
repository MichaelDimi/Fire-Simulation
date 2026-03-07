Shader "Hidden/Volumetrics/SingleSphere"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}

        _SphereCenterVS ("Sphere Center (VS)", Vector) = (0.0, 0.0, 0.0, 0.0)
        _SphereRadius   ("Sphere Radius", Float) = 0.0
        _SigmaA         ("Absorbtion", Float) = 0.0
        _SigmaS         ("Scattering", Float) = 0.0
        _Density        ("Density", Float) = 0.0
        _ScatterColor   ("Scatter Color", Color) = (0.0, 0.0, 0.0, 0.0) // MARK: Only used if there is no light enabled
        _PhaseG         ("Phase g (Henyey-Greenstein)", Float) = 0.0

        // Ray marching controls
        _StepSize       ("Step Size", Float) = 0.2

        // Light Params
        _LightEnabled   ("Light Enabled", Float) = 1
        _LightType      ("Light Type (0=Dir, 1=Point)", Float) = 0
        _LightColor     ("Light Color", Color) = (1, 1, 1, 1)
        _LightDirVS     ("Light Dir To Source (VS)", Vector) = (0, 0, 0, 0)
        _LightPosVS     ("Light Pos (VS)", Vector) = (0, 0, 0, 1)
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

            float4 _SphereCenterVS;
            float  _SphereRadius;
            float  _SigmaA;
            float  _SigmaS;
            float  _Density;
            float _PhaseG;
            float4 _ScatterColor;

            float4x4 _InvProj;

            float  _StepSize;

            float  _LightEnabled;
            float  _LightType;
            float4 _LightColor;
            float4 _LightDirVS;
            float4 _LightPosVS;

            static const float PI = 3.14159265359;

            float InterleavedGradientNoise(float2 screenPos)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(screenPos, magic.xy)));
            }

            static const int p[512] = {
                151, 160, 137,  91,  90,  15, 131,  13, 201,  95,  96,  53, 194, 233,   7, 225,
                140,  36, 103,  30,  69, 142,   8,  99,  37, 240,  21,  10,  23, 190,   6, 148,
                247, 120, 234,  75,   0,  26, 197,  62,  94, 252, 219, 203, 117,  35,  11,  32,
                57, 177,  33,  88, 237, 149,  56,  87, 174,  20, 125, 136, 171, 168,  68, 175,
                74, 165,  71, 134, 139,  48,  27, 166,  77, 146, 158, 231,  83, 111, 229, 122,
                60, 211, 133, 230, 220, 105,  92,  41,  55,  46, 245,  40, 244, 102, 143,  54,
                65,  25,  63, 161,   1, 216,  80,  73, 209,  76, 132, 187, 208,  89,  18, 169,
                200, 196, 135, 130, 116, 188, 159,  86, 164, 100, 109, 198, 173, 186,   3,  64,
                52, 217, 226, 250, 124, 123,   5, 202,  38, 147, 118, 126, 255,  82,  85, 212,
                207, 206,  59, 227,  47,  16,  58,  17, 182, 189,  28,  42, 223, 183, 170, 213,
                119, 248, 152,   2,  44, 154, 163,  70, 221, 153, 101, 155, 167,  43, 172,   9,
                129,  22,  39, 253,  19,  98, 108, 110,  79, 113, 224, 232, 178, 185, 112, 104,
                218, 246,  97, 228, 251,  34, 242, 193, 238, 210, 144,  12, 191, 179, 162, 241,
                81,  51, 145, 235, 249,  14, 239, 107,  49, 192, 214,  31, 181, 199, 106, 157,
                184,  84, 204, 176, 115, 121,  50,  45, 127,   4, 150, 254, 138, 236, 205,  93,
                222, 114,  67,  29,  24,  72, 243, 141, 128, 195,  78,  66, 215,  61, 156, 180,

                151, 160, 137,  91,  90,  15, 131,  13, 201,  95,  96,  53, 194, 233,   7, 225,
                140,  36, 103,  30,  69, 142,   8,  99,  37, 240,  21,  10,  23, 190,   6, 148,
                247, 120, 234,  75,   0,  26, 197,  62,  94, 252, 219, 203, 117,  35,  11,  32,
                57, 177,  33,  88, 237, 149,  56,  87, 174,  20, 125, 136, 171, 168,  68, 175,
                74, 165,  71, 134, 139,  48,  27, 166,  77, 146, 158, 231,  83, 111, 229, 122,
                60, 211, 133, 230, 220, 105,  92,  41,  55,  46, 245,  40, 244, 102, 143,  54,
                65,  25,  63, 161,   1, 216,  80,  73, 209,  76, 132, 187, 208,  89,  18, 169,
                200, 196, 135, 130, 116, 188, 159,  86, 164, 100, 109, 198, 173, 186,   3,  64,
                52, 217, 226, 250, 124, 123,   5, 202,  38, 147, 118, 126, 255,  82,  85, 212,
                207, 206,  59, 227,  47,  16,  58,  17, 182, 189,  28,  42, 223, 183, 170, 213,
                119, 248, 152,   2,  44, 154, 163,  70, 221, 153, 101, 155, 167,  43, 172,   9,
                129,  22,  39, 253,  19,  98, 108, 110,  79, 113, 224, 232, 178, 185, 112, 104,
                218, 246,  97, 228, 251,  34, 242, 193, 238, 210, 144,  12, 191, 179, 162, 241,
                81,  51, 145, 235, 249,  14, 239, 107,  49, 192, 214,  31, 181, 199, 106, 157,
                184,  84, 204, 176, 115, 121,  50,  45, 127,   4, 150, 254, 138, 236, 205,  93,
                222, 114,  67,  29,  24,  72, 243, 141, 128, 195,  78,  66, 215,  61, 156, 180
            };

            float fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
            float my_lerp(float t, float a, float b) { return a + t * (b - a); }
            float grad(int hash, float x, float y, float z)
            {
                int h = hash & 15;
                float u = h < 8 ? x : y,
                    v = h < 4 ? y : h == 12 || h == 14 ? x : z;
                return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
            }

            // We use a Perlin noise procedural texture to create a space varying density field.
            // This function returns values in the range [-1,1].
            float noise(float x, float y, float z)
            {
                int X = (int)(floor(x)) & 255,
                    Y = (int)(floor(y)) & 255,
                    Z = (int)(floor(z)) & 255;
                x -= floor(x);
                y -= floor(y);
                z -= floor(z);
                float u = fade(x),
                    v = fade(y),
                    w = fade(z);
                int A = p[X] + Y, AA = p[A] + Z, AB = p[A + 1] + Z,
                    B = p[X + 1] + Y, BA = p[B] + Z, BB = p[B + 1] + Z;

                return my_lerp(w, my_lerp(v, my_lerp(u, grad(p[AA], x, y, z),
                                            grad(p[BA], x - 1, y, z)),
                                    my_lerp(u, grad(p[AB], x, y - 1, z),
                                            grad(p[BB], x - 1, y - 1, z))),
                            my_lerp(v, my_lerp(u, grad(p[AA + 1], x, y, z - 1),
                                            grad(p[BA + 1], x - 1, y, z - 1)),
                                    my_lerp(u, grad(p[AB + 1], x, y - 1, z - 1),
                                            grad(p[BB + 1], x - 1, y - 1, z - 1))));
            }

            // MARK: Density seems to be working fine. The issue is with lighting
            float EvalDensity(float3 p)
            {
                float freq = 1;
                return max(0, noise(p.x * freq, p.y * freq, p.z * freq));
                return _Density;
            }

            float smoothstep(float lo, float hi, float x)
            {
                float t = clamp((x - lo) / (hi - lo), 0.f, 1.f);
                return t * t * (3.0 - (2.0 * t));
            }

            // float eval_density(float3 sample_pos, float3 sphere_center, float sphere_radius)
            // {
            //     float3 vp = sample_pos - sphere_center;
            //     float dist = min(1.f, length(vp) / sphere_radius);
            //     float falloff = smoothstep(0.8, 1, dist); // smooth transition from 0 to 1 as distance goes from 0.1 to 1
            //     return (1 - falloff);
            // }

            float eval_density(float3 p, float3 center, float radius)
            { 
                float3 vp = p - center;
                float3 vp_xform;

                float theta = (_Time.w - 1) / 120.f * 2 * PI;
                vp_xform.x =  cos(theta) * vp.x + sin(theta) * vp.z;
                vp_xform.y = vp.y;
                vp_xform.z = -sin(theta) * vp.x + cos(theta) * vp.z;

                float dist = min(1.f, length(vp) / radius);
                float falloff = smoothstep(0.8, 1, dist);
                float freq = 0.5;
                int octaves = 5;
                float lacunarity = 2;
                float H = 0.4;
                vp_xform *= freq;
                float fbmResult = 0;
                float offset = 0.75;
                for (int k = 0; k < octaves; k++) {
                    fbmResult += noise(vp_xform.x , vp_xform.y, vp_xform.z) * pow(lacunarity, -H * k);
                    vp_xform *= lacunarity;
                }
                return max(0.f, fbmResult) * (1 - falloff);
            }

            // The Henyey-Greenstein phase function
            float PhaseHG(float3 view_dir, float3 light_dir, float g)
            {
                float cos_theta = dot(view_dir, light_dir);
                return 1 / (4 * PI) * (1 - g * g) / pow(1 + g * g - 2 * g * cos_theta, 1.5);
            }

            float3 GetViewRayDir(float2 uv)
            {
                // Normalized Device Coordinates: (0 ... 1) -> (-1 ... 1)
                float2 ndc = uv * 2.0 - 1.0;

                // Point on the far plane in clip space (z=1)
                float4 clip = float4(ndc.x, ndc.y, 1.0, 1.0);

                // Inverse projection gives a view-space point along the ray
                float4 view = mul(_InvProj, clip);
                float3 pVS = view.xyz / max(view.w, 1e-6);

                return normalize(pVS);
            }

            bool solveQuadratic(float a, float b, float c, out float r0, out float r1)
            {
                float d = b * b - 4 * a * c;
                if (d < 0) return false;
                else if (d == 0) r0 = r1 = -0.5f * b / a;
                else {
                    float q = (b > 0) ? -0.5f * (b + sqrt(d)) : -0.5f * (b - sqrt(d));
                    r0 = q / a;
                    r1 = c / q;
                }

                if (r0 > r1) {
                    float temp = r0;
                    r0 = r1;
                    r1 = temp;
                }

                return true;
            }

            // Ray-sphere intersection in view space, returns entry/exit t
            bool IntersectSphereVS(float3 ro, float3 rd, float3 centerVS, float radius, out float t0, out float t1)
            {
                float3 oc = ro - centerVS;
                float a = dot(rd, rd);
                float b = 2 * dot(oc, rd);
                float c = dot(oc, oc) - radius * radius;
                
                if (!solveQuadratic(a, b, c, t0, t1)) return false;

                if (t0 < 0) {
                    if (t1 < 0) return false;
                    else {
                        t0 = 0;
                    }
                }

                return true;
            }

            float3 LightDirToSourceVS(float3 samplePosVS)
            {
                // 0 = directional, 1 = point
                if (_LightType < 0.5)
                {
                    return normalize(_LightDirVS.xyz);
                }
                else
                {
                    return normalize(_LightPosVS.xyz - samplePosVS);
                }
            }

            // MARK: I have decided not to use jitter, until I find a good way to do it
            void RayMarchForward(float3 roVS, float3 rdVS, float t0, float t1, float jitter, out float3 volRgb, out float transparency)
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
                    float3 samplePosVS = roVS + t * rdVS;

                    // Attenuate volume object transparency by current sample transmission
                    float density = eval_density(samplePosVS, _SphereCenterVS.xyz, _SphereRadius);
                    // float density = EvalDensity(samplePosVS);
                    float sampleAtten = exp(-step * density * (_SigmaA + _SigmaS));
                    transparency *= sampleAtten;

                    // In-Scatterning
                    float lt0, lt1;
                    float3 lgtDirVS = LightDirToSourceVS(samplePosVS);
                    if (density > 0 && 
                        IntersectSphereVS(samplePosVS, lgtDirVS, _SphereCenterVS.xyz, _SphereRadius, lt0, lt1))
                    {
                        int numLightSteps = ceil(lt1 / step);
                        float lightStep = lt1 / numLightSteps;
                        float tau = 0;
                        // Ray-march along the light ray. Store the density values in the tau variable.
                        for (int nl = 0; nl < numLightSteps; ++nl) {
                            float tLight = lightStep * (nl + 0.5);
                            float3 lightSamplePos = samplePosVS + lgtDirVS * tLight;
                            tau += eval_density(lightSamplePos, _SphereCenterVS.xyz, _SphereRadius);
                        }

                        // Attenuate in-scattering contribution by the transmission of all samples accumulated so far
                        float lightAtten = exp(-tau * lightStep * (_SigmaA + _SigmaS));
                        volRgb += _LightColor.rgb *
                                  lightAtten * 
                                  PhaseHG(-rdVS, lgtDirVS, _PhaseG) *
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
                float3 roVS = float3(0,0,0);
                float3 rdVS = GetViewRayDir(i.uv);

                float t0, t1;
                if (!IntersectSphereVS(roVS, rdVS, _SphereCenterVS.xyz, _SphereRadius, t0, t1))
                    return float4(scene, 1);

                // If the sphere is behind the camera
                if (t1 <= 0.0)
                    return float4(scene, 1);

                // Clamp entry to camera near point
                t0 = max(t0, 0.0);

                // Fall back to original absorbtion only method
                if (_LightEnabled < 0.5) 
                {
                    float dist = max(0.0, t1 - t0);
                    float T = exp(-_SigmaA * dist);
                    float3 outRgb = scene * T + _ScatterColor.rgb * (1 - T);
                    return float4(outRgb, 1.0);
                }

                float3 volRgb;
                float transparency;
                // Jitter offset in [0, 1)
                float jitter = InterleavedGradientNoise(i.pos.xy);
                RayMarchForward(roVS, rdVS, t0, t1, jitter, volRgb, transparency);

                // Composite “volume over background”: background * transparency + result
                float3 outCol = scene * transparency + volRgb;
                return float4(outCol, 1.0);
            }
            ENDHLSL
        }   
    }
}
