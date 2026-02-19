// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace OhHeyFork.Services;

public class ConfigurationService
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _logger;

    public OhHeyForkConfiguration Configuration { get; }
    public OhHeyForkSettings Settings => Configuration.Settings;

    public event EventHandler<OhHeyForkConfiguration>? ConfigurationChanged;

    public ConfigurationService(IPluginLog logger, IDalamudPluginInterface pluginInterface)
    {
        _logger = logger;
        _pluginInterface = pluginInterface;
        Configuration = LoadConfiguration();
    }

    private OhHeyForkConfiguration LoadConfiguration()
    {
        if (_pluginInterface.GetPluginConfig() is not OhHeyForkConfiguration config)
        {
            config = new OhHeyForkConfiguration();
            _logger.Info("No existing configuration found. Creating new configuration with version {ConfigurationVersion}.",
                config.Version);
            _pluginInterface.SavePluginConfig(config);
        }
        else
        {
            _logger.Info("Configuration loaded. Version: {Version}", config.Version);
        }

        if (TryMigrateConfiguration(config))
        {
            _pluginInterface.SavePluginConfig(config);
            _logger.Info("Configuration migrated to version {Version}.", config.Version);
        }

        return config;
    }

    public void Save()
    {
        _pluginInterface.SavePluginConfig(Configuration);
        _logger.Debug("Configuration saved.");
        OnConfigurationChanged();
    }


    private void OnConfigurationChanged()
    {
        var handler = ConfigurationChanged;
        try
        {
            handler?.Invoke(this, Configuration);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error invoking ConfigurationChanged event.");
        }
    }

    private bool TryMigrateConfiguration(OhHeyForkConfiguration config)
    {
        var migrated = false;

        config.Settings ??= new OhHeyForkSettings();

        if (config.Version < 1)
        {
            // v0 root fields are left in place as backup.
            config.Version = OhHeyForkConfiguration.CurrentVersion;
            config.V1 = null;
            migrated = true;
        }
        else if (config.Version < 2)
        {
            // v1 flat bucket -> backup to legacy root fields, then use v2 defaults in Settings.
            if (config.V1 is not null)
            {
                CopyLegacyV1ToV0Root(config, config.V1);
            }

            config.Version = OhHeyForkConfiguration.CurrentVersion;
            config.V1 = null;
            migrated = true;
        }
        else if (config.V1 is not null)
        {
            // Clean up stale legacy bucket in already-upgraded configs.
            config.V1 = null;
            migrated = true;
        }

        return migrated;
    }

    private static void CopyLegacyV1ToV0Root(OhHeyForkConfiguration destination, OhHeyForkConfigurationV1Legacy source)
    {
        destination.EnableMainWindowCloseHotkey = source.EnableMainWindowCloseHotkey;
        destination.EnableTargetNotifications = source.EnableTargetNotifications;
        destination.TargetNotificationChatType = source.TargetNotificationChatType;
        destination.EnableTargetSoundNotification = source.EnableTargetSoundNotification;
        destination.TargetSoundNotificationId = source.TargetSoundNotificationId;
        destination.ShowSelfTarget = source.ShowSelfTarget;
        destination.NotifyOnSelfTarget = source.NotifyOnSelfTarget;
        destination.EnableTargetNotificationInCombat = source.EnableTargetNotificationInCombat;
        destination.EnableEmoteNotifications = source.EnableEmoteNotifications;
        destination.EmoteNotificationChatType = source.EmoteNotificationChatType;
        destination.EnableEmoteSoundNotification = source.EnableEmoteSoundNotification;
        destination.EmoteSoundNotificationId = source.EmoteSoundNotificationId;
        destination.ShowSelfEmote = source.ShowSelfEmote;
        destination.NotifyOnSelfEmote = source.NotifyOnSelfEmote;
        destination.EnableEmoteNotificationInCombat = source.EnableEmoteNotificationInCombat;
        destination.EnableEmoteChatNotificationRateLimit = source.EnableEmoteChatNotificationRateLimit;
        destination.EmoteChatNotificationRateLimitWindowSeconds = source.EmoteChatNotificationRateLimitWindowSeconds;
        destination.EmoteChatNotificationRateLimitMaxCount = source.EmoteChatNotificationRateLimitMaxCount;
        destination.EmoteChatNotificationRateLimitMode = source.EmoteChatNotificationRateLimitMode;
        destination.EnableEmoteOverlayWindow = source.EnableEmoteOverlayWindow;
        destination.ShowWorldNameInChatNotifications = source.ShowWorldNameInChatNotifications;
    }
}
