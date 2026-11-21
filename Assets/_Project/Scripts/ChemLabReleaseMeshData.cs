using UnityEngine;

namespace ChemLab
{
    /// <summary>
    /// Added by ChemLabMergeStatic to the merged lab meshes. They are only drawn, never read, so once loaded the CPU copy
    /// is dropped and only the GPU copy stays. Done at runtime: freeing it while the build processes the scene could
    /// serialize empty meshes.
    /// </summary>
    public class ChemLabReleaseMeshData : MonoBehaviour
    {
        void Awake()
        {
            foreach (var f in GetComponentsInChildren<MeshFilter>()) f.sharedMesh.UploadMeshData(true);
        }
    }
}
