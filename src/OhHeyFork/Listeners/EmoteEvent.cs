// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game.Text.SeStringHandling;

namespace OhHeyFork.Listeners;

public record EmoteEvent(
    ushort EmoteId,
    uint EmoteIconId,
    SeString InitiatorName,
    ulong InitiatorId,
    uint InitiatorWorldId,
    SeString? TargetName,
    ulong TargetId,
    bool TargetSelf,
    bool InitiatorIsSelf,
    DateTime Timestamp
);
