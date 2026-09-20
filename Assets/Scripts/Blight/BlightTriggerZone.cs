using UnityEngine;

// Hand-placed trigger volume for per-chunk BlightManager tuning — same role as the old
// BlightFrontTriggerZone, retargeted at the new manager's zone-indexed SetProgress.
[RequireComponent(typeof(Collider2D))]
public class BlightTriggerZone : MonoBehaviour
{
    public enum Action { Pause, Resume, Nudge, SetProgress }

    [SerializeField] Action action = Action.Pause;
    [SerializeField] int zoneIndex; // which BlightZone's region this trigger sits in — used on respawn to decide whether it re-arms
    [SerializeField] float nudgeAmount = 2f;          // used by Nudge
    [SerializeField] int targetZoneIndex = 0;         // used by SetProgress
    [SerializeField] float targetFraction = 0f;       // used by SetProgress — 0..1 through that zone
    [SerializeField] float transitionDuration = 1f;   // used by SetProgress
    [SerializeField] bool triggerOnce = true;

    bool consumed;

    // Called by BlightManager.ResetFromZone on respawn. Same "before stays resolved, from-here-
    // on gets wiped" rule as the rest of the system — a trigger the player already passed on the
    // way to this checkpoint stays consumed; one ahead of it re-arms.
    public void ResetIfAhead(int firstZoneToReset)
    {
        if (zoneIndex >= firstZoneToReset) consumed = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return; // component check, not tag
        Fire();
    }

    // Explicit entry point for GameManager to call right after a teleport (respawn/restart).
    // OnTriggerEnter2D only fires on a not-overlapping -> overlapping transition; a teleport
    // that starts and ends inside the same trigger produces no event at all.
    public void TryFire() => Fire();

    void Fire()
    {
        if (triggerOnce && consumed) return;
        if (BlightManager.Instance == null) return;

        switch (action)
        {
            case Action.Pause: BlightManager.Instance.Pause(); break;
            case Action.Resume: BlightManager.Instance.Resume(); break;
            case Action.Nudge: BlightManager.Instance.Nudge(nudgeAmount); break;
            case Action.SetProgress: BlightManager.Instance.SetProgress(targetZoneIndex, targetFraction, transitionDuration); break;
        }
        consumed = true;
    }
}