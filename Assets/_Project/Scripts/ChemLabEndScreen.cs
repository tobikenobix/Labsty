using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// The assessment report card (a kit sign plate with three tabs: result, details, explanations) and a static
    /// "Basel Leaderboard" mockup. The lobby adds it when a run has just ended (ChemLabRun.ShowResults) and sets the
    /// anchors; it reads the run stored in ChemLabRun (JSON/CSV export already written).
    /// </summary>
    public class ChemLabEndScreen : MonoBehaviour
    {
        public Transform resultsAnchor;
        public Transform leaderboardAnchor;

        Canvas results, board;

        static readonly (string name, string team, int score, string time)[] Mock =
        {
            ("L. Meier", "Basel Campus", 97, "04:41"),
            ("S. Rossi", "Basel Campus", 94, "05:02"),
            ("A. Dubois", "St. Johann", 90, "05:37"),
            ("J. Weber", "Basel Campus", 86, "06:10"),
            ("M. Keller", "Schweizerhalle", 79, "06:48"),
            ("N. Brunner", "St. Johann", 72, "07:00"),
        };

        // Start, not Awake: the lobby sets the anchors right after AddComponent
        void Start()
        {
            BuildReportCard();
            BuildLeaderboard();
        }

        // No A binding here: A submits answers in the lab, so a reflex press would throw away the report. Ray-click the button.
        void OnEnable() { ChemLabControls.Back += BackToResult; ChemLabControls.BlockInspect = null; }
        void OnDisable() { ChemLabControls.Back -= BackToResult; }

        void PlayAgain() => Load(ChemLabRun.LabScene);
        void Lobby() => Load(ChemLabRun.LobbyScene);

        static void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning("[ChemLab] Scene " + scene + " not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        // ================= report card =================

        // 28-unit body text at ~0.92 deg x-height (scale / distance = 0.00107); one view holds ~900 units of height,
        // so the card is paged with tabs instead of a second panel in front of it (one plate at a time).
        const float CardScale = 0.00118f;   // at ResultsAnchor, 1.10 m
        const float W = 1000f, H = 900f, L = -W / 2f + 50f, R = W / 2f - 50f, Inner = W - 100f, Top = H / 2f - 36f;
        const int TabResult = 0, TabDetails = 1, TabExplain = 2;
        GameObject[] pages;
        Button[] tabs;

        void BuildReportCard()
        {
            var r = ChemLabRun.LastResult;
            var recs = ChemLabRun.LastRecords;
            int total = recs.Count;

            results = K.Panel(transform, "Panel_Report", new Vector2(W, H), CardScale, accentLine: false);
            var t = results.transform;
            GameObject Page(string name)
            {
                var g = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)g.transform;
                rt.SetParent(t, false);
                rt.sizeDelta = new Vector2(W, H);
                return g;
            }
            pages = new[] { Page("Page_Result"), Page("Page_Details"), Page("Page_Explanations") };

            Transform p = pages[TabResult].transform;   // where Txt / HRule draw
            TextMeshProUGUI Txt(string name, string s, float x, float y, float w, float h, float size, Color c, TextAlignmentOptions a, FontStyles st = FontStyles.Normal)
                => K.Label(p, name, s, new Vector2(x + w / 2f, y - h / 2f), new Vector2(w, h), size, c, a, st);
            void HRule(float y) { var i = K.Img(p, "Rule", new Vector2(0, y), new Vector2(Inner, 2f), K.Rule); i.raycastTarget = false; }

            // ---- tab 1: result. The outcome leads as a sign band (like the question plate's warning band); the score sits below it.
            var (outcome, band, ink) = r.outcome == Outcome.SafetyReviewRequired ? (L10n.T("end.chipReview"), K.Bad, Color.white)
                                     : r.outcome == Outcome.RetrainingRequired ? (L10n.T("end.chipRetrain"), K.Warn, K.Text)
                                     : (L10n.T("end.chipPassed"), K.Ok, Color.white);
            K.Band(p, new Vector2(W, H), 150f, band);
            var signPos = new Vector2(-404, 375);
            if (r.outcome == Outcome.RetrainingRequired) K.WarningSign(p, signPos, 112f);
            else
            {
                // white sign disc on the coloured band: green check = passed, red ! = safety review
                var disc = K.Img(p, "OutcomeSign", signPos, new Vector2(96, 96), Color.white, K.Circle);
                disc.raycastTarget = false;
                var glyph = r.outcome == Outcome.Pass
                    ? K.Img(disc.transform, "Glyph", Vector2.zero, new Vector2(78, 78), K.Ok, K.Check)
                    : K.Img(disc.transform, "Glyph", Vector2.zero, new Vector2(16, 50), K.Bad, K.Exclaim);
                glyph.raycastTarget = false;
            }
            K.FitOneLine(K.Label(p, "Outcome", outcome, new Vector2(74, 392), new Vector2(772, 58), 46, ink, TextAlignmentOptions.Left, FontStyles.Bold), 30);
            string id = ChemLabRun.StudentId.ToUpper();
            string meta = L10n.T("end.kicker", id, ChemLabRun.Attempt.ToString("000"));
            if (id.Length == 0) meta = meta.Substring(meta.IndexOf('·') + 1).Trim();   // no ID typed: start at "Session"
            K.FitOneLine(K.Label(p, "Meta", $"{meta}  ·  {K.Mmss(r.duration)}", new Vector2(74, 340), new Vector2(772, 36), 28, ink, TextAlignmentOptions.Left), 22);

            // three stat columns: score | detection | decisions
            float sy = Top - 150f;
            float colW = Inner / 3f;
            void Stat(int col, string label, string big, string small)
            {
                float x = L + col * colW;
                K.FitOneLine(Txt("StatL", label, x, sy, colW - 24, 40, 28, K.TextDim, TextAlignmentOptions.Left), 20);
                Txt("StatB", big, x, sy - 40, colW - 24, 58, 44, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
                if (small == null) return;
                // wraps to a second line instead of shrinking, so all three columns keep one text size
                var s = Txt("StatS", small, x, sy - 100, colW - 24, 64, 26, K.TextDim, TextAlignmentOptions.TopLeft);
                s.enableAutoSizing = true; s.fontSizeMin = 22; s.fontSizeMax = 26;
            }
            Stat(0, L10n.T("end.rowScore"), $"{r.score}/100", null);
            Stat(1, L10n.T("end.detection"), $"{r.detected}/{total}", r.falsePenalty > 0
                ? L10n.T("end.pointsPenalty", r.detectionPoints, r.maxDetectionPoints, r.falsePenalty)
                : L10n.T("end.points", r.detectionPoints, r.maxDetectionPoints));
            Stat(2, L10n.T("end.decisions"), $"{r.correct}/{r.answered}", L10n.T("end.points", r.decisionPoints, r.maxDecisionPoints));
            HRule(sy - 172);

            // what it means for you, in the feedback-band style of the question plate; the box hugs its text
            float boxTop = sy - 192, maxH = boxTop - (-H / 2f + 214f);
            string why = DecisionText(r, recs);
            float boxH = Mathf.Min(maxH, Mathf.Max(100f, Mathf.Ceil(Measure(why, Inner - 48, 28)) + 44f));
            var box = K.Img(p, "DecisionBox", new Vector2(0, boxTop - boxH / 2f), new Vector2(Inner, boxH), K.Surface, K.Rounded);
            box.pixelsPerUnitMultiplier = 1.5f;
            box.raycastTarget = false;
            var decision = Txt("DecisionBody", why, L + 24, boxTop - 22, Inner - 48, boxH - 40, 28, K.Text, TextAlignmentOptions.TopLeft);
            decision.enableAutoSizing = true; decision.fontSizeMin = 24; decision.fontSizeMax = 28;   // longer languages may need a sixth line

            // ---- tab 2: pass conditions and issues ----
            p = pages[TabDetails].transform;
            K.FitOneLine(Txt("DetailsTitle", L10n.T("end.detailsTitle"), L, Top, Inner, 56, 40, K.Text, TextAlignmentOptions.Left, FontStyles.Bold), 28);
            float ry = Top - 70;
            const float rowH = 52f;
            void Row(string label, bool? ok, string value = null, bool rule = true)
            {
                K.FitOneLine(Txt("RowL", label, L, ry, Inner - 200, rowH, 28, K.Text, TextAlignmentOptions.MidlineLeft), 22);
                if (ok.HasValue)
                {
                    // the condition speaks in signs too: green check / red cross, plus the word
                    K.SignDisc(p, "RowSign", new Vector2(R - 17, ry - rowH / 2f), 34, ok.Value ? K.Ok : K.Bad, check: ok.Value, cross: !ok.Value);
                    value = $"<b>{L10n.T(ok.Value ? "end.yes" : "end.no")}</b>";
                }
                Txt("RowV", value, R - 200, ry, ok.HasValue ? 154 : 200, rowH, 28, ok == false ? K.Bad : ok == true ? K.Ok : K.Text, TextAlignmentOptions.MidlineRight);
                ry -= rowH;
                if (rule) HRule(ry);
            }
            Row(L10n.T("end.rowScore"), null, $"{r.score}/100");
            Row(L10n.T("end.rowMinScore"), r.minScoreOk);
            Row(L10n.T("end.rowMinDetection"), r.minDetectionOk);
            Row(L10n.T("end.rowMinDecisions"), r.minDecisionsOk);
            Row(L10n.T("end.rowCritical"), r.allCriticalHazardsCorrect);
            Row(L10n.T("end.rowNoCritical"), r.noCriticalChoice, rule: false);

            // issues: critical choices, critical hazards not answered correctly, missed, other wrong answers
            var issues = new List<(bool critical, string title, string body)>();
            foreach (var h in recs.Where(x => x.critical))
            {
                var o = h.q.Option(h.selected);
                string choice = o != null ? L10n.LowerFirst(o.text).TrimEnd('.') : h.selected;
                issues.Add((true, $"{h.data.id} — {h.data.title}",
                    L10n.T("end.issueCritical", h.Shown(h.selected), choice, h.CriticalReason)));
            }
            foreach (var h in recs.Where(x => !x.detected))
                issues.Add((h.data.mandatoryCritical, $"{h.data.id} — {h.data.title}", h.data.missedFeedback ?? L10n.T("end.missedDefault")));
            foreach (var h in recs.Where(x => x.detected && !x.critical && x.isCorrect != true))
                issues.Add((h.data.mandatoryCritical, $"{h.data.id} — {h.data.title}",
                    h.selected == null ? L10n.T("end.noAnswer") : L10n.T("end.issueWrong", h.Shown(h.selected), h.Shown(h.q.correct), h.Explanation)));

            // Issue list: each block is measured so short and long texts stack without gaps or overlap;
            // what doesn't fit above the buttons is counted in a "+ N more: see Explanations" line.
            float limit = -H / 2f + 210f;            // top of the button rows plus a margin
            float iy = ry - 20;
            int shown = 0;
            const float titleH = 40f, gap = 16f, moreH = 36f;
            float bodyW = Inner - 56;
            for (int n = 0; n < issues.Count; n++)
            {
                var i = issues[n];
                float bodyH = Mathf.Ceil(Measure(i.body, bodyW, 28)) + 4f;
                bool last = n == issues.Count - 1;
                if (iy - (titleH + bodyH + (last ? 0f : moreH)) < limit) break;
                Txt("IssueBody", i.body, L + 56, iy - titleH, bodyW, bodyH, 28, K.Text, TextAlignmentOptions.TopLeft);
                // red ! = safety-critical, grey i = other
                K.SignDisc(p, "IssueIcon", new Vector2(L + 19, iy - 20), 36, i.critical ? K.Bad : K.TextDim, check: false, info: !i.critical);
                K.FitOneLine(Txt("IssueTitle", i.title, L + 56, iy, bodyW, titleH, 30, K.Text, TextAlignmentOptions.Left, FontStyles.Bold), 24);
                iy -= titleH + bodyH + gap;
                shown++;
            }
            if (shown < issues.Count)
                Txt("More", L10n.T("end.more", issues.Count - shown), L + 56, iy + gap - 4, bodyW, moreH, 28, K.TextDim, TextAlignmentOptions.Left, FontStyles.Italic);
            if (issues.Count == 0)
                Txt("NoIssues", L10n.T("end.noIssues"), L, iy, Inner, 80, 28, K.Ok, TextAlignmentOptions.TopLeft);

            // ---- tab 3: explanations, on the card itself ----
            BuildExplanations(pages[TabExplain].transform, recs);

            // ---- buttons, shared by all tabs: the three tabs, then Play again / Back to Lobby (the booth's next step) ----
            float tabW = (Inner - 24f) / 3f;
            string[] tabKeys = { "end.pageResult", "end.pageDetails", "end.explTitle" };
            tabs = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                tabs[i] = K.Button(t, "Tab" + i, L10n.T(tabKeys[i]), new Vector2(L + tabW / 2f + i * (tabW + 12f), -H / 2f + 160f), new Vector2(tabW, 76), K.Style.Ghost, () => ShowTab(tab), 28);
            }
            float half = Inner / 2f - 10f;
            K.Button(t, "Again", L10n.T("end.again"), new Vector2(-Inner / 4f - 5f, -H / 2f + 76f), new Vector2(half, 76), K.Style.Ghost, PlayAgain, 28);
            K.Button(t, "Lobby", L10n.T("end.lobby"), new Vector2(Inner / 4f + 5f, -H / 2f + 76f), new Vector2(half, 76), K.Style.Primary, Lobby, 28);
            foreach (var b in t.GetComponentsInChildren<Button>(true)) K.FitOneLine(K.ButtonLabel(b), 22);
            if (!string.IsNullOrEmpty(ChemLabRun.LastJsonPath))
                K.FitOneLine(K.Label(t, "Export", L10n.T("end.exported", ChemLabRun.LastJsonPath), new Vector2(0, -H / 2f + 16f), new Vector2(Inner, 24), 15, K.TextDim, TextAlignmentOptions.Center), 10);
            ShowTab(TabResult);

            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        /// <summary>Height a 28-unit body block needs at this width (a throwaway label measures it).</summary>
        float Measure(string text, float width, float size)
        {
            if (measure == null) { measure = K.Label(results.transform, "Measure", "", Vector2.zero, new Vector2(width, 10), size, K.Text); measure.gameObject.SetActive(false); }
            measure.fontSize = size;
            return measure.GetPreferredValues(text, width, 0).y;
        }
        TextMeshProUGUI measure;

        void BackToResult() { if (pages != null) ShowTab(TabResult); }

        /// <summary>One tab at a time; the chosen tab is the filled blue one (the lobby's toggle pattern).</summary>
        void ShowTab(int tab)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                bool on = i == tab;
                pages[i].SetActive(on);
                K.SetButtonColor(tabs[i], on ? K.Accent : K.Bg);
                K.ButtonLabel(tabs[i]).color = on ? Color.white : K.Text;
                tabs[i].transform.Find("Outline").gameObject.SetActive(!on);
            }
            if (tab == TabExplain) ShowExplPage(0);
        }

        static string DecisionText(ChemLabResult r, List<HazardRecord> recs)
        {
            // names keep their own casing ("PPE / Awareness"): lower-casing turns acronyms into "ppe"
            var sb = new StringBuilder();
            var criticalTopics = recs.Where(x => x.critical || (x.data.mandatoryCritical && x.isCorrect != true)).Select(x => x.data.category).Distinct().ToList();
            var missed = recs.Where(x => !x.detected).Select(x => x.data.title).ToList();
            var wrong = recs.Where(x => x.detected && x.isCorrect != true && !x.critical).Select(x => x.data.category).Distinct().ToList();
            switch (r.outcome)
            {
                case Outcome.SafetyReviewRequired:
                    sb.Append(L10n.T("end.decTextReview", r.score, Join(criticalTopics)));
                    break;
                case Outcome.RetrainingRequired:
                    sb.Append(L10n.T("end.decTextRetrain", r.score, Join(wrong.Count > 0 ? wrong : criticalTopics)));
                    break;
                default:
                    sb.Append(L10n.T("end.decTextPass", r.score));
                    break;
            }
            if (missed.Count > 0) sb.Append(" " + L10n.T(missed.Count == 1 ? "end.missedOne" : "end.missedMany", Join(missed)));
            return sb.ToString();
        }

        static string Join(List<string> items)
        {
            if (items.Count == 0) return L10n.T("end.joinNone");
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + L10n.T("end.and") + items[items.Count - 1];
        }

        // ================= explanations (third tab, paged) =================

        readonly List<GameObject> explPages = new List<GameObject>();
        TextMeshProUGUI explPageLabel;
        Button explPrev, explNext;
        int explPage;

        /// <summary>
        /// Two groups, each item led by its sign: "To review" (wrong, dangerous, not found: what you did on the title row,
        /// then the right answer and why) first, then "Correct" as one-line rows. Packed into pages by measured height.
        /// </summary>
        void BuildExplanations(Transform p, List<HazardRecord> recs)
        {
            float bodyTop = Top - 84f, bodyBottom = -H / 2f + 214f;
            K.FitOneLine(K.Label(p, "Title", L10n.T("end.explTitle"), new Vector2(L + 220, Top - 28), new Vector2(440, 56), 40, K.Text, TextAlignmentOptions.Left, FontStyles.Bold), 28);
            // page through at the top right: Previous  1 / 3  Next
            explNext = K.Button(p, "Next", L10n.T("end.next"), new Vector2(R - 80, Top - 28), new Vector2(160, 60), K.Style.Ghost, () => ShowExplPage(explPage + 1), 26);
            explPageLabel = K.Label(p, "PageNo", "", new Vector2(R - 215, Top - 28), new Vector2(100, 60), 26, K.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            explPrev = K.Button(p, "Prev", L10n.T("end.prev"), new Vector2(R - 350, Top - 28), new Vector2(160, 60), K.Style.Ghost, () => ShowExplPage(explPage - 1), 26);
            K.FitOneLine(K.ButtonLabel(explNext), 18); K.FitOneLine(K.ButtonLabel(explPrev), 18);

            GameObject page = null;
            float y = 0f;
            void NewPage()
            {
                page = new GameObject("ExplPage" + explPages.Count, typeof(RectTransform));
                var rt = (RectTransform)page.transform;
                rt.SetParent(p, false);
                rt.sizeDelta = new Vector2(W, H);
                explPages.Add(page);
                y = bodyTop;
            }
            TextMeshProUGUI Txt(string name, string s, float x, float top, float w, float h, float size, Color c, FontStyles st = FontStyles.Normal)
                => K.Label(page.transform, name, s, new Vector2(x + w / 2f, top - h / 2f), new Vector2(w, h), size, c, TextAlignmentOptions.TopLeft, st);

            const float headH = 46f, titleH = 38f, gap = 18f, rowGap = 12f, statusW = 260f;
            float textX = L + 56, textW = Inner - 56;
            void Group(string header, List<HazardRecord> items, bool compact)
            {
                bool first = true;
                foreach (var h in items)
                {
                    var ok = h.q.Option(h.q.correct);
                    // you picked the right one: the row needs no more than its title
                    string line = compact ? null : L10n.T("end.rightAnswer", $"{h.Shown(h.q.correct)}. {ok?.text}");
                    float lineH = compact ? 0f : Mathf.Ceil(Measure(line, textW, 26)) + 2f;
                    float whyH = compact ? 0f : Mathf.Ceil(Measure(h.Explanation, textW, 26)) + 6f;
                    float body = titleH + lineH + whyH + (compact ? rowGap : gap);
                    bool head = first;
                    // a group that fits on one page starts on a fresh page rather than splitting across two
                    bool groupFits = compact && headH + items.Count * body <= bodyTop - bodyBottom;
                    bool split = first && page != null && groupFits && y - (headH + items.Count * body) < bodyBottom;
                    if (page == null || split || y - (body + (head ? headH : 0f)) < bodyBottom) { NewPage(); head = true; }
                    if (head)
                    {
                        Txt("Group", header, L, y, Inner, 36, 26, K.TextDim, FontStyles.Bold);
                        y -= headH;
                    }
                    first = false;

                    // the sign says what happened: green check, red cross, red ! (dangerous), grey i (not found / not answered)
                    var sign = h.selected == h.q.correct ? K.SignDisc(page.transform, "Sign", new Vector2(L + 18, y - 19), 36, K.Ok, check: true)
                             : h.critical ? K.SignDisc(page.transform, "Sign", new Vector2(L + 18, y - 19), 36, K.Bad, check: false)
                             : h.selected != null ? K.SignDisc(page.transform, "Sign", new Vector2(L + 18, y - 19), 36, K.Bad, check: false, cross: true)
                             : K.SignDisc(page.transform, "Sign", new Vector2(L + 18, y - 19), 36, K.TextDim, check: false, info: true);
                    K.FitOneLine(Txt("Title", $"{h.data.id}  {h.data.title}", textX, y, compact ? textW : textW - statusW, titleH, 28, K.Text, FontStyles.Bold), 22);
                    if (!compact)   // what you did, at the end of the title row
                    {
                        var st = Txt("Status", Status(h), R - statusW, y, statusW, titleH, 26, h.selected == null ? K.TextDim : K.Bad, FontStyles.Bold);
                        st.alignment = TextAlignmentOptions.TopRight;
                        K.FitOneLine(st, 20);
                    }
                    y -= titleH;
                    if (!compact)
                    {
                        Txt("Answer", line, textX, y, textW, lineH, 26, K.Accent);
                        y -= lineH;
                        Txt("Why", h.Explanation, textX, y - 4, textW, whyH, 26, K.TextDim);
                        y -= whyH;
                    }
                    float g = compact ? rowGap : gap;
                    var rule = K.Img(page.transform, "Rule", new Vector2(0, y - g / 2f), new Vector2(Inner, 2f), K.Rule);
                    rule.raycastTarget = false;
                    y -= g;
                }
            }
            var review = recs.Where(h => h.selected != h.q.correct).ToList();   // wrong, dangerous, not found, not answered
            var right = recs.Where(h => h.selected == h.q.correct).ToList();
            if (review.Count > 0) Group(L10n.T("end.explReview", review.Count), review, compact: false);
            if (right.Count > 0) Group(L10n.T("end.explCorrect", right.Count), right, compact: true);
            if (page == null) NewPage();
        }

        /// <summary>"You chose C", "Not found" or "Not answered", capitalised for the start of a line.</summary>
        static string Status(HazardRecord h)
        {
            string s = h.selected == null ? L10n.T(h.detected ? "end.notAnswered" : "end.missed") : L10n.T("end.youChose", h.Shown(h.selected));
            return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1);
        }

        void ShowExplPage(int i)
        {
            explPage = Mathf.Clamp(i, 0, explPages.Count - 1);
            for (int n = 0; n < explPages.Count; n++) explPages[n].SetActive(n == explPage);
            explPageLabel.text = $"{explPage + 1} / {Mathf.Max(1, explPages.Count)}";
            explPrev.interactable = explPage > 0;
            explNext.interactable = explPage < explPages.Count - 1;
        }

        // ================= leaderboard =================

        void BuildLeaderboard()
        {
            var r = ChemLabRun.LastResult;
            board = K.Panel(transform, "Panel_Leaderboard", new Vector2(720, 1100));
            var t = board.transform;
            K.Label(t, "Kicker", L10n.T("board.kicker"), new Vector2(0, 495), new Vector2(640, 36), 20, K.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            K.FitOneLine(K.Label(t, "Title", L10n.T("board.title"), new Vector2(0, 440), new Vector2(640, 70), 50, K.Text, TextAlignmentOptions.Left, FontStyles.Bold), 36);
            K.FitOneLine(K.Label(t, "Sub", L10n.T("board.sub"), new Vector2(0, 390), new Vector2(640, 36), 24, K.TextDim, TextAlignmentOptions.Left), 20);

            var rows = new List<(string name, string team, int score, string time, bool you)>();
            foreach (var m in Mock) rows.Add((m.name, m.team, m.score, m.time, false));
            rows.Add((L10n.T("board.you"), "Basel Campus", r.score, K.Mmss(r.duration), true));
            rows = rows.OrderByDescending(x => x.score).ThenBy(x => x.you ? 0 : 1).ToList();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float y = 300 - i * 108;
                var bg = K.Img(t, "Row" + i, new Vector2(0, y), new Vector2(650, 94), row.you ? K.SelectFill : K.Surface, K.Rounded);
                bg.raycastTarget = false;
                Color medal = i == 0 ? new Color(1f, 0.8f, 0.3f) : i == 1 ? new Color(0.8f, 0.84f, 0.9f) : i == 2 ? new Color(0.85f, 0.55f, 0.35f) : K.SurfaceHi;
                var badge = K.Img(bg.transform, "Rank", new Vector2(-275, 0), new Vector2(56, 56), medal, K.Circle);
                badge.raycastTarget = false;
                K.Label(badge.transform, "N", (i + 1).ToString(), Vector2.zero, new Vector2(56, 56), 26, i < 3 ? Color.black : K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
                K.Label(bg.transform, "Name", $"<b>{row.name}</b>\n<size=70%><color={K.Hex(K.TextDim)}>{row.team}</color></size>", new Vector2(-50, 0), new Vector2(340, 84), 28, row.you ? K.Accent : K.Text, TextAlignmentOptions.Left);
                K.Label(bg.transform, "Score", $"<b>{row.score}</b>\n<size=70%><color={K.Hex(K.TextDim)}>{row.time}</color></size>", new Vector2(240, 0), new Vector2(140, 84), 30, K.Text, TextAlignmentOptions.Right);
            }
            if (leaderboardAnchor != null) board.transform.SetPositionAndRotation(leaderboardAnchor.position, leaderboardAnchor.rotation);
        }
    }
}
