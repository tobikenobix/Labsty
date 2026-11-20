using System;
using UnityEngine;

namespace QAXR
{
    /// <summary>
    /// Pure kinematics for D1/D2 (Roadmap §4.3). No scene, no state, so the same code
    /// measures agents and real users. World head pose H = O · h (origin · local head).
    /// </summary>
    public static class MotionMath
    {
        /// <summary>Angular velocity in deg/s, world frame, rotating from prev to cur in dt seconds.</summary>
        public static Vector3 AngularVelocity(Quaternion prev, Quaternion cur, float dt)
        {
            if (dt <= 0f) return Vector3.zero;
            var dq = (cur * Quaternion.Inverse(prev)).normalized;
            dq.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f; // shortest way round; also covers the q / -q double cover
            if (Mathf.Approximately(angle, 0f) || !IsFinite(axis)) return Vector3.zero;
            return axis * (angle / dt);
        }

        /// <summary>
        /// Splits one frame of head motion into the part caused by the origin moving (artificial
        /// locomotion) and the part caused by the head moving inside the origin (physical).
        /// artificial + physical == world head displacement. Origin scale is assumed to be 1.
        /// </summary>
        public static MotionSplit Split(Pose originPrev, Pose originCur, Vector3 headLocalPrev, Vector3 headLocalCur)
        {
            return new MotionSplit
            {
                artificialTranslation = World(originCur, headLocalPrev) - World(originPrev, headLocalPrev),
                physicalTranslation = World(originCur, headLocalCur) - World(originCur, headLocalPrev),
                artificialRotation = originCur.rotation * Quaternion.Inverse(originPrev.rotation),
            };
        }

        /// <summary>
        /// Projects a world angular velocity onto (yaw, pitch, roll): yaw about world up,
        /// pitch about the head's right axis, roll about the head's forward axis. deg/s in, deg/s out.
        /// </summary>
        public static Vector3 YawPitchRoll(Vector3 angularVelocityWorld, Quaternion headWorld)
        {
            return new Vector3(
                Vector3.Dot(angularVelocityWorld, Vector3.up),
                Vector3.Dot(angularVelocityWorld, headWorld * Vector3.right),
                Vector3.Dot(angularVelocityWorld, headWorld * Vector3.forward));
        }

        /// <summary>
        /// Hampel filter (Giannone et al. 2026: halfWindow 60, k 3). Samples further than
        /// k · 1.4826 · MAD from the window median are replaced by that median.
        /// Returns a new array; outliers = number of replaced samples.
        /// </summary>
        public static float[] Hampel(float[] x, int halfWindow, float k, out int outliers)
        {
            var y = (float[])x.Clone();
            outliers = 0;
            var window = new float[2 * halfWindow + 1];
            for (int i = 0; i < x.Length; i++)
            {
                int lo = Math.Max(0, i - halfWindow), hi = Math.Min(x.Length - 1, i + halfWindow), n = hi - lo + 1;
                Array.Copy(x, lo, window, 0, n);
                float median = Median(window, n);
                for (int j = 0; j < n; j++) window[j] = Math.Abs(x[lo + j] - median);
                float mad = 1.4826f * Median(window, n);
                if (Math.Abs(x[i] - median) > k * mad) { y[i] = median; outliers++; }
            }
            return y;
        }

        /// <summary>Centred moving average (use ~50–100 ms worth of samples). Edges use the samples available.</summary>
        public static float[] MovingAverage(float[] x, int halfWindow)
        {
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                int lo = Math.Max(0, i - halfWindow), hi = Math.Min(x.Length - 1, i + halfWindow);
                float sum = 0f;
                for (int j = lo; j <= hi; j++) sum += x[j];
                y[i] = sum / (hi - lo + 1);
            }
            return y;
        }

        static Vector3 World(Pose origin, Vector3 local) => origin.position + origin.rotation * local;

        static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        // ponytail: sorts a copy per call, O(n·w log w). Fine for offline traces; use a running median if it ever runs live.
        static float Median(float[] buffer, int n)
        {
            var s = new float[n];
            Array.Copy(buffer, s, n);
            Array.Sort(s);
            return n % 2 == 1 ? s[n / 2] : 0.5f * (s[n / 2 - 1] + s[n / 2]);
        }
    }

    public struct MotionSplit
    {
        public Vector3 artificialTranslation;
        public Vector3 physicalTranslation;
        public Quaternion artificialRotation;
    }

    public struct Episode
    {
        public float start, end, peak;
        public float Duration => end - start;
    }

    /// <summary>
    /// Streams one metric and finds episodes where a rule stays violated for at least minDuration
    /// (e.g. artificial speed > 1.4 m/s for > 1 s). An episode starts at the first violating sample
    /// and ends at the first non-violating one. Excluded samples (teleport frames) neither extend
    /// nor break an episode. flagBelow = true flags values below the threshold (too slow, too small).
    /// </summary>
    public sealed class EpisodeDetector
    {
        readonly float threshold, minDuration;
        readonly bool flagBelow;
        bool open;
        float start, peak, lastT;

        public EpisodeDetector(float threshold, float minDuration, bool flagBelow = false)
        {
            this.threshold = threshold;
            this.minDuration = minDuration;
            this.flagBelow = flagBelow;
        }

        /// <summary>True while a violation has lasted at least minDuration (for live display).</summary>
        public bool IsActive => open && lastT - start >= minDuration;

        /// <summary>Feed one sample. Returns true and the episode when a long-enough episode just ended.</summary>
        public bool Step(float t, float value, out Episode ended, bool excluded = false)
        {
            ended = default;
            if (excluded) return false;
            lastT = t;
            bool violated = flagBelow ? value < threshold : value > threshold;
            if (violated)
            {
                if (!open) { open = true; start = t; peak = value; }
                else peak = flagBelow ? Math.Min(peak, value) : Math.Max(peak, value);
                return false;
            }
            return Close(t, out ended);
        }

        /// <summary>Call at the end of a session to close an episode that is still running.</summary>
        public bool Flush(float t, out Episode ended) => Close(t, out ended);

        bool Close(float t, out Episode ended)
        {
            ended = new Episode { start = start, end = t, peak = peak };
            bool wasOpen = open;
            open = false;
            return wasOpen && ended.Duration >= minDuration;
        }
    }
}
