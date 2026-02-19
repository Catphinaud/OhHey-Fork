// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using JetBrains.Annotations;
using OhHeyFork.Listeners;
using OhHeyFork.Services;

namespace OhHeyFork.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class EmoteOverlayWindow : Window, IDisposable
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(60);
    private readonly EmoteService _emoteService;
    private readonly ConfigurationService _configService;
    private readonly ITextureProvider _textureProvider;

    public EmoteOverlayWindow(EmoteService emoteService, ConfigurationService configService, ITextureProvider textureProvider)
        : base("Oh Hey! Emote Overlay##ohhey_emote_overlay_window")
    {
        _emoteService = emoteService;
        _configService = configService;
        _textureProvider = textureProvider;
        IsOpen = _configService.Settings.Emote.EnableOverlayWindow;
        ShowCloseButton = false;

        _configService.ConfigurationChanged += OnConfigurationChanged;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 120)
        };
    }

    public override void Draw()
    {
        if (!_configService.Settings.Emote.EnableOverlayWindow) {
            IsOpen = false;
            return;
        }

        var emotes = _emoteService.GetRecentEmotes(WindowDuration);

        ImGui.TextUnformatted("Emotes in last 60s");
        ImGui.SameLine();
        ImGui.TextUnformatted($"({emotes.Count})");
        ImGui.Separator();

        if (emotes.Count == 0) {
            ImGui.TextUnformatted("No emotes yet.");
            return;
        }

        using var table = ImRaii.Table("##ohhey_emote_overlay_table", 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV);
        if (!table) return;

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var emote in emotes) {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (_textureProvider.TryGetFromGameIcon(new GameIconLookup(emote.EmoteIconId), out var iconTexture)) {
                if (ImGui.ImageButton(iconTexture.GetWrapOrEmpty().Handle, new Vector2(24, 24))) {
                    _emoteService.ReplayEmote(emote);
                }
            } else {
                ImGui.TextUnformatted("?");
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(emote.InitiatorName.ToString());
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(_emoteService.GetEmoteDisplayName(emote.EmoteId));
        }
    }

    public void Dispose()
    {
        _configService.ConfigurationChanged -= OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object? sender, OhHeyForkConfiguration configuration)
    {
        IsOpen = configuration.Settings.Emote.EnableOverlayWindow;
    }
}
