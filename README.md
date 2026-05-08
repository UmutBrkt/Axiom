# Axiom — Diagnostic Bridge for Unity 6

**Token-efficient agentic toolset for Unity 6.** Replaces expensive MCP browsing with bespoke editor script reports that AI agents read in a single call.

**22 diagnostic tools** (~115 detail modes) ·  **16 action tools** (~110 operations) · **JSON command gateway** · **Native Unity MCP integration**

---

# Diagnostic Bridge Philosophy

This project uses the **Axiom Diagnostic Bridge** — a token-efficient layer that replaces expensive MCP browsing with bespoke editor script reports. Instead of the agent searching a dark room with a flashlight (MCP browsing), it reads a neatly printed map (editor script reports).

**Fundamental Workflow:**

```
Report → Execute → Verify
```

Every task follows: **Pre-Flight Diagnostic → Execution → Post-Flight Verification → Cleanup.**

1. **Report:** Run a diagnostic tool. Read from `AgentReports/`.
2. **Execute:** Write/edit code or modify the scene based on the report.
3. **Verify:** Run post-flight diagnostic to confirm changes, detect regressions.
4. **Cleanup:** Delete temp scripts, clear snapshots if unneeded, update StatusUpdate.md.

**Why this matters:**

| Approach                               | Token Cost                                        | Accuracy                                        | Multi-Turn Loops                               |
| :------------------------------------- | :------------------------------------------------ | :---------------------------------------------- | :--------------------------------------------- |
| MCP Browsing (agent crawls hierarchy)  | High — raw JSON of entire scene tree             | Prone to hallucinated clicks and missed objects | Many — "I see X, now let me check Y, then Z"  |
| Diagnostic Bridge (agent reads report) | Low — clean formatted output, only what's needed | 100% — code-driven reflection, no guessing     | Minimal — X, Y, and Z delivered in one report |

---

## Installation

**Via OpenUPM** [![openupm](https://img.shields.io/npm/v/com.axiom.agentbridge?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.axiom.agentbridge/)

`openupm add com.axiom.agentbridge`

### Via Git URL

**Window > Package Manager > + > Add package from git URL:**

```bash
https://github.com/UmutBrkt/Axiom.git?path=/Assets/Axiom
```

Pin a specific version:

```bash
https://github.com/UmutBrkt/Axiom.git?path=/Assets/Axiom#v1.0.0
```

### Post-Install

A dialog prompts you to deploy workspace rules (`.cursorrules`, `project_instructions.md`, `CLAUDE.md`) to your project root. These files tell AI agents how to use Axiom.

Or manually: **Tools > Axiom > Install Workspace Rules to Project Root**

Optional: **Tools > Axiom > Check Optional Packages** to enable additional features.

---

# Quick Introduction

## Gateway JSON Schema

Axiom provides a JSON command gateway (`AgentBridgeGateway.cs`) that accepts structured commands and routes them to the correct tool. This is the primary interface for MCP servers and external agents. The same command works three ways:

1. **Unity MCP** (native): `Axiom_Gateway` with JSON payload
2. **execute_script** (fallback): `AgentBridgeGateway.Execute(jsonString)`
3. **Direct C#** (advanced): `HierarchyLens.GenerateReport(HierarchyMode.Components, rootPath: "Player/")`

```json
{
  "tool": "tool_name (required)",
  "mode": "mode_name_or_letter",
  "context_id": "string (optional)",
  "scope": {
    "root_path": "hierarchy path or scene path",
    "object_names": ["specific GameObjects"],
    "tag_filter": "string",
    "layer_filter": "string",
    "component_filter": "component type name",
    "asset_path": "project-relative asset path",
    "asset_extension": ".ext",
    "max_depth": -1,
    "scene_name": "target scene"
  },
  "output": {
    "format": "markdown|json|flat_text",
    "destination": "file|console|return",
    "file_name": "custom_name"
  }
}
```

Response format:

- `destination: "file"` → returns absolute file path to `AgentReports/`
- `destination: "return"` → returns report content directly
- `destination: "console"` → prints to Unity Console, returns file path
- On error → `{"error": "description"}`

Gateway usage via execute_script:

```csharp
string result = AgentBridgeGateway.Execute(@"{
    ""tool"": ""hierarchy_lens"",
    ""mode"": ""components"",
    ""scope"": { ""root_path"": ""Player/"", ""max_depth"": 3 },
    ""output"": { ""destination"": ""return"" }
}");
```

---

## MCP Tools (Unity AI Assistant)

5 native MCP tools registered via `Assets/Axiom/Editor/AgentBridge/Mcp/`:

| Tool                 | Purpose                                                                                                                       |
| :------------------- | :---------------------------------------------------------------------------------------------------------------------------- |
| `Axiom_Gateway`    | Primary entry point. Forwards JSON to `AgentBridgeGateway.Execute()`.                                                       |
| `Axiom_Status`     | Health probe: editor state, compile status, play mode, report paths. No params.                                               |
| `Axiom_ReadReport` | Reads a report from `AgentReports/`. Params: `relativePath` or `reportName` or `latestPrefix`, optional `maxChars`. |
| `Axiom_Verify`     | Verification:`compilation`, `errors`, `scene_diff_compare_current`.                                                     |
| `Axiom_Rules`      | Returns compact Axiom operating rules. No params.                                                                             |

---

## Token-Lean Implementation Template

This template forces the Report → Execute → Verify loop.

```markdown
# Phase [X]: [Feature Name] — Implementation Plan

## 1. Pre-Flight Diagnostic (The "Map")
- **Action:** Execute Diagnostic Bridge: `<tool>` with mode `<mode>`,
  scoped to `root_path: "<path>"`.
- **Read:** `AgentReports/<tool>_<timestamp>.md`
- **Goal:** Verify [target] exists at expected path with [component] attached.
- **If missing:** Create required GameObjects/components per Expected State below.

### Expected Hierarchy State
| Target Path | Required Components | Key Property Values |
|:---|:---|:---|
| Managers/PlayerSystems/InputHandler | PlayerInput, InputManager | InputActions: "Assets/Input/PlayerActions.inputactions" |

## 2. Execution (The "Work")
- **Scripting:** Edit `[FileName].cs` — [specific method or line description].
- **Wiring:** Write a temporary `[Feature]SetupUtility.cs` [MenuItem] script.
- **Scene Changes:** [Specific GameObjects to create/modify with exact paths].

## 3. Post-Flight Verification (The "Proof")
- **Action:** Execute `scene_diff` mode `D`, comparing snapshot with current.
- **Action:** Execute `log_mirror` mode `A` (Errors Only).
- **Success Criteria:**
  - Scene diff shows expected changes and no unexpected changes.
  - Zero errors in log mirror.
  - [Specific validation]

## 4. Cleanup
- Delete temporary Editor scripts.
- Clear scene diff snapshots if no longer needed.
- Update `AgentReports/StatusUpdate.md` with summary of changes.
```

---

## Diagnostic Tools (22)

| Tool                             | Modes   | Reports                                                |
| :------------------------------- | :------ | :----------------------------------------------------- |
| **HierarchyLens**          | A–F    | Scene structure → full inspector dump                 |
| **LogMirror**              | A–F    | Console logs, compilation status, profiler spikes      |
| **ComponentInspector**     | A–F    | Property values, cross-comparison, missing refs        |
| **ReferenceScanner**       | A–G    | Null refs, missing scripts, material audit             |
| **ProjectCartographer**    | A–G    | File tree, dependencies, orphans, GUIDs, type census   |
| **SceneDiff**              | A–D    | Scene snapshots and diffs (hash → property level)     |
| **SmartSearch**            | 3 modes | Name, component, tag, layer, asset type search         |
| **SettingsReporter**       | A–G    | Full project settings dump                             |
| **ScriptAnalyzer**         | A–E    | Class map, dependencies, assemblies, attributes        |
| **PrefabAuditor**          | A–E    | Variants, overrides, nesting depth, cross-refs         |
| **UIToolkitInspector**     | A–E    | Visual tree, styles, bindings, accessibility           |
| **TestRunner**             | A–D    | Test list, run all/filtered, coverage                  |
| **PhysicsReporter**        | A–F    | Colliders, layer matrix, rigidbodies, joints           |
| **AnimationInspector**     | A–F    | Controllers, state machines, events, avatar, pool      |
| **AudioReporter**          | A–D    | Sources, mixer graph, spatial audio                    |
| **NavMeshInspector**       | A–E    | Agent types, surfaces, obstacles, reachability         |
| **ShaderAuditor**          | A–F    | Materials, keywords, compilation, GPU compat           |
| **RenderAuditor**          | A–G    | Pipeline, GPU Resident Drawer, STP, lights, cameras    |
| **AccessibilityValidator** | A–D    | Screen reader, color contrast, input, text scaling     |
| **CISweep**                | A–C    | Quick health check, full audit, pre-release checklist  |
| **ProjectOnboarder**       | A–C    | Unified briefing — one file = know the entire project |
| **ProjectContext**         | A–C    | Find and summarize project context documents           |

## Action Tools (16)

| Tool                             | Key Operations                                                 |
| :------------------------------- | :------------------------------------------------------------- |
| **SceneActions**           | Create, destroy, reparent, rename, batch ops, scene management |
| **ComponentActions**       | Add, remove, set property (validates RequireComponent)         |
| **WiringUtility**          | Wire/verify SerializedProperty references                      |
| **AssetActions**           | Move, rename, bulk rename, create SO/material, batch import    |
| **SettingsActions**        | Scripting defines, tags, layers, quality, collision matrix     |
| **RenderActions**          | Pipeline config, GPU Resident Drawer, STP, shadows, cameras    |
| **BuildProfileActions**    | Create, diff, modify profiles, trigger builds, analyze reports |
| **PackageManagerActions**  | List, search, add, remove, embed, scoped registries            |
| **PlayModeActions**        | Enter/exit/pause/step, capture state, animator pool reset      |
| **ScreenCaptureActions**   | GameView, SceneView, angled captures, annotations              |
| **PrefabActions**          | Apply/revert overrides, prefab stage management                |
| **InputSimulationActions** | Keyboard, mouse, gamepad, Input Action simulation              |
| **BuildPipelineHooks**     | Pre/post build validation with configurable checks             |
| **MultiplayerActions**     | MPPM 2.0 virtual player control                                |
| **VisionAnalysis**         | Screenshot analysis, capture-and-analyze, diff                 |
| **SentisActions**          | ONNX model loading and inference                               |

---

## Optional Dependencies

All behind `#if` guards — Axiom compiles cleanly without any of them.

| Package                             | Define                        | Enables                                 |
| :---------------------------------- | :---------------------------- | :-------------------------------------- |
| `com.unity.nuget.newtonsoft-json` | `AXIOM_HAS_NEWTONSOFT`      | Full JSON gateway parsing (recommended) |
| `com.unity.inputsystem`           | `AXIOM_HAS_INPUT_SYSTEM`    | InputSimulationActions                  |
| `com.unity.multiplayer.playmode`  | `AXIOM_HAS_MPPM`            | MultiplayerActions                      |
| `com.unity.sentis`                | `AXIOM_HAS_SENTIS`          | SentisActions                           |
| `com.unity.ai.assistant`          | `AXIOM_HAS_UNITY_ASSISTANT` | Native MCP tools (5 tools)              |

---

## Workspace Rules

Axiom deploys three workspace instruction files to your project root so AI agents know how to operate:

| File                        | Consumer                      | Purpose                                       |
| :-------------------------- | :---------------------------- | :-------------------------------------------- |
| `.cursorrules`            | Cursor IDE (auto-loaded)      | 20 core rules + complete tool API reference   |
| `project_instructions.md` | Gemini, Copilot, other agents | JSON command schema + implementation template |
| `CLAUDE.md`               | Claude Code (auto-loaded)     | Complete self-contained reference             |

Install via **Tools > Axiom > Install Workspace Rules to Project Root**.

---

## Requirements

- **Unity 6** ((6000.3), or later.)

## License

[MIT License](Assets/Axiom/LICENSE)
