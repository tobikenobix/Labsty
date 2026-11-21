using System.Collections.Generic;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// The teammate's lab is ~2,000 separate meshes, so the lab scene issued ~2,300 draw calls per eye pass.
/// When the ChemistryLab scene is built (and when it enters Play Mode in the Editor) this merges the lab's
/// renderers per material and per 12 m floor cell into one mesh each. Colliders are left untouched.
/// Runs on the processed copy of the scene only: ChemistryLab.unity and the lab prefab are never modified.
/// </summary>
class ChemLabMergeStatic : IProcessSceneWithReport
{
    const string SceneName = "ChemistryLab";
    const string EnvName = "Lab Environment (teammate)";
    const float Cell = 12f;  // ponytail: floor is ~245 draws at 0° (100 lab materials + ~120 unmerged props); below that needs fewer materials, not a bigger cell

    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (scene.name != SceneName) return;
        var env = scene.GetRootGameObjects().Select(g => g.transform.Find(EnvName)).FirstOrDefault(t => t != null);
        if (env == null) return;

        var groups = new Dictionary<(Material, int, int), List<MeshRenderer>>();
        foreach (var r in env.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!r.enabled || mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount != 1 || r.sharedMaterials.Length != 1 || r.sharedMaterial == null) continue;
            if (r.transform.localToWorldMatrix.determinant < 0f) continue;   // mirrored: winding would flip
            var c = r.bounds.center;
            var key = (r.sharedMaterial, Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<MeshRenderer>();
            list.Add(r);
        }

        int before = 0, after = 0;
        var root = new GameObject("Merged Lab Meshes").transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        foreach (var g in groups)
        {
            var list = g.Value;
            if (list.Count == 1) { before++; after++; continue; }
            var combine = list.Select(r => new CombineInstance { mesh = r.GetComponent<MeshFilter>().sharedMesh, transform = r.transform.localToWorldMatrix }).ToArray();
            var mesh = new Mesh { name = $"Merged_{g.Key.Item1.name}_{g.Key.Item2}_{g.Key.Item3}" };
            long verts = combine.Sum(ci => (long)ci.mesh.vertexCount);
            if (verts > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(combine, true, true);   // ponytail: stays CPU-readable (~+20 MB RAM in the player); fine on Quest 3
            if (mesh.vertexCount != verts)
            {
                // Source meshes are Read/Write off. Their data is available when the scene is built or opened in the
                // Editor, but not when Play Mode loads the lab later (e.g. from the Lobby). Never drop geometry: stop here.
                Object.DestroyImmediate(mesh);
                Debug.Log("[ChemLab] Lab meshes not readable in this load path; merge skipped (build-time merge is unaffected).");
                break;
            }
            before += list.Count;
            after++;

            var go = new GameObject(mesh.name);
            go.transform.SetParent(root, false);
            go.layer = list[0].gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = g.Key.Item1;
            mr.shadowCastingMode = list[0].shadowCastingMode;
            mr.receiveShadows = list[0].receiveShadows;
            foreach (var r in list) Object.DestroyImmediate(r);
        }
        Debug.Log($"[ChemLab] Merged lab meshes: {before} renderers -> {after} ({groups.Count} material/cell groups).");
    }
}
