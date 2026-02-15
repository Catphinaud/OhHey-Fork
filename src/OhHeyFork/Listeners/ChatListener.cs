// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace OhHeyFork.Listeners;

public sealed class ChatListener : IDisposable
{
    private readonly IPluginLog _logger;
    private readonly IChatGui _chatGui;

    public delegate void OnMessageDelegate(
        XivChatType type,
        int timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled);

    public event OnMessageDelegate? Message;

    public ChatListener(IPluginLog logger, IChatGui chatGui)
    {
        _logger = logger;
        _chatGui = chatGui;
        _chatGui.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(
        XivChatType type,
        int timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled)
    {
        var handler = Message;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(type, timestamp, ref sender, ref message, ref isHandled);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in chat message handler.");
        }
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
    }
}
