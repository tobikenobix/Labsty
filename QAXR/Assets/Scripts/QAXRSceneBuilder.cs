using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

/// <summary>
/// Builds Scenario 01 - VR Training Corridor from Unity primitives.
///
///   z = 0 .. 8    Zone A  Normal movement   (green)  supply crate obstacle at z = 5, poster + info screen landmarks
///   z = 8 .. 14   Zone B  Fault zone        (orange) fire extinguisher landmark
///   z = 14 .. 23  Zone C  Interaction zone  (blue)   plant landmark, control panel at z = 19, exit door at z = 23.5
///
/// Agent hierarchy (D2 motion sources):
///   AgentRoot -> Body -> HeadPivot -> ApplicationCameraOffset -> AgentCamera
///
/// No external assets, XR packages or NavMesh required.
/// </summary>
public static class QAXRSceneBuilder
{
    public const string RootName = "QAXR | Test Environment";

    private const float HalfWidth = 2.1f;
    private const float CorridorStart = -1.5f;
    private const float CorridorEnd = 23.5f;
    private const int DebugLayer = 2; // Ignore Raycast

    public static GameObject Build()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null) return existing;

        GameObject root = new GameObject(RootName);
        Transform env = new GameObject("Environment").transform;
        env.SetParent(root.transform);

        float len = CorridorEnd - CorridorStart;
        float mid = (CorridorStart + CorridorEnd) * 0.5f;

        // ---------------- Floor and zones ----------------
        CreateBox("Ground", env, new Vector3(0f, -0.16f, mid), new Vector3(7f, 0.25f, len + 2f), Hex("#172236"));
        CreateBox("Entrance", env, new Vector3(0f, -0.004f, -0.75f), new Vector3(2 * HalfWidth, 0.1f, 1.5f), Hex("#3A4A60"));
        CreateBox("Zone A | Normal Movement", env, new Vector3(0f, -0.005f, 4f), new Vector3(2 * HalfWidth, 0.1f, 8f), Hex("#247660"));
        CreateBox("Zone B | Fault Zone", env, new Vector3(0f, 0f, 11f), new Vector3(2 * HalfWidth, 0.11f, 6f), Hex("#B67A33"));
        CreateBox("Zone C | Interaction Zone", env, new Vector3(0f, -0.005f, 18.75f), new Vector3(2 * HalfWidth, 0.1f, 9.5f), Hex("#266A85"));

        // ---------------- Walls ----------------
        Color wall = Hex("#2B3B50");
        CreateBox("Left Wall", env, new Vector3(-HalfWidth - 0.14f, 1.65f, mid), new Vector3(0.28f, 3.3f, len), wall);
        CreateBox("Right Wall", env, new Vector3(HalfWidth + 0.14f, 1.65f, mid), new Vector3(0.28f, 3.3f, len), wall);
        CreateBox("Back Wall", env, new Vector3(0f, 1.65f, CorridorStart), new Vector3(2 * HalfWidth + 0.56f, 3.3f, 0.25f), wall);
        CreateBox("End Wall", env, new Vector3(0f, 1.65f, CorridorEnd), new Vector3(2 * HalfWidth + 0.56f, 3.3f, 0.25f), wall);
        GameObject door = CreateBox("Exit Door", env, new Vector3(0f, 1.1f, CorridorEnd - 0.14f), new Vector3(1.2f, 2.2f, 0.06f), Hex("#3FBF7F"));
        AddPerceivable(door, QAXRPerceivableKind.Exit, "Exit door", 0.6f);

        for (int i = 0; i < 7; i++)
        {
            float z = 8.4f + 0.85f * i;
            CreateBox("Warning stripe L" + i, env, new Vector3(-1.8f, 0.07f, z), new Vector3(0.32f, 0.015f, 0.2f), Hex("#FFD27D"));
            CreateBox("Warning stripe R" + i, env, new Vector3(1.8f, 0.07f, z), new Vector3(0.32f, 0.015f, 0.2f), Hex("#FFD27D"));
        }
        for (int i = 0; i < 5; i++)
        {
            float z = 1f + i * 5f;
            CreateBox("Wall light " + i, env, new Vector3(-HalfWidth + 0.02f, 2.4f, z), new Vector3(0.03f, 0.25f, 0.8f),
                      Hex(z > 8f && z < 14f ? "#D69A46" : "#6087A6"));
        }

        // ---------------- Obstacle (requires route adjustment) ----------------
        GameObject crate = CreateBox("Obstacle | Supply Crate", env, new Vector3(0.15f, 0.5f, 5f), new Vector3(0.9f, 1.0f, 0.9f), Hex("#8B6B3E"));
        AddPerceivable(crate, QAXRPerceivableKind.Obstacle, "Supply crate", 0.65f);

        // ---------------- Landmarks (inspectable) ----------------
        GameObject poster = CreateBox("Landmark | Safety Poster", env, new Vector3(-HalfWidth + 0.03f, 1.6f, 2.6f), new Vector3(0.04f, 0.9f, 0.7f), Hex("#E8C547"));
        AddPerceivable(poster, QAXRPerceivableKind.Landmark, "Safety poster", 0.1f);
        GameObject screen = CreateBox("Landmark | Info Screen", env, new Vector3(HalfWidth - 0.03f, 1.7f, 6.8f), new Vector3(0.05f, 0.7f, 1.1f), Hex("#5DC4DD"));
        AddPerceivable(screen, QAXRPerceivableKind.Landmark, "Info screen", 0.1f);
        GameObject ext = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ext.name = "Landmark | Fire Extinguisher";
        ext.transform.SetParent(env);
        ext.transform.position = new Vector3(HalfWidth - 0.15f, 0.45f, 10.5f);
        ext.transform.localScale = new Vector3(0.18f, 0.3f, 0.18f);
        ext.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex("#D63A3A"));
        AddPerceivable(ext, QAXRPerceivableKind.Landmark, "Fire extinguisher", 0.12f);
        GameObject plant = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        plant.name = "Landmark | Plant";
        plant.transform.SetParent(env);
        plant.transform.position = new Vector3(-HalfWidth + 0.35f, 0.55f, 17.2f);
        plant.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);
        plant.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex("#3C9A52"));
        AddPerceivable(plant, QAXRPerceivableKind.Landmark, "Plant", 0.3f);

        // ---------------- Control panel (Zone C, right wall) ----------------
        GameObject panelObj = CreateBox("Control Panel", env, new Vector3(HalfWidth - 0.08f, 1.2f, 19f), new Vector3(0.12f, 0.9f, 1.1f), Hex("#4A5568"));
        CreateBox("Panel Stand", env, new Vector3(HalfWidth - 0.1f, 0.38f, 19f), new Vector3(0.1f, 0.75f, 0.3f), Hex("#2B3B50"));
        GameObject buttonObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        buttonObj.name = "Panel Button";
        buttonObj.transform.SetParent(env);
        buttonObj.transform.position = new Vector3(HalfWidth - 0.16f, 1.25f, 19f);
        buttonObj.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        buttonObj.transform.localScale = new Vector3(0.18f, 0.03f, 0.18f);
        Renderer buttonRenderer = buttonObj.GetComponent<Renderer>();
        buttonRenderer.sharedMaterial = MaterialWithColor(Color.white);
        DisableCollider(buttonObj);
        Transform approach = new GameObject("Panel Approach Point").transform;
        approach.SetParent(env);
        approach.position = new Vector3(1.15f, 0f, 19f);
        QAXRControlPanel panel = panelObj.AddComponent<QAXRControlPanel>();
        panel.button = buttonObj.transform;
        panel.buttonRenderer = buttonRenderer;
        QAXRPerceivable panelP = AddPerceivable(panelObj, QAXRPerceivableKind.ControlPanel, "Control panel", 0.15f);
        panelP.attentionPoint = buttonObj.transform;
        panelP.approachPoint = approach;
        panelP.interactionRange = 0.95f;

        // ---------------- Mission (Layer A task definition) ----------------
        var mission = new List<QAXRSubgoal>
        {
            new QAXRSubgoal { name = "Enter corridor", type = QAXRSubgoalType.NavigateToTarget, position = new Vector3(0f, 0f, 2f), completeWhenPassedZ = true, timeout = 30f },
            new QAXRSubgoal { name = "Navigate through Zone A", type = QAXRSubgoalType.NavigateToTarget, position = new Vector3(0f, 0f, 7.3f), completeWhenPassedZ = true, timeout = 40f },
            new QAXRSubgoal { name = "Cross Zone B", type = QAXRSubgoalType.NavigateToTarget, position = new Vector3(0f, 0f, 13.8f), completeWhenPassedZ = true, timeout = 40f },
            new QAXRSubgoal { name = "Find the control panel", type = QAXRSubgoalType.ObserveTarget, position = new Vector3(0.4f, 0f, 16.8f), target = panelP, timeout = 40f },
            new QAXRSubgoal { name = "Approach the control panel", type = QAXRSubgoalType.ApproachTarget, target = panelP, timeout = 40f },
            new QAXRSubgoal { name = "Press the button", type = QAXRSubgoalType.InteractWithTarget, target = panelP, timeout = 30f },
            new QAXRSubgoal { name = "Reach the exit", type = QAXRSubgoalType.NavigateToExit, position = new Vector3(0f, 0f, 22.4f), completionRadius = 0.35f, timeout = 40f },
        };
        Transform wpRoot = new GameObject("Waypoints (mission)").transform;
        wpRoot.SetParent(root.transform);
        foreach (QAXRSubgoal g in mission)
        {
            if (g.type == QAXRSubgoalType.InteractWithTarget) continue;
            Vector3 p = g.type == QAXRSubgoalType.ApproachTarget ? approach.position : g.position;
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m.name = "WP " + g.name;
            m.transform.SetParent(wpRoot);
            m.transform.position = p + new Vector3(0f, 0.06f, 0f);
            m.transform.localScale = new Vector3(0.3f, 0.01f, 0.3f);
            m.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex(g.type == QAXRSubgoalType.NavigateToTarget ? "#9FB3C8" : "#FFFFFF"));
            DisableCollider(m);
            AddPerceivable(m, QAXRPerceivableKind.Waypoint, "Waypoint " + g.name, 0.15f);
        }

        MakeText(env, "ZONE A - NORMAL MOVEMENT\nObstacle + landmarks", new Vector3(-1.9f, 2.9f, 1.0f), 0.3f, Color.white);
        MakeText(env, "ZONE B - FAULT ZONE\nInjected forced roll", new Vector3(-1.9f, 2.9f, 9f), 0.3f, Hex("#FFD59B"));
        MakeText(env, "ZONE C - INTERACTION\nFind panel, press, exit", new Vector3(-1.9f, 2.9f, 15.5f), 0.3f, Color.white);
        MakeText(env, "EXIT", new Vector3(-1.9f, 2.9f, 22.2f), 0.4f, Hex("#3FBF7F"));

        // ---------------- Agent ----------------
        QAXRAgentController agent = BuildAgent(root.transform, "Simulated Agent A01", new Vector3(0f, 0f, 0.5f), mission);

        // ---------------- Cameras, light, marker ----------------
        GameObject overviewObj = new GameObject("Overview Camera");
        overviewObj.transform.SetParent(root.transform);
        overviewObj.transform.position = new Vector3(13f, 12f, 4f);
        overviewObj.transform.LookAt(new Vector3(0f, 0f, 11.5f));
        overviewObj.tag = "MainCamera";
        Camera overview = overviewObj.AddComponent<Camera>();
        overview.fieldOfView = 55f;
        overview.clearFlags = CameraClearFlags.SolidColor;
        overview.backgroundColor = Hex("#0E1827");
        overviewObj.AddComponent<AudioListener>();

        GameObject sunObj = new GameObject("Directional Light");
        sunObj.transform.SetParent(root.transform);
        sunObj.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        Light sun = sunObj.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "FLAGGED ISSUE marker";
        marker.transform.SetParent(root.transform);
        marker.transform.position = new Vector3(0f, 2.4f, 9f);
        marker.transform.localScale = Vector3.one * 0.55f;
        marker.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex("#EE534F"));
        DisableCollider(marker);
        marker.SetActive(false);
        agent.GetComponent<QAXRRiskDetector>().issueMarker = marker;

        // ---------------- Controllers ----------------
        GameObject ctrl = new GameObject("QAXR Scenario Controller");
        ctrl.transform.SetParent(root.transform);
        QAXRScenarioController scenario = ctrl.AddComponent<QAXRScenarioController>();
        scenario.primaryAgent = agent;
        scenario.overviewCamera = overview;
        scenario.controlPanel = panel;
        scenario.debugVisualizer = ctrl.AddComponent<QAXRDebugVisualizer>();

        // ---------------- VR viewer (Meta Quest 3) ----------------
        CreateBox("VR Observer Platform", env, new Vector3(4.7f, 1.0f, 10f), new Vector3(2.4f, 2.0f, 3.0f), Hex("#3A4A60"));
        CreateBox("VR Observer Platform Rail", env, new Vector3(3.55f, 2.5f, 10f), new Vector3(0.06f, 1.0f, 3.0f), Hex("#6087A6"));
        Transform obsPose = new GameObject("VR Pose | Observer Platform").transform;
        obsPose.SetParent(root.transform);
        obsPose.SetPositionAndRotation(new Vector3(4.4f, 2.0f, 10f), Quaternion.Euler(0f, -90f, 0f));
        Transform fzPose = new GameObject("VR Pose | Fault Zone Side").transform;
        fzPose.SetParent(root.transform);
        fzPose.SetPositionAndRotation(new Vector3(-1.6f, 0f, 11f), Quaternion.Euler(0f, 90f, 0f));
        BuildVRRig(root.transform, scenario, obsPose, fzPose);

        return root;
    }

    /// <summary>VR observer rig: tracked head camera, controllers, wrist panel, agent-view monitor, fade.</summary>
    public static QAXRVRRig BuildVRRig(Transform parent, QAXRScenarioController scenario, Transform observerPose, Transform faultZonePose)
    {
        GameObject rigObj = new GameObject("VR Viewer Rig (Meta Quest 3)");
        rigObj.transform.SetParent(parent);
        rigObj.transform.SetPositionAndRotation(observerPose.position, observerPose.rotation);

        GameObject camObj = new GameObject("VR Camera (head)");
        camObj.transform.SetParent(rigObj.transform, false);
        camObj.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Camera cam = camObj.AddComponent<Camera>();
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 200f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex("#0E1827");
        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        cam.enabled = false;

        GameObject fade = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fade.name = "Comfort Fade";
        fade.transform.SetParent(camObj.transform, false);
        fade.transform.localPosition = new Vector3(0f, 0f, 0.08f);
        fade.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        DisableCollider(fade);
        Renderer fadeR = fade.GetComponent<Renderer>();
        fadeR.sharedMaterial = SavedMaterial("QAXR_VR_Fade", "Sprites/Default", new Color(0f, 0f, 0f, 0f));
        fadeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fadeR.enabled = false;

        // Left controller: monitor (agent view) + wrist panel
        Transform left = new GameObject("Left Controller").transform;
        left.SetParent(rigObj.transform, false);
        left.localPosition = new Vector3(-0.2f, 1.1f, 0.3f);
        ControllerVisual(left, Hex("#5DC4DD"));
        Quaternion tilt = Quaternion.Euler(25f, 0f, 0f);

        GameObject monitor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        monitor.name = "Agent View Monitor";
        monitor.transform.SetParent(left, false);
        monitor.transform.localPosition = new Vector3(0f, 0.11f, 0.06f);
        monitor.transform.localRotation = tilt;
        monitor.transform.localScale = new Vector3(0.24f, 0.18f, 1f);
        DisableCollider(monitor);
        Renderer monR = monitor.GetComponent<Renderer>();
        monR.sharedMaterial = SavedMaterial("QAXR_VR_Monitor", "Unlit/Texture", Color.white);
        monR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        TextMesh monLabel = MakeSmallText(left, "Agent view", new Vector3(-0.12f, 0.215f, 0.085f), tilt, 0.0014f, Color.white);

        GameObject panelBg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panelBg.name = "Wrist Panel Background";
        panelBg.transform.SetParent(left, false);
        panelBg.transform.localPosition = new Vector3(0.265f, 0.11f, 0.062f);
        panelBg.transform.localRotation = tilt;
        panelBg.transform.localScale = new Vector3(0.27f, 0.2f, 1f);
        DisableCollider(panelBg);
        panelBg.GetComponent<Renderer>().sharedMaterial = SavedMaterial("QAXR_VR_PanelBg", "Unlit/Color", Hex("#0B1422"));
        TextMesh wrist = MakeSmallText(left, "QAXR", new Vector3(0.14f, 0.2f, 0.058f), tilt, 0.0013f, Color.white);

        Transform right = new GameObject("Right Controller").transform;
        right.SetParent(rigObj.transform, false);
        right.localPosition = new Vector3(0.2f, 1.1f, 0.3f);
        ControllerVisual(right, Hex("#E8C547"));

        GameObject monCamObj = new GameObject("VR Agent Monitor Camera");
        monCamObj.transform.SetParent(parent);
        Camera monCam = monCamObj.AddComponent<Camera>();
        monCam.fieldOfView = 85f;
        monCam.nearClipPlane = 0.06f;
        monCam.clearFlags = CameraClearFlags.SolidColor;
        monCam.backgroundColor = Hex("#0E1827");
        monCam.cullingMask &= ~(1 << DebugLayer);
        monCam.stereoTargetEye = StereoTargetEyeMask.None;
        monCam.enabled = false;

        SetLayerRecursive(rigObj, DebugLayer);

        QAXRVRRig rig = rigObj.AddComponent<QAXRVRRig>();
        rig.scenario = scenario;
        rig.vrCamera = cam;
        rig.leftHand = left;
        rig.rightHand = right;
        rig.observerPose = observerPose;
        rig.faultZonePose = faultZonePose;
        rig.wristText = wrist;
        rig.monitorRenderer = monR;
        rig.monitorLabel = monLabel;
        rig.monitorCamera = monCam;
        rig.fadeRenderer = fadeR;
        return rig;
    }

    private static void ControllerVisual(Transform hand, Color c)
    {
        GameObject v = GameObject.CreatePrimitive(PrimitiveType.Cube);
        v.name = "Controller Visual";
        v.transform.SetParent(hand, false);
        v.transform.localPosition = new Vector3(0f, -0.01f, -0.03f);
        v.transform.localScale = new Vector3(0.045f, 0.035f, 0.12f);
        v.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(c);
        DisableCollider(v);
    }

    private static TextMesh MakeSmallText(Transform parent, string text, Vector3 localPos, Quaternion localRot, float charSize, Color color)
    {
        GameObject go = new GameObject("Text " + text);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        TextMesh t = go.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.font = font;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        t.text = text;
        t.fontSize = 64;
        t.characterSize = charSize;
        t.anchor = TextAnchor.UpperLeft;
        t.color = color;
        return t;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    private static Material SavedMaterial(string name, string shaderName, Color color)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Unlit/Color");
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            const string folder = "Assets/QAXR_Materials";
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = folder + "/" + name + ".mat";
            Material saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (saved != null) return saved;
            Material m = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
#endif
        return new Material(shader) { color = color };
    }

    /// <summary>AgentRoot -> Body -> HeadPivot -> ApplicationCameraOffset -> AgentCamera, plus collectors.</summary>
    public static QAXRAgentController BuildAgent(Transform parent, string name, Vector3 position, List<QAXRSubgoal> mission)
    {
        GameObject agentObj = new GameObject(name);
        agentObj.transform.SetParent(parent);
        agentObj.transform.position = position;

        Transform body = new GameObject("Body").transform;
        body.SetParent(agentObj.transform, false);

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Body Visual";
        visual.transform.SetParent(body, false);
        visual.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        visual.transform.localScale = new Vector3(0.45f, 0.8f, 0.45f);
        visual.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex("#5DC4DD"));
        DisableCollider(visual);

        Transform head = new GameObject("HeadPivot (simulated head pose, not eye gaze)").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.5f, 0f);

        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor (head forward)";
        visor.transform.SetParent(head, false);
        visor.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        visor.transform.localScale = new Vector3(0.3f, 0.12f, 0.12f);
        visor.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(Hex("#0E1827"));
        DisableCollider(visor);

        Transform appOffset = new GameObject("ApplicationCameraOffset (app-controlled motion)").transform;
        appOffset.SetParent(head, false);

        GameObject camObj = new GameObject("AgentCamera");
        camObj.transform.SetParent(appOffset, false);
        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 85f;
        cam.nearClipPlane = 0.06f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex("#0E1827");
        cam.cullingMask &= ~(1 << DebugLayer);
        cam.enabled = false;
        camObj.AddComponent<AudioListener>().enabled = false;

        QAXRAgentController agent = agentObj.AddComponent<QAXRAgentController>();
        agent.body = body;
        agent.headPivot = head;
        agent.appCameraOffset = appOffset;
        agent.agentCamera = cam;
        agent.bodyRenderers = new[] { visual.GetComponent<Renderer>(), visor.GetComponent<Renderer>() };
        agent.mission = mission;
        agent.selfPerceivable = AddPerceivable(agentObj, QAXRPerceivableKind.Agent, name, 0.3f);
        agentObj.AddComponent<QAXRMetricsCollector>();
        agentObj.AddComponent<QAXRRiskDetector>();
        return agent;
    }

    private static QAXRPerceivable AddPerceivable(GameObject go, QAXRPerceivableKind kind, string displayName, float radius)
    {
        QAXRPerceivable p = go.AddComponent<QAXRPerceivable>();
        p.kind = kind;
        p.displayName = displayName;
        p.radius = radius;
        return p;
    }

    private static void DisableCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
        }
    }

    private static void MakeText(Transform parent, string message, Vector3 position, float size, Color color)
    {
        GameObject label = new GameObject("Label: " + message.Split('\n')[0]);
        label.transform.SetParent(parent);
        label.transform.position = position;
        label.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        TextMesh text = label.AddComponent<TextMesh>();
        text.text = message;
        text.fontSize = 54;
        text.characterSize = size / 10f;
        text.anchor = TextAnchor.MiddleLeft;
        text.color = color;
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent);
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = MaterialWithColor(color);
        return cube;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static Material MaterialWithColor(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            const string folder = "Assets/QAXR_Materials";
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            Color32 c = color;
            string path = folder + "/QAXR_" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + ".mat";
            Material saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (saved != null) return saved;
            Material newSaved = new Material(shader);
            if (newSaved.HasProperty("_Color")) newSaved.SetColor("_Color", color);
            if (newSaved.HasProperty("_BaseColor")) newSaved.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(newSaved, path);
            return newSaved;
        }
#endif
        Material material = new Material(shader);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        return material;
    }
}
