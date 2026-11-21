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
    const string TutorialPath = Root + "/Scenes/ChemLab_Tutorial.unity";
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
        EditorUtility.DisplayDialog("Chemistry Lab", "Built 4 scenes:\n" + LobbyPath + "\n" + TutorialPath + "\n" + ScenePath + "\n" + EndPath +
            "\n\nOpen ChemLab_Lobby and press Play (with Quest Link), or Build & Run to the Quest 3.", "OK");
    }

    /// <summary>For batch mode: -executeMethod ChemLabSceneBuilder.BuildBatch</summary>
    public static void BuildBatch() => BuildAll();

    /// <summary>Rebuilds only the lobby (the lab scene and its tuning stay untouched).</summary>
    [MenuItem("Chemistry Lab/Build Lobby Scene")]
    public static void BuildLobbyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildLobbyOnly();
    }

    public static void BuildLobbyOnly()
    {
        SharedMaterials();
        BuildLobby();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LobbyPath);
        Debug.Log("[ChemLab] Built the Lobby scene.");
    }

    /// <summary>Rebuilds only the tutorial room and puts it after the lobby in Build Settings (the lab stays untouched).</summary>
    [MenuItem("Chemistry Lab/Build Tutorial Scene")]
    public static void BuildTutorialMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildTutorialOnly();
    }

    public static void BuildTutorialOnly()
    {
        SharedMaterials();
        BuildTutorial();
        AddTutorialToBuild();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(TutorialPath);
        Debug.Log("[ChemLab] Built the Tutorial scene.");
    }

    static void SharedMaterials()
    {
        mats.Clear();
        // shared materials first (later calls with the same name reuse these)
        M("Glass", new Color(0.85f, 0.95f, 1f, 0.35f), transparent: true, smooth: 0.9f);
        M("LiquidBlue", new Color(0.2f, 0.5f, 0.95f, 0.75f), transparent: true);
        M("LiquidGreen", new Color(0.3f, 0.85f, 0.4f, 0.75f), transparent: true);
        M("LiquidAmber", new Color(0.95f, 0.65f, 0.2f, 0.75f), transparent: true);
        M("Metal", new Color(0.6f, 0.62f, 0.65f), metallic: 0.7f, smooth: 0.6f);
        M("NodeGlow", new Color(0f, 0.85f, 0.75f, 0.6f), transparent: true, emissive: true);
    }

    static void BuildAll()
    {
        SharedMaterials();
        BuildLab();
        BuildLobby();
        BuildTutorial();
        BuildEnd();
        var ours = new[] { LobbyPath, TutorialPath, ScenePath, EndPath };
        EditorBuildSettings.scenes = ours.Select(x => new EditorBuildSettingsScene(x, true))
            .Concat(EditorBuildSettings.scenes.Where(s => !ours.Contains(s.path))).ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LobbyPath);
        Debug.Log("[ChemLab] Built Lobby, Tutorial, Lab and End scenes.");
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
        // random placement zones are hand-tuned in their prefab (HazardZone), not generated here
        var zones = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HazardZones.prefab");
        if (team && zones != null) PrefabUtility.InstantiatePrefab(zones);

        // Movement: left stick walks, right stick turns (no teleport nodes in the lab)
        Vector3 startPos;
        if (team)
        {
            startPos = new Vector3(0.9f, 0, 0.2f);
        }
        else
        {
            startPos = new Vector3(-1.5f, 0, -2.6f);
        }

        var leftHand = Systems(startPos, 0f);

        var game = new GameObject("ChemLab Game");
        var ui = game.AddComponent<ChemLabUI>();
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
        LobbyDecor(env);

        Systems(start, 0f);
        var game = new GameObject("ChemLab Lobby");
        var lobby = game.AddComponent<ChemLabLobby>();
        // 1.05 m / 1.10 m from the start spot, and the lobby draws these panels 1.15x / 1.2x larger: 28-unit body
        // text gets an x-height of ~0.92 deg, above Meta's 0.9 deg minimum (was 0.68 / 0.62 deg at 1.24 / 1.35 m)
        lobby.mainPanelAnchor = Anchor("MainPanelAnchor", new Vector3(-0.38f, 1.5f, -1.42f), start);
        lobby.teachingPanelAnchor = Anchor("TeachingPanelAnchor", new Vector3(0.69f, 1.45f, -1.55f), start);
        // kiosk stands under both panels, ending at their lower edge (main: 600 x 0.00115 tall, teaching: 960 x 0.0012)
        Stand(env, lobby.mainPanelAnchor, 1.5f - 0.345f);
        Stand(env, lobby.teachingPanelAnchor, 1.45f - 0.58f);
        // wall posters face into the room (anchor forward points into the wall)
        lobby.posterAnchors = new[]
        {
            WallAnchor("Poster_Chemicals", new Vector3(3.98f, 1.95f, -0.6f), Vector3.right),
            WallAnchor("Poster_NoFood", new Vector3(1.5f, 1.85f, -3.98f), Vector3.back),
            WallAnchor("Poster_PPE", new Vector3(3.98f, 1.95f, 1.15f), Vector3.right),
            WallAnchor("Poster_Eyewash", new Vector3(-3.98f, 1.95f, 0.35f), Vector3.left),
        };
        game.AddComponent<ChemLabControls>();
        LobbyLabEquipment.Apply(scene);   // same equipment models as the lab scene
        EditorSceneManager.SaveScene(scene, LobbyPath);
    }

    static Transform WallAnchor(string name, Vector3 pos, Vector3 intoWall)
    {
        var t = new GameObject(name).transform;
        t.SetPositionAndRotation(pos, Quaternion.LookRotation(intoWall));
        return t;
    }

    /// <summary>Kiosk stand under a floating panel: base plate and a pole ending at the panel's lower edge.</summary>
    static void Stand(Transform env, Transform panel, float height)
    {
        var g = Group("Stand_" + panel.name, env, new Vector3(panel.position.x, 0, panel.position.z) + panel.forward * 0.04f);
        g.rotation = panel.rotation;
        var dark = M("StandDark", new Color(0.2f, 0.22f, 0.25f), metallic: 0.4f, smooth: 0.5f);
        Cyl("Base", g, new Vector3(0, 0.01f, 0), 0.45f, 0.02f, dark);
        Cyl("Pole", g, new Vector3(0, height / 2f, 0), 0.05f, height, dark);
        Box("Bracket", g, new Vector3(0, height - 0.02f, 0), new Vector3(0.5f, 0.04f, 0.04f), dark);
        foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    }

    /// <summary>
    /// Set dressing for the briefing room: the door into the lab, a demo bench under the window, an eyewash and
    /// safety shower station, a fire extinguisher, a first-aid box, lab coats and goggles by the door, a flammables
    /// cabinet, lockers and a coffee corner (where drinks belong). Big furniture keeps its collider; small props don't.
    /// </summary>
    static void LobbyDecor(Transform env)
    {
        var d = Group("Decor", env, Vector3.zero);
        var glass = M("Glass", Color.white);
        var metal = M("Metal", Color.grey);

        // --- north wall: door to the lab (west of the window) with hazard tape in front
        LabDoor(d, new Vector3(-3.2f, 0, 3.98f));
        var tape = M("HazardTape", new Color(0.98f, 0.66f, 0f), emissive: false);
        for (int i = 0; i < 6; i++)
            Box("Tape", d, new Vector3(-3.7f + i * 0.2f, 0.003f, 3.45f), new Vector3(0.1f, 0.006f, 0.08f), i % 2 == 0 ? tape : M("TapeBlack", new Color(0.08f, 0.08f, 0.08f)));

        // --- north wall: demo bench under the window, glassware and labeled bottles
        Bench("DemoBench", d, new Vector3(0.7f, 0, 3.6f), new Vector2(3.0f, 0.7f));
        var top = Group("BenchProps", d, new Vector3(0.7f, 0.9f, 3.6f));
        for (int i = 0; i < 3; i++)
        {
            float x = -1.2f + i * 0.16f;
            Cyl("Beaker", top, new Vector3(x, 0.07f, -0.1f), 0.09f, 0.14f, glass);
            Cyl("Liquid", top, new Vector3(x, 0.04f, -0.1f), 0.08f, 0.07f, M(i == 0 ? "LiquidBlue" : i == 1 ? "LiquidGreen" : "LiquidAmber", Color.white));
        }
        var flask = Group("Flask", top, new Vector3(-0.55f, 0, -0.05f));
        Sphere("Bulb", flask, new Vector3(0, 0.08f, 0), 0.15f, glass);
        Cyl("Neck", flask, new Vector3(0, 0.2f, 0), 0.04f, 0.12f, glass);
        Sphere("Liquid", flask, new Vector3(0, 0.06f, 0), 0.11f, M("LiquidBlue", Color.white));
        var rack = Group("TubeRack", top, new Vector3(-0.1f, 0, -0.05f));
        Box("Rack", rack, new Vector3(0, 0.03f, 0), new Vector3(0.28f, 0.06f, 0.1f), M("RackWhite", Color.white));
        for (int i = 0; i < 5; i++)
            Cyl("Tube", rack, new Vector3(-0.1f + i * 0.05f, 0.08f, 0), 0.018f, 0.12f, M(i % 2 == 0 ? "LiquidBlue" : "LiquidAmber", Color.white));
        Model("AmberBottle", "Amber", top, new Vector3(0.35f, 0, 0.05f), new Vector3(0.1f, 0.24f, 0.1f), Vector3.zero, 0.7f);
        Model("HClBottle", "HCl", top, new Vector3(0.55f, 0, 0.05f), new Vector3(0.12f, 0.26f, 0.12f), Vector3.zero, 0.6f);
        Model("AcetoneBottle", "Acetone", top, new Vector3(0.75f, 0, 0.05f), new Vector3(0.12f, 0.24f, 0.12f), Vector3.zero, 0.6f);
        var scope = Group("Microscope", top, new Vector3(1.15f, 0, -0.05f));
        var white = M("RackWhite", Color.white);
        Box("Foot", scope, new Vector3(0, 0.02f, 0), new Vector3(0.18f, 0.04f, 0.24f), white);
        Box("Arm", scope, new Vector3(0, 0.17f, 0.08f), new Vector3(0.05f, 0.28f, 0.06f), white);
        Box("Stage", scope, new Vector3(0, 0.12f, -0.01f), new Vector3(0.14f, 0.015f, 0.12f), M("Black", new Color(0.08f, 0.08f, 0.08f)));
        Cyl("Tube", scope, new Vector3(0, 0.3f, 0.01f), 0.045f, 0.16f, white);
        Cyl("Eyepiece", scope, new Vector3(0, 0.4f, 0.03f), 0.03f, 0.06f, M("Black", new Color(0.08f, 0.08f, 0.08f)));
        foreach (var c in top.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        // --- west wall: safety shower + eyewash, fire extinguisher, first-aid box
        var green = M("SafetyGreen", new Color(0.1f, 0.6f, 0.32f), smooth: 0.5f);
        var shower = Group("SafetyShower", d, new Vector3(-3.8f, 0, 1.2f));
        Cyl("Riser", shower, new Vector3(-0.08f, 1.2f, 0), 0.05f, 2.4f, green);
        Box("Arm", shower, new Vector3(0.12f, 2.4f, 0), new Vector3(0.45f, 0.05f, 0.05f), green);
        Cyl("Head", shower, new Vector3(0.32f, 2.32f, 0), 0.3f, 0.08f, green);
        Box("PullRod", shower, new Vector3(0.32f, 2.0f, 0), new Vector3(0.015f, 0.6f, 0.015f), metal);
        Box("PullHandle", shower, new Vector3(0.32f, 1.68f, 0), new Vector3(0.18f, 0.04f, 0.04f), green);
        Box("BowlArm", shower, new Vector3(0.05f, 1.0f, 0), new Vector3(0.25f, 0.04f, 0.04f), green);
        Cyl("Bowl", shower, new Vector3(0.25f, 1.02f, 0), 0.32f, 0.07f, M("EyewashYellow", new Color(0.98f, 0.8f, 0.1f)));
        Box("FloorMark", shower, new Vector3(0.25f, 0.003f, 0), new Vector3(0.9f, 0.006f, 0.9f), M("SafetyGreenFloor", new Color(0.1f, 0.6f, 0.32f, 0.5f), transparent: true));

        var ext = Group("FireExtinguisher", d, new Vector3(-3.86f, 0, -0.7f));
        var red = M("ExtinguisherRed", new Color(0.75f, 0.08f, 0.06f), smooth: 0.7f);
        Box("Bracket", ext, new Vector3(-0.08f, 0.75f, 0), new Vector3(0.03f, 0.2f, 0.14f), metal);
        Cyl("Body", ext, new Vector3(0.02f, 0.6f, 0), 0.17f, 0.52f, red);
        Cyl("Valve", ext, new Vector3(0.02f, 0.9f, 0), 0.06f, 0.08f, M("Black", new Color(0.08f, 0.08f, 0.08f)));
        Box("Hose", ext, new Vector3(0.1f, 0.72f, 0.06f), new Vector3(0.02f, 0.3f, 0.02f), M("Black", new Color(0.08f, 0.08f, 0.08f)));
        Box("Sign", ext, new Vector3(-0.1f, 1.3f, 0), new Vector3(0.01f, 0.3f, 0.3f), red);
        Label("SignText", ext, "FIRE", new Vector3(-0.09f, 1.3f, 0), Quaternion.Euler(0, -90f, 0), 0.6f, Color.white);

        var aid = Group("FirstAidBox", d, new Vector3(-3.9f, 1.45f, 2.7f));
        Box("Box", aid, Vector3.zero, new Vector3(0.14f, 0.42f, 0.5f), green);
        Box("CrossV", aid, new Vector3(0.072f, 0, 0), new Vector3(0.01f, 0.24f, 0.07f), M("PaperWhite", Color.white));
        Box("CrossH", aid, new Vector3(0.072f, 0, 0), new Vector3(0.01f, 0.07f, 0.24f), M("PaperWhite", Color.white));

        // --- east wall: lab coats and goggles, flammables cabinet
        var coats = Group("CoatHooks", d, new Vector3(3.9f, 0, 2.6f));
        Box("Board", coats, new Vector3(0.06f, 1.45f, 0), new Vector3(0.02f, 0.9f, 1.3f), M("CounterWood", new Color(0.62f, 0.47f, 0.32f), smooth: 0.35f));
        Box("Rail", coats, new Vector3(0, 1.75f, 0), new Vector3(0.04f, 0.04f, 1.1f), metal);
        var coat = M("LabCoat", new Color(0.96f, 0.96f, 0.96f));
        var cuff = M("CoatBlue", new Color(0.3f, 0.5f, 0.8f));
        for (int i = 0; i < 3; i++)
        {
            float z = -0.4f + i * 0.4f;
            Box("Coat", coats, new Vector3(-0.1f, 1.3f, z), new Vector3(0.16f, 0.85f, 0.34f), coat);
            Box("Collar", coats, new Vector3(-0.1f, 1.7f, z), new Vector3(0.18f, 0.06f, 0.22f), cuff);
            Box("Placket", coats, new Vector3(-0.181f, 1.3f, z), new Vector3(0.004f, 0.8f, 0.02f), cuff);
        }
        Box("GogglesShelf", coats, new Vector3(-0.12f, 1.0f, 0.95f), new Vector3(0.24f, 0.02f, 0.4f), M("ShelfWhite", new Color(0.88f, 0.88f, 0.86f)));
        Model("SafetyGlasses", "Goggles1", coats, new Vector3(-0.12f, 1.01f, 0.85f), new Vector3(0.16f, 0.06f, 0.16f), new Vector3(0, -90f, 0), 0.6f);
        Model("SafetyGlasses", "Goggles2", coats, new Vector3(-0.12f, 1.01f, 1.05f), new Vector3(0.16f, 0.06f, 0.16f), new Vector3(0, -80f, 0), 0.6f);

        var cab = Group("FlammablesCabinet", d, new Vector3(3.72f, 0, -0.6f));
        var yellow = M("CabinetYellow", new Color(0.98f, 0.75f, 0.05f), metallic: 0.2f, smooth: 0.5f);
        Box("Body", cab, new Vector3(0, 0.6f, 0), new Vector3(0.5f, 1.2f, 1.0f), yellow);
        Box("Seam", cab, new Vector3(-0.252f, 0.6f, 0), new Vector3(0.006f, 1.1f, 0.012f), M("TapeBlack", Color.black));
        Box("HandleL", cab, new Vector3(-0.27f, 0.65f, -0.06f), new Vector3(0.03f, 0.18f, 0.025f), metal);
        Box("HandleR", cab, new Vector3(-0.27f, 0.65f, 0.06f), new Vector3(0.03f, 0.18f, 0.025f), metal);
        Box("Label", cab, new Vector3(-0.252f, 1.0f, 0), new Vector3(0.006f, 0.12f, 0.6f), M("TapeBlack", Color.black));
        Label("LabelText", cab, "FLAMMABLE", new Vector3(-0.258f, 1.0f, 0), Quaternion.Euler(0, 90f, 0), 0.55f, new Color(0.98f, 0.75f, 0.05f));

        // --- south wall (behind the start): lockers and a coffee corner, where drinks belong
        var lockers = Group("Lockers", d, new Vector3(-2.2f, 0, -3.75f));
        var grey = M("LockerGrey", new Color(0.62f, 0.66f, 0.7f), metallic: 0.3f, smooth: 0.45f);
        for (int i = 0; i < 4; i++)
        {
            float x = -0.66f + i * 0.44f;
            Box("Locker", lockers, new Vector3(x, 0.95f, 0), new Vector3(0.42f, 1.9f, 0.45f), grey);
            for (int v = 0; v < 3; v++)
            {
                var vent = Box("Vent", lockers, new Vector3(x, 1.65f - v * 0.04f, 0.226f), new Vector3(0.22f, 0.012f, 0.004f), M("Black", new Color(0.08f, 0.08f, 0.08f)));
                Object.DestroyImmediate(vent.GetComponent<Collider>());
            }
            var h = Box("Handle", lockers, new Vector3(x + 0.14f, 1.05f, 0.235f), new Vector3(0.02f, 0.12f, 0.02f), metal);
            Object.DestroyImmediate(h.GetComponent<Collider>());
        }
        var corner = Group("CoffeeCorner", d, new Vector3(1.5f, 0, -3.7f));
        Box("Counter", corner, new Vector3(0, 0.45f, 0), new Vector3(1.2f, 0.9f, 0.55f), M("CounterWood", new Color(0.62f, 0.47f, 0.32f), smooth: 0.35f));
        Box("Machine", corner, new Vector3(-0.3f, 1.08f, 0.05f), new Vector3(0.28f, 0.36f, 0.3f), M("CoffeeMachine", new Color(0.12f, 0.12f, 0.14f), metallic: 0.3f, smooth: 0.6f));
        var cups = Group("Cups", corner, new Vector3(0.2f, 0.9f, 0));
        Model("CoffeeCup", "Cup1", cups, Vector3.zero, new Vector3(0.09f, 0.13f, 0.09f), Vector3.zero);
        Model("CoffeeCup", "Cup2", cups, new Vector3(0.14f, 0, 0.05f), new Vector3(0.09f, 0.13f, 0.09f), new Vector3(0, 40f, 0));
        foreach (var c in cups.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    }

    /// <summary>Steel door into the lab (on a north wall, facing -Z) with the LAB 404 sign above it.</summary>
    static Transform LabDoor(Transform parent, Vector3 pos)
    {
        var door = Group("LabDoor", parent, pos);
        var frame = M("DoorFrame", new Color(0.25f, 0.28f, 0.32f));
        Box("PostL", door, new Vector3(-0.56f, 1.1f, 0), new Vector3(0.08f, 2.2f, 0.06f), frame);
        Box("PostR", door, new Vector3(0.56f, 1.1f, 0), new Vector3(0.08f, 2.2f, 0.06f), frame);
        Box("Lintel", door, new Vector3(0, 2.24f, 0), new Vector3(1.2f, 0.08f, 0.06f), frame);
        Box("Leaf", door, new Vector3(0, 1.08f, 0.01f), new Vector3(1.04f, 2.12f, 0.04f), M("DoorSteel", new Color(0.55f, 0.63f, 0.7f), metallic: 0.3f, smooth: 0.5f));
        Box("Window", door, new Vector3(0.2f, 1.55f, -0.012f), new Vector3(0.3f, 0.5f, 0.01f), M("Glass", Color.white));
        Box("PushBar", door, new Vector3(0, 1.0f, -0.04f), new Vector3(0.8f, 0.04f, 0.04f), M("Metal", Color.grey));
        Box("Sign", door, new Vector3(0, 2.52f, -0.01f), new Vector3(0.9f, 0.26f, 0.02f), M("SignBlue", new Color(0.08f, 0.28f, 0.54f)));
        Label("SignText", door, "LAB 404  ·  BSL-1", new Vector3(0, 2.52f, -0.025f), Quaternion.identity, 0.9f, Color.white);
        return door;
    }

    // ===================================================================== scene 1b: tutorial room

    /// <summary>
    /// The practice room (7 x 9 m) between the lobby and the lab: start in the south, ring 1 beside bench A (east wall: a
    /// closed acetone bottle as the safe object, an open HCl bottle as practice hazard), ring 2 beside bench B (west
    /// wall: a spill), ring 3 in front of the lab door (north wall). One card anchor per step, ~1.1 m from where the
    /// player stands for it, so the card's 30-unit body text stays near 0.95 deg x-height.
    /// </summary>
    static void BuildTutorial()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLighting(bright: true);
        var env = new GameObject("Environment").transform;
        SimpleRoom(env, 7f, 9f);
        Object.DestroyImmediate(env.Find("LightWall").gameObject);   // the door takes the middle of the north wall
        LabDoor(env, new Vector3(0, 0, 4.48f));

        var start = new Vector3(0, 0, -3.6f);
        Vector3 r1 = new Vector3(1.6f, 0, -1.5f), r2 = new Vector3(-1.6f, 0, 1.2f), r3 = new Vector3(0, 0, 3.3f);
        var ringRoot = new GameObject("Rings").transform;
        ChemLabTeleportNode Ring(string name, Vector3 pos, float yaw)
        {
            var n = Node(ringRoot, name, pos, yaw, teleport: false);   // the floor takes the teleport arc, as in the lab
            Object.DestroyImmediate(n.Find("Tag").gameObject);           // the glow is the sign; the card says what to do
            var node = n.GetComponent<ChemLabTeleportNode>();
            node.nameTag = null;
            return node;
        }
        var rings = new[] { Ring("Ring1", r1, 90f), Ring("Ring2", r2, -90f), Ring("Ring3", r3, 0f) };

        // bench A (east): safe closed bottle + open acid bottle with its cap lying beside it
        Bench("BenchA", env, new Vector3(3.1f, 0, -1.5f), new Vector2(0.7f, 2.0f));
        var practice = new GameObject("Practice items").transform;
        var decoy = Group("Safe_ClosedBottle", practice, new Vector3(3.0f, 0.9f, -1.85f));
        Model("AcetoneBottle", "Bottle", decoy, Vector3.zero, new Vector3(0.12f, 0.24f, 0.12f), new Vector3(0, 90f, 0), 0.6f);   // label towards ring 1 (west)
        MakeInspectable(decoy.gameObject, ChemLabInspectable.Kind.Decoy, null, "Decoy");
        var acid = Group("Practice_OpenAcid", practice, new Vector3(3.0f, 0.9f, -1.2f));
        Model("HClBottle", "Bottle", acid, Vector3.zero, new Vector3(0.14f, 0.27f, 0.14f), new Vector3(0, 90f, 0), 0.6f);
        var cap = Cyl("Cap", acid, new Vector3(-0.05f, 0.015f, 0.14f), 0.05f, 0.03f, M("CapRed", new Color(0.8f, 0.1f, 0.1f)));
        cap.transform.localRotation = Quaternion.Euler(90f, 0, 0);   // lying on its side
        MakeInspectable(acid.gameObject, ChemLabInspectable.Kind.Tutorial, null, "Acid");

        // bench B (west): a clear spill with the tipped beaker it came from
        Bench("BenchB", env, new Vector3(-3.1f, 0, 1.2f), new Vector2(0.7f, 2.0f));
        var spill = Group("Practice_Spill", practice, new Vector3(-2.95f, 0.9f, 1.05f));
        var wet = M("SpillClear", new Color(0.7f, 0.85f, 1f, 0.55f), transparent: true, smooth: 0.95f);
        Cyl("Puddle", spill, new Vector3(0, 0.003f, 0), 0.42f, 0.004f, wet);
        Cyl("Puddle2", spill, new Vector3(0.08f, 0.003f, 0.24f), 0.22f, 0.004f, wet);
        var beaker = Cyl("Beaker", spill, new Vector3(0.02f, 0.045f, -0.26f), 0.09f, 0.14f, M("Glass", Color.white));
        beaker.transform.localRotation = Quaternion.Euler(0, 0, 90f);   // tipped over
        MakeInspectable(spill.gameObject, ChemLabInspectable.Kind.Tutorial, null, "Spill");
        var sc = spill.GetComponent<BoxCollider>();
        sc.size = new Vector3(sc.size.x, 0.12f, sc.size.z);   // a puddle is flat; give the ray something to hit
        sc.center = new Vector3(sc.center.x, 0.05f, sc.center.z);

        var leftHand = Systems(start, 0f);
        var game = new GameObject("ChemLab Tutorial");
        var tut = game.AddComponent<ChemLabTutorial>();
        tut.leftHandAnchor = leftHand;
        tut.rings = rings;
        var bench = Anchor("Card_BenchA", new Vector3(2.55f, 1.55f, -0.95f), r1);
        var spillCard = Anchor("Card_Spill", new Vector3(-2.55f, 1.55f, 1.75f), r2);
        tut.stepAnchors = new[]
        {
            Anchor("Card_Start", new Vector3(-0.4f, 1.5f, -2.55f), start),   // 1 teleport
            Anchor("Card_Ring1", new Vector3(1.6f, 1.5f, -0.4f), r1),       // 2 turn (straight ahead on arrival)
            bench, bench,                                                     // 3 safe bottle, 4 acid
            spillCard, spillCard,                                             // 5 spill, 6 task panel
            Anchor("Card_Door", new Vector3(0.85f, 1.5f, 4.0f), r3),        // 7 finish
        };
        game.AddComponent<ChemLabControls>();
        EditorSceneManager.SaveScene(scene, TutorialPath);
    }

    /// <summary>Puts the tutorial right after the lobby in Build Settings (adds it once).</summary>
    static void AddTutorialToBuild()
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != TutorialPath).ToList();
        int lobby = list.FindIndex(s => s.path == LobbyPath);
        list.Insert(lobby + 1, new EditorBuildSettingsScene(TutorialPath, true));
        EditorBuildSettings.scenes = list.ToArray();
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
        // report card 1.10 m from the start spot, drawn at 0.00118 scale by the end screen: 28-unit text ~0.92 deg
        end.resultsAnchor = Anchor("ResultsAnchor", new Vector3(-0.29f, 1.5f, -1.34f), start);
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

        // Starter rig input managers, both sticks the same: push forward to aim a teleport arc and release to jump,
        // left/right to snap turn, pull back to turn around. No continuous walking or turning (motion sickness).
        foreach (var m in rig.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "ControllerInputActionManager"))
        {
            var so = new SerializedObject(m);
            var sm = so.FindProperty("m_SmoothMotionEnabled"); if (sm != null) sm.boolValue = false;
            var st = so.FindProperty("m_SmoothTurnEnabled"); if (st != null) st.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        SetSnapTurn(rig);
        // Speeds for the continuous providers (inactive while both sticks teleport; kept in case walking is re-enabled).
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

    /// <summary>
    /// Snap-turn step. 45 deg is the XRI default and a common comfort setting: two clicks face a bench square on (90 deg),
    /// which 60 deg cannot. Raise it if turning around in the aisle feels slow.
    /// </summary>
    public const float SnapTurnDegrees = 45f;

    public static void SetSnapTurn(GameObject rig)
    {
        foreach (var snap in rig.GetComponentsInChildren<SnapTurnProvider>(true))
        {
            snap.turnAmount = SnapTurnDegrees;
            PrefabUtility.RecordPrefabInstancePropertyModifications(snap);
        }
    }

    /// <summary>Lets the teleport arc land anywhere on this floor (its collider), never inside furniture.</summary>
    public static void TeleportFloor(GameObject floor)
    {
        var area = floor.AddComponent<ChemLabSafeTeleportArea>();
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
        // the tiling lives on the material, so a room of another size gets its own floor material
        Box("Floor", env, new Vector3(0, -0.05f, 0), new Vector3(w, 0.1f, d), M(w == 8f && d == 8f ? "LobbyFloor" : $"LobbyFloor_{w}x{d}", new Color(0.86f, 0.88f, 0.9f), smooth: 0.65f, tex: floorTex, tiling: new Vector2(w / 2f, d / 2f)));
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
        // exit sign on the wall above the entrance door (wall face z = -1.59 above 2.4 m, ceiling 2.96 m)
        var sign = Model("ExitSign", "ExitSign", parent, Vector3.zero, new Vector3(0.6f, 0, 0.12f), new Vector3(0, 180f, 0), out var es);
        sign.localPosition = new Vector3(0f, 2.4f, -1.59f + es.z / 2f);
        ExitSignGlow();
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
            { "RecyclingBin",          (2.15f, 8.5f, 180f, false) },
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
        // exit sign model: its face is -Z, the room is +Z here; lit like a real exit sign
        var sign = Model("ExitSign", "ExitSign", door, new Vector3(0, 2.22f, 0), new Vector3(0.7f, 0, 0.12f), new Vector3(0, 180f, 0), out var es);
        sign.localPosition += new Vector3(0, 0, 0.02f + es.z / 2f);
        ExitSignGlow();

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
        foreach (var y in new[] { 0.05f, 0.5f, 1.0f, 1.5f })
            Box("Shelf_" + y, shelf, new Vector3(0, y, 0), new Vector3(1.42f, 0.03f, 0.4f), wood);
        // top shelf is glass; its surface stays at 1.965 where H10 stands
        var glass = Model("GlassShelf", "Shelf_1.95", shelf, Vector3.zero, new Vector3(1.42f, 0, 0.4f), Vector3.zero, out var gs, 0.8f);
        glass.localPosition = new Vector3(0, 1.965f - gs.y, 0);
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
        Model("BiohazardBin", "Bin", lw, Vector3.zero, new Vector3(0.45f, 0.62f, 0.45f), new Vector3(0, -90f, 0), out var lwb);
        Label("Text", lw, "LAB WASTE", new Vector3(lwb.z / 2f + 0.01f, lwb.y * 0.5f, 0), Quaternion.LookRotation(Vector3.left), 0.35f, Color.black);
        MakeInspectable(lw.gameObject, ChemLabInspectable.Kind.Decoy, null, "Lab waste bin");

        var rb = Group("RecyclingBin", p, new Vector3(-4.6f, 0, 2.35f));
        Model("RecyclingBin", "Bin", rb, Vector3.zero, new Vector3(0.45f, 0.62f, 0.45f), new Vector3(0, -90f, 0));
        MakeInspectable(rb.gameObject, ChemLabInspectable.Kind.Decoy, null, "Recycling bin");

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

        // H01 Blocked emergency exit: utility trolley with boxes right in front of the exit door
        {
            var g = Group("H01_BlockedExit", p, new Vector3(-3.2f, 0, -3.5f));
            Model("Trolley", "Trolley", g, Vector3.zero, new Vector3(1.0f, 1.1f, 0.6f), Vector3.zero, out var cart, 0.5f);
            Model("CardboardBox", "Box", g, new Vector3(-0.05f, cart.y * 0.8f, 0), new Vector3(0.45f, 0.3f, 0.4f), new Vector3(0, 8f, 0));
            Model("CardboardBox", "Box2", g, new Vector3(0.05f, cart.y * 0.22f, 0), new Vector3(0.5f, 0.28f, 0.4f), Vector3.zero);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H01");
        }

        // H02 Coffee cup + sandwich next to the pipettes
        {
            var g = Group("H02_FoodAndDrink", p, new Vector3(-1.1f, 0.9f, 0.6f));
            Model("CoffeeCup", "Cup", g, Vector3.zero, new Vector3(0.1f, 0.15f, 0.1f), Vector3.zero);
            Model("Sandwich", "Sandwich", g, new Vector3(0.17f, 0, -0.05f), new Vector3(0.15f, 0.08f, 0.12f), new Vector3(0, 25f, 0));
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H02");
        }

        // H03 Unstably stacked boxes on a crate near the walkway
        {
            var g = Group("H03_StackedBoxes", p, new Vector3(2.7f, 0, -3.35f));
            Model("WoodenCrate", "Crate", g, Vector3.zero, new Vector3(0.65f, 0, 0.5f), Vector3.zero, out var crate);
            float y = crate.y;
            Model("CardboardBox", "Box1", g, new Vector3(0.03f, y, 0), new Vector3(0.5f, 0.36f, 0.42f), new Vector3(0, 8f, 4f), out var b1);
            y += b1.y;
            Model("CardboardBox", "Box2", g, new Vector3(0.1f, y, 0.02f), new Vector3(0.46f, 0.32f, 0.38f), new Vector3(0, -14f, -7f), out var b2);
            y += b2.y;
            Model("CardboardBox", "Box3", g, new Vector3(0.2f, y, -0.02f), new Vector3(0.4f, 0.28f, 0.34f), new Vector3(0, 22f, 12f));
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H03"), "Box1", "Box2", "Box3");
        }

        // H04 Lab coat draped over a chair (working area)
        {
            var g = Group("H04_LabCoatOnChair", p, new Vector3(-3.75f, 0, 1.0f));
            g.rotation = Quaternion.Euler(0, -90f, 0);   // chair faces the west bench, backrest towards the room
            // ponytail: the model is chair + coat in one mesh, so the whole chair disappears when fixed.
            // Split the coat into its own mesh (or add an empty chair to swap in) if that looks wrong.
            Model("LabChair", "CoatChair", g, Vector3.zero, new Vector3(0.7f, 1.1f, 0.7f), Vector3.zero);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H04");
        }

        // H05 Used gloves and pipette tips in the general (domestic) waste bin
        {
            var g = Group("H05_WasteSeparation", p, new Vector3(-4.6f, 0, 2.95f));
            // unlike the other bins, this model's front (pedal + opening) is +Z
            Model("TrashBin", "Bin", g, Vector3.zero, new Vector3(0.4f, 0.6f, 0.4f), new Vector3(0, 90f, 0), out var bin);
            float d = bin.y * 0.8f - 0.51f;   // gloves were placed for a 0.51 m rim; follow the model's opening
            var glove = M("GloveBlue", new Color(0.2f, 0.42f, 0.95f));
            Box("Glove1", g, new Vector3(0.03f, 0.53f + d, 0.02f), new Vector3(0.18f, 0.03f, 0.1f), glove, new Vector3(0, 30f, 15f));
            Box("Glove2", g, new Vector3(-0.06f, 0.55f + d, -0.04f), new Vector3(0.17f, 0.03f, 0.09f), glove, new Vector3(10f, -40f, -20f));
            Box("Finger", g, new Vector3(0.11f, 0.58f + d, 0.05f), new Vector3(0.02f, 0.08f, 0.02f), glove, new Vector3(0, 0, -30f));
            var tip = M("TipYellow", new Color(0.95f, 0.9f, 0.5f));
            Cyl("Tip1", g, new Vector3(0.05f, 0.56f + d, -0.08f), 0.015f, 0.07f, tip);
            Cyl("Tip2", g, new Vector3(-0.08f, 0.55f + d, 0.07f), 0.015f, 0.07f, tip);
            Label("Text", g, "GENERAL WASTE", new Vector3(bin.z / 2f + 0.01f, bin.y * 0.45f, 0), Quaternion.LookRotation(Vector3.left), 0.3f, Color.white);
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H05"), "Glove1", "Glove2", "Finger", "Tip1", "Tip2");
        }

        // H06 Unlabeled amber bottle on the main bench
        {
            var g = Group("H06_UnlabeledBottle", p, new Vector3(0.2f, 0.9f, 0.5f));
            Model("AmberBottle", "Bottle", g, Vector3.zero, new Vector3(0.12f, 0.24f, 0.12f), Vector3.zero, 0.7f);
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H06");
        }

        // H07 Safety glasses unused next to active equipment + over-ear headphones
        {
            var g = Group("H07_PPEAndHeadphones", p, new Vector3(1.35f, 0.9f, 0.6f));
            Model("SafetyGlasses", "Glasses", g, new Vector3(-0.05f, 0, 0), new Vector3(0.17f, 0.06f, 0.17f), new Vector3(0, -20f, 0), 0.6f);
            Model("Earmuffs", "Headphones", g, new Vector3(0.22f, 0, 0), new Vector3(0.2f, 0.2f, 0.12f), new Vector3(0, 15f, 0));
            MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H07");
        }

        // H08 Frayed cable in an overloaded power strip, tucked behind the centrifuge
        {
            var g = Group("H08_DamagedCable", p, new Vector3(4.82f, 0.9f, 1.25f));
            Model("PowerStrip", "PowerStrip", g, Vector3.zero, new Vector3(0.36f, 0, 0), new Vector3(0, 90f, 0), 0.4f);
            // cable towards the centrifuge with an exposed section (the visible clue)
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
            // model's open front faces +Z; the room is -Z here
            var cab = Model("FirstAidCabinet", "Cabinet", g, new Vector3(0, 1.48f, 0), new Vector3(0, 0.55f, 0), new Vector3(0, 180f, 0), out var cabSize);
            cab.localPosition += new Vector3(0, 0, 0.08f - cabSize.z / 2f);   // back against the wall
            Label("Sign", g, "FIRST AID", new Vector3(0, 2.12f, 0.05f), Quaternion.identity, 0.45f, new Color(0.08f, 0.55f, 0.25f));
            // blocking shelf + bottles
            Box("BlockingShelf", g, new Vector3(0, 1.38f, -0.12f), new Vector3(0.7f, 0.025f, 0.26f), M("ShelfWhite", Color.white));
            for (int i = 0; i < 4; i++)
                Model("AmberBottle", "Bottle", g, new Vector3(-0.24f + i * 0.16f, 1.39f, -0.14f), new Vector3(0.1f, 0.26f, 0.1f), Vector3.zero, 0.7f);
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H09"), "BlockingShelf", "Bottle");
        }

        // H10 Corrosive HCl on the top (glass) shelf, next to flammable acetone
        {
            var g = Group("H10_ChemicalStorage", p, new Vector3(2.5f, 1.965f, 3.72f));
            Model("HClBottle", "HCl", g, new Vector3(-0.2f, 0, 0), new Vector3(0.14f, 0.27f, 0.14f), Vector3.zero, 0.6f);
            for (int i = 0; i < 2; i++)
                Model("AcetoneBottle", "Flammable", g, new Vector3(0.05f + i * 0.2f, 0, 0), new Vector3(0.13f, 0.24f, 0.13f), Vector3.zero, 0.6f);
            Hide(MakeInspectable(g.gameObject, ChemLabInspectable.Kind.Hazard, "H10"), "HCl");
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

    // ---------- imported models (Models/Hazards/<Model>/<Model>_low.fbx + textures)

    const string ModelDir = Root + "/Models/Hazards/";

    internal static Transform Model(string model, string name, Transform parent, Vector3 localPos, Vector3 fit, Vector3 euler, float smooth = 0.3f)
        => Model(model, name, parent, localPos, fit, euler, out _, smooth);

    /// <summary>
    /// Adds the model as child <paramref name="name"/>: uniformly scaled to fit inside <paramref name="fit"/>
    /// (metres, in the model's own axes; 0 = ignore that axis), bottom-centre at localPos, rotated by euler.
    /// <paramref name="size"/> is the fitted size in the model's axes.
    /// </summary>
    internal static Transform Model(string model, string name, Transform parent, Vector3 localPos, Vector3 fit, Vector3 euler, out Vector3 size, float smooth = 0.3f)
    {
        string dir = ModelDir + model + "/";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(dir + model + "_low.fbx") ?? AssetDatabase.LoadAssetAtPath<GameObject>(dir + model + ".fbx");
        var holder = Group(name, parent, localPos);
        holder.localRotation = Quaternion.Euler(euler);
        if (src == null) { Debug.LogError("[ChemLab] Missing model " + dir); size = Vector3.zero; return holder; }

        var inst = Object.Instantiate(src);
        inst.name = model;
        inst.transform.position = Vector3.zero;   // measure at the origin; keep the FBX's own root rotation (Meshy meshes are Z-up)
        var r = inst.GetComponentInChildren<Renderer>();
        var b = r.bounds;
        float k = float.MaxValue;
        for (int i = 0; i < 3; i++) if (fit[i] > 0f) k = Mathf.Min(k, fit[i] / b.size[i]);
        inst.transform.SetParent(holder, false);
        inst.transform.localScale *= k;
        inst.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z) * k;
        r.sharedMaterial = HazardMat(model, smooth);
        size = b.size * k;
        return holder;
    }

    /// <summary>Exit signs are lit: the sign's own texture as emission.</summary>
    static void ExitSignGlow()
    {
        var m = HazardMat("ExitSign", 0.3f);
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
        m.SetColor("_EmissionColor", Color.white * 0.8f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    /// <summary>URP Lit material next to the model, from its BaseColor / Normal / Metallic PNGs.</summary>
    static Material HazardMat(string model, float smooth)
    {
        string dir = ModelDir + model + "/", path = dir + model + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", ModelTex(dir, model, "_BaseColor"));
        var n = ModelTex(dir, model, "_Normal");
        if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        var mt = ModelTex(dir, model, "_Metallic");
        if (mt != null) { m.SetTexture("_MetallicGlossMap", mt); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        // ponytail: constant smoothness; the Meshy roughness maps would need inverting into the metallic alpha
        m.SetFloat("_Smoothness", smooth);
        if (create) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Finds Model+kind .png (any case; FirstAidCabinet.png is its base colour) and sets Quest-friendly import settings.</summary>
    static Texture2D ModelTex(string dir, string model, string kind)
    {
        var path = new[] { dir + model + kind + ".png", dir + model + kind.ToLowerInvariant() + ".png", kind == "_BaseColor" ? dir + model + ".png" : null }
            .FirstOrDefault(f => f != null && System.IO.File.Exists(f));
        if (path == null) return null;
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        bool dirty = false;
        if (kind == "_Normal" && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (kind == "_Metallic" && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
        var android = ti.GetPlatformTextureSettings("Android");
        if (!android.overridden || android.maxTextureSize != 1024)
        {
            android.overridden = true;
            android.maxTextureSize = 1024;   // ~40 maps at 2048 would eat Quest memory; these props are small
            ti.SetPlatformTextureSettings(android);
            dirty = true;
        }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            // Let URP derive the final blend state (Lit premultiplies when "preserve specular" is on -> SrcBlend One).
            // Without this every rebuild wrote SrcBlend 5 and Unity re-saved it as 1 later (commit b888c96).
            BaseShaderGUI.SetupMaterialBlendMode(m);
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
