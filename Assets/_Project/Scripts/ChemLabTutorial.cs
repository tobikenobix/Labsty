using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// The practice room between the lobby and the lab. Seven steps, in a fixed order, each rehearsing something the lab
    /// asks for: teleport, turn, a safe object (false clicks), a hazard question, a dangerous answer on purpose, the task
    /// panel (X), and finishing with Y / Menu, which here opens the lab. One instruction card docks at each station and
    /// the next floor ring glows. "Skip to the lab" works at any time. Nothing here is scored or exported.
    /// </summary>
    public class ChemLabTutorial : MonoBehaviour
    {
        public Transform leftHandAnchor;
        [Tooltip("Where the instruction card stands for each of the 7 steps.")]
        public Transform[] stepAnchors;
        [Tooltip("Floor rings: 1 = first teleport, 2 = spill bench, 3 = lab door.")]
        public ChemLabTeleportNode[] rings;

        const int Teleport = 0, Turn = 1, Decoy = 2, Acid = 3, Spill = 4, Panel = 5, Finish = 6, StepCount = 7;
        int cur;
        float turnYaw;
        bool panelHidden, sawDanger;
        XROrigin origin;

        Canvas card, tablet, toast, prompt;
        TextMeshProUGUI kicker, title, body, tabStep, toastText;
        Button enterBtn;
        readonly (TextMeshProUGUI count, K.Bar bar)[] tabRows = new (TextMeshProUGUI, K.Bar)[2];
        ChemLabQuestionPanel practice;
        ChemLabControllerLabels labels;
        QuestionData acidQ, spillQ;
        ChemLabInspectable open;   // the practice hazard whose question is showing
        int openStep;
        string sel;
        Coroutine toastCo;

        bool PromptOpen => prompt.gameObject.activeSelf;
        float Yaw => origin != null ? origin.transform.eulerAngles.y : 0f;

        /// <summary>The ring the current step sends the player to (null = stay where you are).</summary>
        ChemLabTeleportNode RingFor(int step) =>
            rings == null || rings.Length < 3 ? null : step == Teleport ? rings[0] : step == Spill ? rings[1] : step == Finish ? rings[2] : null;

        void Awake()
        {
            practice = new ChemLabQuestionPanel(transform, Select, Submit, ClosePractice);
            acidQ = Question("acid");
            spillQ = Question("spill");
            BuildCard();
            BuildTablet();
            BuildToast();
            BuildPrompt();
            labels = ChemLabControllerLabels.Create(
                new ChemLabControllerLabels.Item(false, "Button_B", "B", L10n.T("lbl.close")),
                new ChemLabControllerLabels.Item(false, "Button_A", "A", L10n.T("lbl.submit")),
                new ChemLabControllerLabels.Item(false, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(false, "Trigger", null, L10n.T("lbl.trigger")),
                new ChemLabControllerLabels.Item(true, "Button_B", "Y", L10n.T("lbl.finish")),
                new ChemLabControllerLabels.Item(true, "Button_A", "X", L10n.T("lbl.panel")),
                new ChemLabControllerLabels.Item(true, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(true, "Trigger", null, L10n.T("lbl.trigger")));
            K.MouseModeEnabled += AttachTablet;
        }

        /// <summary>Practice question "tut.{p}Q" with options A (dangerous), B (right) and C.</summary>
        static QuestionData Question(string p) => new QuestionData
        {
            id = p, question = L10n.T($"tut.{p}Q"), correct = "B",
            options = new[]
            {
                new OptionData { key = "A", text = L10n.T($"tut.{p}A"), critical = true },
                new OptionData { key = "B", text = L10n.T($"tut.{p}B") },
                new OptionData { key = "C", text = L10n.T($"tut.{p}C") },
            },
        };

        void Start()
        {
            origin = FindFirstObjectByType<XROrigin>();
            RefreshStep();
        }

        void OnDestroy() => K.MouseModeEnabled -= AttachTablet;

        void OnEnable()
        {
            ChemLabTeleportNode.PlayerArrived += OnArrived;
            ChemLabInspectable.Inspected += OnInspected;
            ChemLabControls.Confirm += Confirm;
            ChemLabControls.Back += Back;
            ChemLabControls.ToggleHelp += TogglePanel;
            ChemLabControls.Finish += OnFinish;
            ChemLabControls.BlockInspect = () => practice.IsOpen || PromptOpen;
        }

        void OnDisable()
        {
            ChemLabTeleportNode.PlayerArrived -= OnArrived;
            ChemLabInspectable.Inspected -= OnInspected;
            ChemLabControls.Confirm -= Confirm;
            ChemLabControls.Back -= Back;
            ChemLabControls.ToggleHelp -= TogglePanel;
            ChemLabControls.Finish -= OnFinish;
            ChemLabControls.BlockInspect = null;
        }

        void Update()
        {
            // turning counts once the rig has turned 30 deg away from where this step began (snap turn is 45)
            if (cur == Turn && origin != null && Mathf.Abs(Mathf.DeltaAngle(Yaw, turnYaw)) > 30f) Complete(Turn, L10n.T("tut.toastTurn"));
            K.KeepInView(practice.canvas.transform, 0.95f, -0.05f);
            K.KeepInView(prompt.transform, 1.0f, 0f);
            // one plate in front at a time: the card and any toast step aside while the practice question or the prompt is open
            bool plate = practice.IsOpen || PromptOpen;
            if (card.gameObject.activeSelf == plate) card.gameObject.SetActive(!plate);
            if (plate && toast.gameObject.activeSelf) toast.gameObject.SetActive(false);
        }

        // ================= steps =================

        void Complete(int step, string toastText)
        {
            if (step != cur) return;   // fixed order: only the current step can complete
            cur++;
            if (cur == Turn) turnYaw = Yaw;
            ChemLabAudio.Button();
            Toast(toastText, K.Ok);
            RefreshStep();
        }

        /// <summary>Card text, the one glowing ring, the card's place and the task panel follow the current step.</summary>
        void RefreshStep()
        {
            kicker.text = L10n.T("tut.kicker", cur + 1, StepCount);
            title.text = L10n.T("tut.s" + (cur + 1));
            body.text = L10n.T("tut.h" + (cur + 1));
            enterBtn.gameObject.SetActive(cur == Finish);
            var ring = RingFor(cur);
            if (rings != null) foreach (var r in rings) if (r != null) r.gameObject.SetActive(r == ring);
            // a step that starts with a walk shows its card in front of you until you reach its ring, then docks there
            if ((cur == Spill || cur == Finish) && ring != null && !ring.PlayerHere) K.PlaceInFront(card.transform, 1.1f, 0f);
            else Dock();
            tabStep.text = $"<color={K.Hex(K.TextDim)}>{kicker.text}</color>\n{title.text}";
            SetRow(0, cur > Acid);
            SetRow(1, cur > Spill);
        }

        void Dock()
        {
            var a = stepAnchors != null && cur < stepAnchors.Length ? stepAnchors[cur] : null;
            if (a == null) return;
            card.transform.SetPositionAndRotation(a.position, a.rotation);
            card.transform.localScale = Vector3.one * K.BaseScale(card.transform);   // undo PlaceInFront's shrink when it was cramped
        }

        void SetRow(int i, bool done)
        {
            tabRows[i].count.text = done ? "1/1" : "0/1";
            tabRows[i].bar.Set(done ? 1f : 0f);
        }

        void OnArrived(ChemLabTeleportNode node)
        {
            if (node != RingFor(cur)) return;
            if (cur == Teleport) Complete(Teleport, L10n.T("lobby.toastTeleport"));
            else Dock();
        }

        void NotYet() => Toast(L10n.T("tut.notYet", L10n.T("tut.s" + (cur + 1))), K.Warn);

        void OnInspected(ChemLabInspectable item)
        {
            if (practice.IsOpen || PromptOpen) return;
            int step = item.displayName == "Decoy" ? Decoy : item.displayName == "Acid" ? Acid : item.displayName == "Spill" ? Spill : -1;
            if (step < 0) return;
            if (step > cur) { NotYet(); return; }
            item.SetMark(ChemLabInspectable.Mark.Checked);
            if (step == Decoy)
            {
                if (cur == Decoy) Complete(Decoy, L10n.T("tut.decoyOk"));
                else Toast(L10n.T("lab.looksOk"), K.Accent);
                return;
            }
            // a practice hazard: only reachable on its own step (once answered it is fixed and no longer reacts)
            open = item;
            openStep = step;
            sel = null;
            practice.Show(L10n.T(step == Acid ? "tut.acidTitle" : "tut.spillTitle"), L10n.T("tut.qMeta"), step == Acid ? acidQ : spillQ, item.transform.position);
        }

        void Select(string key)
        {
            sel = key;
            practice.Highlight(key);
        }

        void Submit()
        {
            if (sel == null || open == null) return;
            var q = openStep == Acid ? acidQ : spillQ;
            var opt = q.Option(sel);
            if (sel == q.correct)
            {
                // the spill step is about seeing a dangerous answer, so the right one waits until that has happened
                if (openStep == Spill && !sawDanger) { sel = null; practice.Highlight(null); practice.ShowNote(L10n.T("tut.spillFirst")); return; }   // on the plate, not a toast over it
                ChemLabAudio.Correct();
                practice.Close();
                open.Fix();
                open = null;
                Complete(openStep, L10n.T("tut.toastCorrect"));
            }
            else
            {
                if (openStep == Spill && opt.critical) sawDanger = true;
                ChemLabAudio.Wrong(opt.critical);
                open.SetMark(ChemLabInspectable.Mark.Wrong);
                practice.ShowWrong(sel, opt.critical, L10n.T(openStep == Acid ? "tut.acidWrong" : "tut.spillWrong"));
                sel = null;
            }
        }

        void ClosePractice()
        {
            practice.Close();
            open = null;
        }

        /// <summary>X: hide / show the task panel and the button labels; on its step, hiding and showing again completes it.</summary>
        void TogglePanel()
        {
            bool show = !tablet.gameObject.activeSelf;
            tablet.gameObject.SetActive(show);
            labels.Visible = show;
            if (cur != Panel) return;
            if (!show) panelHidden = true;
            else if (panelHidden) Complete(Panel, L10n.T("tut.toastPanel"));
        }

        /// <summary>Y / Menu: in the lab this asks to submit; here, on the last step, it asks to enter the lab.</summary>
        void OnFinish()
        {
            if (practice.IsOpen || PromptOpen) return;
            if (cur == Finish) ShowPrompt();
            else NotYet();
        }

        void Confirm()
        {
            if (practice.IsOpen) Submit();
            else if (PromptOpen) EnterLab();
        }

        void Back()
        {
            if (practice.IsOpen) ClosePractice();
            else if (PromptOpen) prompt.gameObject.SetActive(false);
        }

        void ShowPrompt()
        {
            K.PlaceInFront(prompt.transform, 1.0f, 0f);
            prompt.gameObject.SetActive(true);
        }

        void EnterLab()
        {
            if (Application.CanStreamedLevelBeLoaded(ChemLabRun.LabScene)) SceneManager.LoadScene(ChemLabRun.LabScene);
            else Debug.LogWarning("[ChemLab] Lab scene not in Build Settings. Run Chemistry Lab > Build Tutorial Scene.");
        }

        // ================= builders =================

        /// <summary>
        /// Instruction card (900 x 520 at 0.0012, ~1.1 m from where you stand: 30-unit body ~0.95 deg x-height):
        /// step kicker, the step in one line, how to do it, Skip to the lab and, on the last step, Enter the lab.
        /// </summary>
        void BuildCard()
        {
            card = K.Panel(transform, "Panel_Tutorial", new Vector2(900, 520), 0.0012f);
            var t = card.transform;
            kicker = K.Label(t, "Kicker", "", new Vector2(0, 215), new Vector2(820, 36), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            title = K.FitOneLine(K.Label(t, "Title", "", new Vector2(0, 160), new Vector2(820, 64), 44, K.Text, TextAlignmentOptions.Left), 30);
            K.Img(t, "Rule", new Vector2(0, 118), new Vector2(820, 2), K.Rule).raycastTarget = false;
            body = K.Label(t, "Body", "", new Vector2(0, 20), new Vector2(820, 180), 30, K.Text, TextAlignmentOptions.TopLeft);
            body.enableAutoSizing = true; body.fontSizeMin = 24; body.fontSizeMax = 30;
            var skip = K.Button(t, "Skip", L10n.T("tut.skip"), new Vector2(-215, -190), new Vector2(390, 80), K.Style.Ghost, EnterLab, 28);
            K.FitOneLine(K.ButtonLabel(skip), 20);
            enterBtn = K.Button(t, "Enter", L10n.T("tut.enter"), new Vector2(215, -190), new Vector2(390, 80), K.Style.Primary, ShowPrompt, 28);
            K.FitOneLine(K.ButtonLabel(enterBtn), 20);
        }

        /// <summary>Task panel on the left controller, laid out as the lab's (demo timer, one row per practice hazard) plus the current step.</summary>
        void BuildTablet()
        {
            // ponytail: copies ChemLabUI.BuildTablet's layout so the lab UI stays untouched; share one builder once both branches are merged
            tablet = K.Panel(transform, "HUD_Tablet", new Vector2(620, 560), 0.0007f, blocksRays: false);
            var t = tablet.transform;
            K.Label(t, "Title", L10n.T("tab.title"), new Vector2(0, 236), new Vector2(560, 40), 24, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Timer", "07:00", new Vector2(0, 170), new Vector2(560, 90), 86, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.FitOneLine(K.Label(t, "TimerCaption", L10n.T("tut.demo"), new Vector2(130, 150), new Vector2(300, 40), 24, K.TextDim, TextAlignmentOptions.Right), 18);
            K.ProgressBar(t, "TimeBar", new Vector2(0, 108), new Vector2(560, 12), K.Accent).Set(1f);
            string[] rowKeys = { "tut.acidRow", "tut.spillRow" };
            for (int i = 0; i < 2; i++)
            {
                float y = 50 - i * 86;
                K.Label(t, "Row" + i, L10n.T(rowKeys[i]), new Vector2(-60, y), new Vector2(440, 40), 28, K.Text, TextAlignmentOptions.Left);
                var count = K.Label(t, "Count" + i, "0/1", new Vector2(220, y), new Vector2(120, 40), 30, K.Text, TextAlignmentOptions.Right, FontStyles.Bold);
                tabRows[i] = (count, K.ProgressBar(t, "Bar" + i, new Vector2(0, y - 32), new Vector2(560, 10), K.Ok));
            }
            tabStep = K.Label(t, "Step", "", new Vector2(0, -150), new Vector2(560, 96), 26, K.Text, TextAlignmentOptions.TopLeft);
            tabStep.enableAutoSizing = true; tabStep.fontSizeMin = 20; tabStep.fontSizeMax = 26;
            K.Label(t, "Footer", L10n.T("tut.footer", K.Hex(K.TextDim)), new Vector2(0, -238), new Vector2(560, 44), 23, K.Text, TextAlignmentOptions.Left);
            AttachTablet();
        }

        void AttachTablet() { if (tablet != null) K.AttachTablet(tablet.transform, leftHandAnchor); }

        /// <summary>The lab's finish prompt, reworded: B keeps practising, A enters the lab.</summary>
        void BuildPrompt()
        {
            prompt = K.Panel(transform, "Panel_Finish", new Vector2(900, 460), 0.00112f);
            var t = prompt.transform;
            K.Label(t, "Title", L10n.T("tut.finishTitle"), new Vector2(0, 150), new Vector2(820, 70), 44, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            var b = K.Label(t, "Body", L10n.T("tut.finishBody"), new Vector2(0, 30), new Vector2(800, 160), 27, K.TextDim, TextAlignmentOptions.Center);
            b.enableAutoSizing = true; b.fontSizeMin = 22; b.fontSizeMax = 27;
            K.Button(t, "Continue", L10n.T("finish.continue"), new Vector2(-200, -150), new Vector2(360, 80), K.Style.Ghost, () => prompt.gameObject.SetActive(false), key: "B");
            K.Button(t, "Enter", L10n.T("tut.enter"), new Vector2(200, -150), new Vector2(360, 80), K.Style.Primary, EnterLab, key: "A");
            prompt.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            toast = K.Panel(transform, "Panel_Toast", new Vector2(820, 96), 0.0008f, accentLine: false, blocksRays: false);
            toastText = K.Label(toast.transform, "Text", "", Vector2.zero, new Vector2(760, 84), 28, K.Text, TextAlignmentOptions.Center);
            toastText.enableAutoSizing = true; toastText.fontSizeMin = 20; toastText.fontSizeMax = 28;
            toast.gameObject.SetActive(false);
        }

        void Toast(string text, Color col)
        {
            toastText.text = $"<color={K.Hex(col)}><b>|</b></color>  {text}";
            K.PlaceInFront(toast.transform, 0.85f, -0.3f);
            toast.gameObject.SetActive(true);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(HideToast());
        }

        IEnumerator HideToast()
        {
            yield return new WaitForSeconds(3.2f);
            toast.gameObject.SetActive(false);
        }
    }
}
