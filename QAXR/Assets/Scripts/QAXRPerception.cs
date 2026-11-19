using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simulated perception (separate from planning and motion).
/// - View cone around the simulated HEAD forward direction (not eye gaze), limited range.
/// - Optional occlusion check with Physics.Linecast.
/// - Close-range body awareness (360 deg) for obstacles and other agents.
/// Uses the head pivot, NOT the camera, so application-controlled camera motion (injected fault)
/// cannot influence what the agent perceives or decides.
/// </summary>
[Serializable]
public class QAXRPerception
{
    public float viewRange = 9f;
    [Tooltip("Half angle (deg) of the horizontal view cone around the head forward.")]
    public float viewHalfAngle = 55f;
    [Tooltip("Obstacles / agents closer than this are always noticed (body awareness).")]
    public float proximityRadius = 1.6f;
    public bool occlusionCheck = true;

    public struct Percept
    {
        public QAXRPerceivable obj;
        public float distance;    // floor-plane distance from agent to object
        public float bearing;     // deg relative to body forward (+ = right)
        public bool novel;        // first time this object is perceived
    }

    [NonSerialized] public readonly List<Percept> visible = new List<Percept>();
    [NonSerialized] private readonly HashSet<QAXRPerceivable> seen = new HashSet<QAXRPerceivable>();
    [NonSerialized] private readonly HashSet<QAXRPerceivable> inspected = new HashSet<QAXRPerceivable>();
    [NonSerialized] private List<QAXRPerceivable> world = new List<QAXRPerceivable>();

    /// <summary>Collect perceivable objects in a deterministic order. Call at run start.</summary>
    public void RefreshWorld()
    {
        world = new List<QAXRPerceivable>(UnityEngine.Object.FindObjectsByType<QAXRPerceivable>(FindObjectsSortMode.None));
        world.Sort((a, b) =>
        {
            int c = a.kind.CompareTo(b.kind);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.displayName, b.displayName);
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });
        Physics.SyncTransforms();
    }

    public void ResetMemory()
    {
        visible.Clear();
        seen.Clear();
        inspected.Clear();
    }

    public bool HasSeen(QAXRPerceivable p) => p != null && seen.Contains(p);
    public bool WasInspected(QAXRPerceivable p) => p != null && inspected.Contains(p);
    public void MarkInspected(QAXRPerceivable p) { if (p != null) inspected.Add(p); }

    public void Sense(QAXRAgentController self, Transform body, Transform head)
    {
        visible.Clear();
        Vector3 pos = body.position; pos.y = 0f;
        Vector3 headFwd = head.forward; headFwd.y = 0f;
        if (headFwd.sqrMagnitude < 1e-6f) headFwd = body.forward;
        headFwd.Normalize();

        for (int i = 0; i < world.Count; i++)
        {
            QAXRPerceivable p = world[i];
            if (p == null || !p.isActiveAndEnabled) continue;
            if (p.kind == QAXRPerceivableKind.Agent && p.Owner == self) continue;
            if (p.kind == QAXRPerceivableKind.Agent && p.Owner != null && !p.Owner.Active) continue;

            Vector3 to = p.FloorPosition - pos;
            float dist = to.magnitude;
            bool bodyAware = (p.kind == QAXRPerceivableKind.Obstacle || p.kind == QAXRPerceivableKind.Agent) &&
                             dist - p.radius <= proximityRadius;
            bool inCone = false;
            if (!bodyAware && dist <= viewRange)
            {
                Vector3 toAttention = p.AttentionPosition - head.position; toAttention.y = 0f;
                float ang = toAttention.sqrMagnitude < 1e-6f ? 0f : Vector3.Angle(headFwd, toAttention);
                inCone = ang <= viewHalfAngle && (!occlusionCheck || LineOfSight(head.position, p));
            }
            if (!bodyAware && !inCone) continue;

            float bearing = dist < 1e-4f ? 0f : Vector3.SignedAngle(Flat(body.forward), to, Vector3.up);
            visible.Add(new Percept { obj = p, distance = dist, bearing = bearing, novel = !seen.Contains(p) });
        }
        for (int i = 0; i < visible.Count; i++) seen.Add(visible[i].obj);
    }

    public bool IsVisible(QAXRPerceivable p)
    {
        for (int i = 0; i < visible.Count; i++) if (visible[i].obj == p) return true;
        return false;
    }

    /// <summary>Closest visible landmark that has not been inspected yet, within range.</summary>
    public bool TryGetUninspectedLandmark(float maxDistance, out Percept result)
    {
        result = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < visible.Count; i++)
        {
            Percept v = visible[i];
            if (v.obj.kind != QAXRPerceivableKind.Landmark || inspected.Contains(v.obj) || v.distance > maxDistance) continue;
            if (v.distance < best) { best = v.distance; result = v; found = true; }
        }
        return found;
    }

    /// <summary>Distance to the surface of the nearest perceived obstacle/agent (m), or +inf.</summary>
    public float NearestObstacleSurface()
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < visible.Count; i++)
        {
            Percept v = visible[i];
            if (v.obj.kind != QAXRPerceivableKind.Obstacle && v.obj.kind != QAXRPerceivableKind.Agent) continue;
            best = Mathf.Min(best, v.distance - v.obj.radius);
        }
        return best;
    }

    public bool AnyNovelNearby(float maxDistance)
    {
        for (int i = 0; i < visible.Count; i++)
            if (visible[i].novel && visible[i].distance <= maxDistance && visible[i].obj.kind != QAXRPerceivableKind.Waypoint)
                return true;
        return false;
    }

    private static bool LineOfSight(Vector3 eye, QAXRPerceivable p)
    {
        Vector3 target = p.AttentionPosition;
        Vector3 d = target - eye;
        float len = d.magnitude;
        if (len < 0.3f) return true;
        // Ignore hits on the target itself or just in front of it (e.g. the wall it is mounted on).
        RaycastHit[] hits = Physics.RaycastAll(eye, d / len, len - 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform.IsChildOf(p.transform)) continue;
            return false;
        }
        return true;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
