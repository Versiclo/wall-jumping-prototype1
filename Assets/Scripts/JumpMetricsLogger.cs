using System.Collections.Generic;
using UnityEngine;

// Drop this on the SAME GameObject as PlayerController.
// It only listens to PlayerController's events — it never touches movement code,
// so you can delete or disable it for a build with zero risk to gameplay.
[RequireComponent(typeof(PlayerController))]
public class JumpMetricsLogger : MonoBehaviour
{
    PlayerController player;

    [Header("Console logging")]
    [SerializeField] bool logToConsole = true;

    [Header("Trail visualization (Scene view, Gizmos)")]
    [SerializeField] Color groundedJumpColor = Color.cyan;
    [SerializeField] Color wallJumpColor = Color.magenta;
    [SerializeField] float trailPointInterval = 0.02f; // seconds between recorded trail points
    [SerializeField] int maxStoredTrails = 5; // how many past jump arcs stay visible at once
    [SerializeField] float trailPointRadius = 0.05f;

    JumpType currentJumpType;
    float trailTimer;
    List<Vector3> currentTrail;
    readonly List<(List<Vector3> points, Color color)> completedTrails = new List<(List<Vector3>, Color)>();

    void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    void OnEnable()
    {
        player.OnJumpStart += HandleJumpStart;
        player.OnApexReached += HandleApexReached;
        player.OnLanded += HandleLanded;
        player.OnReturnToLaunchHeight += HandleReturnToLaunchHeight;
    }

    void OnDisable()
    {
        player.OnJumpStart -= HandleJumpStart;
        player.OnApexReached -= HandleApexReached;
        player.OnLanded -= HandleLanded;
        player.OnReturnToLaunchHeight -= HandleReturnToLaunchHeight;
    }

    void HandleJumpStart(JumpType type)
    {
        currentJumpType = type;
        currentTrail = new List<Vector3> { transform.position };
        trailTimer = 0f;

        if (logToConsole)
        {
            Debug.Log($"[{type}] Jump started at {transform.position}");
        }
    }

    void HandleApexReached(float apexHeight, float timeToApex)
    {
        if (logToConsole)
        {
            Debug.Log($"[{currentJumpType}] Apex height: {apexHeight:F2}u | Time to apex: {timeToApex:F3}s");
        }
    }

    void HandleReturnToLaunchHeight(float horizontalDistance, float timeElapsed)
    {
        if (logToConsole)
        {
            Debug.Log($"[{currentJumpType}] Equal-height horizontal distance: {horizontalDistance:F2}u | Time: {timeElapsed:F3}s (this is the flat-terrain-comparable gap distance)");
        }
    }

    void HandleLanded(Vector2 displacement, float airTime, JumpType type)
    {
        if (logToConsole)
        {
            Debug.Log($"[{type}] Landed | Horizontal distance: {displacement.x:F2}u | Vertical delta: {displacement.y:F2}u | Airtime: {airTime:F3}s");
        }

        Color color = (type == JumpType.Grounded) ? groundedJumpColor : wallJumpColor;
        if (currentTrail != null)
        {
            completedTrails.Add((currentTrail, color));
            if (completedTrails.Count > maxStoredTrails)
            {
                completedTrails.RemoveAt(0);
            }
        }
        currentTrail = null;
    }

    void Update()
    {
        // sample a point along the current jump's path every trailPointInterval seconds
        if (currentTrail != null)
        {
            trailTimer += Time.deltaTime;
            if (trailTimer >= trailPointInterval)
            {
                trailTimer = 0f;
                currentTrail.Add(transform.position);
            }
        }
    }

    // OnDrawGizmos runs continuously in the editor (Scene view), regardless of selection.
    // Enable the Gizmos toggle in the Game view toolbar to also see this while in Play mode there.
    void OnDrawGizmos()
    {
        foreach (var trail in completedTrails)
        {
            DrawTrail(trail.points, trail.color);
        }

        // draw the jump currently in progress too, so you see it building in real time
        if (currentTrail != null && currentTrail.Count > 1)
        {
            Color liveColor = (currentJumpType == JumpType.Grounded) ? groundedJumpColor : wallJumpColor;
            DrawTrail(currentTrail, liveColor);
        }
    }

    void DrawTrail(List<Vector3> points, Color color)
    {
        if (points == null || points.Count < 2) return;

        Gizmos.color = color;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Gizmos.DrawLine(points[i], points[i + 1]);
        }
        // small spheres mark the start (push-off) and end (landing) points
        Gizmos.DrawSphere(points[0], trailPointRadius);
        Gizmos.DrawSphere(points[points.Count - 1], trailPointRadius);
    }
}