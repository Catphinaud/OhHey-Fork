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
}
