using System.Text;
using UnityEngine;

/// <summary>
/// Layer B - Context-aware stochastic action selection.
///
/// Two tiers (explainable, easy to calibrate):
///  1. Task rules: obligatory behaviour derived from the current subgoal
///     (e.g. approaching the panel -> attend the panel; interacting -> Interact).
///  2. Utility sampling: each eligible action gets  utility = profile base weight x context multipliers,
///     then one action is drawn proportionally with the agent's own seeded RNG.
///
/// Context used: subgoal, distance to goal, visible/novel objects, obstacles ahead, heading error (turns),
/// current and previous action, time since the last non-walk action, accumulated stop time, profile.
/// The selector never sees fault or risk information.
/// </summary>
public static class QAXRBehaviorSelector
{
    public struct Context
    {
        public QAXRSubgoalType subgoal;
        public float distToGoal;
        public bool approaching;              // Observe/Approach and close to target
        public QAXRPerceivable taskTarget;
        public bool taskTargetVisible;
        public bool taskTargetNovel;
        public bool hasLandmark;
        public QAXRPerception.Percept landmark;
        public float obstacleSurface;         // m to nearest obstacle/agent surface
        public bool novelNearby;
        public float headingError;            // deg body vs. steering direction
        public float speed;
        public QAXRAgentAction current, previous;
        public float timeSinceNonWalk;
        public float totalStop;
        public bool openSpace;                // no obstacle near, not near walls
        public bool targetOccupied;           // another agent is using the interaction target
        public string trigger;
    }

    public struct Choice
    {
        public QAXRAgentAction action;
        public float duration;
        public QAXRPerceivable focus;
        public float detourOffset;
        public float speedScale;
        public string reason;
        public string utilities;
        public double draw;
    }

    private static readonly QAXRAgentAction[] Candidates =
    {
        QAXRAgentAction.Walk, QAXRAgentAction.SlowDown, QAXRAgentAction.Stop, QAXRAgentAction.LookAtTarget,
        QAXRAgentAction.LookAround, QAXRAgentAction.TurnBody, QAXRAgentAction.InspectObject
    };

    public static Choice Select(Context c, QAXRBehaviorProfile p, System.Random rng)
    {
        Choice ch = new Choice { speedScale = 1f + Range(rng, -p.speedVariation, p.speedVariation), utilities = "", draw = -1 };

        // ---------- Tier 1: task rules ----------
        if (c.subgoal == QAXRSubgoalType.InteractWithTarget)
            return Rule(ch, QAXRAgentAction.Interact, 999f, c.taskTarget, "task: interact with target");

        if (c.subgoal == QAXRSubgoalType.ApproachTarget)
        {
            if (c.targetOccupied && c.distToGoal < 2.5f)
                return Rule(ch, QAXRAgentAction.Stop, 1.0f, c.taskTarget, "task: target occupied by another agent, waiting");
            return Rule(ch, QAXRAgentAction.LookAtTarget, 999f, c.taskTarget, "task: approach - attend target, slow down, align");
        }

        if (c.subgoal == QAXRSubgoalType.ObserveTarget && c.taskTargetVisible)
        {
            // Cautious profiles may pause when an unfamiliar task object first appears.
            if (c.taskTargetNovel && c.totalStop < p.maxTotalStopSeconds)
            {
                double d = rng.NextDouble();
                ch.draw = d;
                if (d < p.noveltyCaution * 0.6f)
                    return Rule(ch, QAXRAgentAction.Stop, Range(rng, p.stopDuration), c.taskTarget, "task: target found (unfamiliar) - pause and look");
            }
            return Rule(ch, QAXRAgentAction.LookAtTarget, 999f, c.taskTarget, "task: target found - look at it");
        }

        if (c.current == QAXRAgentAction.Stop || c.current == QAXRAgentAction.InspectObject || c.current == QAXRAgentAction.Interact)
            return Rule(ch, QAXRAgentAction.ResumeNavigation, 0.6f, null, "resume after " + c.current);

        // ---------- Tier 2: utility sampling ----------
        bool searching = c.subgoal == QAXRSubgoalType.ObserveTarget && !c.taskTargetVisible;
        float gap = c.timeSinceNonWalk < p.minActionGap ? 0.1f : 1f;
        float turnAhead = Mathf.Clamp01((c.headingError - 20f) / 60f);
        float obstacleNear = c.obstacleSurface < 2.5f ? Mathf.Clamp01((2.5f - c.obstacleSurface) / 2f) : 0f;
        float novel = c.novelNearby ? 1f : 0f;
        bool canStop = c.totalStop < p.maxTotalStopSeconds && obstacleNear < 0.6f;

        float[] u = new float[Candidates.Length];
        u[0] = p.wWalk;
        u[1] = p.wSlowDown * (1f + 3f * turnAhead + 3f * obstacleNear + 2f * novel * p.noveltyCaution) * Mathf.Max(gap, obstacleNear);
        u[2] = canStop ? p.wStop * (1f + 3f * novel * p.noveltyCaution) * gap : 0f;
        u[3] = c.hasLandmark && !c.approaching ? p.wLookAtTarget * (0.5f + p.curiosity) * gap : 0f;
        u[4] = c.approaching ? 0f : p.wLookAround * gap * (obstacleNear > 0.5f ? 0.3f : 1f) * (searching ? 3f : 1f);
        u[5] = p.maxDetourOffset > 0f && c.openSpace && c.distToGoal > 4f && !c.approaching && !searching ? p.wTurnBody * gap : 0f;
        u[6] = c.hasLandmark && !c.approaching && canStop && c.landmark.distance <= 4f ? p.wInspect * (1f + 3f * p.curiosity) * gap : 0f;

        if (c.previous != QAXRAgentAction.Walk)
            for (int i = 1; i < Candidates.Length; i++)
                if (Candidates[i] == c.previous) u[i] *= p.repetitionPenalty;

        float total = 0f;
        for (int i = 0; i < u.Length; i++) total += u[i];
        double draw = rng.NextDouble();
        ch.draw = draw;
        double acc = draw * total;
        int pick = 0;
        for (int i = 0; i < u.Length; i++)
        {
            acc -= u[i];
            if (acc < 0) { pick = i; break; }
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < u.Length; i++)
        {
            if (u[i] <= 0f) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Candidates[i]).Append(':').Append((u[i] / total).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        ch.utilities = sb.ToString();
        ch.action = Candidates[pick];

        string ctx = (searching ? "searching for target; " : "") + (obstacleNear > 0f ? "obstacle near; " : "") +
                     (turnAhead > 0f ? "turn ahead; " : "") + (novel > 0f ? "novel object; " : "") +
                     (gap < 1f ? "recent action; " : "") + "trigger=" + c.trigger;
        switch (ch.action)
        {
            case QAXRAgentAction.Walk: ch.duration = Range(rng, p.decisionInterval); break;
            case QAXRAgentAction.SlowDown: ch.duration = Range(rng, p.slowDownDuration); break;
            case QAXRAgentAction.Stop: ch.duration = Mathf.Min(Range(rng, p.stopDuration), p.maxTotalStopSeconds - c.totalStop); break;
            case QAXRAgentAction.LookAtTarget: ch.duration = Range(rng, p.lookAtLandmarkDuration); ch.focus = c.landmark.obj; break;
            case QAXRAgentAction.LookAround: ch.duration = 0f; break; // agent builds a glance sequence
            case QAXRAgentAction.TurnBody:
                ch.duration = Range(rng, p.turnBodyDuration);
                ch.detourOffset = (rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, p.maxDetourOffset * 0.5f, p.maxDetourOffset);
                break;
            case QAXRAgentAction.InspectObject:
                ch.duration = Mathf.Min(Range(rng, p.inspectDuration), Mathf.Max(0.5f, p.maxTotalStopSeconds - c.totalStop));
                ch.focus = c.landmark.obj;
                break;
        }
        ch.reason = "utility: " + ctx;
        return ch;
    }

    private static Choice Rule(Choice ch, QAXRAgentAction a, float duration, QAXRPerceivable focus, string reason)
    {
        ch.action = a;
        ch.duration = duration;
        ch.focus = focus;
        ch.reason = reason;
        return ch;
    }

    public static float Range(System.Random rng, Vector2 r) => Range(rng, r.x, r.y);
    public static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
}
