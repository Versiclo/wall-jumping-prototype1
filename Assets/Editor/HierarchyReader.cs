using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Text;
using System.IO;

public class HierarchyReader
{
    [MenuItem("Tools/Export Hierarchy")]
    static void ExportHierarchy()
    {
        var sb = new StringBuilder();
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            Walk(root.transform, 0, sb);
        File.WriteAllText("hierarchy_export.txt", sb.ToString());
        Debug.Log("Hierarchy exported to: " + Path.GetFullPath("hierarchy_export.txt"));
    }

    static void Walk(Transform t, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        string componentList = string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name));

        Vector3 pos = t.position;
        Vector3 scale = t.lossyScale;
        string line = $"{indent}{t.name} [{componentList}] pos=({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) scale=({scale.x:F2}, {scale.y:F2}, {scale.z:F2})";

        // Append world-space bounds for anything with a 2D collider, since that's
        // what actually defines walkable/blocking geometry (platform edges, gaps).
        var col = t.GetComponent<Collider2D>();
        if (col != null)
        {
            Bounds b = col.bounds;
            line += $" bounds=(min:({b.min.x:F2},{b.min.y:F2}), max:({b.max.x:F2},{b.max.y:F2}))";
        }

        sb.AppendLine(line);
        foreach (Transform child in t)
            Walk(child, depth + 1, sb);
    }
}