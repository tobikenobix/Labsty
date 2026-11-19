using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Scenario 01 - VR Training Corridor.
///   Zone A (normal movement, obstacle + landmarks)  -> baseline camera motion
///   Zone B (fault zone)       -> optional injected forced camera roll (application-controlled)
///   Zone C (interaction zone) -> find / approach / press control panel, then exit
///
/// Fixed simulation step; per step and per agent:
///   Agent.Step -> Zone -> Fault (writes ApplicationCameraOffset only) -> Metrics.Measure -> Risk -> Metrics.Record
/// The fault never feeds back into the agent, so Baseline and Fault runs with the same seed give identical
/// agent decisions and trajectories. Supports 1-5 agents, each with its own profile, RNG, task state and logs.
/// </summary>
public class QAXRScenarioController : MonoBehaviour
{
    [Header("Scene references")]
    public QAXRAgentController primaryAgent;
    public Camera overviewCamera;
    public QAXRControlPanel controlPanel;
    public QAXRDebugVisualizer debugVisualizer;

    [Header("Run setup")]
    public string scenarioName = "Scenario01_TrainingCorridor";
    public bool injectFault = false;
    public int seed = 42;
    public QAXRProfileType profile = QAXRProfileType.Focused;
    [Range(1, 5)] public int agentCount = 1;
    public Vector3 startPosition = new Vector3(0f, 0f, 0.5f);
    public float simulationStep = 0.02f;
    public float maxRunSeconds = 150f;
    [Tooltip("Write CSV/JSON files at the end of each agent's run.")]
    public bool writeFiles = true;

    [Header("Multi-agent")]
    public QAXRProfileType[] extraAgentProfiles = { QAXRProfileType.Exploratory, QAXRProfileType.Cautious, QAXRProfileType.Focused, QAXRProfileType.Exploratory };
    public float startStagger = 4f;
    public int seedStride = 101;

    [Header("Environment randomness (separate from agent RNGs)")]
    public int environmentSeed = 7;
    [Tooltip("Random delay (s) between entering Zone B and fault onset. 0 = deterministic onset.")]
    public float faultOnsetJitter = 0f;

    [Header("Zones (z, metres)")]
    public float zoneBStartZ = 8f;
    public float zoneCStartZ = 14f;

    [Header("Injected fault: forced camera roll (application-controlled)")]
    public float faultRollAmplitude = 20f;   // deg
    public float faultRollFrequency = 1.2f;  // Hz  -> peak rate = 2*pi*f*A ~ 150 deg/s
    [Tooltip("Rounded to whole oscillation cycles so the roll ends at 0 deg.")]
    public float faultDuration = 4f;         // s

    public class AgentRun
    {
        public QAXRAgentController agent;
        public QAXRMetricsCollector metrics;
        public QAXRRiskDetector detector;
        public Transform appOffset;
        public Camera cam;
        public string runId;
        public QAXRZone zone;
        public bool faultActive, faultTriggered;
        public float faultTimer, faultDelay, faultTotal, appRoll, appRollRate;
        public bool ended;
        public string outcome = "";
        public float endTime;
        public float lastSimMs;
        public Vector3 startPos;
    }

    // ---- Runtime ----
    public readonly List<AgentRun> runs = new List<AgentRun>();
    public bool Running { get; private set; }
    public float SimTime { get; private set; }
    public string RunId { get; private set; } = "";
    public int SelectedIndex { get; private set; }
    public AgentRun Selected => runs.Count > 0 ? runs[Mathf.Clamp(SelectedIndex, 0, runs.Count - 1)] : null;
    public string LastPairResult => pairResult;

    /// <summary>(agentIndex, sample, agent) every simulation step. Used by validation tests.</summary>
    public event Action<int, QAXRMotionSample, QAXRAgentController> OnSample;

    private readonly List<QAXRAgentController> clones = new List<QAXRAgentController>();
    private readonly HashSet<QAXRAgentController> subscribed = new HashSet<QAXRAgentController>();
    private System.Random envRng;
    private float accumulator;
    private float faultEndTime;
    private bool paused;
    private bool showAgentView;
    private bool initialized;
    private int pairStage; // 0 none, 1 baseline running (fault next), 2 fault running
    private string pairResult = "";
    private string baselineLine = "";
    private ulong baselineHash;
    private string runResult = "";
    private bool showPanel = true;
    private readonly Stopwatch sw = new Stopwatch();

    private string ScenarioLabel => injectFault ? "InjectedFault" : "Baseline";

    // ---- Public controls (used by the on-screen panel and the VR controller buttons) ----
    public bool IsPaused => paused;
    public string ConditionLabel => ScenarioLabel;
    public string RunResult => runResult;
    public void RunSingle(bool withFault) { pairStage = 0; StartRun(withFault, seed); }
    public void StartABPair() { pairStage = 1; pairResult = ""; StartRun(false, seed); }
    public void TogglePause() { paused = !paused; }
    public void CycleProfile()
    {
        profile = (QAXRProfileType)(((int)profile + 1) % 3);
        pairStage = 0; StartRun(injectFault, seed);
    }
    public void CycleAgentCount()
    {
        agentCount = agentCount == 1 ? 3 : agentCount == 3 ? 5 : 1;
        pairStage = 0; StartRun(injectFault, seed);
    }
    public void SelectNextAgent()
    {
        SelectedIndex = runs.Count > 0 ? (SelectedIndex + 1) % runs.Count : 0;
        ApplyCameraView();
    }

    private void Start()
    {
        InitializeIfNeeded();
        if (!enabled) return;
        StartRun(injectFault, seed);
        ApplyCameraView();
    }

    public void InitializeIfNeeded()
    {
        if (initialized) return;
        if (primaryAgent == null) primaryAgent = FindAnyObjectByType<QAXRAgentController>();
        if (primaryAgent == null || primaryAgent.agentCamera == null)
        {
            Debug.LogError("QAXR: missing agent references. Use QAXR > Rebuild Demo Scene.");
            enabled = false;
            return;
        }
        initialized = true;
    }

    // ------------------------------------------------------------------ run lifecycle

    public void StartRun(bool withFault, int runSeed)
    {
        InitializeIfNeeded();
        injectFault = withFault;
        seed = runSeed;
        envRng = new System.Random(environmentSeed);
        RunId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + ScenarioLabel + "_" + profile + "_seed" + seed;
        SimTime = accumulator = 0f;
        float cycles = Mathf.Max(1f, Mathf.Round(faultDuration * faultRollFrequency));
        faultEndTime = cycles / faultRollFrequency;
        runResult = "";

        EnsureAgents();
        if (controlPanel != null) controlPanel.ResetPanel();
        runs.Clear();

        Vector3[] offsets = { Vector3.zero, new Vector3(-1.0f, 0f, 0f), new Vector3(1.0f, 0f, 0f), new Vector3(-0.6f, 0f, -1.1f), new Vector3(0.6f, 0f, -1.1f) };
        for (int i = 0; i < agentCount; i++)
        {
            QAXRAgentController a = i == 0 ? primaryAgent : clones[i - 1];
            a.agentId = "A" + (i + 1).ToString("00");
            if (i == 0) { if (profile != QAXRProfileType.Custom) a.profileType = profile; }
            else a.profileType = extraAgentProfiles.Length > 0 ? extraAgentProfiles[(i - 1) % extraAgentProfiles.Length] : profile;
            a.startDelay = i * startStagger;
            int agentSeed = seed + i * seedStride;

            var r = new AgentRun
            {
                agent = a,
                metrics = a.GetComponent<QAXRMetricsCollector>(),
                detector = a.GetComponent<QAXRRiskDetector>(),
                appOffset = a.appCameraOffset != null ? a.appCameraOffset : a.agentCamera.transform,
                cam = a.agentCamera,
                runId = RunId + "_" + a.agentId,
                zone = QAXRZone.None,
                startPos = startPosition + offsets[i],
                faultDelay = faultOnsetJitter > 0f ? (float)envRng.NextDouble() * faultOnsetJitter : 0f
            };
            if (r.metrics == null) r.metrics = a.gameObject.AddComponent<QAXRMetricsCollector>();
            if (r.detector == null) r.detector = a.gameObject.AddComponent<QAXRRiskDetector>();
            runs.Add(r);
            Subscribe(a, r);

            r.appOffset.localRotation = Quaternion.identity;
            r.detector.ResetDetector();
            string profileName = a.profileType == QAXRProfileType.Custom ? a.customProfile.profileName : a.profileType.ToString();
            r.metrics.BeginRun(r.runId, a.agentId, agentSeed, ScenarioLabel, profileName);
            r.metrics.LogEvent(0f, "RunStart", QAXRZone.None, startPosition + offsets[i],
                "scenario=" + scenarioName + " condition=" + ScenarioLabel + " seed=" + agentSeed + " profile=" + profileName +
                " agents=" + agentCount + " step=" + simulationStep + " env_seed=" + environmentSeed);
            a.ResetAgent(agentSeed, startPosition + offsets[i], 0f);
        }
        SelectedIndex = Mathf.Clamp(SelectedIndex, 0, runs.Count - 1);
        Running = true;
        paused = false;
        ApplyCameraView();
        if (debugVisualizer != null) debugVisualizer.Bind(this);
        Debug.Log("QAXR: " + ScenarioLabel + " run started (seed " + seed + ", profile " + profile + ", agents " + agentCount + ").");
    }

    private void EnsureAgents()
    {
        while (clones.Count < agentCount - 1)
        {
            GameObject go = Instantiate(primaryAgent.gameObject, primaryAgent.transform.parent);
            go.name = "Simulated Agent A" + (clones.Count + 2).ToString("00") + " (spawned)";
            QAXRAgentController c = go.GetComponent<QAXRAgentController>();
            QAXRRiskDetector d = go.GetComponent<QAXRRiskDetector>();
            if (d != null) d.issueMarker = null;
            if (c.agentCamera != null) c.agentCamera.enabled = false;
            AudioListener al = c.agentCamera != null ? c.agentCamera.GetComponent<AudioListener>() : null;
            if (al != null) al.enabled = false;
            clones.Add(c);
        }
        for (int i = 0; i < clones.Count; i++)
            clones[i].gameObject.SetActive(i < agentCount - 1);
    }

    /// <summary>Removes spawned agents (used by tests / when reducing agent count).</summary>
    public void DestroySpawnedAgents()
    {
        foreach (var c in clones)
        {
            if (c == null) continue;
            if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
        }
        clones.Clear();
    }

    private void Subscribe(QAXRAgentController a, AgentRun r)
    {
        if (subscribed.Contains(a)) return;
        subscribed.Add(a);
        a.OnEvent += (t, d) => LogEvent(a, t, d);
        QAXRRiskDetector det = r.detector;
        det.OnEvent += (t, d) => LogEvent(a, t, d);
    }

    private void Update()
    {
        if (!Running || paused) return;
        accumulator += Mathf.Min(Time.deltaTime, 0.25f);
        while (accumulator >= simulationStep && Running)
        {
            accumulator -= simulationStep;
            Step(simulationStep);
        }
    }

    /// <summary>Run synchronously until the run ends (headless tests).</summary>
    public void RunToEnd(int maxSteps = 500000)
    {
        int n = 0;
        while (Running && n++ < maxSteps) Step(simulationStep);
    }

    private void Step(float dt)
    {
        SimTime += dt;
        for (int i = 0; i < runs.Count; i++)
        {
            AgentRun r = runs[i];
            if (r.ended) continue;

            sw.Reset(); sw.Start();
            r.agent.Step(dt);
            sw.Stop();
            r.lastSimMs = (float)sw.Elapsed.TotalMilliseconds;

            UpdateZone(r);
            UpdateFault(r, dt);

            QAXRMotionSample s = r.metrics.Measure(SimTime, dt, r.cam, r.agent, r.zone, r.faultActive, r.appRoll, r.appRollRate, r.lastSimMs);
            r.detector.Evaluate(ref s);
            r.metrics.Record(s);
            OnSample?.Invoke(i, s, r.agent);

            if (r.agent.Done && !r.faultActive)
                EndAgent(r, r.agent.MissionComplete ? "completed" : "failed: " + r.agent.FailureReason);
        }

        if (SimTime >= maxRunSeconds)
            foreach (AgentRun r in runs)
                if (!r.ended) EndAgent(r, "timeout after " + maxRunSeconds.ToString("F0") + " s");

        bool all = true;
        foreach (AgentRun r in runs) all &= r.ended;
        if (all) EndRun();
    }

    private void UpdateZone(AgentRun r)
    {
        float z = r.agent.transform.position.z;
        QAXRZone now = z < zoneBStartZ ? QAXRZone.A_Normal : (z < zoneCStartZ ? QAXRZone.B_Fault : QAXRZone.C_Interaction);
        if (now == r.zone) return;
        r.zone = now;
        LogEvent(r.agent, "ZoneEnter", now.ToString());
        if (now == QAXRZone.B_Fault && injectFault && !r.faultTriggered)
        {
            r.faultTriggered = true;
            r.faultTimer = -r.faultDelay;
            LogEvent(r.agent, "FaultArmed", "onset_delay=" + r.faultDelay.ToString("F2", CultureInfo.InvariantCulture) + "s");
        }
    }

    private void UpdateFault(AgentRun r, float dt)
    {
        float prevRoll = r.appRoll;
        if (r.faultTriggered && r.faultTimer < faultEndTime)
        {
            r.faultTimer += dt;
            if (r.faultTimer > 0f && !r.faultActive)
            {
                r.faultActive = true;
                LogEvent(r.agent, "FaultStart", "forced_roll amp=" + faultRollAmplitude + "deg freq=" + faultRollFrequency +
                                               "Hz duration=" + faultEndTime.ToString("F2", CultureInfo.InvariantCulture) + "s");
            }
            if (r.faultActive)
            {
                r.faultTotal += dt;
                if (r.faultTimer >= faultEndTime)
                {
                    r.faultActive = false;
                    r.appRoll = 0f;
                    LogEvent(r.agent, "FaultEnd", "duration=" + r.faultTotal.ToString("F2", CultureInfo.InvariantCulture) + "s");
                }
                else r.appRoll = faultRollAmplitude * Mathf.Sin(2f * Mathf.PI * faultRollFrequency * r.faultTimer);
            }
        }
        else r.appRoll = 0f;

        r.appRollRate = (r.appRoll - prevRoll) / dt;
        r.appOffset.localRotation = Quaternion.Euler(0f, 0f, r.appRoll); // application camera offset only
    }

    private void EndAgent(AgentRun r, string outcome)
    {
        r.ended = true;
        r.outcome = outcome;
        r.endTime = SimTime;
        r.faultActive = false;
        r.appOffset.localRotation = Quaternion.identity;
        r.detector.Finish();
        LogEvent(r.agent, "RunEnd", outcome);
        if (!outcome.StartsWith("completed")) Debug.LogWarning("QAXR " + r.agent.agentId + ": run ended - " + outcome);

        if (writeFiles)
        {
            string prefix = r.metrics.Save(SummaryHeader, SummaryRow(r), ConfigJson(r));
            if (r == runs[0]) Debug.Log("QAXR data saved: " + prefix + "_*.csv / _config.json");
        }
    }

    private void EndRun()
    {
        Running = false;
        AgentRun p = runs[0];
        QAXRAgentController a = p.agent;
        runResult = a.agentId + " " + p.outcome + " in " + (p.endTime - a.startDelay).ToString("F1") + " s. " + p.detector.LastFinding;
        Debug.Log("QAXR run finished: " + runResult);

        string line = ScenarioLabel + ": " + (p.endTime - a.startDelay).ToString("F1") + " s, max roll rate " +
                      p.metrics.MaxRollRate.ToString("F0") + " deg/s, episodes " + p.detector.Episodes.Count;
        if (pairStage == 1)
        {
            baselineLine = line;
            baselineHash = a.DecisionHash;
            pairStage = 2;
            StartRun(true, seed);
        }
        else if (pairStage == 2)
        {
            pairStage = 0;
            bool same = baselineHash == a.DecisionHash;
            pairResult = "A/B seed " + seed + " (" + a.Profile.profileName + "): agent decisions " + (same ? "IDENTICAL" : "DIFFERENT") +
                         "\n  " + baselineLine + "\n  " + line;
            Debug.Log("QAXR " + pairResult);
        }
    }

    private void LogEvent(QAXRAgentController a, string type, string detail)
    {
        foreach (AgentRun r in runs)
            if (r.agent == a) { r.metrics.LogEvent(SimTime, type, r.zone, a.transform.position, detail); return; }
    }

    // ------------------------------------------------------------------ reporting

    public const string SummaryHeader =
        // v1 columns (same order). idle_* now means the Stop action, turn_count the TurnBody action.
        "run_id,agent_id,seed,scenario,condition,mission_completed,button_pressed,duration_s," +
        "time_zone_A_s,time_zone_B_s,time_zone_C_s,walk_count,lookaround_count,idle_count,turn_count,total_idle_s," +
        "max_speed_mps,max_roll_rate_degps,max_angular_speed_degps,fault_injected,fault_duration_s," +
        "risk_episodes,risk_total_s,first_risk_time_s,first_risk_zone,first_risk_pos_z_m,risk_threshold_degps," +
        // v2 appended columns
        "profile,outcome,decisions,decision_hash,slowdown_count,lookattarget_count,inspect_count,mean_speed_mps," +
        "head_travel_deg,obstacle_corrections,start_delay_s,agent_index,agent_count,agent_sim_ms_per_step";

    private string SummaryRow(AgentRun r)
    {
        QAXRAgentController a = r.agent;
        var ep = r.detector.Episodes;
        int[] c = a.actionCounts;
        float steps = Mathf.Max(1, r.metrics.StepCount);
        return string.Join(",",
            r.runId, a.agentId, a.randomSeed, scenarioName, ScenarioLabel,
            a.MissionComplete ? 1 : 0, a.ButtonPressed ? 1 : 0, F(r.endTime - a.startDelay),
            F(r.metrics.ZoneSeconds(QAXRZone.A_Normal)), F(r.metrics.ZoneSeconds(QAXRZone.B_Fault)), F(r.metrics.ZoneSeconds(QAXRZone.C_Interaction)),
            c[(int)QAXRAgentAction.Walk], c[(int)QAXRAgentAction.LookAround], c[(int)QAXRAgentAction.Stop], c[(int)QAXRAgentAction.TurnBody],
            F(a.TotalStopSeconds),
            F(r.metrics.MaxSpeed), F(r.metrics.MaxRollRate), F(r.metrics.MaxAngularSpeed), injectFault ? 1 : 0, F(r.faultTotal),
            ep.Count, F(r.detector.TotalRiskSeconds),
            ep.Count > 0 ? F(ep[0].startTime) : "", ep.Count > 0 ? ep[0].zone.ToString() : "",
            ep.Count > 0 ? F(ep[0].position.z) : "", F(r.detector.rollRateThreshold),
            a.Profile.profileName, Csv(r.outcome), a.DecisionCount, a.DecisionHash.ToString("X16"),
            c[(int)QAXRAgentAction.SlowDown], c[(int)QAXRAgentAction.LookAtTarget], c[(int)QAXRAgentAction.InspectObject],
            F(r.metrics.SumSpeed / steps), F(r.metrics.SumHeadAngularTravel), a.motion.constraintCorrections,
            F(a.startDelay), runs.IndexOf(r), runs.Count, (r.metrics.SumAgentSimMs / steps).ToString("F4", CultureInfo.InvariantCulture));
    }

    private string ConfigJson(AgentRun r)
    {
        QAXRAgentController a = r.agent;
        var sb = new StringBuilder();
        sb.Append("{\n");
        J(sb, "schema", "qaxr-run-config/2"); J(sb, "run_id", r.runId); J(sb, "agent_id", a.agentId);
        J(sb, "agent_index", runs.IndexOf(r)); J(sb, "agent_count", runs.Count);
        J(sb, "seed", a.randomSeed); J(sb, "base_seed", seed); J(sb, "seed_stride", seedStride);
        J(sb, "environment_seed", environmentSeed); J(sb, "condition", ScenarioLabel); J(sb, "scenario", scenarioName);
        J(sb, "profile_type", a.profileType.ToString()); J(sb, "start_delay_s", a.startDelay);
        J(sb, "start_position", V(r.startPos));
        J(sb, "simulation_step_s", simulationStep); J(sb, "sample_interval_s", r.metrics.sampleInterval);
        J(sb, "max_run_seconds", maxRunSeconds); J(sb, "zone_b_start_z", zoneBStartZ); J(sb, "zone_c_start_z", zoneCStartZ);
        J(sb, "fault_injected", injectFault); J(sb, "fault_roll_amplitude_deg", faultRollAmplitude);
        J(sb, "fault_roll_frequency_hz", faultRollFrequency); J(sb, "fault_duration_s", faultEndTime);
        J(sb, "fault_onset_jitter_s", faultOnsetJitter); J(sb, "risk_threshold_degps", r.detector.rollRateThreshold);
        J(sb, "outcome", r.outcome); J(sb, "decision_hash", a.DecisionHash.ToString("X16"));
        J(sb, "unity_version", Application.unityVersion);
        sb.Append("  \"profile\": ").Append(JsonUtility.ToJson(a.Profile, true).Replace("\n", "\n  ")).Append(",\n");
        sb.Append("  \"perception\": ").Append(JsonUtility.ToJson(a.perception)).Append(",\n");
        sb.Append("  \"motion\": ").Append(JsonUtility.ToJson(a.motion)).Append(",\n");
        sb.Append("  \"mission_name\": \"").Append(Esc(a.missionName)).Append("\",\n");
        sb.Append("  \"mission\": [\n");
        for (int i = 0; i < a.mission.Count; i++)
        {
            QAXRSubgoal g = a.mission[i];
            sb.Append("    {\"name\":\"").Append(Esc(g.name)).Append("\",\"type\":\"").Append(g.type)
              .Append("\",\"position\":").Append(V(g.position)).Append(",\"target\":\"").Append(g.target != null ? Esc(g.target.displayName) : "")
              .Append("\",\"completion_radius\":").Append(F(g.completionRadius)).Append(",\"pass_z\":").Append(g.completeWhenPassedZ ? "true" : "false")
              .Append(",\"timeout_s\":").Append(F(g.timeout)).Append('}').Append(i < a.mission.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static void J(StringBuilder sb, string k, string v) => sb.Append("  \"").Append(k).Append("\": \"").Append(Esc(v)).Append("\",\n");
    private static void J(StringBuilder sb, string k, float v) => sb.Append("  \"").Append(k).Append("\": ").Append(F(v)).Append(",\n");
    private static void J(StringBuilder sb, string k, int v) => sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n");
    private static void J(StringBuilder sb, string k, bool v) => sb.Append("  \"").Append(k).Append("\": ").Append(v ? "true" : "false").Append(",\n");
    private static void J(StringBuilder sb, string k, Vector3Json v) => sb.Append("  \"").Append(k).Append("\": ").Append(v.s).Append(",\n");
    private struct Vector3Json { public string s; }
    private static Vector3Json V(Vector3 v) => new Vector3Json { s = "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]" };
    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Csv(string v) => string.IsNullOrEmpty(v) ? "" : (v.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);
    private static string F(float v) => QAXRMetricsCollector.F(v, 3);

    // ------------------------------------------------------------------ view

    private void ApplyCameraView()
    {
        for (int i = 0; i < runs.Count; i++)
        {
            bool sel = showAgentView && i == SelectedIndex;
            QAXRAgentController a = runs[i].agent;
            if (a.agentCamera != null)
            {
                a.agentCamera.enabled = sel;
                AudioListener l = a.agentCamera.GetComponent<AudioListener>();
                if (l != null) l.enabled = sel;
            }
            a.SetBodyVisible(!sel && a.Active);
        }
        if (overviewCamera != null)
        {
            overviewCamera.enabled = !showAgentView;
            AudioListener l = overviewCamera.GetComponent<AudioListener>();
            if (l != null) l.enabled = !showAgentView;
        }
    }

    // ------------------------------------------------------------------ UI

    private GUIStyle h1, body, small;

    private void OnGUI()
    {
        if (h1 == null)
        {
            h1 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        }
        if (!showPanel)
        {
            if (GUI.Button(new Rect(14, 14, 120, 24), "Show QAXR panel")) showPanel = true;
            return;
        }

        AgentRun r = Selected;
        QAXRAgentController a = r != null ? r.agent : null;
        QAXRMotionSample s = r != null ? r.metrics.Current : default;
        const float x = 24f, w = 420f;
        GUI.Box(new Rect(12, 12, w + 24, 720), "");
        float y = 18f;
        Line(ref y, "QAXR | Human-like Simulated Agent Test", h1, 22);
        if (GUI.Button(new Rect(x + w - 50, 18, 50, 20), "Hide")) showPanel = false;
        Line(ref y, "Condition " + ScenarioLabel.ToUpper() + "   Status " + (Running ? (paused ? "PAUSED" : "RUNNING") : "FINISHED") + "   t=" + SimTime.ToString("F1") + " s");
        if (a != null)
        {
            Line(ref y, "Agent " + a.agentId + " (" + (SelectedIndex + 1) + "/" + runs.Count + ")   Profile " + (a.Profile != null ? a.Profile.profileName : "-") + "   Seed " + a.randomSeed + "   Zone " + r.zone);
            Line(ref y, "Mission: " + a.CurrentTask, small, 30);
            Line(ref y, "Subgoal: " + a.SubgoalLabel);
            Line(ref y, "Action: " + a.Action + "   Attention (head): " + a.AttentionName);
            Line(ref y, "Why: " + a.LastReason, small, 30);
            Line(ref y, "Task: " + (a.MissionComplete ? "COMPLETED" : a.MissionFailed ? "FAILED - " + a.FailureReason : "in progress") + "   Button " + (a.ButtonPressed ? "pressed" : "not pressed"));
        }
        y += 2;
        Line(ref y, "D1 walk " + s.speed.ToString("F2") + " m/s   body turn " + s.bodyYawRate.ToString("F0") + " deg/s   head " + s.headYawRate.ToString("F0") + "/" + s.headPitchRate.ToString("F0") + " deg/s");
        Line(ref y, "D1 cam yaw/pitch/roll rate " + s.yawRate.ToString("F0") + " / " + s.pitchRate.ToString("F0") + " / " + s.rollRate.ToString("F0") + " deg/s");
        Line(ref y, "D2 source " + s.source + "   app camera roll " + s.appRoll.ToString("F1") + " deg");
        Line(ref y, "D3 FOV " + s.fov.ToString("F0") + "   obstacle " + (float.IsInfinity(s.nearestObstacle) ? "-" : s.nearestObstacle.ToString("F1") + " m"));
        Line(ref y, "D4 frame " + s.frameTimeMs.ToString("F1") + " ms (Editor)   agent sim " + s.agentSimMs.ToString("F3") + " ms");
        Line(ref y, "D5 fault " + (r != null ? r.faultTotal.ToString("F2") : "0") + " s   risk " + (r != null ? r.detector.TotalRiskSeconds.ToString("F2") : "0") + " s");
        GUI.Label(new Rect(x, y, w, 30), "Result: " + (r != null ? r.detector.LastFinding : ""), small); y += 30;
        if (!string.IsNullOrEmpty(runResult)) { GUI.Label(new Rect(x, y, w, 30), runResult, small); y += 30; }
        if (!string.IsNullOrEmpty(pairResult)) { GUI.Label(new Rect(x, y, w, 48), pairResult, small); y += 48; }

        if (a != null)
        {
            GUI.Label(new Rect(x, y, w, 16), "Recent actions:", small); y += 16;
            foreach (string ra in a.recentActions) { GUI.Label(new Rect(x + 8, y, w, 15), ra, small); y += 15; }
        }

        y = Mathf.Max(y + 4, 470f);
        float bw = (w - 12) / 3f;
        if (GUI.Button(new Rect(x, y, bw, 24), "Run Baseline")) RunSingle(false);
        if (GUI.Button(new Rect(x + bw + 6, y, bw, 24), "Run Fault")) RunSingle(true);
        if (GUI.Button(new Rect(x + 2 * (bw + 6), y, bw, 24), "A/B Pair")) StartABPair();
        y += 28;
        if (GUI.Button(new Rect(x, y, bw, 24), "Replay (same seed)")) RunSingle(injectFault);
        if (GUI.Button(new Rect(x + bw + 6, y, bw, 24), "New seed")) { pairStage = 0; StartRun(injectFault, seed + 1); }
        if (GUI.Button(new Rect(x + 2 * (bw + 6), y, bw, 24), paused ? "Resume" : "Pause")) TogglePause();
        y += 28;
        if (GUI.Button(new Rect(x, y, bw, 24), "Profile: " + profile)) CycleProfile();
        if (GUI.Button(new Rect(x + bw + 6, y, bw, 24), "Agents: " + agentCount)) CycleAgentCount();
        if (GUI.Button(new Rect(x + 2 * (bw + 6), y, bw, 24), "Next agent")) SelectNextAgent();
        y += 28;
        if (GUI.Button(new Rect(x, y, w, 24), showAgentView ? "Switch to Overview" : "Switch to Agent View (selected agent)"))
        {
            showAgentView = !showAgentView;
            ApplyCameraView();
        }
        y += 28;
        if (debugVisualizer != null)
        {
            float tw = w / 5f;
            debugVisualizer.showPath = GUI.Toggle(new Rect(x, y, tw, 20), debugVisualizer.showPath, "Path");
            debugVisualizer.showHead = GUI.Toggle(new Rect(x + tw, y, tw, 20), debugVisualizer.showHead, "Head");
            debugVisualizer.showPerception = GUI.Toggle(new Rect(x + 2 * tw, y, tw * 1.2f, 20), debugVisualizer.showPerception, "Perception");
            debugVisualizer.showTarget = GUI.Toggle(new Rect(x + 3.2f * tw, y, tw, 20), debugVisualizer.showTarget, "Target");
            debugVisualizer.showAllAgents = GUI.Toggle(new Rect(x + 4.1f * tw, y, tw, 20), debugVisualizer.showAllAgents, "All");
            y += 22;
        }
        GUI.Label(new Rect(x, y, w, 46), "Rule: app-controlled roll AND roll rate >= 60 deg/s (demo threshold, NOT validated). " +
            "Agent behaviour = heuristics, not validated human data. Head direction is simulated, not eye gaze.", small);
        y += 46;
        if (r != null) GUI.Label(new Rect(x, y, w, 30), "Data: " + (r.metrics.LastSavedPrefix != "" ? r.metrics.LastSavedPrefix + "_*" : r.metrics.OutputFolder), small);

        void Line(ref float yy, string text, GUIStyle st = null, float hgt = 18f)
        {
            GUI.Label(new Rect(x, yy, w, hgt), text, st ?? body);
            yy += hgt;
        }
    }
}
