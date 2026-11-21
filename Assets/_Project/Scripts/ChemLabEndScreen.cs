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
    /// Scene 3: the assessment report card (light "paper" style), a static "Basel Leaderboard" mockup and
    /// an optional explanations panel. Reads the run stored in ChemLabRun (JSON/CSV export already written).
    /// </summary>
    public class ChemLabEndScreen : MonoBehaviour
    {
        public Transform resultsAnchor;
        public Transform leaderboardAnchor;

        Canvas results, board, explain;

        // report card palette (light)
        static readonly Color Paper = new Color(0.985f, 0.985f, 0.99f, 1f);
        static readonly Color Ink = new Color(0.07f, 0.08f, 0.1f);
        static readonly Color InkDim = new Color(0.38f, 0.41f, 0.45f);
        static readonly Color Rule = new Color(0f, 0f, 0f, 0.09f);
        static readonly Color Green = new Color(0.12f, 0.55f, 0.27f);
        static readonly Color Red = new Color(0.86f, 0.22f, 0.22f);
        static readonly Color Amber = new Color(0.8f, 0.5f, 0.05f);

        static readonly (string name, string team, int score, string time)[] Mock =
        {
            ("L. Meier", "Basel Campus", 97, "04:41"),
            ("S. Rossi", "Basel Campus", 94, "05:02"),
            ("A. Dubois", "St. Johann", 90, "05:37"),
            ("J. Weber", "Basel Campus", 86, "06:10"),
            ("M. Keller", "Schweizerhalle", 79, "06:48"),
            ("N. Brunner", "St. Johann", 72, "07:00"),
        };

        void Awake()
        {
            if (ChemLabRun.LastResult == null) BuildNoRun();
            else { BuildReportCard(); BuildLeaderboard(); BuildExplanations(); }
        }

        // No A binding here: A submits answers in the lab, so a reflex press would throw away the report. Ray-click the button.
        void OnEnable() { ChemLabControls.Back += HideExplanations; ChemLabControls.BlockInspect = null; }
        void OnDisable() { ChemLabControls.Back -= HideExplanations; }

        void PlayAgain() => Load(ChemLabRun.LabScene);
        void Lobby() => Load(ChemLabRun.LobbyScene);

        static void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning("[ChemLab] Scene " + scene + " not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        // ================= report card =================

        // Report card and explanations at 0.92 deg x-height for 28-unit body text (scale / distance = 0.00107);
        // at that size one view holds ~900 units of height (about 50 deg), so both are paged.
        const float CardScale = 0.00118f;   // at ResultsAnchor, 1.10 m
        const float ExplainScale = 0.00107f, ExplainDist = 1.0f;   // in front of the card
        GameObject resultPage, detailPage;
        Button pageBtn;

        void BuildReportCard()
        {
            var r = ChemLabRun.LastResult;
            var recs = ChemLabRun.LastRecords;
            int total = recs.Count;
            const float W = 1000f, H = 900f, L = -W / 2f + 50f, R = W / 2f - 50f, inner = W - 100f;

            results = K.Panel(transform, "Panel_Report", new Vector2(W, H), CardScale, accentLine: false);
            var t = results.transform;
            t.Find("Background").GetComponent<Image>().color = Paper;
            t.Find("Stroke").GetComponent<Image>().color = new Color(0, 0, 0, 0.12f);
            GameObject Page(string name)
            {
                var g = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)g.transform;
                rt.SetParent(t, false);
                rt.sizeDelta = new Vector2(W, H);
                return g;
            }
            resultPage = Page("Page_Result");
            detailPage = Page("Page_Details");

            Transform p = resultPage.transform;   // where Txt / HRule draw
            TextMeshProUGUI Txt(string name, string s, float x, float y, float w, float h, float size, Color c, TextAlignmentOptions a, FontStyles st = FontStyles.Normal)
                => K.Label(p, name, s, new Vector2(x + w / 2f, y - h / 2f), new Vector2(w, h), size, c, a, st);
            void HRule(float y) { var i = K.Img(p, "Rule", new Vector2(0, y), new Vector2(inner, 2f), Rule); i.raycastTarget = false; }

            // ---- page 1: result ----
            float top = H / 2f - 36f;
            Txt("Kicker", L10n.T("end.kicker", ChemLabRun.StudentId.ToUpper(), ChemLabRun.Attempt.ToString("000")), L, top, 560, 34, 24, InkDim, TextAlignmentOptions.Left);
            K.FitOneLine(Txt("Title", L10n.T("end.title"), L, top - 34, 560, 64, 46, Ink, TextAlignmentOptions.Left, FontStyles.Bold), 32);
            Txt("Score", $"{r.score}/100", R - 340, top - 4, 340, 90, 78, Ink, TextAlignmentOptions.Right, FontStyles.Bold);
            // chipInk: darker text on the tinted chip, >= 4.5:1 (WCAG AA); the tint keeps the brighter hue
            var (chipText, chipCol, chipInk) = r.outcome == Outcome.SafetyReviewRequired ? (L10n.T("end.chipReview"), Red, K.Bad)
                                    : r.outcome == Outcome.RetrainingRequired ? (L10n.T("end.chipRetrain"), Amber, new Color(0.54f, 0.325f, 0f))
                                    : (L10n.T("end.chipPassed"), Green, new Color(0.106f, 0.42f, 0.267f));
            float chipW = Mathf.Min(inner, 34 + chipText.Length * 15f);
            K.Chip(p, "Outcome", chipText, new Vector2(R - chipW / 2f, top - 122), new Vector2(chipW, 44), new Color(chipCol.r, chipCol.g, chipCol.b, 0.14f), chipInk)
                .fontStyle = FontStyles.Normal;

            // two stat columns
            float sy = top - 170;
            void Stat(float x, string label, string big, string small)
            {
                Txt("StatL", label, x, sy, inner / 2f - 30, 40, 28, InkDim, TextAlignmentOptions.Left);
                Txt("StatB", big, x, sy - 40, inner / 2f - 30, 58, 44, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
                K.FitOneLine(Txt("StatS", small, x, sy - 98, inner / 2f - 30, 40, 28, InkDim, TextAlignmentOptions.Left), 22);
            }
            Stat(L + 10, L10n.T("end.detection"), $"{r.detected}/{total}", r.falsePenalty > 0
                ? L10n.T("end.pointsPenalty", r.detectionPoints, r.maxDetectionPoints, r.falsePenalty)
                : L10n.T("end.points", r.detectionPoints, r.maxDetectionPoints));
            Stat(L + inner / 2f + 10, L10n.T("end.decisions"), $"{r.correct}/{r.answered}", L10n.T("end.points", r.decisionPoints, r.maxDecisionPoints));
            HRule(sy - 150);

            // final decision box
            string decision = r.outcome == Outcome.SafetyReviewRequired ? L10n.T("end.decNotYet")
                            : r.outcome == Outcome.RetrainingRequired ? L10n.T("end.chipRetrain") : L10n.T("end.decQualified");
            float boxTop = sy - 172;
            const float boxH = 250f;
            var box = K.Img(p, "DecisionBox", new Vector2(0, boxTop - boxH / 2f), new Vector2(inner, boxH), new Color(0f, 0f, 0f, 0.035f), K.Rounded);
            box.raycastTarget = false;
            K.FitOneLine(Txt("DecisionTitle", L10n.T("end.decision", decision), L + 20, boxTop - 14, inner - 40, 48, 34, Ink, TextAlignmentOptions.Left, FontStyles.Bold), 26);
            var decisionBody = Txt("DecisionBody", DecisionText(r, recs), L + 20, boxTop - 64, inner - 40, boxH - 74, 28, Ink, TextAlignmentOptions.TopLeft);
            decisionBody.enableAutoSizing = true; decisionBody.fontSizeMin = 24; decisionBody.fontSizeMax = 28;   // longer languages need a fifth line

            // ---- page 2: pass conditions and issues ----
            p = detailPage.transform;
            K.FitOneLine(Txt("DetailsTitle", L10n.T("end.detailsTitle"), L, top, inner, 56, 40, Ink, TextAlignmentOptions.Left, FontStyles.Bold), 28);
            float ry = top - 70;
            const float rowH = 52f;
            void Row(string label, string value, Color valueCol, bool rule = true)
            {
                K.FitOneLine(Txt("RowL", label, L, ry, inner - 180, rowH, 28, Ink, TextAlignmentOptions.MidlineLeft), 22);
                Txt("RowV", value, R - 170, ry, 170, rowH, 28, valueCol, TextAlignmentOptions.MidlineRight);
                ry -= rowH;
                if (rule) HRule(ry);
            }
            string Yes(bool ok) => ok ? $"<color={K.Hex(Green)}><b>{L10n.T("end.yes")}</b></color>" : $"<color={K.Hex(Red)}><b>{L10n.T("end.no")}</b></color>";
            Row(L10n.T("end.rowScore"), $"{r.score}/100", Ink);
            Row(L10n.T("end.rowMinScore"), Yes(r.minScoreOk), Ink);
            Row(L10n.T("end.rowMinDetection"), Yes(r.minDetectionOk), Ink);
            Row(L10n.T("end.rowMinDecisions"), Yes(r.minDecisionsOk), Ink);
            Row(L10n.T("end.rowCritical"), Yes(r.allCriticalHazardsCorrect), Ink);
            Row(L10n.T("end.rowNoCritical"), Yes(r.noCriticalChoice), Ink, rule: false);

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
            // what doesn't fit above the buttons is counted in a "+ N more: see explanations" line.
            float limit = -H / 2f + 210f;            // top of the button rows plus a margin
            float iy = ry - 20;
            int shown = 0;
            const float titleH = 40f, gap = 16f, moreH = 36f;
            float bodyW = inner - 56;
            for (int n = 0; n < issues.Count; n++)
            {
                var i = issues[n];
                var body = Txt("IssueBody", i.body, L + 56, iy - titleH, bodyW, 10, 28, Ink, TextAlignmentOptions.TopLeft);
                float bodyH = Mathf.Ceil(body.GetPreferredValues(i.body, bodyW, 0).y) + 4f;
                bool last = n == issues.Count - 1;
                float need = titleH + bodyH + (last ? 0f : moreH);
                if (iy - need < limit) { Destroy(body.gameObject); break; }
                var rt = body.rectTransform;
                rt.sizeDelta = new Vector2(bodyW, bodyH);
                rt.anchoredPosition = new Vector2(L + 56 + bodyW / 2f, iy - titleH - bodyH / 2f);
                var badge = K.Img(p, "IssueIcon", new Vector2(L + 19, iy - 20), new Vector2(36, 36), i.critical ? Red : InkDim, i.critical ? K.Rounded : K.Circle);
                badge.raycastTarget = false;
                K.Label(badge.transform, "G", i.critical ? "!" : "?", Vector2.zero, new Vector2(36, 36), 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                K.FitOneLine(Txt("IssueTitle", i.title, L + 56, iy, bodyW, titleH, 30, Ink, TextAlignmentOptions.Left, FontStyles.Bold), 24);
                iy -= titleH + bodyH + gap;
                shown++;
            }
            if (shown < issues.Count)
                Txt("More", L10n.T("end.more", issues.Count - shown), L + 56, iy + gap - 4, bodyW, moreH, 28, InkDim, TextAlignmentOptions.Left, FontStyles.Italic);
            if (issues.Count == 0)
                Txt("NoIssues", L10n.T("end.noIssues"), L, iy, inner, 80, 28, Green, TextAlignmentOptions.TopLeft);

            // ---- buttons, shared by both pages: page switch + explanations, then lobby + play again ----
            float half = inner / 2f - 10f;
            pageBtn = K.Button(t, "Page", L10n.T("end.pageDetails"), new Vector2(-inner / 4f - 5f, -H / 2f + 160f), new Vector2(half, 76), K.Style.Ghost, TogglePage, 28);
            K.Button(t, "Review", L10n.T("end.review"), new Vector2(inner / 4f + 5f, -H / 2f + 160f), new Vector2(half, 76), K.Style.Ghost, ToggleExplanations, 28);
            K.Button(t, "Lobby", L10n.T("end.lobby"), new Vector2(-inner / 4f - 5f, -H / 2f + 76f), new Vector2(half, 76), K.Style.Secondary, Lobby, 28);
            K.Button(t, "Again", L10n.T("end.again"), new Vector2(inner / 4f + 5f, -H / 2f + 76f), new Vector2(half, 76), K.Style.Primary, PlayAgain, 28);
            foreach (var b in t.GetComponentsInChildren<Button>()) K.FitOneLine(K.ButtonLabel(b), 22);
            if (!string.IsNullOrEmpty(ChemLabRun.LastJsonPath))
                K.FitOneLine(K.Label(t, "Export", L10n.T("end.exported", ChemLabRun.LastJsonPath), new Vector2(0, -H / 2f + 16f), new Vector2(inner, 24), 15, InkDim, TextAlignmentOptions.Center), 10);
            ShowPage(details: false);

            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        void TogglePage() => ShowPage(!detailPage.activeSelf);

        void ShowPage(bool details)
        {
            resultPage.SetActive(!details);
            detailPage.SetActive(details);
            K.ButtonLabel(pageBtn).text = L10n.T(details ? "end.pageResult" : "end.pageDetails");
        }

        static string DecisionText(ChemLabResult r, List<HazardRecord> recs)
        {
            var sb = new StringBuilder();
            var criticalTopics = recs.Where(x => x.critical || (x.data.mandatoryCritical && x.isCorrect != true)).Select(x => L10n.Lower(x.data.category)).Distinct().ToList();
            var missed = recs.Where(x => !x.detected).Select(x => L10n.Lower(x.data.title)).ToList();
            var wrong = recs.Where(x => x.detected && x.isCorrect != true && !x.critical).Select(x => L10n.Lower(x.data.category)).Distinct().ToList();
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

        // ================= explanations (toggle, paged) =================

        readonly List<string> explPages = new List<string>();
        TextMeshProUGUI explBody, explPageLabel;
        Button explPrev, explNext;
        int explPage;

        /// <summary>One block per hazard (your choice, the right answer, why), packed into pages by measured height.</summary>
        void BuildExplanations()
        {
            var recs = ChemLabRun.LastRecords;
            const float W = 1000f, H = 900f, bodyTop = 350f, bodyBottom = -330f;
            explain = K.Panel(transform, "Panel_Explanations", new Vector2(W, H), ExplainScale);
            var t = explain.transform;
            K.Label(t, "Title", L10n.T("end.explTitle"), new Vector2(0, 395), new Vector2(920, 60), 42, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            explBody = K.Label(t, "Body", "", new Vector2(0, (bodyTop + bodyBottom) / 2f), new Vector2(920, bodyTop - bodyBottom), 28, K.Text, TextAlignmentOptions.TopLeft);
            explBody.overflowMode = TextOverflowModes.Truncate;

            float room = bodyTop - bodyBottom;
            var page = new StringBuilder();
            foreach (var h in recs)
            {
                var ok = h.q.Option(h.q.correct);
                string mine = h.selected == null ? L10n.T(h.detected ? "end.notAnswered" : "end.missed") : h.selected == h.q.correct ? L10n.T("end.correct") : L10n.T("end.youChose", h.Shown(h.selected));
                string col = h.selected == h.q.correct ? K.Hex(K.Ok) : h.selected == null ? K.Hex(K.TextDim) : K.Hex(K.Bad);
                string block = $"<b>{h.data.id} {h.data.title}</b>  <color={col}>({mine})</color>\n" +
                               $"<color={K.Hex(K.Accent)}>{h.Shown(h.q.correct)}. {ok?.text}</color>\n<color={K.Hex(K.TextDim)}>{h.Explanation}</color>\n\n";
                if (page.Length > 0 && explBody.GetPreferredValues((page + block).TrimEnd('\n'), 920, 0).y > room)
                {
                    explPages.Add(page.ToString().TrimEnd('\n'));
                    page.Clear();
                }
                page.Append(block);
            }
            if (page.Length > 0) explPages.Add(page.ToString().TrimEnd('\n'));

            float by = -H / 2f + 55f;
            explPrev = K.Button(t, "Prev", L10n.T("end.prev"), new Vector2(-335, by), new Vector2(230, 72), K.Style.Ghost, () => ShowExplPage(explPage - 1), 28);
            explPageLabel = K.Label(t, "PageNo", "", new Vector2(-150, by), new Vector2(130, 72), 28, K.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            explNext = K.Button(t, "Next", L10n.T("end.next"), new Vector2(35, by), new Vector2(230, 72), K.Style.Ghost, () => ShowExplPage(explPage + 1), 28);
            K.Button(t, "Close", L10n.T("end.closeB"), new Vector2(320, by), new Vector2(280, 72), K.Style.Ghost, HideExplanations, 28);
            foreach (var b in t.GetComponentsInChildren<Button>()) K.FitOneLine(K.ButtonLabel(b), 22);
            ShowExplPage(0);
            explain.gameObject.SetActive(false);
        }

        void ShowExplPage(int i)
        {
            explPage = Mathf.Clamp(i, 0, explPages.Count - 1);
            explBody.text = explPages.Count > 0 ? explPages[explPage] : "";
            explPageLabel.text = $"{explPage + 1} / {Mathf.Max(1, explPages.Count)}";
            explPrev.interactable = explPage > 0;
            explNext.interactable = explPage < explPages.Count - 1;
        }

        void ToggleExplanations()
        {
            if (explain == null) return;
            if (explain.gameObject.activeSelf) { HideExplanations(); return; }
            ShowExplPage(0);
            K.PlaceInFront(explain.transform, ExplainDist, 0.05f);
            explain.gameObject.SetActive(true);
        }

        void HideExplanations() { if (explain != null) explain.gameObject.SetActive(false); }

        // ================= no run / leaderboard =================

        void BuildNoRun()
        {
            results = K.Panel(transform, "Panel_NoRun", new Vector2(900, 420));
            K.Label(results.transform, "Title", L10n.T("end.noRunTitle"), new Vector2(0, 120), new Vector2(800, 70), 48, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(results.transform, "Body", L10n.T("end.noRunBody"), new Vector2(0, 20), new Vector2(800, 90), 28, K.TextDim, TextAlignmentOptions.Center);
            K.Button(results.transform, "Lobby", L10n.T("end.goLobby"), new Vector2(0, -120), new Vector2(420, 84), K.Style.Primary, Lobby);
            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

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
