using System;
using UnityEngine;

// Owns game-over/restart flow and the active checkpoint. A checkpoint pairs restartPos with
// firstZoneToReset — the index of the first BlightZone that gets wiped back to Pending on death.
// Everything before that index is already permanent (forward-only progression resolved it),
// so death only ever rolls Blight back, never forward.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public event Action<Vector2, float> OnPlayerDied; // (position at death, time)

    [SerializeField] Vector2 initialRestartPos;
    [SerializeField] int initialFirstZoneToReset = 0;

    PlayerController player;
    PlayerBlightExposure playerBlightExposure;
    Vector2 restartPos;
    int firstZoneToReset;

    void Awake()
    {
        Instance = this;
        restartPos = initialRestartPos;
        firstZoneToReset = initialFirstZoneToReset;
    }

    public void RegisterPlayer(PlayerController pc) => player = pc;

    public void RegisterBlightExposure(PlayerBlightExposure pbe) => playerBlightExposure = pbe;

    // Called by CheckpointTrigger when the player reaches a new checkpoint.
    public void SetCheckpoint(Vector2 position, int zoneIndex)
    {
        restartPos = position;
        firstZoneToReset = zoneIndex;
    }

    // public so other systems (blight exposure, future hazards) can trigger the same death path
    public void Die()
    {
        OnPlayerDied?.Invoke(player.transform.position, Time.time);
        BlightManager.Instance?.ResetFromZone(firstZoneToReset);
        playerBlightExposure.ResetExposure();
        player.RespawnAt(restartPos);
        ResyncBlightTriggers();
    }

    // Teleporting can land the player inside a BlightTriggerZone it was already standing in,
    // which produces no OnTriggerEnter2D. Force every zone actually overlapping the new
    // position to re-evaluate itself, so Pause/Resume state stays correct across a respawn.
    static readonly Collider2D[] blightTriggerOverlapBuffer = new Collider2D[20];

    void ResyncBlightTriggers()
    {
        var playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider == null) return;

        Physics2D.SyncTransforms(); // RespawnAt's transform.position hasn't reached the physics world yet without this

        var filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;

        int count = Physics2D.OverlapCollider(playerCollider, filter, blightTriggerOverlapBuffer);
        for (int i = 0; i < count; i++)
        {
            blightTriggerOverlapBuffer[i].GetComponent<BlightTriggerZone>()?.TryFire();
        }
    }
}