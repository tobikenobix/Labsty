using System;
using UnityEngine;

namespace QAXR
{
    /// <summary>The 11 frozen intents (Roadmap §4.2). The order is the column order of every transition row.</summary>
    public enum Intent { Idle, LookAround, Move, Teleport, SnapTurn, Reach, Grab, Use, Release, PointAtUI, Fidget }

    /// <summary>
    /// The starting transition matrix of §4.2 [ph]. Rows are looked up by (current intent, holding).
    /// While holding, Idle/LookAround/Move/Teleport/SnapTurn/Fidget/Grab use the Grab (holding) row,
    /// and Reach (toward a socket) uses the Reach (holding) row.
    /// </summary>
    public static class TransitionTable
    {
        static readonly int N = Enum.GetValues(typeof(Intent)).Length;

        /// <summary>Returns a fresh copy of the row, safe to modify.</summary>
        public static float[] Default(Intent cur, bool holding)
        {
            if (holding)
            {
                switch (cur)
                {
                    case Intent.Reach: return Row((Intent.Release, .80f), (Intent.LookAround, .20f));
                    case Intent.Use: return UseRow();
                    case Intent.Release: return ReleaseRow();
                    case Intent.PointAtUI: return PointAtUIRow();
                    default: return GrabHoldingRow();
                }
            }

            switch (cur)
            {
                case Intent.Idle:
                    return Row((Intent.LookAround, .35f), (Intent.Move, .20f), (Intent.Reach, .15f), (Intent.Teleport, .10f),
                               (Intent.SnapTurn, .10f), (Intent.PointAtUI, .05f), (Intent.Fidget, .05f));
                case Intent.LookAround:
                    return Row((Intent.Reach, .30f), (Intent.Idle, .20f), (Intent.Move, .20f), (Intent.Teleport, .10f),
                               (Intent.SnapTurn, .10f), (Intent.PointAtUI, .10f));
                case Intent.Move:
                case Intent.Teleport:
                case Intent.SnapTurn:
                    return Row((Intent.LookAround, .40f), (Intent.Reach, .30f), (Intent.Idle, .20f), (Intent.Move, .10f));
                case Intent.Reach:
                    return Row((Intent.Grab, .60f), (Intent.Idle, .20f), (Intent.LookAround, .20f));
                case Intent.Grab: // grab just failed, otherwise we'd be holding
                    return GrabHoldingRow();
                case Intent.Use: return UseRow();
                case Intent.Release: return ReleaseRow();
                case Intent.PointAtUI: return PointAtUIRow();
                case Intent.Fidget:
                    return Row((Intent.Idle, .50f), (Intent.LookAround, .50f));
                default:
                    throw new ArgumentOutOfRangeException(nameof(cur), cur, null);
            }
        }

        // Reach in this row means "reach to a socket"; the agent knows it is holding.
        static float[] GrabHoldingRow() => Row((Intent.Use, .30f), (Intent.LookAround, .20f), (Intent.Reach, .20f),
                                               (Intent.Move, .15f), (Intent.Release, .15f));
        static float[] UseRow() => Row((Intent.LookAround, .30f), (Intent.Release, .30f), (Intent.Use, .20f), (Intent.Reach, .20f));
        static float[] ReleaseRow() => Row((Intent.LookAround, .40f), (Intent.Idle, .30f), (Intent.Reach, .30f));
        static float[] PointAtUIRow() => Row((Intent.LookAround, .40f), (Intent.PointAtUI, .30f), (Intent.Idle, .30f));

        static float[] Row(params (Intent intent, float p)[] entries)
        {
            var row = new float[N];
            foreach (var (intent, p) in entries) row[(int)intent] = p;
            return row;
        }
    }

    /// <summary>
    /// One seeded Markov chain per agent with the chaos knob (§4.2): chaos 0 = realistic user,
    /// chaos 1 = monkey that picks uniformly among valid intents. Invalid intents never come out.
    /// </summary>
    public sealed class IntentChain
    {
        public readonly int Seed;
        public float Chaos;
        public readonly System.Random Rng;
        readonly Func<Intent, bool, float[]> rows;

        /// <param name="rows">Row source, e.g. AgentProfile.Row later. Must return a fresh copy. Defaults to TransitionTable.Default.</param>
        public IntentChain(int seed, float chaos = 0.1f, Func<Intent, bool, float[]> rows = null)
        {
            Seed = seed;
            Chaos = chaos;
            Rng = new System.Random(seed);
            this.rows = rows ?? TransitionTable.Default;
        }

        public Intent Next(Intent cur, bool holding, Func<Intent, bool> isValid)
        {
            float[] row = rows(cur, holding);
            int n = row.Length;
            float c = Mathf.Clamp01(Chaos), sum = 0f;
            for (int i = 0; i < n; i++)
            {
                row[i] = isValid((Intent)i) ? (1f - c) * row[i] + c / n : 0f;
                sum += row[i];
            }
            if (sum <= 0f) return Intent.LookAround; // nothing valid: look for something

            double r = Rng.NextDouble() * sum;
            int last = -1;
            for (int i = 0; i < n; i++)
            {
                if (row[i] <= 0f) continue;
                last = i;
                if ((r -= row[i]) <= 0) return (Intent)i;
            }
            return (Intent)last; // float round-off left a sliver: take the last valid intent, never an invalid one
        }
    }

    /// <summary>Human-like motion and timing (§4.2 Option A). All randomness goes through the agent's seeded Rng.</summary>
    public static class AgentSampling
    {
        /// <summary>Minimum-jerk profile (Flash & Hogan 1985): 0 → 1 with zero velocity and acceleration at both ends.</summary>
        public static float MinJerk(float s)
        {
            s = Mathf.Clamp01(s);
            return s * s * s * (10f + s * (-15f + 6f * s));
        }

        public static Vector3 MinJerk(Vector3 from, Vector3 to, float s) => Vector3.LerpUnclamped(from, to, MinJerk(s));

        public static Quaternion MinJerk(Quaternion from, Quaternion to, float s) => Quaternion.Slerp(from, to, MinJerk(s));

        /// <summary>Fitts' law movement time MT = a + b·log2(2D/W), seconds. Default a, b are [ph].</summary>
        public static float FittsTime(float distance, float width, float a = 0.1f, float b = 0.15f)
        {
            if (width <= 0f) throw new ArgumentOutOfRangeException(nameof(width));
            return a + b * Mathf.Log(Mathf.Max(2f * distance / width, 1f), 2f); // index of difficulty never below 0
        }

        /// <summary>Standard normal via Box–Muller.</summary>
        public static double Normal(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(); // (0, 1], avoids log(0)
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Normal(mean, sd) clipped to [min, max], e.g. head-turn amplitude within the DHM limits.</summary>
        public static float ClippedNormal(System.Random rng, float mean, float sd, float min, float max)
            => Mathf.Clamp(mean + sd * (float)Normal(rng), min, max);

        /// <summary>Right-skewed durations (dwell, reaction). median is the median in seconds; sigma the log-space spread [ph 0.5].</summary>
        public static float Lognormal(System.Random rng, float median, float sigma = 0.5f)
            => median * (float)Math.Exp(sigma * Normal(rng));

        /// <summary>
        /// Picks an index with probability ∝ exp(score / temperature). For target choice pass
        /// score = −distance over visible interactables: nearer wins more often, not always. −1 if empty.
        /// </summary>
        public static int SoftmaxPick(System.Random rng, float[] scores, float temperature = 1f)
        {
            if (scores == null || scores.Length == 0) return -1;
            float max = float.NegativeInfinity;
            foreach (var s in scores) max = Math.Max(max, s);
            var w = new double[scores.Length];
            double sum = 0;
            for (int i = 0; i < scores.Length; i++) sum += w[i] = Math.Exp((scores[i] - max) / Math.Max(temperature, 1e-6f));
            double r = rng.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++) if ((r -= w[i]) <= 0) return i;
            return w.Length - 1;
        }
    }
}
