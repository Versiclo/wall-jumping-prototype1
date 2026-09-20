using UnityEngine;

// Passive receiver — BlightManager pushes corruption state in, this script never polls or does
// any distance/overlap math itself (a platform doesn't know which zone(s) it's in; the manager
// does). Static platforms get ApplyCorruption() called while pending, then once more at the
// flip, then never again. Moving platforms (isMoving = true) get it every FixedUpdate for as
// long as they exist, since they can drift in and out of a zone's footprint.
[RequireComponent(typeof(SpriteRenderer))]
public class BlightCorruptible : MonoBehaviour
{
    static readonly int CorruptionAmountID = Shader.PropertyToID("_CorruptionAmount");
    static readonly int HandPlacedID = Shader.PropertyToID("_HandPlaced");

    [Header("Hand-placed hazard variant")]
    [SerializeField] bool forceCorrupted = false; // precision variant — ignores BlightManager entirely, always corrupted

    [Header("Moving platform")]
    [SerializeField] bool isMoving = false; // tick if something moves this platform's Transform every frame — BlightManager re-tests it every FixedUpdate instead of flipping it once

    SpriteRenderer sr;
    MaterialPropertyBlock mpb;
    Collider2D col;

    public bool IsCorrupted { get; private set; }
    public float CorruptionAmount { get; private set; }
    public bool IsMoving => isMoving;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
        col = GetComponent<Collider2D>();
    }

    void Start()
    {
        // Registering here rather than OnEnable relies on Unity's guarantee that all Awakes run
        // before any Start in a scene — so BlightManager.Instance is already set by the time
        // this runs, no explicit script execution order needed for this half of the relationship.
        if (isMoving && BlightManager.Instance != null) BlightManager.Instance.RegisterMoving(this);

        if (forceCorrupted)
        {
            IsCorrupted = true;
            CorruptionAmount = 1f;
            ApplyVisual();
        }
    }

    void OnDisable()
    {
        if (isMoving && BlightManager.Instance != null) BlightManager.Instance.UnregisterMoving(this);
    }

    public Bounds ColliderBounds => col != null ? col.bounds : new Bounds(transform.position, Vector3.zero);

    // Called by BlightManager only.
    public void ApplyCorruption(float amount, bool corrupted)
    {
        if (forceCorrupted) return; // hand-placed variant is permanent, never overwritten

        CorruptionAmount = amount;
        IsCorrupted = corrupted;
        ApplyVisual();
    }

    // Moving platforms only — called instead of ApplyCorruption every tick BlightManager finds
    // this platform outside every zone's bounds. Gameplay punishment clears immediately
    // (IsCorrupted drops now); the visual gradient decays over `duration` seconds so it doesn't
    // pop. duration is the time to clear from full (1) — partial amounts clear proportionally
    // faster, same tunable-timer pattern as everything else.
    public void Cleanse(float dt, float duration)
    {
        if (forceCorrupted) return;

        IsCorrupted = false;
        CorruptionAmount = duration <= 0f ? 0f : Mathf.Max(0f, CorruptionAmount - dt / duration);

        ApplyVisual();
    }

    void ApplyVisual()
    {
        sr.GetPropertyBlock(mpb);
        mpb.SetFloat(CorruptionAmountID, CorruptionAmount);
        mpb.SetFloat(HandPlacedID, forceCorrupted ? 1f : 0f);
        sr.SetPropertyBlock(mpb);
    }
}