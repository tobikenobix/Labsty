using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// Scene 2 UI: wrist tablet (category checklist + countdown), the reusable 4-option hazard panel,
    /// toasts, finish prompt and start/end banners. Everything is generated with ChemLabUIKit.
    /// </summary>
    public class ChemLabUI : MonoBehaviour
    {
        [Tooltip("Left controller transform; the tablet HUD is attached to it.")]
        public Transform leftHandAnchor;
        [Tooltip("Optional wall-mounted Finish panel position.")]
        public Transform finishStationAnchor;

        ChemLabSession S => ChemLabSession.Instance;

        // question panel
        Canvas question;
        TextMeshProUGUI qTitle, qMeta, qText, qFeedback;
        readonly Opt[] qOpt = new Opt[4];
        Button qSubmit;
        HazardRecord qRec;
        readonly Dictionary<string, bool> qStruck = new Dictionary<string, bool>();   // ruled-out key -> was it dangerous

        /// <summary>One answer row: a ruled line with a sign disc (key letter) and the option text.</summary>
        class Opt
        {
            public Button btn; public Image bg, outline, disc, rim, slash; public TextMeshProUGUI key, text; public GameObject tag;
        }

        // message panel
        Canvas msg; TextMeshProUGUI msgTitle, msgBody; Button msgBtn1, msgBtn2; System.Action msgAct1, msgAct2;

        // toast / banner / help / tablet / station
        Canvas toast; TextMeshProUGUI toastText; Coroutine toastCo;
        GameObject toastOk, toastInfo, toastBad, toastWarn;
        Canvas help;
        Canvas tablet; TextMeshProUGUI tabTimer, tabFooter; K.Bar tabTimeBar;
        readonly List<(string cat, TextMeshProUGUI count, K.Bar bar)> tabRows = new List<(string, TextMeshProUGUI, K.Bar)>();
        Canvas station;

        public bool QuestionOpen => question != null && question.gameObject.activeSelf;
        public bool ModalOpen => QuestionOpen || (msg != null && msg.gameObject.activeSelf);

        void Awake()
        {
            BuildQuestion();
            BuildMessage();
            BuildToast();
            BuildHelp();
            BuildTablet();
            BuildStation();
            K.MouseModeEnabled += AttachTablet;
        }

        void OnDestroy() => K.MouseModeEnabled -= AttachTablet;
        void OnEnable() => ChemLabControls.ToggleHelp += ToggleHelp;
        void OnDisable() => ChemLabControls.ToggleHelp -= ToggleHelp;

        int hudSecond = -1;

        void Update()
        {
            // The timer only changes once per second; inspect/resolve call UpdateHud themselves.
            if (S != null && tablet.gameObject.activeSelf && (int)S.Remaining != hudSecond)
            {
                hudSecond = (int)S.Remaining;
                UpdateHud();
            }
            K.KeepInView(question.transform, 0.95f, -0.05f);
            K.KeepInView(msg.transform, 1.15f, 0f);
        }

        // ================= API used by ChemLabSession =================

        public void ShowRunStarted(float seconds)
        {
            tablet.gameObject.SetActive(true);
            station.gameObject.SetActive(true);
            UpdateHud();
            Toast($"Inspection started: {K.Mmss(seconds)} on the clock. Find all 10 hazards!", 3.5f, K.Accent);
        }

        public void ShowRunEnded(string title)
        {
            tablet.gameObject.SetActive(false);
            station.gameObject.SetActive(false);
            help.gameObject.SetActive(false);
            ShowMessage(title, "Calculating your results...", null, null);
        }

        public void ShowQuestion(HazardRecord rec, Vector3 hazardPos)
        {
            qRec = rec;
            qStruck.Clear();
            qTitle.text = rec.data.title;
            qMeta.text = $"{rec.data.hudCategory}  /  {rec.data.area}  /  {rec.data.difficulty}";
            qText.text = rec.data.question;
            for (int i = 0; i < 4; i++)
            {
                bool has = i < rec.data.options.Length;
                qOpt[i].btn.gameObject.SetActive(has);
                if (!has) continue;
                qOpt[i].key.text = rec.data.options[i].key;
                qOpt[i].text.text = rec.data.options[i].text;
                PaintOption(i, null);
            }
            qFeedback.text = $"<color={K.Hex(K.TextDim)}>Point at an answer and pull the trigger, then press A to submit.</color>";
            SetSubmit(null);
            K.PlaceInFront(question.transform, 0.95f, -0.05f, hazardPos);
            question.gameObject.SetActive(true);
        }

        public void HighlightOption(string key)
        {
            if (qRec == null) return;
            for (int i = 0; i < qRec.data.options.Length; i++) PaintOption(i, key);
            SetSubmit(key);
        }

        public void ShowWrong(HazardRecord rec, string key, bool critical)
        {
            qStruck[key] = critical;
            for (int i = 0; i < rec.data.options.Length; i++) PaintOption(i, null);
            qFeedback.text = $"<color={K.Hex(K.Bad)}><b>{rec.data.wrongFeedback}</b></color> " +
                             (critical ? "Dangerous choice, logged for safety review. Choose again." : "Logged as a mistake. Choose again.");
            SetSubmit(null);
        }

        /// <summary>
        /// Sign grammar per row: plain rim = open, blue mandatory disc = your pick,
        /// red prohibition ring + struck text = ruled out (plus a DANGEROUS plate when it was a critical choice).
        /// </summary>
        void PaintOption(int i, string selectedKey)
        {
            var o = qOpt[i];
            string k = qRec.data.options[i].key;
            bool struck = qStruck.TryGetValue(k, out bool dangerous);
            bool sel = !struck && k == selectedKey;
            o.btn.interactable = !struck;
            o.bg.color = sel ? K.SelectFill : K.Bg;
            o.outline.gameObject.SetActive(sel);
            o.disc.color = sel ? K.Accent : Color.white;
            o.rim.gameObject.SetActive(!sel && !struck);
            o.slash.gameObject.SetActive(struck);
            o.key.color = sel ? Color.white : K.Text;
            o.text.color = struck ? K.TextDim : K.Text;
            o.text.fontStyle = struck ? FontStyles.Strikethrough : FontStyles.Normal;
            o.tag.SetActive(struck && dangerous);
            float cut = o.tag.activeSelf ? 186f : 0f;   // make room for the DANGEROUS plate
            o.text.rectTransform.sizeDelta = new Vector2(OptTextW - cut, 92f);
            o.text.rectTransform.anchoredPosition = new Vector2(OptTextX - cut / 2f, 0);
        }

        void SetSubmit(string key)
        {
            qSubmit.interactable = key != null;
            K.SetButtonColor(qSubmit, key != null ? K.Ok : K.SurfaceHi);
            K.ButtonLabel(qSubmit).text = key != null ? $"Submit answer {key}" : "Submit";
            var ink = key != null ? Color.white : K.TextDim;
            foreach (var g in qSubmit.GetComponentsInChildren<Graphic>(true))
                if (g != qSubmit.targetGraphic) g.color = ink;
        }

        public void CloseQuestion()
        {
            question.gameObject.SetActive(false);
            qRec = null;
        }

        public void ShowFinishPrompt()
        {
            int left = S.HazardCount - S.ResolvedCount;
            ShowMessage("Finish the inspection?",
                left > 0 ? "You may have missed some hazards. Anything you have not found counts as missed.\nContinue inspecting or submit now?"
                         : "Submit your inspection now?",
                "Continue", () => S.ContinueInspecting(), "Submit", () => S.FinishNow());
        }

        public void CloseMessage() => msg.gameObject.SetActive(false);

        public void PressPrimary()
        {
            if (msg.gameObject.activeSelf && msgBtn1.gameObject.activeSelf) msgAct1?.Invoke();
        }

        public void Toast(string text, float seconds = 2.2f, Color? dot = null)
        {
            toastText.text = text;
            // the toast speaks in signs too: green check = done, red ! = problem, yellow triangle = caution, blue i = info
            toastOk.SetActive(dot == K.Ok);
            toastBad.SetActive(dot == K.Bad);
            toastWarn.SetActive(dot == K.Warn);
            toastInfo.SetActive(dot != K.Ok && dot != K.Bad && dot != K.Warn);
            K.PlaceInFront(toast.transform, 0.85f, -0.3f);
            toast.gameObject.SetActive(true);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(HideAfter(toast.gameObject, seconds));
        }

        public void ToggleHelp()
        {
            if (help.gameObject.activeSelf) { help.gameObject.SetActive(false); return; }
            var body = help.transform.Find("Body")?.GetComponent<TextMeshProUGUI>();
            if (body != null) body.text = ControlsText;
            K.PlaceInFront(help.transform, 1.0f, 0f);
            help.gameObject.SetActive(true);
        }

        public void UpdateHud()
        {
            if (S == null) return;
            float rem = S.Remaining;
            tabTimer.text = K.Mmss(rem);
            tabTimer.color = rem < 60f ? K.Bad : K.Text;   // yellow is a field colour, never text on the plate
            tabTimeBar.Set(rem / S.Config.timeLimitSeconds);
            tabTimeBar.fillImage.color = rem < 60f ? K.Bad : rem < 120f ? K.Warn : K.Accent;
            foreach (var row in tabRows)
            {
                var inCat = S.Records.Where(r => r.data.hudCategory == row.cat).ToList();
                int done = inCat.Count(r => r.solved);
                row.count.text = $"{done}/{inCat.Count}";
                row.count.color = done == inCat.Count ? K.Ok : K.Text;
                row.bar.Set(inCat.Count == 0 ? 0 : done / (float)inCat.Count);
            }
            tabFooter.text = $"Resolved <b>{S.ResolvedCount}/{S.HazardCount}</b>     <color={K.Hex(K.TextDim)}>X controls  ·  Y finish</color>";
        }

        // ================= builders =================

        const float OptTextX = 46f, OptTextW = 804f;   // option text column: right of the 68-unit disc to the row's right padding

        void BuildQuestion()
        {
            var size = new Vector2(1000, 880);
            question = K.Panel(transform, "Panel_Question", size, accentLine: false);
            var t = question.transform;

            // yellow warning band naming the hazard
            K.Band(t, size, 150f, K.Warn);
            K.WarningSign(t, new Vector2(-404, 365), 112f);
            qTitle = K.Label(t, "Title", "", new Vector2(74, 384), new Vector2(772, 58), 46, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            qTitle.enableAutoSizing = true; qTitle.fontSizeMin = 36; qTitle.fontSizeMax = 46;
            qMeta = K.Label(t, "Meta", "", new Vector2(74, 332), new Vector2(772, 36), 28, K.Text, TextAlignmentOptions.Left);

            qText = K.Label(t, "Question", "", new Vector2(0, 222), new Vector2(920, 84), 31, K.Text, TextAlignmentOptions.TopLeft);

            K.Img(t, "RuleTop", new Vector2(0, 160), new Vector2(920, 2), K.Rule).raycastTarget = false;
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                float y = 160 - 49 - i * 98;
                var o = qOpt[i] = new Opt();
                o.bg = K.Img(t, "Option" + i, new Vector2(0, y), new Vector2(920, 94), K.Bg, K.Rounded);
                o.bg.pixelsPerUnitMultiplier = 1.5f;
                o.btn = o.bg.gameObject.AddComponent<Button>();
                K.Tint(o.btn);
                o.btn.onClick.AddListener(() => { if (qRec != null && idx < qRec.data.options.Length) S.SelectOption(qRec.data.options[idx].key); });
                o.btn.onClick.AddListener(() => ChemLabAudio.Click());
                o.outline = K.Outline(o.bg.transform, new Vector2(920, 94), K.Accent, 1.5f);

                var discPos = new Vector2(-414, 0);
                o.disc = K.Img(o.bg.transform, "Disc", discPos, new Vector2(68, 68), Color.white, K.Circle);
                o.rim = K.Img(o.bg.transform, "Rim", discPos, new Vector2(68, 68), K.Text, K.Ring);
                o.key = K.Label(o.bg.transform, "Key", "", discPos + new Vector2(0, -1), new Vector2(68, 68), 34, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
                o.slash = K.Img(o.bg.transform, "Prohibited", discPos, new Vector2(68, 68), K.Bad, K.Prohibition);
                o.disc.raycastTarget = o.rim.raycastTarget = o.slash.raycastTarget = false;

                o.text = K.Label(o.bg.transform, "Text", "", new Vector2(OptTextX, 0), new Vector2(OptTextW, 92), 28, K.Text, TextAlignmentOptions.MidlineLeft);
                o.text.enableAutoSizing = true; o.text.fontSizeMin = 22; o.text.fontSizeMax = 28;
                o.tag = K.Chip(o.bg.transform, "Dangerous", "DANGEROUS", new Vector2(363, 0), new Vector2(170, 46), K.Bad, Color.white).transform.parent.gameObject;
                K.Img(t, "Rule" + i, new Vector2(0, y - 49), new Vector2(920, 2), K.Rule).raycastTarget = false;
            }

            qFeedback = K.Label(t, "Feedback", "", new Vector2(0, -288), new Vector2(920, 84), 28, K.Text, TextAlignmentOptions.TopLeft);
            K.Button(t, "Close", "Close", new Vector2(-235, -385), new Vector2(430, 70), K.Style.Ghost, () => S.CloseQuestion(), key: "B");
            qSubmit = K.Button(t, "Submit", "Submit", new Vector2(235, -385), new Vector2(430, 70), K.Style.Primary, () => S.SubmitCurrent(), key: "A");
            question.gameObject.SetActive(false);
        }

        void BuildMessage()
        {
            msg = K.Panel(transform, "Panel_Message", new Vector2(900, 460));
            msgTitle = K.Label(msg.transform, "Title", "", new Vector2(0, 150), new Vector2(820, 70), 44, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            msgBody = K.Label(msg.transform, "Body", "", new Vector2(0, 30), new Vector2(800, 160), 27, K.TextDim, TextAlignmentOptions.Center);
            msgBtn1 = K.Button(msg.transform, "Btn1", "", new Vector2(-200, -150), new Vector2(360, 80), K.Style.Ghost, () => msgAct1?.Invoke());
            msgBtn2 = K.Button(msg.transform, "Btn2", "", new Vector2(200, -150), new Vector2(360, 80), K.Style.Primary, () => msgAct2?.Invoke());
            msg.gameObject.SetActive(false);
        }

        void ShowMessage(string title, string body, string b1, System.Action a1, string b2 = null, System.Action a2 = null)
        {
            msgTitle.text = title;
            msgBody.text = body;
            msgBtn1.gameObject.SetActive(b1 != null);
            msgBtn2.gameObject.SetActive(b2 != null);
            if (b1 != null) { K.ButtonLabel(msgBtn1).text = b1; msgAct1 = a1; }
            if (b2 != null) { K.ButtonLabel(msgBtn2).text = b2; msgAct2 = a2; }
            K.PlaceInFront(msg.transform, 1.15f, 0f);
            msg.gameObject.SetActive(true);
        }

        void BuildToast()
        {
            toast = K.Panel(transform, "Panel_Toast", new Vector2(760, 96), 0.0008f, accentLine: false, blocksRays: false);
            var iconPos = new Vector2(-326, 0);
            toastOk = K.SignDisc(toast.transform, "IconDone", iconPos, 60, K.Ok, check: true).gameObject;
            toastBad = K.SignDisc(toast.transform, "IconProblem", iconPos, 60, K.Bad, check: false).gameObject;
            toastInfo = K.SignDisc(toast.transform, "IconInfo", iconPos, 60, K.Accent, check: false, info: true).gameObject;
            toastWarn = K.WarningSign(toast.transform, iconPos + new Vector2(0, 3), 68).gameObject;
            toastText = K.Label(toast.transform, "Text", "", new Vector2(42, 0), new Vector2(636, 84), 28, K.Text, TextAlignmentOptions.Left);
            toast.gameObject.SetActive(false);
        }

        void BuildHelp()
        {
            help = K.Panel(transform, "Panel_Controls", new Vector2(900, 560), blocksRays: false);
            K.Label(help.transform, "Title", "Controls", new Vector2(0, 220), new Vector2(820, 60), 40, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            ChemLabLobby.ControllerGraphic(help.transform, new Vector2(-250, -20), 0.9f);
            K.Label(help.transform, "Body", ControlsText, new Vector2(170, -30), new Vector2(520, 420), 25, K.Text, TextAlignmentOptions.MidlineLeft);
            help.gameObject.SetActive(false);
        }

        public static string ControlsText =>
            ChemLabControls.DesktopMode
                ? "<b>WASD</b> walk\n<b>Hold right mouse</b> to look\n<b>Left click</b> to select\n<b>Enter</b> submit   <b>Esc</b> close\n<b>H</b> controls   <b>F</b> finish"
                : $"<b><color={K.Hex(K.Accent)}>Trigger</color></b>  select / inspect\n" +
                  "<b>Stick forward</b>  aim, release to teleport\n" +
                  "<b>Stick left/right</b>  turn 45°    <b>back</b>  turn around\n" +
                  "<b>A</b>  submit    <b>B</b>  close\n" +
                  "<b>X</b>  this card    <b>Y / Menu</b>  finish";

        void BuildTablet()
        {
            tablet = K.Panel(transform, "HUD_Tablet", new Vector2(620, 560), 0.0007f, blocksRays: false);
            K.Label(tablet.transform, "Title", "LAB SAFETY INSPECTION", new Vector2(0, 236), new Vector2(560, 40), 24, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            tabTimer = K.Label(tablet.transform, "Timer", "07:00", new Vector2(0, 170), new Vector2(560, 90), 86, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(tablet.transform, "TimerCaption", "remaining", new Vector2(150, 150), new Vector2(240, 40), 24, K.TextDim, TextAlignmentOptions.Right);
            tabTimeBar = K.ProgressBar(tablet.transform, "TimeBar", new Vector2(0, 108), new Vector2(560, 12), K.Accent);

            var cats = new List<string>();
            foreach (var h in ChemLabSession.Instance != null ? ChemLabSession.Instance.Config.hazards : LabConfig.Load().hazards)
                if (!cats.Contains(h.hudCategory)) cats.Add(h.hudCategory);
            for (int i = 0; i < cats.Count; i++)
            {
                float y = 50 - i * 86;
                K.Label(tablet.transform, "Cat" + i, cats[i], new Vector2(-60, y), new Vector2(440, 40), 28, K.Text, TextAlignmentOptions.Left);
                var count = K.Label(tablet.transform, "Count" + i, "0/0", new Vector2(220, y), new Vector2(120, 40), 30, K.Text, TextAlignmentOptions.Right, FontStyles.Bold);
                var bar = K.ProgressBar(tablet.transform, "Bar" + i, new Vector2(0, y - 32), new Vector2(560, 10), K.Ok);
                tabRows.Add((cats[i], count, bar));
            }
            tabFooter = K.Label(tablet.transform, "Footer", "", new Vector2(0, -238), new Vector2(560, 44), 23, K.Text, TextAlignmentOptions.Left);
            AttachTablet();
            tablet.gameObject.SetActive(false);
        }

        void AttachTablet()
        {
            if (tablet == null) return;
            if (leftHandAnchor != null && !K.MouseMode)
            {
                tablet.transform.SetParent(leftHandAnchor, false);
                tablet.transform.localPosition = new Vector3(0f, 0.13f, 0.07f);   // raised along the tilt so the larger tablet clears the controller
                tablet.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            }
            else if (Camera.main != null)
            {
                tablet.transform.SetParent(Camera.main.transform, false);
                tablet.transform.localPosition = new Vector3(-0.28f, -0.14f, 0.65f);
                tablet.transform.localRotation = Quaternion.identity;
                tablet.transform.localScale = Vector3.one * 0.00045f;
            }
        }

        void BuildStation()
        {
            station = K.Panel(transform, "Panel_FinishStation", new Vector2(620, 330));
            K.Label(station.transform, "Title", "Inspection Station", new Vector2(0, 100), new Vector2(560, 60), 38, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(station.transform, "Body", "Done early? Submit here, or press Y / Menu.", new Vector2(0, 30), new Vector2(560, 60), 24, K.TextDim, TextAlignmentOptions.Center);
            K.Button(station.transform, "Finish", "Finish Inspection", new Vector2(0, -85), new Vector2(400, 84), K.Style.Primary, () => S.RequestFinish());
            if (finishStationAnchor != null) station.transform.SetPositionAndRotation(finishStationAnchor.position, finishStationAnchor.rotation);
            station.gameObject.SetActive(false);
        }

        IEnumerator HideAfter(GameObject go, float s)
        {
            yield return new WaitForSeconds(s);
            go.SetActive(false);
        }
    }
}
