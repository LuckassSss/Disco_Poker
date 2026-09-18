using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PokerClient.Editor
{
    /// <summary>
    /// Батч-команды для CLI: unity run … -- -executeMethod PokerClient.Editor.BuildTools.&lt;метод&gt;
    /// Выход задаётся аргументом -buildOutput, иначе Builds/.
    /// </summary>
    public static class BuildTools
    {
        [MenuItem("Poker/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            var path = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(path, false);
            AssetDatabase.Refresh();
            Debug.Log("TMP Essential Resources импортированы");
        }

        [MenuItem("Poker/Build Android APK")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, Output("Builds/android/DiscoPoker.apk"));
        }

        [MenuItem("Poker/Build iOS Xcode project")]
        public static void BuildIos() => Build(BuildTarget.iOS, Output("Builds/ios"));

        static void Build(BuildTarget target, string output)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, output, target, BuildOptions.None);
            var summary = report.summary;
            Debug.Log($"Сборка {target}: {summary.result}, {summary.totalSize / 1024 / 1024} МБ, ошибок {summary.totalErrors} → {output}");
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        static string Output(string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-buildOutput");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
