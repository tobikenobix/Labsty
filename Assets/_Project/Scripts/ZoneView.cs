#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ChemLab
{
    /// <summary>
    /// Testing aid for random placement: press Z (headset: click the left thumbstick) to highlight every hazard
    /// (orange = has HazardZones, grey = stays at its scene spot). Select one the usual way (trigger / grip / mouse
    /// click) to see its zones and spots: green = it fits there, red = blocked or no surface. While this is on,
    /// selecting never opens a question or counts as a detection. Same spots as the Scene view (HazardPlacement.Grid).
    /// Created automatically in the Editor and in development builds; stripped from release builds.
    /// </summary>
    public class ZoneView : MonoBehaviour
    {
        static bool on;
        InputAction toggle;
        Material withZones, noZones, selectedMat, fitMat, blockedMat;
        Material[] levelMats;
        Mesh cube, sphere;
        List<ChemLabInspectable> hazards = new List<ChemLabInspectable>();
        List<HazardZone> zones = new List<HazardZone>();
        ChemLabInspectable selected;
        readonly List<(Vector3 pos, bool ok)> spots = new List<(Vector3, bool)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("ZoneView (Z)");
            DontDestroyOnLoad(go);
            go.AddComponent<ZoneView>();
        }

        void Awake()
        {
            toggle = new InputAction("Zone view", InputActionType.Button);
            toggle.AddBinding("<Keyboard>/z");
            toggle.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");   // left thumbstick click, unused elsewhere
            toggle.Enable();

            var shader = Shader.Find("Sprites/Default");   // alpha-blended, no culling, ships with every project (as in ColliderView)
            Material M(Color c) => new Material(shader) { color = c };
            withZones = M(new Color(1f, 0.6f, 0f, 0.35f));
            noZones = M(new Color(0.6f, 0.6f, 0.6f, 0.25f));
            selectedMat = M(new Color(0f, 0.9f, 1f, 0.5f));
            fitMat = M(new Color(0.1f, 0.9f, 0.3f, 0.9f));
            blockedMat = M(new Color(1f, 0.2f, 0.2f, 0.9f));
            levelMats = new[] { M(new Color(0.12f, 0.75f, 0.38f, 0.18f)), M(new Color(0.95f, 0.62f, 0.05f, 0.18f)), M(new Color(0.92f, 0.25f, 0.25f, 0.18f)) };
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            SceneManager.sceneLoaded += (_, _) => { if (on) Refresh(); };
            Debug.Log("[ChemLab] Press Z (headset: click the left thumbstick) to show / hide hazard placement zones.");
        }

        void OnDestroy() => toggle?.Dispose();

        void Refresh()
        {
            hazards = FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None).Where(i => i.kind == ChemLabInspectable.Kind.Hazard).ToList();
            zones = FindObjectsByType<HazardZone>(FindObjectsSortMode.None).ToList();
            selected = null;
            spots.Clear();
        }

        void Update()
        {
            if (toggle.WasPressedThisFrame()) Toggle();
            if (!on) return;

            foreach (var h in hazards)
            {
                var b = h != null ? h.GetComponent<BoxCollider>() : null;
                if (b == null || !b.enabled) continue;   // left out of this run (or solved)
                var mat = h == selected ? selectedMat : zones.Any(z => z.Accepts(h.hazardId)) ? withZones : noZones;
                Draw(cube, mat, h.transform.localToWorldMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size * 1.04f));
            }
            if (selected == null) return;
            foreach (var z in zones.Where(z => z != null && z.Accepts(selected.hazardId)))
                Draw(cube, levelMats[(int)z.difficulty], Matrix4x4.TRS(z.transform.position, z.transform.rotation, z.size));
            foreach (var (pos, ok) in spots)
                Draw(sphere, ok ? fitMat : blockedMat, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.025f));
        }

        void Toggle()
        {
            on = !on;
            ChemLabInspectable.DevSelect = on ? Select : (System.Action<ChemLabInspectable>)null;
            if (on) Refresh(); else { selected = null; spots.Clear(); }
            Debug.Log("[ZoneView] " + (on ? "on: select a hazard to see its zones" : "off"));
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
                parts.Add($"{z.name} ({z.difficulty}) {fit}/{n} fit");
            }
            Debug.Log($"[ZoneView] {item.hazardId}: " + (mine.Count == 0 ? "no zones, stays at its scene spot" : string.Join(", ", parts)));
        }
    }
}
#endif
