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

        void OnEnable() { ChemLabControls.Confirm += PlayAgain; ChemLabControls.Back += HideExplanations; ChemLabControls.BlockInspect = null; }
        void OnDisable() { ChemLabControls.Confirm -= PlayAgain; ChemLabControls.Back -= HideExplanations; }

        void PlayAgain() => Load(ChemLabRun.LabScene);
        void Lobby() => Load(ChemLabRun.LobbyScene);

        static void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning("[ChemLab] Scene " + scene + " not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        // ================= report card =================

        void BuildReportCard()
        {
            var r = ChemLabRun.LastResult;
            var recs = ChemLabRun.LastRecords;
            int total = recs.Count;
            const float W = 1180f, H = 1460f, L = -W / 2f + 50f, R = W / 2f - 50f, inner = W - 100f;

            results = K.Panel(transform, "Panel_Report", new Vector2(W, H), 0.001f, accentLine: false);
            var t = results.transform;
            t.Find("Background").GetComponent<Image>().color = Paper;
            t.Find("Stroke").GetComponent<Image>().color = new Color(0, 0, 0, 0.12f);

            TextMeshProUGUI Txt(string name, string s, float x, float y, float w, float h, float size, Color c, TextAlignmentOptions a, FontStyles st = FontStyles.Normal)
                => K.Label(t, name, s, new Vector2(x + w / 2f, y - h / 2f), new Vector2(w, h), size, c, a, st);
            void HRule(float y) { var i = K.Img(t, "Rule", new Vector2(0, y), new Vector2(inner, 2f), Rule); i.raycastTarget = false; }

            float top = H / 2f - 40f;
            // header
            Txt("Kicker", $"{ChemLabRun.StudentId.ToUpper()} · SESSION {ChemLabRun.Attempt:000}", L, top, 600, 34, 22, InkDim, TextAlignmentOptions.Left);
            Txt("Title", "Assessment Result", L, top - 34, 700, 64, 46, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
            Txt("Score", $"{r.score}/100", R - 420, top - 4, 420, 90, 78, Ink, TextAlignmentOptions.Right, FontStyles.Bold);
            var (chipText, chipCol) = r.outcome == Outcome.SafetyReviewRequired ? ("Safety review required", Red)
                                    : r.outcome == Outcome.RetrainingRequired ? ("Retraining required", Amber) : ("Passed", Green);
            float chipW = 34 + chipText.Length * 15f;
            K.Chip(t, "Outcome", chipText, new Vector2(R - chipW / 2f, top - 122), new Vector2(chipW, 44), new Color(chipCol.r, chipCol.g, chipCol.b, 0.14f), chipCol)
                .fontStyle = FontStyles.Normal;

            // two stat columns
            float sy = top - 190;
            void Stat(float x, string label, string big, string small)
            {
                Txt("StatL", label, x, sy, 500, 40, 28, InkDim, TextAlignmentOptions.Left);
                Txt("StatB", big, x, sy - 44, 500, 58, 44, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
                Txt("StatS", small, x, sy - 104, 500, 40, 28, InkDim, TextAlignmentOptions.Left);
            }
            Stat(L + 20, "Hazard detection", $"{r.detected}/{total}", $"{r.detectionPoints} / {r.maxDetectionPoints} points");
            Stat(L + inner / 2f + 20, "Correct decisions", $"{r.correct}/{r.answered}", $"{r.decisionPoints} / {r.maxDecisionPoints} points");
            HRule(sy - 172);

            // criteria rows
            float ry = sy - 190;
            const float rowH = 62f;
            void Row(string label, string value, Color valueCol, bool rule = true)
            {
                Txt("RowL", label, L, ry, 800, rowH, 30, Ink, TextAlignmentOptions.Left);
                Txt("RowV", value, R - 300, ry, 300, rowH, 30, valueCol, TextAlignmentOptions.Right);
                ry -= rowH;
                if (rule) HRule(ry);
            }
            string Yes(bool ok) => ok ? $"<color={K.Hex(Green)}><b>Yes</b></color>" : $"<color={K.Hex(Red)}><b>No</b></color>";
            Row("Numerical score", $"{r.score}/100", Ink);
            Row("Minimum score achieved", Yes(r.minScoreOk), Ink);
            Row("Minimum detection achieved", Yes(r.minDetectionOk), Ink);
            Row("Minimum decisions achieved", Yes(r.minDecisionsOk), Ink);
            Row("All critical hazards correct", Yes(r.allCriticalHazardsCorrect), Ink);
            Row("No critical unsafe choice", Yes(r.noCriticalChoice), Ink, rule: false);

            // issues: critical choices, critical hazards not answered correctly, missed, other wrong answers
            var issues = new List<(bool critical, string title, string body)>();
            foreach (var h in recs.Where(x => x.critical))
            {
                var o = h.data.Option(h.selected);
                string choice = o != null ? char.ToLower(o.text[0]) + o.text.Substring(1).TrimEnd('.') : h.selected;
                issues.Add((true, $"{h.data.id} — {h.data.title}",
                    $"The student selected {h.selected}: {choice}. This is a critical unsafe choice because {h.data.criticalReason}"));
            }
            foreach (var h in recs.Where(x => !x.detected))
                issues.Add((h.data.mandatoryCritical, $"{h.data.id} — {h.data.title}", h.data.missedFeedback ?? "The student did not detect this hazard."));
            foreach (var h in recs.Where(x => x.detected && !x.critical && x.isCorrect != true))
                issues.Add((h.data.mandatoryCritical, $"{h.data.id} — {h.data.title}",
                    h.selected == null ? "Found, but no answer was submitted." : $"The student selected {h.selected} instead of {h.data.correct}. {h.data.explanation}"));

            float iy = ry - 26;
            int shown = 0;
            foreach (var i in issues)
            {
                if (iy < -H / 2f + 360f) break;
                var badge = K.Img(t, "IssueIcon", new Vector2(L + 20, iy - 22), new Vector2(38, 38), i.critical ? Red : InkDim, i.critical ? K.Rounded : K.Circle);
                badge.raycastTarget = false;
                K.Label(badge.transform, "G", i.critical ? "!" : "?", Vector2.zero, new Vector2(38, 38), 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                Txt("IssueTitle", i.title, L + 56, iy, inner - 56, 46, 32, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
                var body = Txt("IssueBody", i.body, L + 56, iy - 46, inner - 56, 108, 25, Ink, TextAlignmentOptions.TopLeft);
                body.overflowMode = TextOverflowModes.Ellipsis;
                iy -= 162;
                shown++;
            }
            if (shown < issues.Count)
            {
                Txt("More", $"+ {issues.Count - shown} more (see Review explanations)", L + 56, iy, inner - 56, 36, 24, InkDim, TextAlignmentOptions.Left);
                iy -= 40;
            }
            if (issues.Count == 0)
            {
                Txt("NoIssues", "No issues: every hazard was found and handled correctly.", L, iy, inner, 46, 28, Green, TextAlignmentOptions.Left);
                iy -= 60;
            }

            // final decision box
            string decision = r.outcome == Outcome.SafetyReviewRequired ? "Not yet qualified"
                            : r.outcome == Outcome.RetrainingRequired ? "Retraining required" : "Qualified";
            float boxTop = -H / 2f + 330f;
            var box = K.Img(t, "DecisionBox", new Vector2(0, boxTop - 95), new Vector2(inner, 190), new Color(0f, 0f, 0f, 0.035f), K.Rounded);
            box.raycastTarget = false;
            Txt("DecisionTitle", $"Final decision: {decision}", L + 20, boxTop - 14, inner - 40, 48, 34, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
            Txt("DecisionBody", DecisionText(r, recs), L + 20, boxTop - 64, inner - 40, 116, 27, Ink, TextAlignmentOptions.TopLeft);

            // actions
            float by = -H / 2f + 80f;
            var review = K.Button(t, "Review", "Review explanations", new Vector2(-370, by), new Vector2(340, 78), K.Style.Ghost, ToggleExplanations, 26);
            K.Button(t, "Lobby", "Back to Lobby", new Vector2(0, by), new Vector2(340, 78), K.Style.Secondary, Lobby, 26);
            K.Button(t, "Again", "Play again  (A)", new Vector2(370, by), new Vector2(340, 78), K.Style.Primary, PlayAgain, 26);
            if (!string.IsNullOrEmpty(ChemLabRun.LastJsonPath))
                K.Label(t, "Export", "Exported: " + ChemLabRun.LastJsonPath + "  (+ .csv)", new Vector2(0, -H / 2f + 22f), new Vector2(inner, 26), 15, InkDim, TextAlignmentOptions.Center);

            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        static string DecisionText(ChemLabResult r, List<HazardRecord> recs)
        {
            var sb = new StringBuilder();
            var criticalTopics = recs.Where(x => x.critical || (x.data.mandatoryCritical && x.isCorrect != true)).Select(x => x.data.category.ToLower()).Distinct().ToList();
            var missed = recs.Where(x => !x.detected).Select(x => x.data.title.ToLower()).ToList();
            var wrong = recs.Where(x => x.detected && x.isCorrect != true && !x.critical).Select(x => x.data.category.ToLower()).Distinct().ToList();
            switch (r.outcome)
            {
                case Outcome.SafetyReviewRequired:
                    sb.Append($"Despite scoring {r.score}/100, the student must review the {Join(criticalTopics)} procedure and complete another assessment.");
                    break;
                case Outcome.RetrainingRequired:
                    sb.Append($"With {r.score}/100 the minimum requirements were not met. The student should review {Join(wrong.Count > 0 ? wrong : criticalTopics)} and take a fresh assessment.");
                    break;
                default:
                    sb.Append($"The student demonstrated competence with {r.score}/100 and no safety-critical mistakes.");
                    break;
            }
            if (missed.Count > 0) sb.Append($" The missed {Join(missed)} {(missed.Count == 1 ? "hazard" : "hazards")} should also be included in feedback.");
            return sb.ToString();
        }

        static string Join(List<string> items)
        {
            if (items.Count == 0) return "relevant safety";
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }

        // ================= explanations (toggle) =================

        void BuildExplanations()
        {
            var recs = ChemLabRun.LastRecords;
            explain = K.Panel(transform, "Panel_Explanations", new Vector2(1000, 1250));
            var t = explain.transform;
            K.Label(t, "Title", "Explanations", new Vector2(0, 570), new Vector2(920, 60), 42, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            var sb = new StringBuilder();
            foreach (var h in recs)
            {
                var ok = h.data.Option(h.data.correct);
                string mine = h.selected == null ? (h.detected ? "not answered" : "missed") : h.selected == h.data.correct ? "correct" : $"you chose {h.selected}";
                string col = h.selected == h.data.correct ? K.Hex(K.Ok) : h.selected == null ? "#8b97a8" : K.Hex(K.Bad);
                sb.Append($"<b>{h.data.id} {h.data.title}</b>  <color={col}>({mine})</color>\n");
                sb.Append($"<size=85%><color={K.Hex(K.Accent)}>{h.data.correct}. {ok?.text}</color>\n<color={K.Hex(K.TextDim)}>{h.data.explanation}</color></size>\n\n");
            }
            var body = K.Label(t, "Body", sb.ToString(), new Vector2(0, -20), new Vector2(920, 1080), 22, K.Text, TextAlignmentOptions.TopLeft);
            body.overflowMode = TextOverflowModes.Truncate;
            K.Button(t, "Close", "Close  (B)", new Vector2(0, -575), new Vector2(360, 70), K.Style.Ghost, HideExplanations, 26);
            explain.gameObject.SetActive(false);
        }

        void ToggleExplanations()
        {
            if (explain == null) return;
            if (explain.gameObject.activeSelf) { HideExplanations(); return; }
            K.PlaceInFront(explain.transform, 1.25f, 0.05f);
            explain.gameObject.SetActive(true);
        }

        void HideExplanations() { if (explain != null) explain.gameObject.SetActive(false); }

        // ================= no run / leaderboard =================

        void BuildNoRun()
        {
            results = K.Panel(transform, "Panel_NoRun", new Vector2(900, 420));
            K.Label(results.transform, "Title", "No run yet", new Vector2(0, 120), new Vector2(800, 70), 48, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(results.transform, "Body", "Start from the lobby to play the lab and see your results here.", new Vector2(0, 20), new Vector2(800, 90), 28, K.TextDim, TextAlignmentOptions.Center);
            K.Button(results.transform, "Lobby", "Go to Lobby", new Vector2(0, -120), new Vector2(420, 84), K.Style.Primary, Lobby);
            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        void BuildLeaderboard()
        {
            var r = ChemLabRun.LastResult;
            board = K.Panel(transform, "Panel_Leaderboard", new Vector2(720, 1100));
            var t = board.transform;
            K.Label(t, "Kicker", "MOCKUP · STATIC DATA", new Vector2(0, 495), new Vector2(640, 36), 20, K.Warn, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Title", "Basel Leaderboard", new Vector2(0, 440), new Vector2(640, 70), 50, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Sub", "This week · Lab Safety Inspection", new Vector2(0, 390), new Vector2(640, 36), 24, K.TextDim, TextAlignmentOptions.Left);

            var rows = new List<(string name, string team, int score, string time, bool you)>();
            foreach (var m in Mock) rows.Add((m.name, m.team, m.score, m.time, false));
            rows.Add(("You", "Basel Campus", r.score, K.Mmss(r.duration), true));
            rows = rows.OrderByDescending(x => x.score).ThenBy(x => x.you ? 0 : 1).ToList();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float y = 300 - i * 108;
                var bg = K.Img(t, "Row" + i, new Vector2(0, y), new Vector2(650, 94), row.you ? new Color(0f, 0.78f, 0.68f, 0.18f) : K.Surface, K.Rounded);
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
