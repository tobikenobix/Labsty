using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChemLab
{
    /// <summary>
    /// Small shared UI framework for every ChemLab scene: one colour palette, rounded "glass" panels,
    /// pill buttons, chips and progress bars, all built in code as world-space canvases that work with
    /// the Quest controller rays (TrackedDeviceGraphicRaycaster) and with the mouse in desktop mode.
    /// </summary>
    public static class ChemLabUIKit
    {
        // ---------- palette ----------
        public static readonly Color Bg = new Color(0.055f, 0.075f, 0.105f, 0.975f);
        public static readonly Color Surface = new Color(0.11f, 0.14f, 0.19f, 1f);
        public static readonly Color SurfaceHi = new Color(0.16f, 0.2f, 0.27f, 1f);
        public static readonly Color Stroke = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Accent = new Color(0.0f, 0.78f, 0.68f, 1f);     // teal
        public static readonly Color AccentBlue = new Color(0.24f, 0.52f, 1f, 1f);
        public static readonly Color Ok = new Color(0.3f, 0.85f, 0.5f, 1f);
        public static readonly Color Warn = new Color(1f, 0.74f, 0.25f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.36f, 0.36f, 1f);
        public static readonly Color Text = new Color(0.95f, 0.97f, 0.99f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.68f, 0.76f, 1f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.35f);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ---------- mouse support (desktop fallback) ----------
        public static bool MouseMode { get; private set; }
        public static event Action MouseModeEnabled;
        static readonly List<Canvas> canvases = new List<Canvas>();

        public static void EnableMouse()
        {
            if (MouseMode) return;
            MouseMode = true;
            canvases.RemoveAll(c => c == null);
            foreach (var c in canvases)
                if (c.GetComponent<GraphicRaycaster>() == null) c.gameObject.AddComponent<GraphicRaycaster>();
            MouseModeEnabled?.Invoke();
        }

        // ---------- rounded sprite ----------
        static Sprite rounded, circle;

        public static Sprite Rounded
        {
            get
            {
                if (rounded == null) rounded = MakeRounded(64, 22);
                return rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = MakeRounded(64, 32, false);
                return circle;
            }
        }

        static Sprite MakeRounded(int size, int radius, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        // ---------- builders ----------

        /// <summary>A world-space panel: drop shadow, rounded glass background, thin accent line on top.</summary>
        public static Canvas Panel(Transform parent, string name, Vector2 size, float scale = 0.001f, bool accentLine = true, bool blocksRays = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = Camera.main;
            c.sortingOrder = 10;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            if (MouseMode) go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;

            var shadow = Img(go.transform, "Shadow", new Vector2(0, -10), size + new Vector2(24, 24), Shadow, Rounded);
            shadow.raycastTarget = false;
            var bg = Img(go.transform, "Background", Vector2.zero, size, Bg, Rounded);
            bg.raycastTarget = blocksRays;
            var stroke = Img(go.transform, "Stroke", Vector2.zero, size + new Vector2(3, 3), Stroke, Rounded);
            stroke.raycastTarget = false;
            stroke.transform.SetSiblingIndex(1);
            if (accentLine)
            {
                var line = Img(go.transform, "AccentLine", new Vector2(0, size.y / 2f - 6f), new Vector2(size.x * 0.3f, 6f), Accent, Rounded);
                line.raycastTarget = false;
            }
            canvases.Add(c);
            return c;
        }

        public static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color col, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = col;
            if (sprite != null) { img.sprite = sprite; img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple; img.pixelsPerUnitMultiplier = 1f; }
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize,
            Color col, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            if (fontSize < MinReadableSize)
            {
                // grow small text up to the readable size wherever its box has room; never smaller than requested
                t.enableAutoSizing = true;
                t.fontSizeMin = fontSize;
                t.fontSizeMax = MinReadableSize;
            }
            t.color = col;
            t.alignment = align;
            t.fontStyle = style;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Body text target in canvas units: about 1.2 deg at 1.3 m with the default 0.001 panel scale.</summary>
        public const float MinReadableSize = 28f;

        public enum Style { Primary, Secondary, Ghost, Danger }

        public static Button Button(Transform parent, string name, string label, Vector2 pos, Vector2 size, Style style, UnityAction onClick,
            float fontSize = 30, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            Color col = style == Style.Primary ? Accent : style == Style.Danger ? Bad : style == Style.Secondary ? AccentBlue : SurfaceHi;
            var img = Img(parent, name, pos, size, col, Rounded);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.75f, 0.75f, 0.8f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.55f, 0.6f);
            cb.colorMultiplier = 1.2f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => ChemLabAudio.Click());
            var textCol = style == Style.Primary ? new Color(0.02f, 0.1f, 0.1f) : Text;
            var lbl = Label(img.transform, "Label", label, Vector2.zero, size - new Vector2(40, 8), fontSize, textCol, align, style == Style.Primary ? FontStyles.Bold : FontStyles.Normal);
            return b;
        }

        public static TextMeshProUGUI ButtonLabel(Button b) => b.GetComponentInChildren<TextMeshProUGUI>();

        public static void SetButtonColor(Button b, Color c) => b.GetComponent<Image>().color = c;

        public static TextMeshProUGUI Chip(Transform parent, string name, string text, Vector2 pos, Vector2 size, Color col, Color textCol)
        {
            var img = Img(parent, name, pos, size, col, Rounded);
            img.raycastTarget = false;
            return Label(img.transform, "Text", text, Vector2.zero, size, size.y * 0.5f, textCol, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        public class Bar
        {
            public RectTransform fill;
            public Image fillImage;
            public float width;
            public void Set(float t)
            {
                t = Mathf.Clamp01(t);
                fill.sizeDelta = new Vector2(Mathf.Max(fill.sizeDelta.y, width * t), fill.sizeDelta.y);
                fill.gameObject.SetActive(t > 0.001f);
            }
        }

        public static Bar ProgressBar(Transform parent, string name, Vector2 pos, Vector2 size, Color fillCol)
        {
            var track = Img(parent, name, pos, size, new Color(1, 1, 1, 0.08f), Rounded);
            track.raycastTarget = false;
            var fill = Img(track.transform, "Fill", Vector2.zero, size, fillCol, Rounded);
            fill.raycastTarget = false;
            var rt = fill.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var bar = new Bar { fill = rt, fillImage = fill, width = size.x };
            bar.Set(0f);
            return bar;
        }

        /// <summary>Place a panel in front of the head, facing it (optionally in the direction of a world point).</summary>
        public static void PlaceInFront(Transform panel, float dist, float yOffset, Vector3? towards = null)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            Vector3 fwd = towards.HasValue ? towards.Value - head.position : head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            // Straight ahead first; when that is cramped (standing against a shelf), turn up to 50 deg either way for room.
            Vector3 bestPos = Vector3.zero; Quaternion bestRot = Quaternion.identity; float best = -1f;
            foreach (var yaw in SideYaws)
            {
                var dir = Quaternion.Euler(0f, yaw, 0f) * fwd;
                var pos = head.position + dir * dist + Vector3.up * yOffset;
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                float t = ClearFraction(panel, head.position, pos, rot);
                if (t > best + 0.1f) { best = t; bestRot = rot; bestPos = head.position + (pos - head.position) * Mathf.Max(t, MinFraction); }
                if (best >= 1f) break;
            }
            panel.SetPositionAndRotation(bestPos, bestRot);
            panel.localScale = Vector3.one * BaseScale(panel) * Mathf.Max(best, MinFraction);
        }

        static readonly float[] SideYaws = { 0f, 25f, -25f, 50f, -50f };

        // ---------- keep panels out of walls, benches and shelves ----------
        const float ClearMargin = 0.05f;   // gap left between a pulled-in panel and the obstacle
        const float MinFraction = 0.35f;   // never closer than 35% of the requested distance
        static readonly Dictionary<Transform, float> baseScales = new Dictionary<Transform, float>();
        static readonly RaycastHit[] hitBuf = new RaycastHit[16];

        static float BaseScale(Transform panel)
        {
            if (!baseScales.TryGetValue(panel, out var s)) baseScales[panel] = s = panel.localScale.x;
            return s;
        }

        /// <summary>
        /// How far (0..1 of the way from the head) a panel at pos/rot can go before something solid cuts through it.
        /// Rays from the head to a 7 x 5 grid on the panel; the panel is later pulled in AND shrunk by this factor,
        /// so it keeps the same apparent size and just sits in front of the obstacle.
        /// </summary>
        static float ClearFraction(Transform panel, Vector3 head, Vector3 pos, Quaternion rot)
        {
            var half = ((RectTransform)panel).rect.size * BaseScale(panel) * 0.5f;
            float t = 1f;
            for (int i = 0; i <= 6; i++)
                for (int j = 0; j <= 4; j++)
                {
                    var d = pos + rot * new Vector3(Mathf.Lerp(-half.x, half.x, i / 6f), Mathf.Lerp(-half.y, half.y, j / 4f), 0f) - head;
                    float len = d.magnitude;
                    int n = Physics.RaycastNonAlloc(head, d / len, hitBuf, len + ClearMargin, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++)
                        if (!(hitBuf[k].collider is CharacterController)) t = Mathf.Min(t, (hitBuf[k].distance - ClearMargin) / len);
                }
            // Thin things (shelf boards, edges) can slip between the rays: step closer while anything still pokes through the panel.
            t = Mathf.Max(t, MinFraction);
            while (t > MinFraction && Pierced(head + (pos - head) * t, new Vector3(half.x * t, half.y * t, ClearMargin), rot)) t -= 0.05f;
            t = Mathf.Clamp(t, MinFraction, 1f);
            // 0 = blocked even at the closest allowed spot (callers still place it at MinFraction, but prefer another direction)
            return Pierced(head + (pos - head) * t, new Vector3(half.x * t, half.y * t, ClearMargin), rot) ? 0f : t;
        }

        static readonly Collider[] overlapBuf = new Collider[16];

        static bool Pierced(Vector3 center, Vector3 halfExtents, Quaternion rot)
        {
            int n = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuf, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++) if (!(overlapBuf[k] is CharacterController)) return true;
            return false;
        }

        /// <summary>Smoothly bring a panel back in front of the player if they turned away or walked off.</summary>
        public static void KeepInView(Transform panel, float dist, float yOffset)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null || panel == null || !panel.gameObject.activeInHierarchy) return;
            Vector3 toPanel = panel.position - head.position;
            Vector3 flatFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 1e-4f) return;
            float angle = Vector3.Angle(Vector3.ProjectOnPlane(toPanel, Vector3.up), flatFwd);
            // ponytail: 1.6 m keeps 24pt text near 0.9 deg (VR minimum); farther and it stops being readable
            if (angle < 55f && toPanel.magnitude < 1.6f && Mathf.Abs(toPanel.y) < 1.2f) return;
            flatFwd.Normalize();
            Vector3 target = head.position + flatFwd * dist + Vector3.up * yOffset;
            float t = Mathf.Max(ClearFraction(panel, head.position, target, Quaternion.LookRotation(flatFwd)), MinFraction);
            target = head.position + (target - head.position) * t;
            panel.position = Vector3.Lerp(panel.position, target, Time.deltaTime * 4f);
            panel.localScale = Vector3.Lerp(panel.localScale, Vector3.one * BaseScale(panel) * t, Time.deltaTime * 4f);
            Vector3 look = panel.position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.Slerp(panel.rotation, Quaternion.LookRotation(look), Time.deltaTime * 4f);
        }

        public static string Mmss(float s)
        {
            s = Mathf.Max(0, s);
            return $"{(int)(s / 60):00}:{(int)(s % 60):00}";
        }
    }
}
