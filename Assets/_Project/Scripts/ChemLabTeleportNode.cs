using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ChemLab
{
    /// <summary>
    /// A fixed teleport node: glowing floor ring that pulses, plus a floating name tag that faces the player.
    /// The actual teleport is done by the XRI TeleportationAnchor on the same object
    /// (point the controller at it, push the thumbstick forward, release).
    /// </summary>
    public class ChemLabTeleportNode : MonoBehaviour
    {
        public string nodeName = "Node";
        public Renderer glow;
        public Transform nameTag;
        public Color color = new Color(0f, 0.85f, 0.75f);

        public static event Action<ChemLabTeleportNode> PlayerArrived;
        public bool PlayerHere { get; private set; }

        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");

        void Awake() => mpb = new MaterialPropertyBlock();

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // pulse; brighter while the player stands somewhere else (it's an invitation)
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 3f + transform.position.x);
            float k = PlayerHere ? 0.35f : pulse;
            if (glow != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();   // survives a script reload during Play
                glow.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, color * (0.6f + 1.6f * k));
                mpb.SetColor(BaseId, new Color(color.r, color.g, color.b, 0.35f + 0.4f * k));
                glow.SetPropertyBlock(mpb);
            }
            if (nameTag != null)
            {
                Vector3 look = nameTag.position - cam.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) nameTag.rotation = Quaternion.LookRotation(look);
                nameTag.gameObject.SetActive(!PlayerHere);
            }

            Vector3 d = cam.transform.position - transform.position;
            d.y = 0f;
            bool here = d.magnitude < 0.6f;
            if (here && !PlayerHere) PlayerArrived?.Invoke(this);
            PlayerHere = here;
        }

        /// <summary>Desktop fallback: move the rig onto this node.</summary>
        public void TeleportHere(XROrigin origin)
        {
            var cc = origin.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            origin.transform.position = new Vector3(transform.position.x, origin.transform.position.y, transform.position.z);
            if (cc != null) cc.enabled = true;
        }
    }
}
