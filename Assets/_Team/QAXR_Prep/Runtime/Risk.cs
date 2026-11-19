using System;
using System.Collections.Generic;
using UnityEngine;

namespace QAXR
{
    public enum Severity { None, Low, Medium, High }

    /// <summary>One row of the D1–D5 rule table (Roadmap §4.3). Thresholds are [ph] unless evidence says [lit].</summary>
    [Serializable]
    public class RiskRule
    {
        public string id;            // e.g. "ArtificialSpeed", becomes Issue.rule
        public string dimension;     // D1..D5, Usability
        public string metric;
        public string unit;
        public float threshold;
        public float minDuration;    // s the violation must last; 0 = any frame
        public bool flagBelow;       // too slow / too small
        public bool boolean;         // present/absent rules: value 1 = violated
        public Severity booleanSeverity = Severity.Medium;
        public string evidence;      // "lit" or "ph"
        public string source;

        public EpisodeDetector NewDetector() => new EpisodeDetector(boolean ? 0.5f : threshold, minDuration, !boolean && flagBelow);
    }

    /// <summary>Scoring rules of §4.4: exceedance ratio → severity bands, zone risk weights.</summary>
    public static class RiskScoring
    {
        /// <summary>r = value / threshold, or threshold / value for flag-below rules. ≥ 1 means violated.</summary>
        public static float Exceedance(float value, float threshold, bool flagBelow)
        {
            if (flagBelow) return value <= 0f ? float.PositiveInfinity : threshold / value;
            return threshold <= 0f ? float.PositiveInfinity : value / threshold;
        }

        /// <summary>Low 1–1.5×, Medium 1.5–2×, High > 2× [ph]. Below 1 is None.</summary>
        public static Severity FromExceedance(float r)
            => r < 1f ? Severity.None : r < 1.5f ? Severity.Low : r <= 2f ? Severity.Medium : Severity.High;

        public static Severity Rate(RiskRule rule, float value)
            => rule.boolean ? (value >= 0.5f ? rule.booleanSeverity : Severity.None)
                            : FromExceedance(Exceedance(value, rule.threshold, rule.flagBelow));

        /// <summary>Weight of one flagged agent in a zone cell: Low 1/3, Medium 2/3, High 1.</summary>
        public static float Weight(Severity s) => (int)s / 3f;
    }

    /// <summary>The starting rule table of §4.3, until W2 fills the RiskRules asset. Every value here is a placeholder to tune.</summary>
    public static class RiskRuleTable
    {
        public static List<RiskRule> Defaults() => new List<RiskRule>
        {
            R("ArtificialSpeed", "D1", "artificial linear speed (teleports excluded)", "m/s", 1.4f, 1f, false, "lit", "natural walking 1.2–1.4 m/s; Oh & Son 2022"),
            R("ArtificialAcceleration", "D1", "artificial linear acceleration (teleports excluded)", "m/s²", 0.5f, 0.25f, false, "ph", "Oh & Son 2022"),
            R("ArtificialYawRate", "D1", "smooth artificial yaw rate (snap turns excluded)", "deg/s", 45f, 0f, false, "ph", "Oh & Son 2022"),
            R("ArtificialPitchRollRate", "D1", "artificial pitch or roll rate", "deg/s", 5f, 0f, false, "ph", "Oh & Son 2022"),
            R("UncontrolledTranslation", "D2", "origin speed with no locomotion command", "m/s", 0.1f, 0f, false, "ph", "Oh & Son 2022; Palmisano et al. 2020"),
            R("UncontrolledRotation", "D2", "origin angular speed with no locomotion command", "deg/s", 5f, 0f, false, "ph", "Oh & Son 2022; Palmisano et al. 2020"),
            R("HeadTrackingOverridePosition", "D2", "camera local position minus tracked position", "m", 0.01f, 0f, false, "ph", "Palmisano et al. 2020"),
            R("HeadTrackingOverrideRotation", "D2", "camera local rotation minus tracked rotation", "deg", 1f, 0f, false, "ph", "Palmisano et al. 2020"),
            B("NoMitigationDuringMotion", "D3", "continuous artificial motion with no vignette / FOV restriction", Severity.Medium, "lit", "Fernandes & Feiner 2016"),
            B("NoStableReference", "D3", "artificial motion with no QaxrVisualReference in view", Severity.Medium, "ph", "Oh & Son 2022; Tian et al. 2022"),
            R("UITargetTooSmall", "D3", "angular size of an interactive UI target", "deg", 2f, 0f, true, "lit", "Kim et al. 2018 (1.9° vs 3.8°): direction, not threshold"),
            B("GazeDwellSelection", "D3", "gaze-dwell selection in use", Severity.Low, "lit", "Kim et al. 2018"),
            R("FrameTimeOverBudget", "D4", "frame time / refresh budget", "×", 1.5f, 0f, false, "ph", "Caserman et al. 2021; Meta OVR Metrics"),
            R("StaleFrameShare", "D4", "share of over-budget frames in a 2 s window", "share", 0.05f, 0f, false, "ph", "Meta OVR Metrics"),
            R("LongMotionEpisode", "D5", "artificial-motion episode length", "s", 10f, 0f, false, "ph", "Saredakis et al. 2020"),
            R("CumulativeMotionShare", "D5", "artificial-motion time / session time", "share", 0.3f, 0f, false, "ph", "Saredakis et al. 2020"),
            R("LocomotionTooSlow", "Usability", "continuous move speed", "m/s", 0.5f, 1f, true, "ph", "team notes: min movement speed"),
        };

        static RiskRule R(string id, string dim, string metric, string unit, float threshold, float minDuration, bool below, string evidence, string source)
            => new RiskRule { id = id, dimension = dim, metric = metric, unit = unit, threshold = threshold, minDuration = minDuration, flagBelow = below, evidence = evidence, source = source };

        static RiskRule B(string id, string dim, string metric, Severity severity, string evidence, string source)
            => new RiskRule { id = id, dimension = dim, metric = metric, unit = "bool", threshold = 1f, boolean = true, booleanSeverity = severity, evidence = evidence, source = source };
    }

    /// <summary>One issue, with the fields of §4.6 (JsonUtility-friendly: position is a float[3]). J1's Issue record replaces this on merge.</summary>
    [Serializable]
    public class Issue
    {
        public string id, dimension, rule, severity, unit, profile, intent, scene, commit;
        public string detail;         // free text: exception message, object name, ...
        public float value, threshold, time;
        public float[] position = new float[3];
        public int agent, seed;
        public int hits = 1;          // episodes merged into this cluster
        public List<int> agents = new List<int>();

        public Vector3 Position
        {
            get => new Vector3(position[0], position[1], position[2]);
            set => position = new[] { value.x, value.y, value.z };
        }

        public Severity SeverityLevel => Enum.TryParse(severity, out Severity s) ? s : Severity.None;
    }

    /// <summary>The single reporting seam until J1's IssueLog exists: oracles and rules call Report, IssueLog subscribes.</summary>
    public static class IssueSink
    {
        public static event Action<Issue> Reported;

        public static void Report(Issue issue)
        {
            if (string.IsNullOrEmpty(issue.commit)) issue.commit = BuildInfo.Commit;
            if (Reported != null) Reported(issue);
            else Debug.LogWarning($"[QAXR] {issue.severity} {issue.dimension}/{issue.rule} = {issue.value:0.###} {issue.unit} (threshold {issue.threshold}) at {issue.Position} t={issue.time:0.0}s agent {issue.agent}");
        }
    }

    /// <summary>
    /// Merges episodes of the same rule within 0.5 m and 2 s (§4.4) into one issue cluster.
    /// The cluster keeps its first position and time, the worst severity and value, and every agent that hit it.
    /// </summary>
    public sealed class IssueClusterer
    {
        public float radius = 0.5f, window = 2f;
        public readonly List<Issue> Clusters = new List<Issue>();

        /// <summary>Adds an episode; returns the cluster it joined (or the new one) and whether it was new.</summary>
        // ponytail: linear scan over clusters, fine for hundreds of issues; grid-hash it if runs produce thousands.
        public Issue Add(Issue e, out bool isNew)
        {
            foreach (var c in Clusters)
            {
                if (c.rule != e.rule || Mathf.Abs(c.time - e.time) > window || Vector3.Distance(c.Position, e.Position) > radius) continue;
                c.hits++;
                c.time = Mathf.Max(c.time, e.time); // the window slides with the latest episode
                if (!c.agents.Contains(e.agent)) c.agents.Add(e.agent);
                if (e.SeverityLevel > c.SeverityLevel) { c.severity = e.severity; c.value = e.value; }
                isNew = false;
                return c;
            }
            var n = Clone(e);
            if (!n.agents.Contains(e.agent)) n.agents.Add(e.agent);
            Clusters.Add(n);
            isNew = true;
            return n;
        }

        static Issue Clone(Issue e) => JsonUtility.FromJson<Issue>(JsonUtility.ToJson(e));
    }

    /// <summary>
    /// Zone risk on a 1 m grid (§4.4): a cell's risk is the severity-weighted share of agents that passed through
    /// the cell and were flagged there. One agent counts once per cell, with its worst severity. 0..1.
    /// </summary>
    public sealed class ZoneRiskGrid
    {
        public readonly float cellSize;
        readonly Dictionary<Vector2Int, HashSet<int>> visited = new Dictionary<Vector2Int, HashSet<int>>();
        readonly Dictionary<Vector2Int, Dictionary<int, Severity>> flagged = new Dictionary<Vector2Int, Dictionary<int, Severity>>();

        public ZoneRiskGrid(float cellSize = 1f) => this.cellSize = cellSize;

        public Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
        public Vector3 CellCenter(Vector2Int c, float y = 0f) => new Vector3((c.x + 0.5f) * cellSize, y, (c.y + 0.5f) * cellSize);

        public void Visit(int agent, Vector3 position)
        {
            var c = Cell(position);
            if (!visited.TryGetValue(c, out var set)) visited[c] = set = new HashSet<int>();
            set.Add(agent);
        }

        public void Flag(int agent, Vector3 position, Severity severity)
        {
            Visit(agent, position);
            var c = Cell(position);
            if (!flagged.TryGetValue(c, out var map)) flagged[c] = map = new Dictionary<int, Severity>();
            if (!map.TryGetValue(agent, out var old) || severity > old) map[agent] = severity;
        }

        public float Risk(Vector2Int cell)
        {
            if (!visited.TryGetValue(cell, out var agents) || agents.Count == 0 || !flagged.TryGetValue(cell, out var map)) return 0f;
            float sum = 0f;
            foreach (var s in map.Values) sum += RiskScoring.Weight(s);
            return sum / agents.Count;
        }

        public IEnumerable<KeyValuePair<Vector2Int, float>> Cells()
        {
            foreach (var c in visited.Keys) yield return new KeyValuePair<Vector2Int, float>(c, Risk(c));
        }
    }

    /// <summary>
    /// Time-weighted mean of a signal over the last `window` seconds (the LiveTuner's 5–10 s rolling score).
    /// Feed it e.g. RiskScoring.Weight(worst severity active this frame), 0 when nothing is violated.
    /// </summary>
    public sealed class RollingAverage
    {
        readonly float window;
        readonly Queue<(float t, float dt, float v)> samples = new Queue<(float, float, float)>();
        float weighted, time, lastT = float.NaN;

        public RollingAverage(float window) => this.window = window;

        public float Value => time > 0f ? weighted / time : 0f;

        public void Add(float t, float value)
        {
            float dt = float.IsNaN(lastT) ? 0f : Mathf.Max(0f, t - lastT);
            lastT = t;
            samples.Enqueue((t, dt, value));
            weighted += dt * value;
            time += dt;
            while (samples.Count > 0 && samples.Peek().t <= t - window)
            {
                var old = samples.Dequeue();
                weighted -= old.dt * old.v;
                time -= old.dt;
            }
        }

        public void Reset()
        {
            samples.Clear();
            weighted = time = 0f;
            lastT = float.NaN;
        }
    }
}
