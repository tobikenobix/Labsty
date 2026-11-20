#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ChemLab
{
    /// <summary>
    /// Testing aid: press C to tint every solid collider in the scene (teal = room mesh, orange = boxes and other
    /// shapes). Anything you can see but that stays untinted has no collider, so you can walk or reach through it.
    /// Created automatically in the Editor and in development builds; stripped from release builds.
    /// </summary>
    public class ColliderView : MonoBehaviour
    {
        static bool on;
        Collider[] colliders = new Collider[0];
        Material meshMat, shapeMat;
        Mesh cube, sphere, capsule;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("ColliderView (C)");
            DontDestroyOnLoad(go);
            go.AddComponent<ColliderView>();
        }

        void Awake()
        {
            var shader = Shader.Find("Sprites/Default");   // alpha-blended, no culling, ships with every project
            meshMat = new Material(shader) { color = new Color(0f, 0.9f, 0.8f, 0.22f) };
            shapeMat = new Material(shader) { color = new Color(1f, 0.45f, 0f, 0.4f) };
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            SceneManager.sceneLoaded += (_, _) => Refresh();
            Debug.Log("[ChemLab] Press C to show / hide colliders.");
        }

        void Refresh() => colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) { on = !on; Refresh(); }
            if (!on) return;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger || c is CharacterController) continue;
                var t = c.transform.localToWorldMatrix;
                switch (c)
                {
                    case MeshCollider m when m.sharedMesh != null:
                        for (int i = 0; i < m.sharedMesh.subMeshCount; i++) Graphics.RenderMesh(new RenderParams(meshMat), m.sharedMesh, i, t);
                        break;
                    case BoxCollider b:
                        Graphics.RenderMesh(new RenderParams(shapeMat), cube, 0, t * Matrix4x4.TRS(b.center, Quaternion.identity, b.size));
                        break;
                    case SphereCollider s:
                        Graphics.RenderMesh(new RenderParams(shapeMat), sphere, 0, t * Matrix4x4.TRS(s.center, Quaternion.identity, Vector3.one * s.radius * 2f));
                        break;
                    case CapsuleCollider k:   // ponytail: drawn upright (Y axis) and stretched; enough to see where it is
                        var d = k.radius * 2f;
                        Graphics.RenderMesh(new RenderParams(shapeMat), capsule, 0, t * Matrix4x4.TRS(k.center, Quaternion.identity, new Vector3(d, k.height / 2f, d)));
                        break;
                }
            }
        }
    }
}
#endif
