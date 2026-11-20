using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace QAXR.Demo
{
    /// <summary>
    /// Zone 1, fast corridor (Roadmap §5). Bad: while the player moves inside the box, the app's
    /// ContinuousMoveProvider is ramped from startSpeed to topSpeed (acceleration ramp), no vignette.
    /// Fixed: constant comfortSpeed, no ramp, tunneling vignette on while moving.
    /// Like a real app it acts on the player's XR Origin only; ghost agents are ignored (§4.0).
    /// </summary>
    public class Zone1FastCorridor : MonoBehaviour, ITunnelingVignetteProvider
    {
        public bool isFixed;
        [Tooltip("Bad state: top speed in m/s. The LiveTuner slider drives this.")]
        public float topSpeed = 4f;
        [Tooltip("Bad state: speed the ramp starts from, m/s.")]
        public float startSpeed = 1f;
        [Tooltip("Bad state: ramp acceleration, m/s².")]
        public float acceleration = 1.5f;
        [Tooltip("Fixed state: constant comfortable speed, m/s.")]
        public float comfortSpeed = 1.2f;
        [Tooltip("Box centred on this transform; the zone is active while the player's head is inside.")]
        public Vector3 size = new(3f, 3f, 50f);
        [SerializeField] VignetteParameters m_Vignette = new();
        public VignetteParameters vignetteParameters => m_Vignette;

        XROrigin m_Origin;
        ContinuousMoveProvider m_Move;
        TunnelingVignetteController m_Tunnel;
        float m_AppSpeed, m_Speed;
        Vector3 m_LastPos;
        bool m_Inside, m_VignetteOn;

        void Start()
        {
            m_Origin = FindFirstObjectByType<XROrigin>();
            m_Move = FindFirstObjectByType<ContinuousMoveProvider>();
            m_Tunnel = FindFirstObjectByType<TunnelingVignetteController>();
            if (m_Move) m_AppSpeed = m_Move.moveSpeed;
            if (m_Origin) m_LastPos = m_Origin.transform.position;
        }

        void Update()
        {
            if (!m_Origin || !m_Move) return;
            var p = m_Origin.transform.position;
            var d = p - m_LastPos; d.y = 0f;
            m_LastPos = p;
            bool moving = d.magnitude > 0.1f * Time.deltaTime; // > 0.1 m/s

            bool inside = Contains(m_Origin.Camera.transform.position);
            if (inside != m_Inside)
            {
                m_Inside = inside;
                m_Speed = startSpeed;
                if (!inside) m_Move.moveSpeed = m_AppSpeed; // the app's own speed outside the zone
            }
            if (inside)
            {
                m_Speed = isFixed ? comfortSpeed
                    : moving ? Mathf.MoveTowards(m_Speed, topSpeed, acceleration * Time.deltaTime)
                    : startSpeed; // every new start ramps again
                m_Move.moveSpeed = m_Speed;
            }
            SetVignette(inside && isFixed && moving);
        }

        void SetVignette(bool on)
        {
            if (!m_Tunnel || on == m_VignetteOn) return;
            m_VignetteOn = on;
            if (on) m_Tunnel.BeginTunnelingVignette(this);
            else m_Tunnel.EndTunnelingVignette(this);
        }

        void OnDisable()
        {
            SetVignette(false);
            if (m_Move && m_Inside) m_Move.moveSpeed = m_AppSpeed;
            m_Inside = false;
        }

        bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return Mathf.Abs(l.x) < size.x / 2 && Mathf.Abs(l.y) < size.y / 2 && Mathf.Abs(l.z) < size.z / 2;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
