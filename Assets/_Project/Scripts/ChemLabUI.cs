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
    /// Scene 2 UI: the task panel on the left controller (category checklist + countdown; X shows / hides it together
    /// with the button labels on both controllers), the 4-option hazard panel, toasts, finish prompt and start/end
    /// banners. Everything is generated with ChemLabUIKit.
    /// </summary>
    public class ChemLabUI : MonoBehaviour
    {
        [Tooltip("Left controller transform; the tablet HUD is attached to it.")]
        public Transform leftHandAnchor;
        [Tooltip("Optional wall-mounted Finish panel position.")]
        public Transform finishStationAnchor;

        ChemLabSession S => ChemLabSession.Instance;

        ChemLabQuestionPanel question;
        ChemLabControllerLabels labels;

        // message panel
        Canvas msg; TextMeshProUGUI msgTitle, msgBody; Button msgBtn1, msgBtn2; System.Action msgAct1, msgAct2;

        // toast / banner / help / tablet / station
        Canvas toast; TextMeshProUGUI toastText; Coroutine toastCo;
        GameObject toastOk, toastInfo, toastBad, toastWarn;
        Canvas help;
        Canvas tablet; TextMeshProUGUI tabTimer, tabFooter; K.Bar tabTimeBar;
        readonly List<(string cat, TextMeshProUGUI count, K.Bar bar)> tabRows = new List<(string, TextMeshProUGUI, K.Bar)>();
        Canvas station;

        public bool QuestionOpen => question != null && question.IsOpen;
        public bool ModalOpen => QuestionOpen || (msg != null && msg.gameObject.activeSelf);

        void Awake()
        {
            question = new ChemLabQuestionPanel(transform, k => S.SelectOption(k), () => S.SubmitCurrent(), () => S.CloseQuestion());
            BuildMessage();
            BuildToast();
            BuildHelp();
            BuildTablet();
            BuildStation();
            K.MouseModeEnabled += AttachTablet;
            labels = ChemLabControllerLabels.Create(
                new ChemLabControllerLabels.Item(false, "Button_B", "B", L10n.T("lbl.close")),
                new ChemLabControllerLabels.Item(false, "Button_A", "A", L10n.T("lbl.submit")),
                new ChemLabControllerLabels.Item(false, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(false, "Trigger", null, L10n.T("lbl.trigger")),
                new ChemLabControllerLabels.Item(false, "Bumper", null, L10n.T("lbl.grip")),
                new ChemLabControllerLabels.Item(true, "Button_B", "Y", L10n.T("lbl.finish")),
                new ChemLabControllerLabels.Item(true, "Button_A", "X", L10n.T("lbl.panel")),
                new ChemLabControllerLabels.Item(true, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(true, "Trigger", null, L10n.T("lbl.trigger")),
                new ChemLabControllerLabels.Item(true, "Bumper", null, L10n.T("lbl.grip")));
            labels.Visible = false;
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
            K.KeepInView(question.canvas.transform, 0.95f, -0.05f);
            K.KeepInView(msg.transform, 1.0f, 0f);
            // desktop keyboard card steps aside while a question plate is in front (one plate at a time)
            bool card = tablet.gameObject.activeSelf && ChemLabControls.DesktopMode && !QuestionOpen;
            if (card != help.gameObject.activeSelf) ShowHelpCard(card);
        }

        // ================= API used by ChemLabSession =================

        public void ShowRunStarted(float seconds)
        {
            if (tabRows.Count == 0) BuildTabletRows();
            SetTaskPanel(true);
            station.gameObject.SetActive(true);
            UpdateHud();
            Toast(L10n.T("lab.started", K.Mmss(seconds), S.HazardCount), 3.5f, K.Accent);
        }

        public void ShowRunEnded(string title)
        {
            SetTaskPanel(false);
            station.gameObject.SetActive(false);
            ShowMessage(title, L10n.T("lab.calculating"), null, null);
        }

        public void ShowQuestion(HazardRecord rec, Vector3 hazardPos)
        {
            question.Show(rec.data.title, $"{rec.data.hudCategory}  /  {rec.data.area}  /  {rec.data.difficulty}", rec.q, hazardPos, rec.shownOrder);
            rec.shownOrder = question.Order;   // reopening the same hazard keeps its option order
        }

        public void HighlightOption(string key) => question.Highlight(key);

        public void ShowWrong(HazardRecord rec, string key, bool critical) => question.ShowWrong(key, critical, rec.data.wrongFeedback);

        public void ShowCorrect(HazardRecord rec, string key) => question.ShowCorrect(key, rec.Explanation);

        /// <summary>Close the plate after its correct-answer hold, unless B already closed it.</summary>
        public void CloseResolved() { if (question.Resolved) question.Close(); }

        public void CloseQuestion() => question.Close();

        public void ShowFinishPrompt()
        {
            int left = S.HazardCount - S.ResolvedCount;
            ShowMessage(L10n.T("finish.title"),
                left > 0 ? L10n.T("finish.bodyMissed")
                         : L10n.T("finish.body"),
                L10n.T("finish.continue"), () => S.ContinueInspecting(), L10n.T("q.submit"), () => S.FinishNow());
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

        /// <summary>X (H on the keyboard): show / hide the task panel and the controller button labels.</summary>
        public void ToggleHelp()
        {
            if (S == null || (S.phase != Phase.Running && S.phase != Phase.FinishPrompt)) return;
            SetTaskPanel(!tablet.gameObject.activeSelf);
        }

        void SetTaskPanel(bool on)
        {
            tablet.gameObject.SetActive(on);
            labels.Visible = on;
            // no controllers to label in desktop mode: the keyboard card comes and goes with the panel instead
            ShowHelpCard(on && ChemLabControls.DesktopMode && !QuestionOpen);
        }

        void ShowHelpCard(bool on)
        {
            if (on && !help.gameObject.activeSelf)
            {
                var body = help.transform.Find("Body")?.GetComponent<TextMeshProUGUI>();
                if (body != null) body.text = ControlsText;
                K.PlaceInFront(help.transform, 1.0f, 0f);
            }
            help.gameObject.SetActive(on);
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
            tabFooter.text = L10n.T("tab.footer", S.ResolvedCount, S.HazardCount, K.Hex(K.TextDim));
        }

        // ================= builders =================

        void BuildMessage()
        {
            msg = K.Panel(transform, "Panel_Message", new Vector2(900, 460), 0.00112f);   // 27-unit body at 1.0 m: ~0.9 deg x-height
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
            K.PlaceInFront(msg.transform, 1.0f, 0f);
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
            K.Label(help.transform, "Title", L10n.T("help.title"), new Vector2(0, 220), new Vector2(820, 60), 40, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            ChemLabLobby.ControllerGraphic(help.transform, new Vector2(-250, -20), 0.9f);
            K.Label(help.transform, "Body", ControlsText, new Vector2(170, -30), new Vector2(520, 420), 25, K.Text, TextAlignmentOptions.MidlineLeft);
            help.gameObject.SetActive(false);
        }

        public static string ControlsText =>
            ChemLabControls.DesktopMode
                ? L10n.T("help.desktop")
                : L10n.T("help.vr", K.Hex(K.Accent));

        void BuildTablet()
        {
            tablet = K.Panel(transform, "HUD_Tablet", new Vector2(620, 560), 0.0007f, blocksRays: false);
            K.Label(tablet.transform, "Title", L10n.T("tab.title"), new Vector2(0, 236), new Vector2(560, 40), 24, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            tabTimer = K.Label(tablet.transform, "Timer", "07:00", new Vector2(0, 170), new Vector2(560, 90), 86, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(tablet.transform, "TimerCaption", L10n.T("tab.remaining"), new Vector2(150, 150), new Vector2(240, 40), 24, K.TextDim, TextAlignmentOptions.Right);
            tabTimeBar = K.ProgressBar(tablet.transform, "TimeBar", new Vector2(0, 108), new Vector2(560, 12), K.Accent);

            tabFooter = K.Label(tablet.transform, "Footer", "", new Vector2(0, -238), new Vector2(560, 44), 23, K.Text, TextAlignmentOptions.Left);
            AttachTablet();
            tablet.gameObject.SetActive(false);
        }

        /// <summary>One checklist row per category that has a hazard in this run (built when the run starts).</summary>
        void BuildTabletRows()
        {
            var cats = S.Records.Select(r => r.data.hudCategory).Distinct().ToList();
            for (int i = 0; i < cats.Count; i++)
            {
                float y = 50 - i * 86;
                K.Label(tablet.transform, "Cat" + i, cats[i], new Vector2(-60, y), new Vector2(440, 40), 28, K.Text, TextAlignmentOptions.Left);
                var count = K.Label(tablet.transform, "Count" + i, "0/0", new Vector2(220, y), new Vector2(120, 40), 30, K.Text, TextAlignmentOptions.Right, FontStyles.Bold);
                var bar = K.ProgressBar(tablet.transform, "Bar" + i, new Vector2(0, y - 32), new Vector2(560, 10), K.Ok);
                tabRows.Add((cats[i], count, bar));
            }
        }

        void AttachTablet() { if (tablet != null) K.AttachTablet(tablet.transform, leftHandAnchor); }

        void BuildStation()
        {
            station = K.Panel(transform, "Panel_FinishStation", new Vector2(620, 330));
            K.Label(station.transform, "Title", L10n.T("station.title"), new Vector2(0, 100), new Vector2(560, 60), 38, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(station.transform, "Body", L10n.T("station.body"), new Vector2(0, 30), new Vector2(560, 60), 24, K.TextDim, TextAlignmentOptions.Center);
            K.Button(station.transform, "Finish", L10n.T("station.finish"), new Vector2(0, -85), new Vector2(400, 84), K.Style.Primary, () => S.RequestFinish());
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
