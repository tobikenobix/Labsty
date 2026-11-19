using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace QAXR.Demo
{
    /// <summary>
    /// Zone 3, cutscene (Roadmap §5). When the player's head enters the box, a subject flies across
    /// the room for `duration` seconds. Bad: the script takes over the camera (head tracking off,
    /// camera forced to look at the subject = tracking override) and turns the XR Origin towards it
    /// (uncontrolled rotation, D2). Fixed: the subject flies, the player keeps full control.
    /// Plays again after the player leaves and re-enters.
    /// </summary>
    public class Zone3Cutscene : MonoBehaviour
    {
        public bool isFixed;
        public Transform subject;
        [Tooltip("Subject path, local to this transform.")]
        public Vector3 from = new(-5f, 1f, 4f), to = new(5f, 1f, 4f);
        public float arcHeight = 1.5f;
        public float duration = 6f;
        [Tooltip("Bad state: max yaw rate the script turns the origin at, °/s.")]
        public float maxYawRate = 40f;
        [Tooltip("Box centred on this transform; the cutscene starts when the player's head enters.")]
        public Vector3 size = new(11f, 3f, 10f);

        XROrigin m_Origin;
        TrackedPoseDriver m_Tracking;
        bool m_WasInside, m_TookCamera;
        float m_T = -1f; // < 0: not playing

        void Start()
        {
            m_Origin = FindFirstObjectByType<XROrigin>();
            if (m_Origin) m_Tracking = m_Origin.Camera.GetComponent<TrackedPoseDriver>();
            if (subject) subject.position = transform.TransformPoint(from);
        }

        void Update()
        {
            if (!m_Origin || !subject) return;
            bool inside = Contains(m_Origin.Camera.transform.position);
            if (inside && !m_WasInside && m_T < 0f) Play();
            m_WasInside = inside;
            if (m_T < 0f) return;

            m_T += Time.deltaTime;
            float s = Mathf.Clamp01(m_T / duration);
            subject.position = Vector3.Lerp(transform.TransformPoint(from), transform.TransformPoint(to), s)
                               + Vector3.up * (arcHeight * Mathf.Sin(s * Mathf.PI));

            if (m_TookCamera)
            {
                var o = m_Origin.transform;
                var dir = Vector3.ProjectOnPlane(subject.position - o.position, Vector3.up);
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(o.forward, Vector3.up), dir, Vector3.up);
                float step = maxYawRate * Time.deltaTime;
                m_Origin.RotateAroundCameraUsingOriginUp(Mathf.Clamp(yaw, -step, step));
            }
            if (s >= 1f) Stop();
        }

        // Runs after tracking and agents have written the head pose, and overwrites it.
        void LateUpdate()
        {
            if (!m_TookCamera) return;
            var cam = m_Origin.Camera.transform;
            cam.rotation = Quaternion.LookRotation(subject.position - cam.position, Vector3.up);
        }

        void Play()
        {
            m_T = 0f;
            m_TookCamera = !isFixed;
            if (m_TookCamera && m_Tracking) m_Tracking.enabled = false;
        }

        void Stop()
        {
            m_T = -1f;
            if (m_TookCamera && m_Tracking) m_Tracking.enabled = true;
            m_TookCamera = false;
        }

        void OnDisable() { if (m_T >= 0f) Stop(); }

        bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return Mathf.Abs(l.x) < size.x / 2 && Mathf.Abs(l.y) < size.y / 2 && Mathf.Abs(l.z) < size.z / 2;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.DrawLine(from, to);
        }
    }
}
