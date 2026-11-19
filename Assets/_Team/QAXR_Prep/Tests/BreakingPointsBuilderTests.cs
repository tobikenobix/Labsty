using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace QAXR.Tests
{
    public class BreakingPointsBuilderTests
    {
        [Test]
        public void BuildsEveryOracleCase_WithTheIntendedGeometry()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var (zone5, zone6) = BreakingPointsBuilder.Build();

            var points = Object.FindObjectsByType<QaxrBreakingPoint>(FindObjectsSortMode.None);
            foreach (Oracle o in System.Enum.GetValues(typeof(Oracle)))
                Assert.IsTrue(points.Any(p => p.oracle == o), $"no breaking point for {o}");

            // Unreachable: the key item rests at 2.2 m or higher.
            Assert.GreaterOrEqual(Find(zone5, "KeyItem_HighShelf").position.y, 2.2f);

            // 1.5° buttons: 2.6 cm wide, i.e. 1.5° from 1 m.
            float w = Find(zone5, "Panel_1.5deg/Button_00").lossyScale.x;
            Assert.AreEqual(1.5f, 2f * Mathf.Atan(w / 2f) * Mathf.Rad2Deg, 0.01f);

            // Wrong-layer socket really excludes the default interaction layer the grabbables use.
            var socket = Find(zone5, "WrongLayerSocket").GetComponent<XRSocketInteractor>();
            Assert.AreEqual(0, socket.interactionLayers.value & 1);

            // Tiny sign: under 1° from 4 m. Measures the real TMP mesh, so it also checks our font-size rule of thumb.
            var sign = zone5.GetComponentsInChildren<TextMeshPro>().Single(t => t.text.StartsWith("EMERGENCY"));
            sign.ForceMeshUpdate();
            float letterHeight = sign.textBounds.size.y;
            Assert.That(letterHeight, Is.InRange(0.005f, 0.04f), $"sign text height {letterHeight:F3} m");
            Assert.Less(2f * Mathf.Atan(letterHeight / 2f / 4f) * Mathf.Rad2Deg, 1f);

            // Fall-through: the patch has no collider and nothing else covers the hole.
            var patch = Find(zone6, "HolePatch_NoCollider");
            Assert.IsNull(patch.GetComponent<Collider>());
            Physics.SyncTransforms(); // edit mode: let physics see the freshly built colliders
            Assert.IsFalse(Physics.Raycast(patch.position + Vector3.up, Vector3.down, 50f));

            // Teleport into geometry: the destination sits inside the block's collider.
            var anchor = Find(zone6, "TeleportPad_IntoWall").GetComponent<TeleportationAnchor>();
            var block = Find(zone6, "SolidBlock").GetComponent<Collider>();
            var dest = anchor.teleportAnchorTransform.position + Vector3.up * 1.6f; // head height after landing
            Assert.IsTrue(block.bounds.Contains(dest));

            // Stuck: the open-looking door is blocked by a collider that has no renderer.
            var blocker = Find(zone5, "InvisibleBlocker");
            Assert.IsNotNull(blocker.GetComponent<Collider>());
            Assert.IsNull(blocker.GetComponent<Renderer>());
        }

        static Transform Find(GameObject root, string path)
        {
            var t = root.transform.Find(path);
            Assert.IsNotNull(t, $"missing {root.name}/{path}");
            return t;
        }
    }
}
