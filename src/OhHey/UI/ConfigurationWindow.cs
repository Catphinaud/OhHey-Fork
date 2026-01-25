// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using JetBrains.Annotations;
using OhHey;
using OhHey.Services;

namespace OhHey.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public class ConfigurationWindow : Window
{
    private readonly ConfigurationService _configService;
    private readonly EmoteService _emoteService;
    private OhHeyConfiguration Config => _configService.Configuration;

    private static readonly (string Label, XivChatType Type)[] ChatTypeOptions =
    [
        ("Echo", XivChatType.Echo),
        ("Notice", XivChatType.Notice),
        ("Urgent", XivChatType.Urgent),
        ("System Message", XivChatType.SystemMessage),
        ("Debug", XivChatType.Debug),
        ("Say", XivChatType.Say),
        ("Party", XivChatType.Party),
        ("Alliance", XivChatType.Alliance),
        ("Shout", XivChatType.Shout),
        ("Yell", XivChatType.Yell),
        ("Tell (Incoming)", XivChatType.TellIncoming),
        ("Free Company", XivChatType.FreeCompany),
        ("Novice Network", XivChatType.NoviceNetwork),
        ("Linkshell 1", XivChatType.Ls1),
        ("Linkshell 2", XivChatType.Ls2),
        ("Linkshell 3", XivChatType.Ls3),
        ("Linkshell 4", XivChatType.Ls4),
        ("Linkshell 5", XivChatType.Ls5),
        ("Linkshell 6", XivChatType.Ls6),
        ("Linkshell 7", XivChatType.Ls7),
        ("Linkshell 8", XivChatType.Ls8),
        ("Crossworld Linkshell 1", XivChatType.CrossLinkShell1),
        ("Crossworld Linkshell 2", XivChatType.CrossLinkShell2),
        ("Crossworld Linkshell 3", XivChatType.CrossLinkShell3),
        ("Crossworld Linkshell 4", XivChatType.CrossLinkShell4),
        ("Crossworld Linkshell 5", XivChatType.CrossLinkShell5),
        ("Crossworld Linkshell 6", XivChatType.CrossLinkShell6),
        ("Crossworld Linkshell 7", XivChatType.CrossLinkShell7),
        ("Crossworld Linkshell 8", XivChatType.CrossLinkShell8),
        ("PvP Team", XivChatType.PvPTeam)
    ];

    private static readonly string ChatTypeOptionsText =
        string.Join('\0', ChatTypeOptions.Select(option => option.Label)) + '\0';

    public ConfigurationWindow(ConfigurationService configService, EmoteService emoteService)
        : base("Oh Hey! Configuration##ohhey_config_window")
    {
        _configService = configService;
        _emoteService = emoteService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 350)
        };
    }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##ohhey_config_tab_bar");
        if (!tabBar) return;
        GeneralConfig();
        TargetConfig();
        EmoteConfig();
        DebugConfig();
    }


    private void GeneralConfig()
    {
        using var tabItem = ImRaii.TabItem("General##ohhey_config_tab_general");
        if (!tabItem) return;

        var enableCloseHotkey = Config.EnableMainWindowCloseHotkey;
        if (ImGui.Checkbox("Enable closing the main window with ESC", ref enableCloseHotkey))
        {
            Config.EnableMainWindowCloseHotkey = enableCloseHotkey;
            _configService.Save();
        }
    }

    private void TargetConfig()
    {
        using var tabItem = ImRaii.TabItem("Targets##ohhey_config_tab_target");
        if (!tabItem) return;

        ImGui.TextUnformatted("Notification Settings:");

        var enableTargetNotifications = Config.EnableTargetNotifications;
        if (ImGui.Checkbox("Enable target notifications", ref enableTargetNotifications))
        {
            Config.EnableTargetNotifications = enableTargetNotifications;
            _configService.Save();
        }

        var targetChatIndex = GetChatTypeIndex(Config.TargetNotificationChatType);
        if (ImGui.Combo("Notification channel", ref targetChatIndex, ChatTypeOptionsText))
        {
            Config.TargetNotificationChatType = ChatTypeOptions[targetChatIndex].Type;
            _configService.Save();
        }

        TargetSoundConfig();

        ImGui.Separator();
        ImGui.TextUnformatted("Combat Settings:");
        var enableInDuty = Config.EnableTargetNotificationInCombat;
        if (ImGui.Checkbox("Enable target notifications while in combat", ref enableInDuty))
        {
            Config.EnableTargetNotificationInCombat = enableInDuty;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Self-target settings:");

        var allowSelfTarget = Config.ShowSelfTarget;
        if (ImGui.Checkbox("Show self-targeting in target list", ref allowSelfTarget))
        {
            Config.ShowSelfTarget = allowSelfTarget;
            _configService.Save();
        }

        var notifyOnSelfTarget = Config.NotifyOnSelfTarget;
        if (ImGui.Checkbox("Notify on self-target", ref notifyOnSelfTarget))
        {
            Config.NotifyOnSelfTarget = notifyOnSelfTarget;
            _configService.Save();
        }
    }

    private void TargetSoundConfig()
    {
        var soundEnabled = Config.EnableTargetSoundNotification;
        if(ImGui.Checkbox("Enable sound notification on target", ref soundEnabled))
        {
            Config.EnableTargetSoundNotification = soundEnabled;
            _configService.Save();
        }

        ImGui.TextUnformatted("Sound to play (SE.1 - SE.16)");
        var selectedIndex = _configService.Configuration.TargetSoundNotificationId;
        using (var combo = ImRaii.Combo("##ohhey_config_combo_target_sound", $"SE.{selectedIndex}"))
        {
            if (combo)
            {
                for (var i = 1; i <= 16; i++)
                {
                    var isSelected = selectedIndex == i;
                    if (ImGui.Selectable($"SE.{i}", isSelected))
                    {
                        selectedIndex = (uint)i;
                        Config.TargetSoundNotificationId = selectedIndex;
                        _configService.Save();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
            }
        }
        ImGui.SameLine();
        if (ImGui.ArrowButton("##ohhey_config_button_target_sound_play", ImGuiDir.Right))
        {
            UIGlobals.PlayChatSoundEffect(_configService.Configuration.TargetSoundNotificationId);
        }

        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            ImGui.TextUnformatted("Play the selected sound effect");
        }
    }

    private void EmoteConfig()
    {
        using var tabItem = ImRaii.TabItem("Emotes##ohhey_config_tab_emote");
        if (!tabItem) return;

        ImGui.TextUnformatted("Notification Settings:");

        var enableEmoteNotifications = Config.EnableEmoteNotifications;
        if (ImGui.Checkbox("Enable emote notifications", ref enableEmoteNotifications))
        {
            Config.EnableEmoteNotifications = enableEmoteNotifications;
            _configService.Save();
        }

        var emoteChatIndex = GetChatTypeIndex(Config.EmoteNotificationChatType);
        if (ImGui.Combo("Notification channel", ref emoteChatIndex, ChatTypeOptionsText))
        {
            Config.EmoteNotificationChatType = ChatTypeOptions[emoteChatIndex].Type;
            _configService.Save();
        }

        EmoteSoundConfig();

        ImGui.Separator();
        ImGui.TextUnformatted("Combat Settings:");
        var enableInDuty = Config.EnableEmoteNotificationInCombat;
        if (ImGui.Checkbox("Enable emote notifications while in combat", ref enableInDuty))
        {
            Config.EnableEmoteNotificationInCombat = enableInDuty;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Self-emote settings:");

        var allowSelfEmote = Config.ShowSelfEmote;
        if (ImGui.Checkbox("Show self-emote in history", ref allowSelfEmote))
        {
            Config.ShowSelfEmote = allowSelfEmote;
            _configService.Save();
        }

        var notifyOnSelfEmote = Config.NotifyOnSelfEmote;
        if (ImGui.Checkbox("Notify on self-emote", ref notifyOnSelfEmote))
        {
            Config.NotifyOnSelfEmote = notifyOnSelfEmote;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Chat rate limit (per emote):");

        var rateLimitEnabled = Config.EnableEmoteChatNotificationRateLimit;
        if (ImGui.Checkbox("Limit per-emote chat notifications (rolling window)", ref rateLimitEnabled))
        {
            Config.EnableEmoteChatNotificationRateLimit = rateLimitEnabled;
            _configService.Save();
        }

        using (ImRaii.Disabled(!Config.EnableEmoteChatNotificationRateLimit))
        {
            var modeIndex = (int)Config.EmoteChatNotificationRateLimitMode;
            if (ImGui.Combo("Rate limit mode", ref modeIndex, "Rolling window\0Fixed window\0"))
            {
                Config.EmoteChatNotificationRateLimitMode = (EmoteChatNotificationRateLimitMode)modeIndex;
                _configService.Save();
            }

            var maxCount = Config.EmoteChatNotificationRateLimitMaxCount;
            if (ImGui.InputInt("Max notifications per emote window", ref maxCount))
            {
                Config.EmoteChatNotificationRateLimitMaxCount = Math.Clamp(maxCount, 1, 1000);
                _configService.Save();
            }

            var windowSeconds = Config.EmoteChatNotificationRateLimitWindowSeconds;
            if (ImGui.InputInt("Window (seconds)", ref windowSeconds))
            {
                Config.EmoteChatNotificationRateLimitWindowSeconds = Math.Clamp(windowSeconds, 1, 3600);
                _configService.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Overlay window:");
        var enableOverlay = Config.EnableEmoteOverlayWindow;
        if (ImGui.Checkbox("Enable emote overlay window", ref enableOverlay))
        {
            Config.EnableEmoteOverlayWindow = enableOverlay;
            _configService.Save();
        }
    }

    private void EmoteSoundConfig()
    {
        var soundEnabled = Config.EnableEmoteSoundNotification;
        if(ImGui.Checkbox("Enable sound notification on emote", ref soundEnabled))
        {
            Config.EnableEmoteSoundNotification = soundEnabled;
            _configService.Save();
        }

        ImGui.TextUnformatted("Sound to play (SE.1 - SE.16)");
        var selectedIndex = _configService.Configuration.EmoteSoundNotificationId;
        using (var combo = ImRaii.Combo("##ohhey_config_combo_emote_sound", $"SE.{selectedIndex}"))
        {
            if (combo)
            {
                for (var i = 1; i <= 16; i++)
                {
                    var isSelected = selectedIndex == i;
                    if (ImGui.Selectable($"SE.{i}", isSelected))
                    {
                        selectedIndex = (uint)i;
                        Config.EmoteSoundNotificationId = selectedIndex;
                        _configService.Save();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
            }
        }
        ImGui.SameLine();
        if (ImGui.ArrowButton("##ohhey_config_button_emote_sound_play", ImGuiDir.Right))
        {
            UIGlobals.PlayChatSoundEffect(_configService.Configuration.EmoteSoundNotificationId);
        }

        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            ImGui.TextUnformatted("Play the selected sound effect");
        }
    }

    private void DebugConfig()
    {
        using var tabItem = ImRaii.TabItem("Debug##ohhey_config_tab_debug");
        if (!tabItem) return;

        ImGui.TextUnformatted("Emote chat rate limit (per emote):");
        var status = _emoteService.GetEmoteChatRateLimitStatus();
        ImGui.TextUnformatted($"Enabled: {status.Enabled}");
        ImGui.TextUnformatted($"Mode: {status.Mode}");
        ImGui.TextUnformatted($"Window: {status.WindowSeconds}s");
        ImGui.TextUnformatted($"Max per emote window: {status.MaxCount}");
        ImGui.TextUnformatted($"Last emote: {status.LastEmoteName ?? "None"}");
        ImGui.TextUnformatted($"Current in last emote window: {status.CurrentCount}");
        ImGui.TextUnformatted($"Tracked emotes: {status.TrackedEmoteCount}");
        ImGui.TextUnformatted($"Suppressed: {status.SuppressedCount}");
        ImGui.TextUnformatted(status.NextAllowedUtc is null
            ? "Next allowed: now"
            : $"Next allowed: {status.NextAllowedUtc.Value.ToLocalTime():HH:mm:ss}");

        if (ImGui.Button("Reset emote rate limit counters"))
        {
            _emoteService.ResetEmoteChatRateLimitCounters();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recent emotes (last 60s):");
        var recentEmotes = _emoteService.GetRecentEmotes(TimeSpan.FromSeconds(60));
        if (recentEmotes.Count == 0)
        {
            ImGui.TextUnformatted("No emotes yet.");
            return;
        }

        using var table = ImRaii.Table("##ohhey_debug_emote_table", 4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV);
        if (!table) return;

        ImGui.TableSetupColumn("Ago", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var now = DateTime.UtcNow;
        foreach (var emote in recentEmotes)
        {
            var age = now - emote.Timestamp.ToUniversalTime();
            var seconds = Math.Clamp((int)age.TotalSeconds, 0, 999);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted($"{seconds}s");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(emote.InitiatorName.ToString());
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(emote.EmoteName.ToString());
            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(emote.TargetName?.ToString() ?? "Unknown");
        }
    }

    private static int GetChatTypeIndex(XivChatType chatType)
    {
        for (var i = 0; i < ChatTypeOptions.Length; i++)
        {
            if (ChatTypeOptions[i].Type == chatType)
            {
                return i;
            }
        }

        return 0;
    }
}
