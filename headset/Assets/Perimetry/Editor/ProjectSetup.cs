using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Perimetry.Headset.Editor
{
    /// <summary>
    /// One-click project configuration for Quest 3/3S (menu Perimetry > Configure Project). Everything that
    /// affects what reaches the panel is set here so it is reproducible:
    /// Linear colour space (sRGB eye buffers, so the shader's dithered codes reach the panel unchanged),
    /// URP with HDR, MSAA and post-processing off, OpenXR + Meta Quest feature + Touch profile,
    /// single-pass instanced stereo (the shader routes per eye with unity_StereoEyeIndex), Android ARM64
    /// IL2CPP Vulkan, both input handlers (legacy keys for Bluetooth clickers), and the Perimetry scene.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Perimetry.unity";
        public const string SettingsDir = "Assets/Settings";
        public const string ApplicationId = "br.questperimetry.headset";

        [MenuItem("Perimetry/Configure Project")]
        public static void Configure()
        {
            var log = new List<string>();
            ConfigurePlayer(log);
            ConfigureRenderPipeline(log);
            ConfigureXr(BuildTargetGroup.Android, log);
            ConfigureXr(BuildTargetGroup.Standalone, log);
            CreateScene(log);
            log.Add("config hash " + ConfigSync.Sync());
            AssetDatabase.SaveAssets();
            Debug.Log("[Perimetry] project configured:\n - " + string.Join("\n - ", log));
        }

        private static void ConfigurePlayer(List<string> log)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Quest Perimetry";
            PlayerSettings.productName = "Quest Perimetry";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            log.Add("player: Linear, Android ARM64 IL2CPP Vulkan, min SDK 32, id " + ApplicationId);

            // Active Input Handling = Both (2): Touch controllers through XR input, clickers through legacy keys.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 2)
                {
                    p.intValue = 2;
                    so.ApplyModifiedProperties();
                    log.Add("input handling set to Both: RESTART THE EDITOR");
                }
            }
        }

        private static void ConfigureRenderPipeline(List<string> log)
        {
            Directory.CreateDirectory(SettingsDir);
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, SettingsDir + "/PerimetryRenderer.asset");
                urp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(urp, SettingsDir + "/PerimetryURP.asset");
                GraphicsSettings.defaultRenderPipeline = urp;
                log.Add("created URP asset " + SettingsDir + "/PerimetryURP.asset");
            }
            urp.supportsHDR = false;
            urp.msaaSampleCount = 1;
            urp.renderScale = 1f;
            EditorUtility.SetDirty(urp);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            log.Add("URP: HDR off, MSAA off, render scale 1 (post-processing is off on runtime cameras by default)");
        }

        private static void ConfigureXr(BuildTargetGroup group, List<string> log)
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var gs = perTarget.SettingsForBuildTarget(group);
            gs.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(gs.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);

            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            var enabled = new List<string>();
            if (oxr != null)
            {
                oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                foreach (var f in oxr.GetFeatures())
                {
                    string n = f.GetType().Name;
                    // EyeGazeInteraction (Phase 5): XR_EXT_eye_gaze_interaction, used on the Quest Pro; harmless on the Quest 3.
                    if (n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || n == "EyeGazeInteraction"
                        || (group == BuildTargetGroup.Android && n == "MetaQuestFeature"))
                    {
                        f.enabled = true;
                        enabled.Add(n);
                    }
                }
                EditorUtility.SetDirty(oxr);
            }
            log.Add($"XR {group}: OpenXR loader {(assigned ? "assigned" : "already assigned or FAILED, check XR Plug-in Management")}, single-pass instanced, features [{string.Join(", ", enabled)}]");
            if (group == BuildTargetGroup.Android && !enabled.Contains("MetaQuestFeature"))
                log.Add("WARNING: Meta Quest feature not found; enable it in Project Settings > XR Plug-in Management > OpenXR (Android)");
            if (!enabled.Contains("EyeGazeInteraction"))
                log.Add("note: Eye Gaze Interaction Profile not found in this OpenXR package; eye tracking (Quest Pro) stays off");
        }

        private static void CreateScene(List<string> log)
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cam = new GameObject("PerimetryCamera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                new GameObject("PerimetryApp").AddComponent<PerimetryApp>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                log.Add("created scene " + ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
