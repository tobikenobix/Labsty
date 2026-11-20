using Unity.XR.CoreUtils;
using UnityEngine;

namespace QAXR.Demo
{
    /// <summary>
    /// Zone 2, rocking boat (Roadmap §5). Put on the deck root. The deck rolls and heaves at ~0.2 Hz.
    /// Bad: while the player's head is over the deck, the XR Origin is parented to it, so the view
    /// rolls with the boat (roll + uncontrolled motion, D1/D2). Fixed: calm water (motion scaled by
    /// fixedScale) and the origin is never carried, so the horizon stays level.
    /// </summary>
    public class Zone2RockingBoat : MonoBehaviour
    {
        public bool isFixed;
        [Tooltip("Rocking frequency, Hz.")]
        public float frequency = 0.2f;
        [Tooltip("Roll amplitude around the deck's forward axis, degrees.")]
        public float rollAmplitude = 8f;
        [Tooltip("Vertical heave amplitude, m.")]
        public float heaveAmplitude = 0.1f;
        [Range(0f, 1f), Tooltip("Fixed state: fraction of the motion left (calm water).")]
        public float fixedScale = 0.1f;
        [Tooltip("Deck-local box centred on this transform that counts as 'on board'.")]
        public Vector3 size = new(6f, 4f, 13.6f);

        XROrigin m_Origin;
        Transform m_OriginParent;
        Vector3 m_RestPos;
        Quaternion m_RestRot;
        bool m_Carrying;

        void Start()
        {
            m_RestPos = transform.position;
            m_RestRot = transform.rotation;
            m_Origin = FindFirstObjectByType<XROrigin>();
            if (m_Origin) m_OriginParent = m_Origin.transform.parent;
        }

        void Update()
        {
            float k = isFixed ? fixedScale : 1f;
            float w = 2f * Mathf.PI * frequency * Time.time;
            transform.SetPositionAndRotation(
                m_RestPos + Vector3.up * (heaveAmplitude * k * Mathf.Cos(w)),
                m_RestRot * Quaternion.Euler(0f, 0f, rollAmplitude * k * Mathf.Sin(w)));

            if (m_Origin) Carry(!isFixed && Contains(m_Origin.Camera.transform.position));
        }

        void Carry(bool carry)
        {
            if (carry == m_Carrying) return;
            m_Carrying = carry;
            var t = m_Origin.transform;
            if (carry) { t.SetParent(transform, true); return; }
            t.SetParent(m_OriginParent, true);
            t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f); // stand the player up again
        }

        void OnDisable() { if (m_Origin) Carry(false); }

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
