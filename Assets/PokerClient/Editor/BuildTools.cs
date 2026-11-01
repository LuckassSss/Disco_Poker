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
            // -aab — бандл для сторов (CI), иначе APK для эмулятора
            EditorUserBuildSettings.buildAppBundle = Array.IndexOf(Environment.GetCommandLineArgs(), "-aab") >= 0;
            // Релизный ключ — из окружения CI; без него отладочная подпись (сторы такую не примут)
            var keystore = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            if (!string.IsNullOrEmpty(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            }
            Build(BuildTarget.Android, Output("Builds/android/DiscoPoker.apk"));
        }

        [MenuItem("Poker/Build iOS Xcode project")]
        public static void BuildIos()
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            Build(BuildTarget.iOS, Output("Builds/ios"));
        }

        /// <summary>Под симулятор: другой SDK и отдельная папка, подпись не нужна.</summary>
        [MenuItem("Poker/Build iOS Simulator Xcode project")]
        public static void BuildIosSimulator()
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            Build(BuildTarget.iOS, Output("Builds/ios-sim"));
        }

        static void Build(BuildTarget target, string output)
        {
            // CI: -buildVersion 1.2.0 (из тега), -buildNumber N (растёт с каждой сборкой — сторы требуют)
            var version = Arg("-buildVersion");
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
            if (int.TryParse(Arg("-buildNumber"), out var number))
            {
                PlayerSettings.Android.bundleVersionCode = number;
                PlayerSettings.iOS.buildNumber = number.ToString();
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, output, target, BuildOptions.None);
            var summary = report.summary;
            Debug.Log($"Сборка {target}: {summary.result}, {summary.totalSize / 1024 / 1024} МБ, ошибок {summary.totalErrors} → {output}");
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        static string Output(string fallback) => Arg("-buildOutput") ?? fallback;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
