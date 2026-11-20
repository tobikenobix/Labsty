using System.Collections;
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

        Canvas main, controller, toast;
        TextMeshProUGUI step1, step2, toastText;
        Image step1Dot, step2Dot;
        Button startBtn;
        readonly Button[] langBtns = new Button[4];
        static readonly string[] Langs = { "DE", "FR", "IT", "EN" };
        bool teleported, selected;
        Coroutine toastCo;

        void Awake()
        {
            BuildMain();
            BuildController();
            BuildToast();
            RefreshSteps();
            RefreshLanguage();
        }

        void OnEnable()
        {
            ChemLabTeleportNode.PlayerArrived += OnArrived;
            ChemLabInspectable.Inspected += OnInspected;
            ChemLabControls.Confirm += StartLab;
            ChemLabControls.BlockInspect = null;
        }

        void OnDisable()
        {
            ChemLabTeleportNode.PlayerArrived -= OnArrived;
            ChemLabInspectable.Inspected -= OnInspected;
            ChemLabControls.Confirm -= StartLab;
        }

        void OnArrived(ChemLabTeleportNode node)
        {
            if (teleported || node.nodeName == "Start") return;   // the node you spawn on doesn't count
            teleported = true;
            ChemLabAudio.Button();
            Toast("Nice! Left stick walks, right stick turns.", K.Ok);
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
                l.color = on ? new Color(0.02f, 0.1f, 0.1f) : (Langs[i] == "EN" ? K.Text : K.TextDim);
            }
        }

        void RefreshSteps()
        {
            step1.text = (teleported ? $"<color={K.Hex(K.Ok)}>Done</color>  " : "") + "<b>Walk</b> onto the glowing ring: push the LEFT stick to move, RIGHT stick to turn.";
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
            K.Label(main.transform, "Sub", "BSL-1 laboratory · 10 hidden hazards · 7 minutes", new Vector2(0, 268), new Vector2(980, 40), 28, K.TextDim, TextAlignmentOptions.Left);

            K.Label(main.transform, "LangTitle", "LANGUAGE", new Vector2(0, 200), new Vector2(980, 34), 22, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int i = 0; i < 4; i++)
            {
                string lang = Langs[i];
                langBtns[i] = K.Button(main.transform, "Lang_" + lang, lang, new Vector2(-400 + i * 150, 145), new Vector2(130, 64), K.Style.Ghost, () => SetLanguage(lang), 28);
            }

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

        void BuildController()
        {
            controller = K.Panel(transform, "Panel_Controller", new Vector2(700, 760), blocksRays: false);
            K.Label(controller.transform, "Title", "Your controller", new Vector2(0, 320), new Vector2(620, 60), 40, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            ControllerGraphic(controller.transform, new Vector2(0, 60), 1.15f);
            K.Label(controller.transform, "Caption",
                $"Pull the <b><color={K.Hex(K.Accent)}>TRIGGER</color></b> to select.\n<b>Left stick</b> walk   <b>Right stick</b> turn\n<b>A</b> confirm   <b>B</b> close   <b>X</b> controls",
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
            I("Ring", new Vector2(0, 40), new Vector2(250, 250), new Color(1, 1, 1, 0.07f), K.Circle);
            I("RingInner", new Vector2(0, 40), new Vector2(214, 214), K.Bg, K.Circle);
            I("Face", new Vector2(10, 20), new Vector2(170, 150), K.Surface, K.Rounded);
            I("Handle", new Vector2(-20, -110), new Vector2(110, 190), K.Surface, K.Rounded);
            I("Grip", new Vector2(-82, -95), new Vector2(26, 110), K.SurfaceHi, K.Rounded);
            I("Stick", new Vector2(-30, 45), new Vector2(54, 54), K.SurfaceHi, K.Circle);
            I("StickTop", new Vector2(-30, 45), new Vector2(34, 34), new Color(0.24f, 0.29f, 0.37f), K.Circle);
            I("BtnA", new Vector2(45, 5), new Vector2(36, 36), K.SurfaceHi, K.Circle);
            I("BtnB", new Vector2(65, 50), new Vector2(36, 36), K.SurfaceHi, K.Circle);
            var trig = I("Trigger", new Vector2(40, 150), new Vector2(110, 46), K.Accent, K.Rounded);
            var glow = I("TriggerGlow", new Vector2(40, 150), new Vector2(140, 74), new Color(0f, 0.78f, 0.68f, 0.25f), K.Rounded);
            glow.transform.SetSiblingIndex(trig.transform.GetSiblingIndex());

            K.Label(root, "LTrigger", "TRIGGER\n<size=70%>select</size>", new Vector2(175, 150), new Vector2(160, 70), 24, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(root, "LStick", "STICK\n<size=70%>L walk / R turn</size>", new Vector2(-170, 60), new Vector2(150, 70), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            K.Label(root, "LA", "A", new Vector2(45, 5), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LB", "B", new Vector2(65, 50), new Vector2(36, 36), 20, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(root, "LGrip", "GRIP", new Vector2(-160, -95), new Vector2(110, 40), 20, K.TextDim, TextAlignmentOptions.Right, FontStyles.Bold);
            return root;
        }
    }
}
