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
using OhHeyFork;
using OhHeyFork.Services;

namespace OhHeyFork.UI;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public class ConfigurationWindow : Window
{
    private readonly ConfigurationService _configService;
    private readonly EmoteService _emoteService;
    private readonly IEmoteChatRateLimitService _emoteChatRateLimitService;
    private OhHeyForkGeneralSettings GeneralConfigValues => _configService.Settings.General;
    private OhHeyForkTargetSettings TargetConfigValues => _configService.Settings.Target;
    private OhHeyForkEmoteSettings EmoteConfigValues => _configService.Settings.Emote;

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

    public ConfigurationWindow(ConfigurationService configService, EmoteService emoteService, IEmoteChatRateLimitService emoteChatRateLimitService)
        : base("Oh Hey! Configuration##ohhey_config_window")
    {
        _configService = configService;
        _emoteService = emoteService;
        _emoteChatRateLimitService = emoteChatRateLimitService;

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

        var enableCloseHotkey = GeneralConfigValues.EnableMainWindowCloseHotkey;
        if (ImGui.Checkbox("Enable closing the main window with ESC", ref enableCloseHotkey))
        {
            GeneralConfigValues.EnableMainWindowCloseHotkey = enableCloseHotkey;
            _configService.Save();
        }
    }

    private void TargetConfig()
    {
        using var tabItem = ImRaii.TabItem("Targets##ohhey_config_tab_target");
        if (!tabItem) return;

        ImGui.TextUnformatted("Notification Settings:");

        var enableTargetNotifications = TargetConfigValues.EnableNotifications;
        if (ImGui.Checkbox("Enable target notifications", ref enableTargetNotifications))
        {
            TargetConfigValues.EnableNotifications = enableTargetNotifications;
            _configService.Save();
        }

        var targetChatIndex = GetChatTypeIndex(TargetConfigValues.NotificationChatType);
        if (ImGui.Combo("Notification channel", ref targetChatIndex, ChatTypeOptionsText))
        {
            TargetConfigValues.NotificationChatType = ChatTypeOptions[targetChatIndex].Type;
            _configService.Save();
        }

        TargetSoundConfig();

        ImGui.Separator();
        ImGui.TextUnformatted("Combat Settings:");
        var enableInDuty = TargetConfigValues.EnableNotificationInCombat;
        if (ImGui.Checkbox("Enable target notifications while in combat", ref enableInDuty))
        {
            TargetConfigValues.EnableNotificationInCombat = enableInDuty;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Self-target settings:");

        var allowSelfTarget = TargetConfigValues.ShowSelf;
        if (ImGui.Checkbox("Show self-targeting in target list", ref allowSelfTarget))
        {
            TargetConfigValues.ShowSelf = allowSelfTarget;
            _configService.Save();
        }

        var notifyOnSelfTarget = TargetConfigValues.NotifyOnSelf;
        if (ImGui.Checkbox("Notify on self-target", ref notifyOnSelfTarget))
        {
            TargetConfigValues.NotifyOnSelf = notifyOnSelfTarget;
            _configService.Save();
        }
    }

    private void TargetSoundConfig()
    {
        var soundEnabled = TargetConfigValues.EnableSoundNotification;
        if(ImGui.Checkbox("Enable sound notification on target", ref soundEnabled))
        {
            TargetConfigValues.EnableSoundNotification = soundEnabled;
            _configService.Save();
        }

        ImGui.TextUnformatted("Sound to play (SE.1 - SE.16)");
        var selectedIndex = _configService.Settings.Target.SoundNotificationId;
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
                        TargetConfigValues.SoundNotificationId = selectedIndex;
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
            UIGlobals.PlayChatSoundEffect(_configService.Settings.Target.SoundNotificationId);
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

        var enableEmoteNotifications = EmoteConfigValues.EnableNotifications;
        if (ImGui.Checkbox("Enable emote notifications", ref enableEmoteNotifications))
        {
            EmoteConfigValues.EnableNotifications = enableEmoteNotifications;
            _configService.Save();
        }

        var emoteChatIndex = GetChatTypeIndex(EmoteConfigValues.NotificationChatType);
        if (ImGui.Combo("Notification channel", ref emoteChatIndex, ChatTypeOptionsText))
        {
            EmoteConfigValues.NotificationChatType = ChatTypeOptions[emoteChatIndex].Type;
            _configService.Save();
        }

        var suppressDuplicateTargetedLine = EmoteConfigValues.SuppressDuplicateTargetedChatLine;
        if (ImGui.Checkbox("Suppress duplicate targeted emote chat line", ref suppressDuplicateTargetedLine))
        {
            EmoteConfigValues.SuppressDuplicateTargetedChatLine = suppressDuplicateTargetedLine;
            _configService.Save();
        }

        EmoteSoundConfig();

        ImGui.Separator();
        ImGui.TextUnformatted("Combat Settings:");
        var enableInDuty = EmoteConfigValues.EnableNotificationInCombat;
        if (ImGui.Checkbox("Enable emote notifications while in combat", ref enableInDuty))
        {
            EmoteConfigValues.EnableNotificationInCombat = enableInDuty;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Self-emote settings:");

        var allowSelfEmote = EmoteConfigValues.ShowSelf;
        if (ImGui.Checkbox("Show self-emote in history", ref allowSelfEmote))
        {
            EmoteConfigValues.ShowSelf = allowSelfEmote;
            _configService.Save();
        }

        var notifyOnSelfEmote = EmoteConfigValues.NotifyOnSelf;
        if (ImGui.Checkbox("Notify on self-emote", ref notifyOnSelfEmote))
        {
            EmoteConfigValues.NotifyOnSelf = notifyOnSelfEmote;
            _configService.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Chat rate limit (per emote):");

        var rateLimitEnabled = EmoteConfigValues.ChatRateLimit.Enabled;
        if (ImGui.Checkbox("Limit per-emote chat notifications (rolling window)", ref rateLimitEnabled))
        {
            EmoteConfigValues.ChatRateLimit.Enabled = rateLimitEnabled;
            _configService.Save();
        }

        using (ImRaii.Disabled(!EmoteConfigValues.ChatRateLimit.Enabled))
        {
            var modeIndex = (int)EmoteConfigValues.ChatRateLimit.Mode;
            if (ImGui.Combo("Rate limit mode", ref modeIndex, "Rolling window\0Fixed window\0"))
            {
                EmoteConfigValues.ChatRateLimit.Mode = (EmoteChatNotificationRateLimitMode)modeIndex;
                _configService.Save();
            }

            var maxCount = EmoteConfigValues.ChatRateLimit.MaxCount;
            if (ImGui.InputInt("Max notifications per emote window", ref maxCount))
            {
                EmoteConfigValues.ChatRateLimit.MaxCount = Math.Clamp(maxCount, 1, 1000);
                _configService.Save();
            }

            var windowSeconds = EmoteConfigValues.ChatRateLimit.WindowSeconds;
            if (ImGui.InputInt("Window (seconds)", ref windowSeconds))
            {
                EmoteConfigValues.ChatRateLimit.WindowSeconds = Math.Clamp(windowSeconds, 1, 3600);
                _configService.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Overlay window:");
        var enableOverlay = EmoteConfigValues.EnableOverlayWindow;
        if (ImGui.Checkbox("Enable emote overlay window", ref enableOverlay))
        {
            EmoteConfigValues.EnableOverlayWindow = enableOverlay;
            _configService.Save();
        }
    }

    private void EmoteSoundConfig()
    {
        var soundEnabled = EmoteConfigValues.EnableSoundNotification;
        if(ImGui.Checkbox("Enable sound notification on emote", ref soundEnabled))
        {
            EmoteConfigValues.EnableSoundNotification = soundEnabled;
            _configService.Save();
        }

        ImGui.TextUnformatted("Sound to play (SE.1 - SE.16)");
        var selectedIndex = _configService.Settings.Emote.SoundNotificationId;
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
                        EmoteConfigValues.SoundNotificationId = selectedIndex;
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
            UIGlobals.PlayChatSoundEffect(_configService.Settings.Emote.SoundNotificationId);
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
        var status = _emoteChatRateLimitService.GetStatus();
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
            _emoteChatRateLimitService.ResetCounters();
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
            ImGui.TextUnformatted(_emoteService.GetEmoteDisplayName(emote.EmoteId));
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
