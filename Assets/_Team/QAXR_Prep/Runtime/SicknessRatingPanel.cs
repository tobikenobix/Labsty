using System;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

namespace QAXR
{
    /// <summary>
    /// In-VR continuous sickness rating for the validation sessions (Roadmap §4.8, after Plaza et al. 2026).
    /// Put it on an empty GameObject; it builds a row of 0..maxValue buttons (poke or ray) in Awake.
    /// Every promptInterval seconds the buttons light up and ask for a rating; the participant can also
    /// rate at any time. One CSV per session in persistentDataPath/qaxr/ratings:
    /// timestamp, t_s, event (start/prompt/rating/end), rating, latency_s, participant, condition.
    /// Orientation: the participant stands on the -Z side of this object, looking along its +Z.
    /// </summary>
    public class SicknessRatingPanel : MonoBehaviour
    {
        [Header("Session")]
        [SerializeField] string participant = "P01";
        [SerializeField] string condition = "bad";
        [SerializeField] bool startOnAwake = true;

        [Header("Scale")]
        [SerializeField] int maxValue = 10;         // 10 for the 0–10 slider; 20 for FMS
        [SerializeField] float promptInterval = 30f;
        [SerializeField] string question = "How sick do you feel right now?";
        [SerializeField] string lowLabel = "0  no symptoms";
        [SerializeField] string highLabel = "want to stop";

        [Header("Look")]
        [SerializeField] float buttonSize = 0.06f;  // 6 cm ≈ 4.6° at 75 cm, well above the 2° rule
        [SerializeField] Color idleColor = new Color(0.25f, 0.27f, 0.3f);
        [SerializeField] Color promptColor = new Color(1f, 0.65f, 0.1f);
        [SerializeField] Color chosenColor = new Color(0.2f, 0.75f, 0.4f);
        [SerializeField] AudioSource promptSound;   // optional beep on each prompt

        public int LastRating { get; private set; } = -1;

        TextMeshPro questionText;
        Renderer[] buttonRenderers;
        StreamWriter writer;
        float startTime, nextPrompt, promptedAt = -1f;
        bool running;

        void Awake()
        {
            Build();
            if (startOnAwake) Begin();
        }

        public void SetParticipant(string id) => participant = id;
        public void SetCondition(string id) => condition = id;

        /// <summary>Starts a new CSV and prompts immediately.</summary>
        public void Begin()
        {
            End();
            var dir = Path.Combine(Application.persistentDataPath, "qaxr", "ratings");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{Clean(participant)}_{Clean(condition)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("timestamp,t_s,event,rating,latency_s,participant,condition");
            startTime = Time.time;
            nextPrompt = startTime;
            LastRating = -1;
            running = true;
            Write("start", -1, -1f);
            Debug.Log($"[QAXR] Sickness ratings → {path}");
        }

        public void End()
        {
            if (!running) return;
            Write("end", -1, -1f);
            running = false;
            writer?.Dispose();
            writer = null;
        }

        void Update()
        {
            if (!running || Time.time < nextPrompt) return;
            nextPrompt += promptInterval;
            promptedAt = Time.time;
            questionText.text = question;
            Paint(-1, promptColor);
            if (promptSound) promptSound.Play();
            Write("prompt", -1, -1f);
        }

        void Rate(int value)
        {
            if (!running) return;
            float latency = promptedAt >= 0f ? Time.time - promptedAt : -1f; // -1: rated without a prompt
            promptedAt = -1f;
            LastRating = value;
            Write("rating", value, latency);
            questionText.text = $"Thanks — {value}. Rate again any time.";
            Paint(value, idleColor);
        }

        void Paint(int chosen, Color others)
        {
            for (int i = 0; i < buttonRenderers.Length; i++)
                buttonRenderers[i].material.color = i == chosen ? chosenColor : others;
        }

        void Build()
        {
            float pitch = buttonSize * 1.2f, width = maxValue * pitch;
            buttonRenderers = new Renderer[maxValue + 1];
            for (int v = 0; v <= maxValue; v++)
            {
                int value = v; // capture per button
                buttonRenderers[v] = QaxrUi.Button(transform, $"Rate_{v}", v.ToString(),
                                                   new Vector3(v * pitch - width / 2f, 0f, 0f), buttonSize, () => Rate(value));
            }
            questionText = QaxrUi.Text(transform, question, new Vector3(0f, buttonSize * 1.6f, 0f), buttonSize * 0.7f, width + buttonSize);
            QaxrUi.Text(transform, lowLabel, new Vector3(-width / 2f, -buttonSize * 1.1f, 0f), buttonSize * 0.4f, buttonSize * 5f);
            QaxrUi.Text(transform, $"{maxValue}  {highLabel}", new Vector3(width / 2f, -buttonSize * 1.1f, 0f), buttonSize * 0.4f, buttonSize * 5f);
            Paint(-1, idleColor);
        }

        void Write(string ev, int rating, float latency)
        {
            if (writer == null) return;
            var c = CultureInfo.InvariantCulture;
            writer.WriteLine(string.Join(",",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", c),
                (Time.time - startTime).ToString("F3", c),
                ev,
                rating >= 0 ? rating.ToString(c) : "",
                latency >= 0f ? latency.ToString("F2", c) : "",
                Clean(participant), Clean(condition)));
            writer.Flush(); // the headset may be pulled off mid-session
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ");

        void OnApplicationPause(bool paused) { if (paused) writer?.Flush(); }
        void OnDestroy() => End();
    }
}
