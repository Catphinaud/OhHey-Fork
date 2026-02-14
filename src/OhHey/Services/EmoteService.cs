// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using OhHey;
using OhHey.Listeners;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace OhHey.Services;

public sealed class EmoteService : IDisposable
{
    private const string ChatSenderName = "OhHey";
    public const int MaxEmoteHistory = 10;
    private const int MinRateLimitWindowSeconds = 1;
    private const int MaxRateLimitWindowSeconds = 3600;
    private const int MinRateLimitMaxCount = 1;
    private const int MaxRateLimitMaxCount = 1000;

    private readonly IPluginLog _logger;
    private readonly EmoteListener _emoteListener;
    private readonly IChatGui _chatGui;
    private readonly ConfigurationService _configService;
    private readonly ICondition _condition;
    private readonly IPlayerState _playerState;
    private readonly Dictionary<ushort, Queue<DateTime>> _emoteChatNotificationTimes = new();
    private readonly Dictionary<ushort, FixedWindowState> _emoteChatFixedWindowState = new();
    private int _emoteChatNotificationsSuppressed;
    private ushort? _lastEmoteId;
    private string? _lastEmoteName;
    private readonly LinkedList<EmoteEvent> _recentEmotes = new();
    private readonly Dictionary<uint, string> _worlds;

    public LinkedList<EmoteEvent> EmoteHistory { get; } = [];

    public EmoteService(IPluginLog logger, EmoteListener emoteListener, IChatGui chatGui,
        ConfigurationService configService, ICondition condition, IPlayerState playerState, IDataManager dataManager)
    {
        _logger = logger;
        _emoteListener = emoteListener;
        _chatGui = chatGui;
        _configService = configService;
        _condition = condition;
        _playerState = playerState;
        _worlds = dataManager
            .GetExcelSheet<Lumina.Excel.Sheets.World>()
            .ToDictionary(world => world.RowId, world => world.Name.ToString());

        _emoteListener.Emote += OnEmote;
        _emoteListener.ClickEmoteLink += OnClickedEmoteLink;
    }

    private void OnEmote(object? sender, EmoteEvent e)
    {
        _logger.Debug("Emote: {EmoteName} (ID: {EmoteId}) from {InitiatorName} (ID: {InitiatorId}) to {TargetName} (ID: {TargetId})",
            e.EmoteName.ToString(), e.EmoteId,
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
        if (!_configService.Configuration.EnableEmoteNotifications) return;
        if (e.InitiatorIsSelf && !_configService.Configuration.NotifyOnSelfEmote) return;
        if (!_configService.Configuration.EnableEmoteNotificationInCombat && _condition[ConditionFlag.InCombat]) return;
        if (!TryConsumeEmoteChatRateLimitSlot(e)) return;
        var builder = new SeStringBuilder();
        builder.AddUiForeground("[Oh Hey!] ", 537);
        builder.AddUiForegroundOff();
        builder.Add(new PlayerPayload(e.InitiatorName.ToString(), e.InitiatorWorldId));

        if (
            _configService.Configuration.ShowWorldNameInChatNotifications &&
            _playerState.HomeWorld.RowId != e.InitiatorWorldId &&
            _worlds.TryGetValue(e.InitiatorWorldId, out string? worldName) &&
            !string.IsNullOrEmpty(worldName)
        ) {
            builder.AddIcon(BitmapFontIcon.CrossWorld);
            builder.AddText(worldName);
        }

        builder.AddText(" used ");
        builder.AddUiForeground(e.EmoteName.ToString(), 1);
        builder.AddUiForegroundOff();
        builder.AddText(" on you!");
        PrintChatMessage(_configService.Configuration.EmoteNotificationChatType, builder.Build());

        if (!_configService.Configuration.EnableEmoteSoundNotification) return;
        UIGlobals.PlayChatSoundEffect(_configService.Configuration.EmoteSoundNotificationId);
    }

    public EmoteChatRateLimitStatus GetEmoteChatRateLimitStatus()
    {
        var configuration = _configService.Configuration;
        var enabled = configuration.EnableEmoteChatNotificationRateLimit;
        var windowSeconds = ClampRateLimitWindowSeconds(configuration.EmoteChatNotificationRateLimitWindowSeconds);
        var maxCount = ClampRateLimitMaxCount(configuration.EmoteChatNotificationRateLimitMaxCount);
        var mode = configuration.EmoteChatNotificationRateLimitMode;
        var now = DateTime.UtcNow;
        int currentCount = 0;
        DateTime? nextAllowedUtc = null;
        int trackedEmoteCount = 0;
        if (_lastEmoteId.HasValue)
        {
            if (mode == EmoteChatNotificationRateLimitMode.RollingWindow &&
                _emoteChatNotificationTimes.TryGetValue(_lastEmoteId.Value, out var times))
            {
                PruneEmoteChatNotificationTimes(times, now, windowSeconds);
                currentCount = times.Count;
                trackedEmoteCount = _emoteChatNotificationTimes.Count;
                if (enabled && currentCount >= maxCount && times.Count > 0)
                {
                    nextAllowedUtc = times.Peek().AddSeconds(windowSeconds);
                }
            }
            else if (mode == EmoteChatNotificationRateLimitMode.FixedWindow &&
                     _emoteChatFixedWindowState.TryGetValue(_lastEmoteId.Value, out var state))
            {
                var refreshed = RefreshFixedWindow(state, now, windowSeconds);
                currentCount = refreshed.Count;
                trackedEmoteCount = _emoteChatFixedWindowState.Count;
                if (enabled && currentCount >= maxCount)
                {
                    nextAllowedUtc = refreshed.WindowStartUtc.AddSeconds(windowSeconds);
                }
            }
        }

        return new EmoteChatRateLimitStatus(
            enabled,
            windowSeconds,
            maxCount,
            currentCount,
            _emoteChatNotificationsSuppressed,
            nextAllowedUtc,
            trackedEmoteCount,
            _lastEmoteName,
            mode);
    }

    public void ResetEmoteChatRateLimitCounters()
    {
        _emoteChatNotificationTimes.Clear();
        _emoteChatFixedWindowState.Clear();
        _emoteChatNotificationsSuppressed = 0;
        _lastEmoteId = null;
        _lastEmoteName = null;
    }

    public void Dispose()
    {
        _emoteListener.Emote -= OnEmote;
        _emoteListener.ClickEmoteLink -= OnClickedEmoteLink;
    }

    private bool ShouldTrackEmote(EmoteEvent e)
    {
        if (!e.TargetSelf) return false;
        if (!e.InitiatorIsSelf) return true;
        return _configService.Configuration.ShowSelfEmote;
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

    private bool TryConsumeEmoteChatRateLimitSlot(EmoteEvent e)
    {
        var configuration = _configService.Configuration;
        if (!configuration.EnableEmoteChatNotificationRateLimit) return true;

        var windowSeconds = ClampRateLimitWindowSeconds(configuration.EmoteChatNotificationRateLimitWindowSeconds);
        var maxCount = ClampRateLimitMaxCount(configuration.EmoteChatNotificationRateLimitMaxCount);
        var mode = configuration.EmoteChatNotificationRateLimitMode;
        var now = DateTime.UtcNow;
        var emoteId = e.EmoteId;
        _lastEmoteId = emoteId;
        _lastEmoteName = e.EmoteName.ToString();

        if (mode == EmoteChatNotificationRateLimitMode.FixedWindow)
        {
            if (!_emoteChatFixedWindowState.TryGetValue(emoteId, out var state))
            {
                state = new FixedWindowState(now, 0);
            }

            state = RefreshFixedWindow(state, now, windowSeconds);
            if (state.Count >= maxCount)
            {
                _emoteChatNotificationsSuppressed++;
                _emoteChatFixedWindowState[emoteId] = state;
                return false;
            }

            state = state with { Count = state.Count + 1 };
            _emoteChatFixedWindowState[emoteId] = state;
            return true;
        }

        if (!_emoteChatNotificationTimes.TryGetValue(emoteId, out var times))
        {
            times = new Queue<DateTime>();
            _emoteChatNotificationTimes[emoteId] = times;
        }

        PruneEmoteChatNotificationTimes(times, now, windowSeconds);
        if (times.Count >= maxCount)
        {
            _emoteChatNotificationsSuppressed++;
            return false;
        }

        times.Enqueue(now);
        return true;
    }

    private void PruneEmoteChatNotificationTimes(Queue<DateTime> times, DateTime nowUtc, int windowSeconds)
    {
        var threshold = nowUtc.AddSeconds(-windowSeconds);
        while (times.Count > 0 && times.Peek() < threshold)
        {
            times.Dequeue();
        }
    }

    private static int ClampRateLimitWindowSeconds(int value)
        => Math.Clamp(value, MinRateLimitWindowSeconds, MaxRateLimitWindowSeconds);

    private static int ClampRateLimitMaxCount(int value)
        => Math.Clamp(value, MinRateLimitMaxCount, MaxRateLimitMaxCount);

    private static FixedWindowState RefreshFixedWindow(FixedWindowState state, DateTime nowUtc, int windowSeconds)
    {
        if (nowUtc - state.WindowStartUtc >= TimeSpan.FromSeconds(windowSeconds))
        {
            return new FixedWindowState(nowUtc, 0);
        }

        return state;
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

    }

    internal unsafe void ReplayEmote(EmoteEvent emote)
    {
        if (!AgentLobby.Instance()->IsLoggedIn) {
            return;
        }

        if (!AgentEmote.Instance()->CanUseEmote(emote.EmoteId)) {
            _logger.Warning("Cannot replay emote ID {EmoteId} because it is not available.", emote.EmoteId);
            return;
        }

        try {
            AgentEmote.Instance()->ExecuteEmote(emote.EmoteId, addToHistory: false);
        } catch (Exception exception) {
            _logger.Error(exception, "Error replaying emote ID {EmoteId}.", emote.EmoteId);
        }
    }
}

public readonly record struct FixedWindowState(DateTime WindowStartUtc, int Count);

public readonly record struct EmoteChatRateLimitStatus(
    bool Enabled,
    int WindowSeconds,
    int MaxCount,
    int CurrentCount,
    int SuppressedCount,
    DateTime? NextAllowedUtc,
    int TrackedEmoteCount,
    string? LastEmoteName,
    EmoteChatNotificationRateLimitMode Mode);
