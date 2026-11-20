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
    /// Small shared UI framework for every ChemLab scene, styled as lab safety sign plates (ISO 7010 / ISO 3864):
    /// enamel-white plates, black ink, and the four safety colours used only for their sign meaning
    /// (yellow warning, blue mandatory/selected, red prohibited/dangerous, green safe/go).
    /// Built in code as world-space canvases that work with the Quest controller rays
    /// (TrackedDeviceGraphicRaycaster) and with the mouse in desktop mode.
    /// </summary>
    public static class ChemLabUIKit
    {
        // ---------- palette (ISO 3864 safety colours, approx. RAL 1003 / 5005 / 3001 / 6032) ----------
        public static readonly Color Bg = Rgb("#FBFBF9");          // sign plate
        public static readonly Color Surface = Rgb("#EEEEEA");
        public static readonly Color SurfaceHi = Rgb("#DCDCD6");
        public static readonly Color Rule = Rgb("#D8D8D2");
        public static readonly Color Stroke = new Color(0.08f, 0.08f, 0.08f, 0.22f);
        public static readonly Color Accent = Rgb("#154889");      // safety blue: mandatory / selected / info
        public static readonly Color AccentBlue = Accent;
        public static readonly Color SelectFill = Rgb("#E3EBF5");
        public static readonly Color Ok = Rgb("#237F52");          // safety green: safe / go
        public static readonly Color Warn = Rgb("#F9A800");        // safety yellow: fields only, never text on the plate
        public static readonly Color Bad = Rgb("#A52019");         // safety red: prohibited / dangerous
        public static readonly Color Text = Rgb("#141414");
        public static readonly Color TextDim = Rgb("#5A5A55");
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.28f);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static Color Rgb(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

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

        // ---------- sign shapes, baked once from signed-distance functions (white, tinted per use) ----------
        static Sprite rounded, roundedOutline, softShadow, circle, ring, prohibition, check, triFill, triBorder, exclaim;

        public static Sprite Rounded => Get(ref rounded, () => Bake(64, 64, p => SdRoundRect(p, new Vector2(32, 32), new Vector2(32, 32), 22), 22));
        public static Sprite RoundedOutline => Get(ref roundedOutline, () => Bake(64, 64, p => Mathf.Abs(SdRoundRect(p, new Vector2(32, 32), new Vector2(32, 32), 22) + 3f) - 3f, 22));
        static Sprite SoftShadow => Get(ref softShadow, () => Bake(128, 128, p => SdRoundRect(p, new Vector2(64, 64), new Vector2(34, 34), 22), 52, 28f));
        public static Sprite Circle => Get(ref circle, () => Bake(128, 128, p => (p - C128).magnitude - 63f));
        /// <summary>Plain sign rim (neutral answer key, controller-button disc).</summary>
        public static Sprite Ring => Get(ref ring, () => Bake(128, 128, p => Mathf.Abs((p - C128).magnitude - 58.5f) - 4.5f));
        /// <summary>ISO prohibition: red ring with a slash from top-left to bottom-right.</summary>
        public static Sprite Prohibition => Get(ref prohibition, () => Bake(128, 128, p =>
            Mathf.Min(Mathf.Abs((p - C128).magnitude - 56f) - 7f, SegDist(p, C128 + new Vector2(-39.6f, 39.6f), C128 + new Vector2(39.6f, -39.6f)) - 6f)));
        public static Sprite Check => Get(ref check, () => Bake(64, 64, p => Mathf.Min(SegDist(p, new Vector2(16, 33), new Vector2(27, 21)), SegDist(p, new Vector2(27, 21), new Vector2(48, 44))) - 4f));
        static Sprite Exclaim => Get(ref exclaim, () => Bake(32, 96, p => Mathf.Min(SegDist(p, new Vector2(16, 84), new Vector2(16, 38)) - 6f, (p - new Vector2(16, 14)).magnitude - 7f)));
        static Sprite TriFill => Get(ref triFill, () => Bake(128, 112, SdWarningTriangle));
        static Sprite TriBorder => Get(ref triBorder, () => Bake(128, 112, p => Mathf.Abs(SdWarningTriangle(p) + 5.5f) - 5.5f));

        static Sprite Get(ref Sprite s, Func<Sprite> make) { if (s == null) s = make(); return s; }   // Unity null: survives asset unloads

        static readonly Vector2 C128 = new Vector2(64, 64);

        /// <summary>Alpha = inside of the distance field (pixels, negative inside), anti-aliased over `soft` pixels.</summary>
        static Sprite Bake(int w, int h, Func<Vector2, float> sdf, float border = 0f, float soft = 1f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - sdf(new Vector2(x + 0.5f, y + 0.5f)) / soft) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.one * border);
        }

        static float SdRoundRect(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + Vector2.one * r;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Equilateral warning triangle with rounded corners (an inset triangle grown by 8 px).</summary>
        static float SdWarningTriangle(Vector2 p)
        {
            var c = new Vector2(64, 38.7f);
            Vector2 a = c + (new Vector2(64, 108) - c) * 0.77f, b = c + (new Vector2(124, 4) - c) * 0.77f, d = c + (new Vector2(4, 4) - c) * 0.77f;
            float dist = Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, d), SegDist(p, d, a)));
            float s1 = Cross(b - a, p - a), s2 = Cross(d - b, p - b), s3 = Cross(a - d, p - d);
            bool inside = (s1 < 0 && s2 < 0 && s3 < 0) || (s1 > 0 && s2 > 0 && s3 > 0);
            return (inside ? -dist : dist) - 8f;
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        // ---------- builders ----------

        /// <summary>A world-space sign plate: soft drop shadow, dark rim, enamel-white plate, blue header band on top.</summary>
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

            var shadow = Img(go.transform, "Shadow", new Vector2(0, -12), size + new Vector2(60, 60), Shadow, SoftShadow);
            shadow.raycastTarget = false;
            var bg = Img(go.transform, "Background", Vector2.zero, size, Bg, Rounded);
            bg.raycastTarget = blocksRays;
            var stroke = Img(go.transform, "Stroke", Vector2.zero, size + new Vector2(4, 4), Stroke, Rounded);
            stroke.raycastTarget = false;
            stroke.transform.SetSiblingIndex(1);
            if (accentLine) Band(go.transform, size, 24f, Accent);
            canvases.Add(c);
            return c;
        }

        /// <summary>
        /// Full-width colour band across the top of a plate (rounded top corners, square bottom),
        /// like the header field of a safety sign. Children laid out from the band's centre.
        /// </summary>
        public static RectTransform Band(Transform panel, Vector2 panelSize, float height, Color col)
        {
            float r = Rounded.border.x, top = panelSize.y / 2f;
            float h = Mathf.Max(height, 2f * r);   // a sliced sprite needs both corners' height
            var band = Img(panel, "Band", new Vector2(0, top - h / 2f), new Vector2(panelSize.x, h), col, Rounded);
            band.raycastTarget = false;
            // shorter than two corners: hide the rest under plate colour; taller: square off the bottom corners
            var fix = height < h
                ? Img(panel, "BandCover", new Vector2(0, top - height - (h - height) / 2f), new Vector2(panelSize.x, h - height), Bg)
                : Img(panel, "BandBase", new Vector2(0, top - height + r / 2f), new Vector2(panelSize.x, r), col);
            fix.raycastTarget = false;
            var root = new GameObject("BandContent", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(panel, false);
            root.anchoredPosition = new Vector2(0, top - height / 2f);
            root.sizeDelta = new Vector2(panelSize.x, height);
            return root;
        }

        /// <summary>A rounded outline drawn over `target` (same size); ppuMul matches the target's corner radius.</summary>
        public static Image Outline(Transform target, Vector2 size, Color col, float ppuMul = 1f)
        {
            var o = Img(target, "Outline", Vector2.zero, size, col, RoundedOutline);
            o.pixelsPerUnitMultiplier = ppuMul;
            o.raycastTarget = false;
            return o;
        }

        /// <summary>ISO warning sign: yellow triangle, black rim, exclamation mark. Returns the sign root.</summary>
        public static RectTransform WarningSign(Transform parent, Vector2 pos, float width)
        {
            var root = Img(parent, "WarningSign", pos, new Vector2(width, width * 0.875f), Warn, TriFill);
            root.raycastTarget = false;
            Img(root.transform, "Rim", Vector2.zero, root.rectTransform.sizeDelta, Text, TriBorder).raycastTarget = false;
            Img(root.transform, "Mark", new Vector2(0, -width * 0.07f), new Vector2(width * 0.15f, width * 0.45f), Text, Exclaim).raycastTarget = false;
            return root.rectTransform;
        }

        /// <summary>Round sign: filled disc with a white glyph (Check, or the exclamation mark; rotate 180 deg for "i").</summary>
        public static Image SignDisc(Transform parent, string name, Vector2 pos, float d, Color col, bool check, bool info = false)
        {
            var disc = Img(parent, name, pos, new Vector2(d, d), col, Circle);
            disc.raycastTarget = false;
            var g = check ? Img(disc.transform, "Glyph", Vector2.zero, new Vector2(d * 0.8f, d * 0.8f), Color.white, Check)
                          : Img(disc.transform, "Glyph", Vector2.zero, new Vector2(d * 0.17f, d * 0.52f), Color.white, Exclaim);
            g.raycastTarget = false;
            if (info) g.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
            return disc;
        }

        /// <summary>A controller-button key in a sign rim ("A", "B"); ring and letter take `col`.</summary>
        public static Image KeyDisc(Transform parent, string key, Vector2 pos, float d, Color col)
        {
            var rim = Img(parent, "Key_" + key, pos, new Vector2(d, d), col, Ring);
            rim.raycastTarget = false;
            Label(rim.transform, "Letter", key, new Vector2(0, -d * 0.02f), new Vector2(d, d), d * 0.55f, col, TextAlignmentOptions.Center, FontStyles.Bold);
            return rim;
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

        /// <summary>
        /// Primary = green (go), Secondary = blue, Danger = red, Ghost = outlined plate. Optional `key` draws the
        /// controller button ("A", "B") as a sign rim at the left end.
        /// </summary>
        public static Button Button(Transform parent, string name, string label, Vector2 pos, Vector2 size, Style style, UnityAction onClick,
            float fontSize = 30, TextAlignmentOptions align = TextAlignmentOptions.Center, string key = null)
        {
            Color col = style == Style.Primary ? Ok : style == Style.Danger ? Bad : style == Style.Secondary ? Accent : Bg;
            var img = Img(parent, name, pos, size, col, Rounded);
            img.pixelsPerUnitMultiplier = 2f;   // ~11-unit corners, tighter than the plate's
            var b = img.gameObject.AddComponent<Button>();
            Tint(b);
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => ChemLabAudio.Click());
            var textCol = style == Style.Ghost ? Text : Color.white;
            if (style == Style.Ghost) Outline(img.transform, size, Text, 2f);
            float inset = key != null ? 70f : 0f;
            if (key != null) KeyDisc(img.transform, key, new Vector2(-size.x / 2f + 24f + 20f, 0), 40f, textCol);
            Label(img.transform, "Label", label, new Vector2(inset / 2f, 0), size - new Vector2(40 + inset, 8), fontSize, textCol, align, FontStyles.Bold);
            return b;
        }

        /// <summary>Hover/press darken (works on light plates and on colour fields alike); disabled styling is up to the caller.</summary>
        public static void Tint(Selectable s)
        {
            var cb = s.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.87f, 0.87f, 0.87f);
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.74f, 0.74f, 0.74f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            s.colors = cb;
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
            var track = Img(parent, name, pos, size, SurfaceHi, Rounded);
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
