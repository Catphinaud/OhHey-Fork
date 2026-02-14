// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using Lumina.Excel.Sheets;

namespace OhHey.Listeners;

public sealed class EmoteListener : IDisposable
{
    private readonly IPluginLog _logger;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IChatGui _chatGui;
    private readonly Dictionary<uint, Emote> _emoteLinkCache = new();

    public event EventHandler<EmoteEvent>? Emote;
    // replay emote
    public event EventHandler<LinkClickEvent>? ClickEmoteLink;

    private delegate void OnEmoteDelegate(ulong unknown, IntPtr initiatorAddress, ushort emoteId, ulong targetId,
        ulong unknown2);

    // Signature and delegate taken from https://github.com/MgAl2O4/PatMeDalamud/blob/main/plugin/EmoteReaderHooks.cs licensed under MIT license.
    // Thank you !
    [Signature("E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24", DetourName = nameof(OnEmoteHook))]
    private readonly Hook<OnEmoteDelegate>? _onEmoteHook = null!;

    public EmoteListener(IPluginLog logger, IGameInteropProvider interopProvider, IObjectTable objectTable, IDataManager dataManager, IChatGui chatGui)
    {
        _logger = logger;
        _objectTable = objectTable;
        _dataManager = dataManager;
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

        InstallChatLinkHandlers();
    }

    private void InstallChatLinkHandlers()
    {
        var emotes = _dataManager.GetExcelSheet<Emote>();
        uint index = 200;
        foreach (var emote in emotes) {
            uint commandIndex = index++;
            _emoteLinkCache[commandIndex] = emote;
            _chatGui.AddChatLinkHandler(commandIndex, HandleEmoteLink);
        }
    }

    private void HandleEmoteLink(uint commandIndex, SeString source)
    {
        if (!_emoteLinkCache.TryGetValue(commandIndex, out var emote)) {
            _logger.Warning("Received chat link with unknown command index {CommandIndex}. Ignoring.", commandIndex);
            return;
        }

        try {
            // Replay
            // We need to handle where the link might be outdated, so we need to be able to handle that gracefully.
            // Each emote needs like 3 circular commandIndex so we can tell who sent it as we can't add too many Link Handlers and they need to be registered pre linking.
            // And then removed after some time to prevent memory leak, but we can just keep a cache of them and clear it every now and then.
            // So maybe we can remove them after like 5 minutes or something, but we can just keep a cache of them and clear it every now and then.
        } catch (Exception ex) {
            _logger.Error(ex, "Error invoking ReplayEmoteTargeted event handlers.");
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

        var maybeEmote = _dataManager.GetExcelSheet<Emote>().GetRowOrDefault(emoteId);
        if (maybeEmote is null)
        {
            _logger.Warning("Failed to resolve emote ID {EmoteId}. Skipping event trigger.", emoteId);
            return;
        }

        var emote = maybeEmote.Value;

        var targetSelf = targetId == localPlayer.GameObjectId;
        var initiatorIsSelf = initiator.GameObjectId == localPlayer.GameObjectId;

        var emoteEvent = new EmoteEvent(
            EmoteName: emote.Name,
            EmoteId: emoteId,
            EmoteIconId: emote.Icon,
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
        _chatGui.RemoveChatLinkHandler();
        _onEmoteHook?.Dispose();
    }
}
