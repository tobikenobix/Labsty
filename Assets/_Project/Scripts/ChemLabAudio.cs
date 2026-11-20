using UnityEngine;

namespace ChemLab
{
    /// <summary>Procedurally generated UI sounds (no audio files needed): chime, buzz, click, ding.</summary>
    public static class ChemLabAudio
    {
        static AudioSource source;
        static AudioClip correct, wrong, click, ding;

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

        public static void Correct() => Play(correct ??= Make("correct", 0.55f, (t, d) => Tone(t, t < 0.16f ? 660f : 990f, 0.5f) * Env(t, d)));
        public static void Wrong() => Play(wrong ??= Make("wrong", 0.4f, (t, d) => Square(t, 140f, 0.35f) * Env(t, d)));
        public static void Click() => Play(click ??= Make("click", 0.05f, (t, d) => Tone(t, 1400f, 0.25f) * (1f - t / d)), 0.6f);
        public static void Ding() => Play(ding ??= Make("ding", 0.6f, (t, d) => (Tone(t, 880f, 0.35f) + Tone(t, 1320f, 0.15f)) * Mathf.Exp(-t * 6f)));

        static void Play(AudioClip clip, float volume = 1f) => Source.PlayOneShot(clip, volume);

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
