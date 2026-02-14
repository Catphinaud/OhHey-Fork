// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using OhHeyFork.Core.IoC;
using OhHeyFork.Listeners;
using OhHeyFork.Services;
using OhHeyFork.UI;

namespace OhHeyFork;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class OhHeyForkPlugin : IDalamudPlugin
{
    private readonly IServiceProvider _provider;
    public OhHeyForkPlugin(IDalamudPluginInterface pluginInterface)
    {
        var services = new ServiceCollection();
        services
            .AddSingleton(pluginInterface)
            .AddDalamudService<IPluginLog>()
            .AddDalamudService<IFramework>()
            .AddDalamudService<IClientState>()
            .AddDalamudService<IObjectTable>()
            .AddDalamudService<ITargetManager>()
            .AddDalamudService<IGameInteropProvider>()
            .AddDalamudService<IDataManager>()
            .AddDalamudService<IChatGui>()
            .AddDalamudService<ICommandManager>()
            .AddDalamudService<ICondition>()
            .AddDalamudService<ITextureProvider>()
            .AddDalamudService<ISeStringEvaluator>()
            .AddDalamudService<IPlayerState>()
            .AddDalamudService<IGameConfig>()
            .AddSingleton<ConfigurationService>()
            .AddSingleton<EmoteListener>()
            .AddSingleton<EmoteService>()
            .AddSingleton<IEmoteLogMessageService, EmoteLogMessageService>()
            .AddSingleton<TargetListener>()
            .AddSingleton<TargetService>()
            .AddDalamudWindow<ConfigurationWindow>()
            .AddDalamudWindow<MainWindow>()
            .AddDalamudWindow<EmoteOverlayWindow>()
            .AddDalamudWindow<EmoteDebugWindow>()
            .AddSingleton<KeyedWindowService>()
            .AddSingleton<WindowService>()
            .AddSingleton<ChatCommandService>();

        _provider = services.BuildServiceProvider();
        _ = _provider.GetRequiredService<WindowService>();
        _ = _provider.GetRequiredService<ChatCommandService>();
    }

    public void Dispose()
    {
        (_provider as IDisposable)?.Dispose();
    }
}
