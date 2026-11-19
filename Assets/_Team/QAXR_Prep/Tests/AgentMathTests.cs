using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QAXR.Tests
{
    public class AgentMathTests
    {
        static readonly Intent[] All = (Intent[])Enum.GetValues(typeof(Intent));

        [Test]
        public void EveryDefaultRowSumsToOne()
        {
            foreach (var holding in new[] { false, true })
            foreach (var intent in All)
                Assert.AreEqual(1f, TransitionTable.Default(intent, holding).Sum(), 1e-4f, $"{intent} holding={holding}");
        }

        [Test]
        public void DefaultRowIsAFreshCopy()
        {
            TransitionTable.Default(Intent.Idle, false)[1] = 99f;
            Assert.AreEqual(.35f, TransitionTable.Default(Intent.Idle, false)[(int)Intent.LookAround], 1e-6f);
        }

        [Test]
        public void NeverReturnsAnInvalidIntent()
        {
            var rng = new System.Random(1);
            foreach (var chaos in new[] { 0f, 0.3f, 1f })
            {
                var chain = new IntentChain(42, chaos);
                var cur = Intent.Idle;
                for (int i = 0; i < 5000; i++)
                {
                    int validMask = rng.Next(1, 1 << All.Length);
                    bool Valid(Intent x) => (validMask & (1 << (int)x)) != 0;
                    bool holding = rng.Next(2) == 0;
                    var row = TransitionTable.Default(cur, holding);
                    bool anyChoice = All.Any(x => Valid(x) && (chaos > 0f || row[(int)x] > 0f));
                    var next = chain.Next(cur, holding, Valid);
                    if (anyChoice) Assert.IsTrue(Valid(next), $"{cur} → {next} (invalid)");
                    else Assert.AreEqual(Intent.LookAround, next); // documented fallback: nothing to choose
                    cur = next;
                }
            }
        }

        [Test]
        public void SameSeedSameSequence()
        {
            Intent[] Sequence(int seed)
            {
                var chain = new IntentChain(seed);
                var seq = new Intent[200];
                var cur = Intent.Idle;
                for (int i = 0; i < seq.Length; i++) seq[i] = cur = chain.Next(cur, false, _ => true);
                return seq;
            }
            CollectionAssert.AreEqual(Sequence(1003), Sequence(1003));
            CollectionAssert.AreNotEqual(Sequence(1003), Sequence(1004));
        }

        [Test]
        public void ChaosZeroFollowsTheRow()
        {
            var chain = new IntentChain(5, chaos: 0f);
            var counts = new int[All.Length];
            const int n = 20000;
            for (int i = 0; i < n; i++) counts[(int)chain.Next(Intent.Idle, false, _ => true)]++;
            Assert.AreEqual(.35, counts[(int)Intent.LookAround] / (double)n, 0.015);
            Assert.AreEqual(.20, counts[(int)Intent.Move] / (double)n, 0.015);
            Assert.AreEqual(0, counts[(int)Intent.Grab]); // probability 0 in the Idle row
        }

        [Test]
        public void ChaosOneIsUniformOverValidIntents()
        {
            var chain = new IntentChain(9, chaos: 1f);
            bool Valid(Intent x) => x != Intent.Use && x != Intent.Release;
            var counts = new int[All.Length];
            const int n = 27000;
            for (int i = 0; i < n; i++) counts[(int)chain.Next(Intent.Reach, false, Valid)]++;
            foreach (var x in All)
                Assert.AreEqual(Valid(x) ? 1.0 / 9 : 0.0, counts[(int)x] / (double)n, 0.012, x.ToString());
        }

        [Test]
        public void MinJerk_EndpointsMidpointAndSymmetry()
        {
            Assert.AreEqual(0f, AgentSampling.MinJerk(0f), 1e-6f);
            Assert.AreEqual(1f, AgentSampling.MinJerk(1f), 1e-6f);
            Assert.AreEqual(0.5f, AgentSampling.MinJerk(0.5f), 1e-6f);
            Assert.AreEqual(1f, AgentSampling.MinJerk(0.2f) + AgentSampling.MinJerk(0.8f), 1e-5f);
            Assert.AreEqual(1f, AgentSampling.MinJerk(7f), 1e-6f); // clamped
            // zero start velocity: the first 1 % of time covers far less than 1 % of the distance
            Assert.Less(AgentSampling.MinJerk(0.01f), 0.001f);
        }

        [Test]
        public void Fitts_GrowsWithDistanceAndShrinksWithWidth()
        {
            Assert.Greater(AgentSampling.FittsTime(0.6f, 0.05f), AgentSampling.FittsTime(0.3f, 0.05f));
            Assert.Greater(AgentSampling.FittsTime(0.3f, 0.02f), AgentSampling.FittsTime(0.3f, 0.08f));
            Assert.AreEqual(0.1f + 0.15f * 3f, AgentSampling.FittsTime(0.4f, 0.1f), 1e-5f); // log2(8) = 3
            Assert.AreEqual(0.1f, AgentSampling.FittsTime(0f, 0.1f), 1e-6f);               // ID floored at 0
        }

        [Test]
        public void Lognormal_HasTheRequestedMedianAndIsPositive()
        {
            var rng = new System.Random(3);
            var xs = Enumerable.Range(0, 20001).Select(_ => AgentSampling.Lognormal(rng, 0.8f)).OrderBy(x => x).ToArray();
            Assert.Greater(xs[0], 0f);
            Assert.AreEqual(0.8f, xs[xs.Length / 2], 0.03f);
            Assert.Greater(xs.Average(), 0.8f); // right-skewed
        }

        [Test]
        public void ClippedNormal_StaysInBounds()
        {
            var rng = new System.Random(4);
            for (int i = 0; i < 5000; i++)
                Assert.That(AgentSampling.ClippedNormal(rng, 0f, 100f, -60f, 60f), Is.InRange(-60f, 60f));
        }

        [Test]
        public void SoftmaxPick_PrefersHigherScoresButNotAlways()
        {
            var rng = new System.Random(6);
            var negDistances = new[] { -1f, -3f, -6f };
            var counts = new int[3];
            for (int i = 0; i < 10000; i++) counts[AgentSampling.SoftmaxPick(rng, negDistances)]++;
            Assert.Greater(counts[0], counts[1]);
            Assert.Greater(counts[1], counts[2]);
            Assert.Greater(counts[2], 0);
            Assert.AreEqual(-1, AgentSampling.SoftmaxPick(rng, new float[0]));
        }
    }
}
