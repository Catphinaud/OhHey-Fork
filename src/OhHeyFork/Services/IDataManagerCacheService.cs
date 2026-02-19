// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OhHeyFork.Services;

public interface IDataManagerCacheService
{
    bool TryGetWorldName(uint worldId, out string worldName);
    bool TryGetEmoteIconId(ushort emoteId, out uint iconId);
    string GetEmoteDisplayName(ushort emoteId);
    IReadOnlyList<CachedEmoteInfo> GetAllEmotes();
}

public readonly record struct CachedEmoteInfo(
    ushort EmoteId,
    uint IconId,
    string DisplayName);
