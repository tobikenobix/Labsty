using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace QAXR
{
    /// <summary>
    /// Builds the bug test bed of Roadmap §5 (zones 5 and 6) into the open scene, from primitives and XRI
    /// components only. Every planted bug carries a QaxrBreakingPoint saying which oracle should catch it.
    /// Zone 5 (interaction bay) is a 10 × 10 m slab at z 5..15, zone 6 a 10 × 8 m slab at z 15..23, so they
    /// continue the 10 × 10 m floor of Empty.unity. Move the two roots freely. Bake the NavMesh afterwards
    /// with Render Meshes as geometry, otherwise the invisible doorway blocker gets baked out.
    /// </summary>
    public static class BreakingPointsBuilder
    {
        internal const float SlabThickness = 0.1f;
        const int WrongInteractionLayer = 31; // nothing else uses it, so the socket can never accept the core

        [MenuItem("QAXR/Prep/Build Zones 5 + 6 (breaking points)")]
        static void BuildMenu()
        {
            var (zone5, zone6) = Build();
            Undo.RegisterCreatedObjectUndo(zone5, "QAXR zone 5");
            Undo.RegisterCreatedObjectUndo(zone6, "QAXR zone 6");
            EditorSceneManager.MarkSceneDirty(zone5.scene);
            Selection.activeGameObject = zone5;
        }

        public static (GameObject zone5, GameObject zone6) Build()
        {
            var floorMat = Mat("Floor", new Color(0.55f, 0.56f, 0.58f));
            var wallMat = Mat("Wall", new Color(0.82f, 0.8f, 0.76f));
            var propMat = Mat("Prop", new Color(0.35f, 0.45f, 0.6f));
            var itemMat = Mat("Item", new Color(1f, 0.55f, 0.1f));
            return (Zone5(floorMat, wallMat, propMat, itemMat), Zone6(floorMat, wallMat, propMat, itemMat));
        }

        static GameObject Zone5(Material floor, Material wall, Material prop, Material item)
        {
            var root = new GameObject("QAXR_Zone5_InteractionBay");
            root.transform.position = new Vector3(0f, 0f, 10f);
            var t = root.transform;
            Teleportable(Box("Floor", t, new Vector3(0f, -SlabThickness / 2f, 0f), new Vector3(10f, SlabThickness, 10f), floor));

            // A. Runtime exception: a button whose handler throws.
            Box("Pedestal_ThrowingButton", t, new Vector3(-3.5f, 0.45f, -3f), new Vector3(0.4f, 0.9f, 0.4f), prop);
            var button = Box("ThrowingButton", t, new Vector3(-3.5f, 0.92f, -3f), new Vector3(0.12f, 0.04f, 0.12f), item);
            button.AddComponent<XRSimpleInteractable>();
            button.AddComponent<ThrowingButton>();
            Mark(button, Oracle.RuntimeException, "Pressing this button throws a NullReferenceException.");
            Label(t, "PRESS ME", new Vector3(-3.5f, 1.2f, -3f));

            // B. Interaction failed: a socket on an interaction layer the core doesn't have, with a core next to it.
            Box("Pedestal_Socket", t, new Vector3(-1.5f, 0.45f, -3f), new Vector3(0.4f, 0.9f, 0.4f), prop);
            var socket = new GameObject("WrongLayerSocket");
            socket.transform.SetParent(t, false);
            socket.transform.localPosition = new Vector3(-1.5f, 1.05f, -3f);
            var trigger = socket.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.12f;
            socket.AddComponent<XRSocketInteractor>().interactionLayers = 1 << WrongInteractionLayer;
            Mark(socket, Oracle.InteractionFailed, $"Socket listens on interaction layer {WrongInteractionLayer} only; the core next to it can never dock.");
            Box("Pedestal_Core", t, new Vector3(-0.9f, 0.45f, -3f), new Vector3(0.4f, 0.9f, 0.4f), prop);
            Grabbable("Core_ForSocket", t, new Vector3(-0.9f, 0.97f, -3f), item);
            Label(t, "DOCK THE CORE", new Vector3(-1.2f, 1.45f, -3f));

            // C. UI too small: 3 × 3 buttons of 1.5° seen from the viewing spot 1 m in front, plus a 3.8° control panel.
            ButtonPanel(t, "Panel_1.5deg", new Vector3(1.5f, 1.5f, -3f), 1.5f, prop, mark: true);
            ButtonPanel(t, "Panel_3.8deg_Control", new Vector3(3.5f, 1.5f, -3f), 3.8f, prop, mark: false);
            Box("ViewingSpot", t, new Vector3(2.5f, 0.005f, -4f), new Vector3(2.6f, 0.01f, 0.3f), item, collider: false);

            // D. Text too small: a sign with ~2 cm letters, read from the walkway ~4 m away.
            Box("SignPost", t, new Vector3(-4.6f, 0.8f, 4.6f), new Vector3(0.08f, 1.6f, 0.08f), prop);
            var sign = Text(t, "EMERGENCY EXIT  →  turn left after the shelf", new Vector3(-4f, 1.6f, 4.6f), 0.2f, 1.2f);
            Mark(sign.gameObject, Oracle.TextOrUiTooSmall, "Sign text is ~2 cm tall; from the walkway (≥ 4 m) it subtends well under 1°.");

            // E. Held object clipping: grab station with a 2 cm wall 15 cm past the table edge.
            Box("GrabTable", t, new Vector3(-3.5f, 0.375f, 1f), new Vector3(0.8f, 0.75f, 0.5f), prop);
            Grabbable("Core_ThinWall", t, new Vector3(-3.5f, 0.82f, 1f), item);
            var thin = Box("ThinWall", t, new Vector3(-3.5f, 1f, 1.4f), new Vector3(1.5f, 2f, 0.02f), wall);
            Mark(thin, Oracle.HeldObjectClipping, "2 cm wall right behind the grab table; a swung core passes through it.");

            // F. Unreachable: key item on a 2.2 m shelf, above the overhead reach of every default profile.
            Box("Shelf_Post_L", t, new Vector3(-0.45f, 1.1f, 1f), new Vector3(0.05f, 2.2f, 0.4f), prop);
            Box("Shelf_Post_R", t, new Vector3(0.45f, 1.1f, 1f), new Vector3(0.05f, 2.2f, 0.4f), prop);
            Box("Shelf_Board", t, new Vector3(0f, 2.18f, 1f), new Vector3(0.95f, 0.04f, 0.4f), prop);
            var key = Grabbable("KeyItem_HighShelf", t, new Vector3(0f, 2.25f, 1f), item);
            Mark(key, Oracle.Unreachable, "Key item at 2.2 m: above the overhead reach (≈ 1.22 × stature) of every profile up to 1.8 m.");

            // G. Stuck: a closet whose only doors are a 35 cm gap and a normal-looking door with an invisible blocker.
            // Closet occupies x 2..5, z 2..5. Wall height 2.4 m.
            Wall(t, "Closet_South", new Vector3(2f, 0f, 2f), new Vector3(5f, 0f, 2f), wall, doorCenter: 3.5f, doorWidth: 1f, out var southDoor);
            Wall(t, "Closet_West", new Vector3(2f, 0f, 2f), new Vector3(2f, 0f, 5f), wall, doorCenter: 3.5f, doorWidth: 0.35f, out var westDoor);
            Wall(t, "Closet_North", new Vector3(2f, 0f, 5f), new Vector3(5f, 0f, 5f), wall, 0f, 0f, out _);
            Wall(t, "Closet_East", new Vector3(5f, 0f, 2f), new Vector3(5f, 0f, 5f), wall, 0f, 0f, out _);
            var blocker = new GameObject("InvisibleBlocker");
            blocker.transform.SetParent(t, false);
            blocker.transform.localPosition = southDoor + Vector3.up * 1.05f;
            blocker.AddComponent<BoxCollider>().size = new Vector3(1f, 2.1f, 0.1f);
            Mark(blocker, Oracle.Stuck, "Door looks open (and is open on a render-mesh NavMesh), but an invisible collider blocks it.");
            var narrow = new GameObject("NarrowDoorway");
            narrow.transform.SetParent(t, false);
            narrow.transform.localPosition = westDoor + Vector3.up * 1.05f;
            Mark(narrow, Oracle.Stuck, "35 cm gap, narrower than any agent or person.");
            Grabbable("Core_InCloset", t, new Vector3(3.5f, 0.06f, 3.5f), item);
            Label(t, "STORAGE", southDoor + new Vector3(0f, 2.25f, -0.06f));

            return root;
        }

        static GameObject Zone6(Material floor, Material wall, Material prop, Material item)
        {
            var root = new GameObject("QAXR_Zone6_TeleportAndHole");
            root.transform.position = new Vector3(0f, 0f, 19f);
            var t = root.transform;

            // Floor with a 1.2 × 1.2 m hole at (-2.5, 0), built from four slabs around it.
            const float hx0 = -3.1f, hx1 = -1.9f, hz0 = -0.6f, hz1 = 0.6f;
            Teleportable(Slab("Floor_W", t, -5f, hx0, -4f, 4f, floor));
            Teleportable(Slab("Floor_E", t, hx1, 5f, -4f, 4f, floor));
            Teleportable(Slab("Floor_S", t, hx0, hx1, -4f, hz0, floor));
            Teleportable(Slab("Floor_N", t, hx0, hx1, hz1, 4f, floor));
            var patch = Slab("HolePatch_NoCollider", t, hx0, hx1, hz0, hz1, floor);
            Object.DestroyImmediate(patch.GetComponent<Collider>());
            Mark(patch, Oracle.FallThrough, "Floor patch renders but has no collider: anything stepping or dropped here falls out of the world.");
            var crate = Box("Crate_FallsOnPlay", t, new Vector3(-2.5f, 1.2f, 0f), new Vector3(0.3f, 0.3f, 0.3f), item);
            crate.AddComponent<Rigidbody>();

            // Teleport anchor whose destination is inside a solid block.
            var block = Box("SolidBlock", t, new Vector3(3f, 1.5f, 0f), new Vector3(1.6f, 3f, 1.6f), wall);
            var pad = Box("TeleportPad_IntoWall", t, new Vector3(0.8f, 0.01f, 0f), new Vector3(0.8f, 0.02f, 0.8f), prop);
            var destination = new GameObject("Destination_InsideBlock").transform;
            destination.SetParent(block.transform.parent, false);
            destination.localPosition = new Vector3(3f, 0f, 0f);
            pad.AddComponent<TeleportationAnchor>().teleportAnchorTransform = destination;
            Mark(pad, Oracle.TeleportIntoGeometry, "Teleport anchor lands the user inside the solid block next to it.");
            Label(t, "TELEPORT", new Vector3(0.8f, 0.6f, 0f));

            return root;
        }

        // ---- helpers ------------------------------------------------------------------------------------

        static void ButtonPanel(Transform parent, string name, Vector3 center, float degrees, Material mat, bool mark)
        {
            const float viewDistance = 1f;
            float size = 2f * viewDistance * Mathf.Tan(0.5f * degrees * Mathf.Deg2Rad); // 1.5° → 2.6 cm
            var panel = new GameObject(name).transform;
            panel.SetParent(parent, false);
            panel.localPosition = center;
            Box("Backboard", panel, new Vector3(0f, 0f, 0.02f), new Vector3(size * 5f, size * 5f, 0.02f), mat);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var b = Box($"Button_{row}{col}", panel, new Vector3((col - 1) * size * 1.5f, (1 - row) * size * 1.5f, 0f),
                            new Vector3(size, size, 0.01f), mat);
                b.AddComponent<XRSimpleInteractable>();
            }
            if (mark) Mark(panel.gameObject, Oracle.TextOrUiTooSmall, $"3 × 3 buttons of {degrees}° from 1 m (Kim et al. 2018: 1.9° already raised SSQ/VRSQ).");
            Label(parent, $"{degrees}°", center + new Vector3(0f, size * 4f, 0f));
        }

        /// <summary>A 2.4 m wall from a to b (local, on the floor), with an optional door gap centred doorCenter metres from a.</summary>
        internal static void Wall(Transform parent, string name, Vector3 a, Vector3 b, Material mat, float doorCenter, float doorWidth, out Vector3 door)
        {
            const float height = 2.4f, thickness = 0.1f, doorHeight = 2.1f;
            Vector3 dir = (b - a).normalized;
            float length = Vector3.Distance(a, b);
            door = a + dir * doorCenter;
            void Piece(string n, float from, float to, float y0, float y1)
            {
                if (to - from <= 0.001f) return;
                var p = Box(n, parent, a + dir * ((from + to) / 2f) + Vector3.up * ((y0 + y1) / 2f),
                            new Vector3(to - from, y1 - y0, thickness), mat);
                p.transform.localRotation = Quaternion.FromToRotation(Vector3.right, dir);
            }
            if (doorWidth <= 0f) { Piece(name, 0f, length, 0f, height); return; }
            float d0 = doorCenter - doorWidth / 2f, d1 = doorCenter + doorWidth / 2f;
            Piece(name + "_A", 0f, d0, 0f, height);
            Piece(name + "_B", d1, length, 0f, height);
            Piece(name + "_Lintel", d0, d1, doorHeight, height);
        }

        internal static GameObject Slab(string name, Transform parent, float x0, float x1, float z0, float z1, Material mat)
            => Box(name, parent, new Vector3((x0 + x1) / 2f, -SlabThickness / 2f, (z0 + z1) / 2f),
                   new Vector3(x1 - x0, SlabThickness, z1 - z0), mat);

        internal static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Grabbable(string name, Transform parent, Vector3 localPos, Material mat)
        {
            var go = Box(name, parent, localPos, new Vector3(0.1f, 0.1f, 0.1f), mat);
            go.AddComponent<XRGrabInteractable>(); // adds the Rigidbody it requires
            return go;
        }

        internal static void Teleportable(GameObject floor) => floor.AddComponent<TeleportationArea>();

        static void Mark(GameObject go, Oracle oracle, string description)
        {
            var bp = go.AddComponent<QaxrBreakingPoint>();
            bp.oracle = oracle;
            bp.description = description;
        }

        internal static void Label(Transform parent, string text, Vector3 localPos) => QaxrUi.Text(parent, text, localPos, 0.12f, 2f);

        static TextMeshPro Text(Transform parent, string text, Vector3 localPos, float fontSize, float width)
            => QaxrUi.Text(parent, text, localPos, fontSize / 10f, width);

        internal static Material Mat(string name, Color color)
        {
            string path = $"Assets/_Team/QAXR_Prep/Materials/QAXR_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            System.IO.Directory.CreateDirectory("Assets/_Team/QAXR_Prep/Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
