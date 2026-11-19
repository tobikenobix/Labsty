using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace QAXR
{
    /// <summary>
    /// Marks an origin the sickness zones act on: the player's XR Origin (added automatically) and every agent.
    /// Zones move the transform directly; ghost agents read the zone overrides because they run no providers.
    /// </summary>
    public class QaxrOrigin : MonoBehaviour
    {
        public static readonly List<QaxrOrigin> All = new List<QaxrOrigin>();

        [Tooltip("The camera for the player rig, the head transform for an agent.")]
        public Transform head;

        [Tooltip("Set every frame by the agent / input code: what locomotion was asked for (D2 uncontrolled motion).")]
        public LocoCommand loco;

        /// <summary>Move speed a zone imposes, or -1. Ghost agents use it instead of the provider's speed.</summary>
        public float zoneMoveSpeed = -1f;

        /// <summary>True while a zone has a comfort mitigation (vignette) switched on for this origin.</summary>
        public bool zoneMitigation;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Adds QaxrOrigin to every XR Origin in the loaded scenes that lacks one.</summary>
        public static void RegisterPlayerRigs()
        {
            foreach (var rig in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
            {
                if (rig.GetComponent<QaxrOrigin>()) continue;
                var o = rig.gameObject.AddComponent<QaxrOrigin>();
                o.head = rig.Camera ? rig.Camera.transform : null;
            }
        }
    }

    /// <summary>
    /// A box volume that acts on every QaxrOrigin inside it (not only the player's), with the `fixed` toggle of §5.
    /// Membership is polled from positions, so kinematic ghost agents need no colliders or rigidbodies.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public abstract class QaxrZone : MonoBehaviour
    {
        [SerializeField] protected bool isFixed;

        readonly HashSet<QaxrOrigin> inside = new HashSet<QaxrOrigin>();
        readonly List<QaxrOrigin> scratch = new List<QaxrOrigin>();
        BoxCollider volume;

        public bool IsFixed
        {
            get => isFixed;
            set { if (isFixed == value) return; isFixed = value; OnFixedChanged(); }
        }

        public IReadOnlyCollection<QaxrOrigin> Inside => inside;

        protected virtual void Awake()
        {
            volume = GetComponent<BoxCollider>();
            volume.isTrigger = true; // a volume, never something to bump into
            QaxrOrigin.RegisterPlayerRigs();
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>One frame: enter/exit bookkeeping, then Tick per origin inside. Public so tests can drive it.</summary>
        public virtual void Step(float dt)
        {
            inside.RemoveWhere(o => !o);
            scratch.Clear();
            scratch.AddRange(QaxrOrigin.All);
            foreach (var o in scratch)
            {
                bool now = Contains(o.transform.position);
                if (now && inside.Add(o)) OnEnter(o);
                else if (!now && inside.Remove(o)) OnExit(o);
            }
            scratch.Clear();
            scratch.AddRange(inside);
            foreach (var o in scratch) Tick(o, dt);
        }

        public bool Contains(Vector3 world)
        {
            if (!volume) volume = GetComponent<BoxCollider>();
            var p = transform.InverseTransformPoint(world) - volume.center;
            var h = volume.size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        protected virtual void OnEnter(QaxrOrigin o) { }
        protected virtual void OnExit(QaxrOrigin o) { }
        protected virtual void Tick(QaxrOrigin o, float dt) { }
        protected virtual void OnFixedChanged() { }

        /// <summary>CharacterController rigs ignore direct transform writes until physics re-syncs.</summary>
        protected static void SyncPhysics() => Physics.SyncTransforms();
    }

    /// <summary>
    /// Zone 1, fast corridor (D1, D3, D5). Bad: move speed ramps up to badSpeed at `acceleration` m/s², vignette off.
    /// Fixed: fixedSpeed with no ramp, vignette on. Drives the rig's ContinuousMoveProvider when it has one, so the
    /// embodied agent and the player feel it; ghosts read QaxrOrigin.zoneMoveSpeed. LiveTuner changes badSpeed and IsFixed.
    /// </summary>
    public class FastCorridorZone : QaxrZone
    {
        public float badSpeed = 4f;        // m/s
        public float acceleration = 1.5f;  // m/s², ramp in the bad state [ph]
        public float fixedSpeed = 1.4f;    // m/s, top of natural walking

        class State
        {
            public ContinuousMoveProvider provider;
            public float originalSpeed, speed;
            public TunnelingVignetteController vignette;
            public bool vignetteWasEnabled;
        }

        readonly Dictionary<QaxrOrigin, State> states = new Dictionary<QaxrOrigin, State>();

        public float SpeedOf(QaxrOrigin o) => states.TryGetValue(o, out var s) ? s.speed : -1f;

        protected override void OnEnter(QaxrOrigin o)
        {
            var s = new State
            {
                provider = o.GetComponentInChildren<ContinuousMoveProvider>(true),
                vignette = o.GetComponentInChildren<TunnelingVignetteController>(true),
            };
            s.originalSpeed = s.provider ? s.provider.moveSpeed : fixedSpeed;
            s.speed = s.originalSpeed;
            s.vignetteWasEnabled = s.vignette && s.vignette.enabled;
            states[o] = s;
        }

        protected override void Tick(QaxrOrigin o, float dt)
        {
            var s = states[o];
            s.speed = isFixed ? fixedSpeed : Mathf.MoveTowards(s.speed, badSpeed, acceleration * dt);
            if (s.provider) s.provider.moveSpeed = s.speed;
            if (s.vignette) s.vignette.enabled = isFixed;
            o.zoneMoveSpeed = s.speed;
            o.zoneMitigation = isFixed;
        }

        protected override void OnExit(QaxrOrigin o)
        {
            if (!states.Remove(o, out var s)) return;
            if (s.provider) s.provider.moveSpeed = s.originalSpeed;
            if (s.vignette) s.vignette.enabled = s.vignetteWasEnabled;
            o.zoneMoveSpeed = -1f;
            o.zoneMitigation = false;
        }
    }

    /// <summary>
    /// Zone 2, rocking boat (D1 roll, D2 uncontrolled motion). The deck rolls and heaves at ~0.2 Hz and carries every
    /// origin standing on it, as if parented, without reparenting the rig. Fixed: the boat is moored (eases to rest).
    /// </summary>
    public class RockingBoatZone : QaxrZone
    {
        public Transform deck;
        public float frequency = 0.2f;      // Hz, inside the 0.1–0.5 Hz sickness band
        public float rollAmplitude = 8f;    // deg [ph]
        public float heaveAmplitude = 0.15f; // m [ph]

        Vector3 restPos;
        Quaternion restRot;
        Pose prev;
        float phase, amp = 1f;
        bool captured;

        protected override void Awake()
        {
            base.Awake();
            CaptureRest();
        }

        void CaptureRest()
        {
            if (!deck) return;
            restPos = deck.position;
            restRot = deck.rotation;
            prev = new Pose(restPos, restRot);
            captured = true;
        }

        public override void Step(float dt)
        {
            if (deck && !captured) CaptureRest(); // edit-mode tests skip Awake
            if (deck)
            {
                phase += 2f * Mathf.PI * frequency * dt;
                amp = Mathf.MoveTowards(amp, isFixed ? 0f : 1f, dt / 3f); // no jolt when toggled
                float wave = Mathf.Sin(phase) * amp;
                deck.SetPositionAndRotation(restPos + Vector3.up * (heaveAmplitude * Mathf.Sin(phase * 2f) * amp),
                                            restRot * Quaternion.AngleAxis(rollAmplitude * wave, Vector3.forward));
            }
            base.Step(dt); // origins inside get carried in Tick
            if (deck) prev = new Pose(deck.position, deck.rotation);
            SyncPhysics();
        }

        protected override void Tick(QaxrOrigin o, float dt)
        {
            if (!deck) return;
            var delta = deck.rotation * Quaternion.Inverse(prev.rotation);
            var t = o.transform;
            t.SetPositionAndRotation(deck.position + delta * (t.position - prev.position), delta * t.rotation);
        }

        protected override void OnExit(QaxrOrigin o)
        {
            // Step off level: keep the heading, drop the leftover roll and pitch.
            var t = o.transform;
            t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        }
    }

    /// <summary>
    /// Zone 3, cutscene (D2 uncontrolled rotation + head-tracking override). Bad: on entry the script turns the
    /// origin by sweepDegrees around the head and pins the camera (TrackedPoseDriver off) until it ends.
    /// Fixed: never take the camera; show `pointer` (e.g. a "look here" sign) and let the user turn.
    /// </summary>
    public class CutsceneZone : QaxrZone
    {
        public float sweepDegrees = 120f;
        public float duration = 3f;
        public float headPitch = -15f;  // the scripted camera looks down at the scene
        public GameObject pointer;      // shown instead in the fixed state

        class Run
        {
            public float time, yawDone;
            public Quaternion headStart;
            public TrackedPoseDriver driver;
            public bool driverWasEnabled, done;
        }

        readonly Dictionary<QaxrOrigin, Run> runs = new Dictionary<QaxrOrigin, Run>();

        protected override void OnEnter(QaxrOrigin o)
        {
            if (pointer) pointer.SetActive(isFixed);
            if (isFixed) return;
            var r = new Run { headStart = o.head ? o.head.localRotation : Quaternion.identity };
            if (o.head && (r.driver = o.head.GetComponent<TrackedPoseDriver>()))
            {
                r.driverWasEnabled = r.driver.enabled;
                r.driver.enabled = false; // the bug: the app owns the camera
            }
            runs[o] = r;
        }

        protected override void Tick(QaxrOrigin o, float dt)
        {
            if (!runs.TryGetValue(o, out var r) || r.done) return;
            r.time += dt;
            float s = Mathf.Clamp01(r.time / duration);
            float yaw = sweepDegrees * AgentSampling.MinJerk(s);
            var pivot = o.head ? o.head.position : o.transform.position;
            var delta = Quaternion.AngleAxis(yaw - r.yawDone, Vector3.up);
            r.yawDone = yaw;
            var t = o.transform;
            t.SetPositionAndRotation(pivot + delta * (t.position - pivot), delta * t.rotation);
            if (o.head) o.head.localRotation = Quaternion.Slerp(r.headStart, Quaternion.Euler(headPitch, 0f, 0f), AgentSampling.MinJerk(s));
            SyncPhysics();
            if (s >= 1f) Finish(r);
        }

        protected override void OnExit(QaxrOrigin o)
        {
            if (runs.Remove(o, out var r)) Finish(r);
        }

        static void Finish(Run r)
        {
            r.done = true;
            if (r.driver) r.driver.enabled = r.driverWasEnabled;
        }
    }

    /// <summary>
    /// Zone 4, heavy room (D4). Generates its own load on Awake: high-poly spheres, stacked transparent quads
    /// (overdraw) and dense particle systems. Fixed switches the load off. Only meaningful on the Quest.
    /// </summary>
    public class HeavyRoomZone : QaxrZone
    {
        public int sphereSegments = 256;  // 256 × 256 × 2 ≈ 130k triangles per sphere
        public int spheres = 6;
        public int overdrawLayers = 24;
        public int particleSystems = 8;

        GameObject load;

        protected override void Awake()
        {
            base.Awake();
            if (Application.isPlaying) BuildLoad();
        }

        protected override void OnFixedChanged()
        {
            if (load) load.SetActive(!isFixed);
        }

        /// <summary>Builds the load under a child object. Public for tests; called on Awake in play mode.</summary>
        public void BuildLoad()
        {
            if (load) return;
            load = new GameObject("GeneratedLoad");
            load.transform.SetParent(transform, false);
            var lit = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>(); // borrow the pipeline's default lit material
            var litMat = lit.sharedMaterial;
            DestroyImmediate(lit.gameObject);
            var transparent = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultParticleMaterial : null;

            var mesh = Sphere(sphereSegments);
            for (int i = 0; i < spheres; i++)
            {
                var go = new GameObject($"HighPoly_{i}");
                go.transform.SetParent(load.transform, false);
                go.transform.localPosition = new Vector3((i % 3 - 1) * 1.5f, 1f + (i / 3) * 1.2f, 2f);
                go.transform.localScale = Vector3.one * 0.8f;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = litMat;
            }

            for (int i = 0; i < overdrawLayers; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(q.GetComponent<Collider>());
                q.name = $"Overdraw_{i}";
                q.transform.SetParent(load.transform, false);
                q.transform.localPosition = new Vector3(0f, 1.5f, 0.5f + i * 0.05f);
                q.transform.localScale = new Vector3(5f, 3f, 1f);
                if (transparent) q.GetComponent<Renderer>().sharedMaterial = transparent;
            }

            for (int i = 0; i < particleSystems; i++)
            {
                var go = new GameObject($"Particles_{i}");
                go.transform.SetParent(load.transform, false);
                go.transform.localPosition = new Vector3((i % 4 - 1.5f) * 1.2f, 0.2f, 1f + (i / 4) * 1.5f);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.maxParticles = 5000;
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                main.startLifetime = 3f;
                main.startSpeed = 1f;
                var emission = ps.emission;
                emission.rateOverTime = 1500f;
                if (transparent) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = transparent;
            }
            load.SetActive(!isFixed);
        }

        static Mesh Sphere(int seg)
        {
            var verts = new List<Vector3>((seg + 1) * (seg + 1));
            var tris = new List<int>(seg * seg * 6);
            for (int y = 0; y <= seg; y++)
            for (int x = 0; x <= seg; x++)
            {
                float u = (float)x / seg * Mathf.PI * 2f, v = (float)y / seg * Mathf.PI;
                verts.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)) * 0.5f);
            }
            for (int y = 0; y < seg; y++)
            for (int x = 0; x < seg; x++)
            {
                int a = y * (seg + 1) + x, b = a + seg + 1;
                tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var mesh = new Mesh { name = "QAXR_HighPolySphere", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
