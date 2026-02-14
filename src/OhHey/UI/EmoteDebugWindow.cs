// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using JetBrains.Annotations;
using OhHey.Services;

namespace OhHey.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class EmoteDebugWindow : Window
{
    private const int DefaultCacheSize = 150;

    private readonly IEmoteLogMessageService _emoteLogMessageService;

    private readonly List<(uint EmoteRowId, string Message)> _cache = new(DefaultCacheSize);
    private string _filter = string.Empty;
    private int _cacheSize = DefaultCacheSize;
    private bool _initialized;

    public EmoteDebugWindow(IEmoteLogMessageService emoteLogMessageService)
        : base("Oh Hey! Emote Debug##ohhey_emote_debug_window")
    {
        _emoteLogMessageService = emoteLogMessageService;

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
        }
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
