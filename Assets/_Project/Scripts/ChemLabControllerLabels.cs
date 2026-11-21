using System.Collections.Generic;
using TMPro;
using UnityEngine;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// Sign-style labels for the real buttons of both controllers. Each controller gets a column of labels on its
    /// outer side, laid out in the head's frame (so the column never folds up when the controller tilts), facing the
    /// head, with a thin leader line from each label to its button. Buttons are found by the part names of the
    /// XRI starter rig's controller model ("Left/Right Controller Visual"): Trigger, Bumper (grip), ThumbStick,
    /// Button_A (A / X) and Button_B (B / Y). Hidden in desktop mode, where there are no controllers.
    /// </summary>
    public class ChemLabControllerLabels : MonoBehaviour
    {
        public readonly struct Item
        {
            public readonly bool left; public readonly string part, key, text;
            /// <param name="key">Button letter drawn in a sign rim ("A"), or null for named controls (text says it).</param>
            public Item(bool left, string part, string key, string text) { this.left = left; this.part = part; this.key = key; this.text = text; }
        }

        class Tag { public Transform ctrl, plate; public Renderer part; public LineRenderer line; public float side, dy; }

        // ponytail: metres in the head's frame; tune on device if a label covers the hand.
        // Scale 0.0004 gives ~8 mm capitals, about 1 deg at a hand's distance (VR minimum).
        const float Out = 0.12f, Top = 0.065f, Step = 0.032f, Scale = 0.0004f, PlateW = 320f, PlateH = 70f;

        readonly List<Tag> tags = new List<Tag>();
        Transform root;
        static Material lineMat;

        public bool Visible { get => gameObject.activeSelf; set => gameObject.SetActive(value); }

        /// <summary>Items per controller stack top to bottom in the order given.</summary>
        public static ChemLabControllerLabels Create(params Item[] items)
        {
            var l = new GameObject("Controller Labels").AddComponent<ChemLabControllerLabels>();
            l.Build(items);
            return l;
        }

        void Build(Item[] items)
        {
            root = new GameObject("Labels").transform;
            root.SetParent(transform, false);
            if (lineMat == null) lineMat = new Material(Shader.Find("Sprites/Default")) { color = K.Bg };
            var visuals = new Dictionary<bool, Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == "Left Controller Visual") visuals[true] = t;
                else if (t.name == "Right Controller Visual") visuals[false] = t;
            }
            int left = 0, right = 0;
            foreach (var it in items)
            {
                if (!visuals.TryGetValue(it.left, out var vis)) continue;
                var part = FindChild(vis, it.part)?.GetComponent<Renderer>();
                if (part == null) continue;
                int row = it.left ? left++ : right++;
                var tag = new Tag { ctrl = vis.parent, part = part, side = it.left ? -1f : 1f, dy = Top - row * Step };

                var c = new GameObject("Label_" + (it.left ? "L_" : "R_") + it.part, typeof(RectTransform)).AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                c.sortingOrder = 12;
                var rt = (RectTransform)c.transform;
                rt.SetParent(root, false);
                rt.sizeDelta = new Vector2(PlateW, PlateH);
                rt.localScale = Vector3.one * Scale;
                K.Img(rt, "Plate", Vector2.zero, new Vector2(PlateW, PlateH), K.Bg, K.Rounded).pixelsPerUnitMultiplier = 2f;
                K.Outline(rt, new Vector2(PlateW, PlateH), K.Text, 2f);
                float x0 = -PlateW / 2f + 16f;
                if (it.key != null) { K.KeyDisc(rt, it.key, new Vector2(x0 + 24f, 0), 48f, K.Text); x0 += 60f; }
                float w = PlateW / 2f - 14f - x0;
                K.FitOneLine(K.Label(rt, "Text", it.text, new Vector2(x0 + w / 2f, 0), new Vector2(w, PlateH - 8f), 30, K.Text, TextAlignmentOptions.MidlineLeft), 18);
                tag.plate = rt;

                tag.line = new GameObject("Leader").AddComponent<LineRenderer>();
                tag.line.transform.SetParent(root, false);
                tag.line.sharedMaterial = lineMat;
                tag.line.widthMultiplier = 0.0015f;
                tag.line.positionCount = 2;
                tag.line.useWorldSpace = true;
                tag.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tag.line.receiveShadows = false;
                tags.Add(tag);
            }
        }

        static Transform FindChild(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool show = cam != null && !ChemLabControls.DesktopMode;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;
            float half = PlateW / 2f * Scale;
            foreach (var t in tags)
            {
                bool on = t.part != null && t.part.gameObject.activeInHierarchy;
                t.plate.gameObject.SetActive(on);
                t.line.enabled = on;
                if (!on) continue;
                var pos = t.ctrl.position + cam.transform.right * (t.side * Out) + cam.transform.up * t.dy;
                t.plate.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up));
                t.line.SetPosition(0, pos - t.plate.right * (t.side * half));   // the plate's edge nearest the controller
                t.line.SetPosition(1, t.part.bounds.center);
            }
        }
    }
}
