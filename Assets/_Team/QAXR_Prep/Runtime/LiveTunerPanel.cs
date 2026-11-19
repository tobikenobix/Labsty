using System;
using TMPro;
using UnityEngine;

namespace QAXR
{
    /// <summary>
    /// L1 LiveTuner UI (Roadmap §4.7): Zone 1 speed − / + and the `fixed` toggle, with "before: X → now: Y" for the
    /// rolling risk score. The score itself comes from W4: set `Score` in code (e.g. () => rolling.Value).
    /// "Before" is the score at the moment of the last change. Readable from the -Z side, like SicknessRatingPanel.
    /// </summary>
    public class LiveTunerPanel : MonoBehaviour
    {
        [SerializeField] FastCorridorZone zone;
        [SerializeField] float speedStep = 0.5f;
        [SerializeField] float minSpeed = 0.5f, maxSpeed = 6f;
        [SerializeField] float buttonSize = 0.08f;

        /// <summary>Rolling risk score source, 0..1. Null shows "n/a".</summary>
        public Func<float> Score;

        public FastCorridorZone Zone { get => zone; set => zone = value; }
        public float Before { get; private set; } = float.NaN;

        TextMeshPro status;
        Renderer fixedKey;

        void Awake() => Build();

        void Build()
        {
            if (status) return;
            float s = buttonSize;
            QaxrUi.Button(transform, "Slower", "−", new Vector3(-1.5f * s, 0f, 0f), s, Slower);
            QaxrUi.Button(transform, "Faster", "+", new Vector3(-0.3f * s, 0f, 0f), s, Faster);
            fixedKey = QaxrUi.Button(transform, "FixedToggle", "fix", new Vector3(1.3f * s, 0f, 0f), s, ToggleFixed);
            status = QaxrUi.Text(transform, "", new Vector3(0f, 1.3f * s, 0f), s * 0.35f, s * 8f);
            Refresh();
        }

        public void Faster() => Change(() => zone.badSpeed = Mathf.Min(maxSpeed, zone.badSpeed + speedStep));
        public void Slower() => Change(() => zone.badSpeed = Mathf.Max(minSpeed, zone.badSpeed - speedStep));
        public void ToggleFixed() => Change(() => zone.IsFixed = !zone.IsFixed);

        void Change(Action apply)
        {
            if (!zone) return;
            Before = Score != null ? Score() : float.NaN;
            apply();
            Refresh();
        }

        void Update() => Refresh();

        void Refresh()
        {
            if (!status) return;
            if (!zone) { status.text = "LiveTuner: no zone assigned"; return; }
            string now = Score != null ? Score().ToString("0.00") : "n/a";
            string before = float.IsNaN(Before) ? "—" : Before.ToString("0.00");
            status.text = $"Zone 1  {(zone.IsFixed ? "FIXED" : $"{zone.badSpeed:0.0} m/s")}\nrisk  before {before}  →  now {now}";
            if (fixedKey) fixedKey.material.color = zone.IsFixed ? new Color(0.2f, 0.75f, 0.4f) : new Color(0.25f, 0.27f, 0.3f);
        }
    }
}
