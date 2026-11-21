using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;

namespace ChemLab
{
    /// <summary>
    /// Developer mode in the lab (lobby > Choose hazards > Developer mode, ChemLabRun.DeveloperMode): every hazard
    /// of the run gets a yellow box, solved ones too. Select one the usual way (trigger / grip / mouse click): it
    /// turns cyan and its HazardZones appear with their spots, green = it fits there, red = blocked or no surface
    /// (same spots as the Scene view, HazardPlacement.Grid). Selecting never opens a question or counts.
    /// Only active in the lab scene, so the lobby and tutorial inspect as usual. In the Editor, Z also toggles it.
    /// </summary>
    public class ZoneView : MonoBehaviour
    {
        bool on;
        Material hazardMat, selectedMat, fitMat, blockedMat, zoneMat;
        Mesh cube, sphere;
        List<ChemLabInspectable> hazards = new List<ChemLabInspectable>();
        List<HazardZone> zones = new List<HazardZone>();
        ChemLabInspectable selected;
        readonly List<(Vector3 pos, bool ok)> spots = new List<(Vector3, bool)>();
#if UNITY_EDITOR
        InputAction toggle;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("ZoneView (developer mode)");
            DontDestroyOnLoad(go);
            go.AddComponent<ZoneView>();
        }

        void Awake()
        {
            var shader = Shader.Find("Sprites/Default");   // alpha-blended, no culling; in Always Included Shaders, so it ships in release builds
            if (shader == null) { Debug.LogError("[ZoneView] Sprites/Default shader missing, developer mode can't draw"); enabled = false; return; }
            Material M(Color c) => new Material(shader) { color = c };
            hazardMat = M(new Color(1f, 0.92f, 0.1f, 0.4f));    // yellow: the C collider view already uses orange
            selectedMat = M(new Color(0f, 0.9f, 1f, 0.55f));
            fitMat = M(new Color(0.1f, 0.95f, 0.3f, 0.95f));
            blockedMat = M(new Color(1f, 0.15f, 0.15f, 0.95f));
            zoneMat = M(new Color(1f, 0.92f, 0.1f, 0.15f));
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            SceneManager.sceneLoaded += (_, _) => Apply();
#if UNITY_EDITOR
            toggle = new InputAction("Developer mode", InputActionType.Button, "<Keyboard>/z");
            toggle.Enable();
#endif
            Apply();   // the first scene loaded before this object existed
        }

#if UNITY_EDITOR
        void OnDestroy() => toggle?.Dispose();
#endif

        /// <summary>On in the lab scene while DeveloperMode is set; off everywhere else.</summary>
        void Apply()
        {
            on = ChemLabRun.DeveloperMode && SceneManager.GetActiveScene().name == ChemLabRun.LabScene;
            ChemLabInspectable.DevSelect = on ? Select : (System.Action<ChemLabInspectable>)null;
            selected = null;
            spots.Clear();
            if (!on) return;
            var inRun = ChemLabSession.Instance != null ? ChemLabSession.Instance.Records.Select(r => r.data.id).ToList() : null;
            hazards = FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None)
                .Where(i => i.kind == ChemLabInspectable.Kind.Hazard && (inRun == null || inRun.Contains(i.hazardId))).ToList();
            zones = FindObjectsByType<HazardZone>(FindObjectsSortMode.None).ToList();
            Debug.Log($"[ZoneView] developer mode on: {hazards.Count} hazards highlighted, {zones.Count} zones; select a hazard to see where it can go");
        }

        void Update()
        {
#if UNITY_EDITOR
            if (toggle.WasPressedThisFrame()) { ChemLabRun.DeveloperMode = !ChemLabRun.DeveloperMode; Apply(); }
#endif
            if (!on) return;
            foreach (var h in hazards)
            {
                var b = h != null ? h.GetComponent<BoxCollider>() : null;   // drawn from the collider's shape even after a solve disables it
                if (b == null) continue;
                Draw(cube, h == selected ? selectedMat : hazardMat, h.transform.localToWorldMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size * 1.04f));
            }
            if (selected == null) return;
            foreach (var z in zones.Where(z => z != null && z.Accepts(selected.hazardId)))
                Draw(cube, zoneMat, Matrix4x4.TRS(z.transform.position, z.transform.rotation, z.size));
            foreach (var (pos, ok) in spots)
                Draw(sphere, ok ? fitMat : blockedMat, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.04f));
        }

        static void Draw(Mesh mesh, Material mat, Matrix4x4 m) => Graphics.RenderMesh(new RenderParams(mat), mesh, 0, m);

        void Select(ChemLabInspectable item)
        {
            if (item.kind != ChemLabInspectable.Kind.Hazard) { Debug.Log($"[ZoneView] {item.name} is not a hazard"); return; }
            selected = item;
            spots.Clear();
            Physics.SyncTransforms();
            var mine = zones.Where(z => z != null && z.Accepts(item.hazardId)).OrderBy(z => z.name).ToList();
            var parts = new List<string>();
            foreach (var z in mine)
            {
                int fit = 0, n = 0;
                foreach (var (p, _, ok) in HazardPlacement.Grid(z, item)) { spots.Add((p, ok)); n++; if (ok) fit++; }
                parts.Add($"{z.name} {fit}/{n} fit");
            }
            Debug.Log($"[ZoneView] {item.hazardId}: " + (mine.Count == 0 ? "no zones, stays at its scene spot" : string.Join(", ", parts)));
        }
    }
}
