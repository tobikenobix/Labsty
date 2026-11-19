using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records viewpoint motion (D1), motion source (D2), visual context (D3),
/// frame timing (D4) and exposure-related flags (D5) over time. One collector per agent.
///
/// Outputs per run and agent (in Application.persistentDataPath/QAXR_Runs[/subfolder]):
///   *_samples.csv  continuous samples every sampleInterval seconds (simulation runs every step)
///   *_events.csv   discrete events (decisions, actions, subgoals, zones, fault, risk, button, mission)
///   *_summary.csv  one row of run-level results
///   *_config.json  everything needed to reproduce the run (seed, profile parameters, mission, timing)
///
/// Schema rule: the first 34 sample columns keep the v1 order and meaning. New fields are appended.
/// </summary>
public class QAXRMetricsCollector : MonoBehaviour
{
    [Tooltip("Seconds between recorded samples (measurement itself runs every simulation step).")]
    public float sampleInterval = 0.1f;
    [Tooltip("Rates above this count as 'moving' for the D2 motion-source label.")]
    public float sourceRateThreshold = 5f;
    [Tooltip("No vignette in this scene; kept as a D3 column for later.")]
    public float vignetteStrength = 0f;
    [Tooltip("Optional subfolder of QAXR_Runs (used by the validation tests).")]
    public string outputSubfolder = "";

    public QAXRMotionSample Current { get; private set; }
    public float MaxRollRate { get; private set; }
    public float MaxAngularSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public float SumSpeed { get; private set; }
    public int StepCount { get; private set; }
    public float SumHeadAngularTravel { get; private set; }
    public float SumAgentSimMs { get; private set; }
    public string OutputFolder => string.IsNullOrEmpty(outputSubfolder)
        ? Path.Combine(Application.persistentDataPath, "QAXR_Runs")
        : Path.Combine(Application.persistentDataPath, "QAXR_Runs", outputSubfolder);
    public string LastSavedPrefix { get; private set; } = "";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private StringBuilder samples;
    private StringBuilder events;
    private string runId, agentId, scenario, profile;
    private int seed;
    private float nextSampleTime;

    private bool hasLast;
    private Vector3 lastPos, lastVel;
    private Quaternion lastRot;
    private float lastYaw, lastPitch, lastRoll;
    private readonly float[] zoneSeconds = new float[4];

    public const string SampleHeaderV1 =
        "time_s,run_id,agent_id,seed,scenario,zone,agent_state,task_step," +
        "pos_x_m,pos_y_m,pos_z_m,speed_mps,accel_x_mps2,accel_y_mps2,accel_z_mps2,accel_mag_mps2," +
        "yaw_deg,pitch_deg,roll_deg,yaw_rate_degps,pitch_rate_degps,roll_rate_degps,angular_speed_degps," +
        "body_yaw_rate_degps,head_yaw_rate_degps,head_pitch_rate_degps,app_roll_deg,app_roll_rate_degps,motion_source," +
        "fov_deg,vignette,frame_time_ms,fault_active,risk_flag";

    public const string SampleHeaderV2Extra =
        ",profile,subgoal,attention_target,head_yaw_rel_deg,head_pitch_rel_deg,nearest_obstacle_m,decision_id,agent_sim_ms,fps";

    public const string SampleHeader = SampleHeaderV1 + SampleHeaderV2Extra;

    public void BeginRun(string newRunId, string newAgentId, int newSeed, string newScenario, string profileName)
    {
        runId = newRunId; agentId = newAgentId; seed = newSeed; scenario = newScenario; profile = profileName;
        samples = new StringBuilder(128000);
        events = new StringBuilder(16000);
        samples.AppendLine(SampleHeader);
        events.AppendLine("time_s,run_id,agent_id,event_type,zone,pos_x_m,pos_z_m,detail");
        nextSampleTime = 0f;
        hasLast = false;
        MaxRollRate = MaxAngularSpeed = MaxSpeed = SumSpeed = SumHeadAngularTravel = SumAgentSimMs = 0f;
        StepCount = 0;
        Array.Clear(zoneSeconds, 0, zoneSeconds.Length);
        LastSavedPrefix = "";
        Current = default;
    }

    /// <summary>Measure the viewpoint for this simulation step.</summary>
    public QAXRMotionSample Measure(float time, float dt, Camera viewpoint, QAXRAgentController agent,
                                    QAXRZone zone, bool faultActive, float appRoll, float appRollRate, float agentSimMs)
    {
        Transform cam = viewpoint.transform;
        Vector3 pos = cam.position;
        Quaternion rot = cam.rotation;
        Vector3 e = rot.eulerAngles;
        float yaw = e.y, pitch = Mathf.DeltaAngle(0f, e.x), roll = Mathf.DeltaAngle(0f, e.z);

        QAXRMotionSample s = new QAXRMotionSample
        {
            time = time,
            zone = zone,
            action = agent.Action,
            taskStep = agent.CurrentTask,
            subgoal = agent.SubgoalLabel,
            attentionTarget = agent.AttentionName,
            profile = agent.Profile != null ? agent.Profile.profileName : "",
            decisionId = agent.DecisionCount,
            position = pos,
            yaw = yaw, pitch = pitch, roll = roll,
            bodyYawRate = agent.BodyYawRate,
            headYawRate = agent.HeadYawRate,
            headPitchRate = agent.HeadPitchRate,
            headYawRel = agent.HeadYawRel,
            headPitchRel = agent.HeadPitchRel,
            appRoll = appRoll,
            appRollRate = appRollRate,
            fov = viewpoint.fieldOfView,
            vignette = vignetteStrength,
            nearestObstacle = agent.NearestObstacle,
            frameTimeMs = Time.unscaledDeltaTime * 1000f,
            fps = Time.unscaledDeltaTime > 1e-5f ? 1f / Time.unscaledDeltaTime : 0f,
            agentSimMs = agentSimMs,
            faultActive = faultActive
        };

        if (hasLast)
        {
            s.velocity = (pos - lastPos) / dt;
            s.speed = s.velocity.magnitude;
            s.acceleration = (s.velocity - lastVel) / dt;
            s.yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            s.pitchRate = Mathf.DeltaAngle(lastPitch, pitch) / dt;
            s.rollRate = Mathf.DeltaAngle(lastRoll, roll) / dt;
            s.angularSpeed = Quaternion.Angle(lastRot, rot) / dt;
        }

        s.source = ClassifySource(s);

        lastPos = pos; lastVel = s.velocity; lastRot = rot;
        lastYaw = yaw; lastPitch = pitch; lastRoll = roll;
        hasLast = true;

        MaxRollRate = Mathf.Max(MaxRollRate, Mathf.Abs(s.rollRate));
        MaxAngularSpeed = Mathf.Max(MaxAngularSpeed, s.angularSpeed);
        MaxSpeed = Mathf.Max(MaxSpeed, s.speed);
        SumSpeed += s.speed;
        SumHeadAngularTravel += (Mathf.Abs(s.headYawRate) + Mathf.Abs(s.headPitchRate)) * dt;
        SumAgentSimMs += agentSimMs;
        StepCount++;
        zoneSeconds[(int)zone] += dt;

        Current = s;
        return s;
    }

    /// <summary>Store the sample (after the risk detector filled riskActive).</summary>
    public void Record(QAXRMotionSample s)
    {
        Current = s;
        if (samples == null || s.time + 1e-4f < nextSampleTime) return;
        nextSampleTime += sampleInterval;
        if (nextSampleTime < s.time) nextSampleTime = s.time + sampleInterval;

        StringBuilder b = samples;
        b.Append(F(s.time, 2)).Append(',').Append(runId).Append(',').Append(agentId).Append(',')
         .Append(seed).Append(',').Append(scenario).Append(',').Append(s.zone).Append(',')
         .Append(s.action).Append(',').Append(Csv(s.taskStep)).Append(',')
         .Append(F(s.position.x, 3)).Append(',').Append(F(s.position.y, 3)).Append(',').Append(F(s.position.z, 3)).Append(',')
         .Append(F(s.speed, 3)).Append(',')
         .Append(F(s.acceleration.x, 3)).Append(',').Append(F(s.acceleration.y, 3)).Append(',').Append(F(s.acceleration.z, 3)).Append(',')
         .Append(F(s.acceleration.magnitude, 3)).Append(',')
         .Append(F(s.yaw, 2)).Append(',').Append(F(s.pitch, 2)).Append(',').Append(F(s.roll, 2)).Append(',')
         .Append(F(s.yawRate, 2)).Append(',').Append(F(s.pitchRate, 2)).Append(',').Append(F(s.rollRate, 2)).Append(',')
         .Append(F(s.angularSpeed, 2)).Append(',')
         .Append(F(s.bodyYawRate, 2)).Append(',').Append(F(s.headYawRate, 2)).Append(',').Append(F(s.headPitchRate, 2)).Append(',')
         .Append(F(s.appRoll, 2)).Append(',').Append(F(s.appRollRate, 2)).Append(',').Append(s.source).Append(',')
         .Append(F(s.fov, 1)).Append(',').Append(F(s.vignette, 2)).Append(',').Append(F(s.frameTimeMs, 2)).Append(',')
         .Append(s.faultActive ? 1 : 0).Append(',').Append(s.riskActive ? 1 : 0)
         // ---- v2 appended fields ----
         .Append(',').Append(s.profile).Append(',').Append(Csv(s.subgoal)).Append(',').Append(Csv(s.attentionTarget)).Append(',')
         .Append(F(s.headYawRel, 2)).Append(',').Append(F(s.headPitchRel, 2)).Append(',')
         .Append(float.IsInfinity(s.nearestObstacle) ? "" : F(s.nearestObstacle, 2)).Append(',')
         .Append(s.decisionId).Append(',').Append(F(s.agentSimMs, 4)).Append(',').Append(F(s.fps, 1))
         .AppendLine();
    }

    public void LogEvent(float time, string type, QAXRZone zone, Vector3 pos, string detail)
    {
        if (events == null) return;
        events.Append(F(time, 2)).Append(',').Append(runId).Append(',').Append(agentId).Append(',')
              .Append(type).Append(',').Append(zone).Append(',')
              .Append(F(pos.x, 2)).Append(',').Append(F(pos.z, 2)).Append(',').Append(Csv(detail)).AppendLine();
    }

    public float ZoneSeconds(QAXRZone z) => zoneSeconds[(int)z];

    /// <summary>Write samples, events, summary and config. Returns the file prefix.</summary>
    public string Save(string summaryHeader, string summaryRow, string configJson)
    {
        if (samples == null) return "";
        try
        {
            Directory.CreateDirectory(OutputFolder);
            string prefix = Path.Combine(OutputFolder, runId);
            File.WriteAllText(prefix + "_samples.csv", samples.ToString(), Encoding.UTF8);
            File.WriteAllText(prefix + "_events.csv", events.ToString(), Encoding.UTF8);
            File.WriteAllText(prefix + "_summary.csv", summaryHeader + "\n" + summaryRow + "\n", Encoding.UTF8);
            if (!string.IsNullOrEmpty(configJson)) File.WriteAllText(prefix + "_config.json", configJson, Encoding.UTF8);

            // Running table of all runs. A new file is used if the header (schema) changed.
            string allRuns = Path.Combine(OutputFolder, "QAXR_all_runs_summary.csv");
            if (File.Exists(allRuns))
            {
                string first = "";
                using (var r = new StreamReader(allRuns)) first = r.ReadLine() ?? "";
                if (first.TrimStart('﻿') != summaryHeader) allRuns = Path.Combine(OutputFolder, "QAXR_all_runs_summary_v2.csv");
            }
            if (!File.Exists(allRuns)) File.WriteAllText(allRuns, summaryHeader + "\n", Encoding.UTF8);
            File.AppendAllText(allRuns, summaryRow + "\n", Encoding.UTF8);

            LastSavedPrefix = prefix;
            return prefix;
        }
        catch (Exception ex)
        {
            Debug.LogError("QAXR: could not save run data: " + ex.Message);
            return "";
        }
    }

    private QAXRMotionSource ClassifySource(QAXRMotionSample s)
    {
        if (s.faultActive) return QAXRMotionSource.ApplicationControlled;
        if (Mathf.Abs(s.headYawRate) > sourceRateThreshold || Mathf.Abs(s.headPitchRate) > sourceRateThreshold)
            return QAXRMotionSource.SimulatedHead;
        if (Mathf.Abs(s.bodyYawRate) > sourceRateThreshold) return QAXRMotionSource.AgentTurn;
        if (s.speed > 0.05f) return QAXRMotionSource.AgentWalk;
        return QAXRMotionSource.Stationary;
    }

    public static string F(float v, int decimals) => v.ToString("F" + decimals, Inv);

    private static string Csv(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
