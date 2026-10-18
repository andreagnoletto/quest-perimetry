using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Perimetry.Headset.Editor
{
    /// <summary>
    /// Quest APK build (menu Perimetry > Build APK, or batch mode:
    /// <c>Unity -batchmode -quit -projectPath headset -buildTarget Android -executeMethod Perimetry.Headset.Editor.BuildApk.Build</c>).
    /// Writes <c>headset/Builds/QuestPerimetry.apk</c>; exits non-zero in batch mode if the build fails.
    /// </summary>
    public static class BuildApk
    {
        public const string OutputPath = "Builds/QuestPerimetry.apk";

        [MenuItem("Perimetry/Build APK")]
        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ProjectSetup.Configure();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[Perimetry] build {s.result}: {s.outputPath} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors)");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
