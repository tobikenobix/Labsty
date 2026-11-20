using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace QAXR.Tests
{
    public class MotionMathTests
    {
        const float Eps = 1e-3f;

        static void Near(Vector3 expected, Vector3 actual, float eps = Eps)
            => Assert.That(Vector3.Distance(expected, actual), Is.LessThan(eps), $"expected {expected:F4}, got {actual:F4}");

        [Test]
        public void AngularVelocity_YawAndSign()
        {
            Near(new Vector3(0f, 90f, 0f), MotionMath.AngularVelocity(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 1f), 0.01f);
            Near(new Vector3(0f, -60f, 0f), MotionMath.AngularVelocity(Quaternion.identity, Quaternion.Euler(0f, -30f, 0f), 0.5f), 0.01f);
        }

        [Test]
        public void AngularVelocity_WrapsAcross180()
        {
            // 170° → -170° is a +20° turn, not -340°.
            var w = MotionMath.AngularVelocity(Quaternion.Euler(0f, 170f, 0f), Quaternion.Euler(0f, -170f, 0f), 0.1f);
            Near(new Vector3(0f, 200f, 0f), w, 0.05f);
        }

        [Test]
        public void AngularVelocity_ZeroForSameRotationAndDoubleCover()
        {
            var q = Quaternion.Euler(10f, 20f, 30f);
            var negated = new Quaternion(-q.x, -q.y, -q.z, -q.w); // same rotation
            Near(Vector3.zero, MotionMath.AngularVelocity(q, q, 0.01f));
            Near(Vector3.zero, MotionMath.AngularVelocity(q, negated, 0.01f));
            Near(Vector3.zero, MotionMath.AngularVelocity(q, Quaternion.Euler(0f, 90f, 0f), 0f)); // dt guard
        }

        [Test]
        public void Split_PureOriginTranslationIsArtificial()
        {
            var head = new Vector3(0f, 1.6f, 0.1f);
            var s = MotionMath.Split(new Pose(Vector3.zero, Quaternion.identity), new Pose(Vector3.right, Quaternion.identity), head, head);
            Near(Vector3.right, s.artificialTranslation);
            Near(Vector3.zero, s.physicalTranslation);
        }

        [Test]
        public void Split_OriginRotationMovesAnOffsetHead()
        {
            var head = new Vector3(0f, 1.6f, 0.5f);
            var rot = Quaternion.Euler(0f, 90f, 0f);
            var s = MotionMath.Split(new Pose(Vector3.zero, Quaternion.identity), new Pose(Vector3.zero, rot), head, head);
            Near(rot * head - head, s.artificialTranslation);
            Near(Vector3.zero, s.physicalTranslation);
            Assert.That(Quaternion.Angle(rot, s.artificialRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Split_PureHeadMotionIsPhysical_InOriginFrame()
        {
            var o = new Pose(new Vector3(3f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f));
            var s = MotionMath.Split(o, o, Vector3.zero, new Vector3(0f, 0f, 0.2f));
            Near(Vector3.zero, s.artificialTranslation);
            Near(o.rotation * new Vector3(0f, 0f, 0.2f), s.physicalTranslation);
        }

        [Test]
        public void Split_PartsAlwaysSumToWorldHeadDisplacement()
        {
            var rng = new System.Random(7);
            float R(float a) => (float)(rng.NextDouble() * 2 - 1) * a;
            for (int i = 0; i < 200; i++)
            {
                var o0 = new Pose(new Vector3(R(5), R(1), R(5)), Quaternion.Euler(R(30), R(180), R(30)));
                var o1 = new Pose(new Vector3(R(5), R(1), R(5)), Quaternion.Euler(R(30), R(180), R(30)));
                var h0 = new Vector3(R(0.5f), 1.6f + R(0.2f), R(0.5f));
                var h1 = new Vector3(R(0.5f), 1.6f + R(0.2f), R(0.5f));
                var s = MotionMath.Split(o0, o1, h0, h1);
                var world = (o1.position + o1.rotation * h1) - (o0.position + o0.rotation * h0);
                Near(world, s.artificialTranslation + s.physicalTranslation);
            }
        }

        [Test]
        public void YawPitchRoll_ProjectsOnWorldUpAndHeadAxes()
        {
            Near(new Vector3(30f, 0f, 0f), MotionMath.YawPitchRoll(new Vector3(0f, 30f, 0f), Quaternion.identity));
            // Head turned 90° right: head-right is world -Z, head-forward is world +X.
            var head = Quaternion.Euler(0f, 90f, 0f);
            Near(new Vector3(0f, 5f, 7f), MotionMath.YawPitchRoll(new Vector3(7f, 0f, -5f), head));
        }

        static List<Episode> Run(EpisodeDetector d, float hz, float seconds, System.Func<float, float> f, System.Func<float, bool> excluded = null)
        {
            var found = new List<Episode>();
            float dt = 1f / hz, t = 0f;
            for (int i = 0; t < seconds; i++, t = i * dt)
                if (d.Step(t, f(t), out var e, excluded != null && excluded(t))) found.Add(e);
            if (d.Flush(seconds, out var last)) found.Add(last);
            return found;
        }

        [Test]
        public void Episodes_LongViolationFound_ShortBurstIgnored()
        {
            // speed > 1.4 m/s from 1.0 s to 2.5 s (long) and from 4.0 s to 4.5 s (short burst)
            float Speed(float t) => (t >= 1f && t < 2.5f) ? 3f : (t >= 4f && t < 4.5f) ? 5f : 1f;
            var eps = Run(new EpisodeDetector(1.4f, 1f), 30f, 6f, Speed);
            Assert.AreEqual(1, eps.Count);
            Assert.AreEqual(1f, eps[0].start, 0.04f);
            Assert.AreEqual(2.5f, eps[0].end, 0.04f);
            Assert.AreEqual(3f, eps[0].peak, Eps);
        }

        [Test]
        public void Episodes_StillRunningAtEndIsFlushed_AndIsActiveLive()
        {
            var d = new EpisodeDetector(1.4f, 1f);
            d.Step(0f, 2f, out _);
            Assert.IsFalse(d.IsActive);
            d.Step(1.2f, 2f, out _);
            Assert.IsTrue(d.IsActive);
            Assert.IsTrue(d.Flush(2f, out var e));
            Assert.AreEqual(2f, e.Duration, Eps);
        }

        [Test]
        public void Episodes_ExcludedFramesDontBreakOrCount()
        {
            // a teleport spike at 1.5 s is excluded; the low-speed violation around it continues
            var d = new EpisodeDetector(0.5f, 1f, flagBelow: true);
            float Speed(float t) => t < 3f ? (Mathf.Abs(t - 1.5f) < 0.05f ? 100f : 0.2f) : 1f;
            var eps = Run(d, 30f, 4f, Speed, t => Mathf.Abs(t - 1.5f) < 0.05f);
            Assert.AreEqual(1, eps.Count);
            Assert.AreEqual(0.2f, eps[0].peak, Eps); // flagBelow keeps the minimum
            Assert.AreEqual(3f, eps[0].end, 0.04f);
        }

        [Test]
        public void Hampel_RemovesTrackingSpike_LeavesCleanSignal()
        {
            var x = new float[200];
            for (int i = 0; i < x.Length; i++) x[i] = Mathf.Sin(i * 0.05f) + 0.01f * ((i * 37) % 7 - 3);
            var clean = MotionMath.Hampel(x, 10, 3f, out int none);
            Assert.AreEqual(0, none);
            CollectionAssert.AreEqual(x, clean);

            x[100] += 50f; // a tracking jump
            var y = MotionMath.Hampel(x, 10, 3f, out int outliers);
            Assert.AreEqual(1, outliers);
            Assert.AreEqual(Mathf.Sin(100 * 0.05f), y[100], 0.1f);
        }

        [Test]
        public void MovingAverage_FlattensAlternatingNoise()
        {
            var y = MotionMath.MovingAverage(new[] { 0f, 2f, 0f, 2f, 0f, 2f, 0f }, 1);
            Assert.AreEqual(1f, y[0], Eps);          // edge: (0 + 2) / 2
            Assert.AreEqual(2f / 3f, y[1], Eps);     // (0 + 2 + 0) / 3
            Assert.AreEqual(4f / 3f, y[2], Eps);
        }
    }
}
