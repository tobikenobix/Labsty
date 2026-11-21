using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// Inspected objects keep a mark (SetMark): a blue outline once checked, red outline + floating cross after a wrong
    /// answer, green outline + floating check once resolved. A resolved hazard stays in place and stops reacting to the ray.
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
        [Tooltip("Parts hidden when the hazard is left out of a run (FixInstant). Empty = hide the whole object.")]
        public GameObject[] hideOnFix;

        /// <summary>Raised whenever any inspectable is inspected (lab session and lobby listen).</summary>
        public static event Action<ChemLabInspectable> Inspected;

        public bool IsFixed { get; private set; }

        /// <summary>What the player has done with this object; marks only ever move up (Checked -> Wrong -> Correct).</summary>
        public enum Mark { None, Checked, Wrong, Correct }
        public Mark State { get; private set; }

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

        /// <summary>Resolve the hazard: it stays where it is with a green outline and check sign, and stops reacting to the ray.</summary>
        public void Fix()
        {
            if (IsFixed) return;
            IsFixed = true;
            hovering.Clear();
            interactable.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            SetMark(Mark.Correct);
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

        // ---------- marks: outline + floating answer sign ----------

        static Material[] outlineMats;   // per Mark, shared by every object
        readonly List<MeshRenderer> outlines = new List<MeshRenderer>();
        Transform sign;
        GameObject signOk, signWrong;

        public void SetMark(Mark m)
        {
            if (m <= State) return;
            State = m;
            if (outlines.Count == 0) BuildOutline();
            var mat = OutlineMat(m);
            foreach (var o in outlines)
            {
                var arr = o.sharedMaterials;
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                o.sharedMaterials = arr;
            }
            if (m >= Mark.Wrong) ShowSign(m == Mark.Correct);
            if (m == Mark.Wrong && isActiveAndEnabled) StartCoroutine(BlinkOutline());
        }

        /// <summary>Wrong answers flash the outline twice, so they differ from correct ones by more than red vs green.</summary>
        System.Collections.IEnumerator BlinkOutline()
        {
            var step = new WaitForSeconds(0.225f);
            for (int i = 0; i < 4 && State == Mark.Wrong; i++)   // a quick correct answer ends the flashing
            {
                foreach (var o in outlines) if (o != null) o.enabled = i % 2 == 1;
                yield return step;
            }
            foreach (var o in outlines) if (o != null) o.enabled = true;
        }

        static Material OutlineMat(Mark m)
        {
            if (outlineMats == null)
            {
                var shader = Resources.Load<Shader>("ChemLab/ChemLabOutline");
                if (shader == null) { Debug.LogWarning("[ChemLab] Outline shader missing from Resources/ChemLab."); shader = Shader.Find("Sprites/Default"); }
                outlineMats = new Material[4];
                var cols = new[] { Color.clear, ChemLabUIKit.Accent, ChemLabUIKit.Bad, ChemLabUIKit.Ok };
                for (int i = 1; i < 4; i++) { outlineMats[i] = new Material(shader) { name = "Outline_" + (Mark)i }; outlineMats[i].SetColor("_Color", cols[i]); }
            }
            return outlineMats[(int)m];
        }

        /// <summary>A copy of every visible mesh, drawn by the outline shader as a rim around the silhouette.</summary>
        void BuildOutline()
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                // static-batched renderers point at the whole combined batch; text meshes have no hull worth drawing
                if (r == null || !r.enabled || mf.sharedMesh == null || r.isPartOfStaticBatch || r.GetComponent<TMP_Text>() != null) continue;
                var go = new GameObject("Outline");
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var o = go.AddComponent<MeshRenderer>();
                o.shadowCastingMode = ShadowCastingMode.Off;
                o.receiveShadows = false;
                o.lightProbeUsage = LightProbeUsage.Off;
                o.reflectionProbeUsage = ReflectionProbeUsage.Off;
                o.sharedMaterials = new Material[mf.sharedMesh.subMeshCount];
                outlines.Add(o);
            }
        }

        /// <summary>Green check / red cross disc floating above the object, turned to the player.</summary>
        void ShowSign(bool ok)
        {
            if (sign == null)
            {
                var b = new Bounds(transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r.GetComponent<TMP_Text>() != null || outlines.Contains(r as MeshRenderer)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                var c = new GameObject("AnswerSign_" + name, typeof(RectTransform)).AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                c.sortingOrder = 5;
                sign = c.transform;
                ((RectTransform)sign).sizeDelta = new Vector2(120, 120);
                sign.localScale = Vector3.one * 0.0013f;   // 16 cm: readable across the lab
                sign.position = new Vector3(b.center.x, b.max.y + 0.14f, b.center.z);
                ChemLabUIKit.Img(sign, "Rim", Vector2.zero, new Vector2(120, 120), Color.white, ChemLabUIKit.Circle).raycastTarget = false;
                signOk = ChemLabUIKit.SignDisc(sign, "Resolved", Vector2.zero, 104, ChemLabUIKit.Ok, check: true).gameObject;
                signWrong = ChemLabUIKit.SignDisc(sign, "Wrong", Vector2.zero, 104, ChemLabUIKit.Bad, check: false, cross: true).gameObject;
            }
            signOk.SetActive(ok);
            signWrong.SetActive(!ok);
        }

        void LateUpdate()
        {
            if (sign == null || Camera.main == null) return;
            var look = sign.position - Camera.main.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) sign.rotation = Quaternion.LookRotation(look);
        }

        void OnDestroy() { if (sign != null) Destroy(sign.gameObject); }

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
