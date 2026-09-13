using System.Collections.Generic;
using UnityEngine;

// A movable, editor-visible "ghost" of one recorded jump arc.
// Points are stored relative to this object's own transform, so moving (or rotating)
// the GameObject in the Scene view moves the whole arc with it. Bake one via
// JumpMetricsLogger's context menu, then drag the resulting prefab anywhere in
// the level to hold it up against greybox geometry.
public class JumpArcGhost : MonoBehaviour
{
    [SerializeField] List<Vector3> relativePoints = new List<Vector3>();
    [SerializeField] Color arcColor = Color.yellow;
    [SerializeField] float pointRadius = 0.05f;

    public void SetPoints(List<Vector3> worldPoints, Vector3 origin)
    {
        relativePoints.Clear();
        foreach (var p in worldPoints)
        {
            relativePoints.Add(p - origin);
        }
    }

    public void SetColor(Color color)
    {
        arcColor = color;
    }

    void OnDrawGizmos()
    {
        if (relativePoints == null || relativePoints.Count < 2) return;

        Gizmos.color = arcColor;
        for (int i = 0; i < relativePoints.Count - 1; i++)
        {
            Vector3 a = transform.TransformPoint(relativePoints[i]);
            Vector3 b = transform.TransformPoint(relativePoints[i + 1]);
            Gizmos.DrawLine(a, b);
        }

        Gizmos.DrawSphere(transform.TransformPoint(relativePoints[0]), pointRadius);
        Gizmos.DrawSphere(transform.TransformPoint(relativePoints[relativePoints.Count - 1]), pointRadius);
    }
}
