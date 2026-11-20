using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace QAXR
{
    /// <summary>What locomotion the agent or user asked for this frame (Docs~/AgentSpec.md). None + origin moving = uncontrolled motion (D2).</summary>
    [Flags]
    public enum LocoCommand { None = 0, Move = 1, Turn = 2, SnapTurn = 4, Teleport = 8 }

    /// <summary>One row of an agent trace. Head and hand poses are local to the origin.</summary>
    public struct TraceSample
    {
        public float t;
        public Intent intent;
        public string target, held;
        public LocoCommand loco;
        public Pose origin, head, leftHand, rightHand;
        public bool selectLeft, selectRight, activateLeft, activateRight;

        public Pose HeadWorld => new Pose(origin.position + origin.rotation * head.position, origin.rotation * head.rotation);

        /// <summary>Pose interpolation for replay; discrete fields come from a (the earlier sample).</summary>
        public static TraceSample Lerp(TraceSample a, TraceSample b, float u)
        {
            var s = a;
            s.t = Mathf.LerpUnclamped(a.t, b.t, u);
            s.origin = Lerp(a.origin, b.origin, u);
            s.head = Lerp(a.head, b.head, u);
            s.leftHand = Lerp(a.leftHand, b.leftHand, u);
            s.rightHand = Lerp(a.rightHand, b.rightHand, u);
            return s;
        }

        static Pose Lerp(Pose a, Pose b, float u) => new Pose(Vector3.Lerp(a.position, b.position, u), Quaternion.Slerp(a.rotation, b.rotation, u));
    }

    public static class QaxrPaths
    {
        /// <summary>persistentDataPath/qaxr/&lt;run&gt;, created if missing. Same place and adb pull flow as StepLogger.</summary>
        public static string RunDir(string run)
        {
            var dir = Path.Combine(Application.persistentDataPath, "qaxr", run);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string NewRunId() => DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes the qaxr_trace_v1 CSV of Docs~/AgentSpec.md. Call Write every frame; it keeps 30 Hz on its own.</summary>
    public sealed class TraceWriter : IDisposable
    {
        public const float Hz = 30f;
        public const string Format = "qaxr_trace_v1";

        public static readonly string[] Columns = BuildColumns();

        readonly TextWriter writer;
        float nextT = float.NegativeInfinity;

        /// <param name="header">key=value pairs for the # header (run, agent, seed, profile, mode, embodiment, chaos, scene, commit).</param>
        public TraceWriter(TextWriter writer, IDictionary<string, string> header)
        {
            this.writer = writer;
            writer.WriteLine($"# format={Format}");
            writer.WriteLine($"# hz={Hz.ToString(CultureInfo.InvariantCulture)}");
            foreach (var kv in header) writer.WriteLine($"# {kv.Key}={Clean(kv.Value)}");
            writer.WriteLine(string.Join(",", Columns));
        }

        public static TraceWriter Open(string path, IDictionary<string, string> header)
            => new TraceWriter(new StreamWriter(path, false, new UTF8Encoding(false)), header);

        /// <summary>Writes the sample if 1/Hz has passed since the last written one. Returns true if written.</summary>
        public bool Write(in TraceSample s)
        {
            if (s.t < nextT) return false;
            nextT = (nextT == float.NegativeInfinity ? s.t : nextT) + 1f / Hz;
            if (nextT <= s.t) nextT = s.t + 1f / Hz; // after a hitch, don't burst to catch up
            WriteRow(s);
            return true;
        }

        /// <summary>Writes unconditionally (e.g. the frame an issue fires).</summary>
        public void WriteRow(in TraceSample s)
        {
            var sb = new StringBuilder(256);
            sb.Append(F(s.t, "F4")).Append(',').Append(s.intent).Append(',')
              .Append(Clean(s.target)).Append(',').Append(Clean(s.held)).Append(',').Append((int)s.loco);
            Append(sb, s.origin); Append(sb, s.head); Append(sb, s.leftHand); Append(sb, s.rightHand);
            sb.Append(',').Append(B(s.selectLeft)).Append(',').Append(B(s.selectRight))
              .Append(',').Append(B(s.activateLeft)).Append(',').Append(B(s.activateRight));
            writer.WriteLine(sb.ToString());
        }

        public void Flush() => writer.Flush();
        public void Dispose() => writer.Dispose();

        static void Append(StringBuilder sb, Pose p)
        {
            sb.Append(',').Append(F(p.position.x, "F4")).Append(',').Append(F(p.position.y, "F4")).Append(',').Append(F(p.position.z, "F4"));
            sb.Append(',').Append(F(p.rotation.x, "F5")).Append(',').Append(F(p.rotation.y, "F5"))
              .Append(',').Append(F(p.rotation.z, "F5")).Append(',').Append(F(p.rotation.w, "F5"));
        }

        static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
        static char B(bool b) => b ? '1' : '0';
        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ").Replace("\r", "");

        static string[] BuildColumns()
        {
            var cols = new List<string> { "t", "intent", "target", "held", "loco" };
            foreach (var p in new[] { "origin", "head", "lhand", "rhand" })
                foreach (var c in new[] { "px", "py", "pz", "qx", "qy", "qz", "qw" })
                    cols.Add($"{p}_{c}");
            cols.AddRange(new[] { "sel_l", "sel_r", "act_l", "act_r" });
            return cols.ToArray();
        }
    }

    /// <summary>Reads a qaxr_trace_v1 CSV back, for replay (J6), D4 desk replay and tests.</summary>
    public sealed class Trace
    {
        public readonly Dictionary<string, string> Header = new Dictionary<string, string>();
        public readonly List<TraceSample> Samples = new List<TraceSample>();

        public static Trace Load(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            return Parse(reader);
        }

        public static Trace Parse(TextReader reader)
        {
            var trace = new Trace();
            string line;
            int lineNo = 0;
            bool seenColumns = false;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) trace.Header[line.Substring(1, eq - 1).Trim()] = line.Substring(eq + 1).Trim();
                    continue;
                }
                if (!seenColumns)
                {
                    if (line != string.Join(",", TraceWriter.Columns))
                        throw new FormatException($"line {lineNo}: unexpected columns, expected {TraceWriter.Format}");
                    seenColumns = true;
                    continue;
                }
                trace.Samples.Add(ParseRow(line, lineNo));
            }
            if (trace.Header.TryGetValue("format", out var f) && f != TraceWriter.Format)
                throw new FormatException($"format {f}, expected {TraceWriter.Format}");
            return trace;
        }

        /// <summary>The interpolated sample at time t (clamped to the trace). Binary search, so scrubbing is cheap.</summary>
        public TraceSample Evaluate(float t)
        {
            if (Samples.Count == 0) throw new InvalidOperationException("empty trace");
            if (t <= Samples[0].t) return Samples[0];
            int last = Samples.Count - 1;
            if (t >= Samples[last].t) return Samples[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Samples[mid].t <= t) lo = mid; else hi = mid;
            }
            var a = Samples[lo];
            var b = Samples[hi];
            return TraceSample.Lerp(a, b, (t - a.t) / Mathf.Max(b.t - a.t, 1e-6f));
        }

        static TraceSample ParseRow(string line, int lineNo)
        {
            var c = line.Split(',');
            if (c.Length != TraceWriter.Columns.Length)
                throw new FormatException($"line {lineNo}: {c.Length} fields, expected {TraceWriter.Columns.Length}");
            int i = 0;
            float F() => float.Parse(c[i++], NumberStyles.Float, CultureInfo.InvariantCulture);
            Pose P() => new Pose(new Vector3(F(), F(), F()), new Quaternion(F(), F(), F(), F()));
            bool B() => c[i++] == "1";
            var s = new TraceSample { t = F() };
            s.intent = (Intent)Enum.Parse(typeof(Intent), c[i++]);
            s.target = c[i++];
            s.held = c[i++];
            s.loco = (LocoCommand)int.Parse(c[i++], CultureInfo.InvariantCulture);
            s.origin = P(); s.head = P(); s.leftHand = P(); s.rightHand = P();
            s.selectLeft = B(); s.selectRight = B(); s.activateLeft = B(); s.activateRight = B();
            return s;
        }
    }
}
