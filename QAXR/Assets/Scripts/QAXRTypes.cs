using System;
using UnityEngine;

/// <summary>Shared types for the QAXR simulated-agent test environment.</summary>

/// <summary>Layer B behavioural actions (logged in the CSV column agent_state).</summary>
public enum QAXRAgentAction
{
    Walk, SlowDown, Stop, LookAtTarget, LookAround, TurnBody, InspectObject, Interact, ResumeNavigation,
    Finished, Failed
}

/// <summary>Layer A subgoal types.</summary>
public enum QAXRSubgoalType { NavigateToTarget, ObserveTarget, ApproachTarget, InteractWithTarget, NavigateToExit, Completed }

public enum QAXRZone { None, A_Normal, B_Fault, C_Interaction }

/// <summary>D2: where the current viewpoint motion comes from.</summary>
public enum QAXRMotionSource { Stationary, AgentWalk, AgentTurn, SimulatedHead, ApplicationControlled }

/// <summary>What the perception system can recognise.</summary>
public enum QAXRPerceivableKind { Waypoint, Obstacle, ControlPanel, Landmark, Exit, Agent }

/// <summary>Illustrative behaviour configurations (not validated human categories).</summary>
public enum QAXRProfileType { Focused, Exploratory, Cautious, Custom }

/// <summary>One step of a mission (task definition). Editable in the Inspector for future scenes.</summary>
[Serializable]
public class QAXRSubgoal
{
    public string name = "Subgoal";
    public QAXRSubgoalType type = QAXRSubgoalType.NavigateToTarget;
    [Tooltip("Navigation point on the floor (y ignored). For Approach/Interact the target's approach point is used.")]
    public Vector3 position;
    [Tooltip("Object to observe / approach / interact with. Required for Observe, Approach and Interact.")]
    public QAXRPerceivable target;
    public float completionRadius = 0.5f;
    [Tooltip("Corridor scenes: a navigate subgoal also completes once the agent has passed its z position.")]
    public bool completeWhenPassedZ = false;
    [Tooltip("Seconds before this subgoal is declared failed (logged).")]
    public float timeout = 40f;
}

/// <summary>One measurement of the viewpoint at a simulation step.</summary>
public struct QAXRMotionSample
{
    public float time;
    public QAXRZone zone;
    public QAXRAgentAction action;
    public string taskStep;      // mission goal text
    public string subgoal;       // current subgoal name/type
    public string attentionTarget;
    public string profile;
    public int decisionId;

    // D1 Viewpoint motion
    public Vector3 position;
    public Vector3 velocity;
    public float speed;
    public Vector3 acceleration;
    public float yaw, pitch, roll;
    public float yawRate, pitchRate, rollRate;
    public float angularSpeed;

    // D2 Locomotion & control (per-source contributions)
    public float bodyYawRate;
    public float headYawRate;
    public float headPitchRate;
    public float headYawRel, headPitchRel;
    public float appRoll;
    public float appRollRate;
    public QAXRMotionSource source;

    // D3 Visual context
    public float fov;
    public float vignette;
    public float nearestObstacle;

    // D4 Runtime performance (Editor only, not Quest)
    public float frameTimeMs;
    public float fps;
    public float agentSimMs;     // CPU time of this agent's simulation step (test-harness overhead)

    // D5 / reporting
    public bool faultActive;
    public bool riskActive;
}
