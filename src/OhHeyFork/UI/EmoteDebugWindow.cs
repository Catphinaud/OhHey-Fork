// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using JetBrains.Annotations;
using OhHeyFork.Listeners;
using OhHeyFork.Services;

namespace OhHeyFork.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class EmoteDebugWindow : Window
{
    private readonly IEmoteLogMessageService _emoteLogMessageService;
    private readonly EmoteListener _emoteListener;
    private readonly EmoteService _emoteService;
    private readonly IDataManagerCacheService _dataManagerCacheService;
    private readonly ITextureProvider _textureProvider;

    private string _filter = string.Empty;
    private string _emoteBrowserFilter = string.Empty;
    private int _previewEmoteRowId = 105;
    private string _previewRawPayload = string.Empty;
    private string _previewTargetName = "{Name}";
    private string _previewYouToName = string.Empty;
    private string _previewNameToYou = string.Empty;
    private string _previewStatus = "Load an emote row id or paste a raw payload.";
    private int _namePreviewCacheCount;
    private IReadOnlyList<CachedEmoteInfo> _allEmotes = [];
    private readonly Dictionary<ushort, bool> _canUseCache = new();
    private DateTime _nextCanUseRefreshUtc = DateTime.MinValue;

    public EmoteDebugWindow(
        IEmoteLogMessageService emoteLogMessageService,
        EmoteListener emoteListener,
        EmoteService emoteService,
        IDataManagerCacheService dataManagerCacheService,
        ITextureProvider textureProvider)
        : base("Oh Hey! Emote Debug##ohhey_emote_debug_window")
    {
        _emoteLogMessageService = emoteLogMessageService;
        _emoteListener = emoteListener;
        _emoteService = emoteService;
        _dataManagerCacheService = dataManagerCacheService;
        _textureProvider = textureProvider;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320)
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _namePreviewCacheCount = _emoteLogMessageService.GetTargetedPayloadPreviewNameCache().Count;
        _allEmotes = _dataManagerCacheService.GetAllEmotes();
        _nextCanUseRefreshUtc = DateTime.MinValue;
        TryLoadRawPayloadForCurrentRow();
    }

    public override void Draw()
    {
        using (ImRaii.PushId("ohhey_emote_debug"))
        {
            if (ImGui.BeginTabBar("##ohhey_emote_debug_tabs"))
            {
                if (ImGui.BeginTabItem("Payload Preview"))
                {
                    DrawTargetedPayloadPreviewSection();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Payload Cache"))
                {
                    DrawMessageCacheSection();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Replay Links"))
                {
                    DrawReplayLinkDebugSection(_emoteListener.GetReplayLinkDebugSnapshot());
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Replay Targeting"))
                {
                    DrawReplayTargetDebugSection(_emoteService.GetReplayTargetDebugSnapshot());
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Emote Browser"))
                {
                    DrawEmoteBrowserSection();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
    }

    private void DrawEmoteBrowserSection()
    {
        RefreshCanUseCacheIfDue();

        ImGui.TextUnformatted("All emotes with live CanUse status (refreshes every 1s).");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ohhey_emote_browser_filter", "Filter (id or name)...", ref _emoteBrowserFilter, 128);

        var hasFilter = !string.IsNullOrWhiteSpace(_emoteBrowserFilter);
        var filter = _emoteBrowserFilter.Trim();
        var hasIdFilter = ushort.TryParse(filter, out var idFilter);

        using var child = ImRaii.Child("##ohhey_emote_browser_list", new Vector2(0, 0), true);
        if (!child)
        {
            return;
        }

        if (!ImGui.BeginTable("##ohhey_emote_browser_table", 5,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 36f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Can Use", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableHeadersRow();

        foreach (var emote in _allEmotes)
        {
            if (hasFilter)
            {
                if (hasIdFilter)
                {
                    if (emote.EmoteId != idFilter)
                    {
                        continue;
                    }
                }
                else
                {
                    if (emote.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        emote.EmoteId.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
            }

            var canUse = _canUseCache.TryGetValue(emote.EmoteId, out var value) && value;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (_textureProvider.TryGetFromGameIcon(new GameIconLookup(emote.IconId), out var iconTexture))
            {
                ImGui.Image(iconTexture.GetWrapOrEmpty().Handle, new Vector2(24, 24));
            }
            else
            {
                ImGui.TextUnformatted("?");
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(emote.EmoteId.ToString());

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(emote.DisplayName);

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(canUse ? "Yes" : "No");

            ImGui.TableSetColumnIndex(4);
            using (ImRaii.Disabled(!canUse))
            {
                if (ImGui.SmallButton($"Use##ohhey_use_emote_{emote.EmoteId}"))
                {
                    _emoteService.TryUseEmoteIfAvailable(emote.EmoteId);
                }
            }
        }

        ImGui.EndTable();
    }

    private void RefreshCanUseCacheIfDue()
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _nextCanUseRefreshUtc)
        {
            return;
        }

        _nextCanUseRefreshUtc = nowUtc.AddSeconds(1);
        foreach (var emote in _allEmotes)
        {
            _canUseCache[emote.EmoteId] = _emoteService.CanUseEmote(emote.EmoteId);
        }
    }

    private void DrawTargetedPayloadPreviewSection()
    {
        ImGui.TextUnformatted("Preview targeted payload output using emote row data or pasted macro string.");
        ImGui.TextUnformatted($"Prebuilt {{Name}} preview cache entries: {_namePreviewCacheCount}");
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Emote row id##ohhey_preview_row", ref _previewEmoteRowId))
        {
            if (_previewEmoteRowId < 1) _previewEmoteRowId = 1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Load from Emote sheet"))
        {
            TryLoadRawPayloadForCurrentRow();
        }

        ImGui.SameLine();
        if (ImGui.Button("Render"))
        {
            RenderPreview();
        }

        ImGui.SetNextItemWidth(280);
        ImGui.InputText("Target placeholder##ohhey_preview_name", ref _previewTargetName, 64);

        ImGui.TextUnformatted("Raw payload (formatted)");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline(
            "Raw payload##ohhey_preview_payload",
            ref _previewRawPayload,
            4096,
            new Vector2(-1, 120));

        ImGui.SameLine();
        if (ImGui.Button("Clear preview"))
        {
            _previewRawPayload = string.Empty;
            _previewYouToName = string.Empty;
            _previewNameToYou = string.Empty;
            _previewStatus = "Cleared.";
        }

        ImGui.TextUnformatted(_previewStatus);
        if (!string.IsNullOrWhiteSpace(_previewYouToName))
        {
            ImGui.Separator();
            ImGui.TextUnformatted("You -> Name");
            ImGui.TextWrapped(_previewYouToName);
        }

        if (!string.IsNullOrWhiteSpace(_previewNameToYou))
        {
            ImGui.TextUnformatted("Name -> You");
            ImGui.TextWrapped(_previewNameToYou);
        }
    }

    private void DrawMessageCacheSection()
    {
        ImGui.TextUnformatted("Prebuilt targeted payload preview cache ({Name})");

        if (ImGui.Button("Rebuild cache"))
        {
            _emoteLogMessageService.InvalidateCache();
            _namePreviewCacheCount = _emoteLogMessageService.GetTargetedPayloadPreviewNameCache().Count;
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear filter"))
        {
            _filter = string.Empty;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ohhey_emote_debug_filter", "Filter (row id or substring)...", ref _filter, 200);

        ImGui.Separator();

        var all = _emoteLogMessageService.GetTargetedPayloadPreviewNameCache();
        ImGui.TextUnformatted($"Cache entries: {all.Count}");

        using var child = ImRaii.Child("##ohhey_emote_debug_list", new Vector2(0, 0), true);
        if (!child)
            return;

        var hasFilter = !string.IsNullOrWhiteSpace(_filter);
        var filter = _filter.Trim();
        var hasRowIdFilter = uint.TryParse(filter, out var rowIdFilter);

        if (ImGui.BeginTable("##ohhey_payload_cache_table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Emote Row", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableSetupColumn("You -> Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Name -> You", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var kvp in all.OrderBy(k => k.Key))
            {
                var emoteRowId = kvp.Key;
                var preview = kvp.Value;

                if (hasFilter)
                {
                    if (hasRowIdFilter)
                    {
                        if (emoteRowId != rowIdFilter)
                            continue;
                    }
                    else
                    {
                        if (preview.YouToName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            preview.NameToYou.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            emoteRowId.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(emoteRowId.ToString());
                ImGui.TableNextColumn();
                ImGui.TextWrapped(preview.YouToName);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(preview.NameToYou);
            }

            ImGui.EndTable();
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

    private void RenderPreview()
    {
        if (_emoteLogMessageService.TryRenderTargetedPayloadPreview(_previewRawPayload, _previewTargetName, out var preview))
        {
            _previewYouToName = preview.YouToName;
            _previewNameToYou = preview.NameToYou;
            _previewStatus = "Rendered.";
            return;
        }

        _previewYouToName = string.Empty;
        _previewNameToYou = string.Empty;
        _previewStatus = "Could not render payload.";
    }

    private void TryLoadRawPayloadForCurrentRow()
    {
        if (_emoteLogMessageService.TryGetTargetedPayloadPreviewNameCache((uint)_previewEmoteRowId, out var cachedPreview))
        {
            _previewTargetName = "{Name}";
            _previewRawPayload = FormatRawPayload(cachedPreview.RawPayload);
            _previewYouToName = cachedPreview.YouToName;
            _previewNameToYou = cachedPreview.NameToYou;
            _previewStatus = $"Loaded emote row {_previewEmoteRowId} from prebuilt {{Name}} cache.";
            return;
        }

        if (_emoteLogMessageService.TryGetTargetedLogMessageRawPayload((uint)_previewEmoteRowId, out var rawPayload))
        {
            _previewRawPayload = FormatRawPayload(rawPayload);
            _previewStatus = $"Loaded emote row {_previewEmoteRowId}.";
            RenderPreview();
            return;
        }

        _previewStatus = $"No targeted payload found for emote row {_previewEmoteRowId}.";
    }

    private static string FormatRawPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return string.Empty;

        var payload = rawPayload.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        var sb = new StringBuilder(payload.Length + 32);
        var angleDepth = 0;

        for (var i = 0; i < payload.Length; i++)
        {
            var c = payload[i];
            if (c == '<')
            {
                if (angleDepth == 0 && sb.Length > 0 && sb[^1] != '\n')
                    sb.Append('\n');
                angleDepth++;
                sb.Append(c);
                continue;
            }

            sb.Append(c);
            if (c == '>')
            {
                angleDepth = Math.Max(0, angleDepth - 1);
                if (angleDepth == 0)
                    sb.Append('\n');
            }
        }

        var text = Regex.Replace(sb.ToString(), @"\n{2,}", "\n", RegexOptions.CultureInvariant);
        return text.Trim();
    }
}
