// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using OhHeyFork;
using OhHeyFork.Listeners;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace OhHeyFork.Services;

public sealed class EmoteService : IDisposable
{
    private const string ChatSenderName = "OhHeyFork";
    public const int MaxEmoteHistory = 10;

    private readonly IPluginLog _logger;
    private readonly EmoteListener _emoteListener;
    private readonly ChatListener _chatListener;
    private readonly IChatGui _chatGui;
    private readonly IEmoteLogMessageService _emoteLogMessageService;
    private readonly ConfigurationService _configService;
    private readonly ICondition _condition;
    private readonly IPlayerState _playerState;
    private readonly IGameConfig _gameConfig;
    private readonly ITargetManager _targetManager;
    private readonly IObjectTable _objectTable;
    private readonly IDataManagerCacheService _dataManagerCacheService;
    private readonly IEmoteChatRateLimitService _emoteChatRateLimitService;
    private readonly object _replayDebugLock = new();
    private DateTime? _lastReplayAtUtc;
    private ushort? _lastReplayEmoteId;
    private ulong? _lastReplayRequestedTargetId;
    private ulong? _lastReplayResolvedTargetId;
    private ulong? _lastReplayPreviousTargetId;
    private bool _lastReplayChangedTargetForReplay;
    private bool _lastReplayRestoredPreviousTarget;
    private bool _lastReplayClearedTarget;
    private bool _lastReplayExecuted;
    private string? _lastReplayStatus;
    private bool _lastReplaySilent;
    private readonly LinkedList<EmoteEvent> _recentEmotes = new();

    public LinkedList<EmoteEvent> EmoteHistory { get; } = [];

    public EmoteService(IPluginLog logger, EmoteListener emoteListener, ChatListener chatListener, IChatGui chatGui,
        IEmoteLogMessageService emoteLogMessageService, ConfigurationService configService, ICondition condition,
        IPlayerState playerState, IDataManagerCacheService dataManagerCacheService, ITargetManager targetManager, IObjectTable objectTable,
        IGameConfig gameConfig, IEmoteChatRateLimitService emoteChatRateLimitService)
    {
        _logger = logger;
        _emoteListener = emoteListener;
        _chatListener = chatListener;
        _chatGui = chatGui;
        _emoteLogMessageService = emoteLogMessageService;
        _configService = configService;
        _condition = condition;
        _playerState = playerState;
        _gameConfig = gameConfig;
        _targetManager = targetManager;
        _objectTable = objectTable;
        _dataManagerCacheService = dataManagerCacheService;
        _emoteChatRateLimitService = emoteChatRateLimitService;

        _emoteListener.Emote += OnEmote;
        _emoteListener.ClickEmoteLink += OnClickedEmoteLink;
        _chatListener.Message += OnChatMessage;
    }

    private void OnEmote(object? sender, EmoteEvent e)
    {
        var emoteDisplayName = _dataManagerCacheService.GetEmoteDisplayName(e.EmoteId);
        _logger.Debug("Emote: {EmoteName} (ID: {EmoteId}) from {InitiatorName} (ID: {InitiatorId}) to {TargetName} (ID: {TargetId})",
            emoteDisplayName, e.EmoteId,
            e.InitiatorName.ToString(), e.InitiatorId,
            e.TargetName?.ToString() ?? "Unknown", e.TargetId);
        if (!e.TargetSelf) return;
        if (ShouldTrackEmote(e))
        {
            AddEmoteToHistory(e);
            AddEmoteToRecent(e);
        }
        NotifyEmoteUsed(e);
    }

    private void AddEmoteToHistory(EmoteEvent e)
    {
        if (EmoteHistory.Count >= MaxEmoteHistory)
        {
            EmoteHistory.RemoveLast();
        }
        EmoteHistory.AddFirst(e);
    }

    public void ClearEmoteHistory() => EmoteHistory.Clear();

    public IReadOnlyList<EmoteEvent> GetRecentEmotes(TimeSpan window)
    {
        PruneRecentEmotes(DateTime.UtcNow, window);
        var emotes = new List<EmoteEvent>(_recentEmotes.Count);
        foreach (var emote in _recentEmotes)
        {
            emotes.Add(emote);
        }
        return emotes;
    }

    private void NotifyEmoteUsed(EmoteEvent e)
    {
        if (!_configService.Settings.Emote.EnableNotifications) return;
        if (e.InitiatorIsSelf && !_configService.Settings.Emote.NotifyOnSelf) return;
        if (!_configService.Settings.Emote.EnableNotificationInCombat && _condition[ConditionFlag.InCombat]) return;
        if (!_emoteChatRateLimitService.TryConsume(e.EmoteId)) return;
        var emoteDisplayName = _dataManagerCacheService.GetEmoteDisplayName(e.EmoteId);
        var builder = new SeStringBuilder();
        builder.AddUiForeground("[Oh Hey!] ", 537);
        builder.AddUiForegroundOff();
        builder.Add(new PlayerPayload(e.InitiatorName.ToString(), e.InitiatorWorldId));

        if (
            _configService.Settings.NotificationDisplay.ShowWorldNameInChatNotifications &&
            _playerState.HomeWorld.RowId != e.InitiatorWorldId &&
            _dataManagerCacheService.TryGetWorldName(e.InitiatorWorldId, out string? worldName) &&
            !string.IsNullOrEmpty(worldName)
        ) {
            builder.AddIcon(BitmapFontIcon.CrossWorld);
            builder.AddText(worldName);
        }

        builder.AddText(" used ");
        builder.AddUiForeground(emoteDisplayName, 1);
        builder.AddUiForegroundOff();
        builder.AddText(" on you!");
        if (CanShowReplayLink(e.EmoteId))
        {
            var replayLinkPayload = _emoteListener.AddTemporaryReplayLink(e);
            var silentReplayLinkPayload = _emoteListener.AddTemporaryReplayLink(e, silentReplay: true);
            builder.AddText(" ");
            builder.Add(replayLinkPayload);
            builder.AddUiForeground($"[{emoteDisplayName}]", 45);
            builder.AddUiForegroundOff();
            builder.Add(RawPayload.LinkTerminator);
            builder.AddText(" ");
            builder.Add(silentReplayLinkPayload);
            builder.AddUiForeground($"[Silent {emoteDisplayName}]", 45);
            builder.AddUiForegroundOff();
            builder.Add(RawPayload.LinkTerminator);
        }
        PrintChatMessage(_configService.Settings.Emote.NotificationChatType, builder.Build());

        if (!_configService.Settings.Emote.EnableSoundNotification) return;
        UIGlobals.PlayChatSoundEffect(_configService.Settings.Emote.SoundNotificationId);
    }

    public string GetEmoteDisplayName(ushort emoteId)
        => _dataManagerCacheService.GetEmoteDisplayName(emoteId);

    public unsafe bool CanUseEmote(ushort emoteId)
    {
        if (!AgentLobby.Instance()->IsLoggedIn)
        {
            return false;
        }

        return AgentEmote.Instance()->CanUseEmote(emoteId);
    }

    public void TryUseEmoteIfAvailable(ushort emoteId)
    {
        if (!CanUseEmote(emoteId))
        {
            return;
        }

        ReplayEmoteById(emoteId, null, false);
    }

    public void Dispose()
    {
        _emoteListener.Emote -= OnEmote;
        _emoteListener.ClickEmoteLink -= OnClickedEmoteLink;
        _chatListener.Message -= OnChatMessage;
    }

    private void OnChatMessage(
        XivChatType type,
        int timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled)
    {
        if (isHandled)
        {
            return;
        }

        if (type is not XivChatType.StandardEmote) {
            return;
        }

        if (!ShouldSuppressRenderedStandardEmoteChatLine())
        {
            return;
        }


        var senderText = sender.ToString();

        var playerName = _playerState.CharacterName;

        if (playerName.Length > 0 && senderText == playerName) // Ignore the current player
        {
            return;
        }

        var msgText = message.ToString();

        if (!msgText.Contains("you", StringComparison.OrdinalIgnoreCase)) // Fast ... you. check though it breaks german/french/japanese but oh well.
        {
            return;
        }

        var normalizedMessage = NormalizeForComparison(msgText);

        if (normalizedMessage.Length == 0)
        {
            return;
        }

        if (IsTargetedEmoteToYouFromSender(normalizedMessage, senderText, message))
        {
            isHandled = true;
            _logger.Debug("Suppressed rendered emote chat line by direct match: {Message}", message.ToString());
            return;
        }

        _logger.Debug(
            "Unmatched standard emote chat line. Sender: {Sender}; Message: {Message}; Normalized: {Normalized}; Payloads: {Payloads}",
            senderText,
            msgText,
            normalizedMessage,
            BuildPayloadDebugString(message));

    }

    private bool ShouldSuppressRenderedStandardEmoteChatLine()
    {
        if (!_configService.Settings.Emote.EnableNotifications)
        {
            return false;
        }

        if (!_configService.Settings.Emote.SuppressDuplicateTargetedChatLine)
        {
            return false;
        }

        if (!_configService.Settings.Emote.EnableNotificationInCombat && _condition[ConditionFlag.InCombat])
        {
            return false;
        }

        return true;
    }

    private bool IsTargetedEmoteToYouFromSender(string normalizedMessage, string senderText, SeString message)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return false;
        }

        const string namePlaceholder = "ohheynameplaceholder";
        var senderName = TryGetSenderNameFromPlayerPayload(message, out var payloadSenderName)
            ? payloadSenderName
            : senderText;
        var normalizedSenderName = NormalizeForComparison(TrimWrappingQuotes(senderName));
        if (normalizedSenderName.Length == 0)
        {
            return false;
        }

        var previews = _emoteLogMessageService.GetTargetedPayloadPreviewNameCache();
        foreach (var preview in previews.Values)
        {
            var template = preview.NameToYou.Replace("{Name}", namePlaceholder, StringComparison.Ordinal);
            var normalizedTemplate = NormalizeForComparison(template);
            if (normalizedTemplate.Length == 0)
            {
                continue;
            }

            var placeholderIndex = normalizedTemplate.IndexOf(namePlaceholder, StringComparison.Ordinal);
            if (placeholderIndex < 0)
            {
                continue;
            }

            var prefix = normalizedTemplate[..placeholderIndex];
            var suffix = normalizedTemplate[(placeholderIndex + namePlaceholder.Length)..];

            if (prefix.Length > 0 && !normalizedMessage.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (suffix.Length > 0 && !normalizedMessage.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var start = prefix.Length;
            var end = normalizedMessage.Length - suffix.Length;
            if (end <= start)
            {
                continue;
            }

            var nameSegment = normalizedMessage[start..end].Trim();
            if (nameSegment.Length == 0)
            {
                continue;
            }

            if (nameSegment.Contains(normalizedSenderName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSenderNameFromPlayerPayload(SeString message, out string senderName)
    {
        senderName = string.Empty;
        foreach (var payload in message.Payloads)
        {
            if (payload is not PlayerPayload playerPayload)
            {
                continue;
            }

            var candidate = playerPayload.PlayerName.ToString();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            senderName = candidate;
            return true;
        }

        return false;
    }

    private static string NormalizeForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = new char[value.Length];
        var length = 0;
        var previousWasWhitespace = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[length++] = char.ToLowerInvariant(c);
                previousWasWhitespace = false;
                continue;
            }

            if (!char.IsWhiteSpace(c) || previousWasWhitespace)
            {
                continue;
            }

            chars[length++] = ' ';
            previousWasWhitespace = true;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        return new string(chars, 0, length).Trim();
    }

    private static string TrimWrappingQuotes(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string BuildPayloadDebugString(SeString message)
    {
        if (message.Payloads.Count == 0)
        {
            return "<none>";
        }

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < message.Payloads.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            var payload = message.Payloads[i];
            builder
                .Append('#')
                .Append(i)
                .Append(' ')
                .Append(payload.GetType().Name)
                .Append(": ")
                .Append(payload.ToString());
        }

        return builder.ToString();
    }

    public ReplayTargetDebugSnapshot GetReplayTargetDebugSnapshot()
    {
        lock (_replayDebugLock)
        {
            return new ReplayTargetDebugSnapshot(
                LastReplayAtUtc: _lastReplayAtUtc,
                LastReplayEmoteId: _lastReplayEmoteId,
                LastReplayRequestedTargetId: _lastReplayRequestedTargetId,
                LastReplayResolvedTargetId: _lastReplayResolvedTargetId,
                LastReplayPreviousTargetId: _lastReplayPreviousTargetId,
                LastReplayChangedTargetForReplay: _lastReplayChangedTargetForReplay,
                LastReplayRestoredPreviousTarget: _lastReplayRestoredPreviousTarget,
                LastReplayClearedTarget: _lastReplayClearedTarget,
                LastReplayExecuted: _lastReplayExecuted,
                LastReplayStatus: _lastReplayStatus,
                LastReplaySilent: _lastReplaySilent,
                CurrentTargetId: _targetManager.Target?.GameObjectId);
        }
    }

    private bool ShouldTrackEmote(EmoteEvent e)
    {
        if (!e.TargetSelf) return false;
        if (!e.InitiatorIsSelf) return true;
        return _configService.Settings.Emote.ShowSelf;
    }

    private void AddEmoteToRecent(EmoteEvent e)
    {
        _recentEmotes.AddFirst(e);
        PruneRecentEmotes(DateTime.UtcNow, TimeSpan.FromSeconds(60));
    }

    private void PruneRecentEmotes(DateTime nowUtc, TimeSpan window)
    {
        var threshold = nowUtc - window;
        while (_recentEmotes.Last is not null &&
               _recentEmotes.Last.Value.Timestamp.ToUniversalTime() < threshold)
        {
            _recentEmotes.RemoveLast();
        }
    }

    private void PrintChatMessage(Dalamud.Game.Text.XivChatType chatType, SeString message)
    {
        if (chatType == Dalamud.Game.Text.XivChatType.None)
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

    private static bool RequiresChatSenderName(Dalamud.Game.Text.XivChatType chatType)
    {
        return chatType != Dalamud.Game.Text.XivChatType.Notice
               && chatType != Dalamud.Game.Text.XivChatType.Echo
               && chatType != Dalamud.Game.Text.XivChatType.Urgent
               && chatType != Dalamud.Game.Text.XivChatType.SystemMessage
               && chatType != Dalamud.Game.Text.XivChatType.Debug;
    }

    private void OnClickedEmoteLink(object? sender, LinkClickEvent e)
    {
        ReplayEmoteById((ushort)e.EmoteId, e.ContentId, e.SilentReplay);
    }

    private static unsafe bool CanShowReplayLink(ushort emoteId)
    {
        if (!AgentLobby.Instance()->IsLoggedIn)
        {
            return false;
        }

        return AgentEmote.Instance()->CanUseEmote(emoteId);
    }

    internal void ReplayEmote(EmoteEvent emote)
    {
        ReplayEmoteById(emote.EmoteId, null, false);
    }

    private unsafe void ReplayEmoteById(ushort emoteId, ulong? targetObjectId, bool silentReplay)
    {
        if (!AgentLobby.Instance()->IsLoggedIn) {
            return;
        }

        var lastReplayAtUtc = DateTime.UtcNow;
        var previousTargetId = _targetManager.Target?.GameObjectId;
        var changedTargetForReplay = false;
        var resolvedTargetId = default(ulong?);
        var restoredPreviousTarget = false;
        var clearedTarget = false;
        var replayExecuted = false;
        var status = "ok";
        var emoteTextTypeBeforeSilentReplay = false;
        var changedEmoteTextTypeForSilentReplay = false;

        if (targetObjectId.HasValue)
        {
            var target = _objectTable.SearchById(targetObjectId.Value);
            if (target is not null)
            {
                changedTargetForReplay = previousTargetId != target.GameObjectId;
                resolvedTargetId = target.GameObjectId;
                _targetManager.Target = target;
            }
            else
            {
                _logger.Warning("Could not resolve replay target object ID {TargetObjectId}; replaying emote without retarget.", targetObjectId.Value);
                status = "target-not-found";
            }
        }

        try {
            if (silentReplay)
            {
                var emoteTextType = _gameConfig.UiConfig.GetBool("EmoteTextType");
                emoteTextTypeBeforeSilentReplay = emoteTextType;
                if (emoteTextTypeBeforeSilentReplay)
                {
                    _gameConfig.UiConfig.Set("EmoteTextType", false);
                    changedEmoteTextTypeForSilentReplay = true;
                }
            }

            if (!AgentEmote.Instance()->CanUseEmote(emoteId)) {
                _logger.Warning("Cannot replay emote ID {EmoteId} because it is not available.", emoteId);
                _chatGui.Print(new SeStringBuilder()
                    .AddUiForeground("Cannot replay emote: ", 537)
                    .AddUiForegroundOff()
                    .AddText($"Emote ID {emoteId} is not available.")
                    .Build());
                status = "emote-not-available";
                return;
            }

            AgentEmote.Instance()->ExecuteEmote(emoteId, addToHistory: false);
            replayExecuted = true;
        } catch (Exception exception) {
            _logger.Error(exception, "Error replaying emote ID {EmoteId}.", emoteId);
            status = $"error:{exception.GetType().Name}";
        } finally {
            if (changedEmoteTextTypeForSilentReplay)
            {
                _gameConfig.UiConfig.Set("EmoteTextType", emoteTextTypeBeforeSilentReplay);
            }

            if (changedTargetForReplay)
            {
                if (previousTargetId.HasValue)
                {
                    var previousTarget = _objectTable.SearchById(previousTargetId.Value);
                    _targetManager.Target = previousTarget;
                    restoredPreviousTarget = previousTarget is not null;
                    if (previousTarget is null)
                    {
                        status = "previous-target-missing";
                    }
                }
                else
                {
                    _targetManager.Target = null;
                    clearedTarget = true;
                }
            }

            lock (_replayDebugLock)
            {
                _lastReplayAtUtc = lastReplayAtUtc;
                _lastReplayEmoteId = emoteId;
                _lastReplayRequestedTargetId = targetObjectId;
                _lastReplayResolvedTargetId = resolvedTargetId;
                _lastReplayPreviousTargetId = previousTargetId;
                _lastReplayChangedTargetForReplay = changedTargetForReplay;
                _lastReplayRestoredPreviousTarget = restoredPreviousTarget;
                _lastReplayClearedTarget = clearedTarget;
                _lastReplayExecuted = replayExecuted;
                _lastReplayStatus = status;
                _lastReplaySilent = silentReplay;
            }
        }
    }

}

public readonly record struct ReplayTargetDebugSnapshot(
    DateTime? LastReplayAtUtc,
    ushort? LastReplayEmoteId,
    ulong? LastReplayRequestedTargetId,
    ulong? LastReplayResolvedTargetId,
    ulong? LastReplayPreviousTargetId,
    bool LastReplayChangedTargetForReplay,
    bool LastReplayRestoredPreviousTarget,
    bool LastReplayClearedTarget,
    bool LastReplayExecuted,
    string? LastReplayStatus,
    bool LastReplaySilent,
    ulong? CurrentTargetId);
