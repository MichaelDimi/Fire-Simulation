using UnityEngine;

public class VolumetricGrid : MonoBehaviour
{
    // Derived bounds (AABB) - Assumes cube for now
    public Vector3 BoundsMinWS => GetBoundsMinWS();
    public Vector3 BoundsMaxWS => GetBoundsMaxWS();

    public Vector3 BoundsSizeWS => BoundsMaxWS - BoundsMinWS;

    private Vector3 GetBoundsMinWS()
    {
        // Treat transform as an axis-aligned box (rotation ignored).
        // Use lossyScale so you can size the cube in the editor.
        Vector3 half = 0.5f * AbsVec3(transform.lossyScale);
        return transform.position - half;
    }

    private Vector3 GetBoundsMaxWS()
    {
        Vector3 half = 0.5f * AbsVec3(transform.lossyScale);
        return transform.position + half;
    }

    private static Vector3 AbsVec3(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.1f, 0.2f, 1f);
        Vector3 c = 0.5f * (BoundsMinWS + BoundsMaxWS);
        Vector3 s = BoundsMaxWS - BoundsMinWS;
        Gizmos.DrawWireCube(c, s);
    }
}
