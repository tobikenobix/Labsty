using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChemLab
{
    /// <summary>Random hazard placement into HazardZones. TryPose is shared by the run and the Scene-view preview.</summary>
    public static class HazardPlacement
    {
        const float Lift = 0.01f;     // hazard floats this far above the surface during the overlap test
        const float Shrink = 0.95f;   // overlap test uses 95% of the hazard's collider, so touching neighbours is fine
        // ponytail: tuning knobs, no inspector for them; raise Tries if a big, cluttered zone often falls back
        const int Tries = 20;            // random samples per hazard before it stays at its authored spot
        const float MinSpacing = 0.5f;   // metres between a moved hazard and any other hazard of the run

        /// <summary>
        /// Moves each hazard of the run that has zones to a random free spot in one of them. Zones stay inside the
        /// hazard's own area, so it moves locally and its area / difficulty labels stay true. Several hazards may
        /// share a zone; MinSpacing keeps them apart. Hazards without zones, or with no free spot after Tries
        /// samples per zone, stay where the scene puts them. The same seed and scene give the same placement.
        /// </summary>
        public static void Place(IList<HazardRecord> records, int seed)
        {
            var rng = new System.Random(seed);
            var zones = UnityEngine.Object.FindObjectsByType<HazardZone>(FindObjectsSortMode.None).OrderBy(z => z.name).ToList();
            var hazards = UnityEngine.Object.FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None)
                .Where(i => i.kind == ChemLabInspectable.Kind.Hazard && records.Any(r => r.data.id == i.hazardId)).ToList();
            Physics.SyncTransforms();

            foreach (var rec in records.OrderBy(r => r.data.id, StringComparer.Ordinal))
            {
                var h = hazards.FirstOrDefault(i => i.hazardId == rec.data.id);
                var cands = zones.Where(z => z.Accepts(rec.data.id)).ToList();
                if (h == null || h.GetComponent<BoxCollider>() == null || cands.Count == 0) continue;

                // zones in random order, Tries samples each: every zone gets an equal chance however cluttered it is
                bool moved = false;
                foreach (var z in cands.OrderBy(_ => rng.Next()).ToList())
                for (int t = 1; t <= Tries && !moved; t++)   // stops every remaining zone too once moved
                {
                    if (!TryPose(z, h, (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), out var p, out var r)) continue;
                    if (hazards.Any(o => o != h && Vector3.Distance(o.transform.position, p) < MinSpacing)) continue;
                    h.transform.SetPositionAndRotation(p, r);
                    Physics.SyncTransforms();   // later hazards' overlap tests must see this one
                    rec.zoneName = z.name;
                    Debug.Log($"[ChemLab] {rec.data.id} -> {z.name} at {p.ToString("F2")}, try {t}");
                    moved = true;
                }
                if (!moved) Debug.LogWarning($"[ChemLab] {rec.data.id}: no free spot in {cands.Count} zone(s) after {Tries} tries each, stays at its authored spot");
            }
        }

        public const int GridSide = 8;

        /// <summary>
        /// The zone's GridSide x GridSide preview spots for `hazard`, with fixed yaws so they don't flicker.
        /// One source for the Scene-view dots, the in-app ZoneView and the 200-seed check.
        /// </summary>
        public static IEnumerable<(Vector3 pos, Quaternion rot, bool ok)> Grid(HazardZone zone, ChemLabInspectable hazard)
        {
            var rng = new System.Random(0);
            for (int i = 0; i < GridSide; i++)
                for (int j = 0; j < GridSide; j++)
                {
                    bool ok = TryPose(zone, hazard, (i + 0.5f) / GridSide, (j + 0.5f) / GridSide, (float)rng.NextDouble(), out var p, out var r);
                    yield return (p, r, ok);
                }
        }

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
