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
        sb.AppendLine(new string(' ', depth * 2) + t.name +
            " [" + string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name)) + "]");
        foreach (Transform child in t)
            Walk(child, depth + 1, sb);
    }
}