using UnityEngine;

// Hand-placed checkpoint volume. firstZoneToReset is the index of the first BlightZone that
// should be wiped back to Pending on death after this checkpoint is reached — place these at
// zone entrance gates so resetting that zone's front to 0 doesn't cost the player any progress.
[RequireComponent(typeof(Collider2D))]
public class CheckpointTrigger : MonoBehaviour
{
    [SerializeField] Vector2 restartPos;
    [SerializeField] int firstZoneToReset = 0;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return; // component check, same pattern as BlightTriggerZone
        GameManager.Instance.SetCheckpoint(restartPos, firstZoneToReset);
    }
}