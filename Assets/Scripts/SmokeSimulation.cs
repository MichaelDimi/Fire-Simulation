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

    [Header("Simulation")]
    [Range(0.9f, 1f)] public float dissipation = 0.98f;
    [Range(1f, 30f)] public float riseSpeed = 8f;     // voxels/sec
    [Range(0f, 2f)] public float noiseStrength = 1f;

    // public read-only access for the renderer
    public RenderTexture ActiveTexture => _read;

    // internals
    RenderTexture _read;
    RenderTexture _write;
    int _kernel;

    void OnEnable()
    {
        InitTextures();
        _kernel = computeShader.FindKernel("SimulateSmoke");
    }

    void OnDisable()
    {
        _read?.Release();
        _write?.Release();
    }

    void Update()
    {
        if (computeShader == null) return;
        Step(Time.deltaTime);
    }

    void Step(float dt)
    {
        Vector3 gridSize = new Vector3(resolution, resolution, resolution);
        Vector3 emitPosVoxel = emitPositionNormalized * (resolution - 1);

        computeShader.SetTexture(_kernel, "_Read", _read);
        computeShader.SetTexture(_kernel, "_Write", _write);
        computeShader.SetVector("_GridSize", gridSize);
        computeShader.SetFloat("_DeltaTime", dt);
        computeShader.SetFloat("_Dissipation", dissipation);
        computeShader.SetFloat("_EmitRate", emitRate);
        computeShader.SetVector("_EmitPos", emitPosVoxel);
        computeShader.SetFloat("_EmitRadius", emitRadius);
        computeShader.SetFloat("_RiseSpeed", riseSpeed);
        computeShader.SetFloat("_NoiseStrength", noiseStrength);
        computeShader.SetFloat("_Time", Time.time);

        int groups = Mathf.CeilToInt(resolution / 8f);
        computeShader.Dispatch(_kernel, groups, groups, groups);

        // Swap
        (_write, _read) = (_read, _write);
    }

    void InitTextures()
    {
        _read = CreateVolumeRT(resolution);
        _write = CreateVolumeRT(resolution);
    }

    static RenderTexture CreateVolumeRT(int res)
    {
        var rt = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
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


}
