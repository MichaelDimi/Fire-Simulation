using UnityEngine;

public class SmokeSimulation : MonoBehaviour
{
    [Header("Mode")]
    public bool enableSmoke = false;

    [Header("Compute")]
    public ComputeShader computeShader;
    public int resolution = 64;

    [Header("Emission")]
    public Vector3 emitPositionNormalized = new Vector3(0.5f, 0.05f, 0.5f); // [0,1] within grid
    public float emitRadius = 3f; // radius of the floor emitter patch, in voxels
    [Min(0.5f)] public float emitThickness = 2f; // thin vertical thickness so the source reads like fire on the floor

    [Header("Density")]
    [Range(0.9f, 1f)] public float dissipation = 0.98f;

    [Header("Temperature")]
    [Range(0.8f, 1f)] public float temperatureDissipation = 0.985f; // ambient cooling
    [Range(0f, 20f)] public float temperatureEmitRate = 6f; // pilot heat that keeps the fuel igniting
    [Range(1f, 1000f)] public float temperatureMax = 4f; // keeps temperature from clipping to [0,1] while still bounded

    [Header("Fuel")]
    [Range(0.8f, 1f)] public float fuelDissipation = 0.995f;
    [Range(0f, 100f)] public float fuelEmitRate = 25f;

    [Header("Combustion")]
    [Range(0f, 1f)] public float ignitionTemperature = 0.22f;
    [Range(0f, 20f)] public float burnRate = 7f;
    [Range(0f, 10f)] public float heatRelease = 1.8f;
    [Range(0f, 10f)] public float smokeYield = 0.22f;
    [Range(0.5f, 1f)] public float reactionDissipation = 0.88f;
    [HideInInspector][Range(0f, 20f)] public float reactionGain = 8f;

    [Header("Smoke Appearance")]
    [Range(0f, 1f)] public float smokeClearTemperature = 0.4f; // above this, dark smoke starts being suppressed
    [Range(0.01f, 1f)] public float smokeSuppressionRange = 0.2f; // width of the hot-to-cool transition
    [Range(0f, 20f)] public float smokeOxidationRate = 4f; // burns off smoke that enters the luminous core
    [Range(0f, 20f)] public float postCombustionSmokeRate = 2f; // converts cooling reaction remnants into smoke

    [Header("Velocity Field")]
    [Range(1f, 1000f)] public float buoyancyStrength = 10f; // how hard temperature pushes fluid upward (voxels/sec)
    [Range(0.9f, 1f)] public float velocityDissipation = 0.995f; // velocity drag — lower = more viscous
    [Range(0f, 100f)] public float noiseStrength = 0.3f; // seed turbulence near emitter

    [Header("Turbulence")]
    [Range(0f, 20f)] public float vorticityStrength = 2.5f; // boosts existing swirl into visible eddies
    [Range(1, 64)] public int pressureIterations = 24; // Jacobi iterations for incompressibility

    [Range(1, 12)] public int maxSimulationSubsteps = 6;
    [Range(0.001f, 0.05f)] public float maxSimulationStep = 1f / 30f; // split large frame deltas to avoid ignition/extinction pulsing

    public RenderTexture ActiveTexture => _densityRead;
    public RenderTexture ActiveDensityTexture => _densityRead;
    public RenderTexture ActiveTemperatureTexture => _temperatureRead;
    public RenderTexture ActiveFuelTexture => _fuelRead;
    public RenderTexture ActiveReactionTexture => _reactionRead;

    RenderTexture _densityRead;
    RenderTexture _densityWrite;
    RenderTexture _temperatureRead;
    RenderTexture _temperatureWrite;
    RenderTexture _fuelRead;
    RenderTexture _fuelWrite;
    RenderTexture _reactionRead;
    RenderTexture _reactionWrite;
    RenderTexture _velocityRead;
    RenderTexture _velocityWrite;
    RenderTexture _divergence;
    RenderTexture _pressureRead;
    RenderTexture _pressureWrite;

    int _kernelAdvectVelocity;
    int _kernelVorticityConfinement;
    int _kernelComputeDivergence;
    int _kernelClearPressure;
    int _kernelJacobiPressure;
    int _kernelProjectVelocity;
    int _kernelAdvectFuel;
    int _kernelAdvectDensity;
    int _kernelAdvectTemperature;
    int _kernelAdvectReaction;
    int _kernelCombustion;
    int _kernelOpenTopOutflow;

    void OnEnable()
    {
        if (computeShader == null)
            return;

        InitTextures();
        CacheKernels();
    }

    void OnDisable()
    {
        ReleaseTextures();
    }

    void Update()
    {
        if (computeShader == null)
            return;

        if (_densityRead == null || _temperatureRead == null || _fuelRead == null || _reactionRead == null || _velocityRead == null)
        {
            InitTextures();
            CacheKernels();
        }

        SimulateFrame(Time.deltaTime);
    }

    void SimulateFrame(float dt)
    {
        float remainingTime = Mathf.Max(0f, dt);
        int maxSteps = Mathf.Max(1, maxSimulationSubsteps);
        float stepLimit = Mathf.Max(0.001f, maxSimulationStep);

        int stepCount = Mathf.Clamp(Mathf.CeilToInt(remainingTime / stepLimit), 1, maxSteps);
        float stepDt = remainingTime / stepCount;

        for (int step = 0; step < stepCount; step++)
            Step(stepDt);
    }

    void Step(float dt)
    {
        int groups = Mathf.CeilToInt(resolution / 8f);
        Vector3 gridSize = new Vector3(resolution, resolution, resolution);
        Vector3 emitPosVox = emitPositionNormalized * (resolution - 1);

        computeShader.SetVector("_GridSize", gridSize);
        computeShader.SetFloat("_DeltaTime", dt);
        computeShader.SetFloat("_Time", Time.time);
        computeShader.SetVector("_EmitPos", emitPosVox);
        computeShader.SetFloat("_EmitRadius", emitRadius);
        computeShader.SetFloat("_EmitThickness", emitThickness);
        computeShader.SetFloat("_SmokeEnabled", enableSmoke ? 1f : 0f);
        computeShader.SetFloat("_TemperatureMax", temperatureMax);

        computeShader.SetTexture(_kernelAdvectVelocity, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectVelocity, "_VelocityWrite", _velocityWrite);
        computeShader.SetTexture(_kernelAdvectVelocity, "_TemperatureRead", _temperatureRead);
        computeShader.SetFloat("_BuoyancyStrength", buoyancyStrength);
        computeShader.SetFloat("_VelocityDissipation", velocityDissipation);
        computeShader.SetFloat("_NoiseStrength", noiseStrength);

        computeShader.Dispatch(_kernelAdvectVelocity, groups, groups, groups);
        SwapTextures(ref _velocityRead, ref _velocityWrite);

        computeShader.SetTexture(_kernelVorticityConfinement, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelVorticityConfinement, "_VelocityWrite", _velocityWrite);
        computeShader.SetFloat("_VorticityStrength", vorticityStrength);

        computeShader.Dispatch(_kernelVorticityConfinement, groups, groups, groups);
        SwapTextures(ref _velocityRead, ref _velocityWrite);

        computeShader.SetTexture(_kernelComputeDivergence, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelComputeDivergence, "_DivergenceWrite", _divergence);

        computeShader.Dispatch(_kernelComputeDivergence, groups, groups, groups);

        computeShader.SetTexture(_kernelClearPressure, "_PressureWrite", _pressureWrite);
        computeShader.Dispatch(_kernelClearPressure, groups, groups, groups);
        SwapTextures(ref _pressureRead, ref _pressureWrite);

        int pressureSteps = Mathf.Max(1, pressureIterations);
        for (int i = 0; i < pressureSteps; i++)
        {
            computeShader.SetTexture(_kernelJacobiPressure, "_DivergenceRead", _divergence);
            computeShader.SetTexture(_kernelJacobiPressure, "_PressureRead", _pressureRead);
            computeShader.SetTexture(_kernelJacobiPressure, "_PressureWrite", _pressureWrite);

            computeShader.Dispatch(_kernelJacobiPressure, groups, groups, groups);
            SwapTextures(ref _pressureRead, ref _pressureWrite);
        }

        computeShader.SetTexture(_kernelProjectVelocity, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelProjectVelocity, "_PressureRead", _pressureRead);
        computeShader.SetTexture(_kernelProjectVelocity, "_VelocityWrite", _velocityWrite);

        computeShader.Dispatch(_kernelProjectVelocity, groups, groups, groups);
        SwapTextures(ref _velocityRead, ref _velocityWrite);

        computeShader.SetTexture(_kernelAdvectFuel, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectFuel, "_FuelRead", _fuelRead);
        computeShader.SetTexture(_kernelAdvectFuel, "_FuelWrite", _fuelWrite);
        computeShader.SetFloat("_FuelDissipation", fuelDissipation);
        computeShader.SetFloat("_FuelEmitRate", fuelEmitRate);

        computeShader.Dispatch(_kernelAdvectFuel, groups, groups, groups);
        SwapTextures(ref _fuelRead, ref _fuelWrite);

        if (enableSmoke)
        {
            computeShader.SetTexture(_kernelAdvectDensity, "_VelocityRead", _velocityRead);
            computeShader.SetTexture(_kernelAdvectDensity, "_DensityRead", _densityRead);
            computeShader.SetTexture(_kernelAdvectDensity, "_DensityWrite", _densityWrite);
            computeShader.SetFloat("_Dissipation", dissipation);

            computeShader.Dispatch(_kernelAdvectDensity, groups, groups, groups);
            SwapTextures(ref _densityRead, ref _densityWrite);
        }

        computeShader.SetTexture(_kernelAdvectTemperature, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectTemperature, "_TemperatureRead", _temperatureRead);
        computeShader.SetTexture(_kernelAdvectTemperature, "_TemperatureWrite", _temperatureWrite);
        computeShader.SetFloat("_TemperatureDissipation", temperatureDissipation);
        computeShader.SetFloat("_TemperatureEmitRate", temperatureEmitRate);

        computeShader.Dispatch(_kernelAdvectTemperature, groups, groups, groups);
        SwapTextures(ref _temperatureRead, ref _temperatureWrite);

        computeShader.SetTexture(_kernelAdvectReaction, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelAdvectReaction, "_ReactionRead", _reactionRead);
        computeShader.SetTexture(_kernelAdvectReaction, "_ReactionWrite", _reactionWrite);
        computeShader.SetFloat("_ReactionDissipation", reactionDissipation);

        computeShader.Dispatch(_kernelAdvectReaction, groups, groups, groups);
        SwapTextures(ref _reactionRead, ref _reactionWrite);

        computeShader.SetTexture(_kernelCombustion, "_FuelRead", _fuelRead);
        computeShader.SetTexture(_kernelCombustion, "_FuelWrite", _fuelWrite);
        computeShader.SetTexture(_kernelCombustion, "_DensityRead", _densityRead);
        computeShader.SetTexture(_kernelCombustion, "_DensityWrite", _densityWrite);
        computeShader.SetTexture(_kernelCombustion, "_TemperatureRead", _temperatureRead);
        computeShader.SetTexture(_kernelCombustion, "_TemperatureWrite", _temperatureWrite);
        computeShader.SetTexture(_kernelCombustion, "_ReactionRead", _reactionRead);
        computeShader.SetTexture(_kernelCombustion, "_ReactionWrite", _reactionWrite);
        computeShader.SetFloat("_IgnitionTemperature", ignitionTemperature);
        computeShader.SetFloat("_BurnRate", burnRate);
        computeShader.SetFloat("_HeatRelease", heatRelease);
        computeShader.SetFloat("_SmokeYield", smokeYield);
        computeShader.SetFloat("_ReactionGain", reactionGain);
        computeShader.SetFloat("_SmokeClearTemperature", smokeClearTemperature);
        computeShader.SetFloat("_SmokeSuppressionRange", smokeSuppressionRange);
        computeShader.SetFloat("_SmokeOxidationRate", smokeOxidationRate);
        computeShader.SetFloat("_PostCombustionSmokeRate", postCombustionSmokeRate);

        computeShader.Dispatch(_kernelCombustion, groups, groups, groups);
        SwapTextures(ref _fuelRead, ref _fuelWrite);
        SwapTextures(ref _densityRead, ref _densityWrite);
        SwapTextures(ref _temperatureRead, ref _temperatureWrite);
        SwapTextures(ref _reactionRead, ref _reactionWrite);

        computeShader.SetTexture(_kernelOpenTopOutflow, "_FuelRead", _fuelRead);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_FuelWrite", _fuelWrite);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_DensityRead", _densityRead);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_DensityWrite", _densityWrite);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_TemperatureRead", _temperatureRead);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_TemperatureWrite", _temperatureWrite);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_ReactionRead", _reactionRead);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_ReactionWrite", _reactionWrite);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_VelocityRead", _velocityRead);
        computeShader.SetTexture(_kernelOpenTopOutflow, "_VelocityWrite", _velocityWrite);

        computeShader.Dispatch(_kernelOpenTopOutflow, groups, groups, groups);

        SwapTextures(ref _fuelRead, ref _fuelWrite);
        SwapTextures(ref _densityRead, ref _densityWrite);
        SwapTextures(ref _temperatureRead, ref _temperatureWrite);
        SwapTextures(ref _reactionRead, ref _reactionWrite);
        SwapTextures(ref _velocityRead, ref _velocityWrite);
    }

    void CacheKernels()
    {
        _kernelAdvectVelocity = computeShader.FindKernel("AdvectVelocity");
        _kernelVorticityConfinement = computeShader.FindKernel("VorticityConfinement");
        _kernelComputeDivergence = computeShader.FindKernel("ComputeDivergence");
        _kernelClearPressure = computeShader.FindKernel("ClearPressure");
        _kernelJacobiPressure = computeShader.FindKernel("JacobiPressure");
        _kernelProjectVelocity = computeShader.FindKernel("ProjectVelocity");
        _kernelAdvectFuel = computeShader.FindKernel("AdvectFuel");
        _kernelAdvectDensity = computeShader.FindKernel("AdvectDensity");
        _kernelAdvectTemperature = computeShader.FindKernel("AdvectTemperature");
        _kernelAdvectReaction = computeShader.FindKernel("AdvectReaction");
        _kernelCombustion = computeShader.FindKernel("Combustion");
        _kernelOpenTopOutflow = computeShader.FindKernel("OpenTopOutflow");
    }

    void InitTextures()
    {
        ReleaseTextures();

        _densityRead = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _densityWrite = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _temperatureRead = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _temperatureWrite = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _fuelRead = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _fuelWrite = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _reactionRead = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _reactionWrite = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _velocityRead = CreateVolumeRt(resolution, RenderTextureFormat.ARGBFloat);
        _velocityWrite = CreateVolumeRt(resolution, RenderTextureFormat.ARGBFloat);
        _divergence = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _pressureRead = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
        _pressureWrite = CreateVolumeRt(resolution, RenderTextureFormat.RFloat);
    }

    void ReleaseTextures()
    {
        ReleaseTexture(ref _densityRead);
        ReleaseTexture(ref _densityWrite);
        ReleaseTexture(ref _temperatureRead);
        ReleaseTexture(ref _temperatureWrite);
        ReleaseTexture(ref _fuelRead);
        ReleaseTexture(ref _fuelWrite);
        ReleaseTexture(ref _reactionRead);
        ReleaseTexture(ref _reactionWrite);
        ReleaseTexture(ref _velocityRead);
        ReleaseTexture(ref _velocityWrite);
        ReleaseTexture(ref _divergence);
        ReleaseTexture(ref _pressureRead);
        ReleaseTexture(ref _pressureWrite);
    }

    static RenderTexture CreateVolumeRt(int res, RenderTextureFormat format)
    {
        var rt = new RenderTexture(res, res, 0, format)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = res,
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();
        return rt;
    }

    static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        texture = null;
    }

    static void SwapTextures(ref RenderTexture a, ref RenderTexture b)
    {
        (b, a) = (a, b);
    }
}
