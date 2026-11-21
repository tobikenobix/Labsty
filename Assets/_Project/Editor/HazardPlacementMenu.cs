using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChemLab;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu: Chemistry Lab > Hazard Placement. Runs the same HazardPlacement.Place the lab runs at start, in edit mode.
/// Preview moves the hazards (Ctrl+Z puts them back). Check runs 200 seeds and restores the layout.
/// </summary>
public static class HazardPlacementMenu
{
    const int Seeds = 200;

    [MenuItem("Chemistry Lab/Hazard Placement/Preview Random Placement")]
    static void Preview()
    {
        var hazards = Hazards();
        Undo.RecordObjects(hazards.Select(h => (Object)h.transform).ToArray(), "Preview hazard placement");
        int seed = Random.Range(0, int.MaxValue);
        Debug.Log($"[ChemLab] preview placement seed {seed} (Ctrl+Z to undo)");
        HazardPlacement.Place(Records(hazards), seed);
    }

    [MenuItem("Chemistry Lab/Hazard Placement/Check 200 Seeds")]
    public static string Check()
    {
        var hazards = Hazards();
        var zones = Object.FindObjectsByType<HazardZone>(FindObjectsSortMode.None).OrderBy(z => z.name).ToList();
        var home = hazards.ToDictionary(h => h, h => (h.transform.position, h.transform.rotation));
        var zoned = hazards.Where(h => zones.Any(z => z.Accepts(h.hazardId))).Select(h => h.hazardId).ToList();
        var fallbacks = zoned.ToDictionary(id => id, _ => 0);
        var hits = zones.ToDictionary(z => z.name, _ => 0);
        var logs = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = false;   // 200 x per-hazard lines would bury the summary
        try
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var recs = Records(hazards);
                HazardPlacement.Place(recs, seed);
                foreach (var r in recs.Where(r => zoned.Contains(r.data.id)))
                    if (r.zoneName == null) fallbacks[r.data.id]++; else hits[r.zoneName]++;
                foreach (var kv in home) kv.Key.transform.SetPositionAndRotation(kv.Value.position, kv.Value.rotation);
            }
        }
        finally
        {
            foreach (var kv in home) kv.Key.transform.SetPositionAndRotation(kv.Value.position, kv.Value.rotation);
            Physics.SyncTransforms();
            Debug.unityLogger.logEnabled = logs;
        }

        var sb = new StringBuilder($"[ChemLab] hazard placement, {Seeds} seeds\n");
        foreach (var id in zoned) sb.AppendLine($"  {id}: fell back to its authored spot {fallbacks[id]}x");
        var hidden = new List<string>();
        foreach (var z in zones)
        {
            var (seen, fit) = Seen(z, hazards);
            if (fit > 0 && seen == 0) hidden.Add(z.name);
            sb.AppendLine($"  {z.name} ({z.difficulty}): used {hits[z.name]}x, {fit}/{HazardPlacement.GridSide * HazardPlacement.GridSide} grid spots fit, {seen} of those visible from the aisles");
        }
        var report = sb.ToString();
        if (fallbacks.Values.Any(n => n > 0) || hidden.Count > 0) Debug.LogWarning(report); else Debug.Log(report);
        return report;
    }

    // head-height points along both aisles (between the island and the wall benches), every metre
    static readonly List<Vector3> Aisle = Enumerable.Range(0, 15).SelectMany(z => new[] { new Vector3(-1.7f, 1.6f, z), new Vector3(1.7f, 1.6f, z) }).ToList();

    /// <summary>
    /// The zone's preview grid for its first hazard: how many spots fit, and how many of those have a clear line
    /// from at least one aisle point to the hazard's centre.
    /// </summary>
    static (int seen, int fit) Seen(HazardZone z, List<ChemLabInspectable> hazards)
    {
        var h = hazards.FirstOrDefault(i => z.Accepts(i.hazardId));
        var box = h != null ? h.GetComponent<BoxCollider>() : null;
        if (box == null) return (0, 0);
        int seen = 0, fit = 0;
        foreach (var (p, r, ok) in HazardPlacement.Grid(z, h))
        {
            if (!ok) continue;
            fit++;
            var target = p + r * Vector3.Scale(box.center, h.transform.lossyScale);
            if (Aisle.Any(eye => !Physics.Linecast(eye, target, ~0, QueryTriggerInteraction.Ignore))) seen++;
        }
        return (seen, fit);
    }

    static List<ChemLabInspectable> Hazards() =>
        Object.FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None).Where(i => i.kind == ChemLabInspectable.Kind.Hazard).ToList();

    static List<HazardRecord> Records(List<ChemLabInspectable> hazards) =>
        hazards.Select(h => new HazardRecord { data = new HazardData { id = h.hazardId } }).ToList();
}
