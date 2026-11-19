using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Layer A - Task planning.
/// A mission is an ordered list of subgoals. A subgoal ends when its COMPLETION CONDITION holds
/// (reached, observed, in range and stopped, interaction done), not after a fixed timer.
/// Each subgoal has a timeout so an unreachable goal ends the run with a logged failure.
/// </summary>
public class QAXRTaskPlanner
{
    public struct Context
    {
        public Vector3 position;        // agent floor position
        public float speed;
        public float facingError;       // deg between body forward and the subgoal target
        public bool targetVisible;      // perception says the target is visible
        public float attentionDwell;    // seconds the head has been on the target
        public bool interactionDone;    // behaviour layer finished the interaction
    }

    public float observeDwellSeconds = 0.4f;
    public float approachFacingTolerance = 20f;

    public string MissionName { get; private set; } = "";
    public int Index { get; private set; }
    public float SubgoalTime { get; private set; }
    public bool Completed { get; private set; }
    public bool Failed { get; private set; }
    public string FailureReason { get; private set; } = "";
    public int Count => subgoals != null ? subgoals.Count : 0;

    public event Action<string, string> OnEvent;

    private List<QAXRSubgoal> subgoals;

    public QAXRSubgoal Current => (!Completed && !Failed && subgoals != null && Index < subgoals.Count) ? subgoals[Index] : null;

    public QAXRSubgoalType CurrentType => Current != null ? Current.type : QAXRSubgoalType.Completed;

    public string CurrentLabel
    {
        get
        {
            if (Failed) return "FAILED";
            if (Completed) return "Completed";
            QAXRSubgoal g = Current;
            return g == null ? "-" : (Index + 1) + "/" + Count + " " + g.type + " (" + g.name + ")";
        }
    }

    public void Begin(string missionName, List<QAXRSubgoal> list)
    {
        MissionName = missionName;
        subgoals = list;
        Index = 0;
        SubgoalTime = 0f;
        Completed = Failed = false;
        FailureReason = "";

        if (subgoals == null || subgoals.Count == 0) { Fail("Mission has no subgoals"); return; }
        for (int i = 0; i < subgoals.Count; i++)
        {
            QAXRSubgoal g = subgoals[i];
            bool needsTarget = g.type == QAXRSubgoalType.ObserveTarget || g.type == QAXRSubgoalType.ApproachTarget ||
                               g.type == QAXRSubgoalType.InteractWithTarget;
            if (needsTarget && g.target == null)
            {
                Fail("Subgoal '" + g.name + "' (" + g.type + ") has no target - invalid or unreachable interaction");
                return;
            }
        }
        Emit("SubgoalStart", CurrentLabel);
    }

    /// <summary>Floor point the agent should navigate to for the current subgoal.</summary>
    public Vector3 NavigationPoint()
    {
        QAXRSubgoal g = Current;
        if (g == null) return Vector3.zero;
        Vector3 p = (g.type == QAXRSubgoalType.ApproachTarget || g.type == QAXRSubgoalType.InteractWithTarget) && g.target != null
            ? g.target.ApproachPosition
            : g.position;
        p.y = 0f;
        return p;
    }

    /// <summary>Returns true when the subgoal changed this step.</summary>
    public bool Tick(float dt, Context c)
    {
        QAXRSubgoal g = Current;
        if (g == null) return false;
        SubgoalTime += dt;

        bool done;
        switch (g.type)
        {
            case QAXRSubgoalType.NavigateToTarget:
            case QAXRSubgoalType.NavigateToExit:
                Vector3 d = g.position - c.position; d.y = 0f;
                done = d.magnitude <= g.completionRadius || (g.completeWhenPassedZ && c.position.z >= g.position.z);
                if (g.type == QAXRSubgoalType.NavigateToExit) done = d.magnitude <= g.completionRadius;
                break;
            case QAXRSubgoalType.ObserveTarget:
                done = c.targetVisible && c.attentionDwell >= observeDwellSeconds;
                break;
            case QAXRSubgoalType.ApproachTarget:
                Vector3 t = g.target.AttentionPosition - c.position; t.y = 0f;
                done = t.magnitude <= g.target.interactionRange && c.speed < 0.05f && c.facingError <= approachFacingTolerance;
                break;
            case QAXRSubgoalType.InteractWithTarget:
                done = c.interactionDone;
                break;
            default:
                done = true;
                break;
        }

        if (done)
        {
            Emit("SubgoalComplete", CurrentLabel + " t=" + SubgoalTime.ToString("F2") + "s");
            Index++;
            SubgoalTime = 0f;
            if (Index >= subgoals.Count || subgoals[Index].type == QAXRSubgoalType.Completed)
            {
                Completed = true;
                return true;
            }
            Emit("SubgoalStart", CurrentLabel);
            return true;
        }

        if (SubgoalTime > g.timeout)
            Fail("Subgoal '" + g.name + "' timed out after " + g.timeout.ToString("F0") + " s");
        return false;
    }

    public void Fail(string reason)
    {
        if (Failed) return;
        Failed = true;
        FailureReason = reason;
        Emit("MissionFailed", reason);
    }

    private void Emit(string type, string detail) => OnEvent?.Invoke(type, detail);
}
