using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// The 4-option hazard question plate: yellow warning band naming the hazard, ruled answer rows in sign grammar,
    /// a feedback band (info / wrong / correct sign + text) and Close (B) / Submit (A). Shared by the lab (ChemLabUI) and the lobby's practice question;
    /// the owner decides what selecting, submitting and closing do.
    /// </summary>
    public class ChemLabQuestionPanel
    {
        public readonly Canvas canvas;
        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;
        /// <summary>The right answer is showing; the owner closes the plate after ResolvedHold.</summary>
        public bool Resolved => correctKey != null;
        /// <summary>How long the plate holds the green check before it closes.</summary>
        public const float ResolvedHold = 1.2f;

        readonly TextMeshProUGUI title, meta, text, feedback;
        readonly Opt[] opts = new Opt[4];
        readonly Button submit;
        readonly Image fbInfo, fbBad, fbOk;   // feedback band sign: hint / wrong / correct
        QuestionData q;
        string correctKey;
        readonly Dictionary<string, bool> struck = new Dictionary<string, bool>();   // ruled-out key -> was it dangerous

        /// <summary>One answer row: a ruled line with a sign disc (key letter) and the option text.</summary>
        class Opt
        {
            public Button btn; public Image bg, outline, disc, rim, slash, check; public TextMeshProUGUI key, text; public GameObject tag;
        }

        const float OptTextX = 46f, OptTextW = 804f;   // option text column: right of the 68-unit disc to the row's right padding

        public ChemLabQuestionPanel(Transform parent, Action<string> onSelect, Action onSubmit, Action onClose)
        {
            var size = new Vector2(1000, 880);
            canvas = K.Panel(parent, "Panel_Question", size, accentLine: false);
            var t = canvas.transform;

            // yellow warning band naming the hazard
            K.Band(t, size, 150f, K.Warn);
            K.WarningSign(t, new Vector2(-404, 365), 112f);
            title = K.Label(t, "Title", "", new Vector2(74, 384), new Vector2(772, 58), 46, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.FitOneLine(title, 28);   // long translated titles shrink instead of wrapping out of the band
            meta = K.FitOneLine(K.Label(t, "Meta", "", new Vector2(74, 332), new Vector2(772, 36), 28, K.Text, TextAlignmentOptions.Left), 22);

            // vertically centred: a one-line question sits in the middle of its two-line box instead of over a gap
            text = K.Label(t, "Question", "", new Vector2(0, 222), new Vector2(920, 84), 31, K.Text, TextAlignmentOptions.Left);
            text.enableAutoSizing = true; text.fontSizeMin = 26; text.fontSizeMax = 31;   // a third line in long languages shrinks instead of hitting the rule

            K.Img(t, "RuleTop", new Vector2(0, 160), new Vector2(920, 2), K.Rule).raycastTarget = false;
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                float y = 160 - 49 - i * 98;
                var o = opts[i] = new Opt();
                o.bg = K.Img(t, "Option" + i, new Vector2(0, y), new Vector2(920, 94), K.Bg, K.Rounded);
                o.bg.pixelsPerUnitMultiplier = 1.5f;
                o.btn = o.bg.gameObject.AddComponent<Button>();
                K.Tint(o.btn);
                o.btn.onClick.AddListener(() => { if (q != null && idx < q.options.Length) onSelect(OptAt(idx).key); });
                o.btn.onClick.AddListener(() => ChemLabAudio.Click());
                o.outline = K.Outline(o.bg.transform, new Vector2(920, 94), K.Accent, 1.5f);

                var discPos = new Vector2(-414, 0);
                o.disc = K.Img(o.bg.transform, "Disc", discPos, new Vector2(68, 68), Color.white, K.Circle);
                o.rim = K.Img(o.bg.transform, "Rim", discPos, new Vector2(68, 68), K.Text, K.Ring);
                o.key = K.Label(o.bg.transform, "Key", "", discPos + new Vector2(0, -1), new Vector2(68, 68), 34, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
                o.slash = K.Img(o.bg.transform, "Prohibited", discPos, new Vector2(68, 68), K.Bad, K.Prohibition);
                o.check = K.Img(o.bg.transform, "Check", discPos, new Vector2(56, 56), Color.white, K.Check);
                o.disc.raycastTarget = o.rim.raycastTarget = o.slash.raycastTarget = o.check.raycastTarget = false;

                o.text = K.Label(o.bg.transform, "Text", "", new Vector2(OptTextX, 0), new Vector2(OptTextW, 92), 28, K.Text, TextAlignmentOptions.MidlineLeft);
                o.text.enableAutoSizing = true; o.text.fontSizeMin = 22; o.text.fontSizeMax = 28;
                o.tag = K.Chip(o.bg.transform, "Dangerous", L10n.T("q.dangerous"), new Vector2(363, 0), new Vector2(170, 46), K.Bad, Color.white).transform.parent.gameObject;
                K.Img(t, "Rule" + i, new Vector2(0, y - 49), new Vector2(920, 2), K.Rule).raycastTarget = false;
            }

            // feedback band between the last option rule (-232) and the buttons (-356); the sign on the left says what kind of line it is
            var fbPos = new Vector2(-422, -289);
            var band = K.Img(t, "FeedbackBand", new Vector2(0, -289), new Vector2(920, 94), K.Surface, K.Rounded);
            band.pixelsPerUnitMultiplier = 1.5f;
            band.raycastTarget = false;
            fbInfo = K.SignDisc(t, "FbInfo", fbPos, 48, K.Accent, check: false, info: true);
            fbBad = K.SignDisc(t, "FbWrong", fbPos, 48, K.Bad, check: false);
            fbOk = K.SignDisc(t, "FbCorrect", fbPos, 48, K.Ok, check: true);
            feedback = K.Label(t, "Feedback", "", new Vector2(31, -289), new Vector2(830, 86), 28, K.Text, TextAlignmentOptions.Left);
            feedback.enableAutoSizing = true; feedback.fontSizeMin = 22; feedback.fontSizeMax = 28;
            // Close is the quiet way out (narrow, soft rim); Submit takes the room because it is the action
            K.Button(t, "Close", L10n.T("common.close"), new Vector2(-315, -389), new Vector2(290, 66), K.Style.Ghost, () => onClose(), key: "B");
            submit = K.Button(t, "Submit", L10n.T("q.submit"), new Vector2(155, -389), new Vector2(610, 66), K.Style.Primary, () => onSubmit(), key: "A");
            canvas.gameObject.SetActive(false);
        }

        /// <summary>Option keys in the order the rows show them (single-letter keys), e.g. "CADB".</summary>
        public string Order { get; private set; }

        /// <summary>
        /// Open the plate for one question, in front of the head and turned towards the object. The options are shuffled
        /// (pass a previous Order to keep it when the same question reopens); rows are lettered A-D by position, while
        /// selection, scoring and export keep the option's own key.
        /// </summary>
        public void Show(string titleText, string metaText, QuestionData question, Vector3 towards, string order = null)
        {
            q = question;
            struck.Clear();
            correctKey = null;
            if (order == null || order.Length != q.options.Length)
                order = string.Concat(q.options.Select(o => o.key).OrderBy(_ => UnityEngine.Random.value));
            Order = order;
            title.text = titleText;
            meta.text = metaText;
            text.text = q.question;
            for (int i = 0; i < 4; i++)
            {
                bool has = i < q.options.Length;
                opts[i].btn.gameObject.SetActive(has);
                if (!has) continue;
                opts[i].key.text = ((char)('A' + i)).ToString();
                opts[i].text.text = OptAt(i).text;
                Paint(i, null);
            }
            SetFeedback(fbInfo, L10n.T("q.hint"));
            SetSubmit(null);
            K.PlaceInFront(canvas.transform, 0.95f, -0.05f, towards);
            canvas.gameObject.SetActive(true);
        }

        public void Highlight(string key)
        {
            if (q == null) return;
            for (int i = 0; i < q.options.Length; i++) Paint(i, key);
            SetSubmit(key);
        }

        /// <summary>Rule the option out (red prohibition ring, struck text, DANGEROUS plate if critical) and say why.</summary>
        public void ShowWrong(string key, bool critical, string wrongFeedback)
        {
            if (q == null) return;
            struck[key] = critical;
            for (int i = 0; i < q.options.Length; i++) Paint(i, null);
            SetFeedback(fbBad, $"<color={K.Hex(K.Bad)}><b>{wrongFeedback}</b></color> " +
                               (critical ? L10n.T("q.wrongCritical") : L10n.T("q.wrong")));
            SetSubmit(null);
        }

        /// <summary>
        /// The right answer: its row turns into a green check sign, the band gives the why, and every row and Submit
        /// go quiet. The owner closes the plate after ResolvedHold, so the player sees the consequence first.
        /// </summary>
        public void ShowCorrect(string key, string line)
        {
            if (q == null) return;
            correctKey = key;
            for (int i = 0; i < q.options.Length; i++) Paint(i, null);
            SetFeedback(fbOk, line);
            SetSubmit(null);
        }

        /// <summary>A neutral note in the feedback band (info sign), e.g. a tutorial instruction about this question.</summary>
        public void ShowNote(string text) => SetFeedback(fbInfo, text);

        void SetFeedback(Image sign, string line)
        {
            fbInfo.gameObject.SetActive(sign == fbInfo);
            fbBad.gameObject.SetActive(sign == fbBad);
            fbOk.gameObject.SetActive(sign == fbOk);
            feedback.text = line ?? "";
        }

        public void Close()
        {
            canvas.gameObject.SetActive(false);
            q = null;
            correctKey = null;
        }

        /// <summary>
        /// Sign grammar per row: plain rim = open, blue mandatory disc = your pick,
        /// red prohibition ring + struck text = ruled out (plus a DANGEROUS plate when it was a critical choice),
        /// green check sign on a green row = the right answer.
        /// </summary>
        void Paint(int i, string selectedKey)
        {
            var o = opts[i];
            string k = OptAt(i).key;
            bool isStruck = struck.TryGetValue(k, out bool dangerous);
            bool ok = k == correctKey;
            bool sel = !isStruck && !ok && k == selectedKey;
            o.btn.interactable = !isStruck && correctKey == null;
            o.bg.color = ok ? K.OkFill : sel ? K.SelectFill : K.Bg;
            o.outline.gameObject.SetActive(sel || ok);
            o.outline.color = ok ? K.Ok : K.Accent;
            o.disc.color = ok ? K.Ok : sel ? K.Accent : Color.white;
            o.rim.gameObject.SetActive(!sel && !isStruck && !ok);
            o.slash.gameObject.SetActive(isStruck);
            o.check.gameObject.SetActive(ok);
            o.key.gameObject.SetActive(!ok);
            o.key.color = sel ? Color.white : K.Text;
            o.text.color = isStruck ? K.TextDim : K.Text;
            o.text.fontStyle = isStruck ? FontStyles.Strikethrough : FontStyles.Normal;
            o.tag.SetActive(isStruck && dangerous);
            float cut = o.tag.activeSelf ? 186f : 0f;   // make room for the DANGEROUS plate
            o.text.rectTransform.sizeDelta = new Vector2(OptTextW - cut, 92f);
            o.text.rectTransform.anchoredPosition = new Vector2(OptTextX - cut / 2f, 0);
        }

        /// <summary>The option shown in row i.</summary>
        OptionData OptAt(int i) => q.Option(Order[i].ToString());

        void SetSubmit(string key)
        {
            submit.interactable = key != null;
            K.SetButtonColor(submit, key != null ? K.Ok : K.SurfaceHi);
            K.ButtonLabel(submit).text = key != null ? L10n.T("q.submitKey", (char)('A' + Order.IndexOf(key, StringComparison.Ordinal))) : L10n.T("q.submit");
            var ink = key != null ? Color.white : K.TextDim;
            foreach (var g in submit.GetComponentsInChildren<Graphic>(true))
                if (g != submit.targetGraphic) g.color = ink;
        }
    }
}
