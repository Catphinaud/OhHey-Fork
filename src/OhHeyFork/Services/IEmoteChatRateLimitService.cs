// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OhHeyFork.Services;

public interface IEmoteChatRateLimitService
{
    bool TryConsume(ushort emoteId);
    EmoteChatRateLimitStatus GetStatus();
    void ResetCounters();
}

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
