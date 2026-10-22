using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using Application = UnityEngine.Application;
using BuildResult = UnityEditor.Build.Reporting.BuildResult;

namespace UnityKit.Editor
{
    /// <summary>
    /// Unity editor build tool for Flutter integration.
    ///
    /// Two export modes:
    ///   1. Standalone (default) — builds to local Builds/ folder.
    ///      Ready to be zipped and uploaded as a CI artifact.
    ///   2. Deploy to Flutter — copies the built artifact into a Flutter project.
    ///      Configurable via Flutter > Settings or UNITY_KIT_FLUTTER_PROJECT env var.
    /// </summary>
    public class Build : EditorWindow
    {
        // ──────────────────────────────────────────────
        // Paths
        // ──────────────────────────────────────────────

        private static readonly string ProjectPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static readonly string BuildsPath =
            Path.Combine(ProjectPath, "Builds");

        private static readonly string APKPath =
            Path.Combine(BuildsPath, Application.productName + ".apk");

        // Standalone export targets (inside Unity project)
        private static readonly string AndroidArtifactPath =
            Path.Combine(BuildsPath, "android", "unityLibrary");

        private static readonly string IOSArtifactPath =
            Path.Combine(BuildsPath, "ios", "UnityLibrary");

        private static readonly string WebArtifactPath =
            Path.Combine(BuildsPath, "web", "UnityLibrary");

        // EditorPrefs key for optional Flutter project path
        private const string FLUTTER_PROJECT_PREF = "UnityKit_FlutterProjectPath";

        /// <summary>
        /// Returns the user-configured Flutter project path, or empty string.
        /// Checks environment variable first, then EditorPrefs.
        /// </summary>
        private static string FlutterProjectPath
        {
            get
            {
                // Environment variable takes priority (CI)
                var envPath = Environment.GetEnvironmentVariable("UNITY_KIT_FLUTTER_PROJECT");
                if (!string.IsNullOrEmpty(envPath))
                    return Path.GetFullPath(envPath);

                var configuredPath = EditorPrefs.GetString(FLUTTER_PROJECT_PREF, "");
                if (!string.IsNullOrEmpty(configuredPath))
                    return Path.GetFullPath(configuredPath);

                // A Unity project commonly lives inside <flutter>/unity/<project>.
                // Discover that layout so a normal Export also deploys without
                // requiring machine-specific EditorPrefs configuration.
                var current = new DirectoryInfo(ProjectPath);
                while (current != null)
                {
                    if (File.Exists(Path.Combine(current.FullName, "pubspec.yaml")))
                        return current.FullName;
                    current = current.Parent;
                }

                return "";
            }
            set => EditorPrefs.SetString(FLUTTER_PROJECT_PREF, value);
        }

        private static bool HasFlutterProject =>
            !string.IsNullOrEmpty(FlutterProjectPath)
            && File.Exists(Path.Combine(FlutterProjectPath, "pubspec.yaml"));

        // ──────────────────────────────────────────────
        // Menu items — Export (standalone)
        // ──────────────────────────────────────────────

        [MenuItem("Flutter/Export Android (Debug) %&n", false, 101)]
        public static void ExportAndroidDebug()
        {
            DoBuildAndroid(false);
        }

        [MenuItem("Flutter/Export Android (Release) %&m", false, 102)]
        public static void ExportAndroidRelease()
        {
            DoBuildAndroid(true);
        }

        [MenuItem("Flutter/Export iOS (Debug) %&i", false, 201)]
        public static void ExportIOSDebug()
        {
            DoBuildIOS(false);
        }

        [MenuItem("Flutter/Export iOS (Release)", false, 202)]
        public static void ExportIOSRelease()
        {
            DoBuildIOS(true);
        }

        [MenuItem("Flutter/Export WebGL %&w", false, 301)]
        public static void ExportWebGL()
        {
            DoBuildWebGL();
        }

        // ──────────────────────────────────────────────
        // Menu items — Deploy to Flutter (optional)
        // ──────────────────────────────────────────────

        [MenuItem("Flutter/Deploy to Flutter Project", false, 401)]
        public static void DeployToFlutter()
        {
            if (!HasFlutterProject)
            {
                Debug.LogError(
                    "[UnityKit] No Flutter project configured. " +
                    "Set it in Flutter > Settings or via UNITY_KIT_FLUTTER_PROJECT environment variable.");
                return;
            }

            var deployed = false;

            if (Directory.Exists(AndroidArtifactPath))
            {
                var target = Path.Combine(FlutterProjectPath, "android", "unityLibrary");
                Copy(AndroidArtifactPath, target);
                SetupAndroidProject(FlutterProjectPath);
                Debug.Log($"[UnityKit] Deployed Android to: {target}");
                deployed = true;
            }

            if (Directory.Exists(IOSArtifactPath))
            {
                var target = Path.Combine(FlutterProjectPath, "ios", "UnityLibrary");
                Copy(IOSArtifactPath, target);
                Debug.Log($"[UnityKit] Deployed iOS to: {target}");
                deployed = true;
            }

            if (Directory.Exists(WebArtifactPath))
            {
                var target = Path.Combine(FlutterProjectPath, "web", "UnityLibrary");
                Copy(WebArtifactPath, target);
                Debug.Log($"[UnityKit] Deployed WebGL to: {target}");
                deployed = true;
            }

            if (!deployed)
            {
                Debug.LogWarning(
                    "[UnityKit] No build artifacts found. Run an export first (Flutter > Export ...).");
            }
        }

        [MenuItem("Flutter/Deploy to Flutter Project", true)]
        private static bool DeployToFlutterValidate()
        {
            return HasFlutterProject;
        }

        // ──────────────────────────────────────────────
        // Menu items — Settings
        // ──────────────────────────────────────────────

        [MenuItem("Flutter/Settings %&S", false, 501)]
        public static void ShowSettings()
        {
            GetWindow(typeof(Build), false, "Unity Kit Export");
        }

        // ──────────────────────────────────────────────
        // Settings GUI
        // ──────────────────────────────────────────────

        private string _flutterPathField = "";

        private void OnEnable()
        {
            _flutterPathField = FlutterProjectPath;
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity Kit - Export Settings", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // --- Build artifacts ---
            GUILayout.Label("Build Artifacts (standalone)", EditorStyles.boldLabel);
            GUILayout.Label(
                "Exports go to the Builds/ folder inside the Unity project.\n" +
                "Use these as CI artifacts or deploy to a Flutter project.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(5);

            EditorGUILayout.LabelField("Android:", AndroidArtifactPath);
            EditorGUILayout.LabelField("iOS:", IOSArtifactPath);
            EditorGUILayout.LabelField("WebGL:", WebArtifactPath);

            GUILayout.Space(5);

            var hasAndroid = Directory.Exists(AndroidArtifactPath);
            var hasIOS = Directory.Exists(IOSArtifactPath);
            var hasWeb = Directory.Exists(WebArtifactPath);
            EditorGUILayout.LabelField("Status:",
                $"Android: {(hasAndroid ? "BUILT" : "—")}  |  " +
                $"iOS: {(hasIOS ? "BUILT" : "—")}  |  " +
                $"WebGL: {(hasWeb ? "BUILT" : "—")}");

            GUILayout.Space(15);

            // --- Flutter project (optional) ---
            GUILayout.Label("Flutter Project (optional)", EditorStyles.boldLabel);
            GUILayout.Label(
                "Set a path to auto-deploy builds into a Flutter project.\n" +
                "Leave empty for standalone mode (CI artifact only).\n" +
                "Can also be set via UNITY_KIT_FLUTTER_PROJECT environment variable.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            _flutterPathField = EditorGUILayout.TextField("Path:", _flutterPathField);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var selected = EditorUtility.OpenFolderPanel(
                    "Select Flutter Project Root", _flutterPathField, "");
                if (!string.IsNullOrEmpty(selected))
                    _flutterPathField = selected;
            }
            EditorGUILayout.EndHorizontal();

            if (_flutterPathField != FlutterProjectPath)
            {
                if (GUILayout.Button("Save Flutter Project Path"))
                {
                    FlutterProjectPath = _flutterPathField;
                    Debug.Log($"[UnityKit] Flutter project path saved: {_flutterPathField}");
                }
            }

            if (!string.IsNullOrEmpty(_flutterPathField))
            {
                var pubspec = Path.Combine(_flutterPathField, "pubspec.yaml");
                if (File.Exists(pubspec))
                {
                    EditorGUILayout.HelpBox("Flutter project detected (pubspec.yaml found).", MessageType.Info);
                }
                else if (Directory.Exists(_flutterPathField))
                {
                    EditorGUILayout.HelpBox(
                        "Directory exists but no pubspec.yaml found. Is this a Flutter project?",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Directory does not exist.", MessageType.Error);
                }
            }

            GUILayout.Space(20);

            // --- Quick actions ---
            GUILayout.Label("Quick Export", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Android (Debug)", GUILayout.Height(30)))
                ExportAndroidDebug();
            if (GUILayout.Button("Android (Release)", GUILayout.Height(30)))
                ExportAndroidRelease();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("iOS (Debug)", GUILayout.Height(30)))
                ExportIOSDebug();
            if (GUILayout.Button("iOS (Release)", GUILayout.Height(30)))
                ExportIOSRelease();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("WebGL", GUILayout.Height(30)))
                ExportWebGL();

            GUILayout.Space(10);

            GUI.enabled = HasFlutterProject && (hasAndroid || hasIOS || hasWeb);
            if (GUILayout.Button("Deploy to Flutter Project", GUILayout.Height(35)))
                DeployToFlutter();
            GUI.enabled = true;

            if (GUILayout.Button("Clean All Builds", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog(
                        "Clean Builds",
                        "Delete all build artifacts in Builds/ folder?",
                        "Delete", "Cancel"))
                {
                    if (Directory.Exists(BuildsPath))
                        Directory.Delete(BuildsPath, true);
                    Debug.Log("[UnityKit] Build artifacts cleaned.");
                }
            }
        }

        // ──────────────────────────────────────────────
        // Android Build
        // ──────────────────────────────────────────────

        private static void DoBuildAndroid(bool isReleaseBuild)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            if (Directory.Exists(APKPath))
                Directory.Delete(APKPath, true);

            if (Directory.Exists(AndroidArtifactPath))
                Directory.Delete(AndroidArtifactPath, true);

            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;

            var playerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.Android,
                locationPathName = APKPath,
            };

            if (!isReleaseBuild)
            {
                playerOptions.options = BuildOptions.AllowDebugging | BuildOptions.Development;
            }

            // IL2CPP compiler configuration
#if UNITY_2022_1_OR_NEWER
            PlayerSettings.SetIl2CppCompilerConfiguration(
                UnityEditor.Build.NamedBuildTarget.Android,
                isReleaseBuild ? Il2CppCompilerConfiguration.Release : Il2CppCompilerConfiguration.Debug);
#endif

#if UNITY_ANDROID && UNITY_6000_0_OR_NEWER
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = isReleaseBuild
                ? Unity.Android.Types.DebugSymbolLevel.None
                : Unity.Android.Types.DebugSymbolLevel.SymbolTable;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.format =
                Unity.Android.Types.DebugSymbolFormat.LegacyExtensions;
#endif

#if UNITY_ANDROID && UNITY_6000_5_OR_NEWER
            // The ThinLTO build of libunity.so is faster at runtime but only
            // exists for non-development players.
            UnityEditor.Android.UserBuildSettings.linkTimeOptimization = isReleaseBuild
                ? Unity.Android.Types.AndroidLinkTimeOptimization.Thin
                : Unity.Android.Types.AndroidLinkTimeOptimization.None;
#endif

#if UNITY_6000_6_OR_NEWER
            // Release drops the managed safety checks and profiler hooks that
            // the other variants compile into the player.
            PlayerSettings.SetManagedCodeVariant(
                UnityEditor.Build.NamedBuildTarget.Android,
                isReleaseBuild ? ManagedCodeVariant.Release : ManagedCodeVariant.Debug);
#endif

#if UNITY_ANDROID && UNITY_2023_1_OR_NEWER
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
#endif

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var report = BuildPipeline.BuildPlayer(playerOptions);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed");

            // Copy unityLibrary to artifact path
            var buildPath = Path.Combine(APKPath, "unityLibrary");
            Copy(buildPath, AndroidArtifactPath);

            // Unity 6 shared folder
            var sharedPath = Path.Combine(APKPath, "shared");
            if (Directory.Exists(sharedPath))
            {
                Copy(sharedPath, Path.Combine(AndroidArtifactPath, "shared"));
            }

            // Post-process: convert to library module
            ModifyAndroidGradle(AndroidArtifactPath);

            // Merge launcher resources without replacing complete values XML
            // files owned by unityLibrary (for example strings.xml).
            MergeAndroidResources(
                Path.Combine(APKPath, "launcher/src/main/res"),
                Path.Combine(AndroidArtifactPath, "src/main/res")
            );
            MergeUnityLibraryTemplateResources(AndroidArtifactPath);

            var mode = isReleaseBuild ? "Release" : "Debug";
            Debug.Log($"-- Android {mode} Build: SUCCESSFUL --");
            Debug.Log($"[UnityKit] Artifact: {AndroidArtifactPath}");

            // Auto-deploy if Flutter project is configured
            if (HasFlutterProject)
            {
                var target = Path.Combine(FlutterProjectPath, "android", "unityLibrary");
                Copy(AndroidArtifactPath, target);
                SetupAndroidProject(FlutterProjectPath);
                Debug.Log($"[UnityKit] Auto-deployed to: {target}");
            }
        }

        private static void ModifyAndroidGradle(string artifactPath)
        {
            // --- build.gradle ---
            var buildFile = Path.Combine(artifactPath, "build.gradle");
            if (!File.Exists(buildFile))
                throw new FileNotFoundException("Unity Android Gradle module was not exported", buildFile);

            var buildText = File.ReadAllText(buildFile);

            // Convert from application to library
            buildText = buildText.Replace("com.android.application", "com.android.library");
            buildText = buildText.Replace("bundle {", "splits {");
            buildText = buildText.Replace("enableSplit = false", "enable false");
            buildText = buildText.Replace("enableSplit = true", "enable true");
            buildText = buildText.Replace(
                "implementation fileTree(dir: 'libs', include: ['*.jar'])",
                "implementation(name: 'unity-classes', ext: 'jar')");
            buildText = buildText.Replace(" + unityStreamingAssets.tokenize(', ')", "");

            // Disable Unity NDK path (conflicts with Flutter)
            buildText = Regex.Replace(
                buildText,
                @"(?m)^(\s*)ndkPath\s+",
                "$1// ndkPath ");

            // Unity 6: fix shared/ path
            buildText = Regex.Replace(buildText, @"\.\./shared/", "./shared/");

            // Add namespace for Android Gradle Plugin 8+
            if (!Regex.IsMatch(buildText, @"(?m)^\s*namespace\s+"))
            {
                buildText = buildText.Replace(
                    "android {",
                    "android {\n    namespace 'com.unity3d.player'");
            }

            // Remove applicationId
            buildText = Regex.Replace(
                buildText,
                "(?m)^\\s*applicationId\\s*(?:=\\s*)?['\\\"].+['\\\"]\\s*$\\r?\\n?",
                string.Empty);
            File.WriteAllText(buildFile, buildText);

            // --- AndroidManifest.xml ---
            var manifestFile = Path.Combine(artifactPath, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifestFile))
                throw new FileNotFoundException("Unity Android manifest was not exported", manifestFile);

            // Strip application attributes and activity block
            var manifestText = File.ReadAllText(manifestFile);
            manifestText = Regex.Replace(manifestText, @"<application\b[^>]*>", "<application>");
            manifestText = Regex.Replace(
                manifestText,
                @"<activity\b[^>]*>[\s\S]*?</activity>",
                string.Empty);
            File.WriteAllText(manifestFile, manifestText);

            // --- proguard-unity.txt ---
            var proguardFile = Path.Combine(artifactPath, "proguard-unity.txt");
            if (File.Exists(proguardFile))
            {
                var proguardText = File.ReadAllText(proguardFile);
                const string rules =
                    "-keep class com.unity_kit.** { *; }\n" +
                    "-keep class com.unity3d.player.** { *; }\n";
                if (!proguardText.Contains("com.unity_kit.**"))
                    File.WriteAllText(proguardFile, rules + proguardText);
            }

            NormalizeNestedAndroidLibraries(
                artifactPath,
                ReadGradleProperties(Path.Combine(APKPath, "gradle.properties")));
            PatchBundledAarNamespaces(artifactPath);
        }

        /// <summary>
        /// Unity exports Android library plug-ins as nested modules of
        /// unityLibrary (xrmanifest, and since Unity 6.6 accessibility). Some
        /// have no namespace, which AGP 8+ rejects, and some read unity.*
        /// properties from the export's gradle.properties, which the host
        /// Flutter project does not have, so both are written in.
        /// </summary>
        private static void NormalizeNestedAndroidLibraries(
            string artifactPath,
            System.Collections.Generic.IReadOnlyDictionary<string, string> exportProperties)
        {
            foreach (var moduleDirectory in Directory.GetDirectories(artifactPath, "*.androidlib"))
            {
                var buildFile = Path.Combine(moduleDirectory, "build.gradle");
                if (!File.Exists(buildFile))
                    continue;

                var contents = File.ReadAllText(buildFile);

                if (!Regex.IsMatch(contents, @"(?m)^\s*namespace\s+"))
                {
                    var moduleName = Path.GetFileName(moduleDirectory)
                        .Replace(".androidlib", string.Empty)
                        .Replace('-', '_');
                    var namespaceName = moduleName.StartsWith("com.", StringComparison.Ordinal)
                        ? moduleName
                        : "com.unity3d.player." + moduleName;
                    contents = contents.Replace(
                        "android {",
                        $"android {{\n    namespace '{namespaceName}'");
                }

                contents = Regex.Replace(
                    contents,
                    @"getProperty\(\s*[""'](unity\.[A-Za-z0-9_.]+)[""']\s*\)",
                    match => exportProperties.TryGetValue(match.Groups[1].Value, out var value)
                        ? $"\"{value}\""
                        : match.Value);

                // Inside dependencies {}, project(':x') is a ProjectDependency,
                // which no longer exposes projectDir on newer Gradle versions.
                contents = Regex.Replace(
                    contents,
                    @"(?<![\w.])project\(\s*(['""]:unityLibrary['""])\s*\)\.projectDir",
                    "rootProject.project($1).projectDir");

                File.WriteAllText(buildFile, contents);
            }
        }

        private static System.Collections.Generic.IReadOnlyDictionary<string, string> ReadGradleProperties(
            string path)
        {
            var properties = new System.Collections.Generic.Dictionary<string, string>();
            if (!File.Exists(path))
                return properties;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                properties[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            return properties;
        }

        /// <summary>
        /// Unity's permissions AAR can ship with a legacy manifest package.
        /// Giving it a stable unique package lets modern AGP derive a namespace.
        /// </summary>
        private static void PatchBundledAarNamespaces(string artifactPath)
        {
            PatchAarManifestPackage(
                Path.Combine(artifactPath, "libs", "unityandroidpermissions.aar"),
                "com.unity3d.permissions");
        }

        private static void PatchAarManifestPackage(string aarPath, string packageName)
        {
            if (!File.Exists(aarPath))
                return;

            var temporaryPath = aarPath + ".tmp";
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            using (var input = ZipFile.OpenRead(aarPath))
            using (var output = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                foreach (var sourceEntry in input.Entries)
                {
                    var destinationEntry = output.CreateEntry(
                        sourceEntry.FullName,
                        System.IO.Compression.CompressionLevel.Optimal);

                    using var sourceStream = sourceEntry.Open();
                    using var destinationStream = destinationEntry.Open();
                    if (sourceEntry.FullName == "AndroidManifest.xml")
                    {
                        using var reader = new StreamReader(sourceStream);
                        var manifest = reader.ReadToEnd();
                        manifest = Regex.Replace(
                            manifest,
                            @"\bpackage=""[^""]+""",
                            $"package=\"{packageName}\"",
                            RegexOptions.None,
                            TimeSpan.FromSeconds(1));
                        using var writer = new StreamWriter(destinationStream);
                        writer.Write(manifest);
                    }
                    else
                    {
                        sourceStream.CopyTo(destinationStream);
                    }
                }
            }

            File.Delete(aarPath);
            File.Move(temporaryPath, aarPath);
        }

        /// <summary>
        /// Patches the Flutter project's gradle files to include unityLibrary.
        /// Called during deploy, NOT during standalone export.
        /// </summary>
        private static void SetupAndroidProject(string flutterProjectPath)
        {
            var androidPath = Path.Combine(flutterProjectPath, "android");
            var androidAppPath = Path.Combine(androidPath, "app");

            if (!Directory.Exists(androidPath))
            {
                Debug.LogWarning($"[UnityKit] Flutter android/ folder not found at: {androidPath}");
                return;
            }

            // Detect Kotlin DSL (Flutter 3.29+)
            if (File.Exists(Path.Combine(androidPath, "build.gradle.kts")))
            {
                SetupAndroidProjectKotlin(androidPath, androidAppPath);
                return;
            }

            SetupAndroidProjectGroovy(androidPath, androidAppPath);
        }

        private static void SetupAndroidProjectGroovy(string androidPath, string androidAppPath)
        {
            var projBuildPath = Path.Combine(androidPath, "build.gradle");
            var settingsPath = Path.Combine(androidPath, "settings.gradle");
            var appBuildPath = Path.Combine(androidAppPath, "build.gradle");

            if (!File.Exists(projBuildPath) || !File.Exists(settingsPath)) return;

            var projBuildScript = File.ReadAllText(projBuildPath);
            var settingsScript = File.ReadAllText(settingsPath);

            // Add flatDir repository
            if (!Regex.IsMatch(projBuildScript, @"flatDir[^/]*[^}]*}"))
            {
                projBuildScript = new Regex(@"allprojects \{[^\{]*\{", RegexOptions.Multiline)
                    .Replace(projBuildScript, "allprojects {\n    repositories {\n        flatDir {\n            dirs \"${project(':unityLibrary').projectDir}/libs\"\n        }\n");
                File.WriteAllText(projBuildPath, projBuildScript);
            }

            // Add include :unityLibrary
            if (!settingsScript.Contains(":unityLibrary"))
            {
                settingsScript += "\ninclude \":unityLibrary\"\nproject(\":unityLibrary\").projectDir = file(\"./unityLibrary\")\n";
                File.WriteAllText(settingsPath, settingsScript);
            }

            // Add dependency
            if (File.Exists(appBuildPath))
            {
                var appBuildScript = File.ReadAllText(appBuildPath);
                if (!appBuildScript.Contains("implementation project(':unityLibrary')"))
                {
                    if (Regex.IsMatch(appBuildScript, @"dependencies \{"))
                    {
                        appBuildScript = new Regex(@"dependencies \{", RegexOptions.Multiline)
                            .Replace(appBuildScript, "dependencies {\n    implementation project(':unityLibrary')\n");
                    }
                    else
                    {
                        appBuildScript += "\ndependencies {\n    implementation project(':unityLibrary')\n}\n";
                    }
                }
                appBuildScript = EnsureLegacyNativeLibraryPackaging(
                    appBuildScript, isKotlinDsl: false);
                File.WriteAllText(appBuildPath, appBuildScript);
            }
        }

        private static void SetupAndroidProjectKotlin(string androidPath, string androidAppPath)
        {
            var projBuildPath = Path.Combine(androidPath, "build.gradle.kts");
            var settingsPath = Path.Combine(androidPath, "settings.gradle.kts");
            var appBuildPath = Path.Combine(androidAppPath, "build.gradle.kts");

            if (!File.Exists(projBuildPath) || !File.Exists(settingsPath)) return;

            var projBuildScript = File.ReadAllText(projBuildPath);
            var settingsScript = File.ReadAllText(settingsPath);

            // Add flatDir repository
            if (!Regex.IsMatch(projBuildScript, @"flatDir[^/]*[^}]*}"))
            {
                projBuildScript = new Regex(@"allprojects \{[^\{]*\{", RegexOptions.Multiline)
                    .Replace(projBuildScript, "allprojects {\n    repositories {\n        flatDir {\n            dirs(file(\"${project(\":unityLibrary\").projectDir}/libs\"))\n        }\n");
                File.WriteAllText(projBuildPath, projBuildScript);
            }

            // Add include :unityLibrary
            if (!settingsScript.Contains(":unityLibrary"))
            {
                settingsScript += "\ninclude(\":unityLibrary\")\nproject(\":unityLibrary\").projectDir = file(\"./unityLibrary\")\n";
                File.WriteAllText(settingsPath, settingsScript);
            }

            // Add dependency
            if (File.Exists(appBuildPath))
            {
                var appBuildScript = File.ReadAllText(appBuildPath);
                if (!appBuildScript.Contains(":unityLibrary"))
                {
                    if (Regex.IsMatch(appBuildScript, @"dependencies \{"))
                    {
                        appBuildScript = new Regex(@"dependencies \{", RegexOptions.Multiline)
                            .Replace(appBuildScript, "dependencies {\n    implementation(project(\":unityLibrary\"))\n");
                    }
                    else
                    {
                        appBuildScript += "\ndependencies {\n    implementation(project(\":unityLibrary\"))\n}\n";
                    }
                }
                appBuildScript = EnsureLegacyNativeLibraryPackaging(
                    appBuildScript, isKotlinDsl: true);
                File.WriteAllText(appBuildPath, appBuildScript);
            }
        }

        /// <summary>
        /// UnityPlayer loads libmain.so/libunity.so from nativeLibraryDir.
        /// Flutter's default modern APK packaging can keep those libraries in
        /// the APK, so force extraction in every generated host project.
        /// </summary>
        private static string EnsureLegacyNativeLibraryPackaging(
            string appBuildScript,
            bool isKotlinDsl)
        {
            if (appBuildScript.Contains("useLegacyPackaging"))
                return appBuildScript;

            var androidBlock = new Regex(@"android\s*\{");
            if (!androidBlock.IsMatch(appBuildScript))
                throw new InvalidDataException(
                    "Flutter app Gradle file has no android block; " +
                    "cannot configure Unity native library packaging.");

            var packagingBlock = isKotlinDsl
                ? "\n    packaging {\n" +
                  "        jniLibs {\n" +
                  "            useLegacyPackaging = true\n" +
                  "        }\n" +
                  "    }\n"
                : "\n    packaging {\n" +
                  "        jniLibs {\n" +
                  "            useLegacyPackaging = true\n" +
                  "        }\n" +
                  "    }\n";

            return androidBlock.Replace(
                appBuildScript,
                match => match.Value + packagingBlock,
                1);
        }

        // ──────────────────────────────────────────────
        // iOS Build
        // ──────────────────────────────────────────────

        private static void DoBuildIOS(bool isReleaseBuild)
        {
            bool abortBuild = false;

#if !UNITY_IOS
            abortBuild = true;
            if (Application.isBatchMode)
            {
                Debug.LogError("Incorrect iOS build target. Use -buildTarget iOS.");
            }
            else
            {
                bool dialogResult = EditorUtility.DisplayDialog(
                    "Switch build target to iOS?",
                    "Exporting to iOS first requires a build target switch.\nClick 'Export iOS' again after importing finishes.",
                    "Switch to iOS",
                    "Cancel");
                if (dialogResult)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                }
            }
#endif
            if (abortBuild)
                return;

            if (Directory.Exists(IOSArtifactPath))
                Directory.Delete(IOSArtifactPath, true);

#if UNITY_2021_1_OR_NEWER
            EditorUserBuildSettings.iOSXcodeBuildConfig = XcodeBuildConfig.Release;
#else
            EditorUserBuildSettings.iOSBuildConfigType = iOSBuildType.Release;
#endif

            // IL2CPP compiler configuration
#if UNITY_2022_1_OR_NEWER
            PlayerSettings.SetIl2CppCompilerConfiguration(
                UnityEditor.Build.NamedBuildTarget.iOS,
                isReleaseBuild ? Il2CppCompilerConfiguration.Release : Il2CppCompilerConfiguration.Debug);
#endif

            var playerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.iOS,
                locationPathName = IOSArtifactPath,
            };

            if (!isReleaseBuild)
            {
                playerOptions.options = BuildOptions.AllowDebugging | BuildOptions.Development;
            }

            var report = BuildPipeline.BuildPlayer(playerOptions);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("iOS build failed");

            // Post-process Xcode project
            bool postBuildExecuted = false;
#if UNITY_IOS
            XCodePostBuild.PostBuild(BuildTarget.iOS, report.summary.outputPath);
            postBuildExecuted = true;
#endif

            if (postBuildExecuted)
            {
                var mode = isReleaseBuild ? "Release" : "Debug";
                Debug.Log($"-- iOS {mode} Build: SUCCESSFUL --");
                Debug.Log($"[UnityKit] Artifact: {IOSArtifactPath}");

                // Auto-deploy if Flutter project is configured
                if (HasFlutterProject)
                {
                    var target = Path.Combine(FlutterProjectPath, "ios", "UnityLibrary");
                    Copy(IOSArtifactPath, target);
                    Debug.Log($"[UnityKit] Auto-deployed to: {target}");
                }
            }
            else
            {
                Debug.LogError("iOS export failed. Failed to modify Unity's Xcode project.");
            }
        }

        // ──────────────────────────────────────────────
        // WebGL Build
        // ──────────────────────────────────────────────

        private static void DoBuildWebGL()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            if (Directory.Exists(WebArtifactPath))
                Directory.Delete(WebArtifactPath, true);

            var playerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.WebGL,
                locationPathName = WebArtifactPath,
            };

            var report = BuildPipeline.BuildPlayer(playerOptions);

            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("WebGL build failed");

            ModifyWebGLExport(WebArtifactPath);

            Debug.Log("-- WebGL Build: SUCCESSFUL --");
            Debug.Log($"[UnityKit] Artifact: {WebArtifactPath}");

            // Auto-deploy if Flutter project is configured
            if (HasFlutterProject)
            {
                var target = Path.Combine(FlutterProjectPath, "web", "UnityLibrary");
                Copy(WebArtifactPath, target);
                Debug.Log($"[UnityKit] Auto-deployed to: {target}");
            }
        }

        private static void ModifyWebGLExport(string artifactPath)
        {
            // Inject Flutter bridge into index.html
            var indexFile = Path.Combine(artifactPath, "index.html");
            var indexHtml = File.ReadAllText(indexFile);

            indexHtml = indexHtml.Replace("<script>", @"
    <script>
        var mainUnityInstance;

        window['handleUnityMessage'] = function (params) {
            window.parent.postMessage({
                name: 'onUnityMessage',
                data: params,
            }, '*');
        };

        window['handleUnitySceneLoaded'] = function (name, buildIndex, isLoaded, isValid) {
            window.parent.postMessage({
                name: 'onUnitySceneLoaded',
                data: {
                    'name': name,
                    'buildIndex': buildIndex,
                    'isLoaded': isLoaded == 1,
                    'isValid': isValid == 1,
                }
            }, '*');
        };

        window.parent.addEventListener('unityFlutterBiding', function (args) {
            const obj = JSON.parse(args.data);
            mainUnityInstance.SendMessage(obj.gameObject, obj.methodName, obj.message);
        });

        window.parent.addEventListener('unityFlutterBidingFnCal', function (args) {
            mainUnityInstance.SendMessage('FlutterBridge', 'ReceiveMessage', args.data);
        });
        ");

            // Full-screen canvas
            indexHtml = indexHtml.Replace("canvas.style.width = \"960px\";", "canvas.style.width = \"100%\";");
            indexHtml = indexHtml.Replace("canvas.style.height = \"600px\";", "canvas.style.height = \"100%\";");

            // Capture Unity instance
            indexHtml = indexHtml.Replace("}).then((unityInstance) => {", @"
         }).then((unityInstance) => {
           window.parent.postMessage('unityReady', '*');
           mainUnityInstance = unityInstance;
         ");
            File.WriteAllText(indexFile, indexHtml);

            // Full-screen CSS
            var cssFile = Path.Combine(artifactPath, "TemplateData", "style.css");
            if (File.Exists(cssFile))
            {
                File.WriteAllText(cssFile, @"
body { padding: 0; margin: 0; overflow: hidden; }
#unity-container { position: absolute }
#unity-container.unity-desktop { width: 100%; height: 100% }
#unity-container.unity-mobile { width: 100%; height: 100% }
#unity-canvas { background: #231F20 }
.unity-mobile #unity-canvas { width: 100%; height: 100% }
#unity-loading-bar { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); display: none }
#unity-footer { display: none }
.unity-mobile #unity-footer { display: none }
");
            }
        }

        // ──────────────────────────────────────────────
        // Utilities
        // ──────────────────────────────────────────────

        /// <summary>
        /// Merges the resources assigned to unityLibrary by the active Unity
        /// Editor. Some Unity exports omit module-specific template values;
        /// reading the installed version avoids hard-coded resource names.
        /// </summary>
        private static void MergeUnityLibraryTemplateResources(string artifactPath)
        {
            var templateRoot = Path.Combine(
                EditorApplication.applicationContentsPath,
                "PlaybackEngines", "AndroidPlayer", "Apk", "res");
            if (!Directory.Exists(templateRoot))
                throw new DirectoryNotFoundException(
                    $"Unity Android resource templates not found at {templateRoot}");

            var moduleDirectories = Directory.GetDirectories(
                templateRoot, "unityLibrary", SearchOption.AllDirectories);
            if (moduleDirectories.Length == 0)
                throw new InvalidOperationException(
                    $"No unityLibrary resource templates found under {templateRoot}");

            var destinationRoot = Path.Combine(artifactPath, "src", "main", "res");
            foreach (var moduleDirectory in moduleDirectories)
            {
                var qualifierDirectory = Directory.GetParent(moduleDirectory)
                    ?? throw new InvalidOperationException(
                        $"Invalid Unity resource template path: {moduleDirectory}");
                var destination = Path.Combine(
                    destinationRoot,
                    Path.GetRelativePath(templateRoot, qualifierDirectory.FullName));
                MergeAndroidResources(moduleDirectory, destination);
                ValidateMergedValuesResources(moduleDirectory, destination);
            }
        }

        private static void MergeAndroidResources(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var sourceFile in Directory.GetFiles(
                         source, "*", SearchOption.AllDirectories))
            {
                var destinationFile = Path.Combine(
                    destination,
                    Path.GetRelativePath(source, sourceFile));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationFile)
                    ?? throw new InvalidOperationException(
                        $"Invalid Android resource path: {destinationFile}"));

                if (IsValuesXml(destinationFile) && File.Exists(destinationFile))
                    MergeValuesXml(sourceFile, destinationFile);
                else
                    File.Copy(sourceFile, destinationFile, true);
            }
        }

        private static bool IsValuesXml(string file)
        {
            if (!string.Equals(Path.GetExtension(file), ".xml",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return Path.GetFileName(Path.GetDirectoryName(file))
                .StartsWith("values", StringComparison.OrdinalIgnoreCase);
        }

        private static void MergeValuesXml(string sourceFile, string destinationFile)
        {
            var sourceDocument = XDocument.Load(sourceFile);
            var destinationDocument = XDocument.Load(destinationFile);
            var sourceRoot = sourceDocument.Root;
            var destinationRoot = destinationDocument.Root;
            if (sourceRoot?.Name.LocalName != "resources"
                || destinationRoot?.Name.LocalName != "resources")
                throw new InvalidDataException(
                    $"Android values XML must have a <resources> root: {sourceFile}");

            var existingKeys = destinationRoot.Elements()
                .Select(GetResourceKey)
                .Where(key => key != null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var element in sourceRoot.Elements())
            {
                var key = GetResourceKey(element);
                if (key != null && existingKeys.Contains(key))
                    continue;

                destinationRoot.Add(new XElement(element));
                if (key != null)
                    existingKeys.Add(key);
            }

            destinationDocument.Save(destinationFile);
        }

        private static string GetResourceKey(XElement element)
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var type = element.Name.LocalName == "item"
                ? element.Attribute("type")?.Value ?? "item"
                : element.Name.LocalName;
            return $"{type}:{name}";
        }

        private static void ValidateMergedValuesResources(
            string source,
            string destination)
        {
            foreach (var sourceFile in Directory.GetFiles(
                         source, "*.xml", SearchOption.AllDirectories))
            {
                var destinationFile = Path.Combine(
                    destination,
                    Path.GetRelativePath(source, sourceFile));
                if (!IsValuesXml(destinationFile))
                    continue;
                if (!File.Exists(destinationFile))
                    throw new FileNotFoundException(
                        "Unity runtime resource file was not exported", destinationFile);

                var expectedKeys = XDocument.Load(sourceFile).Root?
                    .Elements().Select(GetResourceKey)
                    .Where(key => key != null).ToArray()
                    ?? Array.Empty<string>();
                var actualKeys = XDocument.Load(destinationFile).Root?
                    .Elements().Select(GetResourceKey)
                    .Where(key => key != null)
                    .ToHashSet(StringComparer.Ordinal)
                    ?? new System.Collections.Generic.HashSet<string>();
                var missing = expectedKeys
                    .Where(key => !actualKeys.Contains(key)).ToArray();
                if (missing.Length > 0)
                    throw new InvalidDataException(
                        $"Unity runtime resources missing from {destinationFile}: "
                        + string.Join(", ", missing));
            }
        }

        private static void Copy(string source, string destinationPath, bool clearDestination = true)
        {
            if (clearDestination && Directory.Exists(destinationPath))
                Directory.Delete(destinationPath, true);

            Directory.CreateDirectory(destinationPath);

            foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(source, destinationPath));

            foreach (var newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(source, destinationPath), true);
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
        }
    }
}
