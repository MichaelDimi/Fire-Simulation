using UnityEngine;

public class VolumetricSphere : MonoBehaviour
{

    [Header("Medium / Rendering")]
    public float sigmaA = 0.5f;
    public float sigmaS = 0.5f;
    public float density = 1f;
    [Range(-0.99f, 0.99f)]
    public float phaseG = 0.0f;
    public Color scatterColor = new Color(0.8f, 0.1f, 0.5f, 1f);

    public float RadiusWorld
    {
        get
        {
            var s = transform.lossyScale;
            return 0.5f * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }
    }
}
