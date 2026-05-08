using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

namespace Axiom.Editor.AgentBridge.Diagnostics
{
    /// <summary>
    /// Scans the project filesystem for human-written context documents (READMEs, design docs,
    /// implementation plans, changelogs) and surfaces them with a recommended reading order.
    /// Does NOT use AssetDatabase — many context files live outside Assets/.
    /// </summary>
    public static class ProjectContext
    {
        public enum ProjectContextMode
        {
            Discovery,  // Mode A — find context files, recommended reading order
            Summary,    // Mode B — find + extract condensed summaries
            Import      // Mode C — read specific files/folders and summarize
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Scan Patterns
        // ─────────────────────────────────────────────────────────────────────

        private static readonly string[] ContextFileNames = new[]
        {
            "README.md", "README.txt", "README",
            "ARCHITECTURE.md", "DESIGN.md", "DECISIONS.md",
            "CHANGELOG.md", "CHANGELOG.txt", "CHANGELOG",
            "TODO.md", "TODO.txt",
            "CLAUDE.md", "AGENTS.md",
            ".cursorrules",
            "project_instructions.md",
        };

        private static readonly string[] ContextFilePatterns = new[]
        {
            "*.plan.md", "*.design.md", "*.gdd.md",
        };

        private static readonly string[] ContextFolderNames = new[]
        {
            "Docs", "Documentation", "Design", "ImplementationPlans",
            "Plans", "Architecture", "Specs",
        };

        private static readonly string[] ContextSubfolders = new[]
        {
            "AgentReports",
        };

        private static readonly string[] SkipFolders = new[]
        {
            "Library", "Temp", "Obj", "Logs", "Builds", ".git", "node_modules", "Packages",
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Project Context \u2014 Mode A (Discovery)")]
        public static void RunModeA() { GenerateReport(ProjectContextMode.Discovery); }

        [MenuItem("Axiom/AgentBridge/Project Context \u2014 Mode B (Summary)")]
        public static void RunModeB() { GenerateReport(ProjectContextMode.Summary); }

        // ─────────────────────────────────────────────────────────────────────
        //  Public Entry Point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans the project for context documents and produces a discovery or summary report.
        /// </summary>
        /// <param name="mode">Discovery, Summary, or Import.</param>
        /// <param name="importPaths">For Mode C only: specific file or folder paths to read.</param>
        /// <param name="maxCharsPerFile">Maximum characters to extract per file in Mode B/C. Default 2000.</param>
        /// <param name="maxTotalChars">Maximum total characters across all files in Mode B/C. Default 30000.</param>
        /// <returns>File path of the generated context report.</returns>
        public static string GenerateReport(
            ProjectContextMode mode,
            string[] importPaths = null,
            int maxCharsPerFile = 2000,
            int maxTotalChars = 30000)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var sb = new StringBuilder();

            switch (mode)
            {
                case ProjectContextMode.Discovery:
                    BuildDiscoveryReport(sb, projectRoot, timestamp);
                    break;

                case ProjectContextMode.Summary:
                    BuildSummaryReport(sb, projectRoot, timestamp, maxCharsPerFile, maxTotalChars);
                    break;

                case ProjectContextMode.Import:
                    BuildImportReport(sb, projectRoot, timestamp, importPaths, maxCharsPerFile, maxTotalChars);
                    break;
            }

            string reportLabel = mode switch
            {
                ProjectContextMode.Discovery => "discovery",
                ProjectContextMode.Summary   => "summary",
                ProjectContextMode.Import    => "import",
                _                            => "discovery"
            };

            return OutputWriter.WriteReport($"project_context_{reportLabel}", sb.ToString());
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode A: Discovery
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildDiscoveryReport(StringBuilder sb, string projectRoot, string timestamp)
        {
            var foundFiles = DiscoverContextFiles(projectRoot);

            sb.AppendLine("# Project Context \u2014 Discovery");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {timestamp} | **Project Root:** {projectRoot}");
            sb.AppendLine();
            sb.AppendLine($"## Found Context Files ({foundFiles.Count})");
            sb.AppendLine();

            if (foundFiles.Count == 0)
            {
                sb.AppendLine("(No context files found)");
            }
            else
            {
                sb.AppendLine("| File | Size | Modified | Location |");
                sb.AppendLine("|:---|:---|:---|:---|");

                foreach (var entry in foundFiles)
                {
                    string relPath = MakeRelative(projectRoot, entry.FullName);
                    string sizeStr = FormatFileSize(entry.Length);
                    string modStr  = entry.LastWriteTime.ToString("yyyy-MM-dd");
                    string location= ClassifyLocation(projectRoot, entry.FullName);
                    sb.AppendLine($"| {relPath} | {sizeStr} | {modStr} | {location} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Recommended Reading Order");
            AppendReadingOrder(sb, projectRoot, foundFiles);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode B: Summary
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildSummaryReport(
            StringBuilder sb, string projectRoot, string timestamp,
            int maxCharsPerFile, int maxTotalChars)
        {
            var foundFiles = DiscoverContextFiles(projectRoot);

            sb.AppendLine("# Project Context \u2014 Summary");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {timestamp} | **Project Root:** {projectRoot}");
            sb.AppendLine();
            sb.AppendLine($"## Found Context Files ({foundFiles.Count})");
            sb.AppendLine();

            if (foundFiles.Count > 0)
            {
                sb.AppendLine("| File | Size | Modified | Location |");
                sb.AppendLine("|:---|:---|:---|:---|");

                foreach (var entry in foundFiles)
                {
                    string relPath = MakeRelative(projectRoot, entry.FullName);
                    string sizeStr = FormatFileSize(entry.Length);
                    string modStr  = entry.LastWriteTime.ToString("yyyy-MM-dd");
                    string location= ClassifyLocation(projectRoot, entry.FullName);
                    sb.AppendLine($"| {relPath} | {sizeStr} | {modStr} | {location} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Recommended Reading Order");
            AppendReadingOrder(sb, projectRoot, foundFiles);

            sb.AppendLine();
            sb.AppendLine("## File Summaries");
            sb.AppendLine();

            int totalCharsUsed = 0;
            foreach (var entry in foundFiles)
            {
                if (totalCharsUsed >= maxTotalChars)
                {
                    sb.AppendLine($"*[Budget exhausted — {foundFiles.Count - foundFiles.IndexOf(entry)} file(s) skipped. Increase maxTotalChars or use Mode C with specific paths.]*");
                    break;
                }

                string relPath = MakeRelative(projectRoot, entry.FullName);
                string sizeStr = FormatFileSize(entry.Length);

                try
                {
                    string content = File.ReadAllText(entry.FullName);
                    int budget = Math.Min(maxCharsPerFile, maxTotalChars - totalCharsUsed);
                    string summary = ExtractSummary(content, budget);
                    totalCharsUsed += summary.Length;

                    bool truncated = content.Length > maxCharsPerFile;
                    string truncNote = truncated
                        ? $" \u2014 showing first {budget} chars"
                        : "";

                    sb.AppendLine($"### {relPath} ({sizeStr}{truncNote})");
                    sb.AppendLine();
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"### {relPath} ({sizeStr})");
                    sb.AppendLine();
                    sb.AppendLine($"[Failed to read: {ex.Message}]");
                    sb.AppendLine();
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mode C: Import
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildImportReport(
            StringBuilder sb, string projectRoot, string timestamp,
            string[] importPaths, int maxCharsPerFile, int maxTotalChars)
        {
            sb.AppendLine("# Project Context \u2014 Import");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {timestamp} | **Project Root:** {projectRoot}");
            sb.AppendLine();

            if (importPaths == null || importPaths.Length == 0)
            {
                sb.AppendLine("*No import paths specified. Provide paths via the importPaths parameter or scope.object_names in the gateway.*");
                return;
            }

            var filesToRead = new List<FileInfo>();

            foreach (var rawPath in importPaths)
            {
                string fullPath = Path.IsPathRooted(rawPath)
                    ? rawPath
                    : Path.Combine(projectRoot, rawPath);

                if (Directory.Exists(fullPath))
                {
                    // Enumerate .md and .txt files in the folder (non-recursive)
                    var dirFiles = Directory.GetFiles(fullPath, "*.md")
                        .Concat(Directory.GetFiles(fullPath, "*.txt"))
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.Name);
                    filesToRead.AddRange(dirFiles);
                }
                else if (File.Exists(fullPath))
                {
                    filesToRead.Add(new FileInfo(fullPath));
                }
                else
                {
                    sb.AppendLine($"*[Path not found: {rawPath}]*");
                }
            }

            sb.AppendLine($"## Importing {filesToRead.Count} File(s)");
            sb.AppendLine();
            sb.AppendLine("## File Summaries");
            sb.AppendLine();

            int totalCharsUsed = 0;
            foreach (var entry in filesToRead)
            {
                if (totalCharsUsed >= maxTotalChars)
                {
                    int remaining = filesToRead.Count - filesToRead.IndexOf(entry);
                    sb.AppendLine($"*[Budget exhausted — {remaining} file(s) skipped.]*");
                    break;
                }

                string relPath = MakeRelative(projectRoot, entry.FullName);
                string sizeStr = FormatFileSize(entry.Length);

                try
                {
                    string content = File.ReadAllText(entry.FullName);
                    int budget = Math.Min(maxCharsPerFile, maxTotalChars - totalCharsUsed);
                    string summary = ExtractSummary(content, budget);
                    totalCharsUsed += summary.Length;

                    bool truncated = content.Length > maxCharsPerFile;
                    string truncNote = truncated ? $" \u2014 showing first {budget} chars" : "";

                    sb.AppendLine($"### {relPath} ({sizeStr}{truncNote})");
                    sb.AppendLine();
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"### {relPath} ({sizeStr})");
                    sb.AppendLine();
                    sb.AppendLine($"[Failed to read: {ex.Message}]");
                    sb.AppendLine();
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  File Discovery
        // ─────────────────────────────────────────────────────────────────────

        private static List<FileInfo> DiscoverContextFiles(string projectRoot)
        {
            var found = new List<FileInfo>();
            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string path)
            {
                if (seen.Contains(path)) return;
                seen.Add(path);
                if (File.Exists(path))
                    found.Add(new FileInfo(path));
            }

            // 1. Check project root for each context filename
            foreach (var name in ContextFileNames)
            {
                Add(Path.Combine(projectRoot, name));
            }

            // 2. Check Assets/ root for each context filename
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assetsRoot))
            {
                foreach (var name in ContextFileNames)
                    Add(Path.Combine(assetsRoot, name));
            }

            // 3. Scan for context folders at project root level
            try
            {
                foreach (var dir in Directory.GetDirectories(projectRoot))
                {
                    string dirName = Path.GetFileName(dir);
                    if (ShouldSkipFolder(dirName)) continue;

                    if (ContextFolderNames.Any(f => string.Equals(f, dirName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // List all .md and .txt files inside (recursive)
                        CollectMarkdownFiles(dir, found, seen, maxDepth: 5);
                    }

                    // Also check subfolder names
                    if (ContextSubfolders.Any(f => string.Equals(f, dirName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Check for specific well-known files
                        Add(Path.Combine(dir, "StatusUpdate.md"));
                        Add(Path.Combine(dir, "ProjectBriefing.md"));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectContext] Folder scan error: {ex.Message}");
            }

            // 4. AgentReports — look for StatusUpdate.md and ProjectBriefing.md
            string agentReports = Path.Combine(projectRoot, "AgentReports");
            if (Directory.Exists(agentReports))
            {
                Add(Path.Combine(agentReports, "StatusUpdate.md"));
                Add(Path.Combine(agentReports, "ProjectBriefing.md"));
            }

            // 5. Recurse into Assets/ for README.md at up to 3 levels deep
            if (Directory.Exists(assetsRoot))
            {
                ScanForReadme(assetsRoot, found, seen, currentDepth: 0, maxDepth: 3);
            }

            // 6. Apply context file patterns to context folders
            try
            {
                foreach (var dir in Directory.GetDirectories(projectRoot, "*", SearchOption.AllDirectories))
                {
                    string dirName = Path.GetFileName(dir);
                    if (ShouldSkipFolder(dirName)) continue;
                    if (!ContextFolderNames.Any(f => string.Equals(f, dirName, StringComparison.OrdinalIgnoreCase))) continue;

                    foreach (var pattern in ContextFilePatterns)
                    {
                        foreach (var file in Directory.GetFiles(dir, pattern))
                            Add(file);
                    }
                }
            }
            catch { /* Non-critical */ }

            // Sort by priority tier, then by last write time descending
            found.Sort((a, b) =>
            {
                int tierA = GetPriorityTier(projectRoot, a.FullName);
                int tierB = GetPriorityTier(projectRoot, b.FullName);
                if (tierA != tierB) return tierA.CompareTo(tierB);
                return b.LastWriteTime.CompareTo(a.LastWriteTime);
            });

            return found;
        }

        private static void CollectMarkdownFiles(string dir, List<FileInfo> found, HashSet<string> seen, int maxDepth)
        {
            if (maxDepth < 0) return;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.md"))
                {
                    if (seen.Contains(file)) continue;
                    seen.Add(file);
                    found.Add(new FileInfo(file));
                }
                foreach (var file in Directory.GetFiles(dir, "*.txt"))
                {
                    if (seen.Contains(file)) continue;
                    seen.Add(file);
                    found.Add(new FileInfo(file));
                }
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(subDir);
                    if (!ShouldSkipFolder(name))
                        CollectMarkdownFiles(subDir, found, seen, maxDepth - 1);
                }
            }
            catch { /* Non-critical */ }
        }

        private static void ScanForReadme(string dir, List<FileInfo> found, HashSet<string> seen, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;
            try
            {
                string readmePath = Path.Combine(dir, "README.md");
                if (File.Exists(readmePath) && !seen.Contains(readmePath))
                {
                    seen.Add(readmePath);
                    found.Add(new FileInfo(readmePath));
                }
                if (currentDepth < maxDepth)
                {
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(subDir);
                        if (!ShouldSkipFolder(name))
                            ScanForReadme(subDir, found, seen, currentDepth + 1, maxDepth);
                    }
                }
            }
            catch { /* Non-critical */ }
        }

        private static bool ShouldSkipFolder(string folderName)
        {
            return SkipFolders.Any(s => string.Equals(s, folderName, StringComparison.OrdinalIgnoreCase));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Reading Order + Priority
        // ─────────────────────────────────────────────────────────────────────

        private static int GetPriorityTier(string projectRoot, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string relPath  = MakeRelative(projectRoot, filePath).Replace('\\', '/');

            // Tier 1: Operating rules
            if (string.Equals(fileName, ".cursorrules", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(fileName, "project_instructions.md", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(fileName, "AGENTS.md", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(fileName, "CLAUDE.md", StringComparison.OrdinalIgnoreCase)) return 1;

            // Tier 2: Project briefings
            if (relPath.Contains("AgentReports/ProjectBriefing")) return 2;
            if (relPath.Contains("AgentReports/StatusUpdate")) return 2;

            // Tier 3: Root README
            if (string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase) &&
                Path.GetDirectoryName(filePath) == projectRoot) return 3;

            // Tier 4: Design docs / implementation plans
            if (relPath.Contains("ImplementationPlans") || relPath.Contains("Design") ||
                relPath.Contains("Docs") || relPath.Contains("Architecture"))
                return 4;

            // Tier 5: Everything else
            return 5;
        }

        private static void AppendReadingOrder(StringBuilder sb, string projectRoot, List<FileInfo> files)
        {
            int order = 1;

            void TryAddEntry(string label, Func<FileInfo, bool> predicate)
            {
                var match = files.FirstOrDefault(predicate);
                if (match != null)
                {
                    string relPath = MakeRelative(projectRoot, match.FullName);
                    sb.AppendLine($"{order++}. `{relPath}` \u2014 {label}");
                }
            }

            TryAddEntry("operating rules and command schema",
                f => string.Equals(f.Name, ".cursorrules", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(f.Name, "project_instructions.md", StringComparison.OrdinalIgnoreCase));

            TryAddEntry("full project state",
                f => f.Name.Equals("ProjectBriefing.md", StringComparison.OrdinalIgnoreCase));

            TryAddEntry("current state summary",
                f => f.Name.Equals("StatusUpdate.md", StringComparison.OrdinalIgnoreCase));

            TryAddEntry("project overview",
                f => f.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) &&
                     Path.GetDirectoryName(f.FullName) == projectRoot);

            // Design / implementation docs
            var designDocs = files
                .Where(f => GetPriorityTier(projectRoot, f.FullName) == 4)
                .Take(5)
                .ToList();
            foreach (var doc in designDocs)
            {
                string relPath = MakeRelative(projectRoot, doc.FullName);
                sb.AppendLine($"{order++}. `{relPath}` \u2014 design / implementation reference");
            }

            // Other context files (by modification date)
            var others = files
                .Where(f => GetPriorityTier(projectRoot, f.FullName) == 5)
                .OrderByDescending(f => f.LastWriteTime)
                .Take(5)
                .ToList();
            foreach (var doc in others)
            {
                string relPath = MakeRelative(projectRoot, doc.FullName);
                sb.AppendLine($"{order++}. `{relPath}`");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Smart Extraction Helper
        // ─────────────────────────────────────────────────────────────────────

        private static string ExtractSummary(string content, int maxChars)
        {
            if (content.Length <= maxChars)
                return content;

            var lines = content.Split('\n');
            var sb = new StringBuilder();
            bool inFirstParagraph = true;
            int headerCount = 0;

            foreach (var line in lines)
            {
                if (sb.Length >= maxChars)
                {
                    sb.AppendLine($"\n[Truncated at {maxChars} chars \u2014 {content.Length} total]");
                    break;
                }

                if (line.StartsWith("# ") || line.StartsWith("## "))
                {
                    sb.AppendLine(line);
                    inFirstParagraph = true;
                    headerCount++;
                }
                else if (inFirstParagraph && !string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                }
                else if (string.IsNullOrWhiteSpace(line) && inFirstParagraph && headerCount > 0)
                {
                    inFirstParagraph = false;
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string MakeRelative(string projectRoot, string fullPath)
        {
            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                string rel = fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rel.Replace('\\', '/');
            }
            return fullPath;
        }

        private static string ClassifyLocation(string projectRoot, string fullPath)
        {
            string rel = MakeRelative(projectRoot, fullPath).Replace('\\', '/');
            if (rel.StartsWith("AgentReports/")) return "Agent Reports";
            if (rel.StartsWith("ImplementationPlans/Docs/") || rel.StartsWith("Docs/")) return "Design Docs";
            if (rel.StartsWith("ImplementationPlans/")) return "Implementation Plans";
            if (rel.StartsWith("Assets/")) return "In-project docs";
            if (!rel.Contains("/")) return "Project root";
            return Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "Project root";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
