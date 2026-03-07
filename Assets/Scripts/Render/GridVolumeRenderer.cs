using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteInEditMode, ImageEffectAllowedInSceneView]
public class GridVolumeRenderer : MonoBehaviour
{
    const string headerDecoration = " --- ";
    [Header(headerDecoration + "Main" + headerDecoration)]
    public Shader shader;
    public VolumetricGrid grid;
    public SmokeSimulation simulation;
    public Light lightSource;

    [Header(headerDecoration + "Ray March" + headerDecoration)]
    public int numStepsLight = 8;

    [Header(headerDecoration + "Density" + headerDecoration)]
    public float densityMultiplier = 1;

    [Header(headerDecoration + "Lighting" + headerDecoration)]
    public float lightAbsorptionThroughCloud = 8f;
    public float lightAbsorptionTowardSun = 0.8f;
    [Range(0, 1)]
    public float darknessThreshold = .2f;
    [Range(0, 1)]
    public float forwardScattering = .83f;
    [Range(0, 1)]
    public float backScattering = .3f;
    [Range(0, 1)]
    public float baseBrightness = .8f;
    [Range(0, 1)]
    public float phaseFactor = .15f;

    // Internal
    [HideInInspector]
    public Material material;
    private Texture3D volumeTexture;

    void OnEnable()
    {
        GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
    }

    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {

        // Validate inputs
        if (material == null || material.shader != shader)
            material = new Material(shader);

        numStepsLight = Mathf.Max(1, numStepsLight);

        if (lightSource != null)
        {
            Vector3 dir = lightSource.transform.forward;  // direction FROM light
            material.SetVector("_CustomLightDir", new Vector4(-dir.x, -dir.y, -dir.z, 0));

            Color lc = lightSource.color * lightSource.intensity;
            material.SetVector("_CustomLightCol", new Vector4(lc.r, lc.g, lc.b, 1f));
        }

        if (volumeTexture != null)
            material.SetTexture("SmokeTex", volumeTexture);

        if (simulation != null && simulation.ActiveTexture != null)
            material.SetTexture("SmokeTex", simulation.ActiveTexture);

        material.SetFloat("densityMultiplier", densityMultiplier);
        material.SetFloat("lightAbsorptionThroughCloud", lightAbsorptionThroughCloud);
        material.SetFloat("lightAbsorptionTowardSun", lightAbsorptionTowardSun);
        material.SetFloat("darknessThreshold", darknessThreshold);
        material.SetVector("phaseParams", new Vector4(forwardScattering, backScattering, baseBrightness, phaseFactor));

        material.SetVector("boundsMin", grid.BoundsMinWS);
        material.SetVector("boundsMax", grid.BoundsMaxWS);

        material.SetInt("numStepsLight", numStepsLight);

        // Bit does the following:
        // - sets _MainTex property on material to the source texture
        // - sets the render target to the destination texture
        // - draws a full-screen quad
        // This copies the src texture to the dest texture, with whatever modifications the shader makes
        Graphics.Blit(src, dest, material);
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

        volumeTexture = new Texture3D(resolution, resolution, resolution, TextureFormat.RFloat, false);
        volumeTexture.wrapMode = TextureWrapMode.Clamp;
        volumeTexture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[numVoxels];
        for (int i = 0; i < numVoxels; i++)
            colors[i] = new Color(densityData[i], 0, 0, 0);

        volumeTexture.SetPixels(colors);
        volumeTexture.Apply();
    }

}
