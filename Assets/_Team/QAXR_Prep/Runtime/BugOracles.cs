using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace QAXR
{
    /// <summary>
    /// The bug-oracle checks of Roadmap §4.5 as plain functions, so an agent (Z4/Z5) calls them at the right
    /// moment and the tests can call them on the zone 5/6 breaking points. Reporting goes through IssueSink.
    /// </summary>
    public static class BugOracles
    {
        // ---- Text or UI too small (D3 / usability) -------------------------------------------------------

        /// <summary>Visual angle in degrees of an object of the given size at the given distance.</summary>
        public static float AngularSize(float size, float distance)
            => distance <= 0f ? 180f : 2f * Mathf.Atan(size / (2f * distance)) * Mathf.Rad2Deg;

        /// <summary>Angular height of one line of text seen from the eye (TMP 3D or a world-space TextMeshProUGUI).</summary>
        public static float TextAngularHeight(TMP_Text text, Vector3 eye)
        {
            text.ForceMeshUpdate();
            int lines = Mathf.Max(1, text.textInfo.lineCount);
            float lineHeight = text.textBounds.size.y * text.transform.lossyScale.y / lines;
            return AngularSize(lineHeight, Vector3.Distance(eye, text.transform.TransformPoint(text.textBounds.center)));
        }

        /// <summary>
        /// Angular size of an interactive target from the eye: the middle of its three bounds extents, so a flat
        /// button reads as its face size whatever the viewing angle, and a long thin bar as its thickness.
        /// </summary>
        public static float TargetAngularSize(Bounds b, Vector3 eye)
        {
            float x = b.size.x, y = b.size.y, z = b.size.z;
            float middle = Mathf.Max(Mathf.Min(x, y), Mathf.Min(Mathf.Max(x, y), z));
            return AngularSize(middle, Vector3.Distance(eye, b.center));
        }

        // ---- Unreachable --------------------------------------------------------------------------------

        /// <summary>Highest point a standing person can grab: shoulder (≈ 0.82 × stature) + arm reach (≈ 0.4 × stature) [ph].</summary>
        public static float MaxReachHeight(float stature) => 1.22f * stature;

        public enum Reach { Reachable, TooHigh, NoPath, OffNavMesh }

        /// <summary>
        /// Can an agent standing at `from` walk to and grab `target`? Checks the vertical reach first, then a
        /// complete NavMesh path to a point under the target within arm reach. Needs a baked NavMesh for the path part.
        /// </summary>
        public static Reach CanReach(Vector3 from, Vector3 target, float floorY, float stature, float armReach, int areaMask = NavMesh.AllAreas)
        {
            if (target.y - floorY > MaxReachHeight(stature)) return Reach.TooHigh;
            var under = new Vector3(target.x, floorY, target.z);
            if (!NavMesh.SamplePosition(from, out var start, 1f, areaMask)) return Reach.OffNavMesh;
            if (!NavMesh.SamplePosition(under, out var end, armReach, areaMask)) return Reach.NoPath;
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(start.position, end.position, areaMask, path) && path.status == NavMeshPathStatus.PathComplete
                ? Reach.Reachable : Reach.NoPath;
        }

        // ---- Teleport into geometry ----------------------------------------------------------------------

        /// <summary>
        /// After a teleport: true if the head sphere overlaps solid geometry or there is no ground under the feet.
        /// Pass a mask without the agents' and the rig's own layers.
        /// </summary>
        public static bool LandedInGeometry(Vector3 feet, float eyeHeight, LayerMask solid, float headRadius = 0.1f)
        {
            var head = feet + Vector3.up * eyeHeight;
            bool headInside = Physics.CheckSphere(head, headRadius, solid, QueryTriggerInteraction.Ignore);
            bool noGround = !Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, 1f, solid, QueryTriggerInteraction.Ignore);
            return headInside || noGround;
        }

        // ---- Held object clipping ------------------------------------------------------------------------

        /// <summary>Deepest penetration (m) of the held object's colliders into static geometry (colliders with no Rigidbody).</summary>
        public static float HeldPenetration(GameObject held, LayerMask solid)
        {
            float deepest = 0f;
            var own = held.GetComponentsInChildren<Collider>();
            var hits = new Collider[32];
            foreach (var mine in own)
            {
                if (mine.isTrigger) continue;
                var b = mine.bounds;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, hits, Quaternion.identity, solid, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var other = hits[i];
                    if (other.attachedRigidbody || System.Array.IndexOf(own, other) >= 0) continue;
                    if (Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation,
                                                   other, other.transform.position, other.transform.rotation, out _, out float d))
                        deepest = Mathf.Max(deepest, d);
                }
            }
            return deepest;
        }

        // ---- Issue helper --------------------------------------------------------------------------------

        public static Issue BugIssue(Oracle oracle, Severity severity, Vector3 position, float time, float value = 1f, float threshold = 0f, string unit = "")
            => new Issue
            {
                id = $"Bug-{oracle}", dimension = "Bug", rule = oracle.ToString(), severity = severity.ToString(),
                value = value, threshold = threshold, unit = unit, time = time, Position = position,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, agent = -1,
            };
    }

    /// <summary>
    /// Stuck oracle: the agent is trying to reach a goal but the distance hasn't shrunk by minProgress in `timeout`
    /// seconds [ph]. Call SetGoal on every new goal, then Update every frame while the agent is moving.
    /// </summary>
    public sealed class StuckDetector
    {
        public float timeout = 3f, minProgress = 0.2f;
        float best, since;
        bool hasGoal, reported;

        public void SetGoal(float t, float distance)
        {
            hasGoal = true;
            reported = false;
            best = distance;
            since = t;
        }

        public void Clear() => hasGoal = false;

        /// <summary>Returns true once, the moment the agent counts as stuck for the current goal.</summary>
        public bool Update(float t, float distance)
        {
            if (!hasGoal || reported) return false;
            if (distance < best - minProgress) { best = distance; since = t; return false; }
            if (t - since < timeout) return false;
            reported = true;
            return true;
        }
    }

    /// <summary>
    /// Scene-wide oracles that need no agent: runtime exceptions (any thread) and rigidbodies falling below killY.
    /// Drop one into the scene. `context` lets the agent spawner say who was doing what when an exception fired.
    /// </summary>
    public class QaxrOracleMonitor : MonoBehaviour
    {
        [SerializeField] float killY = -10f;
        [SerializeField] float scanInterval = 0.5f;

        /// <summary>Optional: returns (agent, intent, position) of the agent acting right now.</summary>
        public static System.Func<(int agent, string intent, Vector3 position)> context;

        readonly System.Collections.Concurrent.ConcurrentQueue<(string message, string stack)> exceptions =
            new System.Collections.Concurrent.ConcurrentQueue<(string, string)>();
        readonly HashSet<string> seenExceptions = new HashSet<string>();
        readonly HashSet<Rigidbody> fallen = new HashSet<Rigidbody>();
        readonly Dictionary<Rigidbody, Vector3> lastSeen = new Dictionary<Rigidbody, Vector3>(); // last position above the floor

        float nextScan;

        void OnEnable() => Application.logMessageReceivedThreaded += OnLog;
        void OnDisable() => Application.logMessageReceivedThreaded -= OnLog;

        // Any thread: only queue here, never touch the Unity API.
        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                if (!message.StartsWith("[QAXR]")) exceptions.Enqueue((message, stack));
        }

        void Update()
        {
            while (exceptions.TryDequeue(out var e))
            {
                string key = e.message + "|" + FirstLine(e.stack); // same exception from the same place: report once
                if (!seenExceptions.Add(key)) continue;
                var ctx = context?.Invoke() ?? (-1, "", Vector3.zero);
                var issue = BugOracles.BugIssue(Oracle.RuntimeException, Severity.High, ctx.position, Time.time);
                issue.agent = ctx.agent;
                issue.intent = ctx.intent;
                issue.detail = e.message.Length > 300 ? e.message.Substring(0, 300) : e.message;
                IssueSink.Report(issue);
            }

            if (Time.time < nextScan) return;
            nextScan = Time.time + scanInterval;
            // ponytail: FindObjectsByType every 0.5 s; cache the list if a scene has thousands of bodies.
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                var p = rb.position;
                if (p.y > -1f) { lastSeen[rb] = p; continue; }
                if (p.y >= killY || !fallen.Add(rb)) continue;
                // Report where it left the world (the hole), not where it is now.
                var at = lastSeen.TryGetValue(rb, out var seen) ? seen : p;
                var issue = BugOracles.BugIssue(Oracle.FallThrough, Severity.High, at, Time.time, p.y, killY, "m");
                issue.detail = rb.name;
                IssueSink.Report(issue);
            }
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }
    }
}
