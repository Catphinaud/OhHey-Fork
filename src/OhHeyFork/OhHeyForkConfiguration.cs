// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace OhHeyFork;

[Serializable]
public class OhHeyForkConfiguration : IPluginConfiguration
{
    public const int CurrentVersion = 2;

    // General schema version.
    public int Version { get; set; } = CurrentVersion;

    // Active structured settings.
    public OhHeyForkSettings Settings { get; set; } = new();

    // Legacy flat v1 bucket; kept only to migrate existing user configs.
    public OhHeyForkConfigurationV1Legacy? V1 { get; set; }

    // Legacy root fields from v0 schema; kept so old JSON can still deserialize.
    public bool EnableMainWindowCloseHotkey { get; set; } = false;
    public bool EnableTargetNotifications { get; set; } = true;
    public XivChatType TargetNotificationChatType { get; set; } = XivChatType.Echo;
    public bool EnableTargetSoundNotification { get; set; } = false;
    public uint TargetSoundNotificationId { get; set; } = 1;
    public bool ShowSelfTarget { get; set; } = true;
    public bool NotifyOnSelfTarget { get; set; } = false;
    public bool EnableTargetNotificationInCombat { get; set; } = false;
    public bool EnableEmoteNotifications { get; set; } = true;
    public XivChatType EmoteNotificationChatType { get; set; } = XivChatType.Echo;
    public bool EnableEmoteSoundNotification { get; set; } = false;
    public uint EmoteSoundNotificationId { get; set; } = 1;
    public bool ShowSelfEmote { get; set; } = false;
    public bool NotifyOnSelfEmote { get; set; } = false;
    public bool EnableEmoteNotificationInCombat { get; set; } = true;
    public bool EnableEmoteChatNotificationRateLimit { get; set; } = false;
    public int EmoteChatNotificationRateLimitWindowSeconds { get; set; } = 5;
    public int EmoteChatNotificationRateLimitMaxCount { get; set; } = 5;
    public EmoteChatNotificationRateLimitMode EmoteChatNotificationRateLimitMode { get; set; } = EmoteChatNotificationRateLimitMode.FixedWindow;
    public bool EnableEmoteOverlayWindow { get; set; } = false;
    public bool ShowWorldNameInChatNotifications { get; set; } = true;
}

public enum EmoteChatNotificationRateLimitMode
{
    RollingWindow = 0,
    FixedWindow = 1
}

[Serializable]
public sealed class OhHeyForkSettings
{
    public OhHeyForkGeneralSettings General { get; set; } = new();
    public OhHeyForkTargetSettings Target { get; set; } = new();
    public OhHeyForkEmoteSettings Emote { get; set; } = new();
    public OhHeyForkNotificationDisplaySettings NotificationDisplay { get; set; } = new();
}

[Serializable]
public sealed class OhHeyForkGeneralSettings
{
    public bool EnableMainWindowCloseHotkey { get; set; } = false;
}

[Serializable]
public sealed class OhHeyForkTargetSettings
{
    public bool EnableNotifications { get; set; } = true;
    public XivChatType NotificationChatType { get; set; } = XivChatType.SystemMessage;
    public bool EnableSoundNotification { get; set; } = false;
    public uint SoundNotificationId { get; set; } = 1;
    public bool ShowSelf { get; set; } = false;
    public bool NotifyOnSelf { get; set; } = false;
    public bool EnableNotificationInCombat { get; set; } = false;
}

[Serializable]
public sealed class OhHeyForkEmoteSettings
{
    public bool EnableNotifications { get; set; } = true;
    public XivChatType NotificationChatType { get; set; } = XivChatType.SystemMessage;
    public bool SuppressDuplicateTargetedChatLine { get; set; } = true;
    public bool EnableSoundNotification { get; set; } = false;
    public uint SoundNotificationId { get; set; } = 1;
    public bool ShowSelf { get; set; } = true;
    public bool NotifyOnSelf { get; set; } = true;
    public bool EnableNotificationInCombat { get; set; } = true;
    public bool EnableOverlayWindow { get; set; } = false;
    public OhHeyForkEmoteRateLimitSettings ChatRateLimit { get; set; } = new();
}

[Serializable]
public sealed class OhHeyForkEmoteRateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int WindowSeconds { get; set; } = 5;
    public int MaxCount { get; set; } = 5;
    public EmoteChatNotificationRateLimitMode Mode { get; set; } = EmoteChatNotificationRateLimitMode.FixedWindow;
}

[Serializable]
public sealed class OhHeyForkNotificationDisplaySettings
{
    public bool ShowWorldNameInChatNotifications { get; set; } = true;
}

[Serializable]
public sealed class OhHeyForkConfigurationV1Legacy
{
    public bool EnableMainWindowCloseHotkey { get; set; } = false;
    public bool EnableTargetNotifications { get; set; } = true;
    public XivChatType TargetNotificationChatType { get; set; } = XivChatType.SystemMessage;
    public bool EnableTargetSoundNotification { get; set; } = false;
    public uint TargetSoundNotificationId { get; set; } = 1;
    public bool ShowSelfTarget { get; set; } = false;
    public bool NotifyOnSelfTarget { get; set; } = false;
    public bool EnableTargetNotificationInCombat { get; set; } = false;
    public bool EnableEmoteNotifications { get; set; } = true;
    public XivChatType EmoteNotificationChatType { get; set; } = XivChatType.SystemMessage;
    public bool EnableEmoteSoundNotification { get; set; } = false;
    public uint EmoteSoundNotificationId { get; set; } = 1;
    public bool ShowSelfEmote { get; set; } = true;
    public bool NotifyOnSelfEmote { get; set; } = true;
    public bool EnableEmoteNotificationInCombat { get; set; } = true;
    public bool EnableEmoteChatNotificationRateLimit { get; set; } = true;
    public int EmoteChatNotificationRateLimitWindowSeconds { get; set; } = 5;
    public int EmoteChatNotificationRateLimitMaxCount { get; set; } = 5;
    public EmoteChatNotificationRateLimitMode EmoteChatNotificationRateLimitMode { get; set; } = EmoteChatNotificationRateLimitMode.FixedWindow;
    public bool EnableEmoteOverlayWindow { get; set; } = false;
    public bool ShowWorldNameInChatNotifications { get; set; } = true;
}
