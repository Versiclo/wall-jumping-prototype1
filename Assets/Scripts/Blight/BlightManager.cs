using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owns the ordered zone sequence and is the only thing that talks to BlightCorruptible — nothing
// polls anything. Static corruptibles are found once per zone (a single OverlapBoxAll when that
// zone activates), ramped for as long as they're pending, then flipped once and dropped — after
// that they're never touched again. Moving corruptibles register themselves and get tested
// against every zone that's started, every FixedUpdate, since they can drift in and out.
//
// Runs on FixedUpdate: PlayerController reads BlightCorruptible.IsCorrupted inside its own
// FixedUpdate, so corruption state needs to be current as of the same physics step. Keep this
// component's execution order ahead of PlayerController's (Project Settings > Script Execution
// Order) — BlightCorruptible itself no longer needs pinning, it has no Update methods at all.
public class BlightManager : MonoBehaviour
{
    public static BlightManager Instance { get; private set; }

    [SerializeField] BlightZone[] zones; // hand-ordered in the Inspector — the sequence, not hierarchy order
    [SerializeField] LayerMask corruptibleLayer; // assign the layer your platform prefabs live on
    [SerializeField] float moveCleanseDuration = 1.5f; // seconds for a fully-corrupted mover to visually clear once outside every zone

    public int ActiveZoneIndex { get; private set; } = -1;
    public BlightZone ActiveZone => (ActiveZoneIndex >= 0 && ActiveZoneIndex < zones.Length) ? zones[ActiveZoneIndex] : null;
    public bool IsPaused { get; private set; }

    struct PendingStatic
    {
        public BlightCorruptible corruptible;
        public float axisDistance;
    }

    List<PendingStatic>[] pendingStatics;

    List<BlightCorruptible>[] zoneStatics; // full roster per zone, never pruned — lets a checkpoint reset find and un-claim everything a zone ever corrupted
    static readonly List<BlightCorruptible> movingCorruptibles = new List<BlightCorruptible>();
    readonly HashSet<BlightCorruptible> claimedStatics = new HashSet<BlightCorruptible>();

    Coroutine overrideRoutine;

    void Awake()
    {
        Instance = this;
        pendingStatics = new List<PendingStatic>[zones != null ? zones.Length : 0];
        zoneStatics = new List<BlightCorruptible>[zones != null ? zones.Length : 0];
    }

    void FixedUpdate()
    {
        if (zones == null || zones.Length == 0) return;

        if (ActiveZoneIndex < 0)
        {
            ActiveZoneIndex = 0;
            zones[0].Activate();
            BuildPendingStatics(0);
        }

        if (!IsPaused && overrideRoutine == null)
        {
            AdvanceActiveZone(Time.fixedDeltaTime);
        }

        UpdatePendingStatics();
        UpdateMovingCorruptibles();
    }

    void AdvanceActiveZone(float dt)
    {
        int guard = 0;
        while (ActiveZoneIndex >= 0 && ActiveZoneIndex < zones.Length && dt > 0f && guard++ < zones.Length)
        {
            bool resolved = zones[ActiveZoneIndex].Advance(dt, out float overflow);
            if (!resolved) break;

            ForceResolvePendingStatics(ActiveZoneIndex);
            ActiveZoneIndex++;

            if (ActiveZoneIndex < zones.Length)
            {
                zones[ActiveZoneIndex].Activate();
                BuildPendingStatics(ActiveZoneIndex);
            }

            dt = overflow; // feeds a fast filler zone's leftover time straight into the next zone
        }
    }

    // One-time discovery: everything whose collider overlaps this zone's full footprint (begin
    // gate to end gate, full width) — matches "everything within these rects gets corrupted."
    void BuildPendingStatics(int zoneIndex)
    {
        BlightZone zone = zones[zoneIndex];
        Collider2D[] hits = Physics2D.OverlapBoxAll(zone.GetFootprintWorldCenter(), zone.GetFootprintWorldSize(), zone.WorldAngle, corruptibleLayer);

        var pending = new List<PendingStatic>(hits.Length);
        var roster = new List<BlightCorruptible>(hits.Length);
        foreach (var hit in hits)
        {
            BlightCorruptible bc = hit.GetComponent<BlightCorruptible>();
            if (bc == null || bc.IsMoving) continue; // moving ones are handled every tick, separately, below
            if (!claimedStatics.Add(bc)) continue; // already owned by an earlier zone — first contact wins, this zone never touches it

            pending.Add(new PendingStatic { corruptible = bc, axisDistance = GetLeadingAxisDistance(zone, hit.bounds) });
            roster.Add(bc);
        }
        pendingStatics[zoneIndex] = pending;
        zoneStatics[zoneIndex] = roster;
    }

    // Axis distance of the point on the object's bounds closest to the zone's begin gate — the
    // first point the front will actually touch — rather than transform.position, which for a
    // long platform or an off-center pivot can sit well past where the front first reaches it.
    static float GetLeadingAxisDistance(BlightZone zone, Bounds bounds)
    {
        float d0 = zone.GetAxisDistance(new Vector3(bounds.min.x, bounds.min.y));
        float d1 = zone.GetAxisDistance(new Vector3(bounds.min.x, bounds.max.y));
        float d2 = zone.GetAxisDistance(new Vector3(bounds.max.x, bounds.min.y));
        float d3 = zone.GetAxisDistance(new Vector3(bounds.max.x, bounds.max.y));
        return Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3));
    }

    // Only the active zone's pending list needs per-tick work — earlier zones already flipped
    // everyone when they resolved, later zones haven't been built yet.
    void UpdatePendingStatics()
    {
        if (ActiveZoneIndex < 0 || ActiveZoneIndex >= zones.Length) return;
        List<PendingStatic> pending = pendingStatics[ActiveZoneIndex];
        if (pending == null || pending.Count == 0) return;

        BlightZone zone = zones[ActiveZoneIndex];
        float edge = zone.LeadingEdge;
        float lead = zone.LeadDistance;

        for (int i = pending.Count - 1; i >= 0; i--)
        {
            PendingStatic p = pending[i];
            bool corrupted = edge >= p.axisDistance;
            float amount = ComputeAmount(p.axisDistance, lead, edge, corrupted);

            p.corruptible.ApplyCorruption(amount, corrupted);
            if (corrupted) pending.RemoveAt(i); // done forever — static, never re-checked again
        }
    }

    void ForceResolvePendingStatics(int zoneIndex)
    {
        List<PendingStatic> pending = pendingStatics[zoneIndex];
        if (pending == null) return;
        foreach (var p in pending) p.corruptible.ApplyCorruption(1f, true);
        pending.Clear();
    }

    void UpdateMovingCorruptibles()
    {
        foreach (var c in movingCorruptibles)
        {
            float bestAmount = 0f;
            bool anyCorrupted = false;
            bool claimed = false; // true once any zone's axis/cross bounds actually contain this platform

            int lastStarted = Mathf.Min(ActiveZoneIndex, zones.Length - 1);
            for (int i = 0; i <= lastStarted; i++)
            {
                BlightZone zone = zones[i];
                if (zone.CurrentState == BlightZone.State.Pending) continue;

                float axisDist = GetLeadingAxisDistance(zone, c.ColliderBounds);
                float crossDist = zone.GetCrossDistance(c.transform.position);

                if (Mathf.Abs(crossDist) > zone.Width * 0.5f) continue;
                if (axisDist < 0f || axisDist > zone.Length) continue;

                claimed = true;

                bool corrupted = zone.LeadingEdge >= axisDist;
                float amount = ComputeAmount(axisDist, zone.LeadDistance, zone.LeadingEdge, corrupted);

                bestAmount = Mathf.Max(bestAmount, amount);
                anyCorrupted |= corrupted;
            }

            if (claimed) c.ApplyCorruption(bestAmount, anyCorrupted);
            else c.Cleanse(Time.fixedDeltaTime, moveCleanseDuration);
        }
    }

    static float ComputeAmount(float axisDistance, float lead, float edge, bool corrupted)
    {
        if (lead <= 0f) return corrupted ? 1f : 0f; // InverseLerp(d, d, x) degenerates to 0 — hard-flip case needs an explicit guard
        return Mathf.InverseLerp(axisDistance - lead, axisDistance, edge);
    }

    public void RegisterMoving(BlightCorruptible c)
    {
        if (!movingCorruptibles.Contains(c)) movingCorruptibles.Add(c);
    }

    public void UnregisterMoving(BlightCorruptible c) => movingCorruptibles.Remove(c);

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    // Called by GameManager on respawn. Every zone from firstZoneToReset onward is wiped to
    // Pending (un-corrupting anything it had claimed); zones before it are untouched, since
    // forward-only progression already made them permanent.
    public void ResetFromZone(int firstZoneToReset)
    {
        if (zones == null || zones.Length == 0) return;
        firstZoneToReset = Mathf.Clamp(firstZoneToReset, 0, zones.Length - 1);

        if (overrideRoutine != null) { StopCoroutine(overrideRoutine); overrideRoutine = null; }
        IsPaused = false;

        for (int i = firstZoneToReset; i < zones.Length; i++)
        {
            ResetZone(i);
        }

        ActiveZoneIndex = firstZoneToReset;
        zones[firstZoneToReset].Activate();
        BuildPendingStatics(firstZoneToReset);
        foreach (var trigger in FindObjectsByType<BlightTriggerZone>(FindObjectsInactive.Exclude))
            trigger.ResetIfAhead(firstZoneToReset);
    }

    // Un-claims and un-corrupts everything this zone ever discovered, then returns the zone to
    // Pending. It re-discovers its statics from scratch next activation — fine, they're static.
    void ResetZone(int zoneIndex)
    {
        List<BlightCorruptible> roster = zoneStatics[zoneIndex];
        if (roster != null)
        {
            foreach (var bc in roster)
            {
                bc.ApplyCorruption(0f, false);
                claimedStatics.Remove(bc);
            }
            roster.Clear();
        }
        pendingStatics[zoneIndex] = null;
        zones[zoneIndex].ResetToPending(); // needs to exist on BlightZone — see note above
    }

    // Shoves the active zone's edge forward/back within itself — clamped to [0, its own length],
    // doesn't chain into a zone skip even if it hits the cap. Use SetProgress for a full skip.
    public void Nudge(float delta)
    {
        if (ActiveZoneIndex < 0 || ActiveZoneIndex >= zones.Length) return;
        BlightZone zone = zones[ActiveZoneIndex];
        zone.SetLeadingEdge(zone.LeadingEdge + delta);
    }

    // Bottleneck tuning: eases the sweep to a specific (zone, fraction-through-that-zone) target
    // over `duration` seconds. Every zone strictly before the target is resolved instantly first
    // (even if never normally activated), so a level-select-style jump doesn't skip their statics.
    public void SetProgress(int zoneIndex, float fraction, float duration)
    {
        if (zones == null || zones.Length == 0) return;
        if (overrideRoutine != null) StopCoroutine(overrideRoutine);
        overrideRoutine = StartCoroutine(SetProgressRoutine(Mathf.Clamp(zoneIndex, 0, zones.Length - 1), Mathf.Clamp01(fraction), duration));
    }

    IEnumerator SetProgressRoutine(int targetZoneIndex, float targetFraction, float duration)
    {
        for (int i = 0; i <= targetZoneIndex; i++)
        {
            if (zones[i].CurrentState == BlightZone.State.Pending)
            {
                zones[i].Activate();
                BuildPendingStatics(i);
            }

            if (i < targetZoneIndex)
            {
                zones[i].ResolveInstantly();
                ForceResolvePendingStatics(i);
            }
        }
        ActiveZoneIndex = targetZoneIndex;

        BlightZone target = zones[targetZoneIndex];
        float targetEdge = target.Length * targetFraction;

        if (duration <= 0f)
        {
            target.SetLeadingEdge(targetEdge);
            overrideRoutine = null;
            yield break;
        }

        float start = target.LeadingEdge;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            target.SetLeadingEdge(Mathf.Lerp(start, targetEdge, elapsed / duration));
        }
        target.SetLeadingEdge(targetEdge);
        overrideRoutine = null;
    }
}