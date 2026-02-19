// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OhHeyFork.Services;

public sealed class EmoteChatRateLimitService : IEmoteChatRateLimitService
{
    private const int MinRateLimitWindowSeconds = 1;
    private const int MaxRateLimitWindowSeconds = 3600;
    private const int MinRateLimitMaxCount = 1;
    private const int MaxRateLimitMaxCount = 1000;

    private readonly ConfigurationService _configService;
    private readonly IDataManagerCacheService _dataManagerCacheService;
    private readonly Dictionary<ushort, Queue<DateTime>> _notificationTimes = new();
    private readonly Dictionary<ushort, FixedWindowState> _fixedWindowState = new();
    private int _suppressedCount;
    private ushort? _lastEmoteId;
    private string? _lastEmoteName;

    public EmoteChatRateLimitService(
        ConfigurationService configService,
        IDataManagerCacheService dataManagerCacheService)
    {
        _configService = configService;
        _dataManagerCacheService = dataManagerCacheService;
    }

    public bool TryConsume(ushort emoteId)
    {
        var configuration = _configService.Settings.Emote.ChatRateLimit;
        if (!configuration.Enabled) return true;

        var windowSeconds = ClampRateLimitWindowSeconds(configuration.WindowSeconds);
        var maxCount = ClampRateLimitMaxCount(configuration.MaxCount);
        var mode = configuration.Mode;
        var now = DateTime.UtcNow;
        _lastEmoteId = emoteId;
        _lastEmoteName = _dataManagerCacheService.GetEmoteDisplayName(emoteId);

        if (mode == EmoteChatNotificationRateLimitMode.FixedWindow)
        {
            if (!_fixedWindowState.TryGetValue(emoteId, out var state))
            {
                state = new FixedWindowState(now, 0);
            }

            state = RefreshFixedWindow(state, now, windowSeconds);
            if (state.Count >= maxCount)
            {
                _suppressedCount++;
                _fixedWindowState[emoteId] = state;
                return false;
            }

            state = state with { Count = state.Count + 1 };
            _fixedWindowState[emoteId] = state;
            return true;
        }

        if (!_notificationTimes.TryGetValue(emoteId, out var times))
        {
            times = new Queue<DateTime>();
            _notificationTimes[emoteId] = times;
        }

        PruneNotificationTimes(times, now, windowSeconds);
        if (times.Count >= maxCount)
        {
            _suppressedCount++;
            return false;
        }

        times.Enqueue(now);
        return true;
    }

    public EmoteChatRateLimitStatus GetStatus()
    {
        var configuration = _configService.Settings.Emote.ChatRateLimit;
        var enabled = configuration.Enabled;
        var windowSeconds = ClampRateLimitWindowSeconds(configuration.WindowSeconds);
        var maxCount = ClampRateLimitMaxCount(configuration.MaxCount);
        var mode = configuration.Mode;
        var now = DateTime.UtcNow;
        var currentCount = 0;
        DateTime? nextAllowedUtc = null;
        var trackedEmoteCount = 0;

        if (_lastEmoteId.HasValue)
        {
            if (mode == EmoteChatNotificationRateLimitMode.RollingWindow &&
                _notificationTimes.TryGetValue(_lastEmoteId.Value, out var times))
            {
                PruneNotificationTimes(times, now, windowSeconds);
                currentCount = times.Count;
                trackedEmoteCount = _notificationTimes.Count;
                if (enabled && currentCount >= maxCount && times.Count > 0)
                {
                    nextAllowedUtc = times.Peek().AddSeconds(windowSeconds);
                }
            }
            else if (mode == EmoteChatNotificationRateLimitMode.FixedWindow &&
                     _fixedWindowState.TryGetValue(_lastEmoteId.Value, out var state))
            {
                var refreshed = RefreshFixedWindow(state, now, windowSeconds);
                currentCount = refreshed.Count;
                trackedEmoteCount = _fixedWindowState.Count;
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
            _suppressedCount,
            nextAllowedUtc,
            trackedEmoteCount,
            _lastEmoteName,
            mode);
    }

    public void ResetCounters()
    {
        _notificationTimes.Clear();
        _fixedWindowState.Clear();
        _suppressedCount = 0;
        _lastEmoteId = null;
        _lastEmoteName = null;
    }

    private static void PruneNotificationTimes(Queue<DateTime> times, DateTime nowUtc, int windowSeconds)
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

    private readonly record struct FixedWindowState(DateTime WindowStartUtc, int Count);
}
