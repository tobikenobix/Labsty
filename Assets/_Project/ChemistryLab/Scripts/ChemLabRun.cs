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

        public static string Language = "EN";
        public static string StudentId = "student_001";
        public static int Attempt = 0;

        // filled by the lab when the run ends, read by the end screen
        public static ChemLabResult LastResult;
        public static List<HazardRecord> LastRecords;
        public static LabConfig LastConfig;
        public static bool LastTimedOut;
        public static string LastJsonPath, LastCsvPath, LastSessionId;

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
                sb.Append($"\"id\": {S(h.data.id)}, \"category\": {S(h.data.hudCategory)}, \"status\": {S(h.Status.ToString())}, ");
                sb.Append($"\"mandatoryCritical\": {B(h.data.mandatoryCritical)}, \"detected\": {B(h.detected)}, ");
                sb.Append($"\"detectedAt\": {(h.detected ? F(h.detectedAt) : "null")}, ");
                sb.Append($"\"selectedOption\": {S(h.selected)}, \"correctOption\": {S(h.data.correct)}, \"isCorrect\": {B(h.isCorrect)}, ");
                sb.Append($"\"responseTimeSeconds\": {(h.responseTime >= 0 ? F(h.responseTime) : "null")}, ");
                sb.Append($"\"wrongAnswers\": {h.wrongAnswers}, \"resolved\": {B(h.solved)}, \"criticalError\": {B(h.critical)}}}");
                sb.Append(i < records.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            var csv = new StringBuilder();
            csv.AppendLine("session_id,student_id,hazard_id,category,detected,detected_at,first_answer,correct_answer,first_attempt_correct,wrong_answers,resolved,critical_error,response_time");
            foreach (var h in records)
                csv.AppendLine(string.Join(",", sessionId, StudentId, h.data.id, "\"" + h.data.hudCategory + "\"", h.detected ? 1 : 0,
                    h.detected ? F(h.detectedAt) : "", h.selected ?? "", h.data.correct, h.isCorrect == true ? 1 : 0, h.wrongAnswers,
                    h.solved ? 1 : 0, h.critical ? 1 : 0, h.responseTime >= 0 ? F(h.responseTime) : ""));
            csv.AppendLine();
            csv.AppendLine("session_id,hazards_found,hazards_missed,wrong_answers,completion_time,score,outcome");
            csv.AppendLine(string.Join(",", sessionId, found.Count, missed.Count, wrong, F(r.duration), r.score, r.outcome));

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "ChemLabSessions");
                Directory.CreateDirectory(dir);
                LastJsonPath = Path.Combine(dir, sessionId + ".json");
                LastCsvPath = Path.Combine(dir, sessionId + ".csv");
                File.WriteAllText(LastJsonPath, sb.ToString());
                File.WriteAllText(LastCsvPath, csv.ToString());
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
