using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AceBarcade.EditorTools
{
    /// <summary>
    /// Puts the project settings and the one scene in their known state. Unity locks its settings
    /// files while the editor is open, so settings change here, in code, not by editing the files.
    /// Batch mode: Unity -batchmode -buildTarget Android -projectPath . -executeMethod AceBarcade.EditorTools.ProjectSetup.Apply -quit
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Arcade.unity";

        [MenuItem("Tools/Ace Barcade/Apply Project Settings")]
        public static void Apply()
        {
            ApplyPlayerSettings();
            ApplyRenderSettings();
            FontSetup.Ensure();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Ace Barcade] Project settings applied.");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Tony3974";
            PlayerSettings.productName = "Ace Barcade";
            PlayerSettings.bundleVersion = "0.5";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.Tony3974.AceBarcade");

            // Portrait only, on Android only.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.Android.bundleVersionCode = 5;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
        }

        static void ApplyRenderSettings()
        {
            // Flat unlit sprites: no HDR, no MSAA, no post-processing. Cheap on every phone.
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.supportsHDR = false;
                urp.msaaSampleCount = 1;
                EditorUtility.SetDirty(urp);
            }
        }

        static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Arcade").AddComponent<ArcadeRoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
