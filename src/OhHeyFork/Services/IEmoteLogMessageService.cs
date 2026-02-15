// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game;

namespace OhHeyFork.Services;

public interface IEmoteLogMessageService
{
    /// <summary>
    /// Builds (or returns) a cache mapping emote row id -> evaluated (English) targeted log message text.
    /// </summary>
    IReadOnlyDictionary<uint, string> GetTargetedLogMessagesEnglish();

    /// <summary>
    /// Tries to get the evaluated (English) targeted log message text for a specific emote.
    /// </summary>
    bool TryGetTargetedLogMessageEnglish(uint emoteRowId, out string message);

    /// <summary>
    /// Clears the internal cache so it will be rebuilt on next access.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Tries to get the raw targeted payload from Emote.LogMessageTargeted.Text for a specific emote row.
    /// </summary>
    bool TryGetTargetedLogMessageRawPayload(uint emoteRowId, out string rawPayload);

    /// <summary>
    /// Renders a raw targeted payload into both common perspectives:
    /// "You ... {Name}" and "{Name} ... you".
    /// </summary>
    bool TryRenderTargetedPayloadPreview(string rawPayload, string targetName, out EmoteTargetedPayloadPreview preview);

    /// <summary>
    /// Builds (or returns) a cache of targeted payload previews using "{Name}" as the placeholder.
    /// </summary>
    IReadOnlyDictionary<uint, EmoteTargetedPayloadPreview> GetTargetedPayloadPreviewNameCache();

    /// <summary>
    /// Tries to get a pre-rendered targeted payload preview for an emote row using "{Name}".
    /// </summary>
    bool TryGetTargetedPayloadPreviewNameCache(uint emoteRowId, out EmoteTargetedPayloadPreview preview);
}

public readonly record struct EmoteTargetedPayloadPreview(
    string RawPayload,
    string YouToName,
    string NameToYou);
