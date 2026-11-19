using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// VR viewer for Meta Quest 3 (OpenXR via Quest Link / Air Link in Play mode, or a standalone Android build).
/// The viewer is an OBSERVER of the test: it has no perceivable component, no colliders, lives on the
/// "Ignore Raycast" layer and is never seen by agent cameras or perception, so it cannot affect agents or measurements.
///
/// Views (A = next, B = previous):
///   1 Observer platform   elevated, outside the corridor, whole test visible (most comfortable)
///   2 Fault zone side     standing inside Zone B next to the path
///   3 Follow agent        beside the selected agent; re-positions in comfort "snaps" with a fade
///   4 Ride-along          moves continuously with the agent (no rotation, injected roll NOT applied by default)
///
/// Left controller: wrist status/help panel + optional "agent view" monitor that shows the agent camera
/// (including the injected roll) on a small screen instead of in the headset view.
///
/// If no headset is active the rig disables itself and the desktop view works as before.
/// </summary>
public class QAXRVRRig : MonoBehaviour
{
    public enum View { ObserverPlatform, FaultZoneSide, FollowAgent, RideAlong }

    [Header("References")]
    public QAXRScenarioController scenario;
    public Camera vrCamera;
    public Transform leftHand, rightHand;
    public Transform observerPose, faultZonePose;
    public TextMesh wristText;
    public Renderer monitorRenderer;
    public TextMesh monitorLabel;
    public Camera monitorCamera;
    public Renderer fadeRenderer;

    [Header("Behaviour")]
    public View startView = View.ObserverPlatform;
    public Vector3 followOffset = new Vector3(-1.3f, 0f, -1.4f);
    [Tooltip("Follow view re-positions when the agent is this far (m) from the last placement.")]
    public float followSnapDistance = 2.5f;
    public float fadeSeconds = 0.15f;
    [Tooltip("Ride-along also turns with the agent's body. Off by default for comfort.")]
    public bool rideAlongFollowsBodyYaw = false;
    [Tooltip("WARNING: applies the injected forced roll to YOUR view in ride-along. Likely to cause discomfort. Off by default.")]
    public bool applyInjectedRollInRideAlong = false;
    public int monitorResolution = 512;

    public bool VrActive { get; private set; }
    public View CurrentView { get; private set; }

    private bool showMonitor = true;
    private bool showHelp = false;
    private bool fading;
    private Vector3 followAnchor;
    private Vector3 rideHeadOffset;
    private float rideYaw;
    private RenderTexture monitorRT;
    private Material fadeMat;
    private readonly Dictionary<string, bool> lastButtons = new Dictionary<string, bool>();
    private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
    private string notice = "";
    private float noticeUntil;

    private const int RigLayer = 2; // Ignore Raycast: excluded from agent cameras and perception

    private IEnumerator Start()
    {
        SetActiveVisuals(false);
        if (vrCamera != null) vrCamera.enabled = false;
        if (monitorCamera != null) monitorCamera.enabled = false;

        // XR may need a few frames to start.
        float t = 0f;
        while (!XRSettings.isDeviceActive && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
        if (!XRSettings.isDeviceActive)
        {
            Debug.Log("QAXR VR: no headset active - desktop mode. (Connect Quest 3 via Meta Quest Link and press Play.)");
            enabled = false;
            yield break;
        }

        VrActive = true;
        Debug.Log("QAXR VR: headset active (" + XRSettings.loadedDeviceName + "). A/B = switch view, X = agent monitor, Y = help.");
        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var s in inputSubsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);

        // Only the VR camera renders to the headset. Other cameras keep working for the desktop window.
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c != vrCamera) c.stereoTargetEye = StereoTargetEyeMask.None;
        vrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        vrCamera.enabled = true;

        monitorRT = new RenderTexture(monitorResolution, monitorResolution * 3 / 4, 16) { name = "QAXR Agent Monitor" };
        if (monitorCamera != null) monitorCamera.targetTexture = monitorRT;
        if (monitorRenderer != null) monitorRenderer.material.mainTexture = monitorRT;
        if (fadeRenderer != null) { fadeMat = fadeRenderer.material; SetFade(0f); }

        SetActiveVisuals(true);
        Application.onBeforeRender += UpdateTracking;
        UpdateTracking();
        SetView(startView, false);
    }

    private void OnDestroy()
    {
        Application.onBeforeRender -= UpdateTracking;
        if (monitorRT != null) monitorRT.Release();
    }

    private void Update()
    {
        if (!VrActive) return;
        UpdateTracking();
        HandleInput();
    }

    private void LateUpdate()
    {
        if (!VrActive) return;
        QAXRAgentController a = SelectedAgent();

        if (CurrentView == View.FollowAgent && a != null && !fading)
        {
            Vector3 p = a.transform.position;
            if (Vector3.Distance(Flat(p), Flat(followAnchor)) > followSnapDistance) StartCoroutine(FadeTo(View.FollowAgent));
        }
        if (CurrentView == View.RideAlong && a != null && !fading)
        {
            float yaw = rideAlongFollowsBodyYaw && a.body != null ? a.body.eulerAngles.y : rideYaw;
            float roll = applyInjectedRollInRideAlong && a.appCameraOffset != null ? Mathf.DeltaAngle(0f, a.appCameraOffset.localEulerAngles.z) : 0f;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(roll, Vector3.forward);
            Vector3 head = a.headPivot != null ? a.headPivot.position : a.transform.position + Vector3.up * 1.5f;
            Vector3 target = new Vector3(head.x, a.transform.position.y, head.z);
            transform.SetPositionAndRotation(target - r * rideHeadOffset, r);
        }

        // Agent monitor: copy the selected agent camera (includes the application-controlled roll).
        if (monitorCamera != null && a != null && a.agentCamera != null)
        {
            bool on = showMonitor && !showHelp;
            monitorCamera.enabled = on;
            if (monitorRenderer != null) monitorRenderer.enabled = on;
            if (monitorLabel != null) monitorLabel.gameObject.SetActive(on);
            if (on)
            {
                Transform src = a.agentCamera.transform;
                monitorCamera.transform.SetPositionAndRotation(src.position, src.rotation);
                monitorCamera.fieldOfView = a.agentCamera.fieldOfView;
                if (monitorLabel != null) monitorLabel.text = "Agent view " + a.agentId + " (incl. injected roll)";
            }
        }
        UpdateWristText(a);
    }

    // ------------------------------------------------------------------ tracking

    private void UpdateTracking()
    {
        Pose(XRNode.Head, vrCamera != null ? vrCamera.transform : null);
        Pose(XRNode.LeftHand, leftHand);
        Pose(XRNode.RightHand, rightHand);
    }

    private static void Pose(XRNode node, Transform t)
    {
        if (t == null) return;
        InputDevice d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid) return;
        if (node == XRNode.Head && d.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 eye)) t.localPosition = eye;
        else if (d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)) t.localPosition = p;
        if (node == XRNode.Head && d.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion er)) t.localRotation = er;
        else if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion q)) t.localRotation = q;
    }

    // ------------------------------------------------------------------ input

    private void HandleInput()
    {
        if (scenario == null) return;
        // Right controller
        if (Down(XRNode.RightHand, CommonUsages.primaryButton, "A") || Key(KeyCode.V)) NextView(+1);
        if (Down(XRNode.RightHand, CommonUsages.secondaryButton, "B")) NextView(-1);
        if (Down(XRNode.RightHand, CommonUsages.triggerButton, "RT") || Key(KeyCode.F)) { scenario.RunSingle(true); Notice("Fault run started"); }
        if (Down(XRNode.RightHand, CommonUsages.gripButton, "RG") || Key(KeyCode.N)) { scenario.SelectNextAgent(); Notice("Selected next agent"); }
        if (Down(XRNode.RightHand, CommonUsages.primary2DAxisClick, "RS") || Key(KeyCode.P)) { scenario.StartABPair(); Notice("A/B pair started"); }
        // Left controller
        if (Down(XRNode.LeftHand, CommonUsages.primaryButton, "X") || Key(KeyCode.M)) { showMonitor = !showMonitor; showHelp = false; }
        if (Down(XRNode.LeftHand, CommonUsages.secondaryButton, "Y") || Key(KeyCode.H)) showHelp = !showHelp;
        if (Down(XRNode.LeftHand, CommonUsages.triggerButton, "LT") || Key(KeyCode.B)) { scenario.RunSingle(false); Notice("Baseline run started"); }
        if (Down(XRNode.LeftHand, CommonUsages.gripButton, "LG")) { scenario.TogglePause(); Notice(scenario.IsPaused ? "Paused" : "Resumed"); }
        if (Down(XRNode.LeftHand, CommonUsages.primary2DAxisClick, "LS") || Key(KeyCode.C)) { scenario.CycleProfile(); Notice("Profile: " + scenario.profile); }
    }

    private bool Down(XRNode node, InputFeatureUsage<bool> usage, string id)
    {
        bool now = false;
        InputDevice d = InputDevices.GetDeviceAtXRNode(node);
        if (d.isValid) d.TryGetFeatureValue(usage, out now);
        lastButtons.TryGetValue(id, out bool before);
        lastButtons[id] = now;
        return now && !before;
    }

    private static bool Key(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(k);
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------ views

    public void NextView(int dir)
    {
        int n = System.Enum.GetValues(typeof(View)).Length;
        SetView((View)(((int)CurrentView + dir + n) % n), true);
    }

    public void SetView(View v, bool fade)
    {
        if (fade && isActiveAndEnabled) StartCoroutine(FadeTo(v));
        else ApplyView(v);
    }

    private IEnumerator FadeTo(View v)
    {
        fading = true;
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime) { SetFade(t / fadeSeconds); yield return null; }
        SetFade(1f);
        ApplyView(v);
        yield return null;
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime) { SetFade(1f - t / fadeSeconds); yield return null; }
        SetFade(0f);
        fading = false;
    }

    private void ApplyView(View v)
    {
        bool changed = v != CurrentView;
        CurrentView = v;
        QAXRAgentController a = SelectedAgent();
        switch (v)
        {
            case View.ObserverPlatform:
                if (observerPose != null) PlaceHead(observerPose.position, observerPose.eulerAngles.y);
                break;
            case View.FaultZoneSide:
                if (faultZonePose != null) PlaceHead(faultZonePose.position, faultZonePose.eulerAngles.y);
                break;
            case View.FollowAgent:
                if (a != null)
                {
                    Vector3 p = a.transform.position + followOffset;
                    p.x = Mathf.Clamp(p.x, -a.corridorHalfWidth + 0.3f, a.corridorHalfWidth - 0.3f);
                    p.y = 0f;
                    Vector3 look = a.transform.position + Vector3.forward * 1.5f - p;
                    PlaceHead(p, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg);
                    followAnchor = a.transform.position;
                }
                break;
            case View.RideAlong:
                rideYaw = 0f; // face along the corridor
                Vector3 hl = vrCamera.transform.localPosition;
                float headYaw = vrCamera.transform.localEulerAngles.y;
                // Keep the user's current facing: rotate rig so the head faces +z, then lock rig yaw.
                rideYaw = -headYaw;
                rideHeadOffset = new Vector3(hl.x, 0f, hl.z);
                if (changed) Notice("Ride-along: you move with the agent. Stop if uncomfortable.\nInjected roll is NOT applied to your view.", 5f);
                break;
        }
        if (changed && v != View.RideAlong) Notice("View: " + ViewName(v));
    }

    /// <summary>Place the rig so the user's head stands at floorPoint facing yaw (deg).</summary>
    private void PlaceHead(Vector3 floorPoint, float yaw)
    {
        Vector3 hl = vrCamera.transform.localPosition;
        float headYaw = vrCamera.transform.localEulerAngles.y;
        Quaternion r = Quaternion.Euler(0f, yaw - headYaw, 0f);
        Vector3 off = r * new Vector3(hl.x, 0f, hl.z);
        transform.SetPositionAndRotation(new Vector3(floorPoint.x - off.x, floorPoint.y, floorPoint.z - off.z), r);
    }

    // ------------------------------------------------------------------ UI

    private void UpdateWristText(QAXRAgentController a)
    {
        if (wristText == null || scenario == null) return;
        if (showHelp)
        {
            wristText.text =
                "QAXR VR CONTROLS\n" +
                "A / B      next / previous view\n" +
                "X          agent-view monitor on/off\n" +
                "Y          this help on/off\n" +
                "L trigger  run Baseline\n" +
                "R trigger  run Fault (injected roll)\n" +
                "R stick    press: A/B pair\n" +
                "L stick    press: change profile\n" +
                "R grip     next agent   L grip  pause\n" +
                "Keyboard: V view, M monitor, H help,\nB baseline, F fault, P pair, N agent, C profile";
            return;
        }
        var r = scenario.Selected;
        string s = "View: " + ViewName(CurrentView) + "\n" +
                   scenario.ConditionLabel + (scenario.Running ? (scenario.IsPaused ? "  PAUSED" : "  running") : "  finished") +
                   "  t=" + scenario.SimTime.ToString("F1") + "s\n";
        if (a != null && r != null)
        {
            s += a.agentId + " " + (a.Profile != null ? a.Profile.profileName : "") + "  zone " + r.zone + "\n" +
                 a.SubgoalLabel + "\n" +
                 "Action: " + a.Action + "  head: " + a.AttentionName + "\n" +
                 "Fault: " + (r.faultActive ? "ACTIVE" : "off") + "  risk: " + (r.detector.RiskActive ? "FLAGGED" : "-") +
                 "  episodes " + r.detector.Episodes.Count + "\n";
        }
        if (Time.unscaledTime < noticeUntil) s += ">> " + notice + "\n";
        s += "(Y = help)";
        wristText.text = s;
    }

    private void Notice(string text, float seconds = 2.5f) { notice = text; noticeUntil = Time.unscaledTime + seconds; }

    private static string ViewName(View v)
    {
        switch (v)
        {
            case View.ObserverPlatform: return "1 Observer platform";
            case View.FaultZoneSide: return "2 Fault zone side";
            case View.FollowAgent: return "3 Follow agent";
            default: return "4 Ride-along";
        }
    }

    private QAXRAgentController SelectedAgent() => scenario != null && scenario.Selected != null ? scenario.Selected.agent : null;

    private void SetFade(float a)
    {
        if (fadeMat == null) return;
        fadeRenderer.enabled = a > 0.001f;
        fadeMat.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
    }

    private void SetActiveVisuals(bool on)
    {
        if (leftHand != null) leftHand.gameObject.SetActive(on);
        if (rightHand != null) rightHand.gameObject.SetActive(on);
        if (fadeRenderer != null) fadeRenderer.enabled = false;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
