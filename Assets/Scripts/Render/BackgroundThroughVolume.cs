using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BackgroundThroughVolume : MonoBehaviour
{
    [Header("Background and Atmospheric Volume params")]
    public Color backgroundColor = new Color(0.572f, 0.772f, 0.921f, 1.0f); // ambient "sky" color
    public Color volumeColor = new Color(1.0f, 1.0f, 1.0f, 1.0f); // color of the volume (fog, smoke, etc.)
    public float sigmaA = 0.0f;   // absorption coefficient
    public float distance = 10f;  // distance through volume

    Camera cam;

    void Start()
    {
        // Double check that the camera component is present and set clear flags to solid color
        cam = GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    void Update()
    {
        float T = Mathf.Exp(-distance * sigmaA);
        Color background_color_through_volume = T * backgroundColor + (1f - T) * volumeColor;
        cam.backgroundColor = background_color_through_volume;
    }
}
