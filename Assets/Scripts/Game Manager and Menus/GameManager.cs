using System;
using System.Collections.Generic;
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
    [SerializeField] Vector2 blightRunRestartPos;
    [SerializeField] int blightRunFirstZoneToReset = 0;
    [SerializeField] List<GameObject> cleanRunObjects;
    [SerializeField] List<GameObject> blightRunObjects;

    const string CleanRunCompletedKey = "CleanRunCompleted";
    public bool CleanRunCompleted => PlayerPrefs.GetInt(CleanRunCompletedKey, 0) == 1;

    PlayerController player;
    PlayerBlightExposure playerBlightExposure;
    Vector2 restartPos;
    int firstZoneToReset;

    void Awake()
    {
        Instance = this;
        restartPos = initialRestartPos;
        firstZoneToReset = initialFirstZoneToReset;
        SetRunObjectsActive(blightActive: false);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void RegisterPlayer(PlayerController pc) => player = pc;

    public void RegisterBlightExposure(PlayerBlightExposure pbe) => playerBlightExposure = pbe;

    // Called by CheckpointTrigger when the player reaches a new checkpoint.
    public void SetCheckpoint(Vector2 position, int zoneIndex)
    {
        restartPos = position;
        firstZoneToReset = zoneIndex;
    }

    // Called once, unconditionally, when the player reaches the end of the clean run.
    public void BeginBlightRun()
    {
        PlayerPrefs.SetInt(CleanRunCompletedKey, 1);
        PlayerPrefs.Save();
        RestartBlightRun();
    }

    // PauseMenu "Restart Clean Run" — full reset back to the clean-run start, gate closed.
    public void RestartCleanRun()
    {
        SetRunObjectsActive(blightActive: false);
        SetCheckpoint(initialRestartPos, initialFirstZoneToReset);
        Die();
        BlightManager.Instance.CloseRunGate();
    }

    // PauseMenu "Restart Blight Run" — full reset back to the blight-run start, gate open.
    // Also the tail end of BeginBlightRun().
    public void RestartBlightRun()
    {
        BlightManager.Instance.OpenRunGate();
        SetRunObjectsActive(blightActive: true);
        SetCheckpoint(blightRunRestartPos, blightRunFirstZoneToReset);
        Die();
    }

    void SetRunObjectsActive(bool blightActive)
    {
        foreach (var go in cleanRunObjects) go.SetActive(!blightActive);
        foreach (var go in blightRunObjects) go.SetActive(blightActive);
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