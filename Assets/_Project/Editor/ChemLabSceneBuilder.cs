using System.Collections.Generic;
using System.Linq;
using ChemLab;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Menu: Chemistry Lab > Build Lab Scene.
/// Generates Assets/_Project/Scenes/ChemistryLab.unity: a 10 x 8 m lab built from
/// primitives, the XRI starter VR rig, the 10 hazards, decoy objects, grabbable glassware and the game manager.
/// Each hazard is a parent object (e.g. "H06_UnlabeledBottle") with one BoxCollider; replace its child
/// meshes with real models from ChemistryLab/Models whenever you have them.
/// </summary>
public static class ChemLabSceneBuilder
{
    const string Root = "Assets/_Project";
    const string LobbyPath = Root + "/Scenes/ChemLab_Lobby.unity";
    const string ScenePath = Root + "/Scenes/ChemistryLab.unity";
    const string EndPath = Root + "/Scenes/ChemLab_End.unity";
    const string RigPath = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string VignettePath = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Chemistry Lab/Build All Scenes")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll();
        EditorUtility.DisplayDialog("Chemistry Lab", "Built 3 scenes:\n" + LobbyPath + "\n" + ScenePath + "\n" + EndPath +
            "\n\nOpen ChemLab_Lobby and press Play (with Quest Link), or Build & Run to the Quest 3.", "OK");
    }

    /// <summary>For batch mode: -executeMethod ChemLabSceneBuilder.BuildBatch</summary>
    public static void BuildBatch() => BuildAll();

    static void BuildAll()
    {
        mats.Clear();
        // shared materials first (later calls with the same name reuse these)
        M("Glass", new Color(0.85f, 0.95f, 1f, 0.35f), transparent: true, smooth: 0.9f);
        M("LiquidBlue", new Color(0.2f, 0.5f, 0.95f, 0.75f), transparent: true);
        M("LiquidGreen", new Color(0.3f, 0.85f, 0.4f, 0.75f), transparent: true);
        M("LiquidAmber", new Color(0.95f, 0.65f, 0.2f, 0.75f), transparent: true);
        M("Metal", new Color(0.6f, 0.62f, 0.65f), metallic: 0.7f, smooth: 0.6f);
        M("NodeGlow", new Color(0f, 0.85f, 0.75f, 0.6f), transparent: true, emissive: true);

        BuildLab();
        BuildLobby();
        BuildEnd();
        EditorBuildSettings.scenes = new[] { LobbyPath, ScenePath, EndPath }.Select(x => new EditorBuildSettingsScene(x, true))
            .Concat(EditorBuildSettings.scenes.Where(s => s.path != LobbyPath && s.path != ScenePath && s.path != EndPath)).ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LobbyPath);
        Debug.Log("[ChemLab] Built Lobby, Lab and End scenes.");
    }

    // ===================================================================== scene 2: lab

    static void BuildLab()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var env = new GameObject("Environment").transform;
        // Use the teammate's textured lab when it is in the project, otherwise the primitive room.
        bool team = BuildTeammateEnvironment(env);
        if (team) BuildLighting(lightPositions: new[] { new Vector3(0, 2.6f, 1f), new Vector3(0, 2.6f, 5f), new Vector3(0, 2.6f, 9f), new Vector3(0, 2.6f, 13f) }, panels: false);
        else { BuildLighting(); BuildRoom(env); }
        var lab = new GameObject("Lab").transform;
        var decoys = new GameObject("Decoys (inspectable, not hazards)").transform;
        var hazards = new GameObject("Hazards (10)").transform;
        BuildFurniture(lab, team);
        if (!team) BuildPolish(lab);
        BuildDecoys(decoys);
        BuildHazards(hazards);
        if (team) RelayoutForTeammateLab(lab, decoys, hazards);

        // Movement: left stick walks, right stick turns (no teleport nodes in the lab)
        Vector3 startPos;
        Transform finishAnchor = new GameObject("FinishStationAnchor").transform;
        if (team)
        {
            startPos = new Vector3(0.9f, 0, 0.2f);
            finishAnchor.SetPositionAndRotation(new Vector3(1.65f, 1.45f, -1.5f), Quaternion.LookRotation(Vector3.back));
        }
        else
        {
            startPos = new Vector3(-1.5f, 0, -2.6f);
            finishAnchor.SetPositionAndRotation(new Vector3(-0.3f, 1.45f, -3.93f), Quaternion.LookRotation(Vector3.back));
        }

        var leftHand = Systems(startPos, 0f);

        var game = new GameObject("ChemLab Game");
        var ui = game.AddComponent<ChemLabUI>();
        ui.finishStationAnchor = finishAnchor;
        ui.leftHandAnchor = leftHand;
        game.AddComponent<ChemLabSession>();
        game.AddComponent<ChemLabControls>();
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    // ===================================================================== scene 1: lobby

    static void BuildLobby()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLighting(bright: true);
        var env = new GameObject("Environment").transform;
        SimpleRoom(env, 8f, 8f);

        var start = new Vector3(0, 0, -2.4f);
        // practice target: walk onto the glowing ring with the left stick
        var nodes = new GameObject("Walk Target").transform;
        var practice = Node(nodes, "Walk here", new Vector3(1.9f, 0, -1.2f), -30f, teleport: false);
        practice.localScale = new Vector3(1.2f, 1f, 1.2f);

        // coffee cup on a pedestal: practise selecting
        var ped = Group("Pedestal", env, new Vector3(-1.9f, 0, -1.3f));
        Cyl("Column", ped, new Vector3(0, 0.5f, 0), 0.45f, 1.0f, M("Pedestal", new Color(0.92f, 0.93f, 0.95f), smooth: 0.7f));
        Cyl("Top", ped, new Vector3(0, 1.01f, 0), 0.55f, 0.03f, M("PedestalTop", new Color(0.12f, 0.15f, 0.2f), smooth: 0.8f));
        var pr = Cyl("GlowRing", ped, new Vector3(0, 1.03f, 0), 0.5f, 0.005f, M("NodeGlow", Color.cyan));
        Object.DestroyImmediate(pr.GetComponent<Collider>());
        Label("Tag", ped, "SELECT ME", new Vector3(0, 1.45f, 0), Quaternion.Euler(0, 40f, 0), 0.8f, new Color(0f, 0.6f, 0.55f));
        var cup = Group("Coffee Cup (practice)", env, new Vector3(-1.9f, 1.025f, -1.3f));
        Cyl("Cup", cup, new Vector3(0, 0.065f, 0), 0.085f, 0.13f, M("CupPaper", new Color(0.95f, 0.92f, 0.85f)));
        Cyl("Sleeve", cup, new Vector3(0, 0.06f, 0), 0.088f, 0.05f, M("CupSleeve", new Color(0.45f, 0.3f, 0.18f)));
        Cyl("Lid", cup, new Vector3(0, 0.135f, 0), 0.09f, 0.012f, M("CupLid", new Color(0.2f, 0.15f, 0.12f)));
        MakeInspectable(cup.gameObject, ChemLabInspectable.Kind.Tutorial, null, "Coffee cup");
        var bc = cup.GetComponent<BoxCollider>();
        bc.size *= 2.2f;   // generous target for first-timers

        Systems(start, 0f);
        var game = new GameObject("ChemLab Lobby");
        var lobby = game.AddComponent<ChemLabLobby>();
        // ~1.25 m from the start spot (was ~1.6 m): text cap height 0.51-0.68 deg -> ~0.67-0.9 deg
        lobby.mainPanelAnchor = Anchor("MainPanelAnchor", new Vector3(-0.45f, 1.5f, -1.25f), start);
        lobby.controllerPanelAnchor = Anchor("ControllerPanelAnchor", new Vector3(0.75f, 1.45f, -1.4f), start);
        game.AddComponent<ChemLabControls>();
        EditorSceneManager.SaveScene(scene, LobbyPath);
    }

    // ===================================================================== scene 3: end

    static void BuildEnd()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLighting(bright: true);
        var env = new GameObject("Environment").transform;
        SimpleRoom(env, 8f, 8f);
        var start = new Vector3(0, 0, -2.4f);
        Systems(start, 0f);
        var game = new GameObject("ChemLab End Screen");
        var end = game.AddComponent<ChemLabEndScreen>();
        // ~1.35-1.45 m from the start spot (was ~2.0 m): report text ~0.55 deg -> ~0.8 deg
        end.resultsAnchor = Anchor("ResultsAnchor", new Vector3(-0.35f, 1.5f, -1.1f), start);
        end.leaderboardAnchor = Anchor("LeaderboardAnchor", new Vector3(1.05f, 1.5f, -1.45f), start);
        game.AddComponent<ChemLabControls>();
        EditorSceneManager.SaveScene(scene, EndPath);
    }

    // ===================================================================== shared scene pieces

    /// <summary>XR rig (left stick walks, right stick snap-turns or teleports), interaction manager and UI event system. Returns the left controller.</summary>
    static Transform Systems(Vector3 pos, float yaw)
    {
        new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
        new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        if (rigPrefab == null) { Debug.LogError("[ChemLab] XRI Starter Assets rig not found at " + RigPath); return null; }
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
        rig.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        SetBody(rig.GetComponent<CharacterController>());
        var all = rig.GetComponentsInChildren<Transform>(true);
        // Grab move and jump off.
        foreach (var name in new[] { "Grab Move", "Jump" })
            foreach (var t in all.Where(t => t.name == name)) t.gameObject.SetActive(false);

        // Starter rig input managers: left stick = smooth move; right stick = snap turn (45 deg, back = turn around)
        // and push forward to aim a teleport arc onto the floor, the comfort fallback for anyone who gets sick walking.
        // The manager itself disables teleport on the hand that has smooth motion.
        foreach (var m in rig.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "ControllerInputActionManager"))
        {
            var so = new SerializedObject(m);
            bool left = m.transform.name.Contains("Left") || (m.transform.parent != null && m.transform.parent.name.Contains("Left"));
            var sm = so.FindProperty("m_SmoothMotionEnabled"); if (sm != null) sm.boolValue = left;
            var st = so.FindProperty("m_SmoothTurnEnabled"); if (st != null) st.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // Comfortable speeds: left stick walks in all directions, right stick turns.
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null))
        {
            var typeName = mb.GetType().Name;
            if (typeName == "DynamicMoveProvider") typeName = "ContinuousMoveProvider";   // starter-rig subclass
            if (typeName != "ContinuousMoveProvider" && typeName != "ContinuousTurnProvider") continue;
            var so = new SerializedObject(mb);
            if (typeName == "ContinuousMoveProvider")
            {
                var speed = so.FindProperty("m_MoveSpeed"); if (speed != null) speed.floatValue = 1.5f;
                var strafe = so.FindProperty("m_EnableStrafe");
                if (strafe != null) strafe.boolValue = true;   // left stick: forward/back + left/right
            }
            else { var ts = so.FindProperty("m_TurnSpeed"); if (ts != null) ts.floatValue = 75f; }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // Tunneling vignette (XRI starter asset) narrows the view while the stick moves or smooth-turns you:
        // the standard comfort aid for continuous locomotion. Snap turn and teleport are instant and need none.
        var vignette = AssetDatabase.LoadAssetAtPath<GameObject>(VignettePath);
        var head = all.FirstOrDefault(t => t.name == "Main Camera");
        if (vignette != null && head != null)
        {
            var ctrl = ((GameObject)PrefabUtility.InstantiatePrefab(vignette, head)).GetComponent<TunnelingVignetteController>();
            foreach (var lp in rig.GetComponentsInChildren<LocomotionProvider>(true).Where(p => p is ContinuousMoveProvider || p is ContinuousTurnProvider))
                ctrl.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = lp, enabled = true });
            PrefabUtility.RecordPrefabInstancePropertyModifications(ctrl);
        }
        else Debug.LogWarning("[ChemLab] Tunneling vignette not added (prefab or Main Camera missing).");
        return all.FirstOrDefault(t => t.name == "Left Controller");
    }

    /// <summary>
    /// Player body: the starter rig's 0.1 m radius lets the head clip ~10 cm into walls and shelves, and its 0.5 m step
    /// lets you climb onto bins and crates. 0.25 m keeps the face out of geometry; 0.2 m only steps over cables and mats.
    /// </summary>
    public static void SetBody(CharacterController cc)
    {
        if (cc == null) return;
        cc.radius = 0.25f;
        cc.stepOffset = 0.2f;
    }

    /// <summary>A fixed teleport node: XRI TeleportationAnchor + glowing ring + floating name tag.</summary>
    static Transform Node(Transform parent, string name, Vector3 pos, float yaw, bool teleport = true)
    {
        var g = Group("Node_" + name, parent, pos);
        g.rotation = Quaternion.Euler(0, yaw, 0);
        var ring = Cyl("Ring", g, new Vector3(0, 0.006f, 0), 0.9f, 0.01f, M("NodeGlow", Color.cyan));
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        var inner = Cyl("Inner", g, new Vector3(0, 0.008f, 0), 0.74f, 0.01f, M("NodeInner", new Color(0.05f, 0.12f, 0.14f, 0.85f), transparent: true));
        Object.DestroyImmediate(inner.GetComponent<Collider>());
        var arrow = Box("Arrow", g, new Vector3(0, 0.012f, 0.22f), new Vector3(0.08f, 0.004f, 0.16f), M("NodeGlow", Color.cyan));
        Object.DestroyImmediate(arrow.GetComponent<Collider>());
        if (teleport)
        {
            var box = g.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.02f, 0);
            box.size = new Vector3(0.95f, 0.04f, 0.95f);
            var anchor = g.gameObject.AddComponent<TeleportationAnchor>();
            int teleMask = InteractionLayerMask.GetMask("Teleport");
            if (teleMask != 0) anchor.interactionLayers = teleMask;
        }
        var tag = new GameObject("Tag").transform;
        tag.SetParent(g, false);
        tag.localPosition = new Vector3(0, 0.45f, 0);
        Label("Text", tag, name.ToUpper(), Vector3.zero, Quaternion.identity, 0.7f, new Color(0f, 0.75f, 0.68f));
        var node = g.gameObject.AddComponent<ChemLabTeleportNode>();
        node.nodeName = name;
        node.glow = ring.GetComponent<Renderer>();
        node.nameTag = tag;
        return g;
    }

    /// <summary>Lets the right-stick teleport arc land anywhere on this floor (its collider).</summary>
    static void TeleportFloor(GameObject floor)
    {
        var area = floor.AddComponent<TeleportationArea>();
        int teleMask = InteractionLayerMask.GetMask("Teleport");
        if (teleMask != 0) area.interactionLayers = teleMask;
    }

    /// <summary>Anchor for a world-space panel, facing the player standing at 'viewer'.</summary>
    static Transform Anchor(string name, Vector3 pos, Vector3 viewer)
    {
        var t = new GameObject(name).transform;
        Vector3 d = pos - new Vector3(viewer.x, pos.y, viewer.z);
        t.SetPositionAndRotation(pos, Quaternion.LookRotation(d));
        return t;
    }

    /// <summary>Bright, clean onboarding room (lobby / end screen).</summary>
    static void SimpleRoom(Transform env, float w, float d)
    {
        var floorTex = FloorTexture();
        Box("Floor", env, new Vector3(0, -0.05f, 0), new Vector3(w, 0.1f, d), M("LobbyFloor", new Color(0.86f, 0.88f, 0.9f), smooth: 0.65f, tex: floorTex, tiling: new Vector2(w / 2f, d / 2f)));
        Box("Ceiling", env, new Vector3(0, 3.05f, 0), new Vector3(w, 0.1f, d), M("Ceiling", Color.white));
        var wall = M("LobbyWall", new Color(0.95f, 0.96f, 0.97f));
        Box("Wall_N", env, new Vector3(0, 1.5f, d / 2f + 0.05f), new Vector3(w, 3f, 0.1f), wall);
        Box("Wall_S", env, new Vector3(0, 1.5f, -d / 2f - 0.05f), new Vector3(w, 3f, 0.1f), wall);
        Box("Wall_W", env, new Vector3(-w / 2f - 0.05f, 1.5f, 0), new Vector3(0.1f, 3f, d), wall);
        Box("Wall_E", env, new Vector3(w / 2f + 0.05f, 1.5f, 0), new Vector3(0.1f, 3f, d), wall);
        var accent = M("AccentTeal", new Color(0f, 0.7f, 0.62f), emissive: true);
        Box("Accent_N", env, new Vector3(0, 0.6f, d / 2f - 0.005f), new Vector3(w, 0.04f, 0.02f), accent);
        Box("Accent_W", env, new Vector3(-w / 2f + 0.005f, 0.6f, 0), new Vector3(0.02f, 0.04f, d), accent);
        Box("Accent_E", env, new Vector3(w / 2f - 0.005f, 0.6f, 0), new Vector3(0.02f, 0.04f, d), accent);
        Box("LightWall", env, new Vector3(0, 1.8f, d / 2f - 0.01f), new Vector3(5f, 1.6f, 0.02f), M("WindowSky", new Color(0.55f, 0.75f, 0.95f), emissive: true));
        Label("Brand", env, "LAB SAFETY TRAINING", new Vector3(0, 2.85f, d / 2f - 0.03f), Quaternion.identity, 2.2f, new Color(0.1f, 0.3f, 0.35f));
        // keep floor + wall colliders so you can't walk through the walls
        foreach (var c in env.GetComponentsInChildren<Collider>()) if (c.name != "Floor" && !c.name.StartsWith("Wall_")) Object.DestroyImmediate(c);
        TeleportFloor(env.Find("Floor").gameObject);
    }

    /// <summary>Procedural vinyl floor tile (4 x 4 tiles with grout lines and soft speckle), saved as a PNG asset.</summary>
    static Texture2D FloorTexture()
    {
        string path = Root + "/Textures/Lab_FloorTile.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        const int n = 512;
        var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
        var rnd = new System.Random(7);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool grout = x % 128 < 2 || y % 128 < 2;
                float speck = (float)rnd.NextDouble() * 0.05f;
                float tileTint = ((x / 128 + y / 128) % 2 == 0) ? 0.0f : -0.02f;
                float v = grout ? 0.72f : 0.95f + tileTint - speck;
                tex.SetPixel(x, y, new Color(v, v * 1.005f, v * 1.015f));
            }
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ===================================================================== teammate lab environment

    const string TeammateLabPrefab = "Assets/_Project/LabEnvironment/Lab Environment.prefab";
    static Transform teammateEnv;

    /// <summary>
    /// Instantiates the teammate's textured lab (Assets/_Project/LabEnvironment/Lab Environment.prefab) and aligns it exactly like
    /// their LabEnvironmentBuilder does: floor levelled at y = 0, room running along +Z, entrance door behind the origin.
    /// Room: x = -2.86..2.86, z = -1.6 (entrance) .. 15.1; central island x = -0.8..0.8, z = 1.5..10.5; wall benches at x = +-2.6.
    /// </summary>
    static bool BuildTeammateEnvironment(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeammateLabPrefab);
        if (prefab == null) return false;
        var env = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        env.name = "Lab Environment (teammate)";
        env.transform.SetParent(parent, false);
        env.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Transform floor = env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Plane018") && t.GetComponent<MeshFilter>() != null);
        if (floor == null) { Debug.LogWarning("[ChemLab] Teammate lab floor (Plane018) not found; using it unaligned."); teammateEnv = env.transform; return true; }
        var b = floor.GetComponent<MeshFilter>().sharedMesh.bounds;
        var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
        var world = Enumerable.Range(0, 3).Select(i => floor.TransformVector(axes[i] * b.size[i])).ToArray();
        var order = Enumerable.Range(0, 3).OrderBy(i => world[i].magnitude).ToArray();
        var center = floor.TransformPoint(b.center);
        var up = floor.TransformDirection(axes[order[0]]).normalized;
        if (up.y < 0) up = -up;
        float length = world[order[2]].magnitude;
        var fwd = world[order[2]];
        fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized;
        var door = env.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.StartsWith("Object1111191742"));
        if (door != null && Vector3.Dot(door.bounds.center - center, fwd) > 0) fwd = -fwd;
        var start = center + fwd * (-length * 0.5f + 1.6f);
        var q = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
        env.transform.SetPositionAndRotation(q * -start, q);
        teammateEnv = env.transform;
        if (floor.GetComponent<Collider>() != null) TeleportFloor(floor.gameObject);
        else Debug.LogWarning("[ChemLab] Lab floor has no collider: teleport has nowhere to land.");
        // Glass (fume-hood sashes, cabinet doors) should not stop the controller ray.
        int glass = 0;
        foreach (var r in env.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial;
            bool transparent = m != null && (m.renderQueue >= 2900 || (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f));
            if (!transparent) continue;
            foreach (var c in r.GetComponents<Collider>()) { Object.DestroyImmediate(c); glass++; }
        }
        Debug.Log("[ChemLab] Teammate lab aligned; removed " + glass + " glass colliders.");
        // The entrance trash bin is a hollow mesh: the player's body drops inside it. Solid hull instead.
        foreach (var c in env.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name.StartsWith("I_Trash"))) c.convex = true;
        Physics.SyncTransforms();

        // keep-clear zone in front of the entrance (the H01 cart sits on it)
        var tape = M("SafetyYellow", new Color(0.98f, 0.8f, 0.1f));
        Box("ExitZone_F", parent, new Vector3(0f, 0.003f, -0.6f), new Vector3(1.7f, 0.004f, 0.08f), tape);
        Box("ExitZone_L", parent, new Vector3(-0.85f, 0.003f, -1.1f), new Vector3(0.08f, 0.004f, 1.0f), tape);
        Box("ExitZone_R", parent, new Vector3(0.85f, 0.003f, -1.1f), new Vector3(0.08f, 0.004f, 1.0f), tape);
        Label("KeepClear", parent, "KEEP CLEAR", new Vector3(0f, 0.007f, -0.8f), Quaternion.Euler(90, 0, 0), 1.0f, new Color(0.85f, 0.65f, 0.05f));
        Label("Sign_Storage", parent, "CHEMICAL STORAGE", new Vector3(0f, 2.45f, 12.6f), Quaternion.LookRotation(Vector3.forward), 0.9f, new Color(0.15f, 0.3f, 0.5f));
        return true;
    }

    /// <summary>Height of the teammate's lab surface (bench, island, floor) under (x, z), ignoring our own objects.</summary>
    static float SurfaceY(float x, float z)
    {
        var hits = Physics.RaycastAll(new Vector3(x, 2.2f, z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = 0f; float bestDist = float.MaxValue;
        foreach (var h in hits)
            if (teammateEnv != null && h.collider.transform.IsChildOf(teammateEnv) && h.distance < bestDist) { bestDist = h.distance; best = h.point.y; }
        return best;
    }

    /// <summary>Moves our hazards, decoys and storage shelf from the primitive-room layout into the teammate's lab.</summary>
    static void RelayoutForTeammateLab(params Transform[] roots)
    {
        Physics.SyncTransforms();
        // name -> (x, z, yaw, snapToSurface)
        var layout = new Dictionary<string, (float x, float z, float yaw, bool snap)>
        {
            // entrance
            { "H01_BlockedExit",       (0.0f, -1.1f,   0f, false) },
            { "H03_StackedBoxes",      (-1.75f, 0.9f,  0f, false) },
            { "CoatRack",              (1.7f, -1.15f,  0f, false) },
            { "FireExtinguisher",      (2.2f, 0.75f,   0f, false) },
            // central island
            { "H02_FoodAndDrink",      (-0.4f, 2.7f,   0f, true) },
            { "TubeRack",              (0.35f, 3.2f,   0f, true) },
            { "Pipettes",              (-0.4f, 3.75f,  0f, true) },
            { "H06_UnlabeledBottle",   (0.35f, 4.6f,   0f, true) },
            { "LabeledBottle_NaCl",    (0.4f, 6.8f,    0f, true) },
            { "H07_PPEAndHeadphones",  (-0.15f, 8.0f,  0f, true) },
            { "HotPlate",              (0.45f, 8.35f,  0f, true) },
            { "LabNotebook",           (-0.3f, 9.6f,   0f, true) },
            // left wall bench (work area)
            { "H04_LabCoatOnChair",    (-1.95f, 3.4f, -90f, false) },
            { "Laptop",                (-2.6f, 3.0f,  90f, true) },
            { "H09_FirstAidKit",       (-2.15f, 15.0f,  0f, false) },   // far-left corner wall
            // right wall (waste + equipment)
            { "H05_WasteSeparation",   (2.15f, 7.2f, 180f, false) },
            { "LabWasteBin",           (2.15f, 7.85f, 180f, false) },
            { "Balance",               (2.6f, 3.0f,    0f, true) },
            { "Centrifuge",            (2.45f, 9.35f,  0f, true) },
            { "H08_DamagedCable",      (2.47f, 9.8f,   0f, true) },
            // chemical storage (freestanding rack near the far end)
            { "CorrosivesCabinet",     (1.25f, 12.75f, 0f, false) },
        };
        // the storage rack and everything sitting on it move together, keeping their offsets
        var shelfOld = new Vector3(2.5f, 0, 3.75f);
        var shelfNew = new Vector3(0f, 0, 12.8f);
        var shelfSet = new HashSet<string> { "ChemicalShelf", "H10_ChemicalStorage", "LowerShelfBottles", "MidShelfGlassware" };

        foreach (var root in roots)
            foreach (Transform t in root.Cast<Transform>().ToArray())
            {
                if (shelfSet.Contains(t.name)) { t.position = shelfNew + (t.position - shelfOld); continue; }
                if (!layout.TryGetValue(t.name, out var p)) continue;
                float y = p.snap ? SurfaceY(p.x, p.z) : 0f;
                t.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0, p.yaw, 0));
            }

        // our primitive-room decoys that have no place in the teammate's lab
        foreach (var root in roots)
            foreach (Transform t in root.Cast<Transform>().ToArray())
                if (t.name == "SafetySupervisor" || t.name == "EyeWash" || t.name == "Sink" || t.name == "FumeHood" || t.name == "WallCabinets" || t.name == "Poster" || t.name.StartsWith("GasTap"))
                    Object.DestroyImmediate(t.gameObject);
    }

    // ===================================================================== lighting / room

    static void BuildLighting(bool bright = false, Vector3[] lightPositions = null, bool panels = true)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = bright ? new Color(0.78f, 0.82f, 0.86f) : new Color(0.62f, 0.66f, 0.7f);
        RenderSettings.ambientEquatorColor = bright ? new Color(0.66f, 0.7f, 0.72f) : new Color(0.5f, 0.53f, 0.56f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.33f, 0.35f);
        RenderSettings.skybox = null;

        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = bright ? 0.8f : 0.6f;
        sun.color = new Color(1f, 0.97f, 0.92f);
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(60f, 30f, 0f);

        var lights = new GameObject("Ceiling Lights").transform;
        foreach (var p in lightPositions ?? new[] { new Vector3(-2.5f, 2.85f, -1.8f), new Vector3(2.5f, 2.85f, -1.8f), new Vector3(-2.5f, 2.85f, 2f), new Vector3(2.5f, 2.85f, 2f) })
        {
            if (panels)
            {
                var panel = Box("LightPanel", lights, p + Vector3.up * 0.1f, new Vector3(1.2f, 0.04f, 0.4f), M("LightPanel", Color.white, emissive: true));
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                var frame = Box("LightFrame", lights, p + Vector3.up * 0.11f, new Vector3(1.26f, 0.03f, 0.46f), M("Metal", Color.grey));
                Object.DestroyImmediate(frame.GetComponent<Collider>());
            }
            var l = new GameObject("Point Light").AddComponent<Light>();
            l.transform.SetParent(lights, false);
            l.transform.position = p;
            l.type = LightType.Point;
            l.range = 7f;
            l.intensity = bright ? 1.3f : 1.6f;
            l.color = new Color(1f, 0.98f, 0.95f);
            l.shadows = LightShadows.None;
        }
    }

    static void BuildRoom(Transform env)
    {
        var wall = M("Wall", new Color(0.93f, 0.94f, 0.94f));
        // floor: vinyl tiles (walked on with the left stick)
        Box("Floor", env, new Vector3(0, -0.05f, 0), new Vector3(10f, 0.1f, 8f),
            M("Floor", new Color(0.78f, 0.82f, 0.86f), smooth: 0.6f, tex: FloorTexture(), tiling: new Vector2(5f, 4f)));

        Box("Ceiling", env, new Vector3(0, 3.05f, 0), new Vector3(10f, 0.1f, 8f), M("Ceiling", Color.white));
        Box("Wall_North", env, new Vector3(0, 1.5f, 4.05f), new Vector3(10.2f, 3f, 0.1f), wall);
        Box("Wall_West", env, new Vector3(-5.05f, 1.5f, 0), new Vector3(0.1f, 3f, 8f), wall);
        Box("Wall_East", env, new Vector3(5.05f, 1.5f, 0), new Vector3(0.1f, 3f, 8f), wall);
        // South wall with emergency exit door gap at x = -3.2 (1.0 m wide, 2.1 m high)
        Box("Wall_South_A", env, new Vector3(-4.35f, 1.5f, -4.05f), new Vector3(1.4f, 3f, 0.1f), wall);
        Box("Wall_South_B", env, new Vector3(1.15f, 1.5f, -4.05f), new Vector3(7.7f, 3f, 0.1f), wall);
        Box("Wall_South_Top", env, new Vector3(-3.2f, 2.55f, -4.05f), new Vector3(1.0f, 0.9f, 0.1f), wall);

        // wainscot panels + trim (lower 1 m of the walls) for depth and a finished look
        var wains = M("Wainscot", new Color(0.72f, 0.8f, 0.86f), smooth: 0.5f);
        var trim = M("Trim", new Color(0.2f, 0.42f, 0.62f));
        Box("Wains_N", env, new Vector3(0, 0.5f, 3.995f), new Vector3(10f, 1f, 0.01f), wains);
        Box("Wains_W", env, new Vector3(-4.995f, 0.5f, 0), new Vector3(0.01f, 1f, 8f), wains);
        Box("Wains_E", env, new Vector3(4.995f, 0.5f, 0), new Vector3(0.01f, 1f, 8f), wains);
        Box("Wains_SA", env, new Vector3(-4.35f, 0.5f, -3.995f), new Vector3(1.3f, 1f, 0.01f), wains);
        Box("Wains_SB", env, new Vector3(1.15f, 0.5f, -3.995f), new Vector3(7.7f, 1f, 0.01f), wains);
        Box("Trim_N", env, new Vector3(0, 1.0f, 3.99f), new Vector3(10f, 0.04f, 0.02f), trim);
        Box("Trim_W", env, new Vector3(-4.99f, 1.0f, 0), new Vector3(0.02f, 0.04f, 8f), trim);
        Box("Trim_E", env, new Vector3(4.99f, 1.0f, 0), new Vector3(0.02f, 0.04f, 8f), trim);
        Box("Base_N", env, new Vector3(0, 0.04f, 3.99f), new Vector3(10f, 0.08f, 0.02f), trim);
        Box("Base_W", env, new Vector3(-4.99f, 0.04f, 0), new Vector3(0.02f, 0.08f, 8f), trim);
        Box("Base_E", env, new Vector3(4.99f, 0.04f, 0), new Vector3(0.02f, 0.08f, 8f), trim);

        // windows on the east wall (emissive "daylight")
        var sky = M("WindowSky", new Color(0.55f, 0.75f, 0.95f), emissive: true);
        var frameMat = M("WindowFrame", new Color(0.25f, 0.28f, 0.32f), metallic: 0.5f);
        foreach (var z in new[] { -2.4f, 3.0f })
        {
            Box("Window", env, new Vector3(4.99f, 1.85f, z), new Vector3(0.02f, 1.1f, 1.3f), sky);
            Box("WinFrameT", env, new Vector3(4.98f, 2.42f, z), new Vector3(0.04f, 0.05f, 1.38f), frameMat);
            Box("WinFrameB", env, new Vector3(4.98f, 1.28f, z), new Vector3(0.04f, 0.05f, 1.38f), frameMat);
            Box("WinMullion", env, new Vector3(4.98f, 1.85f, z), new Vector3(0.04f, 1.1f, 0.04f), frameMat);
        }

        // keep-clear floor zone in front of the exit (yellow/black safety tape)
        var tape = M("SafetyYellow", new Color(0.98f, 0.8f, 0.1f));
        Box("ExitZone_F", env, new Vector3(-3.2f, 0.002f, -3.0f), new Vector3(1.6f, 0.004f, 0.08f), tape);
        Box("ExitZone_L", env, new Vector3(-4.0f, 0.002f, -3.5f), new Vector3(0.08f, 0.004f, 1.0f), tape);
        Box("ExitZone_R", env, new Vector3(-2.4f, 0.002f, -3.5f), new Vector3(0.08f, 0.004f, 1.0f), tape);
        Label("KeepClear", env, "KEEP CLEAR", new Vector3(-3.2f, 0.006f, -3.25f), Quaternion.Euler(90, 0, 0), 1.2f, new Color(0.85f, 0.65f, 0.05f));

        // Emergency exit door + sign
        var door = Group("EmergencyExitDoor", env, new Vector3(-3.2f, 0, -4.02f));
        Box("Frame_L", door, new Vector3(-0.53f, 1.07f, 0), new Vector3(0.06f, 2.14f, 0.14f), M("Metal", Color.grey));
        Box("Frame_R", door, new Vector3(0.53f, 1.07f, 0), new Vector3(0.06f, 2.14f, 0.14f), M("Metal", Color.grey));
        Box("Frame_T", door, new Vector3(0, 2.13f, 0), new Vector3(1.12f, 0.06f, 0.14f), M("Metal", Color.grey));
        Box("Panel", door, new Vector3(0, 1.05f, 0.02f), new Vector3(1.0f, 2.1f, 0.05f), M("Door", new Color(0.3f, 0.5f, 0.42f)));
        Box("Window", door, new Vector3(0, 1.6f, 0.05f), new Vector3(0.3f, 0.5f, 0.01f), sky);
        Box("PushBar", door, new Vector3(0, 1.0f, 0.08f), new Vector3(0.8f, 0.06f, 0.06f), M("Metal", Color.grey));
        Box("ExitSign", door, new Vector3(0, 2.35f, 0.04f), new Vector3(0.7f, 0.22f, 0.04f), M("ExitGreen", new Color(0.05f, 0.7f, 0.3f), emissive: true));
        Label("ExitText", door, "EMERGENCY EXIT", new Vector3(0, 2.35f, 0.065f), Quaternion.LookRotation(Vector3.back), 0.6f, Color.white);

        // Area signs (help orientation, not hazard hints)
        Label("Sign_Storage", env, "CHEMICAL STORAGE", new Vector3(2.6f, 2.6f, 3.99f), Quaternion.identity, 1.0f, new Color(0.15f, 0.3f, 0.5f));
        Label("Sign_Safety", env, "SAFETY STATION", new Vector3(-1.5f, 2.6f, 3.99f), Quaternion.identity, 1.0f, new Color(0.1f, 0.5f, 0.25f));
        Label("Sign_Equipment", env, "EQUIPMENT", new Vector3(4.99f, 2.7f, 1f), Quaternion.LookRotation(Vector3.right), 1.0f, new Color(0.15f, 0.3f, 0.5f));
        Label("Sign_Work", env, "WORK AREA", new Vector3(-4.99f, 2.7f, 1f), Quaternion.LookRotation(Vector3.left), 1.0f, new Color(0.15f, 0.3f, 0.5f));
        Label("Sign_BSL", env, "BSL-1 LABORATORY", new Vector3(1.6f, 2.6f, -3.99f), Quaternion.LookRotation(Vector3.back), 1.0f, new Color(0.15f, 0.3f, 0.5f));
    }

    // ===================================================================== furniture

    static void BuildFurniture(Transform lab, bool teammateLab = false)
    {
        if (!teammateLab) BuildBenches(lab);
        // reagent shelf on the island
        // Chemical storage shelf unit (north wall, x = 2.5)
        BuildChemShelf(lab);
        BuildSupervisor(lab);
    }

    static void BuildBenches(Transform lab)
    {
        Bench("MainBench", lab, new Vector3(0, 0, 0.5f), new Vector2(4.0f, 1.2f));
        var shelfMat = M("Metal", Color.grey);
        Box("IslandShelf_PostL", lab, new Vector3(-1.9f, 1.25f, 0.5f), new Vector3(0.04f, 0.7f, 0.04f), shelfMat);
        Box("IslandShelf_PostR", lab, new Vector3(1.9f, 1.25f, 0.5f), new Vector3(0.04f, 0.7f, 0.04f), shelfMat);
        Box("IslandShelf_Board", lab, new Vector3(0, 1.45f, 0.5f), new Vector3(3.9f, 0.03f, 0.3f), M("BenchTop", new Color(0.2f, 0.22f, 0.25f)));

        Bench("WorkBench_West", lab, new Vector3(-4.55f, 0, 1.0f), new Vector2(0.8f, 3.0f));
        Bench("EquipmentBench_East", lab, new Vector3(4.55f, 0, 1.0f), new Vector2(0.8f, 3.0f));
    }

    static void BuildChemShelf(Transform lab)
    {
        var shelf = Group("ChemicalShelf", lab, new Vector3(2.5f, 0, 3.75f));
        var wood = M("ShelfWhite", new Color(0.88f, 0.88f, 0.86f));
        Box("Side_L", shelf, new Vector3(-0.72f, 1.1f, 0), new Vector3(0.03f, 2.2f, 0.4f), wood);
        Box("Side_R", shelf, new Vector3(0.72f, 1.1f, 0), new Vector3(0.03f, 2.2f, 0.4f), wood);
        Box("Back", shelf, new Vector3(0, 1.1f, 0.19f), new Vector3(1.44f, 2.2f, 0.02f), wood);
        foreach (var y in new[] { 0.05f, 0.5f, 1.0f, 1.5f, 1.95f })
            Box("Shelf_" + y, shelf, new Vector3(0, y, 0), new Vector3(1.42f, 0.03f, 0.4f), wood);
    }

    static void BuildSupervisor(Transform lab)
    {
        // supervisor figure near the start (non-interactive)
        var sup = Group("SafetySupervisor", lab, new Vector3(-4.3f, 0, -1.3f));
        sup.rotation = Quaternion.Euler(0, 120f, 0);
        Cyl("Body", sup, new Vector3(0, 0.75f, 0), 0.38f, 1.5f, M("LabCoat", new Color(0.96f, 0.96f, 0.96f)));
        Sphere("Head", sup, new Vector3(0, 1.62f, 0), 0.24f, M("Skin", new Color(0.85f, 0.68f, 0.55f)));
        Box("Goggles", sup, new Vector3(0, 1.65f, 0.11f), new Vector3(0.2f, 0.05f, 0.04f), M("GogglesBlue", new Color(0.4f, 0.7f, 0.95f)));
        Box("Badge", sup, new Vector3(0.1f, 1.2f, 0.19f), new Vector3(0.08f, 0.05f, 0.01f), M("ExitGreen", Color.green));
    }

    static void Bench(string name, Transform parent, Vector3 pos, Vector2 size)
    {
        var g = Group(name, parent, pos);
        Box("Plinth", g, new Vector3(0, 0.05f, 0), new Vector3(size.x - 0.06f, 0.1f, size.y - 0.06f), M("Plinth", new Color(0.25f, 0.27f, 0.3f)));
        Box("Cabinet", g, new Vector3(0, 0.475f, 0), new Vector3(size.x, 0.75f, size.y), M("BenchBody", new Color(0.86f, 0.89f, 0.91f)));
        Box("Top", g, new Vector3(0, 0.875f, 0), new Vector3(size.x + 0.04f, 0.05f, size.y + 0.04f), M("BenchTop", new Color(0.2f, 0.22f, 0.25f), smooth: 0.6f));
        // drawer / door seams on the long sides
        var seam = M("Seam", new Color(0.55f, 0.6f, 0.65f));
        bool alongX = size.x >= size.y;
        float len = alongX ? size.x : size.y, depth = alongX ? size.y : size.x;
        int doors = Mathf.Max(2, Mathf.RoundToInt(len / 0.6f));
        for (int i = 1; i < doors; i++)
        {
            float o = -len / 2f + i * len / doors;
            foreach (float side in new[] { -1f, 1f })
            {
                var p = alongX ? new Vector3(o, 0.475f, side * (depth / 2f + 0.003f)) : new Vector3(side * (depth / 2f + 0.003f), 0.475f, o);
                var s = alongX ? new Vector3(0.012f, 0.7f, 0.006f) : new Vector3(0.006f, 0.7f, 0.012f);
                var sm = Box("Seam", g, p, s, seam);
                Object.DestroyImmediate(sm.GetComponent<Collider>());
            }
        }
    }

    /// <summary>Extra set dressing: fume hood, wall cabinets, sink, gas taps. Fume hood and sink are inspectable decoys.</summary>
    static void BuildPolish(Transform lab)
    {
        // Fume hood on the north wall (west side)
        var fh = Group("FumeHood", lab, new Vector3(-3.6f, 0, 3.6f));
        var body = M("HoodBody", new Color(0.9f, 0.92f, 0.93f));
        Box("Base", fh, new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 0.75f), body);
        Box("Work", fh, new Vector3(0, 0.92f, 0), new Vector3(1.5f, 0.04f, 0.75f), M("BenchTop", Color.black));
        Box("Side_L", fh, new Vector3(-0.73f, 1.55f, 0), new Vector3(0.04f, 1.25f, 0.75f), body);
        Box("Side_R", fh, new Vector3(0.73f, 1.55f, 0), new Vector3(0.04f, 1.25f, 0.75f), body);
        Box("Top", fh, new Vector3(0, 2.25f, 0), new Vector3(1.5f, 0.2f, 0.75f), body);
        Box("Back", fh, new Vector3(0, 1.55f, 0.35f), new Vector3(1.45f, 1.25f, 0.04f), M("HoodInner", new Color(0.8f, 0.84f, 0.86f)));
        Box("Sash", fh, new Vector3(0, 1.45f, -0.36f), new Vector3(1.42f, 0.9f, 0.015f), M("Glass", Color.white));
        Box("SashHandle", fh, new Vector3(0, 1.0f, -0.38f), new Vector3(1.2f, 0.03f, 0.03f), M("Metal", Color.grey));
        Box("HoodLight", fh, new Vector3(0, 2.14f, 0), new Vector3(1.3f, 0.02f, 0.3f), M("LightPanel", Color.white));
        Box("Display", fh, new Vector3(0.6f, 2.25f, -0.38f), new Vector3(0.2f, 0.08f, 0.01f), M("ScreenGreen", new Color(0.2f, 0.85f, 0.45f), emissive: true));
        Label("Label", fh, "FUME HOOD", new Vector3(-0.3f, 2.25f, -0.38f), Quaternion.identity, 0.5f, new Color(0.2f, 0.3f, 0.4f));
        MakeInspectable(fh.gameObject, ChemLabInspectable.Kind.Decoy, null, "Fume hood");

        // Wall cabinets above the west work bench
        var wc = Group("WallCabinets", lab, new Vector3(-4.82f, 0, 1.0f));
        for (int i = 0; i < 3; i++)
        {
            float z = -1.0f + i * 1.0f;
            Box("Cabinet", wc, new Vector3(0, 1.95f, z), new Vector3(0.35f, 0.7f, 0.96f), M("BenchBody", Color.white));
            Box("Handle", wc, new Vector3(0.18f, 1.7f, z + 0.3f), new Vector3(0.02f, 0.12f, 0.02f), M("Metal", Color.grey));
        }

        // Sink on the west bench
        var sink = Group("Sink", lab, new Vector3(-4.5f, 0.9f, -0.15f));
        Box("Basin", sink, new Vector3(0, 0.005f, 0), new Vector3(0.45f, 0.01f, 0.5f), M("Steel", new Color(0.7f, 0.72f, 0.75f), metallic: 0.8f, smooth: 0.8f));
        Box("Rim", sink, new Vector3(0, 0.015f, 0), new Vector3(0.5f, 0.01f, 0.55f), M("Steel", Color.grey));
        Cyl("TapPost", sink, new Vector3(-0.2f, 0.15f, 0), 0.03f, 0.3f, M("Steel", Color.grey));
        var spout = Box("Spout", sink, new Vector3(-0.12f, 0.3f, 0), new Vector3(0.18f, 0.03f, 0.03f), M("Steel", Color.grey));
        MakeInspectable(sink.gameObject, ChemLabInspectable.Kind.Decoy, null, "Sink");

        // Gas taps along the island
        for (int i = 0; i < 4; i++)
        {
            var tap = Cyl("GasTap", lab, new Vector3(-1.5f + i * 1.0f, 0.95f, 0.5f), 0.04f, 0.1f, M("GasYellow", new Color(0.95f, 0.75f, 0.15f)));
            Object.DestroyImmediate(tap.GetComponent<Collider>());
        }

        // Safety poster near the safety station
        var poster = Group("Poster", lab, new Vector3(-0.2f, 1.6f, 3.99f));
        Box("Board", poster, Vector3.zero, new Vector3(0.6f, 0.8f, 0.01f), M("Paper", Color.white));
        Box("Header", poster, new Vector3(0, 0.32f, -0.006f), new Vector3(0.6f, 0.16f, 0.004f), M("Trim", Color.blue));
        Label("Title", poster, "SAFETY FIRST", new Vector3(0, 0.32f, -0.01f), Quaternion.identity, 0.45f, Color.white);
        Label("Body", poster, "PPE on\nNo food or drink\nLabel everything\nReport hazards", new Vector3(0, -0.05f, -0.01f), Quaternion.identity, 0.32f, new Color(0.15f, 0.2f, 0.3f));
    }

    // ===================================================================== decoys

    static void BuildDecoys(Transform p)
    {
        // Main bench: tube rack + pipettes
        var rack = Group("TubeRack", p, new Vector3(-1.55f, 0.9f, 0.3f));
        Box("Rack", rack, new Vector3(0, 0.03f, 0), new Vector3(0.28f, 0.06f, 0.1f), M("RackWhite", Color.white));
        for (int i = 0; i < 5; i++)
            Cyl("Tube", rack, new Vector3(-0.1f + i * 0.05f, 0.08f, 0), 0.018f, 0.12f, M(i % 2 == 0 ? "LiquidBlue" : "LiquidAmber", Color.blue));
        MakeInspectable(rack.gameObject, ChemLabInspectable.Kind.Decoy, null, "Sample tube rack");

        var pip = Group("Pipettes", p, new Vector3(-1.45f, 0.9f, 0.75f));
        Box("Stand", pip, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.24f, 0.06f), M("RackWhite", Color.white));
        for (int i = 0; i < 3; i++)
            Cyl("Pipette", pip, new Vector3(-0.04f + i * 0.04f, 0.2f, 0.04f), 0.02f, 0.22f, M("PipetteGrey", new Color(0.75f, 0.78f, 0.8f)));
        MakeInspectable(pip.gameObject, ChemLabInspectable.Kind.Decoy, null, "Pipettes");

        // Labeled reagent bottle (correct practice)
        var lab1 = Group("LabeledBottle_NaCl", p, new Vector3(0.75f, 0.9f, 0.25f));
        Cyl("Bottle", lab1, new Vector3(0, 0.1f, 0), 0.09f, 0.2f, M("AmberGlass", new Color(0.55f, 0.3f, 0.1f)));
        Cyl("Cap", lab1, new Vector3(0, 0.215f, 0), 0.04f, 0.03f, M("Black", new Color(0.08f, 0.08f, 0.08f)));
        Box("Label", lab1, new Vector3(0, 0.1f, -0.046f), new Vector3(0.07f, 0.07f, 0.004f), M("Paper", Color.white));
        Label("Text", lab1, "NaCl\n0.9%", new Vector3(0, 0.1f, -0.049f), Quaternion.identity, 0.12f, Color.black);
        MakeInspectable(lab1.gameObject, ChemLabInspectable.Kind.Decoy, null, "Labeled saline bottle");

        // Hot plate (active equipment next to the PPE hazard)
        var hp = Group("HotPlate", p, new Vector3(1.75f, 0.9f, 0.35f));
        Box("Base", hp, new Vector3(0, 0.04f, 0), new Vector3(0.22f, 0.08f, 0.26f), M("RackWhite", Color.white));
        Cyl("Plate", hp, new Vector3(0, 0.085f, 0.02f), 0.17f, 0.01f, M("HotRed", new Color(0.9f, 0.2f, 0.1f), emissive: true));
        Box("Knob", hp, new Vector3(0, 0.04f, -0.135f), new Vector3(0.04f, 0.03f, 0.02f), M("Black", Color.black));
        MakeInspectable(hp.gameObject, ChemLabInspectable.Kind.Decoy, null, "Hot plate");

        // Notebook on main bench
        var nb = Group("LabNotebook", p, new Vector3(-0.4f, 0.9f, 0.8f));
        Box("Book", nb, new Vector3(0, 0.01f, 0), new Vector3(0.21f, 0.02f, 0.29f), M("NotebookBlue", new Color(0.15f, 0.25f, 0.55f)));
        MakeInspectable(nb.gameObject, ChemLabInspectable.Kind.Decoy, null, "Lab notebook");

        // Coat rack by the entrance (the correct place for lab coats)
        var cr = Group("CoatRack", p, new Vector3(-4.6f, 0, -2.6f));
        Cyl("Pole", cr, new Vector3(0, 0.9f, 0), 0.04f, 1.8f, M("Metal", Color.grey));
        Cyl("Base", cr, new Vector3(0, 0.02f, 0), 0.4f, 0.04f, M("Metal", Color.grey));
        Box("Hooks", cr, new Vector3(0, 1.75f, 0), new Vector3(0.35f, 0.03f, 0.03f), M("Metal", Color.grey));
        Box("CoatHung", cr, new Vector3(0.12f, 1.35f, 0), new Vector3(0.3f, 0.75f, 0.08f), M("LabCoat", Color.white));
        MakeInspectable(cr.gameObject, ChemLabInspectable.Kind.Decoy, null, "Coat rack");

        // Proper lab waste bin (yellow, lidded)
        var lw = Group("LabWasteBin", p, new Vector3(-4.6f, 0, 3.55f));
        Cyl("Bin", lw, new Vector3(0, 0.27f, 0), 0.38f, 0.54f, M("WasteYellow", new Color(0.95f, 0.78f, 0.1f)));
        Cyl("Lid", lw, new Vector3(0, 0.555f, 0), 0.4f, 0.03f, M("WasteYellow", Color.yellow));
        Label("Text", lw, "LAB WASTE", new Vector3(0.2f, 0.32f, 0), Quaternion.LookRotation(Vector3.left), 0.35f, Color.black);
        MakeInspectable(lw.gameObject, ChemLabInspectable.Kind.Decoy, null, "Lab waste bin");

        // West bench: microscope-ish + laptop
        var lap = Group("Laptop", p, new Vector3(-4.5f, 0.9f, 1.4f));
        lap.rotation = Quaternion.Euler(0, 90f, 0);
        Box("Base", lap, new Vector3(0, 0.01f, 0), new Vector3(0.32f, 0.02f, 0.22f), M("Graphite", new Color(0.25f, 0.27f, 0.3f)));
        var scr = Box("Screen", lap, new Vector3(0, 0.12f, 0.11f), new Vector3(0.32f, 0.22f, 0.01f), M("Graphite", Color.grey));
        scr.transform.localRotation = Quaternion.Euler(-15f, 0, 0);
        MakeInspectable(lap.gameObject, ChemLabInspectable.Kind.Decoy, null, "Laptop");

        // Equipment bench: centrifuge (H08 hides behind it) + balance
        var cf = Group("Centrifuge", p, new Vector3(4.5f, 0.9f, 1.0f));
        Box("Body", cf, new Vector3(0, 0.15f, 0), new Vector3(0.45f, 0.3f, 0.45f), M("DeviceWhite", new Color(0.9f, 0.9f, 0.92f)));
        Cyl("Lid", cf, new Vector3(0, 0.305f, 0), 0.36f, 0.02f, M("Graphite", Color.grey));
        Box("Display", cf, new Vector3(-0.226f, 0.18f, 0), new Vector3(0.005f, 0.06f, 0.14f), M("ScreenBlue", new Color(0.2f, 0.6f, 0.9f), emissive: true));
        MakeInspectable(cf.gameObject, ChemLabInspectable.Kind.Decoy, null, "Centrifuge");

        var bal = Group("Balance", p, new Vector3(4.5f, 0.9f, 0.0f));
        Box("Body", bal, new Vector3(0, 0.04f, 0), new Vector3(0.3f, 0.08f, 0.35f), M("DeviceWhite", Color.white));
        Cyl("Pan", bal, new Vector3(0, 0.09f, 0.03f), 0.16f, 0.01f, M("Metal", Color.grey));
        MakeInspectable(bal.gameObject, ChemLabInspectable.Kind.Decoy, null, "Balance");

        // Fire extinguisher (east wall near entrance)
        var fe = Group("FireExtinguisher", p, new Vector3(4.85f, 0, -2.6f));
        Cyl("Tank", fe, new Vector3(0, 0.55f, 0), 0.16f, 0.5f, M("ExtRed", new Color(0.8f, 0.08f, 0.08f)));
        Cyl("Head", fe, new Vector3(0, 0.84f, 0), 0.06f, 0.08f, M("Black", Color.black));
        Box("Bracket", fe, new Vector3(0.09f, 0.55f, 0), new Vector3(0.02f, 0.3f, 0.1f), M("Metal", Color.grey));
        Label("Sign", fe, "FIRE\nEXTINGUISHER", new Vector3(0.14f, 1.25f, 0), Quaternion.LookRotation(Vector3.right), 0.3f, new Color(0.8f, 0.08f, 0.08f));
        MakeInspectable(fe.gameObject, ChemLabInspectable.Kind.Decoy, null, "Fire extinguisher");

        // Eye wash station (safety station)
        var ew = Group("EyeWash", p, new Vector3(-0.6f, 0, 3.85f));
        Cyl("Pipe", ew, new Vector3(0, 0.55f, 0.05f), 0.05f, 1.1f, M("ExitGreen", Color.green));
        Cyl("Bowl", ew, new Vector3(0, 1.1f, -0.05f), 0.3f, 0.06f, M("ExitGreen", Color.green));
        Box("Sign", ew, new Vector3(0, 1.6f, 0.14f), new Vector3(0.3f, 0.2f, 0.01f), M("ExitGreen", Color.green));
        Label("Text", ew, "EYE WASH", new Vector3(0, 1.6f, 0.13f), Quaternion.identity, 0.3f, Color.white);
        MakeInspectable(ew.gameObject, ChemLabInspectable.Kind.Decoy, null, "Eye wash station");

        // Correct storage: low corrosives cabinet + labeled lower-shelf bottles
        var cc = Group("CorrosivesCabinet", p, new Vector3(3.75f, 0, 3.7f));
        Box("Body", cc, new Vector3(0, 0.45f, 0), new Vector3(0.75f, 0.9f, 0.5f), M("CabinetBlue", new Color(0.2f, 0.35f, 0.6f)));
        Box("Line", cc, new Vector3(0, 0.45f, -0.252f), new Vector3(0.01f, 0.86f, 0.005f), M("Black", Color.black));
        Label("Text", cc, "CORROSIVES", new Vector3(0, 0.75f, -0.256f), Quaternion.identity, 0.3f, Color.white);
        MakeInspectable(cc.gameObject, ChemLabInspectable.Kind.Decoy, null, "Corrosives cabinet");

        var lower = Group("LowerShelfBottles", p, new Vector3(2.5f, 0.52f, 3.72f));
        for (int i = 0; i < 4; i++)
        {
            float x = -0.45f + i * 0.3f;
            Cyl("Bottle", lower, new Vector3(x, 0.1f, 0), 0.09f, 0.2f, M(i % 2 == 0 ? "AmberGlass" : "BottleWhite", new Color(0.92f, 0.92f, 0.9f)));
            Box("Label", lower, new Vector3(x, 0.1f, -0.046f), new Vector3(0.07f, 0.06f, 0.004f), M("Paper", Color.white));
        }
        MakeInspectable(lower.gameObject, ChemLabInspectable.Kind.Decoy, null, "Labeled buffer bottles");

        var mid = Group("MidShelfGlassware", p, new Vector3(2.5f, 1.02f, 3.72f));
        for (int i = 0; i < 5; i++)
            Cyl("Glass", mid, new Vector3(-0.5f + i * 0.25f, 0.08f, 0), 0.09f, 0.16f, M("Glass", Color.white));
        MakeInspectable(mid.gameObject, ChemLabInspectable.Kind.Decoy, null, "Clean glassware");
    }

    // ===================================================================== the 10 hazards

    static void BuildHazards(Transform p)
    {
        var black = M("Black", new Color(0.08f, 0.08f, 0.08f));
        var card = M("Cardboard", new Color(0.66f, 0.49f, 0.3f));
        var metal = M("Metal", Color.grey);
        var white = M("LabCoat", Color.white);

        // H01 Blocked emergency exit: black utility cart right in front of the exit door
        {
            var g = Group("H01_BlockedExit", p, new Vector3(-3.2f, 0, -3.5f));
            Box("ShelfLow", g, new Vector3(0, 0.25f, 0), new Vector3(0.95f, 0.04f, 0.5f), black);
            Box("ShelfHigh", g, new Vector3(0, 0.85f, 0), new Vector3(0.95f, 0.04f, 0.5f), black);
            foreach (var x in new[] { -0.45f, 0.45f })
                foreach (var z in new[] { -0.22f, 0.22f })
                {
                    Cyl("Post", g, new Vector3(x, 0.52f, z), 0.03f, 0.8f, black);
                    Sphere("Wheel", g, new Vector3(x, 0.05f, z), 0.1f, M("Graphite", Color.grey));
                }
            Box("Handle", g, new Vector3(0.5f, 1.0f, 0), new Vector3(0.03f, 0.3f, 0.45f), black);
            Box("Box", g, new Vector3(-0.1f, 1.05f, 0), new Vector3(0.5f, 0.35f, 0.4f), card);
            Box("Box2", g, new Vector3(0.05f, 0.42f, 0.02f), new Vector3(0.6f, 0.3f, 0.4f), card);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H01");
        }

        // H02 Coffee cup + sandwich next to the pipettes
        {
            var g = Group("H02_FoodAndDrink", p, new Vector3(-1.1f, 0.9f, 0.6f));
            Cyl("Cup", g, new Vector3(0, 0.065f, 0), 0.085f, 0.13f, M("CupPaper", new Color(0.95f, 0.92f, 0.85f)));
            Cyl("Sleeve", g, new Vector3(0, 0.06f, 0), 0.088f, 0.05f, M("CupSleeve", new Color(0.45f, 0.3f, 0.18f)));
            Cyl("Lid", g, new Vector3(0, 0.135f, 0), 0.09f, 0.012f, M("CupLid", new Color(0.2f, 0.15f, 0.12f)));
            Cyl("Plate", g, new Vector3(0.17f, 0.005f, -0.05f), 0.22f, 0.01f, M("Paper", Color.white));
            Box("BreadLow", g, new Vector3(0.17f, 0.02f, -0.05f), new Vector3(0.13f, 0.02f, 0.11f), M("Bread", new Color(0.86f, 0.7f, 0.45f)));
            Box("Filling", g, new Vector3(0.17f, 0.037f, -0.05f), new Vector3(0.135f, 0.014f, 0.115f), M("Lettuce", new Color(0.35f, 0.7f, 0.25f)));
            Box("BreadTop", g, new Vector3(0.17f, 0.054f, -0.05f), new Vector3(0.13f, 0.02f, 0.11f), M("Bread", Color.white));
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H02");
        }

        // H03 Unstably stacked boxes on a crate near the walkway
        {
            var g = Group("H03_StackedBoxes", p, new Vector3(2.7f, 0, -3.35f));
            Box("Crate", g, new Vector3(0, 0.2f, 0), new Vector3(0.65f, 0.4f, 0.5f), M("Wood", new Color(0.55f, 0.4f, 0.25f)));
            Box("Box1", g, new Vector3(0.03f, 0.58f, 0), new Vector3(0.5f, 0.36f, 0.42f), card, new Vector3(0, 8f, 4f));
            Box("Box2", g, new Vector3(0.1f, 0.93f, 0.02f), new Vector3(0.46f, 0.32f, 0.38f), card, new Vector3(0, -14f, -7f));
            Box("Box3", g, new Vector3(0.2f, 1.24f, -0.02f), new Vector3(0.4f, 0.28f, 0.34f), card, new Vector3(0, 22f, 12f));
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H03"), "Box1", "Box2", "Box3");
        }

        // H04 Lab coat draped over a chair (working area)
        {
            var g = Group("H04_LabCoatOnChair", p, new Vector3(-3.75f, 0, 1.0f));
            g.rotation = Quaternion.Euler(0, -90f, 0);   // chair faces the west bench, backrest towards the room
            var fabric = M("ChairFabric", new Color(0.15f, 0.15f, 0.17f));
            Box("Seat", g, new Vector3(0, 0.5f, 0), new Vector3(0.46f, 0.07f, 0.46f), fabric);
            Cyl("Post", g, new Vector3(0, 0.26f, 0), 0.05f, 0.45f, metal);
            Box("BaseA", g, new Vector3(0, 0.04f, 0), new Vector3(0.6f, 0.04f, 0.06f), black);
            Box("BaseB", g, new Vector3(0, 0.04f, 0), new Vector3(0.06f, 0.04f, 0.6f), black);
            Box("Back", g, new Vector3(0, 0.85f, -0.22f), new Vector3(0.44f, 0.55f, 0.05f), fabric);
            Box("CoatTop", g, new Vector3(0, 1.13f, -0.22f), new Vector3(0.48f, 0.05f, 0.14f), white);
            Box("CoatBack", g, new Vector3(0, 0.8f, -0.29f), new Vector3(0.46f, 0.62f, 0.025f), white, new Vector3(-6f, 0, 0));
            Box("CoatFront", g, new Vector3(0, 0.95f, -0.16f), new Vector3(0.42f, 0.32f, 0.02f), white);
            Box("SleeveL", g, new Vector3(-0.27f, 0.82f, -0.25f), new Vector3(0.08f, 0.5f, 0.08f), white, new Vector3(0, 0, -8f));
            Box("SleeveR", g, new Vector3(0.27f, 0.78f, -0.24f), new Vector3(0.08f, 0.55f, 0.08f), white, new Vector3(0, 0, 10f));
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H04"), "CoatTop", "CoatBack", "CoatFront", "SleeveL", "SleeveR");
        }

        // H05 Used gloves and pipette tips in the general (domestic) waste bin
        {
            var g = Group("H05_WasteSeparation", p, new Vector3(-4.6f, 0, 2.95f));
            Cyl("Bin", g, new Vector3(0, 0.25f, 0), 0.36f, 0.5f, M("BinGrey", new Color(0.45f, 0.47f, 0.5f)));
            Cyl("Rim", g, new Vector3(0, 0.505f, 0), 0.38f, 0.015f, black);
            var glove = M("GloveBlue", new Color(0.2f, 0.42f, 0.95f));
            Box("Glove1", g, new Vector3(0.03f, 0.53f, 0.02f), new Vector3(0.18f, 0.03f, 0.1f), glove, new Vector3(0, 30f, 15f));
            Box("Glove2", g, new Vector3(-0.06f, 0.55f, -0.04f), new Vector3(0.17f, 0.03f, 0.09f), glove, new Vector3(10f, -40f, -20f));
            Box("Finger", g, new Vector3(0.11f, 0.58f, 0.05f), new Vector3(0.02f, 0.08f, 0.02f), glove, new Vector3(0, 0, -30f));
            var tip = M("TipYellow", new Color(0.95f, 0.9f, 0.5f));
            Cyl("Tip1", g, new Vector3(0.05f, 0.56f, -0.08f), 0.015f, 0.07f, tip);
            Cyl("Tip2", g, new Vector3(-0.08f, 0.55f, 0.07f), 0.015f, 0.07f, tip);
            Label("Text", g, "GENERAL WASTE", new Vector3(0.185f, 0.3f, 0), Quaternion.LookRotation(Vector3.left), 0.3f, Color.white);
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H05"), "Glove1", "Glove2", "Finger", "Tip1", "Tip2");
        }

        // H06 Unlabeled bottle of clear liquid on the main bench
        {
            var g = Group("H06_UnlabeledBottle", p, new Vector3(0.2f, 0.9f, 0.5f));
            Cyl("Body", g, new Vector3(0, 0.09f, 0), 0.09f, 0.18f, M("Glass", Color.white));
            var liq = Cyl("Liquid", g, new Vector3(0, 0.07f, 0), 0.08f, 0.13f, M("LiquidClear", new Color(0.85f, 0.95f, 1f, 0.55f), transparent: true));
            Cyl("Neck", g, new Vector3(0, 0.2f, 0), 0.035f, 0.05f, M("Glass", Color.white));
            Cyl("Cap", g, new Vector3(0, 0.235f, 0), 0.04f, 0.02f, black);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H06");
        }

        // H07 Safety glasses unused next to active equipment + over-ear headphones
        {
            var g = Group("H07_PPEAndHeadphones", p, new Vector3(1.35f, 0.9f, 0.6f));
            var lens = M("GogglesLens", new Color(0.75f, 0.9f, 1f, 0.5f), transparent: true);
            Box("GogglesLens", g, new Vector3(-0.05f, 0.03f, 0), new Vector3(0.17f, 0.05f, 0.02f), lens, new Vector3(-60f, 0, 0));
            Box("GogglesStrap", g, new Vector3(-0.05f, 0.01f, 0.05f), new Vector3(0.18f, 0.01f, 0.08f), M("GogglesBlue", new Color(0.3f, 0.55f, 0.9f)));
            var hp = M("HeadphoneBlack", new Color(0.1f, 0.1f, 0.12f));
            var cupL = Cyl("EarCupL", g, new Vector3(0.12f, 0.03f, -0.02f), 0.08f, 0.04f, hp);
            var cupR = Cyl("EarCupR", g, new Vector3(0.28f, 0.03f, -0.02f), 0.08f, 0.04f, hp);
            Box("Band", g, new Vector3(0.2f, 0.012f, 0.06f), new Vector3(0.2f, 0.02f, 0.03f), hp);
            Box("BandL", g, new Vector3(0.12f, 0.012f, 0.03f), new Vector3(0.025f, 0.02f, 0.08f), hp);
            Box("BandR", g, new Vector3(0.28f, 0.012f, 0.03f), new Vector3(0.025f, 0.02f, 0.08f), hp);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H07");
        }

        // H08 Frayed cable in an overloaded power strip, tucked behind the centrifuge
        {
            var g = Group("H08_DamagedCable", p, new Vector3(4.82f, 0.9f, 1.25f));
            Box("PowerStrip", g, new Vector3(0, 0.02f, 0), new Vector3(0.06f, 0.04f, 0.32f), M("DeviceWhite", Color.white));
            for (int i = 0; i < 4; i++)
                Box("Plug", g, new Vector3(-0.005f, 0.06f, -0.12f + i * 0.08f), new Vector3(0.04f, 0.05f, 0.04f), black);
            Box("Adapter", g, new Vector3(-0.005f, 0.1f, 0.12f), new Vector3(0.05f, 0.04f, 0.06f), M("DeviceWhite", Color.white));
            Box("AdapterPlug", g, new Vector3(-0.005f, 0.14f, 0.12f), new Vector3(0.04f, 0.04f, 0.04f), black);
            // cable towards the centrifuge with an exposed section
            var cable = M("CableBlack", new Color(0.05f, 0.05f, 0.05f));
            Box("CableA", g, new Vector3(-0.08f, 0.012f, -0.16f), new Vector3(0.12f, 0.018f, 0.018f), cable);
            Box("CableB", g, new Vector3(-0.24f, 0.012f, -0.16f), new Vector3(0.1f, 0.018f, 0.018f), cable);
            Box("WireBrown", g, new Vector3(-0.165f, 0.012f, -0.163f), new Vector3(0.06f, 0.006f, 0.006f), M("WireCopper", new Color(0.85f, 0.45f, 0.2f), metallic: 0.8f));
            Box("WireBlue", g, new Vector3(-0.165f, 0.016f, -0.157f), new Vector3(0.05f, 0.006f, 0.006f), M("WireBlueIns", new Color(0.1f, 0.3f, 0.9f)), new Vector3(0, 12f, 0));
            Box("WireCopper", g, new Vector3(-0.165f, 0.008f, -0.16f), new Vector3(0.055f, 0.004f, 0.004f), M("WireCopper", Color.yellow), new Vector3(0, -10f, 0));
            Box("CableToWall", g, new Vector3(0.04f, 0.012f, 0.25f), new Vector3(0.018f, 0.018f, 0.2f), cable);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H08");
        }

        // H09 First-aid cabinet: open, missing supplies, blocked by bottles on a shelf in front
        {
            var g = Group("H09_FirstAidKit", p, new Vector3(-2.0f, 0, 3.9f));
            var green = M("FirstAidGreen", new Color(0.08f, 0.55f, 0.25f));
            Box("Back", g, new Vector3(0, 1.75f, 0.07f), new Vector3(0.5f, 0.5f, 0.02f), M("Paper", Color.white));
            Box("Top", g, new Vector3(0, 2.0f, 0), new Vector3(0.5f, 0.02f, 0.16f), green);
            Box("Bottom", g, new Vector3(0, 1.5f, 0), new Vector3(0.5f, 0.02f, 0.16f), green);
            Box("Left", g, new Vector3(-0.25f, 1.75f, 0), new Vector3(0.02f, 0.5f, 0.16f), green);
            Box("Right", g, new Vector3(0.25f, 1.75f, 0), new Vector3(0.02f, 0.5f, 0.16f), green);
            Box("InnerShelf", g, new Vector3(0, 1.75f, 0), new Vector3(0.48f, 0.015f, 0.14f), M("Paper", Color.white));
            Box("EmptyBandageBox", g, new Vector3(-0.1f, 1.79f, 0.01f), new Vector3(0.12f, 0.06f, 0.08f), M("Paper", Color.white), new Vector3(0, 0, 70f));
            Box("Door", g, new Vector3(-0.26f, 1.75f, -0.33f), new Vector3(0.02f, 0.5f, 0.5f), green, new Vector3(0, -10f, 0));
            Box("CrossV", g, new Vector3(-0.275f, 1.75f, -0.33f), new Vector3(0.005f, 0.24f, 0.07f), M("Paper", Color.white), new Vector3(0, -10f, 0));
            Box("CrossH", g, new Vector3(-0.275f, 1.75f, -0.33f), new Vector3(0.005f, 0.07f, 0.24f), M("Paper", Color.white), new Vector3(0, -10f, 0));
            Label("Sign", g, "FIRST AID", new Vector3(0, 2.12f, 0.05f), Quaternion.identity, 0.45f, new Color(0.08f, 0.55f, 0.25f));
            // blocking shelf + bottles
            Box("BlockingShelf", g, new Vector3(0, 1.38f, -0.12f), new Vector3(0.7f, 0.025f, 0.26f), M("ShelfWhite", Color.white));
            for (int i = 0; i < 4; i++)
                Cyl("Bottle", g, new Vector3(-0.24f + i * 0.16f, 1.53f, -0.14f), 0.09f, 0.28f, M(i % 2 == 0 ? "AmberGlass" : "BottleWhite", Color.white));
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H09"), "BlockingShelf", "Bottle");
        }

        // H10 Corrosive HCl on the top shelf, next to flammables
        {
            var g = Group("H10_ChemicalStorage", p, new Vector3(2.5f, 1.965f, 3.72f));
            Cyl("HCl", g, new Vector3(-0.2f, 0.13f, 0), 0.1f, 0.26f, M("BottleWhite", new Color(0.92f, 0.92f, 0.9f)));
            Cyl("HClCap", g, new Vector3(-0.2f, 0.275f, 0), 0.045f, 0.03f, M("CapRed", new Color(0.8f, 0.1f, 0.1f)));
            Box("HClLabel", g, new Vector3(-0.2f, 0.13f, -0.051f), new Vector3(0.08f, 0.1f, 0.004f), M("Paper", Color.white));
            Label("HClText", g, "HCl\n37%", new Vector3(-0.2f, 0.15f, -0.054f), Quaternion.identity, 0.12f, Color.black);
            // corrosive pictogram: red diamond
            Box("Pictogram", g, new Vector3(-0.2f, 0.1f, -0.054f), new Vector3(0.03f, 0.03f, 0.002f), M("CapRed", Color.red), new Vector3(0, 0, 45f));
            var orange = M("FlammableOrange", new Color(0.95f, 0.5f, 0.1f));
            for (int i = 0; i < 2; i++)
            {
                float x = 0.05f + i * 0.2f;
                Cyl("Flammable", g, new Vector3(x, 0.11f, 0), 0.09f, 0.22f, M("AmberGlass", Color.white));
                Box("FlamLabel", g, new Vector3(x, 0.11f, -0.046f), new Vector3(0.07f, 0.08f, 0.004f), orange);
                Label("FlamText", g, "ETHANOL\nFLAMMABLE", new Vector3(x, 0.11f, -0.049f), Quaternion.identity, 0.08f, Color.black);
            }
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H10"), "HCl", "HClCap", "HClLabel", "HClText", "Pictogram");
        }
    }

    // ===================================================================== helpers

    /// <summary>When fixed, only these child parts disappear (the rest of the object stays, e.g. the chair under the coat).</summary>
    static void Hide(ChemLabInspectable insp, params string[] names)
    {
        insp.hideOnFix = insp.GetComponentsInChildren<Transform>(true).Where(t => t != insp.transform && names.Contains(t.name)).Select(t => t.gameObject).ToArray();
    }

    static ChemLabInspectable MakeInspectable(GameObject g, ChemLabInspectable.Kind kind, string id, string displayName = null)
    {
        // one consistent collider on the parent covering all child meshes (R07: deliberate, fair hitbox)
        var renderers = g.GetComponentsInChildren<Renderer>();
        foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        bool any = false;
        var b = new Bounds();
        foreach (var r in renderers)
        {
            if (r.GetComponent<TextMeshPro>() != null) continue;
            var rb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = g.transform.InverseTransformPoint(corner);
                if (!any) { b = new Bounds(local, Vector3.zero); any = true; }
                else b.Encapsulate(local);
            }
        }
        var box = g.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = b.size + Vector3.one * 0.03f;   // small margin so thin objects are easy to hit

        var simple = g.AddComponent<XRSimpleInteractable>();
        var insp = g.AddComponent<ChemLabInspectable>();
        insp.kind = kind;
        insp.hazardId = id;
        insp.displayName = displayName;
        return insp;
    }

    static Transform Group(string name, Transform parent, Vector3 pos)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        g.localPosition = pos;
        return g;
    }

    static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, Vector3? euler = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static GameObject Cyl(string name, Transform parent, Vector3 localPos, float diameter, float height, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = new Vector3(diameter, height / 2f, diameter);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static GameObject Sphere(string name, Transform parent, Vector3 localPos, float d, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = Vector3.one * d;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static TextMeshPro Label(string name, Transform parent, string text, Vector3 localPos, Quaternion localRot, float size, Color col)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = localRot;
        var t = g.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.color = col;
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.3f, size * 2.2f), Mathf.Max(0.1f, size * 0.6f));
        return t;
    }

    /// <summary>Gets or creates a URP Lit material asset in ChemistryLab/Materials (first call per name defines it).</summary>
    static Material M(string name, Color c, bool transparent = false, float metallic = 0f, float smooth = 0.4f, bool emissive = false, Texture2D tex = null, Vector2? tiling = null)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        string path = $"{Root}/Materials/Lab_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        }
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (create) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }
}
