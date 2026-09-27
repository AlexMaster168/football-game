using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FD.EditorTools
{
    public static class ProjectTools
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Football Dream/Create Main Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Main scene created: " + ScenePath);
        }

        const string MaterialPath = "Assets/Resources/FD_Base.mat";

        /// <summary>Базовый материал в Resources — без него Standard-шейдер вырезается из билда.</summary>
        [MenuItem("Football Dream/Create Base Material")]
        public static void EnsureBaseMaterial()
        {
            Directory.CreateDirectory("Assets/Resources");
            if (!File.Exists(MaterialPath))
            {
                var mat = new Material(Shader.Find("Standard")) { color = Color.white };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            // вариант Standard с эмиссией (прожекторы) — иначе keyword вырежется из билда
            if (!File.Exists("Assets/Resources/FD_Emissive.mat"))
            {
                var em = new Material(Shader.Find("Standard")) { color = Color.white };
                em.EnableKeyword("_EMISSION");
                em.SetColor("_EmissionColor", Color.white);
                em.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                AssetDatabase.CreateAsset(em, "Assets/Resources/FD_Emissive.mat");
            }
            // неосвещённый материал для шлейфа мяча и конфетти
            if (!File.Exists("Assets/Resources/FD_Unlit.mat"))
            {
                var un = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
                AssetDatabase.CreateAsset(un, "Assets/Resources/FD_Unlit.mat");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Football Dream/Build Windows")]
        public static void BuildWindows()
        {
            EnsureBaseMaterial();
            if (!File.Exists(ScenePath)) CreateScene();
            PlayerSettings.productName = "Football Dream 2026";
            PlayerSettings.companyName = "FootballDream";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/FootballDream.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Debug.Log("Build result: " + report.summary.result + ", errors: " + report.summary.totalErrors);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
