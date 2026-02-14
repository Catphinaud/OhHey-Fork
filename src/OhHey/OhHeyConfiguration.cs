// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Configuration;

namespace OhHey;

[Serializable]
public class OhHeyConfiguration : IPluginConfiguration
{
    // General Settings
    public int Version { get; set; } = 0;

    public bool EnableMainWindowCloseHotkey { get; set; } = false;

    // Target Settings
    public bool EnableTargetNotifications { get; set; } = true;

    public Dalamud.Game.Text.XivChatType TargetNotificationChatType { get; set; }
        = Dalamud.Game.Text.XivChatType.Echo;

    public bool EnableTargetSoundNotification { get; set; } = false;

    public uint TargetSoundNotificationId { get; set; } = 1;

    public bool ShowSelfTarget { get; set; } = true;

    public bool NotifyOnSelfTarget { get; set; } = false;

    public bool EnableTargetNotificationInCombat { get; set; } = false;

    // Emote Settings
    public bool EnableEmoteNotifications { get; set; } = true;

    public Dalamud.Game.Text.XivChatType EmoteNotificationChatType { get; set; }
        = Dalamud.Game.Text.XivChatType.Echo;

    public bool EnableEmoteSoundNotification { get; set; } = false;

    public uint EmoteSoundNotificationId { get; set; } = 1;

    public bool ShowSelfEmote { get; set; } = false;

    public bool NotifyOnSelfEmote { get; set; } = false;

    public bool EnableEmoteNotificationInCombat { get; set; } = true;

    public bool EnableEmoteChatNotificationRateLimit { get; set; } = false;

    public int EmoteChatNotificationRateLimitWindowSeconds { get; set; } = 5;

    public int EmoteChatNotificationRateLimitMaxCount { get; set; } = 5;

    public EmoteChatNotificationRateLimitMode EmoteChatNotificationRateLimitMode { get; set; }
        = EmoteChatNotificationRateLimitMode.FixedWindow;

    public bool EnableEmoteOverlayWindow { get; set; } = false;

    public bool ShowWorldNameInChatNotifications { get; set; } = true;
}

public enum EmoteChatNotificationRateLimitMode
{
    RollingWindow = 0,
    FixedWindow = 1
}
