using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ChemLab
{
    /// <summary>
    /// The one reusable hazard/inspectable component. Point + Trigger (or Grip) inspects it.
    /// Hazard = one of the 10 assessed hazards. Tutorial = the lobby coffee cup.
    /// Decoy = ordinary lab object, so the controller ray reacts to many things and does
    /// not give away where the hazards are.
    /// When a hazard is answered correctly, Fix() hides the "problem" parts (hideOnFix, or the whole object).
    /// Swap the child meshes for real models freely; keep the BoxCollider on this object.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ChemLabInspectable : MonoBehaviour
    {
        public enum Kind { Hazard, Tutorial, Decoy }

        public Kind kind = Kind.Decoy;
        [Tooltip("H01..H10 for hazards, empty otherwise.")]
        public string hazardId;
        [Tooltip("Shown when a decoy is inspected (optional).")]
        public string displayName;
        [Tooltip("Parts hidden when the hazard is fixed. Empty = hide the whole object.")]
        public GameObject[] hideOnFix;

        /// <summary>Raised whenever any inspectable is inspected (lab session and lobby listen).</summary>
        public static event Action<ChemLabInspectable> Inspected;

        public bool IsFixed { get; private set; }

        static readonly List<ChemLabInspectable> all = new List<ChemLabInspectable>();
        readonly List<IXRHoverInteractor> hovering = new List<IXRHoverInteractor>();
        XRSimpleInteractable interactable;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.hoverEntered.AddListener(a => { if (!IsGaze(a.interactorObject)) hovering.Add(a.interactorObject); });
            interactable.hoverExited.AddListener(a => hovering.Remove(a.interactorObject));
            // Grip on the object also counts as inspecting it.
            interactable.selectEntered.AddListener(a => { if (!IsGaze(a.interactorObject)) Inspect(); });
        }

        void OnEnable() => all.Add(this);
        void OnDisable() { all.Remove(this); hovering.Clear(); }

        static bool IsGaze(IXRInteractor i) => i is XRGazeInteractor;

        static InteractorHandedness HandOf(IXRInteractor i)
        {
            if (i.handedness != InteractorHandedness.None) return i.handedness;
            for (var t = i.transform; t != null; t = t.parent)
            {
                if (t.name.Contains("Left")) return InteractorHandedness.Left;
                if (t.name.Contains("Right")) return InteractorHandedness.Right;
            }
            return InteractorHandedness.None;
        }

        public void Inspect()
        {
            if (IsFixed) return;
            Inspected?.Invoke(this);
        }

        /// <summary>Visually resolve the hazard: shrink the problem parts away, then stop reacting to the ray.</summary>
        public void Fix()
        {
            if (IsFixed) return;
            IsFixed = true;
            hovering.Clear();
            interactable.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            var parts = hideOnFix != null && hideOnFix.Length > 0 ? hideOnFix : new[] { gameObject };
            StartCoroutine(ShrinkAway(parts));
        }

        /// <summary>Safe state without animation, for a hazard left out of this run.</summary>
        public void FixInstant()
        {
            IsFixed = true;
            GetComponent<XRSimpleInteractable>().enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            if (hideOnFix != null && hideOnFix.Length > 0) { foreach (var p in hideOnFix) if (p != null) p.SetActive(false); }
            else foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        IEnumerator ShrinkAway(GameObject[] parts)
        {
            var start = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++) if (parts[i] != null) start[i] = parts[i].transform.localScale;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.45f)
            {
                float k = 1f - t * t;
                for (int i = 0; i < parts.Length; i++) if (parts[i] != null) parts[i].transform.localScale = start[i] * k;
                yield return null;
            }
            foreach (var p in parts) if (p != null && p != gameObject) p.SetActive(false);
            if (Array.IndexOf(parts, gameObject) >= 0)
            {
                // keep this component alive for the coroutine owner; just hide the renderers
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                transform.localScale = start[Array.IndexOf(parts, gameObject)];
            }
        }

        /// <summary>The object the given controller's ray is on (trigger press). Falls back to any controller ray.</summary>
        public static ChemLabInspectable FindHoveredBy(InteractorHandedness hand)
        {
            ChemLabInspectable any = null;
            foreach (var i in all)
                foreach (var h in i.hovering)
                {
                    if (HandOf(h) == hand) return i;
                    if (any == null) any = i;
                }
            return any;
        }
    }
}
