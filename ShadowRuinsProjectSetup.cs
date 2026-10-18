#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates and registers a runnable Main scene the first time the project is imported.</summary>
[InitializeOnLoad]
public static class ShadowRuinsProjectSetup
{
    private const string MainScenePath = "Assets/Scenes/Main.unity";

    static ShadowRuinsProjectSetup()
    {
        EditorApplication.delayCall += EnsureProjectScene;
    }

    [MenuItem("Shadow Ruins/Create or Repair Main Scene")]
    public static void EnsureProjectScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");

        bool createScene = !File.Exists(MainScenePath);
        Scene scene;
        if (createScene)
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject bootstrap = new GameObject("Shadow Ruins Bootstrap");
            bootstrap.AddComponent<ShadowRuinsGame>();
            EditorSceneManager.SaveScene(scene, MainScenePath);
            Debug.Log("Shadow Ruins: created Assets/Scenes/Main.unity.");
        }
        else
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
            {
                // On a fresh import Unity may initially open an empty Untitled scene. Open our generated
                // gameplay scene in that case, but leave a deliberately opened user scene alone.
                if (string.IsNullOrEmpty(scene.path))
                    scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }
        }

        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        bool hasMain = false;
        foreach (EditorBuildSettingsScene item in existing)
        {
            if (item.path == MainScenePath) { hasMain = true; break; }
        }
        if (!hasMain)
        {
            EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[existing.Length + 1];
            updated[0] = new EditorBuildSettingsScene(MainScenePath, true);
            for (int i = 0; i < existing.Length; i++) updated[i + 1] = existing[i];
            EditorBuildSettings.scenes = updated;
        }

        PlayerSettings.productName = "Shadow Ruins";
        PlayerSettings.companyName = "Shadow Ruins Studio";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
    }
}
#endif
