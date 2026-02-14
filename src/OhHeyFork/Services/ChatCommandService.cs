﻿// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using OhHeyFork.UI;

namespace OhHeyFork.Services;

public sealed class ChatCommandService : IDisposable
{
    private const string CommandName = "/ohheyfork";
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _logger;
    private readonly MainWindow _mainWindow;
    private readonly ConfigurationWindow _configWindow;
    private readonly EmoteDebugWindow _emoteDebugWindow;

    public ChatCommandService(ICommandManager commandManager, IChatGui chatGui, IPluginLog logger, MainWindow mainWindow, ConfigurationWindow configWindow, EmoteDebugWindow emoteDebugWindow)
    {
        _commandManager = commandManager;
        _chatGui = chatGui;
        _logger = logger;
        _mainWindow = mainWindow;
        _configWindow = configWindow;
        _emoteDebugWindow = emoteDebugWindow;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the OhHeyFork main window if used by itself or when using '/ohheyfork main'.\n" +
                          "Use '/ohheyfork config' to open the configuration window.\n" +
                          "Use '/ohheyfork debug' to open the emote log message debug window.",
        });
    }

    private void OnCommand(string command, string argsString)
    {
        var args = argsString.Split(" ").Select(x => x.Trim()).ToArray();
        _logger.Debug("CommandInput: {Command} Args: {Args} Raw Args: {RawArgs}", command, args, argsString);
        if (args.Length == 0 || string.IsNullOrWhiteSpace(argsString))
        {
            _mainWindow.Toggle();
            return;
        }
        switch (args[0].ToLower())
        {
            case "main":
                _mainWindow.Toggle();
                break;
            case "config":
            case "settings":
                _configWindow.Toggle();
                break;
            case "debug":
                _emoteDebugWindow.Toggle();
                break;
            default:
                ShowHelp();
                break;
        }
    }

    private void ShowHelp()
    {
        var builder = new SeStringBuilder();
        builder
            .AddUiForeground("[Oh Hey!] Chat Commands: \n\n", 537).AddUiForegroundOff()
            .AddText("- ").AddUiForeground("/ohheyfork", 37).AddUiForegroundOff().AddText(" Opens the main window.\n")
            .AddText("- ").AddUiForeground("/ohheyfork main", 37).AddUiForegroundOff().AddText(" Opens the main window.\n")
            .AddText("- ").AddUiForeground("/ohheyfork [config|settings]", 37).AddUiForegroundOff().AddText(" Opens the configuration window.\n")
            .AddText("- ").AddUiForeground("/ohheyfork debug", 37).AddUiForegroundOff().AddText(" Opens the emote debug window.\n")
            .AddText("- ").AddUiForeground("/ohheyfork help", 37).AddUiForegroundOff().AddText(" Shows this help message.");
        _chatGui.Print(builder.Build());
    }


    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
    }
}
