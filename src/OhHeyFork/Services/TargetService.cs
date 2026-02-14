// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using OhHeyFork.Listeners;

namespace OhHeyFork.Services;

public sealed class TargetService : IDisposable
{
    private const string ChatSenderName = "OhHeyFork";
    private readonly IPluginLog _logger;
    private readonly TargetListener _targetListener;
    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly ConfigurationService _configService;
    private readonly IObjectTable _objectTable;
    private readonly IPlayerState _playerState;
    private readonly Dictionary<uint, string> _worlds;

    public List<TargetEvent> CurrentTargets { get; } = [];

    public List<TargetEvent> TargetHistory { get; } = [];

    public TargetService(IPluginLog logger, TargetListener targetListener, IChatGui chatGui,
        ConfigurationService configService, ICondition condition, IObjectTable objectTable, IPlayerState playerState, IDataManager dataManager)
    {
        _logger = logger;
        _targetListener = targetListener;
        _chatGui = chatGui;
        _configService = configService;
        _condition = condition;
        _objectTable = objectTable;
        _playerState = playerState;
        _worlds = dataManager
            .GetExcelSheet<Lumina.Excel.Sheets.World>()
            .ToDictionary(world => world.RowId, world => world.Name.ToString());

        _targetListener.Target += OnTarget;
        _targetListener.TargetRemoved += OnTargetRemoved;
    }

    private void PushToHistory(TargetEvent evt)
    {
        var position = TargetHistory.FindIndex(target => target.GameObjectId == evt.GameObjectId);
        if (position != -1)
        {
            TargetHistory.RemoveAt(position);
        }
        var targetEvent = evt with
        {
            Timestamp = DateTime.Now
        };
        if (TargetHistory.Count > 10)
        {
            TargetHistory.RemoveAt(0);
        }
        TargetHistory.Add(targetEvent);
    }

    private void OnTarget(object? sender, TargetEvent e)
    {
        _logger.Debug("Targeted by {Name} (ID: {GameObjectId} Self: {IsSelf})", e.Name, e.GameObjectId, e.IsSelf);
        if (CurrentTargets.Exists(target => target.GameObjectId == e.GameObjectId))
        {
            _logger.Warning("Received duplicate target event for {Name} ({GameObjectId})", e.Name, e.GameObjectId);
            return;
        }

        if (e.IsSelf)
        {
            if (_configService.Configuration.ShowSelfTarget)
            {
                UpdateTargetList(e);
            }
        }
        else {
            UpdateTargetList(e);
        }

        if (!_configService.Configuration.EnableTargetNotifications) return;

        if (e.IsSelf && !_configService.Configuration.NotifyOnSelfTarget) return;
        if (!_configService.Configuration.EnableTargetNotificationInCombat &&
            _condition[ConditionFlag.InCombat]) return;
        SendNotification(e);
    }

    private void UpdateTargetList(TargetEvent e)
    {
        var position = TargetHistory.FindIndex(target => target.GameObjectId == e.GameObjectId);
        if (position != -1)
        {
            TargetHistory.RemoveAt(position);
        }
        CurrentTargets.Add(e);
    }

    private void OnTargetRemoved(object? sender, ulong e)
    {
        var position = CurrentTargets.FindIndex(target => target.GameObjectId == e);
        if (position == -1)
        {
            // This happens when we don't handle self targeting, since we still receive the target removed event.
            if (_objectTable.LocalPlayer?.GameObjectId == e) return;
            _logger.Warning("Received target removed event for unknown GameObjectId {GameObjectId}", e);
            return;
        }
        var target = CurrentTargets[position];
        _logger.Debug("No longer targeted by {Name} (ID: {GameObjectId} Self: {IsSelf})",
            target.Name, target.GameObjectId, target.IsSelf);
        CurrentTargets.RemoveAt(position);
        PushToHistory(target);
    }

    public void ClearHistory() => TargetHistory.Clear();

    private void SendNotification(TargetEvent evt)
    {
        var builder = new SeStringBuilder();

        builder.AddUiForeground("[Oh Hey!] ", 537);
        builder.AddUiForegroundOff();
        builder.Add(new PlayerPayload(evt.SeName.TextValue, evt.WorldId));

        if (
            _configService.Configuration.ShowWorldNameInChatNotifications &&
            _playerState.HomeWorld.RowId != evt.WorldId &&
            _worlds.TryGetValue(evt.WorldId, out string? worldName) &&
            !string.IsNullOrEmpty(worldName)
        ) {
            builder.AddIcon(BitmapFontIcon.CrossWorld);
            builder.AddText(worldName);
        }

        builder.AddText(" is targeting you!");

        PrintChatMessage(_configService.Configuration.TargetNotificationChatType,  builder.Build());

        if (_configService.Configuration.EnableTargetSoundNotification)
        {
            UIGlobals.PlayChatSoundEffect(_configService.Configuration.TargetSoundNotificationId);
        }
    }

    public void Dispose()
    {
        _targetListener.Target -= OnTarget;
        _targetListener.TargetRemoved -= OnTargetRemoved;
    }

    private void PrintChatMessage(XivChatType chatType, SeString message)
    {
        if (chatType == XivChatType.None)
        {
            _chatGui.Print(message);
            return;
        }

        _chatGui.Print(new XivChatEntry
        {
            Message = message,
            Type = chatType,
            Name = RequiresChatSenderName(chatType) ? ChatSenderName : string.Empty
        });
    }

    private static bool RequiresChatSenderName(XivChatType chatType)
    {
        return chatType != XivChatType.Notice
               && chatType != XivChatType.Echo
               && chatType != XivChatType.Urgent
               && chatType != XivChatType.SystemMessage
               && chatType != XivChatType.Debug;
    }
}
