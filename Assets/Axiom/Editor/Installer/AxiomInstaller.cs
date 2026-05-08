using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.IO;
using System.Collections.Generic;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;  // ADDED: UPM mode detection

namespace Axiom.Editor.Installer
{
    /// <summary>
    /// An optional package that unlocks conditional Axiom features.
    /// PackageId is the current/canonical package ID used for installation.
    /// AltPackageId handles renamed packages (e.g. com.unity.sentis → com.unity.ai.inference).
    /// </summary>
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
    /// Auto-detects install mode:
    ///   UPM:    Reads from Editor/WorkspaceRules~/ via PackageInfo.FindForAssembly()
    ///   Legacy: Reads TextAssets from Assets/Axiom/Editor/WorkspaceRules/
    ///
    /// Menu structure:
    ///   Tools/Axiom/Install Workspace Rules to Project Root  (100)
    ///   Tools/Axiom/Remove Workspace Rules from Project Root (101)
    ///   Tools/Axiom/Check Optional Packages                  (102)
    ///   Tools/Axiom/Verify Installation                      (200)
    /// </summary>
    public static class AxiomInstaller
    {
        private const string MenuRoot = "Tools/Axiom/";

        // Legacy Assets-based TextAsset path
        private const string LegacyWorkspaceRulesPath = "Assets/Axiom/Editor/WorkspaceRules";

        // UPM tilde folder (ships with package, not imported by Unity)
        private const string UpmWorkspaceRulesFolder = "Editor/WorkspaceRules~";

        // CHANGED: Added CLAUDE.md. Used by UPM mode — source filename → target at project root.
        private static readonly (string sourceFile, string targetFile, string description)[] RootFiles =
        {
            (".cursorrules",            ".cursorrules",            "Agent API reference (Cursor IDE)"),
            ("project_instructions.md", "project_instructions.md", "Command schema + implementation template"),
            ("CLAUDE.md",               "CLAUDE.md",               "Claude Code complete reference"),
        };

        // CHANGED: Added claude entry. Used by legacy mode — TextAsset name → target at project root.
        private static readonly (string sourceName, string targetName, string description)[] LegacyRootFiles =
        {
            ("cursorrules",          ".cursorrules",            "Agent API reference (Cursor IDE)"),
            ("project_instructions", "project_instructions.md", "Command schema + implementation template"),
            ("claude",               "CLAUDE.md",               "Claude Code complete reference"),
        };

        /// <summary>
        /// All optional packages that unlock conditional Axiom features behind #if guards.
        /// Ordered by importance / likelihood of wanting to install.
        /// </summary>
        internal static readonly OptionalPackage[] OptionalPackages =
        {
            new OptionalPackage(
                "com.unity.nuget.newtonsoft-json", null,
                "AXIOM_HAS_NEWTONSOFT",
                "Newtonsoft.Json",
                "Full JSON schema parsing in the gateway. Strongly recommended — enables richer command routing."),

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

            new OptionalPackage(
                "com.unity.ai.assistant", null,
                "AXIOM_HAS_UNITY_ASSISTANT",
                "Unity AI Assistant",
                "Native Unity MCP bridge — registers Axiom_Gateway, Axiom_Status, Axiom_ReadReport, Axiom_Verify, Axiom_Rules as MCP tools visible to Cursor / Claude Code / Windsurf."),
        };

        // ── Package installation queue ────────────────────────────────────

        private static readonly Queue<string> s_PackageQueue = new Queue<string>();
        private static AddRequest s_AddRequest;
        private static int s_TotalToInstall;
        private static int s_InstalledCount;

        // ── ADDED: UPM mode detection ─────────────────────────────────────

        private static bool IsUpmInstall()
        {
            return PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly) != null;
        }

        private static string GetPackagePath()
        {
            var info = PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly);
            return info?.resolvedPath;
        }

        // ── Workspace rules ───────────────────────────────────────────────

        [MenuItem(MenuRoot + "Install Workspace Rules to Project Root", false, 100)]
        public static void InstallWorkspaceRules()
        {
            // CHANGED: Branch between UPM and legacy install modes
            if (IsUpmInstall())
                InstallFromUpm();
            else
                InstallFromLegacy();
        }

        // ADDED: UPM mode — reads from Editor/WorkspaceRules~/ via PackageInfo
        private static void InstallFromUpm()
        {
            string pkgPath = GetPackagePath();
            string rulesPath = pkgPath != null ? Path.Combine(pkgPath, UpmWorkspaceRulesFolder) : null;

            if (rulesPath == null || !Directory.Exists(rulesPath))
            {
                EditorUtility.DisplayDialog("Axiom — Error",
                    "WorkspaceRules~ folder not found in package.\n" +
                    "Try reinstalling the package.", "OK");
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int installed = 0;
            int skipped = 0;

            foreach (var (sourceFile, targetFile, description) in RootFiles)
            {
                string src = Path.Combine(rulesPath, sourceFile);
                string dst = Path.Combine(projectRoot, targetFile);

                if (!File.Exists(src))
                {
                    Debug.LogWarning($"[Axiom] Source not found in package: {src}");
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
                $"Location: {projectRoot}",
                "OK");
        }

        // Original legacy mode — reads TextAssets from Assets/Axiom/Editor/WorkspaceRules/
        private static void InstallFromLegacy()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int installed = 0;
            int skipped = 0;

            foreach (var (sourceName, targetName, description) in LegacyRootFiles)
            {
                string sourcePath = $"{LegacyWorkspaceRulesPath}/{sourceName}.txt";
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
                $"Location: {projectRoot}",
                "OK");
        }

        [MenuItem(MenuRoot + "Remove Workspace Rules from Project Root", false, 101)]
        public static void RemoveWorkspaceRules()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            int removed = 0;

            // CHANGED: Uses RootFiles (which now includes CLAUDE.md)
            foreach (var (_, targetFile, _) in RootFiles)
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

            // Summary dialog with three choices
            string summaryLines = "";
            foreach (var pkg in missing)
                summaryLines += $"  • {pkg.FriendlyName}  ({pkg.PackageId})\n";

            int choice = EditorUtility.DisplayDialogComplex(
                "Axiom — Optional Packages",
                $"The following optional packages are not installed:\n\n{summaryLines}\n" +
                "Install them to unlock additional Axiom features.\n\n" +
                "Install all, or choose individually?",
                "Install All", "Cancel", "Choose...");

            if (choice == 1) return; // Cancel

            if (choice == 0) // Install All
            {
                var toInstall = new List<string>();
                foreach (var pkg in missing)
                    toInstall.Add(pkg.PackageId);
                StartPackageInstallation(toInstall);
                return;
            }

            // Choose individually
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

            // ADDED: Show install mode
            report += $"Install Mode: {(isUpm ? "UPM Package" : "Development (Assets)")}\n";
            if (isUpm)
            {
                var info = PackageInfo.FindForAssembly(typeof(AxiomInstaller).Assembly);
                if (info != null) report += $"Package Version: {info.version}\n";
            }
            report += "\n";

            // Root workspace files — CHANGED: now includes CLAUDE.md
            report += "Root Workspace Files:\n";
            foreach (var (_, targetFile, description) in RootFiles)
            {
                string targetPath = Path.Combine(projectRoot, targetFile);
                bool exists = File.Exists(targetPath);
                report += $"  [{(exists ? "+" : "X")}] {targetFile}";
                report += exists ? " — OK" : " — MISSING (run Install Workspace Rules)";
                report += $"  ({description})\n";
            }

            // AgentBridge source folders
            report += "\nAgentBridge Source:\n";
            var folders = new[]
            {
                ("Assets/Axiom/Editor/AgentBridge/Core",        ""),
                ("Assets/Axiom/Editor/AgentBridge/Diagnostics", ""),
                ("Assets/Axiom/Editor/AgentBridge/Actions",     ""),
                ("Assets/Axiom/Editor/AgentBridge/MCP",         " (compiled only with com.unity.ai.assistant)"),
            };
            int totalScripts = 0;
            foreach (var (folder, note) in folders)
            {
                bool exists = AssetDatabase.IsValidFolder(folder);
                int count = 0;
                if (exists)
                {
                    count = AssetDatabase.FindAssets("t:MonoScript", new[] { folder }).Length;
                    totalScripts += count;
                }
                report += $"  [{(exists ? "+" : "X")}] {folder}";
                report += exists ? $" — {count} scripts{note}" : " — MISSING";
                report += "\n";
            }
            report += $"  Total: {totalScripts} source files" +
                      $"  (expected 46 base + 2 MCP = 48 when ai.assistant installed)\n";

            // Assembly definition
            string asmdefPath = "Assets/Axiom/Editor/AgentBridge/AgentBridge.asmdef";
            bool asmdef = File.Exists(Path.Combine(projectRoot, asmdefPath));
            report += $"\nAssembly Definition:\n  [{(asmdef ? "+" : "X")}] {asmdefPath}\n";

            // Report output directory
            string reportsDir = Path.Combine(projectRoot, "AgentReports");
            bool reportsExist = Directory.Exists(reportsDir);
            report += $"\nReport Output:\n  [{(reportsExist ? "+" : "-")}] AgentReports/";
            report += reportsExist ? " — exists\n" : " — auto-created on first diagnostic run\n";

            // Optional packages
            report += "\nOptional Packages:\n";
            foreach (var pkg in OptionalPackages)
            {
                bool installed = pkg.IsInstalled();
                string status = installed ? "ENABLED" : "not installed";
                string altNote = !string.IsNullOrEmpty(pkg.AltPackageId)
                    ? $" (or {pkg.AltPackageId})" : "";
                report += $"  [{(installed ? "+" : "-")}] {pkg.FriendlyName}\n";
                report += $"       {pkg.PackageId}{altNote} — {status}\n";
                if (!installed)
                    report += $"       → Install to enable: {pkg.FeatureDescription}\n";
            }

            Debug.Log($"[Axiom] Installation Verification:\n{report}");
            EditorUtility.DisplayDialog("Axiom — Verification", report, "OK");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a package ID appears in Packages/manifest.json dependencies.
        /// Uses a simple string search — sufficient for UPM package IDs which are unique reverse-DNS names.
        /// </summary>
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
                // Match "com.example.package": to avoid false substring hits
                return json.Contains($"\"{packageId}\":");
            }
            catch
            {
                return false;
            }
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
