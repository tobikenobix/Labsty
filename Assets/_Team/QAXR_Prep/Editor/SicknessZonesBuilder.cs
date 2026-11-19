using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static QAXR.BreakingPointsBuilder;

namespace QAXR
{
    /// <summary>
    /// Builds sickness zones 1–4 of Roadmap §5 around the 10 × 10 m floor at the origin (zones 5–6 continue it at +z):
    /// zone 1 corridor at +x (x 5..35), zone 2 boat at -x (x -13..-5), zone 3 cutscene room at -z (z -11..-5),
    /// zone 4 heavy room at -x/-z (x -10..-3, z -11..-5). Each zone's `fixed` toggle is on its root.
    /// </summary>
    public static class SicknessZonesBuilder
    {
        [MenuItem("QAXR/Prep/Build Zones 1–4 (sickness)")]
        static void BuildMenu()
        {
            var zones = Build();
            foreach (var z in zones) Undo.RegisterCreatedObjectUndo(z.gameObject, "QAXR sickness zones");
            EditorSceneManager.MarkSceneDirty(zones[0].gameObject.scene);
            Selection.activeGameObject = zones[0].gameObject;
        }

        public static QaxrZone[] Build()
        {
            var floor = Mat("Floor", new Color(0.55f, 0.56f, 0.58f));
            var wall = Mat("Wall", new Color(0.82f, 0.8f, 0.76f));
            var prop = Mat("Prop", new Color(0.35f, 0.45f, 0.6f));
            var item = Mat("Item", new Color(1f, 0.55f, 0.1f));
            var water = Mat("Water", new Color(0.15f, 0.35f, 0.55f));
            return new QaxrZone[] { Corridor(floor, wall), Boat(floor, prop, water), Cutscene(floor, item), HeavyRoom(floor) };
        }

        static FastCorridorZone Corridor(Material floor, Material wall)
        {
            var t = Root("QAXR_Zone1_FastCorridor", new Vector3(20f, 0f, 0f));
            Slab("Floor", t, -15f, 15f, -1.5f, 1.5f, floor);
            Box("Wall_N", t, new Vector3(0f, 1.5f, 1.55f), new Vector3(30f, 3f, 0.1f), wall);
            Box("Wall_S", t, new Vector3(0f, 1.5f, -1.55f), new Vector3(30f, 3f, 0.1f), wall);
            Box("Wall_End", t, new Vector3(15.05f, 1.5f, 0f), new Vector3(0.1f, 3f, 3.2f), wall);
            Sign(t, "ZONE 1  FAST CORRIDOR", new Vector3(-14.8f, 2.5f, 0f), 90f);
            return Volume<FastCorridorZone>(t, new Vector3(0f, 1.25f, 0f), new Vector3(30f, 3f, 3f));
        }

        static RockingBoatZone Boat(Material floor, Material prop, Material water)
        {
            var t = Root("QAXR_Zone2_RockingBoat", new Vector3(-10f, 0f, 0f));
            Teleportable(Slab("Pier", t, 3f, 5f, -1f, 1f, floor));
            var deck = Box("Deck", t, new Vector3(0f, -0.05f, 0f), new Vector3(6f, 0.1f, 4f), prop);
            Teleportable(deck);
            Box("Mast", deck.transform, new Vector3(0f, 15f, 0f), new Vector3(0.02f, 30f, 0.02f), prop); // child of a scaled deck: 0.12 × 3 × 0.08 m
            Box("Water", t, new Vector3(0f, -0.6f, 0f), new Vector3(14f, 0.02f, 10f), water, collider: false);
            Sign(t, "ZONE 2  BOAT", new Vector3(3.2f, 2.2f, 0f), -90f);
            var zone = Volume<RockingBoatZone>(t, new Vector3(0f, 1.25f, 0f), new Vector3(6.4f, 3.5f, 4.4f));
            zone.deck = deck.transform;
            return zone;
        }

        static CutsceneZone Cutscene(Material floor, Material item)
        {
            var t = Root("QAXR_Zone3_Cutscene", new Vector3(0f, 0f, -8f));
            Teleportable(Slab("Floor", t, -3f, 3f, -3f, 3f, floor));
            var focus = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            focus.name = "CutsceneFocus";
            focus.transform.SetParent(t, false);
            focus.transform.localPosition = new Vector3(4f, 1.5f, 1f);
            focus.GetComponent<Renderer>().sharedMaterial = item;
            Sign(t, "ZONE 3  CUTSCENE", new Vector3(0f, 2.5f, 2.9f), 180f);
            var pointer = QaxrUi.Text(t, "Something is happening on your left  ←", new Vector3(0f, 1.6f, -2.5f), 0.1f, 4f);
            pointer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            pointer.gameObject.SetActive(false);
            var zone = Volume<CutsceneZone>(t, new Vector3(0f, 1.25f, 0f), new Vector3(6f, 3f, 6f));
            zone.pointer = pointer.gameObject;
            return zone;
        }

        static HeavyRoomZone HeavyRoom(Material floor)
        {
            var t = Root("QAXR_Zone4_HeavyRoom", new Vector3(-6.5f, 0f, -8f));
            Teleportable(Slab("Floor", t, -3.5f, 3.5f, -3f, 3f, floor));
            Sign(t, "ZONE 4  HEAVY ROOM (load spawns in Play mode)", new Vector3(0f, 2.6f, 2.9f), 180f);
            return Volume<HeavyRoomZone>(t, new Vector3(0f, 1.5f, 0f), new Vector3(7f, 3f, 6f));
        }

        static Transform Root(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            return go.transform;
        }

        static T Volume<T>(Transform root, Vector3 center, Vector3 size) where T : QaxrZone
        {
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = center;
            box.size = size;
            return root.gameObject.AddComponent<T>();
        }

        static void Sign(Transform parent, string text, Vector3 localPos, float yaw)
            => QaxrUi.Text(parent, text, localPos, 0.15f, 4f).transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }
}
