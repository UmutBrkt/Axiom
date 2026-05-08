using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine.SceneManagement;
using Axiom.Editor.AgentBridge.Core;
using Axiom.Editor.AgentBridge.Actions;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Orchestrates multiple Axiom diagnostic tools into a single unified project briefing.
    /// Solves the agent "cold start" problem — one file read gives a complete mental model.
    /// </summary>
    public static class ProjectOnboarder
    {
        public enum ProjectOnboarderMode
        {
            QuickBriefing,   // Mode A
            FullBriefing,    // Mode B
            FullWithHealth   // Mode C
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Project Onboarder \u2014 Mode A (Quick Briefing)")]
        public static void RunModeA() { GenerateReport(ProjectOnboarderMode.QuickBriefing); }

        [MenuItem("Axiom/AgentBridge/Project Onboarder \u2014 Mode B (Full Briefing)")]
        public static void RunModeB() { GenerateReport(ProjectOnboarderMode.FullBriefing); }

        [MenuItem("Axiom/AgentBridge/Project Onboarder \u2014 Mode C (Full Briefing + Health)")]
        public static void RunModeC() { GenerateReport(ProjectOnboarderMode.FullWithHealth); }

        // ─────────────────────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a unified project briefing by orchestrating multiple diagnostic tools.
        /// </summary>
        /// <param name="mode">Detail level: QuickBriefing, FullBriefing, or FullWithHealth.</param>
        /// <returns>File path of the generated briefing report.</returns>
        public static string GenerateReport(ProjectOnboarderMode mode)
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string modeLabel = mode switch
            {
                ProjectOnboarderMode.QuickBriefing  => "Quick",
                ProjectOnboarderMode.FullBriefing   => "Full",
                ProjectOnboarderMode.FullWithHealth => "Full + Health",
                _                                   => "Quick"
            };

            var activeScene = SceneManager.GetActiveScene();
            string sceneName = string.IsNullOrEmpty(activeScene.name) ? "(unsaved)" : activeScene.name;

            sb.AppendLine($"# Project Briefing \u2014 {modeLabel}");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {timestamp} | **Unity:** {Application.unityVersion} | **Scene:** {sceneName}");
            sb.AppendLine();

            AppendIdentitySection(sb);
            AppendScaleSection(sb);
            AppendHierarchySection(sb, sceneName);
            AppendCompilationSection(sb);
            AppendScriptFoldersSection(sb);

            if (mode == ProjectOnboarderMode.FullBriefing || mode == ProjectOnboarderMode.FullWithHealth)
            {
                AppendCodebaseArchitectureSection(sb);
                AppendDomainStateSection(sb);
                AppendPackagesSection(sb);
                AppendMissingScriptsSection(sb);
            }

            if (mode == ProjectOnboarderMode.FullWithHealth)
            {
                AppendHealthSection(sb);
            }

            string reportLabel = mode switch
            {
                ProjectOnboarderMode.QuickBriefing  => "quick",
                ProjectOnboarderMode.FullBriefing   => "full",
                ProjectOnboarderMode.FullWithHealth => "full_health",
                _                                   => "quick"
            };

            return OutputWriter.WriteReport($"project_onboarder_{reportLabel}", sb.ToString());
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode A Sections
        // ─────────────────────────────────────────────────────────────────────

        private static void AppendIdentitySection(StringBuilder sb)
        {
            sb.AppendLine("## Identity");
            try
            {
                string pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name
                    : "Built-in";

                var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                var scriptingBackend = PlayerSettings.GetScriptingBackend(buildTargetGroup);
                var apiLevel = PlayerSettings.GetApiCompatibilityLevel(buildTargetGroup);

                sb.AppendLine($"- Product: {PlayerSettings.productName}");
                sb.AppendLine($"- Company: {PlayerSettings.companyName}");
                sb.AppendLine($"- Platform: {EditorUserBuildSettings.activeBuildTarget}");
                sb.AppendLine($"- Pipeline: {pipeline}");
                sb.AppendLine($"- Color Space: {QualitySettings.activeColorSpace}");
                sb.AppendLine($"- Scripting: {scriptingBackend} / {apiLevel}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Identity collection failed: {ex.Message}]");
            }
            sb.AppendLine();
        }

        private static void AppendScaleSection(StringBuilder sb)
        {
            sb.AppendLine("## Scale");
            try
            {
                int totalAssets  = AssetDatabase.FindAssets("", new[] { "Assets" }).Length;
                int scriptCount  = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }).Length;
                int textureCount = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }).Length;
                int materialCount= AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Length;
                int prefabCount  = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Length;
                int sceneCount   = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Length;
                int assemblyCount= CompilationPipeline.GetAssemblies().Length;

                int scriptFolderCount = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                    .Select(g => Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(g)))
                    .Distinct()
                    .Count();

                sb.AppendLine($"- Total Assets: {totalAssets}");
                sb.AppendLine($"- Scripts: {scriptCount} across {scriptFolderCount} folders");
                sb.AppendLine($"- Textures: {textureCount} | Materials: {materialCount} | Prefabs: {prefabCount} | Scenes: {sceneCount}");
                sb.AppendLine($"- Assemblies: {assemblyCount}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Scale collection failed: {ex.Message}]");
            }
            sb.AppendLine();
        }

        private static void AppendHierarchySection(StringBuilder sb, string sceneName)
        {
            int rootCount = SceneManager.GetActiveScene().rootCount;
            sb.AppendLine($"## Active Scene: {sceneName} ({rootCount} root objects)");

            string hierarchyContent = CallAndCapture(() =>
                HierarchyLens.GenerateReport(
                    HierarchyLens.HierarchyMode.Components,
                    maxDepth: 2,
                    includeInactive: false));

            if (hierarchyContent.StartsWith("[Tool"))
            {
                sb.AppendLine(hierarchyContent);
            }
            else
            {
                // Strip the top-level title and Generated line to avoid duplication
                var lines = hierarchyContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("# Hierarchy Report")) continue;
                    if (line.StartsWith("**Generated:**")) continue;
                    sb.AppendLine(line);
                }
            }
            sb.AppendLine();
        }

        private static void AppendCompilationSection(StringBuilder sb)
        {
            string compilationContent = CallAndCapture(() =>
                LogMirror.GenerateReport(LogMirror.LogMirrorMode.CompilationReport));

            string status = "UNKNOWN";
            if (compilationContent.Contains("**Status:** CLEAN"))
                status = "CLEAN";
            else if (compilationContent.Contains("**Compile Errors:** True") || compilationContent.Contains("HAS_ERRORS"))
                status = "HAS_ERRORS";
            else if (compilationContent.Contains("**Compiling:** True"))
                status = "COMPILING";
            else if (compilationContent.Contains("CLEAN"))
                status = "CLEAN";
            else if (compilationContent.StartsWith("[Tool"))
                status = compilationContent;

            sb.AppendLine($"## Compilation: {status}");
            sb.AppendLine();
        }

        private static void AppendScriptFoldersSection(StringBuilder sb)
        {
            sb.AppendLine("## Top-Level Script Folders");
            try
            {
                var folderCounts = new Dictionary<string, int>();

                var scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
                foreach (var guid in scriptGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var parts = assetPath.Split('/');
                    if (parts.Length >= 3)
                    {
                        string topFolder = $"Assets/{parts[1]}";
                        if (!folderCounts.ContainsKey(topFolder))
                            folderCounts[topFolder] = 0;
                        folderCounts[topFolder]++;
                    }
                }

                if (folderCounts.Count == 0)
                {
                    sb.AppendLine("(No .cs files found directly under Assets/ subfolders)");
                }
                else
                {
                    foreach (var kvp in folderCounts.OrderByDescending(x => x.Value))
                        sb.AppendLine($"- {kvp.Key}: {kvp.Value} scripts");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Script folder scan failed: {ex.Message}]");
            }
            sb.AppendLine();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode B Sections
        // ─────────────────────────────────────────────────────────────────────

        private static void AppendCodebaseArchitectureSection(StringBuilder sb)
        {
            sb.AppendLine("## Codebase Architecture");
            sb.AppendLine();

            // Assemblies
            sb.AppendLine("### Assemblies");
            string asmContent = CallAndCapture(() =>
                ScriptAnalyzer.GenerateReport(ScriptAnalyzer.ScriptAnalyzerMode.AssemblyDefinitions));
            AppendContentSkippingTitle(sb, asmContent);

            // Key Classes
            sb.AppendLine("### Key Classes");
            string classContent = CallAndCapture(() =>
                ScriptAnalyzer.GenerateReport(ScriptAnalyzer.ScriptAnalyzerMode.ClassMap));
            AppendCondensedClassMap(sb, classContent);
            sb.AppendLine();
        }

        private static void AppendContentSkippingTitle(StringBuilder sb, string content)
        {
            if (content.StartsWith("[Tool"))
            {
                sb.AppendLine(content);
                return;
            }
            var lines = content.Split('\n');
            bool pastTitle = false;
            foreach (var line in lines)
            {
                if (!pastTitle && line.StartsWith("# ")) { pastTitle = true; continue; }
                if (!pastTitle && line.StartsWith("**Generated:**")) continue;
                if (pastTitle) sb.AppendLine(line);
            }
        }

        private static void AppendCondensedClassMap(StringBuilder sb, string classMapContent)
        {
            if (classMapContent.StartsWith("[Tool"))
            {
                sb.AppendLine(classMapContent);
                return;
            }

            var lines = classMapContent.Split('\n');
            int classLinesAdded = 0;
            int truncatedCount = 0;
            bool inRelevantSection = false;
            var output = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("# ") || line.StartsWith("**Generated:**")) continue;

                // Track relevant sections
                if (line.StartsWith("### MonoBehaviour") || line.StartsWith("### NetworkBehaviour") ||
                    line.StartsWith("### ScriptableObject") || line.StartsWith("### Editor"))
                {
                    inRelevantSection = true;
                    output.Add(line);
                    continue;
                }

                // Stop at irrelevant sections
                if (line.StartsWith("### ") && inRelevantSection &&
                    !line.Contains("MonoBehaviour") && !line.Contains("NetworkBehaviour") &&
                    !line.Contains("ScriptableObject") && !line.Contains("Editor"))
                {
                    inRelevantSection = false;
                    continue;
                }

                if (!inRelevantSection) continue;

                // Skip auto-generated classes
                if (line.Contains("< ") || (line.Contains("<") && line.Contains(">"))) continue;
                if (line.Contains("__")) continue;

                bool isClassLine = line.StartsWith("- ") || line.StartsWith("| ");
                if (isClassLine)
                {
                    if (classLinesAdded >= 50) { truncatedCount++; continue; }
                    output.Add(line);
                    classLinesAdded++;
                }
                else
                {
                    output.Add(line);
                }
            }

            foreach (var line in output)
                sb.AppendLine(line);

            if (truncatedCount > 0)
                sb.AppendLine($"*{truncatedCount} additional class(es) omitted. Run `script_analyzer` ClassMap mode for full list.*");
        }

        private static void AppendDomainStateSection(StringBuilder sb)
        {
            sb.AppendLine("## Domain State");
            sb.AppendLine();

            sb.AppendLine("### Physics");
            string physicsContent = CallAndCapture(() =>
                PhysicsReporter.GenerateReport(PhysicsReporter.PhysicsReporterMode.ColliderCensus));
            AppendLeadingSummaryLines(sb, physicsContent, 8);

            sb.AppendLine("### Rendering");
            string renderContent = CallAndCapture(() =>
                RenderAuditor.GenerateReport(RenderAuditor.RenderAuditorMode.PipelineSummary));
            AppendLeadingSummaryLines(sb, renderContent, 8);
            sb.AppendLine();
        }

        private static void AppendLeadingSummaryLines(StringBuilder sb, string content, int maxLines)
        {
            if (content.StartsWith("[Tool"))
            {
                sb.AppendLine(content);
                return;
            }
            var lines = content.Split('\n');
            int count = 0;
            bool pastHeader = false;
            foreach (var line in lines)
            {
                if (!pastHeader && (line.StartsWith("# ") || line.StartsWith("**Generated:"))) continue;
                pastHeader = true;
                if (string.IsNullOrWhiteSpace(line) && count == 0) continue;
                if (count >= maxLines) break;
                sb.AppendLine(line);
                if (!string.IsNullOrWhiteSpace(line)) count++;
            }
        }

        private static void AppendPackagesSection(StringBuilder sb)
        {
            sb.AppendLine("## Packages (Project-Specific)");
            try
            {
                var result = PackageManagerActions.ListPackages();
                if (result.Success)
                {
                    var lines = result.Message.Split('\n');
                    int count = 0;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        // Skip Unity's own packages
                        if (line.Contains("com.unity.")) continue;
                        if (count >= 20) break;
                        sb.AppendLine(line);
                        count++;
                    }
                    if (count == 0)
                        sb.AppendLine("(No non-Unity packages found)");
                }
                else
                {
                    sb.AppendLine($"[Packages failed: {result.Message}]");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Packages failed: {ex.Message}]");
            }
            sb.AppendLine();
        }

        private static void AppendMissingScriptsSection(StringBuilder sb)
        {
            string content = CallAndCapture(() =>
                ReferenceScanner.GenerateReport(ReferenceScannerMode.MissingScripts));

            if (content.StartsWith("[Tool"))
            {
                sb.AppendLine($"## Missing Scripts: {content}");
                sb.AppendLine();
                return;
            }

            // Extract summary count from the report
            string summary = "(see report)";
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("**") || line.Contains("missing") || line.Contains("Total") || line.Contains("Found"))
                {
                    string trimmed = line.Trim().TrimStart('#').Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        summary = trimmed;
                        break;
                    }
                }
            }

            sb.AppendLine($"## Missing Scripts: {summary}");
            sb.AppendLine();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode C Section
        // ─────────────────────────────────────────────────────────────────────

        private static void AppendHealthSection(StringBuilder sb)
        {
            sb.AppendLine("## Project Health");

            string ciContent = CallAndCapture(() =>
                CISweep.GenerateReport(CISweep.CISweepMode.QuickHealthCheck));

            if (ciContent.StartsWith("[Tool"))
            {
                sb.AppendLine(ciContent);
                sb.AppendLine();
                return;
            }

            // Include the CISweep output, skipping only its top-level title
            var lines = ciContent.Split('\n');
            bool pastTitle = false;
            foreach (var line in lines)
            {
                if (!pastTitle && line.StartsWith("# CI Sweep")) { pastTitle = true; continue; }
                if (!pastTitle) continue;
                if (line.StartsWith("**Generated:**")) continue;
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CallAndCapture Helper
        // ─────────────────────────────────────────────────────────────────────

        private static string CallAndCapture(Func<string> toolCall)
        {
            try
            {
                string reportPath = toolCall();
                if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
                {
                    string content = File.ReadAllText(reportPath);
                    File.Delete(reportPath);
                    return content;
                }
                return "[Tool returned no output]";
            }
            catch (Exception ex)
            {
                return $"[Tool failed: {ex.Message}]";
            }
        }
    }
}
