using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QAXR.Tests
{
    public class TraceTests
    {
        static TraceSample Sample(float t, int i) => new TraceSample
        {
            t = t,
            intent = (Intent)(i % 11),
            target = i % 2 == 0 ? "EnergyCore" : "",
            held = i % 3 == 0 ? "Core, with comma" : "",
            loco = (LocoCommand)(i % 16),
            origin = new Pose(new Vector3(i, 0f, -i * 0.5f), Quaternion.Euler(0f, i * 10f, 0f)),
            head = new Pose(new Vector3(0.01f * i, 1.6f, 0.05f), Quaternion.Euler(i, i * 2f, 0f)),
            leftHand = new Pose(new Vector3(-0.2f, 1.1f, 0.3f), Quaternion.identity),
            rightHand = new Pose(new Vector3(0.2f, 1.1f, 0.3f), Quaternion.Euler(0f, 0f, 30f)),
            selectLeft = i % 2 == 1, selectRight = i % 3 == 1, activateLeft = false, activateRight = i == 4,
        };

        [Test]
        public void RoundTrip_KeepsEveryField()
        {
            var sw = new StringWriter();
            var w = new TraceWriter(sw, new Dictionary<string, string> { ["agent"] = "3", ["seed"] = "1003", ["scene"] = "QAXR_Demo" });
            for (int i = 0; i < 6; i++) w.WriteRow(Sample(i * 0.1f, i));
            var trace = Trace.Parse(new StringReader(sw.ToString()));

            Assert.AreEqual("qaxr_trace_v1", trace.Header["format"]);
            Assert.AreEqual("1003", trace.Header["seed"]);
            Assert.AreEqual(6, trace.Samples.Count);
            for (int i = 0; i < 6; i++)
            {
                var a = Sample(i * 0.1f, i);
                var b = trace.Samples[i];
                Assert.AreEqual(a.intent, b.intent);
                Assert.AreEqual(a.target, b.target);
                Assert.AreEqual(a.held?.Replace(",", ";") ?? "", b.held); // commas can't survive CSV
                Assert.AreEqual(a.loco, b.loco);
                Assert.AreEqual(a.selectLeft, b.selectLeft);
                Assert.AreEqual(a.activateRight, b.activateRight);
                Assert.Less(Vector3.Distance(a.HeadWorld.position, b.HeadWorld.position), 1e-3f);
                Assert.Less(Quaternion.Angle(a.rightHand.rotation, b.rightHand.rotation), 0.01f);
            }
        }

        [Test]
        public void Write_Keeps30Hz_AndDoesntBurstAfterAHitch()
        {
            var sw = new StringWriter();
            var w = new TraceWriter(sw, new Dictionary<string, string>());
            int written = 0;
            for (int f = 0; f < 90 * 2; f++) if (w.Write(Sample(f / 90f, f))) written++; // 2 s at 90 fps
            Assert.AreEqual(60, written, 1);
            Assert.IsTrue(w.Write(Sample(5f, 0)));   // 3 s hitch
            Assert.IsFalse(w.Write(Sample(5.01f, 0))); // no catch-up burst
        }

        [Test]
        public void Evaluate_InterpolatesAndClamps()
        {
            var trace = new Trace();
            var a = Sample(0f, 0);
            var b = Sample(1f, 0);
            b.origin.position = new Vector3(2f, 0f, 0f);
            trace.Samples.Add(a);
            trace.Samples.Add(b);
            Assert.AreEqual(1f, trace.Evaluate(0.5f).origin.position.x, 1e-5f);
            Assert.AreEqual(0f, trace.Evaluate(-3f).origin.position.x, 1e-5f);
            Assert.AreEqual(2f, trace.Evaluate(9f).origin.position.x, 1e-5f);
        }

        [Test]
        public void Parse_RejectsForeignColumns()
        {
            Assert.Throws<System.FormatException>(() => Trace.Parse(new StringReader("t,x,y\n0,1,2\n")));
        }
    }

    public class RiskTests
    {
        [Test]
        public void SeverityBands_FollowTheExceedanceRatio()
        {
            Assert.AreEqual(Severity.None, RiskScoring.FromExceedance(0.9f));
            Assert.AreEqual(Severity.Low, RiskScoring.FromExceedance(1.2f));
            Assert.AreEqual(Severity.Medium, RiskScoring.FromExceedance(1.8f));
            Assert.AreEqual(Severity.High, RiskScoring.FromExceedance(2.5f));
            // §4.6 example: uncontrolled rotation 38.2 deg/s against 5 → High
            var rules = RiskRuleTable.Defaults();
            Assert.AreEqual(Severity.High, RiskScoring.Rate(rules.Single(r => r.id == "UncontrolledRotation"), 38.2f));
            // flag-below: a 1.5° button against 2° → r = 1.33 → Low
            Assert.AreEqual(Severity.Low, RiskScoring.Rate(rules.Single(r => r.id == "UITargetTooSmall"), 1.5f));
            Assert.AreEqual(Severity.None, RiskScoring.Rate(rules.Single(r => r.id == "UITargetTooSmall"), 3.8f));
            // boolean rules use their own severity
            Assert.AreEqual(Severity.Low, RiskScoring.Rate(rules.Single(r => r.id == "GazeDwellSelection"), 1f));
            Assert.AreEqual(Severity.None, RiskScoring.Rate(rules.Single(r => r.id == "GazeDwellSelection"), 0f));
        }

        [Test]
        public void DefaultRules_AreWellFormed()
        {
            var rules = RiskRuleTable.Defaults();
            Assert.AreEqual(rules.Count, rules.Select(r => r.id).Distinct().Count(), "duplicate rule ids");
            var dims = new[] { "D1", "D2", "D3", "D4", "D5", "Usability" };
            foreach (var r in rules)
            {
                Assert.Contains(r.dimension, dims, r.id);
                Assert.That(r.evidence, Is.EqualTo("lit").Or.EqualTo("ph"), r.id);
                Assert.Greater(r.threshold, 0f, r.id);
            }
            foreach (var d in dims) Assert.IsTrue(rules.Any(r => r.dimension == d), $"no rule for {d}");
        }

        [Test]
        public void RuleDetector_UsesTheRuleDirectionAndDuration()
        {
            var slow = RiskRuleTable.Defaults().Single(r => r.id == "LocomotionTooSlow").NewDetector();
            for (int i = 0; i <= 45; i++) slow.Step(i / 30f, 0.3f, out _); // 1.5 s at 0.3 m/s
            Assert.IsTrue(slow.IsActive);
        }

        static Issue E(string rule, Vector3 p, float t, int agent, Severity s = Severity.Low)
            => new Issue { rule = rule, Position = p, time = t, agent = agent, severity = s.ToString(), value = (int)s };

        [Test]
        public void Clusterer_MergesNearbyEpisodesOfTheSameRule()
        {
            var c = new IssueClusterer();
            c.Add(E("ArtificialSpeed", Vector3.zero, 10f, 1), out bool n1);
            c.Add(E("ArtificialSpeed", new Vector3(0.3f, 0f, 0f), 11.5f, 2, Severity.High), out bool n2); // near: merge
            c.Add(E("ArtificialSpeed", new Vector3(3f, 0f, 0f), 11f, 3), out bool n3);                  // far: new
            c.Add(E("UncontrolledRotation", Vector3.zero, 10f, 1), out bool n4);                        // other rule: new
            c.Add(E("ArtificialSpeed", Vector3.zero, 20f, 1), out bool n5);                              // later: new
            Assert.AreEqual(new[] { true, false, true, true, true }, new[] { n1, n2, n3, n4, n5 });
            var first = c.Clusters[0];
            Assert.AreEqual(2, first.hits);
            Assert.AreEqual(new List<int> { 1, 2 }, first.agents);
            Assert.AreEqual("High", first.severity); // worst wins
        }

        [Test]
        public void ZoneGrid_IsTheSeverityWeightedShareOfAgentsPassingThrough()
        {
            var g = new ZoneRiskGrid();
            var p = new Vector3(2.5f, 0f, 3.5f);
            for (int a = 0; a < 4; a++) g.Visit(a, p);
            g.Flag(0, p, Severity.High);
            g.Flag(0, p, Severity.Low);   // same agent, worst kept
            g.Flag(1, p, Severity.Medium);
            Assert.AreEqual((1f + 2f / 3f) / 4f, g.Risk(g.Cell(p)), 1e-5f);
            Assert.AreEqual(0f, g.Risk(g.Cell(new Vector3(9f, 0f, 9f))));
            Assert.AreEqual(new Vector2Int(2, 3), g.Cell(p));
        }

        [Test]
        public void RollingAverage_ForgetsOldValues()
        {
            var r = new RollingAverage(5f);
            for (int i = 0; i <= 50; i++) r.Add(i * 0.1f, 1f); // 5 s of risk 1
            Assert.AreEqual(1f, r.Value, 1e-4f);
            for (int i = 51; i <= 150; i++) r.Add(i * 0.1f, 0f); // then 10 s of 0
            Assert.AreEqual(0f, r.Value, 1e-4f);
            for (int i = 151; i <= 175; i++) r.Add(i * 0.1f, 1f); // 2.5 s of 1 in a 5 s window
            Assert.AreEqual(0.5f, r.Value, 0.03f);
        }

        [Test]
        public void IssueJson_MatchesTheRoadmapShape()
        {
            var i = new Issue { id = "D2-uncontrolled-0007", dimension = "D2", rule = "UncontrolledRotation", severity = "High",
                                value = 38.2f, threshold = 5f, unit = "deg/s", time = 41.7f, agent = 3, seed = 1003, Position = new Vector3(3.1f, 1.6f, -4.2f) };
            var json = JsonUtility.ToJson(i);
            StringAssert.IsMatch(@"""position"":\[3\.(1|09)", json); // an array, like the §4.6 example (float prints as 3.0999…)
            StringAssert.Contains("\"rule\":\"UncontrolledRotation\"", json);
        }
    }
}
