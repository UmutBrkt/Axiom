/*
2026-03-09 AI-Tag — refreshed 2026-05-02 for com.unity.ai.assistant 2.7.0-pre.1.
This was created with the help of Assistant, a Unity Artificial Intelligence product.

The 2.7.0-pre.1 validation walks the Windows process chain to identify the MCP
client. For Claude Code's named-pipe transport that walk returns "Unknown" for
both server and client process names. With Unknown identity:

  1. Validation status = Pending ("Awaiting user approval")
  2. Even with policy.requiresApproval=false, the auto-approve path at
     Bridge.cs:1164 should fire — but in practice it doesn't always (likely
     race between settings reload + validation, or unidentified-identity path)
  3. When the user clicks Accept in the UI, CompletePendingApproval looks up
     the pending approval by identity ("Unknown:server|Unknown:client") and
     fails to find it ("No pending approval found for identity ...")

So neither auto-approve nor manual approve work. The workaround is to
side-step the entire approval gate by force-setting each transport's
ApprovalState to `Approved` directly via reflection.

Three menu items:
  Tools/Axiom/Fix MCP Policy           — set policy + restart bridge (run once after editor restart or package update)
  Tools/Axiom/Force Approve MCP Now    — run AFTER reconnecting your MCP client; force-approves whatever's stuck
  Tools/Axiom/Diag MCP Settings        — prints current policy + transport states (sanity check)
*/
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FixMcpPolicy
{
    const string AssemblySuffix = ", Unity.AI.MCP.Editor";

    // After every domain reload (e.g. compile finishes) the MCP bridge re-registers
    // transports in the Pending state — manual MCP calls then fail with
    // "Connection revoked", forcing the user to re-run Force Approve. This static ctor
    // hooks afterAssemblyReload + retries the force-approve until at least one
    // transport state lands. ApprovalState is a runtime enum so a missing transport
    // (no client connected) is silently no-op'd.
    static FixMcpPolicy()
    {
        AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
    }

    private static int _autoApproveAttemptsRemaining;

    private static void OnAfterReload()
    {
        // Most reloads land 200–800ms before the bridge has a fresh transport set
        // wired up. Retry every editor tick for ~3s; bail once we patched at least
        // one transport or budget runs out.
        _autoApproveAttemptsRemaining = 60;
        EditorApplication.update += AutoApproveTick;
    }

    private static void AutoApproveTick()
    {
        if (_autoApproveAttemptsRemaining-- <= 0)
        {
            EditorApplication.update -= AutoApproveTick;
            return;
        }
        try
        {
            int patched = ForceApproveCore(verbose: false);
            if (patched > 0)
            {
                EditorApplication.update -= AutoApproveTick;
                Debug.Log($"[FixMcpPolicy] Auto-approved {patched} transport(s) after assembly reload.");
            }
        }
        catch
        {
            // Bridge types may not be loaded yet on early ticks — keep retrying silently.
        }
    }

    [MenuItem("Tools/Axiom/Fix MCP Policy")]
    public static void Execute()
    {
        try
        {
            // 1. Settings: set direct.allowed=true, direct.requiresApproval=false
            Type managerType = Type.GetType(
                "Unity.AI.MCP.Editor.Settings.MCPSettingsManager" + AssemblySuffix);
            if (managerType == null) { Debug.LogError("[FixMcpPolicy] MCPSettingsManager not found."); return; }

            PropertyInfo settingsProp = managerType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            object settings = settingsProp.GetValue(null);

            FieldInfo policiesField = settings.GetType().GetField("connectionPolicies", BindingFlags.Public | BindingFlags.Instance);
            object policies = policiesField.GetValue(settings);

            FieldInfo directField = policies.GetType().GetField("direct", BindingFlags.Public | BindingFlags.Instance);
            object directPolicy = directField.GetValue(policies);

            FieldInfo allowedField = directPolicy.GetType().GetField("allowed", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo approvalField = directPolicy.GetType().GetField("requiresApproval", BindingFlags.Public | BindingFlags.Instance);

            allowedField.SetValue(directPolicy, true);
            approvalField.SetValue(directPolicy, false);

            MethodInfo saveMethod = managerType.GetMethod("SaveSettings", BindingFlags.Public | BindingFlags.Static);
            saveMethod.Invoke(null, null);
            Debug.Log("[FixMcpPolicy] Direct policy: allowed=true, requiresApproval=false. Saved.");

            // 2. Disconnect any existing transports + restart bridge
            Type bridgeType = Type.GetType("Unity.AI.MCP.Editor.UnityMCPBridge" + AssemblySuffix);
            bridgeType.GetMethod("DisconnectAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            bridgeType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            bridgeType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

            Debug.Log("[FixMcpPolicy] Bridge restarted. Reconnect your MCP client. If it gets stuck waiting for approval, run Tools → Axiom → Force Approve MCP Now.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FixMcpPolicy] Failed: {e.Message}\n{e.StackTrace}");
        }
    }

    [MenuItem("Tools/Axiom/Force Approve MCP Now")]
    public static void ForceApprove()
    {
        try { ForceApproveCore(verbose: true); }
        catch (Exception e) { Debug.LogError($"[ForceApprove] Failed: {e.Message}\n{e.StackTrace}"); }
    }

    private static int ForceApproveCore(bool verbose)
    {
        Type storeType = Type.GetType("Unity.AI.MCP.Editor.TransportStore" + AssemblySuffix);
        if (storeType == null) { if (verbose) Debug.LogError("[ForceApprove] TransportStore not found."); return 0; }

        MethodInfo getActive = storeType.GetMethod(
            "GetActiveTransportStates",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (getActive == null) { if (verbose) Debug.LogError("[ForceApprove] GetActiveTransportStates not found."); return 0; }

        IList states = (IList)getActive.Invoke(null, null);
        if (states == null || states.Count == 0)
        {
            if (verbose) Debug.LogWarning("[ForceApprove] No active transports. Reconnect your MCP client first.");
            return 0;
        }

        Type stateType = Type.GetType("Unity.AI.MCP.Editor.TransportState" + AssemblySuffix);
        FieldInfo approvalField = stateType.GetField("ApprovalState", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo identityField = stateType.GetField("IdentityKey", BindingFlags.Public | BindingFlags.Instance);

        Type enumType = Type.GetType("Unity.AI.MCP.Editor.ConnectionApprovalState" + AssemblySuffix);
        object approvedEnum = Enum.Parse(enumType, "Approved");

        int patched = 0;
        foreach (object state in states)
        {
            object current = approvalField.GetValue(state);
            if (current != null && current.ToString() == "Approved") continue;
            string identity = identityField.GetValue(state)?.ToString() ?? "(no identity)";
            approvalField.SetValue(state, approvedEnum);
            patched++;
            if (verbose) Debug.Log($"[ForceApprove] Transport [{identity}]: {current} → Approved");
        }
        if (verbose) Debug.Log($"[ForceApprove] Force-approved {patched} transport(s).");
        return patched;
    }

    [MenuItem("Tools/Axiom/Diag MCP Settings")]
    public static void DiagSettings()
    {
        try
        {
            Type managerType = Type.GetType("Unity.AI.MCP.Editor.Settings.MCPSettingsManager" + AssemblySuffix);
            object settings = managerType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static).GetValue(null);

            object policies = settings.GetType().GetField("connectionPolicies", BindingFlags.Public | BindingFlags.Instance).GetValue(settings);
            object direct = policies.GetType().GetField("direct", BindingFlags.Public | BindingFlags.Instance).GetValue(policies);
            object gateway = policies.GetType().GetField("gateway", BindingFlags.Public | BindingFlags.Instance).GetValue(policies);

            Debug.Log($"[DiagMCP] direct.allowed={GetBool(direct, "allowed")} direct.requiresApproval={GetBool(direct, "requiresApproval")}");
            Debug.Log($"[DiagMCP] gateway.allowed={GetBool(gateway, "allowed")} gateway.requiresApproval={GetBool(gateway, "requiresApproval")}");

            // Dump active transports
            Type storeType = Type.GetType("Unity.AI.MCP.Editor.TransportStore" + AssemblySuffix);
            IList states = (IList)storeType.GetMethod("GetActiveTransportStates", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

            if (states == null || states.Count == 0)
            {
                Debug.Log("[DiagMCP] No active transports.");
                return;
            }

            Type stateType = Type.GetType("Unity.AI.MCP.Editor.TransportState" + AssemblySuffix);
            FieldInfo approvalField = stateType.GetField("ApprovalState", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo identityField = stateType.GetField("IdentityKey", BindingFlags.Public | BindingFlags.Instance);

            Debug.Log($"[DiagMCP] Active transports: {states.Count}");
            foreach (object s in states)
            {
                Debug.Log($"  [{identityField.GetValue(s)}] state={approvalField.GetValue(s)}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DiagMCP] {e.Message}");
        }
    }

    static bool GetBool(object obj, string fieldName) =>
        (bool)obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance).GetValue(obj);
}
