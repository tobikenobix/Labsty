using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace QAXR.Tests
{
    /// <summary>Each oracle against its own breaking point from the zone 5/6 builder.</summary>
    public class OracleTests
    {
        GameObject zone5, zone6;
        readonly List<Issue> reported = new List<Issue>();

        void Collect(Issue i) => reported.Add(i);

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            (zone5, zone6) = BreakingPointsBuilder.Build();
            Physics.SyncTransforms();
            reported.Clear();
            IssueSink.Reported += Collect;
        }

        [TearDown]
        public void TearDown() => IssueSink.Reported -= Collect;

        Transform Find(GameObject root, string path) => root.transform.Find(path) ?? throw new AssertionException($"missing {path}");

        [Test]
        public void AngularSize_Basics()
        {
            Assert.AreEqual(1.5f, BugOracles.AngularSize(2f * Mathf.Tan(0.75f * Mathf.Deg2Rad), 1f), 1e-3f);
            Assert.AreEqual(90f, BugOracles.AngularSize(2f, 1f), 1e-3f);
        }

        [Test]
        public void TooSmall_FlagsThe15DegreePanel_NotTheControl()
        {
            // 1 m in front of each panel's centre button, as the builder designed them
            float small = BugOracles.TargetAngularSize(Find(zone5, "Panel_1.5deg/Button_11").GetComponent<Renderer>().bounds,
                                                       zone5.transform.TransformPoint(new Vector3(1.5f, 1.5f, -4f)));
            float big = BugOracles.TargetAngularSize(Find(zone5, "Panel_3.8deg_Control/Button_11").GetComponent<Renderer>().bounds,
                                                     zone5.transform.TransformPoint(new Vector3(3.5f, 1.5f, -4f)));
            Assert.AreEqual(1.5f, small, 0.05f);
            Assert.AreEqual(3.8f, big, 0.05f);
            // oblique view: still the face size, not the 1 cm thickness
            float oblique = BugOracles.TargetAngularSize(Find(zone5, "Panel_1.5deg/Button_11").GetComponent<Renderer>().bounds,
                                                         zone5.transform.TransformPoint(new Vector3(2.5f, 1.5f, -4f)));
            Assert.AreEqual(1.5f / Mathf.Sqrt(2f), oblique, 0.05f);
        }

        [Test]
        public void TooSmall_FlagsTheTinySign_NotTheStationLabels()
        {
            var eye = zone5.transform.TransformPoint(new Vector3(-2f, 1.6f, 1f)); // walkway, ~4 m from the sign
            var texts = zone5.GetComponentsInChildren<TMP_Text>();
            var sign = texts.Single(t => t.text.StartsWith("EMERGENCY"));
            var label = texts.Single(t => t.text == "PRESS ME");
            Assert.Less(BugOracles.TextAngularHeight(sign, eye), 1f);
            Assert.Greater(BugOracles.TextAngularHeight(label, zone5.transform.TransformPoint(new Vector3(-3.5f, 1.6f, -4.5f))), 1f);
        }

        [Test]
        public void Unreachable_ShelfItemIsTooHigh_TableItemIsNot()
        {
            var shelfItem = Find(zone5, "KeyItem_HighShelf").position;
            var tableItem = Find(zone5, "Core_ThinWall").position;
            Assert.AreEqual(BugOracles.Reach.TooHigh, BugOracles.CanReach(Vector3.zero, shelfItem, 0f, 1.75f, 0.7f));
            Assert.AreEqual(BugOracles.Reach.TooHigh, BugOracles.CanReach(Vector3.zero, shelfItem, 0f, 1.55f, 0.62f));
            // no NavMesh baked in a test scene: the height check passes, then the path check reports it
            Assert.AreNotEqual(BugOracles.Reach.TooHigh, BugOracles.CanReach(Vector3.zero, tableItem, 0f, 1.55f, 0.62f));
        }

        [Test]
        public void TeleportIntoGeometry_DetectsTheBadAnchor_NotTheFloor()
        {
            var dest = Find(zone6, "Destination_InsideBlock").position;
            Assert.IsTrue(BugOracles.LandedInGeometry(dest, 1.6f, ~0));
            Assert.IsFalse(BugOracles.LandedInGeometry(zone6.transform.TransformPoint(new Vector3(2f, 0f, 3f)), 1.6f, ~0));
            Assert.IsTrue(BugOracles.LandedInGeometry(zone6.transform.TransformPoint(new Vector3(-2.5f, 0f, 0f)), 1.6f, ~0), "the hole has no ground");
        }

        [Test]
        public void HeldClipping_MeasuresPenetrationIntoTheThinWall()
        {
            var core = Find(zone5, "Core_ThinWall").gameObject;
            Assert.AreEqual(0f, BugOracles.HeldPenetration(core, ~0), 1e-4f);
            core.transform.position = Find(zone5, "ThinWall").position; // swung into the wall
            Physics.SyncTransforms();
            Assert.Greater(BugOracles.HeldPenetration(core, ~0), 0.01f);
        }

        [Test]
        public void Stuck_FiresOnceAfterTimeoutWithoutProgress()
        {
            var s = new StuckDetector { timeout = 3f, minProgress = 0.2f };
            Assert.IsFalse(s.Update(0f, 5f)); // no goal yet
            s.SetGoal(0f, 5f);
            Assert.IsFalse(s.Update(1f, 4f));    // progress resets the clock
            Assert.IsFalse(s.Update(3.5f, 3.95f)); // only 2.5 s since progress
            Assert.IsTrue(s.Update(4.1f, 3.9f));
            Assert.IsFalse(s.Update(6f, 3.9f));    // once per goal
        }

        [Test]
        public void Monitor_ReportsExceptionsOnceAndFallThroughAtTheHole()
        {
            var monitor = new GameObject("Monitor").AddComponent<QaxrOracleMonitor>();
            Invoke(monitor, "OnEnable"); // edit mode: no automatic lifecycle
            try
            {
                LogAssert.Expect(LogType.Error, "boom from a handler");
                LogAssert.Expect(LogType.Error, "boom from a handler");
                Debug.LogError("boom from a handler");
                Debug.LogError("boom from a handler");

                var crate = Find(zone6, "Crate_FallsOnPlay").GetComponent<Rigidbody>();
                Invoke(monitor, "Update"); // sees the crate above the floor
                crate.transform.position = new Vector3(crate.transform.position.x, -50f, crate.transform.position.z);
                Physics.SyncTransforms();
                SetField(monitor, "nextScan", 0f);
                Invoke(monitor, "Update");

                var exceptions = reported.Where(i => i.rule == "RuntimeException").ToList();
                Assert.AreEqual(1, exceptions.Count);
                Assert.AreEqual("boom from a handler", exceptions[0].detail);
                var fall = reported.Single(i => i.rule == "FallThrough");
                Assert.Greater(fall.Position.y, -1f, "reported where it left the floor, not at -50 m");
            }
            finally { Invoke(monitor, "OnDisable"); }
        }

        static void Invoke(object o, string method) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);
        static void SetField(object o, string field, object v) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, v);
    }

    public class ZoneTests
    {
        QaxrOrigin origin;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            origin = new GameObject("Agent").AddComponent<QaxrOrigin>();
            origin.head = new GameObject("Head").transform;
            origin.head.SetParent(origin.transform, false);
            origin.head.localPosition = new Vector3(0f, 1.6f, 0f);
            if (!QaxrOrigin.All.Contains(origin)) QaxrOrigin.All.Add(origin); // edit mode: OnEnable didn't run
        }

        [TearDown]
        public void TearDown() => QaxrOrigin.All.Clear();

        static T Zone<T>(Vector3 center, Vector3 size) where T : QaxrZone
        {
            var go = new GameObject(typeof(T).Name);
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return go.AddComponent<T>();
        }

        [Test]
        public void FastCorridor_RampsInBadState_JumpsToSafeSpeedWhenFixed_RestoresOnExit()
        {
            var z = Zone<FastCorridorZone>(new Vector3(0f, 1f, 0f), new Vector3(10f, 3f, 3f));
            z.Step(0.1f);
            Assert.AreEqual(1.4f + 1.5f * 0.1f, origin.zoneMoveSpeed, 1e-4f); // ghost: ramps from walking speed
            for (int i = 0; i < 40; i++) z.Step(0.1f);
            Assert.AreEqual(4f, origin.zoneMoveSpeed, 1e-4f);
            Assert.IsFalse(origin.zoneMitigation);

            z.IsFixed = true;
            z.Step(0.1f);
            Assert.AreEqual(1.4f, origin.zoneMoveSpeed, 1e-4f);
            Assert.IsTrue(origin.zoneMitigation);

            origin.transform.position = new Vector3(50f, 0f, 0f);
            z.Step(0.1f);
            Assert.AreEqual(-1f, origin.zoneMoveSpeed);
            Assert.IsFalse(origin.zoneMitigation);
        }

        [Test]
        public void Boat_CarriesOriginsAsIfParented_AndLevelsThemOnExit()
        {
            var z = Zone<RockingBoatZone>(new Vector3(0f, 1f, 0f), new Vector3(6f, 4f, 4f));
            z.deck = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            origin.transform.position = new Vector3(1.5f, 0f, 0.5f);
            z.Step(0f);
            var onDeck = z.deck.InverseTransformPoint(origin.transform.position);

            float maxRoll = 0f;
            for (int i = 0; i < 100; i++) // ~1.25 s at 80 fps: the first quarter period of 0.2 Hz
            {
                z.Step(1f / 80f);
                Assert.Less(Vector3.Distance(onDeck, z.deck.InverseTransformPoint(origin.transform.position)), 1e-3f, "slid on the deck");
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Mathf.DeltaAngle(0f, origin.transform.eulerAngles.z)));
            }
            Assert.Greater(maxRoll, 5f, "origin should roll with the deck");

            origin.transform.position = new Vector3(20f, 0f, 0f);
            z.Step(1f / 80f);
            Assert.Less(Vector3.Angle(origin.transform.up, Vector3.up), 0.01f, "levelled on exit");
        }

        [Test]
        public void Boat_Fixed_EasesToRest()
        {
            var z = Zone<RockingBoatZone>(Vector3.zero, Vector3.one);
            z.deck = new GameObject("Deck").transform;
            for (int i = 0; i < 100; i++) z.Step(0.05f);
            z.IsFixed = true;
            for (int i = 0; i < 100; i++) z.Step(0.05f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, z.deck.rotation), 0.01f);
        }

        [Test]
        public void Cutscene_TurnsAroundTheHead_FixedDoesNot()
        {
            var z = Zone<CutsceneZone>(new Vector3(0f, 1f, 0f), new Vector3(6f, 3f, 6f));
            origin.head.localPosition = new Vector3(0.3f, 1.6f, 0.2f);
            var headBefore = origin.head.position;
            for (int i = 0; i <= 40; i++) z.Step(0.1f); // 4 s > 3 s duration
            Assert.AreEqual(120f, Mathf.DeltaAngle(0f, origin.transform.eulerAngles.y), 0.5f);
            Assert.Less(Vector3.Distance(headBefore, origin.head.position), 1e-3f, "pivots around the head");
            Assert.AreEqual(-15f, Mathf.DeltaAngle(0f, origin.head.localEulerAngles.x), 0.5f, "camera overridden");

            // fixed: nothing moves, the pointer shows
            var fixedZone = Zone<CutsceneZone>(new Vector3(30f, 1f, 0f), new Vector3(6f, 3f, 6f));
            fixedZone.IsFixed = true;
            fixedZone.pointer = new GameObject("Pointer");
            fixedZone.pointer.SetActive(false);
            origin.transform.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.identity);
            for (int i = 0; i <= 40; i++) fixedZone.Step(0.1f);
            Assert.AreEqual(0f, origin.transform.eulerAngles.y, 1e-3f);
            Assert.IsTrue(fixedZone.pointer.activeSelf);
        }

        [Test]
        public void HeavyRoom_BuildsLoad_FixedSwitchesItOff()
        {
            var z = Zone<HeavyRoomZone>(Vector3.zero, Vector3.one);
            z.sphereSegments = 32;
            z.BuildLoad();
            var load = z.transform.Find("GeneratedLoad").gameObject;
            Assert.AreEqual(z.spheres + z.overdrawLayers + z.particleSystems, load.transform.childCount);
            z.IsFixed = true;
            Assert.IsFalse(load.activeSelf);
        }

        [Test]
        public void LiveTuner_ChangesZoneOneAndRemembersTheScoreBefore()
        {
            var z = Zone<FastCorridorZone>(Vector3.zero, Vector3.one);
            var tuner = new GameObject("Tuner").AddComponent<LiveTunerPanel>();
            tuner.Zone = z;
            float score = 0.62f;
            tuner.Score = () => score;
            tuner.Faster();
            Assert.AreEqual(4.5f, z.badSpeed, 1e-5f);
            Assert.AreEqual(0.62f, tuner.Before, 1e-5f);
            score = 0.2f;
            tuner.ToggleFixed();
            Assert.IsTrue(z.IsFixed);
            Assert.AreEqual(0.2f, tuner.Before, 1e-5f);
            for (int i = 0; i < 20; i++) tuner.Slower();
            Assert.AreEqual(0.5f, z.badSpeed, 1e-5f); // clamped
        }

        [Test]
        public void SicknessZonesBuilder_BuildsAllFourWithVolumes()
        {
            var zones = SicknessZonesBuilder.Build();
            Assert.AreEqual(4, zones.Length);
            Assert.IsInstanceOf<FastCorridorZone>(zones[0]);
            Assert.IsNotNull(((RockingBoatZone)zones[1]).deck);
            Assert.IsNotNull(((CutsceneZone)zones[2]).pointer);
            // the corridor volume covers a player walking down it at floor height
            Assert.IsTrue(zones[0].Contains(new Vector3(20f, 0f, 0f)));
            Assert.IsFalse(zones[0].Contains(Vector3.zero));
            Assert.IsTrue(zones[2].Contains(new Vector3(0f, 0f, -8f)));
        }
    }
}
