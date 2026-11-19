using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Demo risk rule (D1 + D2 + D5):
///   motion source == ApplicationControlled  AND  |camera roll rate| >= threshold
/// Consecutive flagged steps are merged into episodes (gap tolerance), so we can report
/// how often and how long the condition occurs and WHERE it happened.
///
/// The 60 deg/s threshold is an ARBITRARY demo value, not a validated sickness threshold.
/// Detecting the injected fault shows the measurement pipeline works, not that users get sick.
/// </summary>
public class QAXRRiskDetector : MonoBehaviour
{
    [Serializable]
    public class Episode
    {
        public float startTime, endTime, peakRollRate;
        public QAXRZone zone;
        public Vector3 position;
        public float Duration => endTime - startTime;
    }

    public float rollRateThreshold = 60f;
    [Tooltip("A flagged episode ends after this long below the threshold (merges oscillation zero-crossings).")]
    public float episodeGapTolerance = 0.3f;
    public GameObject issueMarker;

    public bool RiskActive { get; private set; }
    public List<Episode> Episodes { get; } = new List<Episode>();
    public float TotalRiskSeconds { get; private set; }
    public string LastFinding { get; private set; } = "No issue detected yet";

    /// <summary>(eventType, detail)</summary>
    public event Action<string, string> OnEvent;

    private Episode open;
    private float lastOverTime;

    public void ResetDetector()
    {
        RiskActive = false;
        Episodes.Clear();
        TotalRiskSeconds = 0f;
        open = null;
        LastFinding = "No issue detected yet";
        if (issueMarker != null) issueMarker.SetActive(false);
    }

    public void Evaluate(ref QAXRMotionSample s)
    {
        bool over = s.source == QAXRMotionSource.ApplicationControlled &&
                    Mathf.Abs(s.rollRate) >= rollRateThreshold;

        if (over)
        {
            lastOverTime = s.time;
            if (open == null)
            {
                open = new Episode { startTime = s.time, zone = s.zone, position = s.position };
                Episodes.Add(open);
                RiskActive = true;
                LastFinding = "RISK: app-controlled roll " + Mathf.Abs(s.rollRate).ToString("F0") +
                              " deg/s in " + s.zone + " at z=" + s.position.z.ToString("F1") + " m";
                OnEvent?.Invoke("RiskFlagStart", "roll_rate=" + Mathf.Abs(s.rollRate).ToString("F1") +
                                                 " threshold=" + rollRateThreshold.ToString("F0"));
                if (issueMarker != null && Episodes.Count == 1)
                {
                    issueMarker.transform.position = new Vector3(s.position.x, 2.4f, s.position.z);
                    issueMarker.SetActive(true);
                }
                Debug.LogWarning("QAXR flag: " + LastFinding + " (demo rule, not a sickness threshold)");
            }
            open.peakRollRate = Mathf.Max(open.peakRollRate, Mathf.Abs(s.rollRate));
            open.endTime = s.time;
        }
        else if (open != null && s.time - lastOverTime > episodeGapTolerance)
        {
            CloseEpisode();
        }

        s.riskActive = open != null;
    }

    /// <summary>Call at the end of a run to close an open episode.</summary>
    public void Finish()
    {
        if (open != null) CloseEpisode();
        if (Episodes.Count == 0) LastFinding = "No flagged condition in this run";
        else LastFinding = "Found " + Episodes.Count + " risk episode(s), total " + TotalRiskSeconds.ToString("F2") +
                           " s, first in " + Episodes[0].zone + " at z=" + Episodes[0].position.z.ToString("F1") + " m";
    }

    public float PeakRollRate()
    {
        float p = 0f;
        foreach (Episode e in Episodes) p = Mathf.Max(p, e.peakRollRate);
        return p;
    }

    private void CloseEpisode()
    {
        TotalRiskSeconds += open.Duration;
        OnEvent?.Invoke("RiskFlagEnd", "duration=" + open.Duration.ToString("F2") + "s peak=" + open.peakRollRate.ToString("F1"));
        open = null;
        RiskActive = false;
    }
}
