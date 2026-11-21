using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Swaps the lobby's primitive placeholder equipment for the models the lab scene (ChemistryLab.unity) uses, with
/// the same meshes, materials, rotations and sizes, so both rooms look alike:
/// flask (Lab.fbx glassware), first-aid cabinet, coffee cup, coat stand + coat, fire extinguisher, the room model's
/// emergency shower / eyewash and door, and the room's floor, wall and ceiling materials.
/// Idempotent: anything already swapped is left alone. Run from the menu on the open lobby, or let
/// ChemLabSceneBuilder call <see cref="Apply"/> when it rebuilds the lobby.
/// </summary>
public static class LobbyLabEquipment
{
    const string LobbyPath = "Assets/_Project/Scenes/ChemLab_Lobby.unity";
    const string MatDir = "Assets/_Project/Materials/";
    const string LabFbx = "Assets/_Project/LabEnvironment/Lab.fbx";
    const long FlaskMeshId = 7969386909851093774;            // the lab's MidShelfGlassware flasks
    const string HangerFbx = "Assets/Resources/3D/Coathanger/PS Hanger12.fbx";
    const long HangerMeshId = -7616958594727952831;           // the lab's CoatRack stand
    const string HangerMat = "Assets/Samples/XR Interaction Toolkit/3.6.1/Hands Interaction Demo/DemoAssets/Materials/BrushedMetal.mat";
    const string FabricFbx = "Assets/Resources/3D/hanging cloth/fabric.fbx";
    const string ExtinguisherFbx = "Assets/Resources/3D/SM_Fire_extinguisher_01.fbx";

    [MenuItem("Chemistry Lab/Lobby: Use Lab Equipment Models")]
    static void Menu()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != LobbyPath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(LobbyPath);
        }
        int n = Apply(scene, undo: true);
        if (n > 0) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[ChemLab] Lobby: swapped {n} placeholder(s) for lab models." + (n > 0 ? " Save the scene to keep them." : ""));
    }

    /// <summary>Returns how many placeholders were replaced.</summary>
    public static int Apply(Scene scene, bool undo = false)
    {
        Transform decor = null, env = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Environment") { env = root.transform; decor = env.Find("Decor"); }
        if (decor == null) { Debug.LogError("[ChemLab] Lobby has no Environment/Decor."); return 0; }

        int n = 0;
        if (Flask(decor.Find("BenchProps/Flask"), undo)) n++;
        if (FirstAid(decor.Find("FirstAidBox"), undo)) n++;
        if (Cup(env.Find("Coffee Cup (practice)"), undo)) n++;
        if (Coats(decor.Find("CoatHooks"), scene, undo)) n++;
        if (Extinguisher(decor.Find("FireExtinguisher"), scene, undo)) n++;

        // fixtures and surfaces taken from the lab's room model (Lab Environment.prefab)
        var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefab);
        if (room == null) { Debug.LogError("[ChemLab] Missing " + RoomPrefab); return n; }
        var lab = new RoomInfo(room);
        if (!lab.ok) { Debug.LogError("[ChemLab] No floor (Plane018) in " + RoomPrefab); return n; }
        if (Shower(decor.Find("SafetyShower"), lab, undo)) n++;
        if (Door(decor.Find("LabDoor"), lab, undo)) n++;
        n += Surfaces(env, lab);
        return n;
    }

    // ===================================================================== room model fixtures + surfaces

    const string RoomPrefab = "Assets/_Project/LabEnvironment/Lab Environment.prefab";
    const float LobbyHalf = 4f;   // SimpleRoom(env, 8, 8): inner wall faces at +-4 m, floor top at y = 0

    // primitive shower + eyewash -> the lab's emergency shower / eyewash station (green floor mark stays)
    static bool Shower(Transform g, RoomInfo lab, bool undo)
    {
        if (g == null || g.Find("Riser") == null) return false;
        var parts = lab.Fixture("emergencyshower");
        if (parts == null) return false;
        Remove(g, undo, "Riser", "Arm", "Head", "PullRod", "PullHandle", "BowlArm", "Bowl");
        Place(lab, parts, "EmergencyShower", g, Vector3.right, undo);   // west wall, room is +X
        return true;
    }

    // primitive door -> the lab's door model; the LAB 404 sign moves above it
    static bool Door(Transform g, RoomInfo lab, bool undo)
    {
        if (g == null || g.Find("Leaf") == null) return false;
        var parts = lab.Fixture("_Door_");
        if (parts == null) return false;
        Remove(g, undo, "PostL", "PostR", "Lintel", "Leaf", "Window", "PushBar");
        var door = Place(lab, parts, "Door", g, Vector3.back, undo);      // north wall, room is -Z
        float top = RendererBounds(door).max.y;
        foreach (var n in new[] { "Sign", "SignText" })
        {
            var s = g.Find(n);
            if (s != null) s.position = new Vector3(s.position.x, Mathf.Max(s.position.y, top + 0.2f), s.position.z);
        }
        return true;
    }

    /// <summary>
    /// Copies the lab parts into a new group under <paramref name="parent"/>, turned so the side that faced into the
    /// lab faces into the lobby (<paramref name="inward"/>), standing on the floor at the same height and with the
    /// same gap to the wall as in the lab (lobby walls are square, inner faces LobbyHalf from the centre).
    /// </summary>
    static Transform Place(RoomInfo lab, List<Renderer> parts, string name, Transform parent, Vector3 inward, bool undo)
    {
        var b = Union(parts);
        var labIn = lab.InwardNormal(b.center, out float gap, b);
        var rot = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(labIn, lab.up));
        var anchor = b.center - lab.up * Vector3.Dot(b.center - lab.center, lab.up);   // on the lab floor below the fixture

        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        foreach (var r in parts)
        {
            var t = r.transform;
            var go = new GameObject(t.name);
            go.transform.SetParent(holder, false);
            go.transform.localPosition = rot * (t.position - anchor);
            go.transform.localRotation = rot * t.rotation;
            go.transform.localScale = t.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
        }
        // stand on the lobby floor below the placeholder, back to the wall with the lab's gap
        holder.position = new Vector3(parent.position.x, 0, parent.position.z);
        holder.rotation = Quaternion.identity;
        var lb = RendererBounds(holder);
        float back = Vector3.Dot(lb.center, inward) - Extent(lb, inward);
        float want = -LobbyHalf + Mathf.Clamp(gap, 0f, 0.3f);   // inner wall face, measured along the inward axis
        holder.position += inward * (want - back);
        foreach (Transform c in holder.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(c.gameObject, StaticEditorFlags.BatchingStatic);
        Created(holder.gameObject, undo);
        Debug.Log($"[ChemLab] Lobby: {name} from the lab room model ({parts.Count} parts, {lb.size.x:0.00} x {lb.size.y:0.00} x {lb.size.z:0.00} m).");
        return holder;
    }

    // floor, walls and ceiling get the lab's materials, re-tiled so a texture repeat is the same size in metres
    static int Surfaces(Transform env, RoomInfo lab)
    {
        int n = 0;
        float floorRepeat = 1f, ceilRepeat = 1f, wallRepeat = 1f;
        var floor = lab.SurfaceMaterial(lab.floor, out floorRepeat);
        Material ceiling = null, wall = null;
        if (lab.ceiling != null) ceiling = lab.SurfaceMaterial(lab.ceiling, out ceilRepeat);
        var wallR = lab.RendererWithTexture("_Wall_");
        if (wallR != null) wall = lab.SurfaceMaterial(wallR, out wallRepeat);

        float w = 2f * LobbyHalf, h = 3f;
        n += Assign(env, new[] { "Floor" }, floor, "LobbyRoom_Floor", new Vector2(w, w) / floorRepeat);
        n += Assign(env, new[] { "Ceiling" }, ceiling, "LobbyRoom_Ceiling", new Vector2(w, w) / ceilRepeat);
        n += Assign(env, new[] { "Wall_N", "Wall_S", "Wall_E", "Wall_W" }, wall, "LobbyRoom_Wall", new Vector2(w, h) / wallRepeat);
        return n;
    }

    static int Assign(Transform env, string[] names, Material src, string matName, Vector2 tiling)
    {
        if (src == null) { Debug.LogWarning($"[ChemLab] Lobby: no lab material found for {matName}"); return 0; }
        string path = MatDir + matName + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
        else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
        m.SetTextureScale("_BaseMap", tiling);
        m.SetTextureOffset("_BaseMap", Vector2.zero);
        EditorUtility.SetDirty(m);

        int n = 0;
        foreach (var name in names)
        {
            var r = env.Find(name)?.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == m) continue;
            Undo.RecordObject(r, "Lobby lab materials");
            r.sharedMaterial = m;
            n++;
        }
        if (n > 0) Debug.Log($"[ChemLab] Lobby: {string.Join("/", names)} now use {path} (copy of lab '{src.name}', tiling {tiling.x:0.0} x {tiling.y:0.0}).");
        return n;
    }

    /// <summary>The lab room prefab's floor frame and renderers, read straight from the asset.</summary>
    class RoomInfo
    {
        public readonly bool ok;
        public readonly Renderer floor, ceiling;
        public readonly Vector3 center, up, along, across;   // floor centre, up, long and short floor axes
        public readonly float halfAlong, halfAcross;
        readonly Renderer[] all;

        public RoomInfo(GameObject root)
        {
            all = root.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null).ToArray();
            floor = all.FirstOrDefault(r => r.name.StartsWith("Plane018"));
            ceiling = all.FirstOrDefault(r => r.name.StartsWith("Plane020"));
            if (floor == null) return;
            var t = floor.transform;
            var mb = floor.GetComponent<MeshFilter>().sharedMesh.bounds;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var world = axes.Select((a, i) => t.TransformVector(a * mb.size[i])).ToArray();
            var order = Enumerable.Range(0, 3).OrderBy(i => world[i].magnitude).ToArray();   // thin, short, long
            center = t.TransformPoint(mb.center);
            up = world[order[0]].normalized; if (up.y < 0) up = -up;
            across = world[order[1]].normalized; halfAcross = world[order[1]].magnitude / 2f;
            along = world[order[2]].normalized; halfAlong = world[order[2]].magnitude / 2f;
            ok = true;
        }

        public Renderer RendererWithTexture(string key) =>
            all.Where(r => Uses(r, key)).OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault();

        static bool Uses(Renderer r, string key) =>
            r.sharedMaterials.Any(m => m != null && m.mainTexture != null && m.mainTexture.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// The largest group of touching renderers that use a texture named like <paramref name="key"/>, plus small
        /// parts (pipes, screws, handles with other materials) that sit inside that group's bounds.
        /// </summary>
        public List<Renderer> Fixture(string key)
        {
            var hits = all.Where(r => Uses(r, key)).ToList();
            if (hits.Count == 0) { Debug.LogWarning($"[ChemLab] Lobby: nothing in the lab room uses a '{key}' texture."); return null; }
            var groups = new List<List<Renderer>>();
            foreach (var r in hits)
            {
                var b = r.bounds; b.Expand(0.25f);
                var touching = groups.Where(gr => gr.Any(o => b.Intersects(o.bounds))).ToList();
                var merged = new List<Renderer> { r };
                foreach (var gr in touching) { merged.AddRange(gr); groups.Remove(gr); }
                groups.Add(merged);
            }
            var best = groups.OrderByDescending(gr => Union(gr).size.sqrMagnitude).First();
            var u = Union(best); u.Expand(0.05f);
            float max = Mathf.Max(u.size.x, u.size.y, u.size.z);
            foreach (var r in all)
                if (!best.Contains(r) && r.bounds.size.magnitude < max && u.Contains(r.bounds.min) && u.Contains(r.bounds.max))
                    best.Add(r);
            if (groups.Count > 1) Debug.Log($"[ChemLab] Lobby: '{key}' is used by {groups.Count} separate objects in the lab; took the largest.");
            return best;
        }

        /// <summary>Inward normal of the lab wall nearest to p, and the gap between that wall and the bounds' back.</summary>
        public Vector3 InwardNormal(Vector3 p, out float gap, Bounds b)
        {
            var d = p - center;
            float a = Vector3.Dot(d, along), c = Vector3.Dot(d, across);
            Vector3 n; float wallDist;
            if (halfAlong - Mathf.Abs(a) < halfAcross - Mathf.Abs(c)) { n = -Mathf.Sign(a) * along; wallDist = halfAlong - Mathf.Abs(a); }
            else { n = -Mathf.Sign(c) * across; wallDist = halfAcross - Mathf.Abs(c); }
            gap = wallDist - Extent(b, n);
            return n;
        }

        /// <summary>The renderer's main material and how many metres one texture repeat covers on it.</summary>
        public Material SurfaceMaterial(Renderer r, out float metresPerRepeat)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            int sub = 0; long most = -1;
            for (int i = 0; i < mesh.subMeshCount && i < r.sharedMaterials.Length; i++)
                if ((long)mesh.GetIndexCount(i) > most) { most = mesh.GetIndexCount(i); sub = i; }
            var m = r.sharedMaterials[sub];
            metresPerRepeat = 1f;
            try
            {
                var v = mesh.vertices; var uv = mesh.uv; var tri = mesh.GetTriangles(sub);
                double aw = 0, auv = 0;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    Vector3 p0 = r.transform.TransformPoint(v[tri[i]]), p1 = r.transform.TransformPoint(v[tri[i + 1]]), p2 = r.transform.TransformPoint(v[tri[i + 2]]);
                    aw += Vector3.Cross(p1 - p0, p2 - p0).magnitude;
                    Vector2 u0 = uv[tri[i]], u1 = uv[tri[i + 1]], u2 = uv[tri[i + 2]];
                    auv += Mathf.Abs((u1.x - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (u1.y - u0.y));
                }
                if (auv > 1e-9) metresPerRepeat = (float)System.Math.Sqrt(aw / auv);
            }
            catch (System.Exception e) { Debug.LogWarning($"[ChemLab] Lobby: can't read UVs of {r.name} ({e.Message}); assuming 1 m per repeat."); }
            if (m != null && m.HasProperty("_BaseMap")) metresPerRepeat /= Mathf.Max(0.001f, m.GetTextureScale("_BaseMap").x);
            metresPerRepeat = Mathf.Clamp(metresPerRepeat, 0.1f, 20f);
            return m;
        }
    }

    static Bounds Union(List<Renderer> rs)
    {
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Bounds RendererBounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Half the size of the bounds along direction n.</summary>
    static float Extent(Bounds b, Vector3 n) => Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;

    // Bulb + neck + liquid primitives -> the lab's glass flask mesh, standing on the bench top
    static bool Flask(Transform g, bool undo)
    {
        if (g == null || g.Find("Bulb") == null) return false;
        Remove(g, undo, "Bulb", "Neck", "Liquid");
        var f = MeshObject("Flask", g, MeshById(LabFbx, FlaskMeshId), Load<Material>(MatDir + "Lab_Glass.mat"), undo);
        f.localRotation = new Quaternion(-0.67287f, -0.21736157f, -0.21736224f, 0.6728697f);
        f.localScale = Vector3.one * 1.64107f;
        SitOn(f, g);
        return true;
    }

    // green box with a cross -> the lab's first-aid cabinet model (same 0.55 m height), back against the west wall
    static bool FirstAid(Transform g, bool undo)
    {
        if (g == null || g.Find("Box") == null) return false;
        Remove(g, undo, "Box", "CrossV", "CrossH");
        // model's front faces +Z; the room is +X from the west wall, so yaw +90
        var cab = ChemLabSceneBuilder.Model("FirstAidCabinet", "Cabinet", g, Vector3.zero, new Vector3(0, 0.55f, 0), new Vector3(0, 90f, 0), out var size);
        cab.localPosition = new Vector3(-0.1f + size.z / 2f, -0.275f, 0);   // wall face is 0.1 m behind the group
        Created(cab.gameObject, undo);
        return true;
    }

    // paper cup primitives -> the CoffeeCup model the lab (H02) and the coffee corner use; the root keeps its big collider
    static bool Cup(Transform g, bool undo)
    {
        if (g == null || g.Find("Sleeve") == null) return false;
        Remove(g, undo, "Cup", "Sleeve", "Lid");
        var c = ChemLabSceneBuilder.Model("CoffeeCup", "Cup", g, Vector3.zero, new Vector3(0.09f, 0.13f, 0.09f), Vector3.zero);
        Created(c.gameObject, undo);
        return true;
    }

    // three box coats on a rail -> the lab's coat stand with a hanging coat (white, as a lab coat); board + goggles stay
    static bool Coats(Transform g, Scene scene, bool undo)
    {
        if (g == null || g.Find("Coat") == null) return false;
        Remove(g, undo, "Coat", "Collar", "Placket", "Rail");
        var rack = new GameObject("CoatRack").transform;
        rack.SetParent(g, false);
        rack.localPosition = new Vector3(-0.2f, 0, -0.15f);   // both stand by an east wall, so the lab's orientation carries over
        Created(rack.gameObject, undo);

        var h = MeshObject("hanger", rack, MeshById(HangerFbx, HangerMeshId), Load<Material>(HangerMat), undo);
        h.localPosition = new Vector3(0, 0.029f, 0);
        h.localRotation = Quaternion.Euler(-90f, 0, 0);
        h.localScale = Vector3.one * 0.4f;

        var fabric = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(FabricFbx), scene);
        fabric.name = "fabric";
        fabric.transform.SetParent(rack, false);
        fabric.transform.localPosition = new Vector3(-0.127f, 0.437f, 0.038871527f);
        fabric.transform.localRotation = new Quaternion(0, 0.7071068f, 0.7071068f, 0);
        fabric.transform.localScale = Vector3.one * 0.13f;
        var r = fabric.GetComponentInChildren<Renderer>();
        if (r != null) r.sharedMaterial = Load<Material>(MatDir + "Lab_LabCoat.mat");
        Created(fabric, undo);
        return true;
    }

    // cylinder extinguisher -> the lab's SM_Fire_extinguisher_01 on the wall bracket position; the FIRE sign stays
    static bool Extinguisher(Transform g, Scene scene, bool undo)
    {
        if (g == null || g.Find("SM_Fire_extinguisher_01") != null) return false;
        Remove(g, undo, "Bracket", "Body", "Valve", "Hose");
        var e = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(ExtinguisherFbx), scene);
        e.name = "SM_Fire_extinguisher_01";
        e.transform.SetParent(g, false);
        e.transform.localPosition = new Vector3(0.005128622f, 0.304f, 0.005796194f);
        e.transform.localRotation = new Quaternion(-0.52959305f, 0.4685418f, 0.46854156f, 0.52959293f);
        var r = e.GetComponentInChildren<Renderer>();
        if (r != null) r.sharedMaterial = Load<Material>(MatDir + "Lab_ExtRed.mat");
        Created(e, undo);
        return true;
    }

    // ---------- helpers

    static void Remove(Transform parent, bool undo, params string[] names)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var c = parent.GetChild(i).gameObject;
            if (System.Array.IndexOf(names, c.name) < 0) continue;
            if (undo) Undo.DestroyObjectImmediate(c); else Object.DestroyImmediate(c);
        }
    }

    static Transform MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool undo)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        Created(go, undo);
        return go.transform;
    }

    /// <summary>Moves t so its renderer's bottom-centre sits at the origin of <paramref name="on"/>.</summary>
    static void SitOn(Transform t, Transform on)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null) return;
        var b = r.bounds;
        var bottom = on.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
        t.localPosition -= bottom;
    }

    static Mesh MeshById(string path, long id)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Mesh m && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out _, out long lid) && lid == id) return m;
        Debug.LogError($"[ChemLab] Mesh {id} not found in {path}");
        return null;
    }

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) Debug.LogError("[ChemLab] Missing asset " + path);
        return a;
    }

    static void Created(GameObject go, bool undo)
    {
        if (undo) Undo.RegisterCreatedObjectUndo(go, "Lobby lab equipment");
    }
}
