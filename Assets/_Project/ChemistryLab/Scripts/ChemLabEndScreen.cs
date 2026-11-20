using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using K = ChemLab.ChemLabUIKit;

namespace ChemLab
{
    /// <summary>
    /// Scene 3: results + static "Basel Leaderboard" mockup. Reads the run that the lab stored in ChemLabRun
    /// (the JSON/CSV export was already written when the run ended).
    /// </summary>
    public class ChemLabEndScreen : MonoBehaviour
    {
        public Transform resultsAnchor;
        public Transform leaderboardAnchor;

        Canvas results, board;
        TextMeshProUGUI right;
        bool showExplanations;

        // static mockup rows (name, site, score, time)
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
            else { BuildResults(); BuildLeaderboard(); }
        }

        void OnEnable() { ChemLabControls.Confirm += PlayAgain; ChemLabControls.BlockInspect = null; }
        void OnDisable() => ChemLabControls.Confirm -= PlayAgain;

        void PlayAgain() => Load(ChemLabRun.LabScene);
        void Lobby() => Load(ChemLabRun.LobbyScene);

        static void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning("[ChemLab] Scene " + scene + " not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        void BuildNoRun()
        {
            results = K.Panel(transform, "Panel_NoRun", new Vector2(900, 420));
            K.Label(results.transform, "Title", "No run yet", new Vector2(0, 120), new Vector2(800, 70), 48, K.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            K.Label(results.transform, "Body", "Start from the lobby to play the lab and see your results here.", new Vector2(0, 20), new Vector2(800, 90), 28, K.TextDim, TextAlignmentOptions.Center);
            K.Button(results.transform, "Lobby", "Go to Lobby", new Vector2(0, -120), new Vector2(420, 84), K.Style.Primary, Lobby);
            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        void BuildResults()
        {
            var r = ChemLabRun.LastResult;
            var recs = ChemLabRun.LastRecords;
            var sc = ChemLabRun.LastConfig.scoring;
            int found = recs.Count(x => x.detected), total = recs.Count;
            Color outCol = ChemLabSession.OutcomeColor(r.outcome);

            results = K.Panel(transform, "Panel_Results", new Vector2(1500, 1100));
            var t = results.transform;
            K.Label(t, "Kicker", $"{ChemLabRun.StudentId.ToUpper()}  ·  ATTEMPT {ChemLabRun.Attempt}  ·  {(ChemLabRun.LastTimedOut ? "TIME UP" : "SUBMITTED")}",
                new Vector2(-330, 495), new Vector2(760, 40), 22, K.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Title", "Assessment Result", new Vector2(-330, 440), new Vector2(760, 70), 52, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            K.Label(t, "Score", $"{r.score}<size=45%><color={K.Hex(K.TextDim)}>/100</color></size>", new Vector2(520, 445), new Vector2(380, 130), 120, K.Text, TextAlignmentOptions.Right, FontStyles.Bold);
            K.Chip(t, "Outcome", ChemLabSession.OutcomeLabel(r.outcome), new Vector2(-215, 372), new Vector2(530, 46), new Color(outCol.r, outCol.g, outCol.b, 0.18f), outCol);

            // stat cards
            void Card(int i, string title, string big, string small)
            {
                var c = K.Img(t, "Card" + i, new Vector2(-555 + i * 370, 250), new Vector2(350, 150), K.Surface, K.Rounded);
                c.raycastTarget = false;
                K.Label(c.transform, "T", title, new Vector2(0, 45), new Vector2(310, 36), 22, K.TextDim, TextAlignmentOptions.Left);
                K.Label(c.transform, "B", big, new Vector2(0, -5), new Vector2(310, 60), 46, K.Text, TextAlignmentOptions.Left, FontStyles.Bold);
                K.Label(c.transform, "S", small, new Vector2(0, -50), new Vector2(310, 34), 21, K.TextDim, TextAlignmentOptions.Left);
            }
            Card(0, "Completion time", K.Mmss(r.duration), $"limit {K.Mmss(ChemLabRun.LastConfig.timeLimitSeconds)}");
            Card(1, "Hazards found", $"{found}/{total}", $"{total - found} missed  ·  {r.detectionPoints}/{r.maxDetectionPoints} pts");
            Card(2, "Correct decisions", $"{r.correct}/{r.answered}", $"first attempt  ·  {r.decisionPoints}/{r.maxDecisionPoints} pts");
            Card(3, "Wrong answers", r.wrongAnswers.ToString(), "logged as mistakes");

            // checklist
            string Mark(bool ok) => ok ? $"<color={K.Hex(K.Ok)}>Yes</color>" : $"<color={K.Hex(K.Bad)}>No</color>";
            var sb = new StringBuilder();
            sb.Append($"<size=26><b>Pass criteria</b></size>\n");
            sb.Append($"Minimum score ({sc.passScore})<pos=82%>{Mark(r.minScoreOk)}\n");
            sb.Append($"Minimum detection ({sc.minDetected}/10)<pos=82%>{Mark(r.minDetectionOk)}\n");
            sb.Append($"Minimum decisions ({sc.minCorrect}/10)<pos=82%>{Mark(r.minDecisionsOk)}\n");
            sb.Append($"All critical hazards correct<pos=82%>{Mark(r.allCriticalHazardsCorrect)}\n");
            sb.Append($"No critical unsafe choice<pos=82%>{Mark(r.noCriticalChoice)}\n\n");
            sb.Append($"<b>Final decision</b>\n<color={K.Hex(outCol)}>{ChemLabSession.OutcomeLabel(r.outcome)}</color>\n<size=21><color={K.Hex(K.TextDim)}>{r.recommendation}</color></size>");
            K.Label(t, "Checklist", sb.ToString(), new Vector2(-445, -110), new Vector2(560, 520), 23, K.Text, TextAlignmentOptions.TopLeft);

            right = K.Label(t, "Right", "", new Vector2(305, -110), new Vector2(840, 520), 22, K.Text, TextAlignmentOptions.TopLeft);
            right.overflowMode = TextOverflowModes.Truncate;
            FillRight();

            var review = K.Button(t, "Review", "Review explanations", new Vector2(-470, -470), new Vector2(440, 80), K.Style.Ghost, null);
            review.onClick.AddListener(() => { showExplanations = !showExplanations; FillRight(); K.ButtonLabel(review).text = showExplanations ? "Show results" : "Review explanations"; });
            K.Button(t, "Lobby", "Back to Lobby", new Vector2(0, -470), new Vector2(440, 80), K.Style.Secondary, Lobby);
            K.Button(t, "Again", "Play again  (A)", new Vector2(470, -470), new Vector2(440, 80), K.Style.Primary, PlayAgain);
            if (!string.IsNullOrEmpty(ChemLabRun.LastJsonPath))
                K.Label(t, "Export", "Exported: " + ChemLabRun.LastJsonPath + "  (+ .csv)", new Vector2(0, -530), new Vector2(1400, 30), 16, new Color(0.45f, 0.52f, 0.6f), TextAlignmentOptions.Center);

            if (resultsAnchor != null) results.transform.SetPositionAndRotation(resultsAnchor.position, resultsAnchor.rotation);
        }

        void FillRight()
        {
            var recs = ChemLabRun.LastRecords;
            var sc = ChemLabRun.LastConfig.scoring;
            var t = new StringBuilder();
            if (!showExplanations)
            {
                t.Append("<size=26><b>Hazard-by-hazard</b></size>\n");
                foreach (var h in recs)
                {
                    string status, col;
                    switch (h.Status)
                    {
                        case HazardStatus.Correct: status = "Correct"; col = K.Hex(K.Ok); break;
                        case HazardStatus.Incorrect: status = h.solved ? "Fixed (2nd try)" : "Incorrect"; col = K.Hex(K.Bad); break;
                        case HazardStatus.DetectedUnanswered: status = "Not answered"; col = K.Hex(K.Warn); break;
                        default: status = "Missed"; col = "#8b97a8"; break;
                    }
                    string star = h.data.mandatoryCritical ? $"<color={K.Hex(K.Warn)}>*</color>" : "";
                    t.Append($"<b>{h.data.id}</b>{star} {h.data.title}<pos=60%><color={col}>{status}</color><pos=88%>{h.Points(sc)}/{h.MaxPoints(sc)}");
                    if (h.critical) t.Append($" <color={K.Hex(K.Bad)}>[!]</color>");
                    t.Append("\n");
                }
                t.Append($"<size=16><color={K.Hex(K.TextDim)}>* safety-critical hazard   [!] critical unsafe choice</color></size>\n");
                var issues = new StringBuilder();
                foreach (var h in recs.Where(x => x.critical))
                {
                    var o = h.data.Option(h.selected);
                    issues.Append($"<color={K.Hex(K.Bad)}><b>[!] {h.data.id} {h.data.title}</b></color>  <size=18>Chose {h.selected}: {o?.text} Critical because {h.data.criticalReason}</size>\n");
                }
                foreach (var h in recs.Where(x => !x.detected))
                    issues.Append($"<color=#8b97a8><b>{h.data.id} {h.data.title}</b></color>  <size=18>Not detected.</size>\n");
                if (issues.Length > 0) t.Append("\n<size=24><b>Issues</b></size>\n").Append(issues);
            }
            else
            {
                t.Append("<size=26><b>Explanations</b></size>\n");
                foreach (var h in recs)
                {
                    var ok = h.data.Option(h.data.correct);
                    t.Append($"<b>{h.data.id} {h.data.title}</b>  <size=17><color={K.Hex(K.Ok)}>{h.data.correct}. {ok?.text}</color> <color={K.Hex(K.TextDim)}>{h.data.explanation}</color></size>\n");
                }
            }
            right.text = t.ToString();
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
