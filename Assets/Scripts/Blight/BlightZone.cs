using UnityEngine;

// One segment of the blight sweep. A zone's Transform IS its two gates: local origin is the
// begin gate, local origin + transform.up * length is the end gate, so rotating/moving the
// object in the Scene view is the entire authoring workflow — no separate gate objects needed.
// Corner turns between zones are free: each zone has its own independent rotation.
//
// A zone knows nothing about its neighbors or about which platforms it affects — BlightManager
// owns sequencing and owns finding + notifying corruptibles. This script is just geometry plus
// a leading-edge value that advances over time.
public class BlightZone : MonoBehaviour
{
    public enum State { Pending, Active, Resolved }

    [Header("Shape")]
    [SerializeField] float width = 3f;
    [SerializeField] float length = 5f;

    [Header("Sweep")]
    [SerializeField] float speed = 2.5f;       // units/sec the leading edge advances once Active
    [SerializeField] float acceleration = 0f;  // optional speed-up while Active; 0 = constant, matches old v1 default

    [Header("Fairness buffer")]
    [SerializeField] float leadDistance = 3f; // CorruptionAmount starts ramping this far ahead of the edge; 0 = hard instant flip, no telegraph

    [Header("Leading-edge effect (optional)")]
    [SerializeField] ParticleSystem leadingEdgeEffect; // author the look yourself (foam/splash/whatever) — this script only drives its transform + play/stop

    [Header("Zone fill (optional)")]
    [SerializeField] SpriteRenderer zoneFill; // 1x1 white sprite, sorting layer below platforms/player — this script only drives its scale
    [SerializeField] bool matchEffectSpeedToZone = true; // scales leadingEdgeEffect.main.simulationSpeed to currentSpeed/speed, so the effect visibly speeds up when acceleration kicks in
    [SerializeField] Color fillColor = new Color(0.165f, 0.133f, 0.2f, 0.55f); // muted, darkened corruption hue — tune against platform pulse color in Scene view

    public State CurrentState { get; private set; } = State.Pending;
    public float LeadingEdge { get; private set; }
    public float Width => width;
    public float Length => length;
    public float LeadDistance => leadDistance;

    float currentSpeed;
    Vector2 fillSpriteUnitSize = Vector2.one; // native size (in world units) of zoneFill's sprite at scale 1 — cached so scale math is PPU-agnostic

    void Awake()
    {
        InitZoneFill();
        SyncEffectShapeWidth();
    }

    #if UNITY_EDITOR
    void OnValidate()
    {
        SyncEffectShapeWidth();
    }
    #endif

    void SyncEffectShapeWidth()
    {
        if (leadingEdgeEffect == null) return;
        var shape = leadingEdgeEffect.shape;
        Vector3 scale = shape.scale;
        scale.x = width;
        shape.scale = scale;
    }

    public void Activate()
    {
        CurrentState = State.Active;
        LeadingEdge = 0f;
        currentSpeed = speed;
        SetEdgeEffectActive(true);
        InitZoneFill();
    }

    // Advances the leading edge by dt. Returns true if the edge reached `length` this call —
    // `overflowTime` is whatever fraction of dt happened *after* that, for BlightManager to feed
    // straight into the next zone so a fast gap-filler doesn't cost the sweep a visible stall.
    public bool Advance(float dt, out float overflowTime)
    {
        overflowTime = 0f;
        if (CurrentState != State.Active || dt <= 0f) return false;

        currentSpeed += acceleration * dt;
        float remaining = length - LeadingEdge;
        float step = currentSpeed * dt;

        if (currentSpeed > 0f && step >= remaining)
        {
            float timeToResolve = remaining / currentSpeed;
            overflowTime = dt - timeToResolve;
            LeadingEdge = length;
            CurrentState = State.Resolved;
            SetEdgeEffectActive(false);
            return true;
        }

        LeadingEdge = Mathf.Max(0f, LeadingEdge + step);
        UpdateEdgeEffectTransform();
        UpdateZoneFill();
        return false;
    }

    // Used by BlightManager for SetProgress-style jumps: skips straight to fully resolved
    // without ticking through Advance().
    public void ResolveInstantly()
    {
        CurrentState = State.Resolved;
        LeadingEdge = length;
        SetEdgeEffectActive(false);
        UpdateZoneFill();
    }

    // Also for SetProgress — directly places the edge (used by its eased lerp toward a target).
    public void SetLeadingEdge(float value)
    {
        LeadingEdge = Mathf.Clamp(value, 0f, length);
        UpdateEdgeEffectTransform();
        UpdateZoneFill();
    }

    // Mirror of Activate()/ResolveInstantly() — used by BlightManager.ResetFromZone on respawn
    // to put an already-swept zone back to its pre-activation state.
    public void ResetToPending()
    {
        CurrentState = State.Pending;
        LeadingEdge = 0f;
        currentSpeed = 0f;
        SetEdgeEffectActive(false);
        InitZoneFill(); // collapses the fill sprite back to zero height, same as before first Activate()
    }

    // Distance of a world position along this zone's growth axis (local up), from the begin gate.
    public float GetAxisDistance(Vector3 worldPos) => transform.InverseTransformPoint(worldPos).y;

    // Distance of a world position across this zone's width axis (local right), from center.
    public float GetCrossDistance(Vector3 worldPos) => transform.InverseTransformPoint(worldPos).x;

    // Full-footprint query box (0 to length, full width) — used once, when the zone activates,
    // to discover which static corruptibles it will ever affect.
    public Vector2 GetFootprintWorldCenter() => transform.TransformPoint(new Vector2(0f, length * 0.5f));
    public Vector2 GetFootprintWorldSize() => new Vector2(width, length);
    public float WorldAngle => transform.eulerAngles.z;

    void SetEdgeEffectActive(bool active)
    {
        if (leadingEdgeEffect == null) return;
        if (active) leadingEdgeEffect.Play();
        else leadingEdgeEffect.Stop();
    }

    void UpdateEdgeEffectTransform()
    {
        if (leadingEdgeEffect == null) return;
        leadingEdgeEffect.transform.localPosition = new Vector3(0f, LeadingEdge, 0f);

        if (matchEffectSpeedToZone && !Mathf.Approximately(speed, 0f))
        {
            var main = leadingEdgeEffect.main;
            main.simulationSpeed = Mathf.Max(0.05f, currentSpeed / speed);
        }
    }

    void InitZoneFill()
    {
        if (zoneFill == null) return;
        zoneFill.color = fillColor;
        fillSpriteUnitSize = zoneFill.sprite.bounds.size;
        zoneFill.transform.localScale = new Vector3(width / fillSpriteUnitSize.x, 0f, 1f);
    }

    void UpdateZoneFill()
    {
        if (zoneFill == null) return;
        zoneFill.transform.localScale = new Vector3(width / fillSpriteUnitSize.x, LeadingEdge / fillSpriteUnitSize.y, 1f);
    }

    void OnDrawGizmos()
    {
        Vector3 origin = transform.position;
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        float halfW = width * 0.5f;

        Vector3 p00 = origin - right * halfW;
        Vector3 p01 = origin + right * halfW;
        Vector3 p10 = origin - right * halfW + up * length;
        Vector3 p11 = origin + right * halfW + up * length;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(p00, p01); // begin gate

        Gizmos.color = Color.red;
        Gizmos.DrawLine(p10, p11); // end gate

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        Gizmos.DrawLine(p00, p10);
        Gizmos.DrawLine(p01, p11);

        if (Application.isPlaying && CurrentState != State.Pending)
        {
            Vector3 e0 = origin - right * halfW + up * LeadingEdge;
            Vector3 e1 = origin + right * halfW + up * LeadingEdge;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(e0, e1);

            float bufferEnd = Mathf.Min(LeadingEdge + leadDistance, length);
            Vector3 b0 = origin - right * halfW + up * bufferEnd;
            Vector3 b1 = origin + right * halfW + up * bufferEnd;
            Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
            Gizmos.DrawLine(b0, b1);
        }
    }
}