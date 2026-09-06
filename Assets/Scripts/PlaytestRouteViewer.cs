using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Editor-side review tool. Drag each returned .csv log into your Assets folder (Unity
// imports it as a TextAsset automatically), then drag those TextAssets into "Log Files"
// below — that's the entire multi-tester workflow. Each file gets its own hue so overlaid
// testers stay distinguishable; brightness within that hue encodes speed (dim = slow/hesitant,
// bright = fast/confident). Keep this off any GameObject in a shipped build.
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

    // Hue identifies the tester (file). Each entry here is a (saturation, brightness) pair,
    // cycled by run number, so consecutive attempts by the same tester stay visually distinct.
    static readonly (float sat, float val)[] RunShades =
    {
        (1f, 1f), (0.55f, 0.85f), (1f, 0.55f), (0.55f, 0.55f)
    };

    readonly Dictionary<TextAsset, ParsedLog> parseCache = new Dictionary<TextAsset, ParsedLog>();

    struct LogSample { public float x, y, speed; public int run; }
    struct LogDeath { public float x, y; }
    struct PauseMarker { public float x, y, duration; public int run; }

    class ParsedLog
    {
        public List<LogSample> samples = new List<LogSample>();
        public List<LogDeath> deaths = new List<LogDeath>();
        public List<PauseMarker> pauses = new List<PauseMarker>();
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

                    Gizmos.color = RunColor(hue, a.run);
                    Gizmos.DrawLine(new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
                }
            }

            if (drawPauses)
            {
                foreach (PauseMarker p in log.pauses)
                {
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
                    Gizmos.DrawWireCube(new Vector3(d.x, d.y, 0f), Vector3.one * deathMarkerSize);
                }
            }
        }
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
                log.deaths.Add(new LogDeath { x = ParseFloat(t[3]), y = ParseFloat(t[4]) });
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