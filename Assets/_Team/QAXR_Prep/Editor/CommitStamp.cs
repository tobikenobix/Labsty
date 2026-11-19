using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace QAXR
{
    /// <summary>Writes the git commit into Resources before every player build, so BuildInfo.Commit works on the Quest (§4.6).</summary>
    class CommitStamp : IPreprocessBuildWithReport
    {
        // ponytail: hard-coded to this folder; move it with the folder when QAXR_Prep is merged. The file is gitignored.
        const string StampPath = "Assets/_Team/QAXR_Prep/Resources/qaxr_commit.txt";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string commit = BuildInfo.ReadGit() ?? "unknown";
            Directory.CreateDirectory(Path.GetDirectoryName(StampPath));
            File.WriteAllText(StampPath, commit);
            AssetDatabase.ImportAsset(StampPath);
            UnityEngine.Debug.Log($"[QAXR] Build stamped with commit {commit}");
        }
    }
}
