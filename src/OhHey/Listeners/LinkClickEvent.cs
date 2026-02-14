// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace OhHey.Listeners;

public record LinkClickEvent(
    // We need the content id, how long ago, and what emote
    ulong ContentId,
    TimeSpan TimeSinceEmote,
    uint EmoteId
);
