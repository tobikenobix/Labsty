using UnityEngine;

namespace QAXR
{
    /// <summary>
    /// The git commit for the "commit" field of issues.json (Roadmap §4.6). A Quest build can't run git,
    /// so CommitStamp writes Resources/qaxr_commit.txt at build time; in the Editor we ask git directly.
    /// </summary>
    public static class BuildInfo
    {
        static string commit;

        public static string Commit => commit ??= Load();

        static string Load()
        {
#if UNITY_EDITOR
            return ReadGit() ?? "unknown";
#else
            var stamp = Resources.Load<TextAsset>("qaxr_commit");
            return stamp ? stamp.text.Trim() : "unknown";
#endif
        }

#if UNITY_EDITOR
        /// <summary>Short hash, plus "-dirty" when there are uncommitted changes. Null if git isn't available.</summary>
        public static string ReadGit()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "describe --always --dirty --abbrev=7")
                {
                    WorkingDirectory = System.IO.Path.GetDirectoryName(Application.dataPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return p.ExitCode == 0 && output.Length > 0 ? output : null;
            }
            catch (System.Exception) { return null; } // git not installed or not on PATH
        }
#endif
    }
}
