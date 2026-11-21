using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ChemLab
{
    /// <summary>
    /// Meta Quest 3 button mapping, shared by all three scenes (walking/turning come from the XRI starter rig):
    ///   Either thumbstick forward      aim a teleport arc onto the floor, release to jump (no continuous walking)
    ///   Either thumbstick left/right   snap turn 45 deg; pull back = turn around
    ///   Trigger                        select: inspect the object you point at, press UI buttons
    ///   Grip                           also inspects the object you point at
    ///   A (right)                      confirm / submit answer
    ///   B (right)                      close panel
    ///   X (left)                       show / hide the controls card
    ///   Y or Menu (left)               finish the lab early
    /// When no headset is active (Play in Editor without Link), a keyboard + mouse fallback is enabled.
    /// </summary>
    public class ChemLabControls : MonoBehaviour
    {
        public static event Action Confirm, Back, ToggleHelp, Finish;
        /// <summary>Set by the active scene UI: when true, trigger presses go to UI buttons only.</summary>
        public static Func<bool> BlockInspect;
        public static bool DesktopMode { get; private set; }

        public float desktopMoveSpeed = 2.2f;
        public float desktopLookSpeed = 0.15f;

        InputAction leftTrigger, rightTrigger, aBtn, bBtn, xBtn, yBtn;
        XROrigin origin;
        CharacterController body;
        Transform cam;
        float pitch;

        static InputAction Make(string name, params string[] paths)
        {
            var a = new InputAction(name, InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            a.Enable();
            return a;
        }

        void Awake()
        {
            DesktopMode = false;
            CreateActions();
            // Teleport-only locomotion, so gravity has nothing to do; with it on, walking (headset off, out of the Guardian)
            // past the floor colliders dropped the rig forever: blue clear colour, rays hitting nothing, until a restart.
            foreach (var g in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(FindObjectsSortMode.None))
                g.useGravity = false;
        }

        void CreateActions()
        {
            leftTrigger = Make("Inspect L", "<XRController>{LeftHand}/{TriggerButton}");
            rightTrigger = Make("Inspect R", "<XRController>{RightHand}/{TriggerButton}");
            // confirm / back sit on the main hand's face buttons, panel / finish on the other hand (left-handed mode swaps them);
            // the Menu button exists only on the left controller, so it always finishes
            string main = ChemLabRun.LeftHanded ? "{LeftHand}" : "{RightHand}", other = ChemLabRun.LeftHanded ? "{RightHand}" : "{LeftHand}";
            aBtn = Make("Confirm", $"<XRController>{main}/{{PrimaryButton}}", "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            bBtn = Make("Back", $"<XRController>{main}/{{SecondaryButton}}", "<Keyboard>/escape", "<Keyboard>/backspace");
            xBtn = Make("Help", $"<XRController>{other}/{{PrimaryButton}}", "<Keyboard>/h");
            yBtn = Make("Finish", $"<XRController>{other}/{{SecondaryButton}}", "<XRController>{LeftHand}/{MenuButton}",
                        "<XRController>{LeftHand}/menu", "<Keyboard>/f");
        }

        IEnumerator Start()
        {
            SetFoveation();   // no-op without a headset
            // Only fall back to keyboard/mouse when OpenXR really failed to start.
            yield return new WaitForSeconds(3f);
            var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance != null
                ? UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager : null;
            bool xrRunning = (mgr != null && mgr.activeLoader != null) || UnityEngine.XR.XRSettings.isDeviceActive;
            if (xrRunning) yield break;

            DesktopMode = true;
            Debug.Log("[ChemLab] No XR headset active: keyboard + mouse fallback enabled.");
            origin = FindFirstObjectByType<XROrigin>();
            if (origin != null)
            {
                body = origin.GetComponent<CharacterController>();
                cam = origin.Camera != null ? origin.Camera.transform : null;
                if (cam != null)
                    foreach (var tpd in cam.GetComponents<UnityEngine.InputSystem.XR.TrackedPoseDriver>()) tpd.enabled = false;
            }
            ChemLabUIKit.EnableMouse();
        }

        /// <summary>
        /// Fixed foveated rendering (OpenXR Foveated Rendering feature, SRP foveation API): the periphery of each eye
        /// is shaded at lower rate. 0 = off, 1 = strongest. Panels are placed in the centre of view, so text stays sharp.
        /// </summary>
        [Range(0f, 1f)] public float foveation = 0.66f;   // ponytail: tune on device; raise if GPU-bound, lower if edges shimmer

        void SetFoveation()
        {
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) d.foveatedRenderingLevel = foveation;
        }

        void OnDestroy()
        {
            foreach (var a in new[] { leftTrigger, rightTrigger, aBtn, bBtn, xBtn, yBtn }) a?.Dispose();
        }

        void Update()
        {
            if (rightTrigger == null) CreateActions();   // bindings are lost if scripts reload during Play
            if (rightTrigger.WasPressedThisFrame()) TryInspect(InteractorHandedness.Right);
            if (leftTrigger.WasPressedThisFrame()) TryInspect(InteractorHandedness.Left);
            if (aBtn.WasPressedThisFrame()) Confirm?.Invoke();
            if (bBtn.WasPressedThisFrame()) Back?.Invoke();
            if (xBtn.WasPressedThisFrame()) ToggleHelp?.Invoke();
            if (yBtn.WasPressedThisFrame()) Finish?.Invoke();
            if (DesktopMode) DesktopUpdate();
        }

        static bool Blocked => BlockInspect != null && BlockInspect();

        void TryInspect(InteractorHandedness hand)
        {
            if (Blocked) return;
            var target = ChemLabInspectable.FindHoveredBy(hand);
            if (target != null) target.Inspect();
        }

        // ---------- keyboard + mouse fallback (testing only) ----------

        void DesktopUpdate()
        {
            if (origin == null || cam == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            var offset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform.localPosition.y : 0f;
            cam.localPosition = new Vector3(0f, 1.6f - offset, 0f);

            if (mouse != null && mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue() * desktopLookSpeed;
                origin.transform.Rotate(0f, d.x, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
            }
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (kb != null)
            {
                Vector2 m = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) m.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) m.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) m.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) m.x -= 1;
                if (m != Vector2.zero)
                {
                    var f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                    var r = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                    var move = (f * m.y + r * m.x).normalized * desktopMoveSpeed * Time.deltaTime;
                    if (body != null && body.enabled) body.Move(move + Vector3.down * 0.01f);
                    else origin.transform.position += move;
                }
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Blocked)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                var ray = origin.Camera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out var hit, 15f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var target = hit.collider.GetComponentInParent<ChemLabInspectable>();
                    if (target != null) { target.Inspect(); return; }
                    // clicking a teleport node moves you there (desktop stand-in for the teleport ray)
                    var node = hit.collider.GetComponentInParent<ChemLabTeleportNode>();
                    if (node != null) node.TeleportHere(origin);
                }
            }
        }
    }
}
