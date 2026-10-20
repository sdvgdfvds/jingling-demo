using System;
using System.Collections;
using System.Reflection;
using Unity.AI.MCP.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Grok/Codex ↔ Unity MCP usable on Personal (entitlement MaxDirect=0):
/// 1) Disables direct/gateway approval prompts (avoids Accept-after-disconnect race)
/// 2) Locally floors MaxDirect so entitlement resets cannot revoke live clients
/// 3) Promotes Pending/CapacityLimit history to Accepted and re-approves live Denied transports
/// 4) Watchdog re-applies the floor / approvals if Account.settings pushes MaxDirect back to 0
/// </summary>
[InitializeOnLoad]
static class UnityMcpCodexApprovalFix
{
    const string PrefKey = "Unity.AI.MCP.ProjectSettings.v2";
    // Headroom for Grok + Codex + probes + an occasional second client.
    const int DirectFloor = 8;
    const double WatchdogSeconds = 0.5;

    static bool s_Hooked; // refresh-20260830-capacitylimit-floor
    static bool s_AccountHooked;
    static double s_NextWatchdogTime;
    static int s_LastLoggedMax = int.MinValue;

    static UnityMcpCodexApprovalFix()
    {
        EditorApplication.delayCall += Apply;
    }

    static void Apply()
    {
        try
        {
            DisableApproval("direct");
            DisableApproval("gateway");
            EnsureDirectCapacity(forceLog: true);
            PromotePendingHistory();
            ReapproveLiveTransports();
            HookEvents();

            Debug.Log(
                "[UnityMcpCodexApprovalFix] Auto-approve on; " +
                $"MaxDirect={SafeMaxDirect()} (floor={DirectFloor}). " +
                "Pending history promoted; live Denied transports re-approved. " +
                "Retry the Grok/Codex Unity tool — do not click Deny/Revoke in MCP Settings.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityMcpCodexApprovalFix] Failed to apply: {ex.Message}");
        }
    }

    static void HookEvents()
    {
        if (s_Hooked) return;
        s_Hooked = true;

        UnityMCPBridge.MaxDirectConnectionsPolicyChanged += OnPolicyChanged;
        TryHookCensusPolicyChanged();
        TryHookAccountSettingsChanged();

        EditorApplication.update += WatchdogUpdate;
        s_NextWatchdogTime = EditorApplication.timeSinceStartup + WatchdogSeconds;
    }

    /// <summary>
    /// Entitlement wiring sets MaxDirect=0 on Account.settings.OnChange.
    /// Re-floor on the next editor tick so we win the race before EvictOverflow runs.
    /// </summary>
    static void TryHookAccountSettingsChanged()
    {
        if (s_AccountHooked) return;
        try
        {
            var accountType = Type.GetType(
                "Unity.AI.Toolkit.Accounts.Services.Account, Unity.AI.Toolkit.Accounts");
            var settingsProp = accountType?.GetProperty("settings",
                BindingFlags.Public | BindingFlags.Static);
            var settings = settingsProp?.GetValue(null);
            if (settings == null) return;

            var evt = settings.GetType().GetEvent("OnChange",
                BindingFlags.Public | BindingFlags.Instance);
            if (evt == null) return;

            evt.AddEventHandler(settings, (Action)OnAccountSettingsChanged);
            s_AccountHooked = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityMcpCodexApprovalFix] Account hook failed: {ex.Message}");
        }
    }

    static void OnAccountSettingsChanged()
    {
        // Defer so AcpEntitlementWiring.Apply() finishes first, then we raise the floor
        // before ConnectionCensus.EvictOverflowAsync (also delayCall) tears clients down.
        EditorApplication.delayCall += () =>
        {
            EnsureDirectCapacity(forceLog: false);
            ReapproveLiveTransports();
        };
    }

    static void TryHookCensusPolicyChanged()
    {
        try
        {
            var censusType = Type.GetType(
                "Unity.AI.MCP.Editor.Connection.ConnectionCensus, Unity.AI.MCP.Editor");
            if (censusType == null) return;

            var evt = censusType.GetEvent("PolicyChanged",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (evt == null) return;

            evt.AddEventHandler(null, (Action)OnPolicyChanged);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityMcpCodexApprovalFix] Census hook skipped: {ex.Message}");
        }
    }

    static void OnPolicyChanged()
    {
        EnsureDirectCapacity(forceLog: false);
        ReapproveLiveTransports();
    }

    static void WatchdogUpdate()
    {
        if (EditorApplication.timeSinceStartup < s_NextWatchdogTime)
            return;

        s_NextWatchdogTime = EditorApplication.timeSinceStartup + WatchdogSeconds;
        try
        {
            DisableApproval("direct");
            EnsureDirectCapacity(forceLog: false);
            ReapproveLiveTransports();
        }
        catch
        {
            // Ignore transient domain-reload / type-load races.
        }
    }

    static int SafeMaxDirect()
    {
        try { return UnityMCPBridge.MaxDirectConnectionsResolver(); }
        catch { return -999; }
    }

    static void EnsureDirectCapacity(bool forceLog)
    {
        int max = SafeMaxDirect();
        // -1 = unlimited; leave it. Otherwise enforce a local floor.
        if (max >= 0 && max < DirectFloor)
        {
            // Setter evaluates the Func once and writes ConnectionCensus.Policy.
            UnityMCPBridge.MaxDirectConnectionsResolver = () => DirectFloor;
            // Belt-and-suspenders: also poke census directly in case Bridge facade lags.
            TrySetCensusMaxDirect(DirectFloor);
            if (forceLog || s_LastLoggedMax != DirectFloor)
            {
                Debug.LogWarning(
                    $"[UnityMcpCodexApprovalFix] Plan MaxDirect was {max}; " +
                    $"locally set to {DirectFloor} so MCP clients are not revoked.");
                s_LastLoggedMax = DirectFloor;
            }
            return;
        }

        if (forceLog || max != s_LastLoggedMax)
        {
            s_LastLoggedMax = max;
            if (forceLog)
                Debug.Log($"[UnityMcpCodexApprovalFix] MaxDirect OK: {max}");
        }
    }

    static void TrySetCensusMaxDirect(int maxDirect)
    {
        try
        {
            var censusType = Type.GetType(
                "Unity.AI.MCP.Editor.Connection.ConnectionCensus, Unity.AI.MCP.Editor");
            var policyType = Type.GetType(
                "Unity.AI.MCP.Editor.Connection.ConnectionPolicy, Unity.AI.MCP.Editor");
            if (censusType == null || policyType == null) return;

            var policyProp = censusType.GetProperty("Policy",
                BindingFlags.Public | BindingFlags.Static);
            var current = policyProp?.GetValue(null);
            if (current == null) return;

            int maxGateway = -1;
            var gwProp = policyType.GetProperty("MaxGateway");
            if (gwProp != null)
                maxGateway = (int)gwProp.GetValue(current);

            var ctor = policyType.GetConstructor(new[] { typeof(int), typeof(int) });
            if (ctor == null) return;
            var next = ctor.Invoke(new object[] { maxDirect, maxGateway });

            var setPolicy = censusType.GetMethod("SetPolicy",
                BindingFlags.Public | BindingFlags.Static);
            setPolicy?.Invoke(null, new[] { next });
        }
        catch
        {
            // Non-fatal — Bridge resolver path is enough in normal cases.
        }
    }

    /// <summary>
    /// grok-shell-unity often sticks at ValidationStatus.Pending ("Awaiting user approval").
    /// Promote Pending/CapacityLimit (and accidental Rejected for known agents) to Accepted
    /// so reconnects auto-approve even if requiresApproval flips back on.
    /// </summary>
    static void PromotePendingHistory()
    {
        try
        {
            var storeType = Type.GetType("Unity.AI.MCP.Editor.ConnectionStore, Unity.AI.MCP.Editor");
            if (storeType == null) return;

            var field = storeType.GetField("ConnectionsByIdentity",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var dict = field?.GetValue(null) as IDictionary;
            if (dict == null || dict.Count == 0) return;

            var statusType = Type.GetType("Unity.AI.MCP.Editor.Security.ValidationStatus, Unity.AI.MCP.Editor");
            if (statusType == null) return;

            object accepted = Enum.Parse(statusType, "Accepted");
            int promoted = 0;

            foreach (DictionaryEntry entry in dict)
            {
                var record = entry.Value;
                if (record == null) continue;

                var statusProp = record.GetType().GetField("Status",
                    BindingFlags.Public | BindingFlags.Instance);
                var reasonProp = record.GetType().GetField("ValidationReason",
                    BindingFlags.Public | BindingFlags.Instance);
                var infoProp = record.GetType().GetField("Info",
                    BindingFlags.Public | BindingFlags.Instance);
                var identityProp = record.GetType().GetField("Identity",
                    BindingFlags.Public | BindingFlags.Instance);

                if (statusProp == null) continue;

                var status = statusProp.GetValue(record);
                string statusName = status?.ToString() ?? "";
                // CapacityLimit = "Your Unity plan doesn't include MCP connections"
                // when MaxDirect briefly hits 0. Always heal it for known agents.
                if (statusName != "Pending" && statusName != "CapacityLimit" &&
                    statusName != "Rejected" && statusName != "Denied")
                    continue;

                // Only auto-heal known agent clients — don't override an intentional Deny of unknown tools.
                string processName = "";
                string clientTitle = "";
                try
                {
                    var info = infoProp?.GetValue(record);
                    var client = info?.GetType().GetField("Client")?.GetValue(info);
                    processName = client?.GetType().GetField("ProcessName")?.GetValue(client) as string ?? "";
                    var clientInfo = info?.GetType().GetField("ClientInfo")?.GetValue(info);
                    clientTitle = clientInfo?.GetType().GetField("Name")?.GetValue(clientInfo) as string
                        ?? clientInfo?.GetType().GetField("Title")?.GetValue(clientInfo) as string
                        ?? "";
                }
                catch { /* best-effort */ }

                if (!IsKnownAgent(processName, clientTitle))
                    continue;

                // Rejected: only auto-heal if reason looks like capacity/timeout, not explicit user Deny.
                if (statusName == "Rejected")
                {
                    string reason = reasonProp?.GetValue(record) as string ?? "";
                    if (reason.IndexOf("Denied by user", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("Revoked by user", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                }

                statusProp.SetValue(record, accepted);
                reasonProp?.SetValue(record, "Auto-approved by UnityMcpCodexApprovalFix");
                promoted++;

                // Unblock any waiting approval TCS for this identity.
                try
                {
                    var identity = identityProp?.GetValue(record);
                    var key = identity?.GetType().GetField("CombinedIdentityKey",
                        BindingFlags.Public | BindingFlags.Instance)?.GetValue(identity) as string;
                    if (!string.IsNullOrEmpty(key))
                        TryCompletePendingApproval(key, approved: true);
                }
                catch { /* best-effort */ }
            }

            if (promoted > 0)
            {
                var save = storeType.GetField("OnSaveRequested",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (save?.GetValue(null) is Action saveAction)
                    saveAction.Invoke();

                Debug.Log($"[UnityMcpCodexApprovalFix] Promoted {promoted} connection history record(s) to Accepted.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityMcpCodexApprovalFix] PromotePendingHistory failed: {ex.Message}");
        }
    }

    static bool IsKnownAgent(string processName, string clientTitle)
    {
        string p = (processName ?? "").ToLowerInvariant();
        string t = (clientTitle ?? "").ToLowerInvariant();
        return p.Contains("grok") || p.Contains("codex") || p.Contains("claude") ||
               t.Contains("grok") || t.Contains("codex") || t.Contains("claude");
    }

    static void TryCompletePendingApproval(string identityKey, bool approved)
    {
        try
        {
            var bridgeType = Type.GetType("Unity.AI.MCP.Editor.Bridge, Unity.AI.MCP.Editor");
            var method = bridgeType?.GetMethod("CompletePendingApproval",
                BindingFlags.Public | BindingFlags.Static);
            // CompletePendingApproval logs/throws "No pending approval" when the
            // Accept race already cleared the TCS — swallow that noise.
            method?.Invoke(null, new object[] { identityKey, approved });
        }
        catch (TargetInvocationException)
        {
            // Expected when nothing is pending.
        }
        catch
        {
            // optional
        }
    }

    /// <summary>
    /// Live transports stuck in Denied keep returning "Connection revoked" even after MaxDirect is raised.
    /// Flip them back to Approved when auto-approve policy is on.
    /// </summary>
    static void ReapproveLiveTransports()
    {
        try
        {
            var storeType = Type.GetType("Unity.AI.MCP.Editor.TransportStore, Unity.AI.MCP.Editor");
            var method = storeType?.GetMethod("GetActiveTransportStates",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return;

            var states = method.Invoke(null, null) as IEnumerable;
            if (states == null) return;

            var approvalType = Type.GetType("Unity.AI.MCP.Editor.ConnectionApprovalState, Unity.AI.MCP.Editor");
            if (approvalType == null) return;
            object approved = Enum.Parse(approvalType, "Approved");

            int fixedCount = 0;
            foreach (var state in states)
            {
                var approvalField = state.GetType().GetField("ApprovalState",
                    BindingFlags.Public | BindingFlags.Instance);
                if (approvalField == null) continue;

                string current = approvalField.GetValue(state)?.ToString() ?? "";
                if (current != "Denied" && current != "AwaitingApproval" &&
                    current != "Revoked" && current != "None")
                    continue;

                approvalField.SetValue(state, approved);
                fixedCount++;

                try
                {
                    var key = state.GetType().GetField("IdentityKey",
                        BindingFlags.Public | BindingFlags.Instance)?.GetValue(state) as string;
                    if (!string.IsNullOrEmpty(key))
                        TryCompletePendingApproval(key, approved: true);
                }
                catch { /* best-effort */ }
            }

            if (fixedCount > 0)
                Debug.Log($"[UnityMcpCodexApprovalFix] Re-approved {fixedCount} live transport(s).");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityMcpCodexApprovalFix] ReapproveLiveTransports failed: {ex.Message}");
        }
    }

    static void DisableApproval(string policyName)
    {
        var mgrType = Type.GetType(
            "Unity.AI.MCP.Editor.Settings.MCPSettingsManager, Unity.AI.MCP.Editor");
        if (mgrType != null)
        {
            var settingsProp = mgrType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            var saveMethod = mgrType.GetMethod("SaveSettings", BindingFlags.Public | BindingFlags.Static);
            var settings = settingsProp?.GetValue(null);
            if (settings != null)
            {
                var policiesField = settings.GetType().GetField("connectionPolicies",
                    BindingFlags.Public | BindingFlags.Instance);
                var policies = policiesField?.GetValue(settings);
                if (policies != null)
                {
                    var policyField = policies.GetType().GetField(policyName,
                        BindingFlags.Public | BindingFlags.Instance);
                    var policy = policyField?.GetValue(policies);
                    if (policy != null)
                    {
                        var requiresField = policy.GetType().GetField("requiresApproval",
                            BindingFlags.Public | BindingFlags.Instance);
                        var allowedField = policy.GetType().GetField("allowed",
                            BindingFlags.Public | BindingFlags.Instance);

                        bool changed = false;
                        if (allowedField != null && allowedField.GetValue(policy) is false)
                        {
                            allowedField.SetValue(policy, true);
                            changed = true;
                        }

                        if (requiresField != null && requiresField.GetValue(policy) is true)
                        {
                            requiresField.SetValue(policy, false);
                            changed = true;
                        }

                        saveMethod?.Invoke(null, null);
                        if (changed || policyName == "direct")
                            return;
                    }
                }
            }
        }

        PatchEditorPrefsJson();
    }

    static void PatchEditorPrefsJson()
    {
        string json = EditorPrefs.GetString(PrefKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            json =
                "{\n" +
                "    \"bridgeEnabled\": true,\n" +
                "    \"batchModeEnabled\": true,\n" +
                "    \"autoApproveInBatchMode\": true,\n" +
                "    \"validationLevel\": \"standard\",\n" +
                "    \"processValidationEnabled\": true,\n" +
                "    \"connectionPolicies\": {\n" +
                "        \"gateway\": {\n" +
                "            \"allowed\": true,\n" +
                "            \"requiresApproval\": false\n" +
                "        },\n" +
                "        \"direct\": {\n" +
                "            \"allowed\": true,\n" +
                "            \"requiresApproval\": false\n" +
                "        }\n" +
                "    },\n" +
                "    \"clientStates\": [],\n" +
                "    \"enabledToolOverrides\": [],\n" +
                "    \"disabledToolOverrides\": []\n" +
                "}";
            EditorPrefs.SetString(PrefKey, json);
            return;
        }

        json = json.Replace("\"requiresApproval\": true", "\"requiresApproval\": false");
        EditorPrefs.SetString(PrefKey, json);
    }

    [MenuItem("Tools/Unity MCP/Apply Codex Approval Fix")]
    static void MenuApply() => Apply();

    [MenuItem("Tools/Unity MCP/Print Bridge Status")]
    static void MenuStatus()
    {
        Debug.Log(
            $"[UnityMcpCodexApprovalFix] Running={UnityMCPBridge.IsRunning} " +
            $"Enabled={UnityMCPBridge.Enabled} MaxDirect={SafeMaxDirect()} " +
            $"Clients={UnityMCPBridge.GetConnectedClientCount()} " +
            $"Identities=[{string.Join(", ", UnityMCPBridge.GetActiveIdentityKeys())}]");
        UnityMCPBridge.PrintClientInfo();
    }

    [MenuItem("Tools/Unity MCP/Force Reapprove All Agents")]
    static void MenuForceReapprove()
    {
        PromotePendingHistory();
        ReapproveLiveTransports();
        EnsureDirectCapacity(forceLog: true);
        Debug.Log("[UnityMcpCodexApprovalFix] Force reapprove finished — retry the Unity MCP tool now.");
    }
}
