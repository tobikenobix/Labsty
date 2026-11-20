// Editor-only helper used while setting up the lab materials.
// It watches <project>/Temp/claude/cmd.txt and runs simple commands:
//   dump                                   -> Temp/claude/renderers.txt (every renderer + its materials)
//   snap <name> x y z yaw pitch [fov]      -> Temp/claude/<name>.png  (camera render of the scene)
//   apply <file>                           -> applies lines "path|slot|Assets/....mat" to scene renderers
//   save                                   -> saves the open scene
// Safe to delete once the lab looks the way you want.
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class LabMaterialTool
{
    static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "claude"));
    static readonly string Cmd = Path.Combine(Dir, "cmd.txt");
    static double next;

    static LabMaterialTool()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(Cmd)) return;
        string[] lines;
        try { lines = File.ReadAllLines(Cmd); File.Delete(Cmd); } catch { return; }
        var log = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            try { Run(line, log); log.AppendLine("ok: " + line); }
            catch (Exception e) { log.AppendLine("ERROR in '" + line + "': " + e); }
        }
        File.WriteAllText(Path.Combine(Dir, "result.txt"), log.ToString());
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static void Run(string line, StringBuilder log)
    {
        var a = line.Split(' ');
        switch (a[0])
        {
            case "dump": Dump(); break;
            case "snap":
                Snap(a[1], new Vector3(F(a[2]), F(a[3]), F(a[4])), F(a[5]), F(a[6]), a.Length > 7 ? F(a[7]) : 75f);
                break;
            case "apply": Apply(line.Substring(6).Trim(), log); break;
            case "save":
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                break;
            default: throw new Exception("unknown command");
        }
    }

    static float F(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }

    static void Dump()
    {
        var sb = new StringBuilder();
        var all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var b = new Bounds();
        bool first = true;
        foreach (var r in all.OrderBy(r => PathOf(r.transform)))
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            var mf = r.GetComponent<MeshFilter>();
            var mats = string.Join(", ", r.sharedMaterials.Select(m => m == null ? "<none>" : AssetDatabase.GetAssetPath(m).Replace("Assets/3D Models/", "") + "#" + m.name));
            sb.AppendFormat("{0} | mesh={1} | c={2} s={3} | {4}\n", PathOf(r.transform),
                mf && mf.sharedMesh ? mf.sharedMesh.name : "-", r.bounds.center.ToString("F2"), r.bounds.size.ToString("F2"), mats);
        }
        sb.Insert(0, "SCENE BOUNDS c=" + b.center.ToString("F2") + " s=" + b.size.ToString("F2") + "\n");
        File.WriteAllText(Path.Combine(Dir, "renderers.txt"), sb.ToString());
    }

    static void Snap(string name, Vector3 pos, float yaw, float pitch, float fov)
    {
        var go = new GameObject("__ClaudeSnapCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // file lines: <exact hierarchy path>|<slot index or *>|<Assets/... .mat>
    static void Apply(string file, StringBuilder log)
    {
        var byPath = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .GroupBy(r => PathOf(r.transform)).ToDictionary(g => g.Key, g => g.ToArray());
        int changed = 0;
        foreach (var raw in File.ReadAllLines(file))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            var p = l.Split('|');
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p[2]);
            if (mat == null) { log.AppendLine("missing material " + p[2]); continue; }
            Renderer[] rs;
            if (!byPath.TryGetValue(p[0], out rs)) { log.AppendLine("missing renderer " + p[0]); continue; }
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (p[1] == "*" || p[1] == i.ToString()) mats[i] = mat;
                Undo.RecordObject(r, "Claude lab materials");
                r.sharedMaterials = mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                changed++;
            }
        }
        EditorSceneManager.MarkAllScenesDirty();
        log.AppendLine("renderers changed: " + changed);
    }
}
