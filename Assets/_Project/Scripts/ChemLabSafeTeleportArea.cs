using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace ChemLab
{
    /// <summary>
    /// Teleport floor that never drops the player's body into a wall, bench or shelf. The arc can land on floor right at
    /// the foot of furniture; the landing spot is then moved to the nearest free spot on this floor (up to 0.6 m away),
    /// or the teleport is refused if there is none.
    /// </summary>
    public class ChemLabSafeTeleportArea : TeleportationArea
    {
        const float Radius = 0.25f;    // matches the rig's CharacterController radius
        const float Feet = 0.2f;       // ignore what you can step over (cables, mats, tape)
        const float Head = 1.9f;       // tall player's head
        const float MaxShift = 0.6f;
        static readonly Collider[] buf = new Collider[8];
        static readonly Color FreeCol = new Color(0f, 0.9f, 0.75f, 0.45f);
        static readonly Color BlockedCol = new Color(1f, 0.25f, 0.2f, 0.45f);
        Transform marker;
        Material markerMat;

        protected override void Awake()
        {
            base.Awake();
            // Hide XRI's stock reticle on this floor: it sits on the raw hit, not where you'll land.
            customReticle = new GameObject("NoReticle");
            customReticle.SetActive(false);
            customReticle.transform.SetParent(transform, false);
            // Body-sized disc (0.5 m = CharacterController diameter); same always-shipped shader as ColliderView.
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>());
            g.name = "TeleportMarker";
            g.transform.localScale = new Vector3(Radius * 2f, 0.002f, Radius * 2f);
            markerMat = new Material(Shader.Find("Sprites/Default"));
            g.GetComponent<Renderer>().sharedMaterial = markerMat;
            marker = g.transform;
            g.SetActive(false);
        }

        /// <summary>Shows where the player will really land (after FindFreeSpot), red when the teleport will be refused.</summary>
        void LateUpdate()
        {
            bool show = false;
            foreach (var i in interactorsHovering)
            {
                if (!(i is XRRayInteractor ray) || !ray.TryGetCurrent3DRaycastHit(out var hit) || !colliders.Contains(hit.collider)) continue;
                bool free = FindFreeSpot(hit.point, out var spot);   // same check the real teleport uses
                marker.position = (free ? spot : hit.point) + Vector3.up * 0.01f;
                markerMat.color = free ? FreeCol : BlockedCol;
                show = true;
                break;
            }
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        }

        protected override bool GenerateTeleportRequest(IXRInteractor interactor, RaycastHit raycastHit, ref TeleportRequest teleportRequest)
        {
            if (!base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest)) return false;
            if (!FindFreeSpot(teleportRequest.destinationPosition, out var spot)) return false;
            teleportRequest.destinationPosition = spot;
            return true;
        }

        /// <summary>Nearest spot within MaxShift where the body fits and that is still this floor (not under a bench top).</summary>
        public bool FindFreeSpot(Vector3 p, out Vector3 spot)
        {
            spot = p;
            if (Fits(p)) return true;
            for (float r = 0.05f; r <= MaxShift + 1e-3f; r += 0.05f)
                for (int a = 0; a < 16; a++)
                {
                    var q = p + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                    if (OnThisFloor(ref q) && Fits(q)) { spot = q; return true; }
                }
            return false;
        }

        bool OnThisFloor(ref Vector3 q)
        {
            if (!Physics.Raycast(q + Vector3.up * Head, Vector3.down, out var hit, Head + 0.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (!colliders.Contains(hit.collider)) return false;
            q = hit.point;
            return true;
        }

        static bool Fits(Vector3 p)
        {
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (Feet + Radius), p + Vector3.up * (Head - Radius), Radius, buf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!(buf[i] is CharacterController)) return false;
            return true;
        }
    }
}
