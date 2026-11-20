// Builds a real-world-scale, textured lab environment from Assets/3D Models/Lab.fbx.
//
//   Tools > Lab Environment > Build or Rebuild Lab Environment
//   Tools > Lab Environment > Reset Lab Materials to Defaults
//   Tools > Lab Environment > Render Preview Images
//
// What it does:
//   1. Textures: normal maps flagged as Normal Map, alpha textures as transparency.
//   2. Materials: one URP Lit material per FBX material in "Assets/3D Models/Lab Materials"
//      (base color / normal / emission / alpha set up from LabMaterialDefs.json).
//   3. Model import: scale x1.25 to real-world size, generated colliders, no animation,
//      FBX materials remapped to the materials above.
//   4. Prefab variant "Assets/3D Models/Lab Environment.prefab": static flags, ceiling and
//      full-height walls don't cast shadows (Quest shadow distance is only 2.5 m).
//   5. Scene (BasicScene): replaces the old Lab instance with the variant, levelled
//      (the Blender export is tilted ~0.5 deg), long axis along +Z, floor at y = 0 and the
//      XR Origin standing just inside the entrance; floor becomes the teleport area; the
//      100 m grid plane is disabled; indoor lighting and a baked reflection probe are set up.
//   6. Renders preview PNGs + report.txt to <project>/Logs/LabPreview.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LabTools
{
    [Serializable]
    public class LabMatDef
    {
        public string name, baseMap, normalMap, emissionMap, surface, category;
        public float[] baseColor, emissionColor, tiling;
        public float cutoff = 0.5f, smoothness = 0.2f, metallic;
        public bool doubleSided;
    }

    [Serializable]
    public class LabMatDefs
    {
        public LabMatDef[] materials;
        public string[] normalMaps;
        public string[] alphaTextures;
    }

    public static class LabPaths
    {
        public const string Model = "Assets/3D Models/Lab.fbx";
        public const string Textures = "Assets/3D Models/Textures";
        public const string Materials = "Assets/3D Models/Lab Materials";
        public const string DefsFile = "Assets/Editor/LabEnvironment/LabMaterialDefs.json";
        public const string Prefab = "Assets/3D Models/Lab Environment.prefab";
        public const string Scene = "Assets/Scenes/BasicScene.unity";
        public const string ProbeTexture = "Assets/Scenes/BasicScene/LabReflectionProbe.exr";
        public const string PreviewDir = "Logs/LabPreview";
        public const string AutoRunTrigger = "Temp/LabBuilder.autorun";

        static LabMatDefs s_Defs;

        public static LabMatDefs Defs
        {
            get
            {
                if (s_Defs == null && File.Exists(DefsFile))
                    s_Defs = JsonUtility.FromJson<LabMatDefs>(File.ReadAllText(DefsFile));
                return s_Defs;
            }
        }
    }

    /// <summary>Sets correct import settings the first time a lab texture is imported.</summary>
    class LabTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetImporter.importSettingsMissing || !assetPath.StartsWith(LabPaths.Textures + "/")) return;
            var defs = LabPaths.Defs;
            if (defs == null) return;
            var rel = assetPath.Substring(LabPaths.Textures.Length + 1);
            var ti = (TextureImporter)assetImporter;
            if (Array.IndexOf(defs.normalMaps, rel) >= 0) ti.textureType = TextureImporterType.NormalMap;
            else if (Array.IndexOf(defs.alphaTextures, rel) >= 0) ti.alphaIsTransparency = true;
            ti.anisoLevel = 4;
        }
    }

    [InitializeOnLoad]
    public static class LabEnvironmentBuilder
    {
        /// <summary>The Blender export is ~80% of real-world size (outlet plates, fume-hood work surface).</summary>
        public const float RealWorldScale = 1.25f;
        /// <summary>Distance from the entrance wall where the XR Origin (world origin) ends up.</summary>
        const float StartDistanceFromEntrance = 1.6f;

        static readonly StringBuilder s_Log = new StringBuilder();

        static LabEnvironmentBuilder()
        {
            if (!File.Exists(LabPaths.AutoRunTrigger)) return;
            // Never run during Play Mode: wait until the editor is back in Edit Mode.
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredEditMode) TryAutoRun();
            };
            EditorApplication.delayCall += TryAutoRun;
        }

        static void TryAutoRun()
        {
            if (!File.Exists(LabPaths.AutoRunTrigger) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(LabPaths.AutoRunTrigger);
            Build(true);
        }

        [MenuItem("Tools/Lab Environment/Build or Rebuild Lab Environment")]
        static void BuildMenu() => Build(false);

        [MenuItem("Tools/Lab Environment/Reset Lab Materials to Defaults")]
        static void ResetMaterialsMenu()
        {
            s_Log.Clear();
            BuildMaterials(true);
            AssetDatabase.SaveAssets();
            Debug.Log("[Lab] Materials reset.\n" + s_Log);
        }

        [MenuItem("Tools/Lab Environment/Render Preview Images")]
        static void PreviewMenu()
        {
            s_Log.Clear();
            var env = FindEnvironmentInstance(SceneManager.GetActiveScene());
            if (env == null) { Debug.LogWarning("[Lab] No 'Lab Environment' in the open scene."); return; }
            RenderPreviews(MeasureRoom(env));
            WriteReport();
        }

        public static void Build(bool resetMaterials)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Lab] Exit Play Mode first."); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => Build(resetMaterials);
                return;
            }

            s_Log.Clear();
            var t0 = DateTime.Now;
            Log($"Lab environment build started {t0:yyyy-MM-dd HH:mm:ss} (scale x{RealWorldScale})");
            try
            {
                if (LabPaths.Defs == null) throw new FileNotFoundException(LabPaths.DefsFile);
                Progress("Configuring textures", 0.05f);
                ConfigureTextures();
                Progress("Creating materials", 0.15f);
                var mats = BuildMaterials(resetMaterials);
                Progress("Re-importing Lab.fbx (scale, colliders, materials)", 0.25f);
                ConfigureModelImporter(mats);
                Progress("Building prefab variant", 0.55f);
                var prefab = BuildPrefab();
                Progress("Setting up scene", 0.75f);
                var room = SetupScene(prefab);
                Progress("Rendering previews", 0.9f);
                RenderPreviews(room);
                Log($"DONE in {(DateTime.Now - t0).TotalSeconds:0.0}s");
            }
            catch (Exception e)
            {
                Log("ERROR: " + e);
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                WriteReport();
            }
        }

        // ------------------------------------------------------------------ textures

        static void ConfigureTextures()
        {
            var defs = LabPaths.Defs;
            int changed = 0;
            var missing = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var d in defs.materials)
                foreach (var f in new[] { d.baseMap, d.normalMap, d.emissionMap })
                    if (!string.IsNullOrEmpty(f) && AssetImporter.GetAtPath($"{LabPaths.Textures}/{f}") == null)
                        missing.Add(f);

                foreach (var f in defs.normalMaps)
                    if (AssetImporter.GetAtPath($"{LabPaths.Textures}/{f}") is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.SaveAndReimport();
                        changed++;
                    }

                foreach (var f in defs.alphaTextures)
                    if (AssetImporter.GetAtPath($"{LabPaths.Textures}/{f}") is TextureImporter ti && !ti.alphaIsTransparency)
                    {
                        ti.alphaIsTransparency = true;
                        ti.SaveAndReimport();
                        changed++;
                    }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Log($"Textures: {defs.normalMaps.Length} normal maps, {defs.alphaTextures.Length} alpha textures, {changed} importer changes");
            if (missing.Count > 0) Log("  MISSING textures: " + string.Join(", ", missing.Distinct()));
        }

        // ------------------------------------------------------------------ materials

        static Dictionary<string, Material> BuildMaterials(bool reset)
        {
            EnsureFolder(LabPaths.Materials);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("URP Lit shader not found");

            var result = new Dictionary<string, Material>();
            int created = 0, updated = 0;
            foreach (var d in LabPaths.Defs.materials)
            {
                var path = $"{LabPaths.Materials}/{SafeFileName(d.name)}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader) { name = d.name };
                    ApplyDef(mat, d);
                    AssetDatabase.CreateAsset(mat, path);
                    created++;
                }
                else if (reset)
                {
                    mat.shader = shader;
                    ApplyDef(mat, d);
                    EditorUtility.SetDirty(mat);
                    updated++;
                }

                result[d.name] = mat;
            }

            AssetDatabase.SaveAssets();
            Log($"Materials: {created} created, {updated} reset, {result.Count - created - updated} kept as-is ({LabPaths.Materials})");
            return result;
        }

        static void ApplyDef(Material m, LabMatDef d)
        {
            Texture2D Tex(string f) => string.IsNullOrEmpty(f) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{LabPaths.Textures}/{f}");

            var baseTex = Tex(d.baseMap);
            var color = d.baseColor != null && d.baseColor.Length >= 3
                ? new Color(d.baseColor[0], d.baseColor[1], d.baseColor[2], d.baseColor.Length > 3 ? d.baseColor[3] : 1f)
                : Color.white;
            var tiling = d.tiling != null && d.tiling.Length == 2 ? new Vector2(d.tiling[0], d.tiling[1]) : Vector2.one;
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
            {
                m.SetTexture(p, baseTex);
                m.SetTextureScale(p, tiling);
            }
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);

            var normal = Tex(d.normalMap);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            SetKeyword(m, "_NORMALMAP", normal != null);

            m.SetFloat("_Smoothness", d.smoothness);
            m.SetFloat("_Metallic", d.metallic);
            m.SetFloat("_SmoothnessTextureChannel", 0f);

            var emissionTex = Tex(d.emissionMap);
            var ec = d.emissionColor != null && d.emissionColor.Length >= 3 ? new Color(d.emissionColor[0], d.emissionColor[1], d.emissionColor[2]) : Color.black;
            bool emissive = ec.maxColorComponent > 0f;
            m.SetTexture("_EmissionMap", emissionTex);
            m.SetColor("_EmissionColor", emissive ? ec : Color.black);
            SetKeyword(m, "_EMISSION", emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            bool transparent = d.surface == "transparent", cutout = d.surface == "cutout";
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", d.cutoff);
            m.SetFloat("_Cull", d.doubleSided ? 0f : 2f);
            m.SetFloat("_CastShadows", transparent ? 0f : 1f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            m.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(m, "_ALPHATEST_ON", cutout);
            SetKeyword(m, "_ALPHAPREMULTIPLY_ON", false);
            m.SetShaderPassEnabled("DepthOnly", !transparent);
            m.SetShaderPassEnabled("ShadowCaster", !transparent);

            UrpValidate(m);
        }

        /// <summary>Lets URP's own material GUI code finalise keywords/passes (same as opening it in the Inspector).</summary>
        static void UrpValidate(Material m)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Unity.RenderPipelines.Universal.Editor");
                var baseGui = asm?.GetType("UnityEditor.BaseShaderGUI");
                var setKw = baseGui?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(mi => mi.Name == "SetMaterialKeywords" && mi.GetParameters().Length == 3);
                setKw?.Invoke(null, new object[] { m, null, null });
            }
            catch (Exception e)
            {
                Log("  (URP keyword validation skipped: " + e.GetBaseException().Message + ")");
            }
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        // ------------------------------------------------------------------ model import

        static void ConfigureModelImporter(Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(LabPaths.Model);
            if (mi == null) throw new Exception("Model importer not found for " + LabPaths.Model);

            var embedded = AssetDatabase.LoadAllAssetsAtPath(LabPaths.Model).OfType<Material>().Select(m => m.name).ToList();

            mi.useFileScale = true;
            mi.globalScale = RealWorldScale;
            mi.addCollider = true;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(LabPaths.Model);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var stillEmbedded = renderers.SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && AssetDatabase.GetAssetPath(m) == LabPaths.Model)
                .Select(m => m.name).Distinct().ToList();
            Log($"Model: {LabPaths.Model} scale x{RealWorldScale}, colliders on, animation off; {renderers.Length} renderers, " +
                $"{mats.Count} materials remapped ({embedded.Count} embedded before)");
            var notInDefs = embedded.Where(n => !mats.ContainsKey(n)).ToList();
            if (notInDefs.Count > 0) Log("  FBX materials with no definition: " + string.Join(", ", notInDefs));
            if (stillEmbedded.Count > 0) Log("  Renderers still using embedded (untextured) materials: " + string.Join(", ", stillEmbedded));
        }

        // ------------------------------------------------------------------ prefab variant

        static GameObject BuildPrefab()
        {
            if (!File.Exists(LabPaths.Prefab))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(LabPaths.Model);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "Lab Environment";
                PrefabUtility.SaveAsPrefabAsset(inst, LabPaths.Prefab);
                Object.DestroyImmediate(inst);
            }

            var root = PrefabUtility.LoadPrefabContents(LabPaths.Prefab);
            try
            {
                var frame = FindFloorFrame(root);
                float ceiling = 3f;
                var ceilingGo = FindByPrefix(root, "Plane020");
                if (ceilingGo != null && frame.found)
                    ceiling = Vector3.Dot(ceilingGo.GetComponent<Renderer>().bounds.center - frame.center, frame.up);

                const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                int noShadow = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var b = r.bounds;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                        float h = frame.found ? Vector3.Dot(corner - frame.center, frame.up) : corner.y;
                        lo = Mathf.Min(lo, h);
                        hi = Mathf.Max(hi, h);
                    }

                    var mf = r.GetComponent<MeshFilter>();
                    int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                    bool ceilingLevel = lo > ceiling - 0.35f;
                    bool fullHeightPanel = hi - lo > 0.9f * ceiling && verts <= 400; // walls, end panels
                    var mode = ceilingLevel || fullHeightPanel ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    if (mode == ShadowCastingMode.Off) noShadow++;
                    r.shadowCastingMode = mode;
                }

                PrefabUtility.SaveAsPrefabAsset(root, LabPaths.Prefab);
                Log($"Prefab variant: {LabPaths.Prefab} (static batching/occlusion/reflection-probe static; {noShadow} ceiling/wall renderers don't cast shadows; ceiling height {ceiling:0.00} m)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(LabPaths.Prefab);
        }

        // ------------------------------------------------------------------ scene

        public struct RoomInfo
        {
            public bool valid;
            public float length, width, height; // metres, after scaling
            public float entranceZ, farZ;        // world Z of the entrance wall and far wall
        }

        struct FloorFrame
        {
            public bool found;
            public GameObject go;
            public Vector3 center, up, forward;
            public float length, width;
        }

        static FloorFrame FindFloorFrame(GameObject root)
        {
            var f = new FloorFrame();
            var floor = FindByPrefix(root, "Plane018");
            var mf = floor != null ? floor.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return f;

            var t = floor.transform;
            var b = mf.sharedMesh.bounds;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var world = axes.Select((a, i) => t.TransformVector(a * b.size[i])).ToArray();
            var order = Enumerable.Range(0, 3).OrderBy(i => world[i].magnitude).ToArray(); // thin, mid, long

            f.found = true;
            f.go = floor;
            f.center = t.TransformPoint(b.center);
            f.up = t.TransformDirection(axes[order[0]]).normalized;
            if (f.up.y < 0) f.up = -f.up;
            f.length = world[order[2]].magnitude;
            f.width = world[order[1]].magnitude;
            var fwd = world[order[2]];
            f.forward = (fwd - f.up * Vector3.Dot(fwd, f.up)).normalized;

            // Point "forward" away from the entrance door so the player starts at the door looking in.
            var door = FindByPrefix(root, "Object1111191742") ?? FindByPrefix(root, "Plane033");
            if (door != null)
            {
                var dr = door.GetComponent<Renderer>();
                var dp = dr != null ? dr.bounds.center : door.transform.position;
                if (Vector3.Dot(dp - f.center, f.forward) > 0) f.forward = -f.forward;
            }

            return f;
        }

        static RoomInfo SetupScene(GameObject prefab)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != LabPaths.Scene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException("Scene switch cancelled");
                scene = EditorSceneManager.OpenScene(LabPaths.Scene, OpenSceneMode.Single);
            }

            // Remove the previously placed Lab model and earlier builds.
            int removed = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                bool isLab = src != null && AssetDatabase.GetAssetPath(src) == LabPaths.Model;
                if (isLab || go.name == "Lab Environment" || go.name == "Lab Reflection Probe")
                {
                    Object.DestroyImmediate(go);
                    removed++;
                }
            }

            var env = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            env.name = "Lab Environment";
            env.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            env.transform.localScale = Vector3.one;

            var frame = FindFloorFrame(env);
            if (!frame.found) throw new Exception("Floor mesh (Plane018) not found - cannot align the lab.");

            // Level the floor, run the room along +Z and put the start point (just inside the entrance) at the origin.
            var start = frame.center + frame.forward * (-frame.length * 0.5f + StartDistanceFromEntrance);
            var q = Quaternion.Inverse(Quaternion.LookRotation(frame.forward, frame.up));
            env.transform.SetPositionAndRotation(q * -start, q);

            var room = MeasureRoom(env);
            Log($"Scene: removed {removed} old object(s); lab levelled (tilt was {Vector3.Angle(frame.up, Vector3.up):0.00} deg), " +
                $"room {room.width:0.00} x {room.height:0.00} x {room.length:0.00} m (W x H x L), entrance wall at z={room.entranceZ:0.00}, far wall z={room.farZ:0.00}");

            SetupTeleportFloor(scene, frame.go);
            SetupLighting(scene, room);
            SetupReflectionProbe(scene, room);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("Scene saved: " + scene.path);
            ReportReferenceSizes(env);
            return room;
        }

        static RoomInfo MeasureRoom(GameObject env)
        {
            var room = new RoomInfo();
            var floor = FindByPrefix(env, "Plane018");
            var ceiling = FindByPrefix(env, "Plane020");
            if (floor == null) return room;
            var fb = floor.GetComponent<Renderer>().bounds;
            room.valid = true;
            room.length = fb.size.z;
            room.width = fb.size.x;
            room.height = ceiling != null ? ceiling.GetComponent<Renderer>().bounds.center.y - fb.center.y : 3f;
            room.entranceZ = fb.min.z;
            room.farZ = fb.max.z;
            return room;
        }

        static void ReportReferenceSizes(GameObject env)
        {
            void Report(string prefix, string label, Func<Bounds, string> f)
            {
                var go = FindByPrefix(env, prefix);
                var r = go != null ? go.GetComponent<Renderer>() : null;
                if (r != null) Log($"  {label}: {f(r.bounds)}");
            }

            Log("Reference sizes after scaling:");
            Report("Plane018", "floor height (should be 0)", b => $"y = {b.center.y:0.000} m");
            Report("Object1111191742", "entrance door frame", b => $"top at {b.max.y:0.00} m");
            Report("Box2696", "fume hood work surface", b => $"{b.min.y:0.00} m above floor");
            Report("Box3073", "wall outlet plate", b => $"{b.size.y * 100:0.0} cm tall (US plate = 11.4 cm)");
            Report("Object1111191506", "storage cabinet", b => $"{b.size.y:0.00} m tall");
        }

        static void SetupTeleportFloor(Scene scene, GameObject floor)
        {
            var taType = FindType("UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea");
            if (taType == null)
            {
                Log("Teleport: XRI TeleportationArea type not found - skipped");
                return;
            }

            Component oldTa = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name != "Plane") continue;
                oldTa = go.GetComponent(taType);
                if (oldTa != null) break;
            }

            var ta = floor.GetComponent(taType);
            if (ta == null) ta = floor.AddComponent(taType);
            if (oldTa != null)
            {
                var src = new SerializedObject(oldTa);
                var dst = new SerializedObject(ta);
                var it = src.GetIterator();
                for (bool enter = true; it.NextVisible(enter); enter = false)
                {
                    if (it.name is "m_Script" or "m_Colliders" or "m_InteractionManager") continue;
                    dst.CopyFromSerializedProperty(it);
                }
                dst.ApplyModifiedPropertiesWithoutUndo();
                oldTa.gameObject.SetActive(false);
                Log("Teleport: lab floor is now a TeleportationArea (settings copied from the grid 'Plane', which is now disabled)");
            }
            else
            {
                var so = new SerializedObject(ta);
                var bits = so.FindProperty("m_InteractionLayers.m_Bits");
                if (bits != null) { bits.longValue = 1L << 31; so.ApplyModifiedPropertiesWithoutUndo(); }
                Log("Teleport: lab floor is now a TeleportationArea (Teleport interaction layer)");
            }
        }

        static void SetupLighting(Scene scene, RoomInfo room)
        {
            Light sun = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true))
                .FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null)
            {
                var go = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(go, scene);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            // Steep "overhead" key light. Ceiling/walls don't cast shadows, so it reaches the room interior.
            sun.transform.rotation = Quaternion.Euler(76f, -30f, 0f);
            sun.useColorTemperature = false;
            sun.color = new Color(1f, 0.97f, 0.93f);
            sun.intensity = 0.85f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            RenderSettings.sun = sun;

            // Even, slightly cool indoor fill (stands in for bounce light from the white room).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.66f, 0.69f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.61f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.56f, 0.56f, 0.56f);
            Log("Lighting: directional key light (76 deg, soft shadows) + trilight indoor ambient; ceiling panels are emissive");
        }

        static void SetupReflectionProbe(Scene scene, RoomInfo room)
        {
            var go = new GameObject("Lab Reflection Probe");
            SceneManager.MoveGameObjectToScene(go, scene);
            float midZ = (room.entranceZ + room.farZ) * 0.5f;
            go.transform.position = new Vector3(0f, 1.5f, midZ);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(room.width + 1.5f, room.height + 0.2f, room.length + 0.5f);
            probe.center = new Vector3(0f, room.height * 0.5f - 1.5f, 0f);
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = true;
            probe.mode = ReflectionProbeMode.Custom;

            try
            {
                EnsureFolder(Path.GetDirectoryName(LabPaths.ProbeTexture)?.Replace('\\', '/'));
                if (Lightmapping.BakeReflectionProbe(probe, LabPaths.ProbeTexture))
                {
                    AssetDatabase.ImportAsset(LabPaths.ProbeTexture, ImportAssetOptions.ForceUpdate);
                    if (AssetImporter.GetAtPath(LabPaths.ProbeTexture) is TextureImporter ti && ti.textureShape != TextureImporterShape.TextureCube)
                    {
                        ti.textureShape = TextureImporterShape.TextureCube;
                        ti.SaveAndReimport();
                    }
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(LabPaths.ProbeTexture);
                    // The scene has no generated lighting data, so the default (skybox) reflection is black and
                    // metals render black wherever the probe isn't sampled. Use the lab cubemap as the fallback too.
                    var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(LabPaths.ProbeTexture);
                    if (cube != null)
                    {
                        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                        RenderSettings.customReflectionTexture = cube;
                    }
                    Log("Reflection probe baked: " + LabPaths.ProbeTexture + (cube != null ? " (also used as the scene's environment reflection)" : ""));
                }
                else Log("Reflection probe bake returned false");
            }
            catch (Exception e)
            {
                Log("Reflection probe bake failed: " + e.GetBaseException().Message);
            }
        }

        // ------------------------------------------------------------------ previews

        static void RenderPreviews(RoomInfo room)
        {
            if (!room.valid) { Log("Previews skipped (room not measured)"); return; }
            Directory.CreateDirectory(LabPaths.PreviewDir);

            var camGo = new GameObject("~LabPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // 1.75 m capsule as a human-scale reference.
            var human = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            human.hideFlags = HideFlags.HideAndDontSave;
            human.transform.localScale = new Vector3(0.45f, 0.875f, 0.45f);
            human.transform.position = new Vector3(1.55f, 0.875f, 3.2f);
            Object.DestroyImmediate(human.GetComponent<Collider>());

            float e = room.entranceZ, far = room.farZ, w = room.width * 0.5f, h = room.height;
            var shots = new (string name, Vector3 pos, Vector3 target, float fov)[]
            {
                ("01_entrance_eye_level", new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.25f, 6f), 80f),
                ("02_far_end_looking_back", new Vector3(0.6f, 1.6f, far - 1.6f), new Vector3(0f, 1.2f, e + 3f), 80f),
                ("03_side_plus_x", new Vector3(1.4f, 1.6f, (e + far) * 0.5f - 2f), new Vector3(w, 1.1f, (e + far) * 0.5f + 1f), 85f),
                ("04_side_minus_x", new Vector3(-1.4f, 1.6f, (e + far) * 0.5f - 2f), new Vector3(-w, 1.1f, (e + far) * 0.5f + 1f), 85f),
                ("05_overview_corner", new Vector3(1.8f, h - 0.3f, e + 0.3f), new Vector3(-0.8f, 0.6f, far * 0.55f), 95f),
                ("06_scale_reference", new Vector3(-1.4f, 1.5f, 0.6f), new Vector3(1.4f, 1.0f, 3.6f), 70f),
                ("07_bench_closeup", new Vector3(1.4f, 1.55f, 4.5f), new Vector3(0f, 0.9f, 6.0f), 75f),
            };

            try
            {
                foreach (var s in shots)
                {
                    cam.transform.position = s.pos;
                    cam.transform.LookAt(s.target);
                    cam.fieldOfView = s.fov;
                    var file = Path.Combine(LabPaths.PreviewDir, s.name + ".png");
                    File.WriteAllBytes(file, RenderCamera(cam, 1600, 900));
                }
                Log($"Previews: {shots.Length} images in {Path.GetFullPath(LabPaths.PreviewDir)}");
            }
            catch (Exception ex)
            {
                Log("Preview rendering failed: " + ex.GetBaseException());
            }
            finally
            {
                Object.DestroyImmediate(human);
                Object.DestroyImmediate(camGo);
            }
        }

        static byte[] RenderCamera(Camera cam, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ------------------------------------------------------------------ helpers

        static GameObject FindEnvironmentInstance(Scene scene) =>
            scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Lab Environment");

        static GameObject FindByPrefix(GameObject root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix, StringComparison.Ordinal) && t.GetComponent<Renderer>() != null)
                    return t.gameObject;
            return null;
        }

        static Type FindType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string SafeFileName(string n)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        static void Progress(string msg, float p) => EditorUtility.DisplayProgressBar("Lab Environment", msg, p);

        static void Log(string line)
        {
            s_Log.AppendLine(line);
        }

        static void WriteReport()
        {
            Directory.CreateDirectory(LabPaths.PreviewDir);
            File.WriteAllText(Path.Combine(LabPaths.PreviewDir, "report.txt"), s_Log.ToString());
            Debug.Log("[Lab] " + s_Log);
        }
    }
}
