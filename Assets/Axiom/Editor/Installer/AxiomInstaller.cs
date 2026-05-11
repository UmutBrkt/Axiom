using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.IO;
using System.Collections.Generic;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Axiom.Editor.Installer
{
    internal readonly struct OptionalPackage
    {
        public readonly string PackageId;
        public readonly string AltPackageId;
        public readonly string DefineSymbol;
        public readonly string FriendlyName;
        public readonly string FeatureDescription;

        public OptionalPackage(string packageId, string altPackageId, string define, string name, string desc)
        {
            PackageId        = packageId;
            AltPackageId     = altPackageId;
            DefineSymbol     = define;
            FriendlyName     = name;
            FeatureDescription = desc;
        }

        public bool IsInstalled() =>
            AxiomInstaller.IsPackageInstalled(PackageId) ||
            (!string.IsNullOrEmpty(AltPackageId) && AxiomInstaller.IsPackageInstalled(AltPackageId));
    }

    /// <summary>
    /// Deploys Axiom workspace rules and handles optional package installation.
    ///
    /// Handles three install scenarios for workspace rules:
    ///   1. Development (Assets-based): TextAssets from Assets/Axiom/Editor/WorkspaceRules/
    ///   2. UPM from OpenUPM or ?path= git URL: Editor/WorkspaceRules/ with .txt files (built from main)
    ///   3. UPM from #upm git URL: Editor/WorkspaceRules~/ with real filenames (CI-renamed)
    /// </summary>
    public static class AxiomInstaller
    {
        private const string MenuRoot = "Tools/Axiom/";

        // Development mode: TextAsset path in Assets
        private const string DevWorkspaceRulesPath = "Assets/Axiom/Editor/WorkspaceRules";

        // UPM paths (relative to package root, resolved via PackageInfo)
        private const string UpmTildeFolder   = "Editor/WorkspaceRules~";   // #upm branch
        private const string UpmRegularFolder = "Editor/WorkspaceRules";    // OpenUPM / ?path=

        // Tilde folder has real filenames (CI-renamed from .txt)
        private static readonly (string sourceFile, string targetFile, string description)[] TildeFiles =
        {
            (".cursorrules",            ".cursorrules",            "Agent API reference (Cursor IDE)"),
            ("project_instructions.md", "project_instructions.md", "Command schema + implementation template"),
            ("CLAUDE.md",               "CLAUDE.md",               "Claude Code complete reference"),
        };

        // Regular folder has .txt TextAsset names (built from main as-is)
        private static readonly (string sourceFile, string targetFile, string description)[] RegularFiles =
        {
            ("cursorrules.txt",          ".cursorrules",            "Agent API reference (Cursor IDE)"),
            ("project_instructions.txt", "project_instructions.md", "Command schema + implementation template"),
            ("claude.txt",               "CLAUDE.md",               "Claude Code complete reference"),
        };

        // Development mode uses AssetDatabase TextAssets (same names as RegularFiles but loaded differently)
        private static readonly (string sourceName, string targetName, string description)[] DevFiles =
        {
            ("cursorrules",          ".cursorrules",            "Agent API reference (Cursor IDE)"),
            ("project_instructions", "project_instructions.md", "Command schema + implementation template"),
            ("claude",               "CLAUDE.md",               "Claude Code complete reference"),
        };

        // All target filenames for Remove and Verify operations
        private static readonly string[] AllTargetFiles = { ".cursorrules", "project_instructions.md", "CLAUDE.md" };

        /// <summary>
        /// Optional packages — only those that are truly optional.
        /// Newtonsoft.Json and Unity AI Assistant are required dependencies in package.json.
        /// </summary>
        internal static readonly OptionalPackage[] OptionalPackages =
        {
            new OptionalPackage(
                "com.unity.inputsystem", null,
                "AXIOM_HAS_INPUT_SYSTEM",
                "Input System",
                "InputSimulationActions — simulate keyboard, mouse, and gamepad input in Play Mode."),

            new OptionalPackage(
                "com.unity.multiplayer.playmode", null,
                "AXIOM_HAS_MPPM",
                "Multiplayer Play Mode",
                "MultiplayerActions — configure and test multi-player virtual players via MPPM 2.0."),

            new OptionalPackage(
                "com.unity.ai.inference", "com.unity.sentis",
                "AXIOM_HAS_SENTIS",
                "AI Inference (formerly Sentis)",
                "SentisActions — run ONNX ML models from editor scripts. (Note: asmdef checks com.unity.sentis; update the version define if AXIOM_HAS_SENTIS does not fire.)"),
        };

        // ── Package installation queue ────────────────────────────────────

        private static readonly Queue<string> s_PackageQueue = new Queue<string>();
        private static AddRequest s_AddRequest;
        private static int s_TotalToInstall;
        private static int s_InstalledCount;

        // ── Install mode detection ────────────────────────────────────────

        private static bool IsUpmInstall()
        {
            return PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly) != null;
        }

        private static string GetPackagePath()
        {
            var info = PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly);
            return info?.resolvedPath;
        }

        // ── Workspace rules: Install ──────────────────────────────────────

        [MenuItem(MenuRoot + "Install Workspace Rules to Project Root", false, 100)]
        public static void InstallWorkspaceRules()
        {
            if (IsUpmInstall())
                InstallFromUpm();
            else
                InstallFromDev();
        }

        /// <summary>
        /// UPM install — handles both tilde and non-tilde WorkspaceRules folders.
        /// Tilde folder exists on #upm branch installs (CI renames files to real names).
        /// Regular folder exists on OpenUPM and ?path= installs (built from main, .txt names).
        /// </summary>
        private static void InstallFromUpm()
        {
            string pkgPath = GetPackagePath();
            if (pkgPath == null)
            {
                EditorUtility.DisplayDialog("Axiom — Error",
                    "Could not locate Axiom package via PackageInfo.", "OK");
                return;
            }

            // Try tilde folder first (#upm branch — real filenames)
            string tildePath = Path.Combine(pkgPath, UpmTildeFolder);
            if (Directory.Exists(tildePath))
            {
                InstallFromDirectory(tildePath, TildeFiles);
                return;
            }

            // Fall back to regular folder (OpenUPM / ?path= — .txt names)
            string regularPath = Path.Combine(pkgPath, UpmRegularFolder);
            if (Directory.Exists(regularPath))
            {
                InstallFromDirectory(regularPath, RegularFiles);
                return;
            }

            EditorUtility.DisplayDialog("Axiom — Error",
                $"WorkspaceRules folder not found in package.\n\n" +
                $"Checked:\n  {tildePath}\n  {regularPath}\n\n" +
                "Try reinstalling the package.", "OK");
        }

        /// <summary>
        /// Reads files from a directory on disk and copies them to project root.
        /// Used by both tilde and regular UPM install paths.
        /// </summary>
        private static void InstallFromDirectory(string dirPath,
            (string sourceFile, string targetFile, string description)[] fileMap)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int installed = 0;
            int skipped = 0;

            foreach (var (sourceFile, targetFile, description) in fileMap)
            {
                string src = Path.Combine(dirPath, sourceFile);
                string dst = Path.Combine(projectRoot, targetFile);

                if (!File.Exists(src))
                {
                    Debug.LogWarning($"[Axiom] Source not found: {src}");
                    continue;
                }

                if (File.Exists(dst))
                {
                    bool overwrite = EditorUtility.DisplayDialog(
                        "Axiom — File Exists",
                        $"'{targetFile}' already exists at project root.\n" +
                        $"({description})\n\nOverwrite with package version?",
                        "Overwrite", "Skip");

                    if (!overwrite) { skipped++; continue; }
                }

                File.Copy(src, dst, overwrite: true);
                installed++;
                Debug.Log($"[Axiom] Installed '{targetFile}' → {projectRoot}");
            }

            EditorUtility.DisplayDialog("Axiom — Install Complete",
                $"Workspace rules installed.\n\n" +
                $"  Installed: {installed}\n" +
                $"  Skipped:   {skipped}\n\n" +
                $"Location: {projectRoot}", "OK");
        }

        /// <summary>
        /// Development mode — reads TextAssets from Assets/Axiom/Editor/WorkspaceRules/.
        /// Only used when running inside the Axiom development project itself.
        /// </summary>
        private static void InstallFromDev()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int installed = 0;
            int skipped = 0;

            foreach (var (sourceName, targetName, description) in DevFiles)
            {
                string sourcePath = $"{DevWorkspaceRulesPath}/{sourceName}.txt";
                string targetPath = Path.Combine(projectRoot, targetName);

                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(sourcePath);
                if (asset == null)
                {
                    Debug.LogWarning($"[Axiom] Embedded file not found: {sourcePath}");
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    bool overwrite = EditorUtility.DisplayDialog(
                        "Axiom — File Exists",
                        $"'{targetName}' already exists at project root.\n" +
                        $"({description})\n\nOverwrite with embedded version?",
                        "Overwrite", "Skip");

                    if (!overwrite) { skipped++; continue; }
                }

                File.WriteAllText(targetPath, asset.text);
                installed++;
                Debug.Log($"[Axiom] Installed '{targetName}' → {projectRoot}");
            }

            EditorUtility.DisplayDialog("Axiom — Install Complete",
                $"Workspace rules installed.\n\n" +
                $"  Installed: {installed}\n" +
                $"  Skipped:   {skipped}\n\n" +
                $"Location: {projectRoot}", "OK");
        }

        // ── Workspace rules: Remove ───────────────────────────────────────

        [MenuItem(MenuRoot + "Remove Workspace Rules from Project Root", false, 101)]
        public static void RemoveWorkspaceRules()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int removed = 0;

            foreach (string targetFile in AllTargetFiles)
            {
                string targetPath = Path.Combine(projectRoot, targetFile);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    removed++;
                    Debug.Log($"[Axiom] Removed '{targetFile}' from project root.");
                }
            }

            EditorUtility.DisplayDialog("Axiom — Removal Complete",
                $"Removed {removed} workspace rule file(s) from project root.", "OK");
        }

        // ── Optional packages ─────────────────────────────────────────────

        [MenuItem(MenuRoot + "Check Optional Packages", false, 102)]
        public static void CheckOptionalPackages()
        {
            var missing = new List<OptionalPackage>();
            foreach (var pkg in OptionalPackages)
            {
                if (!pkg.IsInstalled())
                    missing.Add(pkg);
            }

            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Axiom — Optional Packages",
                    "All optional packages are already installed.\n\n" +
                    BuildInstalledSummary(),
                    "OK");
                return;
            }

            string summaryLines = "";
            foreach (var pkg in missing)
                summaryLines += $"  • {pkg.FriendlyName}  ({pkg.PackageId})\n";

            int choice = EditorUtility.DisplayDialogComplex(
                "Axiom — Optional Packages",
                $"The following optional packages are not installed:\n\n{summaryLines}\n" +
                "Install them to unlock additional Axiom features.\n\n" +
                "Install all, or choose individually?",
                "Install All", "Cancel", "Choose...");

            if (choice == 1) return;

            if (choice == 0)
            {
                var toInstall = new List<string>();
                foreach (var pkg in missing)
                    toInstall.Add(pkg.PackageId);
                StartPackageInstallation(toInstall);
                return;
            }

            var selected = new List<string>();
            foreach (var pkg in missing)
            {
                bool install = EditorUtility.DisplayDialog(
                    $"Install {pkg.FriendlyName}?",
                    $"{pkg.FeatureDescription}\n\nPackage: {pkg.PackageId}",
                    "Install", "Skip");

                if (install)
                    selected.Add(pkg.PackageId);
            }

            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("Axiom — No Changes",
                    "No packages were selected for installation.", "OK");
                return;
            }

            StartPackageInstallation(selected);
        }

        // ── Verify installation ───────────────────────────────────────────

        [MenuItem(MenuRoot + "Verify Installation", false, 200)]
        public static void VerifyInstallation()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            bool isUpm = IsUpmInstall();
            string report = "Axiom Installation Status\n=========================\n\n";

            report += $"Install Mode: {(isUpm ? "UPM Package" : "Development (Assets)")}\n";
            if (isUpm)
            {
                var info = PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly);
                if (info != null) report += $"Package Version: {info.version}\n";
                report += $"Package Path: {GetPackagePath()}\n";
            }
            report += "\n";

            // Workspace rules at project root
            report += "Root Workspace Files:\n";
            var fileDescriptions = new Dictionary<string, string>
            {
                { ".cursorrules",            "Agent API reference (Cursor IDE)" },
                { "project_instructions.md", "Command schema + implementation template" },
                { "CLAUDE.md",               "Claude Code complete reference" },
            };
            foreach (var kvp in fileDescriptions)
            {
                string targetPath = Path.Combine(projectRoot, kvp.Key);
                bool exists = File.Exists(targetPath);
                report += $"  [{(exists ? "+" : "X")}] {kvp.Key}";
                report += exists ? " — OK" : " — MISSING (run Install Workspace Rules)";
                report += $"  ({kvp.Value})\n";
            }

            // WorkspaceRules source location (helps debug install issues)
            if (isUpm)
            {
                string pkgPath = GetPackagePath();
                string tildePath = Path.Combine(pkgPath, UpmTildeFolder);
                string regularPath = Path.Combine(pkgPath, UpmRegularFolder);
                bool hasTilde = Directory.Exists(tildePath);
                bool hasRegular = Directory.Exists(regularPath);
                report += $"\nWorkspaceRules Source:\n";
                report += $"  [{(hasTilde ? "+" : "-")}] {UpmTildeFolder} (upm branch)\n";
                report += $"  [{(hasRegular ? "+" : "-")}] {UpmRegularFolder} (OpenUPM/main)\n";
            }

            // AgentBridge source folders
            report += "\nAgentBridge Source:\n";
            string basePath;
            if (isUpm)
                basePath = Path.Combine(GetPackagePath(), "Editor", "AgentBridge");
            else
                basePath = Path.Combine(Application.dataPath, "Axiom", "Editor", "AgentBridge");

            var folders = new[]
            {
                ("Core",        ""),
                ("Diagnostics", ""),
                ("Actions",     ""),
                ("MCP",         " (requires com.unity.ai.assistant)"),
            };
            int totalScripts = 0;
            foreach (var (sub, note) in folders)
            {
                string folderPath = Path.Combine(basePath, sub);
                bool exists = Directory.Exists(folderPath);
                int count = 0;
                if (exists)
                    count = Directory.GetFiles(folderPath, "*.cs", SearchOption.TopDirectoryOnly).Length;
                totalScripts += count;
                report += $"  [{(exists ? "+" : "X")}] {sub}/ — {(exists ? $"{count} scripts{note}" : "MISSING")}\n";
            }
            report += $"  Total: {totalScripts} source files (expected 48)\n";

            // Required dependencies
            report += "\nRequired Dependencies:\n";
            bool hasNewtonsoft = IsPackageInstalled("com.unity.nuget.newtonsoft-json");
            bool hasAssistant = IsPackageInstalled("com.unity.ai.assistant");
            report += $"  [{(hasNewtonsoft ? "+" : "X")}] Newtonsoft.Json — {(hasNewtonsoft ? "OK" : "MISSING")}\n";
            report += $"  [{(hasAssistant ? "+" : "X")}] Unity AI Assistant — {(hasAssistant ? "OK" : "MISSING")}\n";

            // Optional packages
            report += "\nOptional Packages:\n";
            foreach (var pkg in OptionalPackages)
            {
                bool installed = pkg.IsInstalled();
                string altNote = !string.IsNullOrEmpty(pkg.AltPackageId)
                    ? $" (or {pkg.AltPackageId})" : "";
                report += $"  [{(installed ? "+" : "-")}] {pkg.FriendlyName}\n";
                report += $"       {pkg.PackageId}{altNote} — {(installed ? "ENABLED" : "not installed")}\n";
                if (!installed)
                    report += $"       → Install to enable: {pkg.FeatureDescription}\n";
            }

            // Report output
            string reportsDir = Path.Combine(projectRoot, "AgentReports");
            bool reportsExist = Directory.Exists(reportsDir);
            report += $"\nReport Output:\n  [{(reportsExist ? "+" : "-")}] AgentReports/";
            report += reportsExist ? " — exists\n" : " — auto-created on first diagnostic run\n";

            Debug.Log($"[Axiom] Installation Verification:\n{report}");
            EditorUtility.DisplayDialog("Axiom — Verification", report, "OK");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        internal static bool IsPackageInstalled(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            try
            {
                string manifestPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath),
                    "Packages", "manifest.json");
                if (!File.Exists(manifestPath)) return false;
                string json = File.ReadAllText(manifestPath);
                return json.Contains($"\"{packageId}\":");
            }
            catch { return false; }
        }

        private static string BuildInstalledSummary()
        {
            string s = "Installed features:\n";
            foreach (var pkg in OptionalPackages)
                s += $"  [+] {pkg.FriendlyName}\n";
            return s;
        }

        internal static void StartPackageInstallation(List<string> packageIds)
        {
            s_PackageQueue.Clear();
            foreach (var id in packageIds)
                s_PackageQueue.Enqueue(id);
            s_TotalToInstall = packageIds.Count;
            s_InstalledCount = 0;

            string list = string.Join("\n", packageIds.ConvertAll(p => $"  • {p}"));
            Debug.Log($"[Axiom Installer] Queuing {packageIds.Count} package(s):\n{list}");

            EditorUtility.DisplayDialog(
                "Axiom — Installing Packages",
                $"Starting installation of {packageIds.Count} package(s):\n\n{list}\n\n" +
                "Unity will recompile when each package is ready.\n" +
                "You can monitor progress in Window > Package Manager.",
                "OK");

            ProcessNextPackage();
        }

        private static void ProcessNextPackage()
        {
            if (s_PackageQueue.Count == 0)
            {
                if (s_TotalToInstall > 0)
                {
                    EditorUtility.DisplayDialog(
                        "Axiom — Packages Installed",
                        $"Successfully installed {s_InstalledCount} of {s_TotalToInstall} package(s).\n\n" +
                        "Run  Tools > Axiom > Verify Installation  to confirm all features are enabled.",
                        "OK");
                }
                return;
            }

            string packageId = s_PackageQueue.Dequeue();
            s_AddRequest = Client.Add(packageId);
            Debug.Log($"[Axiom Installer] Installing: {packageId}");
            EditorApplication.update += MonitorCurrentPackage;
        }

        private static void MonitorCurrentPackage()
        {
            if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;

            EditorApplication.update -= MonitorCurrentPackage;
            var req = s_AddRequest;
            s_AddRequest = null;

            if (req.Status == StatusCode.Success)
            {
                s_InstalledCount++;
                Debug.Log($"[Axiom Installer] Installed: {req.Result.packageId}");
            }
            else
            {
                string err = req.Error?.message ?? "Unknown error";
                Debug.LogWarning($"[Axiom Installer] Failed to install: {err}");
                EditorUtility.DisplayDialog(
                    "Axiom — Package Install Warning",
                    $"A package installation returned an error:\n\n{err}\n\n" +
                    "Remaining packages in the queue will still be attempted.\n" +
                    "You can install failed packages manually via Window > Package Manager.",
                    "Continue");
            }

            ProcessNextPackage();
        }
    }
}
