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
        TextMeshProUGUI qCategory, qDifficulty, qText, qFeedback;
        readonly Button[] qOpt = new Button[4];
        Button qSubmit;
        HazardRecord qRec;
        readonly HashSet<string> qDisabled = new HashSet<string>();

        // message panel
        Canvas msg; TextMeshProUGUI msgTitle, msgBody; Button msgBtn1, msgBtn2; System.Action msgAct1, msgAct2;

        // toast / banner / help / tablet / station
        Canvas toast; TextMeshProUGUI toastText; Image toastDot; Coroutine toastCo;
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
            qDisabled.Clear();
            qCategory.text = rec.data.hudCategory.ToUpper();
            qDifficulty.text = rec.data.difficulty;
            qText.text = rec.data.question;
            for (int i = 0; i < 4; i++)
            {
                bool has = i < rec.data.options.Length;
                qOpt[i].gameObject.SetActive(has);
                if (!has) continue;
                var o = rec.data.options[i];
                K.ButtonLabel(qOpt[i]).text = $"<b><color={K.Hex(K.Accent)}>{o.key}</color></b>   {o.text}";
                qOpt[i].interactable = true;
                K.SetButtonColor(qOpt[i], K.Surface);
            }
            qFeedback.text = $"<color={K.Hex(K.TextDim)}>Point at an answer and pull the trigger, then Submit (A).</color>";
            qSubmit.interactable = false;
            K.PlaceInFront(question.transform, 0.95f, -0.05f, hazardPos);
            question.gameObject.SetActive(true);
        }

        public void HighlightOption(string key)
        {
            if (qRec == null) return;
            for (int i = 0; i < qRec.data.options.Length; i++)
            {
                var k = qRec.data.options[i].key;
                if (qDisabled.Contains(k)) continue;
                K.SetButtonColor(qOpt[i], k == key ? K.AccentBlue : K.Surface);
            }
            qSubmit.interactable = true;
        }

        public void ShowWrong(HazardRecord rec, string key, bool critical)
        {
            qDisabled.Add(key);
            for (int i = 0; i < rec.data.options.Length; i++)
            {
                if (rec.data.options[i].key != key) continue;
                K.SetButtonColor(qOpt[i], new Color(0.45f, 0.13f, 0.15f));
                qOpt[i].interactable = false;
            }
            qFeedback.text = $"<color={K.Hex(K.Bad)}><b>{rec.data.wrongFeedback}</b></color>" +
                             (critical ? $"\n<color={K.Hex(K.Warn)}>[!] Dangerous choice - logged for safety review. Try again.</color>"
                                       : $"\n<color={K.Hex(K.TextDim)}>Logged as a mistake. Try again.</color>");
            qSubmit.interactable = false;
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
            toastDot.color = dot ?? K.TextDim;
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
            tabTimer.color = rem < 60f ? K.Bad : rem < 120f ? K.Warn : K.Text;
            tabTimeBar.Set(rem / S.Config.timeLimitSeconds);
            tabTimeBar.fillImage.color = rem < 60f ? K.Bad : K.Accent;
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

        void BuildQuestion()
        {
            question = K.Panel(transform, "Panel_Question", new Vector2(1000, 880));
            qCategory = K.Chip(question.transform, "Category", "", new Vector2(-300, 380), new Vector2(330, 44), new Color(0f, 0.78f, 0.68f, 0.18f), K.Accent);
            qDifficulty = K.Chip(question.transform, "Difficulty", "", new Vector2(380, 380), new Vector2(190, 44), new Color(1f, 0.74f, 0.25f, 0.18f), K.Warn);
            K.Label(question.transform, "Title", "You found a potential hazard", new Vector2(0, 318), new Vector2(920, 56), 40, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            qText = K.Label(question.transform, "Question", "", new Vector2(0, 250), new Vector2(920, 84), 27, K.TextDim, TextAlignmentOptions.TopLeft);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                qOpt[i] = K.Button(question.transform, "Option" + i, "", new Vector2(0, 140 - i * 112), new Vector2(920, 100), K.Style.Ghost,
                    () => { if (qRec != null && idx < qRec.data.options.Length) S.SelectOption(qRec.data.options[idx].key); },
                    25, TextAlignmentOptions.Left);
            }
            qFeedback = K.Label(question.transform, "Feedback", "", new Vector2(0, -305), new Vector2(920, 80), 24, K.Text, TextAlignmentOptions.TopLeft);
            K.Button(question.transform, "Close", "Close  (B)", new Vector2(-235, -390), new Vector2(430, 76), K.Style.Ghost, () => S.CloseQuestion());
            qSubmit = K.Button(question.transform, "Submit", "Submit  (A)", new Vector2(235, -390), new Vector2(430, 76), K.Style.Primary, () => S.SubmitCurrent());
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
            toastDot = K.Img(toast.transform, "Dot", new Vector2(-345, 0), new Vector2(22, 22), K.Accent, K.Circle);
            toastDot.raycastTarget = false;
            toastText = K.Label(toast.transform, "Text", "", new Vector2(20, 0), new Vector2(660, 84), 28, K.Text, TextAlignmentOptions.Left);
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
