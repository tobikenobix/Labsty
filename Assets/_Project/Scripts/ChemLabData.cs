using System;
using UnityEngine;

namespace ChemLab
{
    // Shapes match Resources/ChemLab/hazards.json. Edit the JSON to change question text,
    // correct answers, critical flags or explanations without touching code.

    [Serializable]
    public class OptionData
    {
        public string key;
        public string text;
        public bool critical;
    }

    /// <summary>One question of a hazard's pool. Empty explanation / criticalReason = use the hazard's.</summary>
    [Serializable]
    public class QuestionData
    {
        public string id;                // "Q1".."Q3"
        public string question;
        public string correct;
        public string explanation;
        public string criticalReason;
        public OptionData[] options;

        public OptionData Option(string key)
        {
            foreach (var o in options) if (o.key == key) return o;
            return null;
        }
    }

    [Serializable]
    public class HazardData
    {
        public string id;
        public string title;
        public string area;
        public string fixSound;          // clip in Resources/ChemLab/Audio played when fixed ("" = silent)
        public float fixSoundSeconds;    // cut + fade the clip after this many seconds (0 = play it all)
        public string difficulty;
        public string category;
        public string explanation;
        public bool mandatoryCritical;   // one of the safety-critical hazards that must be answered correctly to pass
        public string criticalReason;    // why the critical options are unsafe (shown in the report)
        public string hudCategory;       // group shown on the player's tablet (e.g. "Escape Routes")
        public string wrongFeedback;     // one line shown after a wrong answer
        public string missedFeedback;    // report line when the hazard was never found
        public QuestionData[] questions; // the pool; one is drawn per run
        [NonSerialized] public string exportCategory;   // English hudCategory, kept for the JSON/CSV export
    }

    [Serializable]
    public class ScoringConfig
    {
        public int pointsPerDetection = 3;
        public int pointsPerCorrectDecision = 7;
        public int passScore = 80;
        public int minDetected = 8;
        public int minCorrect = 8;
    }

    [Serializable]
    public class LabConfig
    {
        public ScoringConfig scoring = new ScoringConfig();
        public float timeLimitSeconds = 420f;   // 7-minute countdown
        public float targetTimeSeconds = 480f;
        public string supervisorWelcome;
        public HazardData[] hazards;

        public HazardData Find(string id)
        {
            foreach (var h in hazards) if (h.id == id) return h;
            return null;
        }

        public static LabConfig Load()
        {
            var asset = Resources.Load<TextAsset>("ChemLab/hazards");
            if (asset == null)
            {
                Debug.LogError("[ChemLab] Resources/ChemLab/hazards.json not found.");
                return new LabConfig { hazards = new HazardData[0] };
            }
            var cfg = JsonUtility.FromJson<LabConfig>(asset.text);
            foreach (var h in cfg.hazards)
            {
                if (h.questions == null || h.questions.Length == 0) Debug.LogError($"[ChemLab] {h.id} has no questions in hazards.json.");
                else foreach (var q in h.questions)
                    if (q.Option(q.correct) == null) Debug.LogError($"[ChemLab] {h.id} {q.id}: correct answer '{q.correct}' is not one of its options.");
            }
            Localize(cfg);
            return cfg;
        }

        /// <summary>Swaps the English JSON text for the selected language's "Hazards" table entries (English stays where none exists).</summary>
        static void Localize(LabConfig cfg)
        {
            cfg.supervisorWelcome = L10n.Hazard("Lab.welcome", cfg.supervisorWelcome);
            foreach (var h in cfg.hazards)
            {
                h.exportCategory = h.hudCategory;
                string k = h.id + ".";
                h.title = L10n.Hazard(k + "title", h.title);
                h.area = L10n.Hazard(k + "area", h.area);
                h.difficulty = L10n.Hazard(k + "difficulty", h.difficulty);
                h.category = L10n.Hazard(k + "category", h.category);
                h.hudCategory = L10n.Hazard(k + "hudCategory", h.hudCategory);
                h.explanation = L10n.Hazard(k + "explanation", h.explanation);
                h.criticalReason = L10n.Hazard(k + "criticalReason", h.criticalReason);
                h.wrongFeedback = L10n.Hazard(k + "wrongFeedback", h.wrongFeedback);
                h.missedFeedback = L10n.Hazard(k + "missedFeedback", h.missedFeedback);
                if (h.questions == null) continue;
                foreach (var q in h.questions)
                {
                    string qk = k + q.id + ".";
                    q.question = L10n.Hazard(qk + "question", q.question);
                    q.explanation = L10n.Hazard(qk + "explanation", q.explanation);
                    q.criticalReason = L10n.Hazard(qk + "criticalReason", q.criticalReason);
                    foreach (var o in q.options) o.text = L10n.Hazard(qk + "option" + o.key, o.text);
                }
            }
        }
    }

    public enum HazardStatus { Missed, DetectedUnanswered, Correct, Incorrect }

    /// <summary>What happened to one hazard during one session.</summary>
    public class HazardRecord
    {
        public HazardData data;
        public QuestionData q;              // the question drawn from the pool for this run
        public string Explanation => string.IsNullOrEmpty(q.explanation) ? data.explanation : q.explanation;
        public string CriticalReason => string.IsNullOrEmpty(q.criticalReason) ? data.criticalReason : q.criticalReason;
        public bool detected;
        public float detectedAt = -1f;      // seconds since inspection start
        public float questionShownAt = -1f; // seconds since inspection start (first display)
        public string selected;             // first submitted answer (assessed)
        public float responseTime = -1f;    // question display -> first submission
        public bool? isCorrect;             // first attempt
        public bool critical;               // first answer was a safety-critical choice
        public int attempts;                // total submissions
        public int wrongAnswers;            // every wrong submission is logged as a mistake
        public bool solved;                 // eventually answered correctly -> hazard fixed in the scene
        public string shownOrder;           // option keys in the shuffled order the panel showed them, e.g. "CADB"
        // set by HazardPlacement when the hazard was moved into a zone; null = it stayed at its authored spot
        public string zoneName, zoneArea, zoneDifficulty;
        public string Area => zoneArea == null ? data.area : L10n.Hazard("Area." + zoneArea, zoneArea);
        public string Difficulty => zoneDifficulty == null ? data.difficulty : L10n.Hazard("Difficulty." + zoneDifficulty, zoneDifficulty);

        /// <summary>The letter the student saw for an option key (rows are lettered A-D by position after shuffling).</summary>
        public string Shown(string key)
        {
            int i = key == null || shownOrder == null ? -1 : shownOrder.IndexOf(key, System.StringComparison.Ordinal);
            return i < 0 ? key : ((char)('A' + i)).ToString();
        }

        public HazardStatus Status =>
            !detected ? HazardStatus.Missed :
            isCorrect == null ? HazardStatus.DetectedUnanswered :
            isCorrect.Value ? HazardStatus.Correct : HazardStatus.Incorrect;

        public int Points(ScoringConfig sc) => (detected ? sc.pointsPerDetection : 0) + (isCorrect == true ? sc.pointsPerCorrectDecision : 0);
        public int MaxPoints(ScoringConfig sc) => sc.pointsPerDetection + sc.pointsPerCorrectDecision;
    }
}
