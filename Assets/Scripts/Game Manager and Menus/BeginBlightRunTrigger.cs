using UnityEngine;

// Hand-placed at the end of the clean run. Fires the clean→blight run transition once;
// disables its own collider after firing so re-entering this spot later (backtracking
// during the blight run) can't re-trigger it.
[RequireComponent(typeof(Collider2D))]
public class BeginBlightRunTrigger : MonoBehaviour
{

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return; // component check, same pattern as CheckpointTrigger
        GameManager.Instance.BeginBlightRun();
    }
}