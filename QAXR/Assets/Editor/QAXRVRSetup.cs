#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>
/// QAXR > Configure VR (Meta Quest 3)
/// - XR Plug-in Management: OpenXR loader for Standalone (PC + Quest Link / Air Link) and Android (Quest standalone),
///   "Initialize XR on Startup" on.
/// - OpenXR features: Oculus Touch + Meta Quest Touch Plus controller profiles (both targets),
///   Meta Quest Support (Android).
/// - Android player settings for Quest: IL2CPP, ARM64, min API 32, Vulkan.
/// Safe to run more than once.
/// </summary>
public static class QAXRVRSetup
{
    private const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
    private const string SettingsAsset = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

    [MenuItem("QAXR/Configure VR (Meta Quest 3)")]
    public static void ConfigureFromMenu() => Debug.Log(Configure());

    public static string Configure()
    {
        var log = new StringBuilder("QAXR VR setup:\n");

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, SettingsAsset);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            log.AppendLine("- created " + SettingsAsset);
        }

        foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings gs = perTarget.SettingsForBuildTarget(group);
            gs.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(gs.Manager, LoaderType, group);
            EditorUtility.SetDirty(gs);
            EditorUtility.SetDirty(gs.Manager);
            log.AppendLine("- " + group + ": OpenXR loader " + (assigned ? "assigned" : "already assigned / check XR Plug-in Management") + ", initialize on startup");

            FeatureHelpers.RefreshFeatures(group);
            OpenXRSettings ox = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (ox == null) { log.AppendLine("  ! OpenXR settings not found for " + group); continue; }
            foreach (OpenXRFeature f in ox.GetFeatures())
            {
                string n = f.GetType().Name;
                bool want = n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" ||
                            (group == BuildTargetGroup.Android && n == "MetaQuestFeature");
                if (want && !f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); log.AppendLine("  + enabled " + n); }
                else if (want) log.AppendLine("  = " + n + " already enabled");
            }
            EditorUtility.SetDirty(ox);
        }

        // Quest standalone (Android) player settings.
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        log.AppendLine("- Android: IL2CPP, ARM64, min API 32, Vulkan/GLES3");

        AssetDatabase.SaveAssets();
        log.AppendLine("Done. PC: connect Quest 3 with Meta Quest Link (set Meta as active OpenXR runtime), then press Play.");
        log.AppendLine("Standalone: File > Build Profiles > Android > Switch Platform > Build And Run with the headset connected.");
        return log.ToString();
    }
}
#endif
