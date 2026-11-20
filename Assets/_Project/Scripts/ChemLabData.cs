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

    [Serializable]
    public class HazardData
    {
        public string id;
        public string title;
        public string area;
        public string difficulty;
        public string category;
        public string question;
        public string correct;
        public string explanation;
        public bool mandatoryCritical;   // one of the safety-critical hazards that must be answered correctly to pass
        public string criticalReason;    // why the critical options are unsafe (shown in the report)
        public string hudCategory;       // group shown on the player's tablet (e.g. "Escape Routes")
        public string wrongFeedback;     // one line shown after a wrong answer
        public string missedFeedback;    // report line when the hazard was never found
        public OptionData[] options;

        public OptionData Option(string key)
        {
            foreach (var o in options) if (o.key == key) return o;
            return null;
        }
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
            return JsonUtility.FromJson<LabConfig>(asset.text);
        }
    }

    public enum HazardStatus { Missed, DetectedUnanswered, Correct, Incorrect }

    /// <summary>What happened to one hazard during one session.</summary>
    public class HazardRecord
    {
        public HazardData data;
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

        public HazardStatus Status =>
            !detected ? HazardStatus.Missed :
            isCorrect == null ? HazardStatus.DetectedUnanswered :
            isCorrect.Value ? HazardStatus.Correct : HazardStatus.Incorrect;

        public int Points(ScoringConfig sc) => (detected ? sc.pointsPerDetection : 0) + (isCorrect == true ? sc.pointsPerCorrectDecision : 0);
        public int MaxPoints(ScoringConfig sc) => sc.pointsPerDetection + sc.pointsPerCorrectDecision;
    }
}
