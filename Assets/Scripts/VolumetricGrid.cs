using UnityEngine;

[ExecuteInEditMode]
public class VolumetricGrid : MonoBehaviour
{
    // Derived bounds (AABB) - Assumes cube for now
    public Vector3 BoundsMinWS => GetBoundsMinWS();
    public Vector3 BoundsMaxWS => GetBoundsMaxWS();

    public Vector3 BoundsSizeWS => BoundsMaxWS - BoundsMinWS;

    public int resolution = 8;

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

    // private void OnDrawGizmos()
    // {
    //     Gizmos.color = new Color(0.9f, 0.1f, 0.2f, 1f);
    //     Vector3 c = 0.5f * (BoundsMinWS + BoundsMaxWS);
    //     Vector3 s = BoundsMaxWS - BoundsMinWS;
    //     Gizmos.DrawWireCube(c, s);
    // }

    private void OnDrawGizmosSelected()
    {
        // Draw outer boundary
        Gizmos.color = new Color(0.9f, 0.1f, 0.2f, 1f);
        Vector3 c = 0.5f * (BoundsMinWS + BoundsMaxWS);
        Vector3 s = BoundsMaxWS - BoundsMinWS;
        Gizmos.DrawWireCube(c, s);

        // Draw each cell boundary box
        // DrawCellBoundaries();
    }

    private void DrawCellBoundaries()
    {
        Vector3 gridSize = BoundsSizeWS;
        Vector3 cellSize = gridSize / resolution;

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);

        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    Vector3 cellCenter = BoundsMinWS
                        + new Vector3(
                            (x + 0.5f) * cellSize.x,
                            (y + 0.5f) * cellSize.y,
                            (z + 0.5f) * cellSize.z
                        );
                    Gizmos.DrawWireCube(cellCenter, cellSize);
                }
            }
        }
    }
}
