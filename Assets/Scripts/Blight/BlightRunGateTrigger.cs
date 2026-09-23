using UnityEngine;

// Hand-placed trigger volume for testing the Blight run gate without going through GameManager's
// scripted transition — e.g. walking straight to a mid-level position and opening the gate by
// hand. Not the primary path: GameManager calls BlightManager directly at the run-1 -> run-2
// handoff. Kept here as an optional authoring/testing convenience.
[RequireComponent(typeof(Collider2D))]
public class BlightRunGateTrigger : MonoBehaviour
{
    public enum Action { Open, Close }

    [SerializeField] Action action = Action.Open;
    [SerializeField] bool triggerOnce = true;

    bool consumed;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return; // component check, not tag — matches BlightTriggerZone
        Fire();
    }

    // Mirrors BlightTriggerZone.TryFire() — needed if a teleport ever drops the player directly
    // inside this collider, since OnTriggerEnter2D won't fire for an overlap that starts already-overlapping.
    public void TryFire() => Fire();

    void Fire()
    {
        if (triggerOnce && consumed) return;
        if (BlightManager.Instance == null) return;

        switch (action)
        {
            case Action.Open: BlightManager.Instance.OpenRunGate(); break;
            case Action.Close: BlightManager.Instance.CloseRunGate(); break;
        }
        consumed = true;
    }
}