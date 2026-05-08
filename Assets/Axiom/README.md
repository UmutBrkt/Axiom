# Axiom — Diagnostic Bridge for Unity 6

[![openupm](https://img.shields.io/npm/v/com.axiom.agentbridge?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.axiom.agentbridge/)

Token-efficient agentic toolset for Unity 6 (6000.3 LTS). Replaces expensive MCP browsing with bespoke editor script reports.

**22 diagnostic tools** | **16 action tools** | **JSON command gateway** | **Native Unity MCP integration**

## Installation

### Via OpenUPM (Recommended)

```bash
openupm add com.axiom.agentbridge
```

### Via Git URL

In Unity: **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/UmutBrkt/Axiom.git#upm
```

To pin a specific version:

```
https://github.com/UmutBrkt/Axiom.git#v1.0.0
```

### Post-Install

After import, a dialog prompts you to deploy workspace rules to your project root. Or manually: **Tools > Axiom > Install Workspace Rules to Project Root**.

Optional: **Tools > Axiom > Check Optional Packages** to enable feature-unlocking packages.

## How It Works

```
Report → Execute → Verify
```

Instead of an AI agent crawling the Unity editor through expensive MCP tool calls, Axiom's diagnostic tools produce clean Markdown reports. The agent reads one file and knows exactly what it needs.

Three execution paths reach the same tools:

1. **Unity MCP (native):** AI client calls `Axiom_Gateway` with JSON payload
2. **execute_script:** `AgentBridgeGateway.Execute(jsonString)`
3. **Direct C#:** `HierarchyLens.GenerateReport(HierarchyMode.Components, rootPath: "Player/")`

## Diagnostic Tools (22)

| Tool | Modes | Reports |
|:---|:---|:---|
| HierarchyLens | A–F | Scene structure → full inspector dump |
| LogMirror | A–F | Console logs, compilation, profiler |
| ComponentInspector | A–F | Property values, cross-compare, missing refs |
| ReferenceScanner | A–G | Null refs, missing scripts, material audit |
| ProjectCartographer | A–G | File tree, dependencies, orphans, GUIDs |
| SceneDiff | A–D | Scene snapshots and diffs |
| SmartSearch | 3 modes | Name, component, tag, layer, asset type |
| SettingsReporter | A–G | Full project settings dump |
| ScriptAnalyzer | A–E | Class map, dependencies, assemblies |
| PrefabAuditor | A–E | Variants, overrides, nesting |
| UIToolkitInspector | A–E | Visual tree, styles, accessibility |
| TestRunner | A–D | Test list, run, coverage |
| PhysicsReporter | A–F | Colliders, layers, rigidbodies, joints |
| AnimationInspector | A–F | Controllers, states, events, avatar |
| AudioReporter | A–D | Sources, mixers, spatial audio |
| NavMeshInspector | A–E | Agents, surfaces, reachability |
| ShaderAuditor | A–F | Materials, keywords, GPU compat |
| RenderAuditor | A–G | Pipeline, GPU Drawer, STP, lights |
| AccessibilityValidator | A–D | Screen reader, contrast, input, text |
| CISweep | A–C | Health check, full audit, pre-release |
| ProjectOnboarder | A–C | Unified briefing (one file = know the project) |
| ProjectContext | A–C | Find/summarize project context docs |

## Action Tools (16)

SceneActions, ComponentActions, WiringUtility, AssetActions, SettingsActions, RenderActions, BuildProfileActions, PackageManagerActions, PlayModeActions, ScreenCaptureActions, PrefabActions, InputSimulationActions, BuildPipelineHooks, MultiplayerActions, VisionAnalysis, SentisActions

## Optional Dependencies

All behind `#if` guards — compiles cleanly without any of them.

| Package | Enables |
|:---|:---|
| com.unity.nuget.newtonsoft-json | Full JSON gateway parsing |
| com.unity.inputsystem | InputSimulationActions |
| com.unity.multiplayer.playmode | MultiplayerActions |
| com.unity.sentis | SentisActions |
| com.unity.ai.assistant | Native MCP tools (5 tools) |

## Requirements

- **Unity 6** (6000.3 LTS or later)

## License

MIT License — see [LICENSE](LICENSE)
