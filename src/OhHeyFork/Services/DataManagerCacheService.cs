// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace OhHeyFork.Services;

public sealed class DataManagerCacheService : IDataManagerCacheService
{
    private readonly Dictionary<uint, string> _worldNamesById;
    private readonly Dictionary<ushort, EmoteCacheEntry> _emotesById;
    private readonly Dictionary<ushort, string> _emoteDisplayNamesById;

    public DataManagerCacheService(IDataManager dataManager)
    {
        _worldNamesById = dataManager
            .GetExcelSheet<World>()
            .ToDictionary(world => world.RowId, world => world.Name.ToString());

        _emotesById = dataManager
            .GetExcelSheet<Emote>()
            .Where(emote => emote.RowId <= ushort.MaxValue)
            .ToDictionary(
                emote => (ushort)emote.RowId,
                emote => new EmoteCacheEntry(
                    IconId: emote.Icon,
                    DisplayName: BuildDisplayName((ushort)emote.RowId, emote.Name.ToString())));

        _emoteDisplayNamesById = _emotesById.ToDictionary(entry => entry.Key, entry => entry.Value.DisplayName);
    }

    public bool TryGetWorldName(uint worldId, out string worldName)
        => _worldNamesById.TryGetValue(worldId, out worldName!);

    public bool TryGetEmoteIconId(ushort emoteId, out uint iconId)
    {
        if (_emotesById.TryGetValue(emoteId, out var entry))
        {
            iconId = entry.IconId;
            return true;
        }

        iconId = default;
        return false;
    }

    public string GetEmoteDisplayName(ushort emoteId)
    {
        if (_emoteDisplayNamesById.TryGetValue(emoteId, out var displayName))
        {
            return displayName;
        }

        // Cache unknown ids so fallback names are allocated only once per id.
        displayName = BuildFallbackEmoteName(emoteId);
        _emoteDisplayNamesById[emoteId] = displayName;
        return displayName;
    }

    private static string BuildDisplayName(ushort emoteId, string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return BuildFallbackEmoteName(emoteId);
        }

        var cleaned = new string(rawName.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length == 0
            ? BuildFallbackEmoteName(emoteId)
            : cleaned;
    }

    private static string BuildFallbackEmoteName(ushort emoteId)
        => emoteId switch
        {
            96 => "NPC Sit",
            100 => "NPC Sleep",
            _ => $"Emote#{emoteId}"
        };

    private readonly record struct EmoteCacheEntry(uint IconId, string DisplayName);
}
