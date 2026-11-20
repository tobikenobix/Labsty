using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// Scene 1: 60-second onboarding lobby. Language selector (EN functional), a controller graphic
    /// explaining the trigger, one glowing floor ring to practise walking, one coffee cup to practise
    /// selecting, and the Start Lab button that loads the lab (where the 7-minute countdown begins).
    /// </summary>
    public class ChemLabLobby : MonoBehaviour
    {
        public Transform mainPanelAnchor;
        public Transform controllerPanelAnchor;

        Canvas main, controller, toast, hazards;
        TextMeshProUGUI step1, step2, toastText, sub, perRunCaption;
        HazardData[] allHazards;
        readonly List<(string id, RectTransform sign, TextMeshProUGUI title, TextMeshProUGUI off, Image critical, bool mandatory)> hzRows =
            new List<(string, RectTransform, TextMeshProUGUI, TextMeshProUGUI, Image, bool)>();
        readonly Button[] countBtns = new Button[10];
        bool HazardsOpen => hazards != null && hazards.gameObject.activeSelf;
        Image step1Dot, step2Dot;
        Button startBtn;
        readonly Button[] langBtns = new Button[4];
        static readonly string[] Langs = { "DE", "FR", "IT", "EN" };
        bool teleported, selected;
        Coroutine toastCo;

        void Awake()
        {
            allHazards = LabConfig.Load().hazards;
            BuildMain();
            BuildController();
            BuildToast();
            BuildHazards();
            RefreshSteps();
            RefreshLanguage();
            RefreshHazards();
        }

        void OnEnable()
        {
            ChemLabTeleportNode.PlayerArrived += OnArrived;
            ChemLabInspectable.Inspected += OnInspected;
            ChemLabControls.Confirm += StartLab;
            ChemLabControls.Back += CloseHazards;
            ChemLabControls.BlockInspect = null;
        }

        void OnDisable()
        {
            ChemLabTeleportNode.PlayerArrived -= OnArrived;
            ChemLabInspectable.Inspected -= OnInspected;
            ChemLabControls.Confirm -= StartLab;
            ChemLabControls.Back -= CloseHazards;
        }

        void OnArrived(ChemLabTeleportNode node)
        {
            if (teleported || node.nodeName == "Start") return;   // the node you spawn on doesn't count
            teleported = true;
            ChemLabAudio.Button();
            Toast("Nice! Stick forward teleports, left/right turns.", K.Ok);
            RefreshSteps();
        }

        void OnInspected(ChemLabInspectable item)
        {
            if (item.kind != ChemLabInspectable.Kind.Tutorial) return;
            if (!selected)
            {
                selected = true;
                ChemLabAudio.Correct();
                Toast("Selected! In the lab, this opens a safety question.", K.Ok);
                RefreshSteps();
            }
            else Toast("You've got it. Press Start Lab when ready.", K.Accent);
        }

        void StartLab()
        {
            if (HazardsOpen) return;   // A doesn't start the lab from behind the hazard panel
            if (Application.CanStreamedLevelBeLoaded(ChemLabRun.LabScene)) SceneManager.LoadScene(ChemLabRun.LabScene);
            else Debug.LogWarning("[ChemLab] Lab scene not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        void SetLanguage(string lang)
        {
            if (lang != "EN") { Toast($"{lang} is coming soon. English only in this MVP.", K.Warn); return; }
            ChemLabRun.Language = lang;
            RefreshLanguage();
        }

        void RefreshLanguage()
        {
            for (int i = 0; i < 4; i++)
            {
                bool on = Langs[i] == ChemLabRun.Language;
                K.SetButtonColor(langBtns[i], on ? K.Accent : K.SurfaceHi);
                var l = K.ButtonLabel(langBtns[i]);
                l.color = on ? Color.white : (Langs[i] == "EN" ? K.Text : K.TextDim);
            }
        }

        void RefreshSteps()
        {
            step1.text = (teleported ? $"<color={K.Hex(K.Ok)}>Done</color>  " : "") + "<b>Teleport</b> onto the glowing ring: push a stick forward, aim the arc at the ring, release.";
            step2.text = (selected ? $"<color={K.Hex(K.Ok)}>Done</color>  " : "") + "<b>Select</b> the coffee cup: point at it and pull the trigger.";
            step1Dot.color = teleported ? K.Ok : K.SurfaceHi;
            step2Dot.color = selected ? K.Ok : K.SurfaceHi;
            K.ButtonLabel(startBtn).text = teleported && selected ? "Start Lab  (A)" : "Start Lab";
        }

        // ================= builders =================

        void BuildMain()
        {
            main = K.Panel(transform, "Panel_Lobby", new Vector2(1100, 900));
            K.Label(main.transform, "Kicker", "TUM · ETH  —  LAB SAFETY INITIATIVE", new Vector2(0, 385), new Vector2(980, 40), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(main.transform, "Title", "Lab Safety Inspection", new Vector2(0, 325), new Vector2(980, 80), 62, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            sub = K.Label(main.transform, "Sub", "", new Vector2(0, 268), new Vector2(980, 40), 28, K.TextDim, TextAlignmentOptions.Left);

            K.Label(main.transform, "LangTitle", "LANGUAGE", new Vector2(0, 200), new Vector2(980, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int i = 0; i < 4; i++)
            {
                string lang = Langs[i];
                langBtns[i] = K.Button(main.transform, "Lang_" + lang, lang, new Vector2(-400 + i * 150, 145), new Vector2(130, 64), K.Style.Ghost, () => SetLanguage(lang), 28);
            }
            K.Label(main.transform, "HazardsTitle", "HAZARDS", new Vector2(325, 200), new Vector2(330, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var choose = K.Button(main.transform, "ChooseHazards", "Choose hazards", new Vector2(325, 145), new Vector2(330, 64), K.Style.Ghost, OpenHazards, 28);
            K.WarningSign(choose.transform, new Vector2(-121, 1), 44f);
            var cl = K.ButtonLabel(choose).rectTransform;
            cl.anchoredPosition = new Vector2(22, 0);
            cl.sizeDelta = new Vector2(240, 56);

            K.Label(main.transform, "WarmupTitle", "60-SECOND WARM-UP", new Vector2(0, 60), new Vector2(980, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var c1 = K.Img(main.transform, "Step1Card", new Vector2(0, -15), new Vector2(980, 100), K.Surface, K.Rounded);
            step1Dot = K.Img(c1.transform, "Dot", new Vector2(-450, 0), new Vector2(26, 26), K.SurfaceHi, K.Circle);
            step1 = K.Label(c1.transform, "Text", "", new Vector2(30, 0), new Vector2(880, 90), 26, K.Text, TextAlignmentOptions.Left);
            var c2 = K.Img(main.transform, "Step2Card", new Vector2(0, -130), new Vector2(980, 100), K.Surface, K.Rounded);
            step2Dot = K.Img(c2.transform, "Dot", new Vector2(-450, 0), new Vector2(26, 26), K.SurfaceHi, K.Circle);
            step2 = K.Label(c2.transform, "Text", "", new Vector2(30, 0), new Vector2(880, 90), 26, K.Text, TextAlignmentOptions.Left);
            foreach (var g in new Graphic[] { c1, c2, step1Dot, step2Dot }) g.raycastTarget = false;

            K.Label(main.transform, "Rules", "In the lab: find each hazard, choose the safest action. Right answer = hazard fixed. Wrong answer = logged as a mistake.",
                new Vector2(0, -250), new Vector2(980, 80), 24, K.TextDim, TextAlignmentOptions.Left);
            startBtn = K.Button(main.transform, "StartLab", "Start Lab", new Vector2(0, -365), new Vector2(980, 96), K.Style.Primary, StartLab, 36);

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
            K.Label(t, "Title", "Hazards in the lab", new Vector2(0, 458), new Vector2(1000, 64), 52, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Sub", "Switch hazards on or off, then choose how many to plant each run.", new Vector2(0, 405), new Vector2(1000, 40), 28, K.TextDim, TextAlignmentOptions.Left);
            K.Label(t, "PoolTitle", "PLANTED HAZARDS", new Vector2(0, 335), new Vector2(1000, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
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
                var off = K.Label(bg.transform, "Off", "OFF", new Vector2(207.5f, 0), new Vector2(70, 40), 24, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
                var crit = h.mandatoryCritical ? K.SignDisc(bg.transform, "Critical", new Vector2(225.5f, 0), 34, K.Bad, check: false) : null;
                K.Img(t, "Rule_" + h.id, pos + new Vector2(0, -42), new Vector2(485, 2), K.Rule).raycastTarget = false;
                hzRows.Add((id, sign, title, off, crit, h.mandatoryCritical));
            }

            K.Label(t, "CountTitle", "HAZARDS PER RUN", new Vector2(0, -169), new Vector2(1000, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int n = 1; n <= 10; n++)
            {
                int count = n;
                countBtns[n - 1] = K.Button(t, "Count_" + n, n.ToString(), new Vector2(-454 + (n - 1) * 101, -236), new Vector2(88, 72), K.Style.Ghost, () => SetHazardsPerRun(count), 32);
            }
            perRunCaption = K.Label(t, "CountCaption", "", new Vector2(0, -313), new Vector2(1000, 40), 28, K.TextDim, TextAlignmentOptions.Left);
            K.SignDisc(t, "LegendDisc", new Vector2(-484, -362), 32, K.Bad, check: false);
            K.Label(t, "Legend", "Safety-critical: must be answered correctly to pass.", new Vector2(23, -362), new Vector2(954, 40), 28, K.Text, TextAlignmentOptions.Left);

            K.Button(t, "SwitchAllOn", "Switch all on", new Vector2(-335, -458), new Vector2(330, 72), K.Style.Ghost, SwitchAllHazardsOn);
            K.Button(t, "Close", "Close", new Vector2(335, -458), new Vector2(330, 72), K.Style.Ghost, CloseHazards, key: "B");
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
                if (allHazards.Count(h => !ChemLabRun.DisabledHazards.Contains(h.id)) <= 1) { Toast("At least one hazard has to stay on.", K.Warn); return; }
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
                if (r.critical != null) r.critical.rectTransform.anchoredPosition = new Vector2(isOn ? 225.5f : 149.5f, 0);
                // the title takes the row up to whatever sits at its right end (critical disc and/or OFF)
                float right = (r.critical != null ? (isOn ? 208.5f : 132.5f) : (isOn ? 242.5f : 172.5f)) - 12f, left = -168.5f;
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
                K.ButtonLabel(b).color = chosen ? Color.white : available ? K.Text : new Color(0.64f, 0.64f, 0.61f);
            }
            perRunCaption.text = $"Each run plants {perRun} of the {on} hazards that are on, picked at random.";
            sub.text = $"BSL-1 laboratory · {perRun} hidden hazards · 7 minutes";
        }

        void BuildController()
        {
            controller = K.Panel(transform, "Panel_Controller", new Vector2(700, 760), blocksRays: false);
            K.Label(controller.transform, "Title", "Your controller", new Vector2(0, 320), new Vector2(620, 60), 40, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            ControllerGraphic(controller.transform, new Vector2(0, 60), 1.15f);
            K.Label(controller.transform, "Caption",
                $"Pull the <b><color={K.Hex(K.Accent)}>TRIGGER</color></b> to select.\n<b>Stick forward</b> teleport   <b>left/right</b> turn\n<b>A</b> confirm   <b>B</b> close   <b>X</b> controls",
                new Vector2(0, -260), new Vector2(640, 170), 27, K.Text, TextAlignmentOptions.Center);
            if (controllerPanelAnchor != null) controller.transform.SetPositionAndRotation(controllerPanelAnchor.position, controllerPanelAnchor.rotation);
        }

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

            K.Label(root, "LTrigger", "TRIGGER\n<size=70%>select</size>", new Vector2(175, 150), new Vector2(160, 70), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(root, "LStick", "STICK\n<size=70%>teleport / turn</size>", new Vector2(-170, 60), new Vector2(150, 70), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            K.Label(root, "LA", "A", new Vector2(45, 5), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LB", "B", new Vector2(65, 50), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LGrip", "GRIP", new Vector2(-160, -95), new Vector2(110, 40), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            return root;
        }
    }
}
