using UnityEngine;

public class SmokeSimulation : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader computeShader;
    public int resolution = 64;

    [Header("Emission")]
    public Vector3 emitPositionNormalized = new Vector3(0.5f, 0.05f, 0.5f); // [0,1] within grid
    public float emitRadius = 3f;    // in voxels
    public float emitRate = 5f;    // density units/sec

    [Header("Density")]
    [Range(0.9f, 1f)] public float dissipation = 0.98f;

    [Header("Velocity Field")]
    [Range(1f, 1000f)] public float buoyancyStrength = 10f; // how hard density pushes fluid upward (voxels/sec)
    [Range(0.9f, 1f)] public float velocityDissipation = 0.995f; // velocity drag — lower = more viscous
    [Range(0f, 100f)] public float noiseStrength = 0.3f; // Seed turbulence near emitter

    // public read-only access for the renderer
    public RenderTexture ActiveTexture => _densityRead;

    // internals
    RenderTexture _densityRead, _densityWrite;
    RenderTexture _velocityRead, _velocityWrite;

    int _kernelAdvectVelocity;
    int _kernelAdvectDensity;

    void OnEnable()
    {
        InitTextures();
        _kernelAdvectVelocity = computeShader.FindKernel("AdvectVelocity");
        _kernelAdvectDensity = computeShader.FindKernel("AdvectDensity");
    }

    void OnDisable()
    {
        _densityRead?.Release();
        _densityWrite?.Release();
        _velocityRead?.Release();
        _velocityWrite?.Release();
    }

    void Update()
    {
        if (computeShader == null) return;
        Step(Time.deltaTime);
    }

    void Step(float dt)
    {
        int groups = Mathf.CeilToInt(resolution / 8f);
        Vector3 gridSize = new Vector3(resolution, resolution, resolution);
        Vector3 emitPosVox = emitPositionNormalized * (resolution - 1);

        // Shared params (both kernels need these)
        computeShader.SetVector("_GridSize", gridSize);
        computeShader.SetFloat("_DeltaTime", dt);
        computeShader.SetFloat("_Time", Time.time);
        computeShader.SetVector("_EmitPos", emitPosVox);
        computeShader.SetFloat("_EmitRadius", emitRadius);

        // ── Kernel 1: Advect Velocity ────────────────────────────────────────
        computeShader.SetTexture(_kernelAdvectVelocity, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectVelocity, "_VelocityWrite", _velocityWrite);
        computeShader.SetTexture(_kernelAdvectVelocity, "_DensityRead", _densityRead);
        computeShader.SetFloat("_BuoyancyStrength", buoyancyStrength);
        computeShader.SetFloat("_VelocityDissipation", velocityDissipation);
        computeShader.SetFloat("_NoiseStrength", noiseStrength);

        computeShader.Dispatch(_kernelAdvectVelocity, groups, groups, groups);
        SwapTextures(ref _velocityRead, ref _velocityWrite);

        // ── Kernel 2: Advect Density ─────────────────────────────────────────
        computeShader.SetTexture(_kernelAdvectDensity, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectDensity, "_DensityRead", _densityRead);
        computeShader.SetTexture(_kernelAdvectDensity, "_DensityWrite", _densityWrite);
        computeShader.SetFloat("_Dissipation", dissipation);
        computeShader.SetFloat("_EmitRate", emitRate);

        computeShader.Dispatch(_kernelAdvectDensity, groups, groups, groups);
        SwapTextures(ref _densityRead, ref _densityWrite);
    }

    void InitTextures()
    {
        _densityRead = CreateVolumeRT(resolution, RenderTextureFormat.RFloat);
        _densityWrite = CreateVolumeRT(resolution, RenderTextureFormat.RFloat);
        _velocityRead = CreateVolumeRT(resolution, RenderTextureFormat.ARGBFloat);
        _velocityWrite = CreateVolumeRT(resolution, RenderTextureFormat.ARGBFloat);
    }

    static RenderTexture CreateVolumeRT(int res, RenderTextureFormat format)
    {
        var rt = new RenderTexture(res, res, 0, format)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = res,
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,

        };
        rt.Create();
        return rt;
    }

    static void SwapTextures(ref RenderTexture a, ref RenderTexture b)
    {
        (b, a) = (a, b);
    }
}
