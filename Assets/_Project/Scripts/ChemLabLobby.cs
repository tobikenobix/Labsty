using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// Scene 1: the briefing room before the lab. Start panel (language, hazard choice, a five-step tutorial and
    /// Start Lab), a teaching panel on how the inspection works, safety posters, and the task panel on the left
    /// controller with button labels on both controllers, as in the lab. Tutorial: teleport onto the ring, turn,
    /// inspect the coffee cup, answer its practice question (check / cross marks as in the lab), hide and show the
    /// task panel with X. Steps can be done in any order; Start Lab works at any time.
    /// </summary>
    public class ChemLabLobby : MonoBehaviour
    {
        public Transform mainPanelAnchor;
        [UnityEngine.Serialization.FormerlySerializedAs("controllerPanelAnchor")]
        public Transform teachingPanelAnchor;
        public Transform leftHandAnchor;
        [Tooltip("Wall posters, in order: chemicals warning, no food or drink, lab coat and glasses, eyewash.")]
        public Transform[] posterAnchors;

        Canvas main, teaching, toast, hazards, tablet;
        TextMeshProUGUI toastText, sub, perRunCaption, hint, tabCount, tabStep;
        K.Bar tabBar;
        HazardData[] allHazards;
        readonly List<(string id, RectTransform sign, TextMeshProUGUI title, TextMeshProUGUI off, Image critical, bool mandatory)> hzRows =
            new List<(string, RectTransform, TextMeshProUGUI, TextMeshProUGUI, Image, bool)>();
        readonly Button[] countBtns = new Button[10];
        bool HazardsOpen => hazards != null && hazards.gameObject.activeSelf;
        Button startBtn;
        readonly Button[] langBtns = new Button[4];
        static readonly string[] Langs = { "DE", "FR", "IT", "EN" };
        Coroutine toastCo;

        // ---- tutorial ----
        const int Teleport = 0, Turn = 1, Inspect = 2, Answer = 3, Panel = 4, StepCount = 5;
        readonly bool[] done = new bool[StepCount];
        readonly (Image bg, Image disc, Image check, TextMeshProUGUI num, TextMeshProUGUI text)[] rows =
            new (Image, Image, Image, TextMeshProUGUI, TextMeshProUGUI)[StepCount];
        XROrigin origin;
        float startYaw;
        bool panelWasHidden;
        ChemLabQuestionPanel practice;
        ChemLabControllerLabels labels;
        ChemLabInspectable cup;
        QuestionData practiceQ;
        string practiceSel;

        int Current { get { for (int i = 0; i < StepCount; i++) if (!done[i]) return i; return StepCount; } }

        void Awake()
        {
            allHazards = LabConfig.Load().hazards;
            BuildMain();
            BuildTeaching();
            BuildPosters();
            BuildToast();
            BuildHazards();
            BuildTablet();
            practice = new ChemLabQuestionPanel(transform, SelectPractice, SubmitPractice, ClosePractice);
            practiceQ = new QuestionData
            {
                id = "P1", question = L10n.T("tut.q"), correct = "B",
                options = new[]
                {
                    new OptionData { key = "A", text = L10n.T("tut.qA"), critical = true },
                    new OptionData { key = "B", text = L10n.T("tut.qB") },
                    new OptionData { key = "C", text = L10n.T("tut.qC") },
                },
            };
            labels = ChemLabControllerLabels.Create(
                new ChemLabControllerLabels.Item(false, "Button_B", "B", L10n.T("lbl.close")),
                new ChemLabControllerLabels.Item(false, "Button_A", "A", L10n.T("lbl.submitStart")),
                new ChemLabControllerLabels.Item(false, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(false, "Trigger", null, L10n.T("lbl.trigger")),
                new ChemLabControllerLabels.Item(true, "Button_A", "X", L10n.T("lbl.panel")),
                new ChemLabControllerLabels.Item(true, "ThumbStick", null, L10n.T("lbl.stick")),
                new ChemLabControllerLabels.Item(true, "Trigger", null, L10n.T("lbl.trigger")));
            RefreshSteps();
            RefreshLanguage();
            RefreshHazards();
            K.MouseModeEnabled += AttachTablet;
        }

        void Start()
        {
            origin = FindFirstObjectByType<XROrigin>();
            if (origin != null) startYaw = origin.transform.eulerAngles.y;
            cup = FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None).FirstOrDefault(i => i.kind == ChemLabInspectable.Kind.Tutorial);
        }

        void OnDestroy() => K.MouseModeEnabled -= AttachTablet;

        void OnEnable()
        {
            ChemLabTeleportNode.PlayerArrived += OnArrived;
            ChemLabInspectable.Inspected += OnInspected;
            ChemLabControls.Confirm += Confirm;
            ChemLabControls.Back += Back;
            ChemLabControls.ToggleHelp += TogglePanel;
            ChemLabControls.BlockInspect = () => practice != null && practice.IsOpen;
        }

        void OnDisable()
        {
            ChemLabTeleportNode.PlayerArrived -= OnArrived;
            ChemLabInspectable.Inspected -= OnInspected;
            ChemLabControls.Confirm -= Confirm;
            ChemLabControls.Back -= Back;
            ChemLabControls.ToggleHelp -= TogglePanel;
            ChemLabControls.BlockInspect = null;
        }

        void Update()
        {
            // turning counts once the rig has turned 30 deg away from where it started (snap turn is 45)
            if (!done[Turn] && origin != null && Mathf.Abs(Mathf.DeltaAngle(origin.transform.eulerAngles.y, startYaw)) > 30f)
                Complete(Turn, L10n.T("tut.toastTurn"));
            K.KeepInView(practice.canvas.transform, 0.95f, -0.05f);
        }

        void Complete(int step, string toastText)
        {
            if (done[step]) return;
            done[step] = true;
            ChemLabAudio.Button();
            Toast(Current == StepCount ? L10n.T("lobby.toastReady") : toastText, K.Ok);
            RefreshSteps();
        }

        void OnArrived(ChemLabTeleportNode node)
        {
            if (node.nodeName == "Start") return;   // the node you spawn on doesn't count
            Complete(Teleport, L10n.T("lobby.toastTeleport"));
        }

        void OnInspected(ChemLabInspectable item)
        {
            if (item.kind != ChemLabInspectable.Kind.Tutorial || practice.IsOpen) return;
            if (done[Answer]) { Toast(L10n.T("lobby.toastReady"), K.Accent); return; }
            item.SetMark(ChemLabInspectable.Mark.Checked);
            if (!done[Inspect]) { done[Inspect] = true; RefreshSteps(); }
            practiceSel = null;
            practice.Show(L10n.T("tut.qTitle"), L10n.T("tut.qMeta"), practiceQ, item.transform.position);
        }

        void SelectPractice(string key)
        {
            practiceSel = key;
            practice.Highlight(key);
        }

        void SubmitPractice()
        {
            if (practiceSel == null) return;
            var opt = practiceQ.Option(practiceSel);
            if (practiceSel == practiceQ.correct)
            {
                ChemLabAudio.Correct();
                practice.Close();
                if (cup != null) cup.Fix();
                Complete(Answer, L10n.T("tut.toastCorrect"));
            }
            else
            {
                ChemLabAudio.Wrong(opt.critical);
                if (cup != null) cup.SetMark(ChemLabInspectable.Mark.Wrong);
                practice.ShowWrong(practiceSel, opt.critical, L10n.T("tut.qWrong"));
                practiceSel = null;
            }
        }

        void ClosePractice() => practice.Close();

        /// <summary>X: hide / show the task panel and the button labels; showing it again after hiding completes the step.</summary>
        void TogglePanel()
        {
            bool show = !tablet.gameObject.activeSelf;
            tablet.gameObject.SetActive(show);
            labels.Visible = show;
            if (!show) panelWasHidden = true;
            else if (panelWasHidden) Complete(Panel, L10n.T("tut.toastPanel"));
        }

        void Confirm()
        {
            if (practice.IsOpen) SubmitPractice();
            else StartLab();
        }

        void Back()
        {
            if (practice.IsOpen) ClosePractice();
            else CloseHazards();
        }

        void StartLab()
        {
            if (HazardsOpen || practice.IsOpen) return;   // A doesn't start the lab from behind another panel
            if (Application.CanStreamedLevelBeLoaded(ChemLabRun.LabScene)) SceneManager.LoadScene(ChemLabRun.LabScene);
            else Debug.LogWarning("[ChemLab] Lab scene not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        void SetLanguage(string lang)
        {
            if (lang == ChemLabRun.Language) return;
            L10n.Set(lang);
            SceneManager.LoadScene(ChemLabRun.LobbyScene);   // ponytail: a reload rebuilds every code-built panel in the new language
        }

        void RefreshLanguage()
        {
            for (int i = 0; i < 4; i++)
            {
                bool on = Langs[i] == ChemLabRun.Language;
                K.SetButtonColor(langBtns[i], on ? K.Accent : K.SurfaceHi);
                var l = K.ButtonLabel(langBtns[i]);
                l.color = on ? Color.white : K.Text;
            }
        }

        /// <summary>
        /// Step rows in sign grammar: green check disc = done, blue disc on a blue-tinted row = the step to do now,
        /// plain rim = later. The hint line explains the current step; the task panel mirrors it.
        /// </summary>
        void RefreshSteps()
        {
            int cur = Current;
            for (int i = 0; i < StepCount; i++)
            {
                var r = rows[i];
                bool isDone = done[i], isCur = i == cur;
                r.bg.color = isCur ? K.SelectFill : K.Bg;
                r.disc.color = isDone ? K.Ok : isCur ? K.Accent : Color.white;
                r.check.gameObject.SetActive(isDone);
                r.num.gameObject.SetActive(!isDone);
                r.num.color = isCur ? Color.white : K.Text;
                r.text.color = isDone ? K.TextDim : K.Text;
            }
            string doneText = L10n.T("tut.hDone", L10n.T("lobby.start"));
            hint.text = cur < StepCount ? L10n.T("tut.h" + (cur + 1)) : doneText;
            int count = done.Count(d => d);
            tabCount.text = L10n.T("tut.progress", count, StepCount);
            tabBar.Set(count / (float)StepCount);
            tabStep.text = cur < StepCount ? L10n.T("tut.s" + (cur + 1)) : doneText;
            K.ButtonLabel(startBtn).text = cur == StepCount ? L10n.T("lobby.startReady") : L10n.T("lobby.start");
        }

        // ================= builders =================

        void BuildMain()
        {
            main = K.Panel(transform, "Panel_Lobby", new Vector2(1100, 900));
            K.Label(main.transform, "Kicker", L10n.T("lobby.kicker"), new Vector2(0, 385), new Vector2(980, 40), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(main.transform, "Title", L10n.T("lobby.title"), new Vector2(0, 325), new Vector2(980, 80), 62, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            sub = K.Label(main.transform, "Sub", "", new Vector2(0, 268), new Vector2(980, 40), 28, K.TextDim, TextAlignmentOptions.Left);

            K.Label(main.transform, "LangTitle", L10n.T("lobby.language"), new Vector2(0, 200), new Vector2(980, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int i = 0; i < 4; i++)
            {
                string lang = Langs[i];
                langBtns[i] = K.Button(main.transform, "Lang_" + lang, lang, new Vector2(-400 + i * 150, 145), new Vector2(130, 64), K.Style.Ghost, () => SetLanguage(lang), 28);
            }
            K.Label(main.transform, "HazardsTitle", L10n.T("lobby.hazards"), new Vector2(325, 200), new Vector2(330, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var choose = K.Button(main.transform, "ChooseHazards", L10n.T("lobby.chooseHazards"), new Vector2(325, 145), new Vector2(330, 64), K.Style.Ghost, OpenHazards, 28);
            K.WarningSign(choose.transform, new Vector2(-121, 1), 44f);
            var cl = K.ButtonLabel(choose).rectTransform;
            cl.anchoredPosition = new Vector2(22, 0);
            cl.sizeDelta = new Vector2(240, 56);
            K.FitOneLine(K.ButtonLabel(choose), 20);

            K.Label(main.transform, "TutorialTitle", L10n.T("tut.title"), new Vector2(0, 72), new Vector2(980, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Img(main.transform, "RuleTop", new Vector2(0, 52), new Vector2(980, 2), K.Rule).raycastTarget = false;
            for (int i = 0; i < StepCount; i++)
            {
                float y = 23 - i * 58;
                var bg = K.Img(main.transform, "Step" + (i + 1), new Vector2(0, y), new Vector2(980, 54), K.Bg, K.Rounded);
                bg.pixelsPerUnitMultiplier = 2f;
                var disc = K.Img(bg.transform, "Disc", new Vector2(-458, 0), new Vector2(42, 42), Color.white, K.Circle);
                K.Img(disc.transform, "Rim", Vector2.zero, new Vector2(42, 42), K.Text, K.Ring);
                var check = K.Img(disc.transform, "Check", Vector2.zero, new Vector2(36, 36), Color.white, K.Check);
                var num = K.Label(disc.transform, "Num", (i + 1).ToString(), new Vector2(0, -1), new Vector2(42, 42), 24, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
                var text = K.FitOneLine(K.Label(bg.transform, "Text", L10n.T("tut.s" + (i + 1)), new Vector2(30, 0), new Vector2(880, 50), 28, K.Text, TextAlignmentOptions.MidlineLeft), 20);
                foreach (var g in bg.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
                K.Img(main.transform, "Rule" + (i + 1), new Vector2(0, y - 29), new Vector2(980, 2), K.Rule).raycastTarget = false;
                rows[i] = (bg, disc, check, num, text);
            }
            hint = K.Label(main.transform, "Hint", "", new Vector2(0, -276), new Vector2(980, 76), 28, K.Accent, TextAlignmentOptions.MidlineLeft);
            hint.enableAutoSizing = true; hint.fontSizeMin = 22; hint.fontSizeMax = 28;
            startBtn = K.Button(main.transform, "StartLab", L10n.T("lobby.start"), new Vector2(0, -375), new Vector2(980, 84), K.Style.Primary, StartLab, 36);

            if (mainPanelAnchor != null) main.transform.SetPositionAndRotation(mainPanelAnchor.position, mainPanelAnchor.rotation);
        }

        /// <summary>
        /// "Choose hazards" plate (1100 x 1080): two columns of ruled hazard rows (whole row toggles), the 1-10 count
        /// in the language-button pattern, a legend for the safety-critical disc, and Switch all on / Close (B).
        /// It takes the start panel's place while open, so two plates never overlap in depth.
        /// </summary>
        void BuildHazards()
        {
            var size = new Vector2(1100, 1080);
            hazards = K.Panel(transform, "Panel_Hazards", size);
            var t = hazards.transform;
            K.Label(t, "Title", L10n.T("hz.title"), new Vector2(0, 458), new Vector2(1000, 64), 52, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Sub", L10n.T("hz.sub"), new Vector2(0, 394), new Vector2(1000, 64), 28, K.TextDim, TextAlignmentOptions.TopLeft);   // two lines for longer languages
            K.Label(t, "PoolTitle", L10n.T("hz.pool"), new Vector2(0, 335), new Vector2(1000, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Img(t, "RuleTop", new Vector2(0, 309), new Vector2(1000, 2), K.Rule).raycastTarget = false;

            for (int i = 0; i < allHazards.Length && i < 10; i++)
            {
                var h = allHazards[i];
                var pos = new Vector2(i < 5 ? -257.5f : 257.5f, 266 - (i % 5) * 84);
                var bg = K.Img(t, "Hazard_" + h.id, pos, new Vector2(485, 82), K.Bg, K.Rounded);
                bg.pixelsPerUnitMultiplier = 1.5f;
                var btn = bg.gameObject.AddComponent<Button>();
                K.Tint(btn);
                string id = h.id;
                btn.onClick.AddListener(() => ToggleHazard(id));
                btn.onClick.AddListener(() => ChemLabAudio.Click());
                var sign = K.WarningSign(bg.transform, new Vector2(-214.5f, 0), 52f);
                var title = K.Label(bg.transform, "Title", h.title, new Vector2(-10.5f, 0), new Vector2(316, 80), 28, K.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                title.enableAutoSizing = true; title.fontSizeMin = 24; title.fontSizeMax = 28;
                var off = K.FitOneLine(K.Label(bg.transform, "Off", L10n.T("hz.off"), new Vector2(197.5f, 0), new Vector2(90, 40), 24, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold), 16);
                var crit = h.mandatoryCritical ? K.SignDisc(bg.transform, "Critical", new Vector2(225.5f, 0), 34, K.Bad, check: false) : null;
                K.Img(t, "Rule_" + h.id, pos + new Vector2(0, -42), new Vector2(485, 2), K.Rule).raycastTarget = false;
                hzRows.Add((id, sign, title, off, crit, h.mandatoryCritical));
            }

            K.Label(t, "CountTitle", L10n.T("hz.perRun"), new Vector2(0, -169), new Vector2(1000, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int n = 1; n <= 10; n++)
            {
                int count = n;
                countBtns[n - 1] = K.Button(t, "Count_" + n, n.ToString(), new Vector2(-454 + (n - 1) * 101, -236), new Vector2(88, 72), K.Style.Ghost, () => SetHazardsPerRun(count), 32);
            }
            perRunCaption = K.Label(t, "CountCaption", "", new Vector2(0, -313), new Vector2(1000, 40), 28, K.TextDim, TextAlignmentOptions.Left);
            K.SignDisc(t, "LegendDisc", new Vector2(-484, -362), 32, K.Bad, check: false);
            K.Label(t, "Legend", L10n.T("hz.legend"), new Vector2(23, -362), new Vector2(954, 40), 28, K.Text, TextAlignmentOptions.Left);

            K.Button(t, "SwitchAllOn", L10n.T("hz.allOn"), new Vector2(-335, -458), new Vector2(330, 72), K.Style.Ghost, SwitchAllHazardsOn);
            K.Button(t, "Close", L10n.T("common.close"), new Vector2(335, -458), new Vector2(330, 72), K.Style.Ghost, CloseHazards, key: "B");
            hazards.gameObject.SetActive(false);
        }

        void OpenHazards()
        {
            hazards.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            main.gameObject.SetActive(false);
            hazards.gameObject.SetActive(true);
        }

        void CloseHazards()
        {
            if (!HazardsOpen) return;
            hazards.gameObject.SetActive(false);
            main.gameObject.SetActive(true);
        }

        void ToggleHazard(string id)
        {
            if (!ChemLabRun.DisabledHazards.Remove(id))
            {
                if (allHazards.Count(h => !ChemLabRun.DisabledHazards.Contains(h.id)) <= 1) { Toast(L10n.T("hz.lastOn"), K.Warn); return; }
                ChemLabRun.DisabledHazards.Add(id);
            }
            ChemLabRun.SaveHazardSettings();
            RefreshHazards();
        }

        void SetHazardsPerRun(int n)
        {
            ChemLabRun.HazardsPerRun = n;
            ChemLabRun.SaveHazardSettings();
            RefreshHazards();
        }

        void SwitchAllHazardsOn()
        {
            ChemLabRun.DisabledHazards.Clear();
            ChemLabRun.SaveHazardSettings();
            RefreshHazards();
        }

        /// <summary>Row signs, count buttons, caption and the start panel subtitle follow the saved settings.</summary>
        void RefreshHazards()
        {
            int on = allHazards.Count(h => !ChemLabRun.DisabledHazards.Contains(h.id));
            int perRun = Mathf.Clamp(ChemLabRun.HazardsPerRun, 1, Mathf.Max(1, on));
            foreach (var r in hzRows)
            {
                bool isOn = !ChemLabRun.DisabledHazards.Contains(r.id);
                // on = yellow warning triangle; off = empty outlined triangle, dimmed title and the word OFF
                r.sign.GetComponent<Image>().color = isOn ? K.Warn : K.Bg;
                r.sign.Find("Rim").GetComponent<Image>().color = isOn ? K.Text : K.TextDim;
                r.sign.Find("Mark").gameObject.SetActive(isOn);
                r.title.color = isOn ? K.Text : K.TextDim;
                r.off.gameObject.SetActive(!isOn);
                if (r.critical != null) r.critical.rectTransform.anchoredPosition = new Vector2(isOn ? 225.5f : 127.5f, 0);
                // the title takes the row up to whatever sits at its right end (critical disc and/or OFF)
                float right = (r.critical != null ? (isOn ? 208.5f : 110.5f) : (isOn ? 242.5f : 152.5f)) - 12f, left = -168.5f;
                r.title.rectTransform.sizeDelta = new Vector2(right - left, 80);
                r.title.rectTransform.anchoredPosition = new Vector2((left + right) / 2f, 0);
            }
            for (int n = 1; n <= 10; n++)
            {
                var b = countBtns[n - 1];
                bool chosen = n == perRun, available = n <= on;
                b.interactable = available;
                K.SetButtonColor(b, chosen ? K.Accent : available ? K.Bg : K.Surface);
                b.transform.Find("Outline").gameObject.SetActive(available && !chosen);
                K.ButtonLabel(b).color = chosen ? Color.white : available ? K.Text : new Color(0.42f, 0.42f, 0.396f);   // #6B6B65, 4.6:1 on Surface
            }
            perRunCaption.text = L10n.T("hz.caption", perRun, on);
            sub.text = L10n.T("lobby.sub", perRun);
        }

        /// <summary>"How the inspection works": one row per rule, each led by the sign it refers to in the lab.</summary>
        void BuildTeaching()
        {
            var size = new Vector2(780, 960);
            teaching = K.Panel(transform, "Panel_Teaching", size, blocksRays: false);
            var t = teaching.transform;
            K.FitOneLine(K.Label(t, "Title", L10n.T("tch.title"), new Vector2(0, 410), new Vector2(700, 60), 42, K.Text, TextAlignmentOptions.Left, FontStyles.Bold), 30);
            K.Img(t, "RuleTop", new Vector2(0, 362), new Vector2(700, 2), K.Rule).raycastTarget = false;

            var icon = new Vector2(-305, 0);
            RectTransform Row(int i, string key)
            {
                float y = 300 - i * 118;
                var row = new GameObject("Row" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(t, false);
                row.anchoredPosition = new Vector2(0, y);
                row.sizeDelta = new Vector2(700, 110);
                var l = K.Label(row, "Text", L10n.T(key), new Vector2(50, 0), new Vector2(580, 108), 28, K.Text, TextAlignmentOptions.MidlineLeft);
                l.enableAutoSizing = true; l.fontSizeMin = 22; l.fontSizeMax = 28;
                K.Img(t, "Rule" + i, new Vector2(0, y - 59), new Vector2(700, 2), K.Rule).raycastTarget = false;
                return row;
            }
            K.WarningSign(Row(0, "tch.find"), icon, 76f);
            K.KeyDisc(Row(1, "tch.answer"), "A", icon, 66f, K.Accent);
            K.SignDisc(Row(2, "tch.right"), "Icon", icon, 70f, K.Ok, check: true);
            K.SignDisc(Row(3, "tch.wrong"), "Icon", icon, 70f, K.Bad, check: false, cross: true);
            K.Chip(Row(4, "tch.critical"), "Icon", L10n.T("q.dangerous"), icon + new Vector2(-2, 0), new Vector2(128, 44), K.Bad, Color.white);   // ends just left of the text column (-240)
            var o = Row(5, "tch.outline");
            var sample = K.Img(o, "Icon", icon, new Vector2(64, 64), K.SurfaceHi, K.Rounded);
            sample.pixelsPerUnitMultiplier = 2f;
            K.Outline(sample.transform, new Vector2(64, 64), K.Accent, 1f);
            var score = K.Label(t, "Score", L10n.T("tch.score"), new Vector2(0, -415), new Vector2(700, 80), 26, K.TextDim, TextAlignmentOptions.TopLeft);
            score.enableAutoSizing = true; score.fontSizeMin = 22; score.fontSizeMax = 26;
            if (teachingPanelAnchor != null) teaching.transform.SetPositionAndRotation(teachingPanelAnchor.position, teachingPanelAnchor.rotation);
        }

        static readonly string[] PosterKeys = { "poster.chem", "poster.food", "poster.ppe", "poster.eyewash" };

        /// <summary>Safety signs on the walls (set dressing): an ISO-style sign above a caption, on enamel plates.</summary>
        void BuildPosters()
        {
            if (posterAnchors == null) return;
            for (int i = 0; i < posterAnchors.Length && i < PosterKeys.Length; i++)
            {
                if (posterAnchors[i] == null) continue;
                var p = K.Panel(transform, "Poster" + i, new Vector2(500, 640), 0.0012f, accentLine: false, blocksRays: false);
                var t = p.transform;
                var sign = new Vector2(0, 90);
                switch (i)
                {
                    case 0: K.WarningSign(t, sign, 330f); break;
                    case 1:   // a cup behind the red prohibition ring
                        K.Img(t, "Cup", sign + new Vector2(-12, -10), new Vector2(110, 140), K.Text, K.Rounded).pixelsPerUnitMultiplier = 3f;
                        K.Img(t, "Handle", sign + new Vector2(62, 0), new Vector2(64, 64), K.Text, K.Ring);
                        K.Img(t, "Prohibited", sign, new Vector2(330, 330), K.Bad, K.Prohibition);
                        break;
                    case 2: K.SignDisc(t, "Mandatory", sign, 330f, K.Accent, check: false, info: true); break;
                    default:   // safe-condition sign: green square, white cross
                        K.Img(t, "Safe", sign, new Vector2(320, 320), K.Ok, K.Rounded).pixelsPerUnitMultiplier = 0.6f;
                        K.Img(t, "CrossV", sign, new Vector2(70, 220), Color.white);
                        K.Img(t, "CrossH", sign, new Vector2(220, 70), Color.white);
                        break;
                }
                var cap = K.Label(t, "Caption", L10n.T(PosterKeys[i]), new Vector2(0, -205), new Vector2(440, 170), 52, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
                cap.enableAutoSizing = true; cap.fontSizeMin = 32; cap.fontSizeMax = 52;
                p.transform.SetPositionAndRotation(posterAnchors[i].position, posterAnchors[i].rotation);
            }
        }

        /// <summary>Task panel on the left controller: tutorial progress and the step to do now (X hides / shows it).</summary>
        void BuildTablet()
        {
            tablet = K.Panel(transform, "HUD_Tablet", new Vector2(620, 480), 0.0007f, blocksRays: false);
            var t = tablet.transform;
            K.Label(t, "Title", L10n.T("tut.panelTitle"), new Vector2(0, 196), new Vector2(560, 40), 24, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            tabCount = K.Label(t, "Count", "", new Vector2(0, 130), new Vector2(560, 90), 86, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Caption", L10n.T("tut.caption"), new Vector2(110, 110), new Vector2(320, 40), 24, K.TextDim, TextAlignmentOptions.Right);
            tabBar = K.ProgressBar(t, "Bar", new Vector2(0, 68), new Vector2(560, 12), K.Ok);
            tabStep = K.Label(t, "Step", "", new Vector2(0, -50), new Vector2(560, 150), 30, K.Text, TextAlignmentOptions.TopLeft);
            tabStep.enableAutoSizing = true; tabStep.fontSizeMin = 24; tabStep.fontSizeMax = 30;
            K.Label(t, "Footer", L10n.T("tut.footer", K.Hex(K.TextDim)), new Vector2(0, -198), new Vector2(560, 44), 23, K.Text, TextAlignmentOptions.Left);
            AttachTablet();
        }

        void AttachTablet() { if (tablet != null) K.AttachTablet(tablet.transform, leftHandAnchor); }

        void BuildToast()
        {
            toast = K.Panel(transform, "Panel_Toast", new Vector2(820, 96), 0.0008f, accentLine: false, blocksRays: false);
            toastText = K.Label(toast.transform, "Text", "", new Vector2(0, 0), new Vector2(760, 84), 28, K.Text, TextAlignmentOptions.Center);
            toast.gameObject.SetActive(false);
        }

        void Toast(string text, Color col)
        {
            toastText.text = $"<color={K.Hex(col)}><b>|</b></color>  {text}";
            K.PlaceInFront(toast.transform, 0.85f, -0.3f);
            toast.gameObject.SetActive(true);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(Hide());
        }

        IEnumerator Hide()
        {
            yield return new WaitForSeconds(2.8f);
            toast.gameObject.SetActive(false);
        }

        /// <summary>A stylised Quest controller drawn with UI shapes, trigger highlighted.</summary>
        public static RectTransform ControllerGraphic(Transform parent, Vector2 pos, float scale)
        {
            var root = new GameObject("ControllerGraphic", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchoredPosition = pos;
            root.localScale = Vector3.one * scale;

            Image I(string n, Vector2 p, Vector2 s, Color c, Sprite sp) { var i = K.Img(root, n, p, s, c, sp); i.raycastTarget = false; return i; }
            I("Ring", new Vector2(0, 40), new Vector2(250, 250), K.Surface, K.Circle);
            I("RingInner", new Vector2(0, 40), new Vector2(214, 214), K.Bg, K.Circle);
            I("Face", new Vector2(10, 20), new Vector2(170, 150), K.Surface, K.Rounded);
            I("Handle", new Vector2(-20, -110), new Vector2(110, 190), K.Surface, K.Rounded);
            I("Grip", new Vector2(-82, -95), new Vector2(26, 110), K.SurfaceHi, K.Rounded);
            I("Stick", new Vector2(-30, 45), new Vector2(54, 54), K.SurfaceHi, K.Circle);
            I("StickTop", new Vector2(-30, 45), new Vector2(34, 34), new Color(0.24f, 0.29f, 0.37f), K.Circle);
            I("BtnA", new Vector2(45, 5), new Vector2(36, 36), K.SurfaceHi, K.Circle);
            I("BtnB", new Vector2(65, 50), new Vector2(36, 36), K.SurfaceHi, K.Circle);
            var trig = I("Trigger", new Vector2(40, 150), new Vector2(110, 46), K.Accent, K.Rounded);
            var glow = I("TriggerGlow", new Vector2(40, 150), new Vector2(140, 74), new Color(K.Accent.r, K.Accent.g, K.Accent.b, 0.25f), K.Rounded);
            glow.transform.SetSiblingIndex(trig.transform.GetSiblingIndex());

            K.Label(root, "LTrigger", L10n.T("ctrl.trigger"), new Vector2(175, 150), new Vector2(160, 70), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(root, "LStick", L10n.T("ctrl.stick"), new Vector2(-170, 60), new Vector2(150, 70), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            K.Label(root, "LA", "A", new Vector2(45, 5), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LB", "B", new Vector2(65, 50), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LGrip", L10n.T("ctrl.grip"), new Vector2(-160, -95), new Vector2(110, 40), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            return root;
        }
    }
}
