using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SphereVolumeRenderer : MonoBehaviour
{

    [Header("Renderer")]
    [SerializeField] private Shader volumetricShader;

    private Material mat;
    private Camera cam;

    [Header("Volumes")]
    public VolumetricSphere sphere;

    [Header("Lighting (for ray marching)")]
    public Light lightSource;
    [Min(0.1f)] public float stepSize = 0.2f;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        EnsureMaterial();
    }

    void OnDisable()
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }

    void OnValidate()
    {
        // Called when you change inspector values
        if (!isActiveAndEnabled) return;
        EnsureMaterial();
    }

    private void EnsureMaterial()
    {
        if (volumetricShader == null) return;

        if (mat == null || mat.shader != volumetricShader)
        {
            if (mat != null) DestroyImmediate(mat);
            mat = new Material(volumetricShader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        EnsureMaterial();
        if (mat == null || sphere == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        PushSphereParams(mat, sphere);
        PushLightParams(mat, lightSource);
        Graphics.Blit(src, dest, mat);
    }

    private void PushSphereParams(Material mat, VolumetricSphere s)
    {
        Vector3 centerVS = cam.worldToCameraMatrix.MultiplyPoint(s.transform.position);
        mat.SetVector("_SphereCenterVS", new Vector4(centerVS.x, centerVS.y, centerVS.z, 1f));
        mat.SetFloat("_SphereRadius", s.RadiusWorld);
        mat.SetFloat("_SigmaA", s.sigmaA);
        mat.SetFloat("_SigmaS", s.sigmaS);
        mat.SetFloat("_Density", s.density);
        mat.SetFloat("_PhaseG", s.phaseG);
        mat.SetColor("_ScatterColor", s.scatterColor);

        mat.SetFloat("_StepSize", stepSize);

        mat.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
    }

    private void PushLightParams(Material mat, Light l)
    {
        Color lc = l.color * l.intensity;
        mat.SetVector("_LightColor", new Vector4(lc.r, lc.g, lc.b, 1f));

        if (l.type == LightType.Directional)
        {
            // Direction *toward the light source*
            Vector3 dirToLightWS = -l.transform.forward;
            Vector3 dirToLightVS = cam.worldToCameraMatrix.MultiplyVector(dirToLightWS).normalized;

            mat.SetFloat("_LightType", 0f); // 0 = directional
            mat.SetVector("_LightDirVS", new Vector4(dirToLightVS.x, dirToLightVS.y, dirToLightVS.z, 0f));
        }
        else
        {
            Vector3 posVS = cam.worldToCameraMatrix.MultiplyPoint(l.transform.position);

            mat.SetFloat("_LightType", 1f); // 1 = point
            mat.SetVector("_LightPosVS", new Vector4(posVS.x, posVS.y, posVS.z, 1f));
        }
    }
}
