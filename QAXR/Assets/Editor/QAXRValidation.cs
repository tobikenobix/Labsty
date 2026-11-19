#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless acceptance tests (QAXR > Run Validation Tests).
/// Builds the corridor in a temporary scene, runs many simulations synchronously with the fixed step
/// and checks Tests 1-9 plus multi-agent support. Writes a report to QAXR_Runs/validation/.
/// </summary>
public static class QAXRValidation
{
    private const string ScenePath = "Assets/Scenes/QAXR_Demo.unity";

    private class Rec
    {
        public QAXRProfileType profile; public int seed; public bool fault;
        public string outcome = ""; public bool completed, failed, button; public float duration;
        public ulong decisionHash, trajHash = 1469598103934665603UL;
        public int[] counts;
        public float maxRollRate, sumSpeed, sumMovingSpeed; public int steps, movingSteps;
        public float corridorSpeedSum, approachSpeedSum; public int corridorSteps, approachSteps, approachAttn;
        public int randomInApproach;
        public float headTravel;
        public float maxLongAccel, maxBodyRate, maxHeadRate, maxPosJump, maxCamYawStep;
        public float accelLimit, turnLimit, headLimit, speedLimit;
        public float maxAbsX, minCrateDist = float.MaxValue, minAgentSep = float.MaxValue;
        public int sourceErrors, rollErrors, yawRateErrors;
        public int episodes, corrections;
        public List<bool> agentsCompleted = new List<bool>();
        public string failure = "";
    }

    [MenuItem("QAXR/Run Validation Tests")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("QAXR: stop Play mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(RunAll(true));
    }

    public static string RunAll(bool restoreScene)
    {
        var report = new StringBuilder();
        int pass = 0, fail = 0;
        void Result(string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            report.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!string.IsNullOrEmpty(detail)) report.AppendLine("       " + detail.Replace("\n", "\n       "));
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        QAXRSceneBuilder.Build();
        var s = UnityEngine.Object.FindAnyObjectByType<QAXRScenarioController>();
        s.InitializeIfNeeded();
        s.writeFiles = false;
        Transform crate = GameObject.Find("Obstacle | Supply Crate").transform;
        report.AppendLine("QAXR validation " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  Unity " + Application.unityVersion);
        report.AppendLine("Note: profiles and behaviour rules are heuristics, not empirically validated human behaviour.\n");

        var profiles = new[] { QAXRProfileType.Focused, QAXRProfileType.Exploratory, QAXRProfileType.Cautious };
        var all = new List<Rec>();
        var byProfile = new Dictionary<QAXRProfileType, List<Rec>>();
        foreach (var p in profiles)
        {
            byProfile[p] = new List<Rec>();
            for (int seed = 1; seed <= 8; seed++) { var r = Run(s, crate, p, seed, false); byProfile[p].Add(r); all.Add(r); }
        }

        // ---------------- Test 1 Task completion ----------------
        int done = 0; var failures = new List<string>();
        foreach (var r in all) { if (r.completed && r.button) done++; else failures.Add(r.profile + "/" + r.seed + ": " + r.outcome); }
        Result("Test 1 - Task completion (" + all.Count + " runs, 3 profiles x 8 seeds)", done == all.Count,
               done + "/" + all.Count + " completed incl. button press" + (failures.Count > 0 ? "\n" + string.Join("\n", failures) : ""));

        // ---------------- Test 2 Context-aware behaviour ----------------
        {
            bool ok = true; var sb = new StringBuilder();
            foreach (var p in profiles)
            {
                float cs = 0, aps = 0, attn = 0; int rnd = 0, n = 0;
                foreach (var r in byProfile[p])
                {
                    cs += r.corridorSpeedSum / Mathf.Max(1, r.corridorSteps);
                    aps += r.approachSpeedSum / Mathf.Max(1, r.approachSteps);
                    attn += (float)r.approachAttn / Mathf.Max(1, r.approachSteps);
                    rnd += r.randomInApproach; n++;
                }
                cs /= n; aps /= n; attn /= n;
                bool pOk = aps < 0.75f * cs && attn > 0.8f && rnd == 0;
                ok &= pOk;
                sb.AppendLine(p + ": empty-corridor walking speed " + cs.ToString("F2") + " m/s vs final 2 m of panel approach " + aps.ToString("F2") +
                              " m/s; head on panel during approach " + (attn * 100f).ToString("F0") + "%; random look/turn/inspect during approach: " + rnd);
            }
            Result("Test 2 - Context-aware behaviour (empty corridor vs. approaching panel)", ok, sb.ToString().TrimEnd());
        }

        // ---------------- Test 3 Smooth motion ----------------
        {
            bool ok = true; float a = 0, b = 0, h = 0, j = 0, cy = 0;
            foreach (var r in all)
            {
                ok &= r.maxLongAccel <= r.accelLimit + 0.01f && r.maxBodyRate <= r.turnLimit + 0.1f && r.maxHeadRate <= r.headLimit + 0.1f &&
                      r.maxPosJump <= r.speedLimit * s.simulationStep * 1.05f + 1e-4f && r.maxCamYawStep <= (r.turnLimit + r.headLimit) * s.simulationStep + 0.05f;
                a = Mathf.Max(a, r.maxLongAccel / r.accelLimit); b = Mathf.Max(b, r.maxBodyRate / r.turnLimit);
                h = Mathf.Max(h, r.maxHeadRate / r.headLimit); j = Mathf.Max(j, r.maxPosJump / (r.speedLimit * s.simulationStep));
                cy = Mathf.Max(cy, r.maxCamYawStep);
            }
            Result("Test 3 - Smooth motion (rate limits, no jumps)", ok,
                "max use of limits: accel " + (a * 100).ToString("F0") + "%, body turn " + (b * 100).ToString("F0") + "%, head " + (h * 100).ToString("F0") +
                "%, step distance " + (j * 100).ToString("F0") + "% of max speed*dt; largest camera yaw change per 0.02 s step " + cy.ToString("F2") + " deg");
        }

        // ---------------- Test 4 Behaviour profiles ----------------
        {
            var st = new Dictionary<QAXRProfileType, float[]>(); // duration, moving speed, head travel, exploration actions, stops
            var sb = new StringBuilder();
            foreach (var p in profiles)
            {
                float d = 0, sp = 0, ht = 0, ex = 0, stp = 0; int n = byProfile[p].Count;
                foreach (var r in byProfile[p])
                {
                    d += r.duration; sp += r.sumMovingSpeed / Mathf.Max(1, r.movingSteps); ht += r.headTravel;
                    ex += r.counts[(int)QAXRAgentAction.LookAround] + r.counts[(int)QAXRAgentAction.InspectObject] + r.counts[(int)QAXRAgentAction.TurnBody] +
                          Mathf.Max(0, r.counts[(int)QAXRAgentAction.LookAtTarget] - 2);
                    stp += r.counts[(int)QAXRAgentAction.Stop] + r.counts[(int)QAXRAgentAction.InspectObject];
                }
                st[p] = new[] { d / n, sp / n, ht / n, ex / n, stp / n };
                sb.AppendLine(p + ": duration " + (d / n).ToString("F1") + " s, moving speed " + (sp / n).ToString("F2") + " m/s, head travel " +
                              (ht / n).ToString("F0") + " deg, exploratory actions " + (ex / n).ToString("F1") + ", stops/inspections " + (stp / n).ToString("F1"));
            }
            var F = st[QAXRProfileType.Focused]; var E = st[QAXRProfileType.Exploratory]; var C = st[QAXRProfileType.Cautious];
            bool ok = F[0] < E[0] && F[0] < C[0]          // focused fastest to complete
                   && C[1] < F[1] && C[1] < E[1]          // cautious slowest walking
                   && E[2] > F[2] && E[3] > F[3] && E[3] > C[3]; // exploratory most head / exploration activity
            Result("Test 4 - Behaviour profiles distinguishable (8 seeds each)", ok, sb.ToString().TrimEnd());
        }

        // ---------------- Test 5 Deterministic replay ----------------
        {
            bool ok = true; var sb = new StringBuilder();
            foreach (var p in profiles)
            {
                var r1 = Run(s, crate, p, 77, false); var r2 = Run(s, crate, p, 77, false);
                bool same = r1.decisionHash == r2.decisionHash && r1.trajHash == r2.trajHash;
                ok &= same;
                sb.AppendLine(p + " seed 77: decisions " + r1.decisionHash.ToString("X16") + " vs " + r2.decisionHash.ToString("X16") +
                              ", trajectory hash " + (r1.trajHash == r2.trajHash ? "equal" : "DIFFERENT"));
            }
            Result("Test 5 - Deterministic replay (same seed and config twice)", ok, sb.ToString().TrimEnd());
        }

        // ---------------- Test 6 + 7 Baseline / Fault, measurement integrity ----------------
        {
            bool ok6 = true, ok7 = true; var sb6 = new StringBuilder(); var sb7 = new StringBuilder();
            foreach (var p in profiles)
            {
                var b = Run(s, crate, p, 42, false); var f = Run(s, crate, p, 42, true);
                bool same = b.decisionHash == f.decisionHash && b.trajHash == f.trajHash;
                bool faultSeen = f.maxRollRate > 60f && f.episodes >= 1 && b.maxRollRate < 0.5f && b.episodes == 0;
                ok6 &= same && faultSeen;
                sb6.AppendLine(p + ": decisions " + (b.decisionHash == f.decisionHash ? "identical" : "DIFFERENT") + ", body/head trajectory " +
                               (b.trajHash == f.trajHash ? "identical" : "DIFFERENT") + "; max roll rate baseline " + b.maxRollRate.ToString("F2") +
                               " vs fault " + f.maxRollRate.ToString("F0") + " deg/s; risk episodes " + b.episodes + " vs " + f.episodes);
                bool m = b.sourceErrors == 0 && f.sourceErrors == 0 && b.rollErrors == 0 && f.rollErrors == 0 && b.yawRateErrors == 0 && f.yawRateErrors == 0;
                ok7 &= m;
                sb7.AppendLine(p + ": D2 label errors " + (b.sourceErrors + f.sourceErrors) + ", roll vs app-roll mismatches " + (b.rollErrors + f.rollErrors) +
                               ", camera yaw rate != body+head rate " + (b.yawRateErrors + f.yawRateErrors));
            }
            Result("Test 6 - Baseline/Fault: camera-only fault does not change agent behaviour", ok6, sb6.ToString().TrimEnd());
            Result("Test 7 - Measurement integrity (D1 values, D2 motion-source labels)", ok7, sb7.ToString().TrimEnd());
        }

        // ---------------- Test 8 Data export ----------------
        {
            var a = s.primaryAgent;
            var m = a.GetComponent<QAXRMetricsCollector>();
            m.outputSubfolder = "validation";
            s.writeFiles = true;
            Run(s, crate, QAXRProfileType.Focused, 42, true);
            s.writeFiles = false;
            m.outputSubfolder = "";
            string prefix = s.runs[0].metrics.LastSavedPrefix;
            bool files = prefix != "" && File.Exists(prefix + "_samples.csv") && File.Exists(prefix + "_events.csv") &&
                         File.Exists(prefix + "_summary.csv") && File.Exists(prefix + "_config.json");
            bool ok = files;
            string detail = "prefix " + prefix;
            if (files)
            {
                string header = File.ReadAllLines(prefix + "_samples.csv")[0].TrimStart('﻿');
                string ev = File.ReadAllText(prefix + "_events.csv");
                string cfg = File.ReadAllText(prefix + "_config.json");
                bool h = header == QAXRMetricsCollector.SampleHeader && header.StartsWith(QAXRMetricsCollector.SampleHeaderV1);
                bool e = ev.Contains(",RunStart,") && ev.Contains(",AgentConfig,") && ev.Contains(",Decision,") && ev.Contains(",SubgoalStart,") &&
                         ev.Contains(",FaultStart,") && ev.Contains(",ButtonPressed,") && ev.Contains(",MissionComplete,") && ev.Contains("draw=");
                bool c = cfg.Contains("\"seed\": 42") && cfg.Contains("\"profileName\": \"Focused\"") && cfg.Contains("\"simulation_step_s\"") &&
                         cfg.Contains("\"mission\"") && cfg.Contains("\"decision_hash\"");
                ok = h && e && c;
                detail += "\nsample header v1-compatible + v2 fields: " + h + "; events (decisions with random draws, subgoals, fault, button): " + e +
                          "; config (seed, profile params, mission, timing, decision hash): " + c;
            }
            Result("Test 8 - Data export (timestamps, identifiers, events, configuration)", ok, detail);
        }

        // ---------------- Test 9 Robustness ----------------
        {
            var a = s.primaryAgent;
            var original = a.mission;
            var sb = new StringBuilder();

            var unreachable = new List<QAXRSubgoal>(original);
            unreachable.Insert(1, new QAXRSubgoal { name = "Unreachable test point", type = QAXRSubgoalType.NavigateToTarget, position = new Vector3(6f, 0f, 4f), timeout = 60f });
            a.mission = unreachable;
            var u = Run(s, crate, QAXRProfileType.Focused, 42, false);
            bool uOk = u.failed && !u.completed && u.duration < s.maxRunSeconds && u.outcome.Contains("failed");
            sb.AppendLine("unreachable waypoint -> " + u.outcome + " after " + u.duration.ToString("F1") + " s (max x " + u.maxAbsX.ToString("F2") + " m)");

            var invalid = new List<QAXRSubgoal>();
            foreach (var g in original)
            {
                var c = new QAXRSubgoal { name = g.name, type = g.type, position = g.position, target = g.target, completionRadius = g.completionRadius, completeWhenPassedZ = g.completeWhenPassedZ, timeout = g.timeout };
                if (g.type == QAXRSubgoalType.ApproachTarget) c.target = null;
                invalid.Add(c);
            }
            a.mission = invalid;
            var inv = Run(s, crate, QAXRProfileType.Focused, 42, false);
            bool iOk = inv.failed && inv.outcome.Contains("no target");
            sb.AppendLine("missing interaction target -> " + inv.outcome + " after " + inv.duration.ToString("F1") + " s");
            a.mission = original;

            float maxX = 0f, minCrate = float.MaxValue; int corr = 0;
            foreach (var r in all) { maxX = Mathf.Max(maxX, r.maxAbsX); minCrate = Mathf.Min(minCrate, r.minCrateDist); corr += r.corrections; }
            float lim = a.corridorHalfWidth - a.motion.bodyRadius;
            bool bOk = maxX <= lim + 1e-3f && minCrate >= 0.65f + a.motion.bodyRadius - 0.01f;
            sb.AppendLine("bounds over " + all.Count + " runs: max |x| " + maxX.ToString("F2") + " m (limit " + lim.ToString("F2") + "), min distance to crate centre " +
                          minCrate.ToString("F2") + " m (footprint " + (0.65f + a.motion.bodyRadius).ToString("F2") + "), collision-constraint corrections " + corr +
                          " (0 = obstacle avoided by steering alone)");
            Result("Test 9 - Robustness (unreachable goal, invalid target, navigation bounds)", uOk && iOk && bOk, sb.ToString().TrimEnd());
        }

        // ---------------- Multi-agent ----------------
        {
            var sb = new StringBuilder(); bool ok = true;
            foreach (int n in new[] { 3, 5 })
            {
                var r = Run(s, crate, QAXRProfileType.Focused, 42, true, n);
                int c = 0; foreach (bool b in r.agentsCompleted) if (b) c++;
                var ids = new HashSet<string>(); foreach (var ar in s.runs) ids.Add(ar.agent.agentId + "|" + ar.runId);
                bool nOk = c == n && ids.Count == n && r.minAgentSep >= 0.5f;
                ok &= nOk;
                var lines = new List<string>();
                foreach (var ar in s.runs) lines.Add(ar.agent.agentId + " " + ar.agent.Profile.profileName + " seed " + ar.agent.randomSeed + ": " + ar.outcome +
                                                    " (" + (ar.endTime - ar.agent.startDelay).ToString("F1") + " s, risk episodes " + ar.detector.Episodes.Count + ")");
                sb.AppendLine(n + " agents: " + c + "/" + n + " completed, min separation " + r.minAgentSep.ToString("F2") + " m\n  " + string.Join("\n  ", lines));
            }
            s.agentCount = 1;
            s.DestroySpawnedAgents();
            Result("Multi-agent - 3 and 5 independent agents (fault run)", ok, sb.ToString().TrimEnd());
        }

        report.AppendLine("\nSummary: " + pass + " passed, " + fail + " failed.");
        string text = report.ToString();
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "QAXR_Runs", "validation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "validation_report.txt"), text);
            text += "Report: " + Path.Combine(dir, "validation_report.txt") + "\n";
        }
        catch (Exception e) { text += "Could not write report: " + e.Message + "\n"; }

        if (restoreScene && File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return text;
    }

    private static Rec Run(QAXRScenarioController s, Transform crate, QAXRProfileType profile, int seed, bool fault, int agents = 1)
    {
        var rec = new Rec { profile = profile, seed = seed, fault = fault };
        s.profile = profile;
        s.agentCount = agents;
        bool first = true;
        Vector3 lastPos = Vector3.zero; float lastCamYaw = 0f;
        float dt = s.simulationStep;
        var positions = new Dictionary<int, Vector3>();

        Action<int, QAXRMotionSample, QAXRAgentController> h = (i, smp, a) =>
        {
            if (a.Active && a.Started) positions[i] = a.transform.position; else positions.Remove(i);
            if (i == s.runs.Count - 1 && positions.Count > 1)
            {
                var keys = new List<int>(positions.Keys);
                for (int x = 0; x < keys.Count; x++)
                    for (int y = x + 1; y < keys.Count; y++)
                        rec.minAgentSep = Mathf.Min(rec.minAgentSep, Vector3.Distance(positions[keys[x]], positions[keys[y]]));
            }
            if (i != 0) return;

            var p = a.Profile;
            rec.accelLimit = Mathf.Max(p.maxAcceleration, p.maxDeceleration);
            rec.turnLimit = p.turningSpeed; rec.headLimit = p.headMaxSpeed; rec.speedLimit = p.preferredSpeed * (1f + p.speedVariation);
            Vector3 pos = a.transform.position;
            rec.trajHash = Hash(rec.trajHash, pos.x.ToString("F5") + pos.z.ToString("F5") + a.motion.bodyYaw.ToString("F4") +
                                              a.motion.headYaw.ToString("F4") + a.motion.headPitch.ToString("F4") + (int)a.Action);
            rec.steps++;
            rec.sumSpeed += a.CurrentSpeed;
            if (a.CurrentSpeed > 0.2f) { rec.sumMovingSpeed += a.CurrentSpeed; rec.movingSteps++; }
            rec.headTravel += (Mathf.Abs(a.HeadYawRate) + Mathf.Abs(a.HeadPitchRate)) * dt;
            rec.maxRollRate = Mathf.Max(rec.maxRollRate, Mathf.Abs(smp.rollRate));
            rec.maxAbsX = Mathf.Max(rec.maxAbsX, Mathf.Abs(pos.x));
            Vector3 dc = pos - crate.position; dc.y = 0f;
            rec.minCrateDist = Mathf.Min(rec.minCrateDist, dc.magnitude);

            var type = a.Planner.CurrentType;
            // "Empty corridor": walking along a navigate subgoal with no obstacle within 2.5 m.
            if (type == QAXRSubgoalType.NavigateToTarget && a.Action == QAXRAgentAction.Walk && a.NearestObstacle > 2.5f)
            { rec.corridorSpeedSum += a.CurrentSpeed; rec.corridorSteps++; }
            Vector3 toAp = a.NavGoal - pos; toAp.y = 0f;
            if (type == QAXRSubgoalType.ApproachTarget && toAp.magnitude < 2.0f) // final approach to the panel
            {
                rec.approachSpeedSum += a.CurrentSpeed; rec.approachSteps++;
                if (a.AttentionName == "Control panel") rec.approachAttn++;
                if (a.Action == QAXRAgentAction.LookAround || a.Action == QAXRAgentAction.TurnBody || a.Action == QAXRAgentAction.InspectObject) rec.randomInApproach++;
            }

            rec.maxLongAccel = Mathf.Max(rec.maxLongAccel, Mathf.Abs(a.motion.longitudinalAccel));
            rec.maxBodyRate = Mathf.Max(rec.maxBodyRate, Mathf.Abs(a.BodyYawRate));
            rec.maxHeadRate = Mathf.Max(rec.maxHeadRate, Mathf.Max(Mathf.Abs(a.HeadYawRate), Mathf.Abs(a.HeadPitchRate)));
            if (!first)
            {
                rec.maxPosJump = Mathf.Max(rec.maxPosJump, Vector3.Distance(pos, lastPos));
                rec.maxCamYawStep = Mathf.Max(rec.maxCamYawStep, Mathf.Abs(Mathf.DeltaAngle(lastCamYaw, smp.yaw)));
                if (Mathf.Abs(smp.yawRate - (smp.bodyYawRate + smp.headYawRate)) > 0.5f) rec.yawRateErrors++;
            }
            if (smp.faultActive)
            {
                if (smp.source != QAXRMotionSource.ApplicationControlled) rec.sourceErrors++;
                if (Mathf.Abs(smp.roll - smp.appRoll) > 0.05f) rec.rollErrors++;
            }
            else
            {
                if (smp.source == QAXRMotionSource.ApplicationControlled) rec.sourceErrors++;
                if (Mathf.Abs(smp.roll) > 0.01f) rec.rollErrors++;
            }
            lastPos = pos; lastCamYaw = smp.yaw; first = false;
        };

        s.OnSample += h;
        try
        {
            s.StartRun(fault, seed);
            s.RunToEnd(100000);
        }
        finally { s.OnSample -= h; }

        var r0 = s.runs[0];
        var ag = r0.agent;
        rec.outcome = r0.outcome;
        rec.completed = ag.MissionComplete;
        rec.failed = ag.MissionFailed;
        rec.button = ag.ButtonPressed;
        rec.duration = r0.endTime - ag.startDelay;
        rec.decisionHash = ag.DecisionHash;
        rec.counts = (int[])ag.actionCounts.Clone();
        rec.episodes = r0.detector.Episodes.Count;
        rec.corrections = ag.motion.constraintCorrections;
        foreach (var ar in s.runs) rec.agentsCompleted.Add(ar.agent.MissionComplete && ar.agent.ButtonPressed);
        return rec;
    }

    private static ulong Hash(ulong h, string s)
    {
        unchecked { for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; } }
        return h;
    }
}
#endif
