using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Toggleable runtime debug drawing (Game and Scene view) using LineRenderers on the "Ignore Raycast" layer.
/// Read-only: it never changes agent state. Agent cameras exclude this layer so the lines do not appear in the
/// agent view, and perception raycasts ignore it, so measurements are unaffected.
///   Path        agent -> steering point -> current navigation goal (cyan / yellow)
///   Head        simulated head forward direction (white) - NOT eye gaze
///   Perception  horizontal view cone of the perception system (green)
///   Target      line from head to the current attention target (magenta)
/// </summary>
public class QAXRDebugVisualizer : MonoBehaviour
{
    public bool showPath = true;
    public bool showHead = true;
    public bool showPerception = false;
    public bool showTarget = true;
    public bool showAllAgents = false;
    public float lineWidth = 0.03f;

    private QAXRScenarioController scenario;
    private Material material;

    private class Lines { public LineRenderer path, head, cone, target; }
    private readonly Dictionary<QAXRAgentController, Lines> lines = new Dictionary<QAXRAgentController, Lines>();

    public void Bind(QAXRScenarioController s) { scenario = s; }

    private void LateUpdate()
    {
        if (scenario == null) return;
        for (int i = 0; i < scenario.runs.Count; i++)
        {
            QAXRAgentController a = scenario.runs[i].agent;
            Lines l = Get(a);
            bool vis = (showAllAgents || i == scenario.SelectedIndex) && a.Active && a.gameObject.activeInHierarchy;

            Set(l.path, vis && showPath, new[] { a.transform.position + Vector3.up * 0.08f, a.SteerPoint + Vector3.up * 0.08f, a.NavGoal + Vector3.up * 0.08f });

            Transform h = a.headPivot;
            if (h != null)
            {
                Set(l.head, vis && showHead, new[] { h.position, h.position + h.forward * 1.8f });

                QAXRPerceivable focusTarget = null;
                foreach (var v in a.perception.visible) if (v.obj.displayName == a.AttentionName) { focusTarget = v.obj; break; }
                Set(l.target, vis && showTarget && focusTarget != null,
                    focusTarget != null ? new[] { h.position, focusTarget.AttentionPosition } : new Vector3[0]);

                var cone = new List<Vector3>();
                if (vis && showPerception)
                {
                    Vector3 f = h.forward; f.y = 0f; f.Normalize();
                    Vector3 o = new Vector3(h.position.x, 0.1f, h.position.z);
                    float half = a.perception.viewHalfAngle, range = a.perception.viewRange;
                    cone.Add(o);
                    for (int k = 0; k <= 16; k++)
                        cone.Add(o + Quaternion.Euler(0f, -half + 2f * half * k / 16f, 0f) * f * range);
                    cone.Add(o);
                }
                Set(l.cone, vis && showPerception, cone.ToArray());
            }
        }
    }

    private Lines Get(QAXRAgentController a)
    {
        if (lines.TryGetValue(a, out Lines l) && l.path != null) return l;
        if (material == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            material = new Material(sh);
        }
        l = new Lines
        {
            path = Make(a.agentId + " path", new Color(0.3f, 0.9f, 1f), new Color(1f, 0.85f, 0.3f)),
            head = Make(a.agentId + " head", Color.white, Color.white),
            cone = Make(a.agentId + " perception", new Color(0.3f, 1f, 0.4f, 0.8f), new Color(0.3f, 1f, 0.4f, 0.8f)),
            target = Make(a.agentId + " target", new Color(1f, 0.3f, 0.9f), new Color(1f, 0.3f, 0.9f))
        };
        lines[a] = l;
        return l;
    }

    private LineRenderer Make(string n, Color c0, Color c1)
    {
        GameObject go = new GameObject("Debug " + n);
        go.transform.SetParent(transform, false);
        go.layer = 2; // Ignore Raycast: excluded from agent cameras and perception
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = material;
        lr.startColor = c0; lr.endColor = c1;
        lr.startWidth = lr.endWidth = lineWidth;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;
        return lr;
    }

    private static void Set(LineRenderer lr, bool on, Vector3[] pts)
    {
        if (lr == null) return;
        lr.enabled = on && pts.Length > 1;
        if (!lr.enabled) return;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
    }
}
