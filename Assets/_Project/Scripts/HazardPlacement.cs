using System;
using System.Linq;
using UnityEngine;

namespace ChemLab
{
    /// <summary>Random hazard placement into HazardZones. TryPose is shared by the run and the Scene-view preview.</summary>
    public static class HazardPlacement
    {
        const float Lift = 0.01f;     // hazard floats this far above the surface during the overlap test
        const float Shrink = 0.95f;   // overlap test uses 95% of the hazard's collider, so touching neighbours is fine

        /// <summary>
        /// Pose for `hazard` at point (u, v) of `zone` (0..1 across its x / z) with yaw fraction t (0..1 of yawMin..yawMax).
        /// False when there is no flat surface under the point or something overlaps the hazard there.
        /// pos is still set (to the box top) on failure, for the preview dot.
        /// </summary>
        public static bool TryPose(HazardZone zone, ChemLabInspectable hazard, float u, float v, float t, out Vector3 pos, out Quaternion rot)
        {
            var z = zone.transform;
            var top = z.position + z.rotation * new Vector3((u - 0.5f) * zone.size.x, zone.size.y / 2f, (v - 0.5f) * zone.size.z);
            rot = z.rotation * Quaternion.Euler(0f, Mathf.Lerp(zone.yawMin, zone.yawMax, t), 0f);
            pos = top;
            if (!Surface(top, zone.size.y, out float y)) return false;

            var box = hazard.GetComponent<BoxCollider>();
            var scale = hazard.transform.lossyScale;
            var c = Vector3.Scale(box.center, scale);
            var s = Vector3.Scale(box.size, scale);
            pos = new Vector3(top.x, y - (c.y - s.y / 2f), top.z);   // collider bottom on the surface

            var own = hazard.GetComponentsInChildren<Collider>(true);
            foreach (var hit in Physics.OverlapBox(pos + rot * c + Vector3.up * Lift, s * (Shrink / 2f), rot, ~0, QueryTriggerInteraction.Ignore))
                if (Array.IndexOf(own, hit) < 0) return false;
            return true;
        }

        /// <summary>Height of the first flat surface below `top` within `depth`, skipping inspectables (they get the overlap test instead).</summary>
        static bool Surface(Vector3 top, float depth, out float y)
        {
            y = 0f;
            var hits = Physics.RaycastAll(top, Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits.OrderBy(h => h.distance))
            {
                if (h.collider.GetComponentInParent<ChemLabInspectable>() != null) continue;
                if (h.normal.y < 0.9f) return false;   // sloped or vertical: not a place to stand a bottle on
                y = h.point.y;
                return true;
            }
            return false;
        }
    }
}
