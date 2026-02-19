// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using OhHeyFork.Services;

namespace OhHeyFork.Listeners;

public sealed class EmoteListener : IDisposable
{
    private const int DefaultReplayLinkTtlMinutes = 5;
    private const int DefaultReplayLinkMaxEntries = 64;
    private const uint ReplayLinkCommandIndexStart = 20_000;

    private readonly IPluginLog _logger;
    private readonly IObjectTable _objectTable;
    private readonly IDataManagerCacheService _dataManagerCacheService;
    private readonly IChatGui _chatGui;
    private readonly Dictionary<uint, TemporaryReplayLink> _temporaryReplayLinks = new();
    private readonly Queue<uint> _reusableReplayLinkIds = new();
    private readonly object _replayLinkLock = new();
    private uint _nextReplayLinkId = ReplayLinkCommandIndexStart;
    private long _replayLinksCreated;
    private long _replayLinksRemoved;
    private long _replayLinksRemovedExpired;
    private long _replayLinksRemovedEvicted;
    private long _replayLinksClicked;

    public event EventHandler<EmoteEvent>? Emote;
    // replay emote
    public event EventHandler<LinkClickEvent>? ClickEmoteLink;

    private delegate void OnEmoteDelegate(ulong unknown, IntPtr initiatorAddress, ushort emoteId, ulong targetId,
        ulong unknown2);

    // Signature and delegate taken from https://github.com/MgAl2O4/PatMeDalamud/blob/main/plugin/EmoteReaderHooks.cs licensed under MIT license.
    // Thank you !
    [Signature("E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24", DetourName = nameof(OnEmoteHook))]
    private readonly Hook<OnEmoteDelegate>? _onEmoteHook = null!;

    public EmoteListener(IPluginLog logger, IGameInteropProvider interopProvider, IObjectTable objectTable, IDataManagerCacheService dataManagerCacheService, IChatGui chatGui)
    {
        _logger = logger;
        _objectTable = objectTable;
        _dataManagerCacheService = dataManagerCacheService;
        _chatGui = chatGui;

        interopProvider.InitializeFromAttributes(this);
        if (_onEmoteHook is null)
        {
            _logger.Error("Failed to initialize emote hook. Any emote related features will not work.");
            _logger.Error("Please report this to Mei. Thank you <3");
            return;
        }
        _onEmoteHook.Enable();
        _logger.Debug("Emote hook initialized.");

    }

    public DalamudLinkPayload AddTemporaryReplayLink(EmoteEvent emoteEvent, bool silentReplay = false, TimeSpan? ttl = null)
    {
        var now = DateTime.UtcNow;
        var effectiveTtl = ttl ?? TimeSpan.FromMinutes(DefaultReplayLinkTtlMinutes);
        lock (_replayLinkLock)
        {
            PruneTemporaryReplayLinks(now);

            while (_temporaryReplayLinks.Count >= DefaultReplayLinkMaxEntries)
            {
                var oldest = _temporaryReplayLinks
                    .OrderBy(entry => entry.Value.CreatedUtc)
                    .First();
                RemoveTemporaryReplayLink(oldest.Key, ReplayLinkRemoveReason.Evicted);
            }

            var commandIndex = _reusableReplayLinkIds.Count > 0
                ? _reusableReplayLinkIds.Dequeue()
                : _nextReplayLinkId++;

            _temporaryReplayLinks[commandIndex] = new TemporaryReplayLink(
                emoteEvent.InitiatorId,
                emoteEvent.EmoteId,
                silentReplay,
                now,
                effectiveTtl);
            _replayLinksCreated++;
            return _chatGui.AddChatLinkHandler(commandIndex, HandleReplayLinkClick);
        }
    }

    private void HandleReplayLinkClick(uint commandIndex, SeString source)
    {
        TemporaryReplayLink replayLink;
        lock (_replayLinkLock)
        {
            if (!_temporaryReplayLinks.TryGetValue(commandIndex, out replayLink))
            {
                _logger.Warning("Received chat link with unknown command index {CommandIndex}. Ignoring.", commandIndex);
                return;
            }

            var now = DateTime.UtcNow;
            if (replayLink.IsExpired(now))
            {
                RemoveTemporaryReplayLink(commandIndex, ReplayLinkRemoveReason.Expired);
                return;
            }

            _replayLinksClicked++;
        }

        try
        {
            var handler = ClickEmoteLink;
            handler?.Invoke(this, new LinkClickEvent(
                replayLink.InitiatorId,
                DateTime.UtcNow - replayLink.CreatedUtc,
                replayLink.EmoteId,
                replayLink.SilentReplay));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error invoking ReplayEmoteTargeted event handlers.");
        }
    }

    private void PruneTemporaryReplayLinks(DateTime nowUtc)
    {
        if (_temporaryReplayLinks.Count == 0)
        {
            return;
        }

        var expiredIds = _temporaryReplayLinks
            .Where(entry => entry.Value.IsExpired(nowUtc))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var commandIndex in expiredIds)
        {
            RemoveTemporaryReplayLink(commandIndex, ReplayLinkRemoveReason.Expired);
        }
    }

    private void RemoveTemporaryReplayLink(uint commandIndex, ReplayLinkRemoveReason reason = ReplayLinkRemoveReason.Manual)
    {
        if (!_temporaryReplayLinks.Remove(commandIndex))
        {
            return;
        }

        _chatGui.RemoveChatLinkHandler(commandIndex);
        _replayLinksRemoved++;
        if (reason == ReplayLinkRemoveReason.Expired)
        {
            _replayLinksRemovedExpired++;
        }
        else if (reason == ReplayLinkRemoveReason.Evicted)
        {
            _replayLinksRemovedEvicted++;
        }
        _reusableReplayLinkIds.Enqueue(commandIndex);
    }

    public ReplayLinkDebugSnapshot GetReplayLinkDebugSnapshot()
    {
        lock (_replayLinkLock)
        {
            var now = DateTime.UtcNow;
            var entries = _temporaryReplayLinks
                .OrderByDescending(entry => entry.Value.CreatedUtc)
                .Select(entry => new ReplayLinkDebugEntry(
                    entry.Key,
                    entry.Value.InitiatorId,
                    entry.Value.EmoteId,
                    entry.Value.SilentReplay,
                    entry.Value.CreatedUtc,
                    entry.Value.Ttl,
                    entry.Value.Ttl - (now - entry.Value.CreatedUtc)))
                .ToArray();

            return new ReplayLinkDebugSnapshot(
                ActiveCount: _temporaryReplayLinks.Count,
                ReusableIdCount: _reusableReplayLinkIds.Count,
                NextCommandIndex: _nextReplayLinkId,
                MaxEntries: DefaultReplayLinkMaxEntries,
                DefaultTtl: TimeSpan.FromMinutes(DefaultReplayLinkTtlMinutes),
                CreatedCount: _replayLinksCreated,
                RemovedCount: _replayLinksRemoved,
                RemovedExpiredCount: _replayLinksRemovedExpired,
                RemovedEvictedCount: _replayLinksRemovedEvicted,
                ClickedCount: _replayLinksClicked,
                Entries: entries);
        }
    }


    private void OnEmoteHook(ulong unknown, IntPtr initiatorAddress, ushort emoteId, ulong targetId, ulong unknown2)
    {
        try
        {
            HandleEmoteEvent(initiatorAddress, emoteId, targetId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error executing emote hook.");
        }

        _onEmoteHook!.Original(unknown, initiatorAddress, emoteId, targetId, unknown2);
    }

    private void HandleEmoteEvent(IntPtr initiatorAddress, ushort emoteId, ulong targetId)
    {
        // We aren't logged in, somehow ??
        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return;
        }

        if (_objectTable.PlayerObjects.FirstOrDefault(chara => chara.Address == initiatorAddress) is not
            IPlayerCharacter initiator)
        {
            _logger.Warning("Failed to resolve initiator address {InitiatorAddress}. Skipping event trigger.", initiatorAddress);
            return;
        }

        var target = _objectTable.SearchById(targetId);

        if (!_dataManagerCacheService.TryGetEmoteIconId(emoteId, out var emoteIconId))
        {
            _logger.Warning("Failed to resolve emote metadata for emote ID {EmoteId}. Skipping event trigger.", emoteId);
            return;
        }

        var targetSelf = targetId == localPlayer.GameObjectId;
        var initiatorIsSelf = initiator.GameObjectId == localPlayer.GameObjectId;

        var emoteEvent = new EmoteEvent(
            EmoteId: emoteId,
            EmoteIconId: emoteIconId,
            InitiatorName: initiator.Name,
            InitiatorId: initiator.GameObjectId,
            InitiatorWorldId: initiator.HomeWorld.RowId,
            TargetName: target?.Name,
            TargetId: targetId,
            TargetSelf: targetSelf,
            InitiatorIsSelf: initiatorIsSelf,
            Timestamp: DateTime.Now
        );

        OnEmote(emoteEvent);
    }

    private void OnEmote(EmoteEvent emoteEvent)
    {
        var handler = Emote;
        try
        {
            handler?.Invoke(this, emoteEvent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error invoking Emote event handlers.");
        }
    }

    public void Dispose()
    {
        lock (_replayLinkLock)
        {
            foreach (var commandIndex in _temporaryReplayLinks.Keys.ToArray())
            {
                _chatGui.RemoveChatLinkHandler(commandIndex);
            }
            _temporaryReplayLinks.Clear();
            _reusableReplayLinkIds.Clear();
        }
        _chatGui.RemoveChatLinkHandler();
        _onEmoteHook?.Dispose();
    }

    private readonly record struct TemporaryReplayLink(
        ulong InitiatorId,
        uint EmoteId,
        bool SilentReplay,
        DateTime CreatedUtc,
        TimeSpan Ttl)
    {
        public bool IsExpired(DateTime nowUtc) => nowUtc - CreatedUtc > Ttl;
    }

    private enum ReplayLinkRemoveReason
    {
        Manual = 0,
        Expired = 1,
        Evicted = 2
    }
}

public readonly record struct ReplayLinkDebugSnapshot(
    int ActiveCount,
    int ReusableIdCount,
    uint NextCommandIndex,
    int MaxEntries,
    TimeSpan DefaultTtl,
    long CreatedCount,
    long RemovedCount,
    long RemovedExpiredCount,
    long RemovedEvictedCount,
    long ClickedCount,
    IReadOnlyList<ReplayLinkDebugEntry> Entries);

public readonly record struct ReplayLinkDebugEntry(
    uint CommandIndex,
    ulong InitiatorId,
    uint EmoteId,
    bool SilentReplay,
    DateTime CreatedUtc,
    TimeSpan Ttl,
    TimeSpan Remaining);
