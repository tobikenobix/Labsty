#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates and saves the fully visible demo scene when the project is first opened.</summary>
[InitializeOnLoad]
public static class QAXRFirstOpenSetup
{
    private const string ScenePath = "Assets/Scenes/QAXR_Demo.unity";
    private const string SetupStamp = "ProjectSettings/QAXR_Initialized.txt";

    static QAXRFirstOpenSetup()
    {
        EditorApplication.delayCall += AutoSetup;
    }

    private static void AutoSetup()
    {
        if (File.Exists(SetupStamp)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += AutoSetup;
            return;
        }
        try
        {
            RebuildInternal();
            File.WriteAllText(SetupStamp, "QAXR scene generated. Use QAXR > Rebuild Demo Scene to recreate it.\n");
            Debug.Log("QAXR full project is ready. Open the Game tab and press Play.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("QAXR first-open setup failed: " + e.Message +
                           ". You can use QAXR > Rebuild Demo Scene, or press Play in the starter scene.");
        }
    }

    [MenuItem("QAXR/Rebuild Demo Scene")]
    public static void RebuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RebuildInternal();
        File.WriteAllText(SetupStamp, "QAXR scene generated.\n");
        Debug.Log("QAXR scene regenerated. Press Play.");
    }

    public static void RebuildInternal()
    {
        // Creates real saved Unity scene objects. This is not just a script bundle.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        QAXRSceneBuilder.Build();
        if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Could not save generated demo scene at " + ScenePath);

        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
#endif
