using System;
using System.Linq;
using UnityEngine;

namespace ChemLab
{
    /// <summary>
    /// A box where a hazard may land at the start of a run. The hazard goes to a random point inside, onto the first
    /// surface below the box top, with a random yaw between yawMin and yawMax (relative to this transform).
    /// Select the zone in the Scene view to see where its hazard fits: green dot = valid spot, red = blocked or no surface.
    /// Move / resize it live; the dots update as you drag.
    /// </summary>
    public class HazardZone : MonoBehaviour
    {
        public enum Level { Easy, Medium, Hard }

        [Tooltip("Hazard ids that may land here, e.g. H06.")]
        public string[] hazardIds = { "H06" };
        [Tooltip("Box size in metres around this transform. Centre it on the surface and keep it thin (y ~0.12): the ray " +
                 "starts at the box top, so anything taller than that half-height is passed through and rejected instead of landed on.")]
        public Vector3 size = new Vector3(0.6f, 0.12f, 0.6f);
        [Range(0f, 360f)] public float yawMin = 0f;
        [Range(0f, 360f)] public float yawMax = 360f;
        [Tooltip("Area shown in the question panel when a hazard lands here (English).")]
        public string area = "Main lab bench";
        public Level difficulty = Level.Easy;

        public bool Accepts(string id) => hazardIds != null && Array.IndexOf(hazardIds, id) >= 0;

#if UNITY_EDITOR
        static readonly Color[] LevelColors = { new Color(0.12f, 0.75f, 0.38f), new Color(0.95f, 0.62f, 0.05f), new Color(0.92f, 0.25f, 0.25f) };

        void OnDrawGizmos()
        {
            var col = LevelColors[(int)difficulty];
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(col.r, col.g, col.b, 0.15f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = col;
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(transform.position + Vector3.up * (size.y / 2f + 0.05f),
                $"{string.Join(",", hazardIds ?? new string[0])}  {difficulty}\n{area}");
        }

        void OnDrawGizmosSelected()
        {
            var id = hazardIds?.FirstOrDefault();
            var hazard = FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None).FirstOrDefault(i => i.hazardId == id);
            if (hazard == null || hazard.GetComponent<BoxCollider>() == null) return;
            Physics.SyncTransforms();
            int ok = 0, n = 0;
            bool shown = false;
            foreach (var (p, r, valid) in HazardPlacement.Grid(this, hazard))
            {
                n++;
                if (valid) ok++;
                Gizmos.color = valid ? Color.green : Color.red;
                Gizmos.DrawSphere(p, 0.012f);
                if (valid && !shown)   // one example footprint so the hazard's size is visible
                {
                    shown = true;
                    var box = hazard.GetComponent<BoxCollider>();
                    var s = hazard.transform.lossyScale;
                    Gizmos.matrix = Matrix4x4.TRS(p + r * Vector3.Scale(box.center, s), r, Vector3.one);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.Scale(box.size, s));
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }
            UnityEditor.Handles.Label(transform.position - Vector3.up * (size.y / 2f + 0.05f), $"{id}: {ok * 100 / n}% of spots fit");
        }
#endif
    }
}
