using UnityEngine;

[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class GridVolumeRenderer : MonoBehaviour
{
    const string HeaderDecoration = " --- ";

    [Header(HeaderDecoration + "Main" + HeaderDecoration)]
    public Shader fireShader;
    public Shader smokeShader;
    public VolumetricGrid grid;
    public SmokeSimulation simulation;
    public Light lightSource;

    [Header(HeaderDecoration + "Fire" + HeaderDecoration)]
    [Range(0f, 10f)] public float fireEmissionStrength = 2.5f;
    [Range(0f, 1f)] public float fireTemperatureThreshold = 0.15f;
    [Range(0.2f, 10f)] public float fireTemperatureMax = 2.5f;
    [Range(0f, 4f)] public float fireOpacity = 1.1f;
    [Min(0.01f)] public float fireStepSize = 0.1f;
    [Range(0.05f, 0.95f)] public float fireColorMidPoint = 0.35f;
    [Range(0.1f, 1f)] public float fireColorHighPoint = 0.8f;
    [Min(0.1f)] public float fireDetailScale = 3f;
    [Range(0f, 1f)] public float fireDetailStrength = 0.72f;
    [Range(0f, 10f)] public float fireFlickerSpeed = 1.5f;
    public Color fireColorLow = new Color(0.9f, 0.12f, 0.02f, 1f);
    public Color fireColorMid = new Color(1f, 0.45f, 0.05f, 1f);
    public Color fireColorHigh = new Color(1f, 0.95f, 0.75f, 1f);

    [Header(HeaderDecoration + "Smoke" + HeaderDecoration)]
    public float densityMultiplier = 1f;
    [Min(0.01f)] public float smokeStepSize = 0.1f;

    [Header(HeaderDecoration + "Directional Lighting" + HeaderDecoration)]
    public int numStepsLight = 8;
    public float lightAbsorptionThroughCloud = 8f;
    public float lightAbsorptionTowardSun = 0.8f;
    [Range(0, 1)] public float darknessThreshold = 0.2f;
    [Range(0, 1)] public float forwardScattering = 0.83f;
    [Range(0, 1)] public float backScattering = 0.3f;
    [Range(0, 1)] public float baseBrightness = 0.8f;
    [Range(0, 1)] public float phaseFactor = 0.15f;

    [Header(HeaderDecoration + "Fire Lighting For Smoke" + HeaderDecoration)]
    [Range(1, 16)] public int numStepsFireLight = 8;
    [Range(0f, 10f)] public float fireLightStrength = 0.9f;
    [Range(0.1f, 20f)] public float fireLightRange = 18f;
    [Range(0f, 10f)] public float fireLightAbsorption = 1f;

    Texture3D _volumeTexture;
    Material _fireMaterial;
    Material _smokeMaterial;
    RenderTexture _intermediateRt;

    void OnEnable()
    {
        GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
    }

    void OnValidate()
    {
        numStepsLight = Mathf.Max(1, numStepsLight);
        numStepsFireLight = Mathf.Max(1, numStepsFireLight);
        fireStepSize = Mathf.Max(0.01f, fireStepSize);
        smokeStepSize = Mathf.Max(0.01f, smokeStepSize);
        fireTemperatureMax = Mathf.Max(fireTemperatureThreshold + 0.01f, fireTemperatureMax);
        fireColorMidPoint = Mathf.Clamp(fireColorMidPoint, 0.05f, 0.95f);
        fireColorHighPoint = Mathf.Clamp(fireColorHighPoint, fireColorMidPoint + 0.01f, 1f);
        ResolveSimulation();
    }

    void OnDisable()
    {
        ReleaseIntermediate();
        ReleaseMaterial(ref _fireMaterial);
        ReleaseMaterial(ref _smokeMaterial);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (grid == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        SmokeSimulation sim = ResolveSimulation();

        numStepsLight = Mathf.Max(1, numStepsLight);
        numStepsFireLight = Mathf.Max(1, numStepsFireLight);
        fireStepSize = Mathf.Max(0.01f, fireStepSize);
        smokeStepSize = Mathf.Max(0.01f, smokeStepSize);

        bool hasDensity = (sim != null && sim.ActiveDensityTexture != null) || _volumeTexture != null;
        bool smokeEnabled = densityMultiplier > 0.0001f;
        bool hasFire = sim != null &&
                       sim.ActiveTemperatureTexture != null &&
                       sim.ActiveReactionTexture != null;

        bool renderedFire = false;
        if (hasFire && EnsureMaterial(fireShader, ref _fireMaterial))
        {
            EnsureIntermediate(src);
            ConfigureFireMaterial(_fireMaterial, sim);
            Graphics.Blit(src, _intermediateRt, _fireMaterial);
            renderedFire = true;
        }

        if (smokeEnabled && hasDensity && EnsureMaterial(smokeShader, ref _smokeMaterial))
        {
            ConfigureSmokeMaterial(_smokeMaterial, sim);
            Graphics.Blit(renderedFire ? _intermediateRt : src, dest, _smokeMaterial);
            return;
        }

        if (renderedFire)
        {
            Graphics.Blit(_intermediateRt, dest);
            return;
        }

        Graphics.Blit(src, dest);
    }

    Vector3 GridNormalizedToWorld(Vector3 normalizedPosition)
    {
        return grid.BoundsMinWS + Vector3.Scale(grid.BoundsSizeWS, normalizedPosition);
    }

    SmokeSimulation ResolveSimulation()
    {
        if (simulation != null)
            return simulation;

        if (grid != null && grid.TryGetComponent(out SmokeSimulation resolved))
        {
            simulation = resolved;
            return resolved;
        }

        simulation = null;
        return null;
    }

    bool EnsureMaterial(Shader shader, ref Material material)
    {
        if (shader == null)
        {
            ReleaseMaterial(ref material);
            return false;
        }

        if (material != null && material.shader == shader)
            return true;

        ReleaseMaterial(ref material);
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    void EnsureIntermediate(RenderTexture src)
    {
        if (_intermediateRt != null &&
            _intermediateRt.width == src.width &&
            _intermediateRt.height == src.height &&
            _intermediateRt.format == src.format)
        {
            return;
        }

        ReleaseIntermediate();

        var descriptor = src.descriptor;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;

        _intermediateRt = new RenderTexture(descriptor)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        _intermediateRt.Create();
    }

    void ConfigureFireMaterial(Material material, SmokeSimulation sim)
    {
        PushSharedVolumeParams(material, sim);
        material.SetFloat("fireEmissionStrength", fireEmissionStrength);
        material.SetFloat("fireTemperatureThreshold", fireTemperatureThreshold);
        material.SetFloat("fireTemperatureMax", fireTemperatureMax);
        material.SetFloat("fireOpacity", fireOpacity);
        material.SetFloat("fireStepSize", fireStepSize);
        material.SetFloat("fireColorMidPoint", fireColorMidPoint);
        material.SetFloat("fireColorHighPoint", fireColorHighPoint);
        material.SetFloat("fireDetailScale", fireDetailScale);
        material.SetFloat("fireDetailStrength", fireDetailStrength);
        material.SetFloat("fireFlickerSpeed", fireFlickerSpeed);
        material.SetVector("fireColorLow", fireColorLow);
        material.SetVector("fireColorMid", fireColorMid);
        material.SetVector("fireColorHigh", fireColorHigh);
    }

    void ConfigureSmokeMaterial(Material material, SmokeSimulation sim)
    {
        PushSharedVolumeParams(material, sim);
        PushDirectionalLightParams(material);

        material.SetFloat("densityMultiplier", densityMultiplier);
        material.SetFloat("smokeStepSize", smokeStepSize);
        material.SetFloat("lightAbsorptionThroughCloud", lightAbsorptionThroughCloud);
        material.SetFloat("lightAbsorptionTowardSun", lightAbsorptionTowardSun);
        material.SetFloat("darknessThreshold", darknessThreshold);
        material.SetVector("phaseParams", new Vector4(forwardScattering, backScattering, baseBrightness, phaseFactor));
        material.SetInt("numStepsLight", numStepsLight);

        material.SetFloat("fireEmissionStrength", fireEmissionStrength);
        material.SetFloat("fireTemperatureThreshold", fireTemperatureThreshold);
        material.SetFloat("fireTemperatureMax", fireTemperatureMax);
        material.SetFloat("fireColorMidPoint", fireColorMidPoint);
        material.SetFloat("fireColorHighPoint", fireColorHighPoint);
        material.SetInt("numStepsFireLight", numStepsFireLight);
        material.SetFloat("fireLightStrength", fireLightStrength);
        material.SetFloat("fireLightRange", fireLightRange);
        material.SetFloat("fireLightAbsorption", fireLightAbsorption);
        material.SetVector("fireColorLow", fireColorLow);
        material.SetVector("fireColorMid", fireColorMid);
        material.SetVector("fireColorHigh", fireColorHigh);
        material.SetVector("_EmitterPosWS", GridNormalizedToWorld(sim != null ? sim.emitPositionNormalized : new Vector3(0.5f, 0.05f, 0.5f)));
    }

    void PushSharedVolumeParams(Material material, SmokeSimulation sim)
    {
        if (_volumeTexture != null)
            material.SetTexture("SmokeTex", _volumeTexture);

        if (sim != null)
        {
            if (sim.ActiveDensityTexture != null)
                material.SetTexture("SmokeTex", sim.ActiveDensityTexture);

            if (sim.ActiveTemperatureTexture != null)
                material.SetTexture("TemperatureTex", sim.ActiveTemperatureTexture);

            if (sim.ActiveReactionTexture != null)
                material.SetTexture("ReactionTex", sim.ActiveReactionTexture);
        }

        material.SetVector("boundsMin", grid.BoundsMinWS);
        material.SetVector("boundsMax", grid.BoundsMaxWS);
    }

    void PushDirectionalLightParams(Material material)
    {
        if (lightSource != null)
        {
            Vector3 dir = lightSource.transform.forward;
            material.SetVector("_CustomLightDir", new Vector4(-dir.x, -dir.y, -dir.z, 0f));

            Color lightColor = lightSource.color * lightSource.intensity;
            material.SetVector("_CustomLightCol", new Vector4(lightColor.r, lightColor.g, lightColor.b, 1f));
            return;
        }

        material.SetVector("_CustomLightDir", new Vector4(0f, -1f, 0f, 0f));
        material.SetVector("_CustomLightCol", Vector4.zero);
    }

    void ReleaseIntermediate()
    {
        if (_intermediateRt == null)
            return;

        _intermediateRt.Release();
        DestroyObject(_intermediateRt);
        _intermediateRt = null;
    }

    static void ReleaseMaterial(ref Material material)
    {
        if (material == null)
            return;

        DestroyObject(material);
        material = null;
    }

    static void DestroyObject(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    // MARK: Unused - do not remove though - good for demos / comparison
    //       - Use: LoadVolumeData("grid.100.bin");
    void LoadVolumeData(string filename)
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);
        byte[] bytes = System.IO.File.ReadAllBytes(path);

        int resolution = 128;
        int numVoxels = resolution * resolution * resolution;
        float[] densityData = new float[numVoxels];
        System.Buffer.BlockCopy(bytes, 0, densityData, 0, bytes.Length);

        _volumeTexture = new Texture3D(resolution, resolution, resolution, TextureFormat.RFloat, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] colors = new Color[numVoxels];
        for (int i = 0; i < numVoxels; i++)
            colors[i] = new Color(densityData[i], 0f, 0f, 0f);

        _volumeTexture.SetPixels(colors);
        _volumeTexture.Apply();
    }
}
