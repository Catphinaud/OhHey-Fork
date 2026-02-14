// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using JetBrains.Annotations;
using OhHey.Listeners;
using OhHey.Services;

namespace OhHey.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class EmoteDebugWindow : Window
{
    private const int DefaultCacheSize = 150;

    private readonly IEmoteLogMessageService _emoteLogMessageService;
    private readonly EmoteListener _emoteListener;
    private readonly EmoteService _emoteService;

    private readonly List<(uint EmoteRowId, string Message)> _cache = new(DefaultCacheSize);
    private string _filter = string.Empty;
    private int _cacheSize = DefaultCacheSize;
    private bool _initialized;

    public EmoteDebugWindow(IEmoteLogMessageService emoteLogMessageService, EmoteListener emoteListener, EmoteService emoteService)
        : base("Oh Hey! Emote Debug##ohhey_emote_debug_window")
    {
        _emoteLogMessageService = emoteLogMessageService;
        _emoteListener = emoteListener;
        _emoteService = emoteService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320)
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
        EnsureCached();
    }

    public override void Draw()
    {
        if (!_initialized)
        {
            EnsureCached();
        }

        using (ImRaii.PushId("ohhey_emote_debug"))
        {
            ImGui.TextUnformatted("Evaluated targeted emote log messages (English)");

            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("Cache size", ref _cacheSize))
            {
                if (_cacheSize < 1) _cacheSize = 1;
                if (_cacheSize > 5000) _cacheSize = 5000;
            }

            ImGui.SameLine();
            if (ImGui.Button("Rebuild cache"))
            {
                _emoteLogMessageService.InvalidateCache();
                EnsureCached(force: true);
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear filter"))
            {
                _filter = string.Empty;
            }

            ImGui.Separator();

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##ohhey_emote_debug_filter", "Filter (row id or substring)...", ref _filter, 200);

            ImGui.Separator();

            var all = _emoteLogMessageService.GetTargetedLogMessagesEnglish();
            ImGui.TextUnformatted($"Service cache entries: {all.Count}");
            ImGui.TextUnformatted($"Window cached entries: {_cache.Count} (showing up to {_cacheSize})");

            using var child = ImRaii.Child("##ohhey_emote_debug_list", new Vector2(0, 0), true);
            if (!child) return;

            var hasFilter = !string.IsNullOrWhiteSpace(_filter);
            var filter = _filter.Trim();
            var hasRowIdFilter = uint.TryParse(filter, out var rowIdFilter);

            foreach (var (emoteRowId, message) in _cache)
            {
                if (hasFilter)
                {
                    if (hasRowIdFilter)
                    {
                        if (emoteRowId != rowIdFilter)
                            continue;
                    }
                    else
                    {
                        if (message.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            emoteRowId.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                }

                ImGui.TextUnformatted($"{emoteRowId}: {message}");
            }

            ImGui.Separator();
            DrawReplayLinkDebugSection(_emoteListener.GetReplayLinkDebugSnapshot());
            ImGui.Separator();
            DrawReplayTargetDebugSection(_emoteService.GetReplayTargetDebugSnapshot());
        }
    }

    private static void DrawReplayLinkDebugSection(ReplayLinkDebugSnapshot snapshot)
    {
        ImGui.TextUnformatted("Replay Link Handlers");
        ImGui.TextUnformatted(
            $"Active: {snapshot.ActiveCount}/{snapshot.MaxEntries} | Reusable IDs: {snapshot.ReusableIdCount} | Next ID: {snapshot.NextCommandIndex} | TTL: {snapshot.DefaultTtl.TotalMinutes:0}m");
        ImGui.TextUnformatted(
            $"Created: {snapshot.CreatedCount} | Removed: {snapshot.RemovedCount} | Expired: {snapshot.RemovedExpiredCount} | Evicted: {snapshot.RemovedEvictedCount} | Clicked: {snapshot.ClickedCount}");

        if (!ImGui.CollapsingHeader("Active handlers", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (snapshot.Entries.Count == 0)
        {
            ImGui.TextUnformatted("No active replay link handlers.");
            return;
        }

        if (ImGui.BeginTable("##ohhey_replay_handler_table", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Cmd Id");
            ImGui.TableSetupColumn("Emote");
            ImGui.TableSetupColumn("Silent");
            ImGui.TableSetupColumn("Initiator Id");
            ImGui.TableSetupColumn("Created (UTC)");
            ImGui.TableSetupColumn("TTL");
            ImGui.TableSetupColumn("Remaining");
            ImGui.TableHeadersRow();

            foreach (var entry in snapshot.Entries)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.CommandIndex.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.EmoteId.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.SilentReplay ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.InitiatorId.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.CreatedUtc.ToString("HH:mm:ss"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{entry.Ttl.TotalSeconds:0}s");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{Math.Max(0, entry.Remaining.TotalSeconds):0}s");
            }

            ImGui.EndTable();
        }
    }

    private static void DrawReplayTargetDebugSection(ReplayTargetDebugSnapshot snapshot)
    {
        ImGui.TextUnformatted("Replay Targeting");
        ImGui.TextUnformatted($"Current target ID: {(snapshot.CurrentTargetId?.ToString() ?? "none")}");
        ImGui.TextUnformatted($"Last replay at (UTC): {(snapshot.LastReplayAtUtc?.ToString("HH:mm:ss") ?? "none")}");
        ImGui.TextUnformatted($"Last replay emote ID: {(snapshot.LastReplayEmoteId?.ToString() ?? "none")}");
        ImGui.TextUnformatted($"Requested target ID: {(snapshot.LastReplayRequestedTargetId?.ToString() ?? "none")}");
        ImGui.TextUnformatted($"Resolved replay target ID: {(snapshot.LastReplayResolvedTargetId?.ToString() ?? "none")}");
        ImGui.TextUnformatted($"Previous target ID: {(snapshot.LastReplayPreviousTargetId?.ToString() ?? "none")}");
        ImGui.TextUnformatted($"Changed target for replay: {snapshot.LastReplayChangedTargetForReplay}");
        ImGui.TextUnformatted($"Restored previous target: {snapshot.LastReplayRestoredPreviousTarget}");
        ImGui.TextUnformatted($"Cleared target after replay: {snapshot.LastReplayClearedTarget}");
        ImGui.TextUnformatted($"Replay executed: {snapshot.LastReplayExecuted}");
        ImGui.TextUnformatted($"Silent replay: {snapshot.LastReplaySilent}");
        ImGui.TextUnformatted($"Status: {snapshot.LastReplayStatus ?? "none"}");
    }

    private void EnsureCached(bool force = false)
    {
        if (_initialized && !force)
            return;

        _cache.Clear();

        var all = _emoteLogMessageService.GetTargetedLogMessagesEnglish();
        foreach (var kvp in all.OrderBy(k => k.Key).Take(_cacheSize))
        {
            _cache.Add((kvp.Key, kvp.Value));
        }

        _initialized = true;
    }
}
