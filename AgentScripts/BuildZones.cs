using QAXR.Demo;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off builder for Assets/Testing Scene/QAXR_Demo.unity (zones 1–3). Run with:
// unity command run_script --file AgentScripts/BuildZones.cs --entry BuildZones.Run
public static class BuildZones
{
    const string Dir = "Assets/Testing Scene";
    const string ScenePath = "Assets/_Project/Scenes/QAXR_Demo.unity";
    const string Mats = "Assets/_Project/Materials/";

    static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Mats + n + ".mat");

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Transform Root(string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.position = pos;
        return t;
    }

    static void Label(Transform parent, Vector3 pos, string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 3;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(8, 2);
    }

    public static string Run()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
        AssetDatabase.CopyAsset("Assets/_Project/Scenes/Empty.unity", ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Object.DestroyImmediate(GameObject.Find("Floor"));
        var origin = Object.FindFirstObjectByType<XROrigin>();
        origin.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);

        // Comfort vignette on the camera, idle until a provider (Zone 1 fixed) asks for it.
        var vignette = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab");
        PrefabUtility.InstantiatePrefab(vignette, origin.Camera.transform);

        Material floor = M("M_Floor_Dark"), wall = M("M_Brand_Grey"), accent = M("M_Coral"),
                 metal = M("M_Metal"), water = M("M_Brand_Blue"), glow = M("M_Stage_Glow"), wood = M("M_Podium");

        // Walkable track along +Z: start → Zone 1 → landing → Zone 2 → landing → Zone 3.
        var track = Root("Track", Vector3.zero);
        Box("Start", track, new Vector3(0, -0.1f, -1), new Vector3(6, 0.2f, 6), floor);
        Box("Landing1", track, new Vector3(0, -0.1f, 54), new Vector3(6, 0.2f, 4), floor);
        Box("Landing2", track, new Vector3(0, -0.1f, 72), new Vector3(6, 0.2f, 4), floor);

        // Zone 1: 50 m corridor, z 2..52. Pillars every 2 m give strong optic flow.
        var z1 = Root("Zone1_FastCorridor", new Vector3(0, 1.5f, 27));
        z1.gameObject.AddComponent<Zone1FastCorridor>();
        Box("Floor", z1, new Vector3(0, -1.6f, 0), new Vector3(3, 0.2f, 50), floor);
        Box("WallL", z1, new Vector3(-1.65f, 0, 0), new Vector3(0.3f, 3, 50), wall);
        Box("WallR", z1, new Vector3(1.65f, 0, 0), new Vector3(0.3f, 3, 50), wall);
        for (int i = 0; i < 25; i++)
        {
            var m = i % 2 == 0 ? accent : metal;
            float z = -24 + 2 * i;
            Box("PillarL" + i, z1, new Vector3(-1.45f, 0, z), new Vector3(0.15f, 3, 0.3f), m);
            Box("PillarR" + i, z1, new Vector3(1.45f, 0, z), new Vector3(0.15f, 3, 0.3f), m);
        }
        Label(z1, new Vector3(0, 2.2f, -25.2f), "ZONE 1 · FAST CORRIDOR\n<size=60%>bad: ramps to 4 m/s, no vignette · fixed: 1.2 m/s + vignette</size>");

        // Zone 2: rolling deck z 56.2..69.8 over water; landings at z 56 and 70.
        var z2 = Root("Zone2_RockingBoat", new Vector3(0, 0, 63));
        Box("Water", z2, new Vector3(0, -1.1f, 0), new Vector3(40, 0.2f, 24), water);
        var deck = new GameObject("Deck").transform;
        deck.SetParent(z2, false);
        deck.gameObject.AddComponent<Zone2RockingBoat>();
        Box("Hull", deck, new Vector3(0, -0.25f, 0), new Vector3(6, 0.5f, 13.6f), wood);
        Box("RailL", deck, new Vector3(-2.95f, 0.5f, 0), new Vector3(0.1f, 1, 13.6f), metal);
        Box("RailR", deck, new Vector3(2.95f, 0.5f, 0), new Vector3(0.1f, 1, 13.6f), metal);
        Box("Mast", deck, new Vector3(0, 2.5f, 2), new Vector3(0.2f, 5, 0.2f), metal);
        Label(z2, new Vector3(0, 3.2f, -7.2f), "ZONE 2 · ROCKING BOAT\n<size=60%>bad: 0.2 Hz roll, you ride the deck · fixed: calm, horizon level</size>");

        // Zone 3: room z 74..88 with a doorway; the cutscene plays when the head enters.
        var room = Root("Zone3_Room", new Vector3(0, 0, 81));
        Box("Floor", room, new Vector3(0, -0.1f, 0), new Vector3(12, 0.2f, 14), floor);
        Box("WallBack", room, new Vector3(0, 2, 7), new Vector3(12, 4, 0.3f), wall);
        Box("WallL", room, new Vector3(-6, 2, 0), new Vector3(0.3f, 4, 14), wall);
        Box("WallR", room, new Vector3(6, 2, 0), new Vector3(0.3f, 4, 14), wall);
        Box("WallFrontL", room, new Vector3(-3.5f, 2, -7), new Vector3(5, 4, 0.3f), wall);
        Box("WallFrontR", room, new Vector3(3.5f, 2, -7), new Vector3(5, 4, 0.3f), wall);
        Box("Stage", room, new Vector3(0, 0.15f, 5.5f), new Vector3(8, 0.3f, 2.5f), wood);
        Label(room, new Vector3(0, 4.6f, -7.2f), "ZONE 3 · CUTSCENE\n<size=60%>bad: script grabs camera + turns you · fixed: you keep control</size>");

        var z3 = Root("Zone3_Cutscene", new Vector3(0, 1.5f, 82));
        var cut = z3.gameObject.AddComponent<Zone3Cutscene>();
        var subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        subject.name = "CutsceneSubject";
        Object.DestroyImmediate(subject.GetComponent<Collider>());
        subject.transform.SetParent(z3, false);
        subject.transform.localScale = Vector3.one * 0.8f;
        subject.GetComponent<Renderer>().sharedMaterial = glow;
        subject.transform.localPosition = cut.from;
        cut.subject = subject.transform;

        EditorSceneManager.SaveScene(scene);

        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return "built " + ScenePath;
    }
}
