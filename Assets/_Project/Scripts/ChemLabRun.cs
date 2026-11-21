using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ChemLab
{
    /// <summary>Data that travels between the three scenes (Lobby -> Lab -> End) and the run export.</summary>
    public static class ChemLabRun
    {
        public const string LobbyScene = "ChemLab_Lobby";
        public const string LabScene = "ChemistryLab";
        public const string EndScene = "ChemLab_End";

        public static string Language => L10n.Code;   // the selected locale is the single source
        public static string StudentId = "student_001";
        public static int Attempt = 0;

        // filled by the lab when the run ends, read by the end screen
        public static ChemLabResult LastResult;
        public static List<HazardRecord> LastRecords;
        public static LabConfig LastConfig;
        public static bool LastTimedOut;
        public static string LastJsonPath, LastCsvPath, LastSessionId;

        // ---------- hazard selection (lobby "Choose hazards"), saved on the headset ----------

        const string DisabledKey = "chemlab.disabledHazards", PerRunKey = "chemlab.hazardsPerRun";
        /// <summary>Hazard ids switched off in the lobby.</summary>
        public static readonly HashSet<string> DisabledHazards =
            new HashSet<string>(PlayerPrefs.GetString(DisabledKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        /// <summary>Most hazards planted per run (capped by how many are switched on).</summary>
        public static int HazardsPerRun = PlayerPrefs.GetInt(PerRunKey, 10);

        public static void SaveHazardSettings()
        {
            PlayerPrefs.SetString(DisabledKey, string.Join(",", DisabledHazards));
            PlayerPrefs.SetInt(PerRunKey, HazardsPerRun);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The hazards for one run: HazardsPerRun of the switched-on ones, in id order. Safety-critical hazards are
        /// picked first (so a run can't pass without meeting them), the remaining slots at random.
        /// </summary>
        public static List<HazardData> PickHazards(HazardData[] all)
        {
            var usable = all.Where(h => h.questions != null && h.questions.Length > 0).ToList();   // LabConfig.Load logs the others
            var on = usable.Where(h => !DisabledHazards.Contains(h.id)).ToList();
            if (on.Count == 0) on = usable;   // never an empty lab, even with stale settings
            return on.OrderBy(h => h.mandatoryCritical ? 0 : 1).ThenBy(_ => UnityEngine.Random.value)
                .Take(Mathf.Clamp(HazardsPerRun, 1, on.Count)).OrderBy(h => h.id).ToList();
        }

        // ---------- question pool ----------

        static readonly Dictionary<string, int> lastQuestion = new Dictionary<string, int>();

        /// <summary>Random question from the hazard's pool, never the one it showed last run (while the app stays open).</summary>
        public static QuestionData PickQuestion(HazardData h)
        {
            int n = h.questions.Length;
            int i = n > 1 && lastQuestion.TryGetValue(h.id, out var last)
                ? (last + UnityEngine.Random.Range(1, n)) % n   // any of the other n-1, evenly
                : UnityEngine.Random.Range(0, n);
            lastQuestion[h.id] = i;
            return h.questions[i];
        }

        /// <summary>Writes session_xxx.json (full detail) and session_xxx.csv (one row per hazard + summary row).</summary>
        public static void Export(string sessionId, bool practice, ChemLabResult r, IReadOnlyList<HazardRecord> records)
        {
            string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
            string S(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string B(bool? v) => v == null ? "null" : v.Value ? "true" : "false";
            string Ids(IEnumerable<HazardRecord> rs) => "[" + string.Join(", ", rs.Select(x => S(x.data.id))) + "]";

            LastSessionId = sessionId;
            var found = records.Where(x => x.detected).ToList();
            var missed = records.Where(x => !x.detected).ToList();
            int wrong = records.Sum(x => x.wrongAnswers);

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"sessionId\": {S(sessionId)},\n");
            sb.Append($"  \"studentId\": {S(StudentId)},\n");
            sb.Append($"  \"attempt\": {Attempt},\n");
            sb.Append($"  \"language\": {S(Language)},\n");
            sb.Append($"  \"mode\": {S(practice ? "practice" : "assessment")},\n");
            sb.Append($"  \"completion_time\": {F(r.duration)},\n");
            sb.Append($"  \"timed_out\": {B(LastTimedOut)},\n");
            sb.Append($"  \"hazardsInRun\": {records.Count},\n");
            sb.Append($"  \"hazards_found\": {Ids(found)},\n");
            sb.Append($"  \"hazards_missed\": {Ids(missed)},\n");
            sb.Append($"  \"wrong_answers\": {wrong},\n");
            sb.Append($"  \"score\": {r.score},\n");
            sb.Append($"  \"detectionPoints\": {r.detectionPoints},\n");
            sb.Append($"  \"decisionPoints\": {r.decisionPoints},\n");
            sb.Append($"  \"falsePositiveClicks\": {r.falseClicks},\n");
            sb.Append($"  \"outcome\": {S(r.outcome.ToString())},\n");
            sb.Append($"  \"minimumScoreAchieved\": {B(r.minScoreOk)},\n");
            sb.Append($"  \"minimumDetectionAchieved\": {B(r.minDetectionOk)},\n");
            sb.Append($"  \"minimumDecisionsAchieved\": {B(r.minDecisionsOk)},\n");
            sb.Append($"  \"allCriticalHazardsCorrect\": {B(r.allCriticalHazardsCorrect)},\n");
            sb.Append($"  \"noCriticalUnsafeChoice\": {B(r.noCriticalChoice)},\n");
            sb.Append($"  \"safetyReviewRequired\": {B(r.outcome == Outcome.SafetyReviewRequired)},\n");
            sb.Append("  \"hazards\": [\n");
            for (int i = 0; i < records.Count; i++)
            {
                var h = records[i];
                sb.Append("    {");
                sb.Append($"\"id\": {S(h.data.id)}, \"questionId\": {S(h.q.id)}, \"category\": {S(h.data.exportCategory)}, \"status\": {S(h.Status.ToString())}, ");
                sb.Append($"\"mandatoryCritical\": {B(h.data.mandatoryCritical)}, \"detected\": {B(h.detected)}, ");
                sb.Append($"\"detectedAt\": {(h.detected ? F(h.detectedAt) : "null")}, ");
                sb.Append($"\"selectedOption\": {S(h.selected)}, \"correctOption\": {S(h.q.correct)}, \"isCorrect\": {B(h.isCorrect)}, ");
                sb.Append($"\"responseTimeSeconds\": {(h.responseTime >= 0 ? F(h.responseTime) : "null")}, ");
                sb.Append($"\"wrongAnswers\": {h.wrongAnswers}, \"resolved\": {B(h.solved)}, \"criticalError\": {B(h.critical)}}}");
                sb.Append(i < records.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            var csv = new StringBuilder();
            csv.AppendLine("session_id,student_id,hazard_id,question_id,category,detected,detected_at,first_answer,correct_answer,first_attempt_correct,wrong_answers,resolved,critical_error,response_time");
            foreach (var h in records)
                csv.AppendLine(string.Join(",", sessionId, StudentId, h.data.id, h.q.id, "\"" + h.data.exportCategory + "\"", h.detected ? 1 : 0,
                    h.detected ? F(h.detectedAt) : "", h.selected ?? "", h.q.correct, h.isCorrect == true ? 1 : 0, h.wrongAnswers,
                    h.solved ? 1 : 0, h.critical ? 1 : 0, h.responseTime >= 0 ? F(h.responseTime) : ""));
            csv.AppendLine();
            csv.AppendLine("session_id,hazards_found,hazards_missed,wrong_answers,completion_time,score,outcome");
            csv.AppendLine(string.Join(",", sessionId, found.Count, missed.Count, wrong, F(r.duration), r.score, r.outcome));

            LastJsonPath = LastCsvPath = null;   // the end screen shows "exported" only for a path set after a successful write
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "ChemLabSessions");
                Directory.CreateDirectory(dir);
                var json = Path.Combine(dir, sessionId + ".json");
                var csvPath = Path.Combine(dir, sessionId + ".csv");
                File.WriteAllText(json, sb.ToString());
                File.WriteAllText(csvPath, csv.ToString());
                LastJsonPath = json;
                LastCsvPath = csvPath;
                r.savedPath = LastJsonPath;
                Debug.Log("[ChemLab] Run exported: " + LastJsonPath + " and .csv\n" + sb);
            }
            catch (Exception e)
            {
                Debug.LogError("[ChemLab] Could not export run: " + e.Message);
            }
        }
    }
}
