using System;
using UnityEngine;

/// <summary>
/// Parameter set that shapes one simulated user's behaviour.
/// All values are ILLUSTRATIVE heuristics chosen to make profiles distinguishable; they are not
/// calibrated against empirical human data. They are plain fields so they can be calibrated later.
/// </summary>
[Serializable]
public class QAXRBehaviorProfile
{
    public string profileName = "Focused";

    [Header("Locomotion (Layer C)")]
    public float preferredSpeed = 1.4f;          // m/s
    [Tooltip("Speed varies by +/- this fraction, resampled at each decision.")]
    public float speedVariation = 0.05f;
    public float maxAcceleration = 1.2f;         // m/s^2
    public float maxDeceleration = 1.6f;         // m/s^2
    public float turningSpeed = 90f;             // deg/s, max body yaw rate
    public float bodyTurnSmoothTime = 0.25f;     // s
    [Tooltip("Start slowing this far (m) before interaction targets and the exit.")]
    public float slowdownDistance = 2.0f;
    [Tooltip("Heading error (deg) at which walking speed is halved (slowing for turns).")]
    public float turnSlowdownAngle = 60f;

    [Header("Head (Layer C)")]
    public float headMaxSpeed = 120f;            // deg/s
    public float headSmoothTime = 0.15f;         // s
    public Vector2 glanceHold = new Vector2(0.4f, 1.0f);
    public Vector2 glancePause = new Vector2(0.25f, 0.6f);
    public Vector2 lookAroundYaw = new Vector2(20f, 55f);
    public float lookAroundPitchMax = 10f;
    public Vector2Int glancesPerLookAround = new Vector2Int(1, 2);
    [Tooltip("Anticipatory head turn toward the steering direction while walking (fraction of bearing).")]
    public float headAnticipation = 0.5f;

    [Header("Base utilities (Layer B, before context multipliers)")]
    public float wWalk = 10f;
    public float wSlowDown = 0.8f;
    public float wStop = 0.4f;
    public float wLookAtTarget = 1.0f;
    public float wLookAround = 1.0f;
    public float wTurnBody = 0.2f;
    public float wInspect = 0.6f;

    [Header("Context sensitivity (Layer B)")]
    [Tooltip("Multiplier for inspecting landmarks not seen before.")]
    public float curiosity = 0.3f;
    [Tooltip("Tendency to slow / stop near unfamiliar objects.")]
    public float noveltyCaution = 0.2f;
    [Tooltip("Seconds after a non-walk action before another one becomes likely.")]
    public float minActionGap = 2.0f;
    [Tooltip("Utility multiplier when the same action was chosen last time.")]
    public float repetitionPenalty = 0.3f;
    public Vector2 decisionInterval = new Vector2(0.8f, 1.4f);

    [Header("Durations")]
    public Vector2 stopDuration = new Vector2(0.5f, 1.2f);
    public Vector2 slowDownDuration = new Vector2(1.0f, 2.0f);
    public Vector2 inspectDuration = new Vector2(1.0f, 2.0f);
    public Vector2 lookAtLandmarkDuration = new Vector2(0.8f, 1.5f);
    public Vector2 turnBodyDuration = new Vector2(1.5f, 2.5f);
    [Tooltip("Lateral detour (m) for TurnBody. 0 disables detours.")]
    public float maxDetourOffset = 0.0f;
    public float maxTotalStopSeconds = 6f;
    public Vector2 interactionHesitation = new Vector2(0.3f, 0.6f);
    public Vector2 resultObserveTime = new Vector2(0.8f, 1.2f);

    public static QAXRBehaviorProfile Create(QAXRProfileType type)
    {
        var p = new QAXRBehaviorProfile();
        switch (type)
        {
            case QAXRProfileType.Focused:
                p.profileName = "Focused";
                p.preferredSpeed = 1.45f; p.speedVariation = 0.04f;
                p.maxAcceleration = 1.4f; p.maxDeceleration = 1.8f; p.turningSpeed = 100f;
                p.slowdownDistance = 1.6f; p.turnSlowdownAngle = 70f;
                p.headMaxSpeed = 120f; p.glancesPerLookAround = new Vector2Int(1, 1);
                p.wLookAround = 0.5f; p.wInspect = 0.2f; p.wStop = 0.15f; p.wTurnBody = 0f; p.wSlowDown = 0.4f;
                p.curiosity = 0.1f; p.noveltyCaution = 0.05f; p.minActionGap = 3.0f;
                p.stopDuration = new Vector2(0.4f, 0.8f); p.inspectDuration = new Vector2(0.6f, 1.0f);
                p.maxDetourOffset = 0f; p.maxTotalStopSeconds = 3f;
                p.interactionHesitation = new Vector2(0.2f, 0.4f); p.resultObserveTime = new Vector2(0.5f, 0.8f);
                break;

            case QAXRProfileType.Exploratory:
                p.profileName = "Exploratory";
                p.preferredSpeed = 1.25f; p.speedVariation = 0.08f;
                p.maxAcceleration = 1.2f; p.maxDeceleration = 1.6f; p.turningSpeed = 90f;
                p.slowdownDistance = 2.0f; p.turnSlowdownAngle = 60f;
                p.headMaxSpeed = 140f; p.glancesPerLookAround = new Vector2Int(2, 3);
                p.lookAroundYaw = new Vector2(25f, 65f); p.lookAroundPitchMax = 15f;
                p.wWalk = 6f; p.wLookAround = 3.0f; p.wInspect = 3.0f; p.wLookAtTarget = 1.5f; p.wStop = 0.5f; p.wTurnBody = 1.0f; p.wSlowDown = 0.8f;
                p.curiosity = 1.0f; p.noveltyCaution = 0.2f; p.minActionGap = 1.0f;
                p.inspectDuration = new Vector2(1.8f, 3.5f); p.lookAtLandmarkDuration = new Vector2(1.0f, 2.0f);
                p.maxDetourOffset = 0.6f; p.maxTotalStopSeconds = 10f;
                p.interactionHesitation = new Vector2(0.3f, 0.6f); p.resultObserveTime = new Vector2(1.0f, 1.8f);
                break;

            case QAXRProfileType.Cautious:
                p.profileName = "Cautious";
                p.preferredSpeed = 0.95f; p.speedVariation = 0.05f;
                p.maxAcceleration = 0.8f; p.maxDeceleration = 1.2f; p.turningSpeed = 60f;
                p.slowdownDistance = 3.2f; p.turnSlowdownAngle = 35f;
                p.headMaxSpeed = 90f; p.glancesPerLookAround = new Vector2Int(1, 1);
                p.wLookAround = 0.5f; p.wInspect = 0.4f; p.wStop = 1.0f; p.wTurnBody = 0f; p.wSlowDown = 1.5f;
                p.curiosity = 0.3f; p.noveltyCaution = 1.0f; p.minActionGap = 1.5f;
                p.stopDuration = new Vector2(1.0f, 2.2f);
                p.maxDetourOffset = 0f; p.maxTotalStopSeconds = 10f;
                p.interactionHesitation = new Vector2(0.8f, 1.5f); p.resultObserveTime = new Vector2(1.2f, 2.0f);
                break;
        }
        return p;
    }

    public QAXRBehaviorProfile Clone() => (QAXRBehaviorProfile)MemberwiseClone();
}
