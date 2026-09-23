using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Editor-side review tool. Drag each returned .csv log into your Assets folder (Unity
// imports it as a TextAsset automatically), then drag those TextAssets into "Log Files"
// below — that's the entire multi-tester workflow. Each file gets its own hue so overlaid
// testers stay distinguishable; brightness within that hue encodes speed (dim = slow/hesitant,
// bright = fast/confident). Per-run visibility is filtered via "Run Filters" — use the
// "Refresh Run List" context menu after adding/removing logs or after a tester logs new runs.
// Keep this off any GameObject in a shipped build.
public class PlaytestRouteViewer : MonoBehaviour
{
    [SerializeField] List<TextAsset> logFiles = new List<TextAsset>();
    [SerializeField] float deathMarkerSize = 0.3f;
    [SerializeField] bool drawRoutes = true;
    [SerializeField] bool drawDeaths = true;

    [Header("Pause markers — replaces the old speed-brightness coloring")]
    [SerializeField] bool drawPauses = true;
    [SerializeField] float pauseSpeedThreshold = 0.5f;    // units/sec below this counts as "not moving"
    [SerializeField] float minPauseDurationToShow = 0.3f; // ignore blips shorter than this (e.g. one landing frame)
    [SerializeField] float pauseMarkerBaseRadius = 0.15f;
    [SerializeField] float pauseMarkerRadiusPerSecond = 0.08f;
    [SerializeField] float pauseMarkerMaxRadius = 1.2f;

    [Header("Blight exposure markers")]
    [SerializeField] bool drawExposures = true;
    [SerializeField] float exposureMarkerRadius = 0.15f;

    [Header("Run filtering — populated by \"Refresh Run List\" context menu")]
    [SerializeField] List<RunFilter> runFilters = new List<RunFilter>();

    // Hue identifies the tester (file). Each entry here is a (saturation, brightness) pair,
    // cycled by run number, so consecutive attempts by the same tester stay visually distinct.
    static readonly (float sat, float val)[] RunShades =
    {
        (1f, 1f), (0.55f, 0.85f), (1f, 0.55f), (0.55f, 0.55f)
    };

    readonly Dictionary<TextAsset, ParsedLog> parseCache = new Dictionary<TextAsset, ParsedLog>();

    struct LogSample { public float x, y, speed; public int run; }
    struct LogDeath { public float x, y; public int run; }
    struct PauseMarker { public float x, y, duration; public int run; }
    struct ExposureMarker { public float x, y; public int run; }

    [System.Serializable]
    public class RunFilter
    {
        public TextAsset log;
        public int run;
        public bool blighted; // informational, set by Refresh — not hand-edited
        public bool visible = true;
    }

    class ParsedLog
    {
        public List<LogSample> samples = new List<LogSample>();
        public List<LogDeath> deaths = new List<LogDeath>();
        public List<PauseMarker> pauses = new List<PauseMarker>();
        public List<ExposureMarker> exposures = new List<ExposureMarker>();
        public HashSet<int> blightRuns = new HashSet<int>(); // runs where BlightManager.IsRunActive was true
    }

    void OnDrawGizmos()
    {
        for (int i = 0; i < logFiles.Count; i++)
        {
            if (logFiles[i] == null) continue;

            ParsedLog log = GetParsed(logFiles[i]);
            float hue = (i * 0.618033f) % 1f; // golden-angle spacing keeps hues distinct for any tester count

            if (drawRoutes)
            {
                for (int s = 0; s < log.samples.Count - 1; s++)
                {
                    LogSample a = log.samples[s];
                    LogSample b = log.samples[s + 1];
                    if (a.run != b.run) continue; // don't draw a line across a death/respawn teleport
                    if (!IsVisible(logFiles[i], a.run)) continue;

                    Gizmos.color = RunColor(hue, a.run);
                    Gizmos.DrawLine(new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
                }
            }

            if (drawPauses)
            {
                foreach (PauseMarker p in log.pauses)
                {
                    if (!IsVisible(logFiles[i], p.run)) continue;
                    Gizmos.color = RunColor(hue, p.run);
                    float radius = Mathf.Min(pauseMarkerMaxRadius, pauseMarkerBaseRadius + p.duration * pauseMarkerRadiusPerSecond);
                    Gizmos.DrawSphere(new Vector3(p.x, p.y, 0f), radius);
                }
            }

            if (drawDeaths)
            {
                Gizmos.color = Color.HSVToRGB(hue, 1f, 1f);
                foreach (LogDeath d in log.deaths)
                {
                    if (!IsVisible(logFiles[i], d.run)) continue;
                    Gizmos.DrawWireCube(new Vector3(d.x, d.y, 0f), Vector3.one * deathMarkerSize);
                }
            }

            if (drawExposures)
            {
                float exposureHue = (hue + 0.5f) % 1f; // offset from this log's route hue, not a flat global color
                foreach (ExposureMarker e in log.exposures)
                {
                    if (!IsVisible(logFiles[i], e.run)) continue;
                    Gizmos.color = RunColor(exposureHue, e.run); // reuses the sat/val-per-run cycling too
                    Gizmos.DrawWireSphere(new Vector3(e.x, e.y, 0f), exposureMarkerRadius);
                }
            }
        }
    }

    bool IsVisible(TextAsset asset, int run)
    {
        RunFilter f = runFilters.Find(rf => rf.log == asset && rf.run == run);
        return f == null || f.visible; // unrefreshed runs default to visible
    }

    [ContextMenu("Refresh Run List")]
    void RefreshRunList()
    {
        List<RunFilter> updated = new List<RunFilter>();
        foreach (TextAsset asset in logFiles)
        {
            if (asset == null) continue;
            ParsedLog log = GetParsed(asset);
            HashSet<int> runsInLog = new HashSet<int>();
            foreach (LogSample s in log.samples) runsInLog.Add(s.run);

            foreach (int run in runsInLog)
            {
                RunFilter existing = runFilters.Find(rf => rf.log == asset && rf.run == run);
                updated.Add(new RunFilter
                {
                    log = asset,
                    run = run,
                    blighted = log.blightRuns.Contains(run),
                    visible = existing != null ? existing.visible : true
                });
            }
        }
        runFilters = updated;
    }

    Color RunColor(float hue, int run)
    {
        (float sat, float val) = RunShades[run % RunShades.Length];
        return Color.HSVToRGB(hue, sat, val);
    }

    ParsedLog GetParsed(TextAsset asset)
    {
        if (!parseCache.TryGetValue(asset, out ParsedLog log))
        {
            log = Parse(asset.text);
            parseCache[asset] = log;
        }
        return log;
    }

    [ContextMenu("Clear Parse Cache")]
    void ClearParseCache() => parseCache.Clear(); // run this if you replace a file's contents without changing its name

    ParsedLog Parse(string text)
    {
        ParsedLog log = new ParsedLog();

        bool inPause = false;
        float pauseStartX = 0f, pauseStartY = 0f, pauseStartTime = 0f;
        int pauseRun = 0;
        float lastTime = 0f;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] t = line.Split(',');
            if (t.Length < 7) continue;

            if (t[0] == "S")
            {
                float time = ParseFloat(t[2]);
                float x = ParseFloat(t[3]);
                float y = ParseFloat(t[4]);
                float speed = ParseFloat(t[5]);
                int run = ParseInt(t[6]);

                log.samples.Add(new LogSample { x = x, y = y, speed = speed, run = run });

                bool stationary = speed < pauseSpeedThreshold;
                if (inPause && run != pauseRun)
                {
                    // a death/respawn happened mid-pause — close the old run's pause before opening a new one
                    EndPause(log, pauseStartX, pauseStartY, lastTime - pauseStartTime, pauseRun);
                    inPause = false;
                }

                if (stationary && !inPause)
                {
                    inPause = true;
                    pauseStartX = x; pauseStartY = y; pauseStartTime = time; pauseRun = run;
                }
                else if (!stationary && inPause)
                {
                    EndPause(log, pauseStartX, pauseStartY, time - pauseStartTime, pauseRun);
                    inPause = false;
                }
                lastTime = time;
            }
            else if (t[0] == "E" && t[1] == "DIED")
            {
                log.deaths.Add(new LogDeath { x = ParseFloat(t[3]), y = ParseFloat(t[4]), run = ParseInt(t[6]) });
            }
            else if (t[0] == "E" && t[1] == "EXPOSURE")
            {
                log.exposures.Add(new ExposureMarker { x = ParseFloat(t[3]), y = ParseFloat(t[4]), run = ParseInt(t[6]) });
            }
            else if (t[0] == "E" && t[1] == "BLIGHTRUN")
            {
                log.blightRuns.Add(ParseInt(t[6]));
            }
        }
        if (inPause)
        {
            EndPause(log, pauseStartX, pauseStartY, lastTime - pauseStartTime, pauseRun);
        }

        return log;
    }

    void EndPause(ParsedLog log, float x, float y, float duration, int run)
    {
        if (duration >= minPauseDurationToShow)
        {
            log.pauses.Add(new PauseMarker { x = x, y = y, duration = duration, run = run });
        }
    }

    float ParseFloat(string s) { float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v); return v; }
    int ParseInt(string s) { int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v); return v; }
}