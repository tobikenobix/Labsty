using UnityEngine;

namespace ChemLab
{
    /// <summary>
    /// Game sounds. Clips load from Resources/ChemLab/Audio/&lt;name&gt;; a missing clip falls back to a
    /// procedurally generated tone (chime, buzz, click, ding), so nothing breaks if a file is removed.
    /// </summary>
    public static class ChemLabAudio
    {
        const string Dir = "ChemLab/Audio/";
        const float FadeSeconds = 0.3f;   // fade-out at the end of a cut clip, avoids a click
        static AudioSource source;
        static AudioClip correct, wrong, click, ding;   // synthesized fallbacks

        static AudioSource Source
        {
            get
            {
                if (source == null)
                {
                    var go = new GameObject("ChemLab Audio");
                    Object.DontDestroyOnLoad(go);
                    source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                }
                return source;
            }
        }

        public static void Correct() => Play(Clip("correct") ?? (correct ??= Make("correct", 0.55f, (t, d) => Tone(t, t < 0.16f ? 660f : 990f, 0.5f) * Env(t, d))));
        public static void Wrong(bool critical = false) => Play(Clip(critical ? "wrong_critical" : "wrong") ?? (wrong ??= Make("wrong", 0.4f, (t, d) => Square(t, 140f, 0.35f) * Env(t, d))));
        public static void Click() => Play(Clip("click") ?? (click ??= Make("click", 0.05f, (t, d) => Tone(t, 1400f, 0.25f) * (1f - t / d))), 0.6f);
        public static void Ding() => Play(Clip("complete") ?? DingTone());
        public static void Button() => Play(Clip("button") ?? DingTone());
        public static void Alarm(float seconds = 2f) { var c = Clip("alarm"); if (c != null) PlayCut(c, null, seconds); }

        /// <summary>One-shot 3D sound at a world position, cut after maxSeconds (0 = full length).</summary>
        public static void PlayAt(string name, Vector3 position, float maxSeconds = 0f)
        {
            var c = Clip(name);
            if (c != null) PlayCut(c, position, maxSeconds);
        }

        /// <summary>Looping sound, 3D on <paramref name="at"/> or 2D when null. Destroy the returned source's GameObject to stop it.</summary>
        public static AudioSource Loop(string name, float volume, Transform at = null)
        {
            var c = Clip(name);
            if (c == null) return null;
            var go = new GameObject("Loop " + name);
            if (at != null) go.transform.SetParent(at, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.volume = volume;
            s.spatialBlend = at != null ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1f; s.maxDistance = 8f;
            s.Play();
            return s;
        }

        static AudioClip Clip(string name) => string.IsNullOrEmpty(name) ? null : Resources.Load<AudioClip>(Dir + name);
        static AudioClip DingTone() => ding ??= Make("ding", 0.6f, (t, d) => (Tone(t, 880f, 0.35f) + Tone(t, 1320f, 0.15f)) * Mathf.Exp(-t * 6f));

        static void Play(AudioClip clip, float volume = 1f) => Source.PlayOneShot(clip, volume);

        static void PlayCut(AudioClip clip, Vector3? position, float maxSeconds)
        {
            var go = new GameObject("SFX " + clip.name);
            if (position.HasValue) go.transform.position = position.Value;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.spatialBlend = position.HasValue ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1f; s.maxDistance = 12f;
            s.Play();
            float len = maxSeconds > 0f ? Mathf.Min(maxSeconds, clip.length) : clip.length;
            go.AddComponent<FadeOut>().stopAt = Time.unscaledTime + len;
        }

        sealed class FadeOut : MonoBehaviour
        {
            public float stopAt;
            void Update()
            {
                float left = stopAt - Time.unscaledTime;
                if (left <= 0f) { Destroy(gameObject); return; }
                GetComponent<AudioSource>().volume = Mathf.Clamp01(left / FadeSeconds);
            }
        }

        static float Tone(float t, float f, float a) => Mathf.Sin(2f * Mathf.PI * f * t) * a;
        static float Square(float t, float f, float a) => (Mathf.Sin(2f * Mathf.PI * f * t) > 0 ? 1f : -1f) * a * 0.6f;
        static float Env(float t, float d) => Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((d - t) / 0.12f);

        static AudioClip Make(string name, float dur, System.Func<float, float, float> f)
        {
            const int rate = 44100;
            int n = Mathf.CeilToInt(dur * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)rate, dur);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
