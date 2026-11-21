using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChemLab
{
    public enum Phase { Intro, Running, FinishPrompt, Ended }

    public enum Outcome { Pass, RetrainingRequired, SafetyReviewRequired }

    public class ChemLabResult
    {
        public int detected, correct, answered, score, falseClicks, falsePenalty, wrongAnswers;
        public int detectionPoints, decisionPoints, maxDetectionPoints, maxDecisionPoints;
        public float duration;
        // two-layer assessment: numerical performance + mandatory safety conditions
        public bool minScoreOk, minDetectionOk, minDecisionsOk, allCriticalHazardsCorrect, noCriticalChoice;
        public Outcome outcome;
        public string recommendation, savedPath;
    }

    /// <summary>
    /// Scene 2 game loop. The 7-minute countdown starts when the lab loads.
    /// Click hazard -> 4-option panel. Correct: chime + green check over the hazard. Wrong: buzz + one-line feedback + red cross,
    /// logged as a mistake. Every inspected object keeps an outline, so the player can see what they have already checked.
    /// The run ends when all hazards are resolved, the timer hits 0, or the player finishes early (Y / Menu).
    /// Scoring: 3 points per hazard found + 7 per FIRST answer correct (= 100), minus 1 per false alarm after the third,
    /// plus pass / retraining / safety-review rules.
    /// </summary>
    public class ChemLabSession : MonoBehaviour
    {
        public static ChemLabSession Instance { get; private set; }

        public Phase phase { get; private set; } = Phase.Intro;
        public LabConfig Config { get; private set; }
        public IReadOnlyList<HazardRecord> Records => records;
        public HazardRecord Current { get; private set; }
        public ChemLabInspectable CurrentObject { get; private set; }
        public string CurrentSelection { get; private set; }
        public float Elapsed => phase == Phase.Intro ? 0f : phase == Phase.Ended ? endTime - startTime : Time.time - startTime;
        public float Remaining => Mathf.Max(0f, Config.timeLimitSeconds - Elapsed);
        public int DetectedCount => records.Count(r => r.detected);
        public int ResolvedCount => records.Count(r => r.solved);
        public int HazardCount => records.Count;

        const int FreeFalseClicks = 3;   // false alarms allowed before they cost points
        readonly List<HazardRecord> records = new List<HazardRecord>();
        ChemLabUI ui;
        float startTime, endTime;
        int falseClicks;
        string sessionId;
        AudioSource tick;   // clock ticking during the last minute

        void Awake()
        {
            Instance = this;
            Config = LabConfig.Load();
            ui = GetComponent<ChemLabUI>();
            var active = ChemLabRun.PickHazards(Config.hazards);
            foreach (var h in active) records.Add(new HazardRecord { data = h, q = ChemLabRun.PickQuestion(h) });
            // hazards left out of this run start in their safe state
            foreach (var i in FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None))
                if (i.kind == ChemLabInspectable.Kind.Hazard && active.All(h => h.id != i.hazardId)) i.FixInstant();
            sessionId = "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // after FixInstant, so the left-out hazards' colliders no longer block spots
            int seed = ChemLabRun.LastPlacementSeed = ChemLabRun.PlacementSeed >= 0 ? ChemLabRun.PlacementSeed : Environment.TickCount & int.MaxValue;
            Debug.Log($"[ChemLab] {sessionId} placement seed {seed}" + (ChemLabRun.RandomizePlacement ? "" : " (randomizing off)"));
            if (ChemLabRun.RandomizePlacement) HazardPlacement.Place(records, seed);
            ChemLabRun.BeginAttempt();
        }

        void OnEnable()
        {
            ChemLabInspectable.Inspected += OnInspect;
            ChemLabControls.Confirm += Confirm;
            ChemLabControls.Back += Back;
            ChemLabControls.Finish += RequestFinish;
            ChemLabControls.BlockInspect = () => ui != null && ui.ModalOpen;
        }

        void OnDisable()
        {
            ChemLabInspectable.Inspected -= OnInspect;
            ChemLabControls.Confirm -= Confirm;
            ChemLabControls.Back -= Back;
            ChemLabControls.Finish -= RequestFinish;
            ChemLabControls.BlockInspect = null;
        }

        IEnumerator Start()
        {
            // give head tracking a moment so panels appear at eye height
            yield return new WaitForSeconds(1.0f);
            ChemLabAudio.Loop("ambient", 0.3f);
            var hood = FindObjectsByType<ChemLabInspectable>(FindObjectsSortMode.None).FirstOrDefault(i => i.displayName == "Fume hood");
            if (hood != null) ChemLabAudio.Loop("ventilator", 0.5f, hood.transform);
            phase = Phase.Running;
            startTime = Time.time;
            ui.ShowRunStarted(Config.timeLimitSeconds);
        }

        void Update()
        {
            if (phase == Phase.Running || phase == Phase.FinishPrompt)
            {
                if (tick == null && Remaining <= 60f) tick = ChemLabAudio.Loop("clock_tick", 0.6f);
                if (Remaining <= 0f) EndRun(timedOut: true);
            }
        }

        // ---------- interaction ----------

        void OnInspect(ChemLabInspectable item)
        {
            if (phase != Phase.Running || ui.ModalOpen) return;

            if (item.kind != ChemLabInspectable.Kind.Hazard)
            {
                falseClicks++;
                item.SetMark(ChemLabInspectable.Mark.Checked);
                if (falseClicks == FreeFalseClicks) ui.Toast(L10n.T("lab.falseWarn", FreeFalseClicks), 3.5f, ChemLabUIKit.Warn);   // the rule is announced before it costs anything
                else ui.Toast(string.IsNullOrEmpty(item.displayName) ? L10n.T("lab.looksOk") : L10n.T("lab.itemOk", L10n.TOr("decoy." + item.displayName, item.displayName)));
                return;
            }

            var rec = records.FirstOrDefault(r => r.data.id == item.hazardId);
            if (rec == null) { Debug.LogWarning("[ChemLab] Unknown hazard id " + item.hazardId); return; }
            if (rec.solved) return;

            if (!rec.detected) { rec.detected = true; rec.detectedAt = Elapsed; }   // detection counted once
            item.SetMark(ChemLabInspectable.Mark.Checked);
            if (rec.questionShownAt < 0) rec.questionShownAt = Elapsed;
            Current = rec;
            CurrentObject = item;
            CurrentSelection = null;
            ui.ShowQuestion(rec, item.transform.position);
            ui.UpdateHud();
        }

        public void SelectOption(string key)
        {
            if (Current == null) return;
            CurrentSelection = key;
            ui.HighlightOption(key);
        }

        void Confirm()
        {
            if (Current != null) SubmitCurrent();
            else ui.PressPrimary();
        }

        public void SubmitCurrent()
        {
            if (Current == null || CurrentSelection == null) return;
            var rec = Current;
            var key = CurrentSelection;
            var opt = rec.q.Option(key);
            bool correct = key == rec.q.correct;

            rec.attempts++;
            if (rec.selected == null)   // only the first answer is scored
            {
                rec.selected = key;
                rec.responseTime = Elapsed - rec.questionShownAt;
                rec.isCorrect = correct;
                rec.critical = opt != null && opt.critical;
            }

            if (correct)
            {
                rec.solved = true;
                ChemLabAudio.Correct();
                var obj = CurrentObject;
                ChemLabAudio.PlayAt(rec.data.fixSound, obj.transform.position, rec.data.fixSoundSeconds);
                CloseQuestion();
                obj.Fix();
                ui.Toast(L10n.T("lab.resolved", rec.data.title), 2.4f, ChemLabUIKit.Ok);
                ui.UpdateHud();
                if (records.All(r => r.solved)) StartCoroutine(EndAfter(1.6f));
            }
            else
            {
                rec.wrongAnswers++;   // logged as a mistake
                ChemLabAudio.Wrong(opt != null && opt.critical);
                CurrentObject.SetMark(ChemLabInspectable.Mark.Wrong);
                ui.ShowWrong(rec, key, opt != null && opt.critical);
                CurrentSelection = null;
            }
        }

        public void CloseQuestion()
        {
            Current = null;
            CurrentObject = null;
            CurrentSelection = null;
            ui.CloseQuestion();
        }

        void Back()
        {
            if (ui.QuestionOpen) CloseQuestion();
            else if (phase == Phase.FinishPrompt) ContinueInspecting();
        }

        public void RequestFinish()
        {
            if (phase != Phase.Running || ui.QuestionOpen) return;
            phase = Phase.FinishPrompt;
            ui.ShowFinishPrompt();
        }

        public void ContinueInspecting()
        {
            if (phase == Phase.FinishPrompt) phase = Phase.Running;
            ui.CloseMessage();
        }

        public void FinishNow() => EndRun(timedOut: false);

        IEnumerator EndAfter(float s)
        {
            yield return new WaitForSeconds(s);
            EndRun(timedOut: false);
        }

        void EndRun(bool timedOut)
        {
            if (phase == Phase.Ended) return;
            endTime = Time.time;
            phase = Phase.Ended;
            if (ui.QuestionOpen) CloseQuestion();
            ui.CloseMessage();

            var result = Score(endTime - startTime);
            ChemLabRun.LastTimedOut = timedOut;
            ChemLabRun.Export(sessionId, false, result, records);
            ChemLabRun.LastResult = result;
            ChemLabRun.LastRecords = records.ToList();
            ChemLabRun.LastConfig = Config;

            if (tick != null) Destroy(tick.gameObject);
            if (timedOut) ChemLabAudio.Alarm(); else ChemLabAudio.Ding();
            ui.ShowRunEnded(timedOut ? L10n.T("lab.timeUp") : records.All(r => r.solved) ? L10n.T("lab.allResolved") : L10n.T("lab.submitted"));
            StartCoroutine(LoadEnd());
        }

        IEnumerator LoadEnd()
        {
            yield return new WaitForSeconds(2.2f);
            if (Application.CanStreamedLevelBeLoaded(ChemLabRun.EndScene)) SceneManager.LoadScene(ChemLabRun.EndScene);
            else Debug.LogWarning("[ChemLab] End scene not in Build Settings. Run Chemistry Lab > Build All Scenes.");
        }

        // ---------- scoring ----------

        public ChemLabResult Score(float duration)
        {
            var sc = Config.scoring ?? new ScoringConfig();
            var r = new ChemLabResult
            {
                duration = duration,
                detected = records.Count(x => x.detected),
                correct = records.Count(x => x.isCorrect == true),
                answered = records.Count(x => x.selected != null),
                falseClicks = falseClicks,
                wrongAnswers = records.Sum(x => x.wrongAnswers),
            };
            // Hazard recognition and safety decision-making are scored separately.
            // Clicking everything isn't detection: past FreeFalseClicks, each false alarm costs one detection point.
            r.falsePenalty = Mathf.Max(0, falseClicks - FreeFalseClicks);
            r.detectionPoints = Mathf.Max(0, sc.pointsPerDetection * r.detected - r.falsePenalty);
            r.decisionPoints = sc.pointsPerCorrectDecision * r.correct;
            r.maxDetectionPoints = sc.pointsPerDetection * records.Count;
            r.maxDecisionPoints = sc.pointsPerCorrectDecision * records.Count;
            // Score = % of the points this run offered, so a run with fewer hazards is graded on the same scale.
            int max = r.maxDetectionPoints + r.maxDecisionPoints;
            r.score = max == 0 ? 0 : Mathf.RoundToInt(100f * (r.detectionPoints + r.decisionPoints) / max);
            // minDetected / minCorrect are written for the full hazard set; scale them to this run's share.
            float share = Config.hazards.Length == 0 ? 1f : records.Count / (float)Config.hazards.Length;

            r.minScoreOk = r.score >= sc.passScore;
            r.minDetectionOk = r.detected >= Mathf.CeilToInt(sc.minDetected * share);
            r.minDecisionsOk = r.correct >= Mathf.CeilToInt(sc.minCorrect * share);
            r.allCriticalHazardsCorrect = records.Where(x => x.data.mandatoryCritical).All(x => x.isCorrect == true);
            r.noCriticalChoice = !records.Any(x => x.critical);

            if (!r.noCriticalChoice || !r.allCriticalHazardsCorrect) r.outcome = Outcome.SafetyReviewRequired;
            else if (!r.minScoreOk || !r.minDetectionOk || !r.minDecisionsOk) r.outcome = Outcome.RetrainingRequired;
            else r.outcome = Outcome.Pass;

            var weak = records.Where(x => x.Status != HazardStatus.Correct).Select(x => x.data.category).Distinct().ToList();
            switch (r.outcome)
            {
                case Outcome.Pass:
                    r.recommendation = weak.Count == 0
                        ? "Competence demonstrated. All hazards found and handled correctly."
                        : "Competence demonstrated. For full marks, review: " + string.Join(", ", weak) + ".";
                    break;
                case Outcome.RetrainingRequired:
                    r.recommendation = "Knowledge gap. Review " + string.Join(", ", weak) + ", then take a fresh assessment.";
                    break;
                default:
                    r.recommendation = "Despite scoring " + r.score + "/100, a safety-critical requirement was not met. Review the flagged procedures with HSE and complete another assessment.";
                    break;
            }
            return r;
        }

        public static string OutcomeLabel(Outcome o) =>
            o == Outcome.Pass ? "PASS: Qualified" :
            o == Outcome.RetrainingRequired ? "Retraining required" : "Not yet qualified: safety review required";

        public static Color OutcomeColor(Outcome o) =>
            o == Outcome.Pass ? ChemLabUIKit.Ok : o == Outcome.RetrainingRequired ? ChemLabUIKit.Warn : ChemLabUIKit.Bad;
    }
}
