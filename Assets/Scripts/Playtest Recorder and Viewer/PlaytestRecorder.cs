using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Runtime playtest logger — ships in the build, sits next to PlayerController.
// Samples position on a fixed interval and logs deaths, so a returned log file can be
// redrawn later as a route (see PlaytestRouteViewer). Reads nothing from PlayerController
// except the OnDied event and the Transform — same "never touches movement code" boundary
// JumpMetricsLogger already uses.
[RequireComponent(typeof(PlayerController))]
public class PlaytestRecorder : MonoBehaviour
{
    [Header("Identification (optional — set per build before sending to a tester)")]
    [SerializeField] string sessionLabel = ""; // e.g. "Alice" — leave blank to auto-name by device

    [Header("Sampling")]
    [SerializeField] float sampleInterval = 0.04f;  // seconds between recorded position samples
    [SerializeField] float flushIntervalSeconds = 5f;  // how often the buffer is written to disk

    PlayerController player;

    [SerializeField] GameManager gameManager; // needs to be assigned in-editor

    Vector3 lastSamplePos;
    int ticksPerSample;
    int tickCounter;
    float sampleIntervalActual; // ticksPerSample * fixedDeltaTime — the exact interval actually used
    float flushTimer;
    string filePath;
    bool headerWritten;
    int currentRun;            // increments on every death — lets the viewer avoid drawing
    bool resyncNextSample;     // a line across the teleport back to restartPos
    readonly List<string> buffer = new List<string>();

    void Awake()
    {
        player = GetComponent<PlayerController>();
        gameManager = GameObject.Find("Game Manager").GetComponent<GameManager>();
        lastSamplePos = transform.position;

        // FixedUpdate is the sampling clock, so we can't sample finer than one physics tick.
        // Counting ticks (instead of accumulating Time.fixedDeltaTime in a float and comparing
        // with >=) also avoids rounding error pushing the real interval above the requested one —
        // the old approach measured a requested 0.1s out at ~0.12s.
        ticksPerSample = Mathf.Max(1, Mathf.RoundToInt(sampleInterval / Time.fixedDeltaTime));
        sampleIntervalActual = ticksPerSample * Time.fixedDeltaTime;
        if (sampleInterval < Time.fixedDeltaTime)
        {
            Debug.LogWarning($"PlaytestRecorder: sampleInterval ({sampleInterval}s) is below the physics tick ({Time.fixedDeltaTime}s) and can't be sampled that finely. Clamped to {sampleIntervalActual}s.");
        }

        string folder = Path.Combine(Application.persistentDataPath, "PlaytestLogs");
        Directory.CreateDirectory(folder);

        string who = string.IsNullOrEmpty(sessionLabel)
            ? SystemInfo.deviceUniqueIdentifier.Substring(0, Mathf.Min(8, SystemInfo.deviceUniqueIdentifier.Length))
            : sessionLabel;
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        filePath = Path.Combine(folder, $"playtest_{who}_{timestamp}.csv");
    }

    void OnEnable() => gameManager.OnPlayerDied += HandleDied;
    void OnDisable()
    {
        gameManager.OnPlayerDied -= HandleDied;
        Flush();
    }

    void HandleDied(Vector2 position, float time)
    {
        WriteHeaderIfNeeded();
        buffer.Add(FormatRow("E", "DIED", time, position.x, position.y, 0f, currentRun));
        currentRun++;
        resyncNextSample = true; // next sample is post-teleport; don't measure "speed" across that jump
    }

    void FixedUpdate()
    {
        tickCounter++;
        if (tickCounter >= ticksPerSample)
        {
            tickCounter = 0;
            float speed = resyncNextSample
                ? 0f
                : Vector3.Distance(transform.position, lastSamplePos) / sampleIntervalActual;
            resyncNextSample = false;
            lastSamplePos = transform.position;

            WriteHeaderIfNeeded();
            buffer.Add(FormatRow("S", "", Time.time, transform.position.x, transform.position.y, speed, currentRun));
        }
        
        flushTimer += Time.fixedDeltaTime;
        if (flushTimer >= flushIntervalSeconds)
        {
            flushTimer = 0f;
            Flush();
        }
    }

    void WriteHeaderIfNeeded()
    {
        if (headerWritten) return;
        headerWritten = true;
        buffer.Insert(0, $"# tester={sessionLabel},device={SystemInfo.deviceUniqueIdentifier},sessionStartUtc={DateTime.UtcNow:O}");
    }

    // Row shape is always: kind,eventType,time,x,y,speed,run
    // eventType is blank for "S" (sample) rows; speed is unused (0) for "E" (event) rows.
    string FormatRow(string kind, string eventType, float time, float x, float y, float speed, int run)
    {
        return string.Join(",", new[]
        {
            kind, eventType,
            time.ToString("F3", CultureInfo.InvariantCulture),
            x.ToString("F3", CultureInfo.InvariantCulture),
            y.ToString("F3", CultureInfo.InvariantCulture),
            speed.ToString("F3", CultureInfo.InvariantCulture),
            run.ToString(CultureInfo.InvariantCulture)
        });
    }

    void Flush()
    {
        if (buffer.Count == 0) return;
        File.AppendAllLines(filePath, buffer);
        buffer.Clear();
    }

    void OnApplicationQuit() => Flush();
}