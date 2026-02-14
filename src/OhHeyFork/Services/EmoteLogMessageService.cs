// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Text.Expressions;
using Lumina.Text.ReadOnly;

namespace OhHeyFork.Services;

public sealed class EmoteLogMessageService : IEmoteLogMessageService
{
    private readonly IPluginLog _logger;
    private readonly IDataManager _dataManager;
    private readonly ISeStringEvaluator _seStringEvaluator;
    private readonly IPlayerState _playerState;

    private readonly object _gate = new();
    private Dictionary<uint, string>? _targetedEnglishCache;

    public EmoteLogMessageService(IPluginLog logger, IDataManager dataManager, ISeStringEvaluator seStringEvaluator, IPlayerState playerState)
    {
        _logger = logger;
        _dataManager = dataManager;
        _seStringEvaluator = seStringEvaluator;
        _playerState = playerState;
    }

    public IReadOnlyDictionary<uint, string> GetTargetedLogMessagesEnglish()
    {
        EnsureCacheBuilt();
        return _targetedEnglishCache!;
    }

    public bool TryGetTargetedLogMessageEnglish(uint emoteRowId, out string message)
    {
        EnsureCacheBuilt();
        return _targetedEnglishCache!.TryGetValue(emoteRowId, out message!);
    }

    public void InvalidateCache()
    {
        lock (_gate) {
            _targetedEnglishCache = null;
        }
    }

    private void EnsureCacheBuilt()
    {
        lock (_gate) {
            if (_targetedEnglishCache is not null)
                return;

            var cache = new Dictionary<uint, string>();

            var sheet = _dataManager.GetExcelSheet<Emote>();
            foreach (var emote in sheet) {
                var emoteRowId = emote.RowId;
                var logMessageId = emote.LogMessageTargeted.RowId;

                if (logMessageId == 0)
                    continue;

                try {
                    var objectIdStr = Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
                    var o = _seStringEvaluator.EvaluateObjStr(objectIdStr, 1, ClientLanguage.English);
                    _logger.Debug("Evaluating Emote {EmoteRowId} LogMessage {LogMessageId} with ObjStr: {ObjStr}",
                        emoteRowId,
                        logMessageId,
                        o);
                    uint i = 0;
                    Dictionary<uint, SeStringParameter> parameters = new()
                    {
                        [i++] = new SeStringParameter(3), // gnum3
                        [i++] = new SeStringParameter(3),
                        [i++] = new SeStringParameter(3),
                        [i++] = new SeStringParameter(3),
                        [i++] = new SeStringParameter(3),
                        [i++] = new SeStringParameter(3),
                        [i++] = new SeStringParameter(3), // gnum6
                        [i++] = new SeStringParameter(3),
                        [8] = new SeStringParameter(3), // gnum8
                        [9] = new SeStringParameter(3), // gnum8
                        [10] = new SeStringParameter(3), // gnum8
                    };

                    // var other = GetLocalParameters(emote.LogMessageTargeted.Value.Text.AsSpan(), parameters);
                    var evaluated = _seStringEvaluator.EvaluateFromLogMessage(
                        logMessageId,
                        localParameters: parameters.Values.ToArray(),
                        language: ClientLanguage.English);

                    var text = evaluated.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    // Keep first entry if duplicates somehow occur.
                    cache.TryAdd(emoteRowId, text);
                } catch (Exception ex) {
                    _logger.Debug(ex,
                        "Failed to evaluate targeted LogMessage {LogMessageId} for Emote {EmoteRowId}.",
                        logMessageId,
                        emoteRowId);
                }
            }

            _logger.Debug("Built Emote targeted log message cache: {Count} entries.", cache.Count);
            _targetedEnglishCache = cache;
        }
    }


    private SeStringParameter[] GetLocalParameters(ReadOnlySeStringSpan rosss, Dictionary<uint, SeStringParameter>? parameters)
    {
        parameters ??= [];

        static bool TryExtractGnumIndex(string expressionText, out uint index)
        {
            // We see strings like: "<if(gnum8,<ennoun(ObjStr,2,gnum8,1,1)>,you)>"
            // When gnumN is used as the condition, we want it to evaluate truthy so the ",you" branch is picked.
            // This is best-effort string parsing (Lumina expression spans don't currently expose a typed gnum node).
            index = 0;

            // Fast path: locate "gnum" and parse consecutive digits.
            var pos = expressionText.IndexOf("gnum", StringComparison.Ordinal);
            if (pos < 0)
                return false;

            pos += 4;
            if (pos >= expressionText.Length || !char.IsDigit(expressionText[pos]))
                return false;

            uint value = 0;
            while (pos < expressionText.Length && char.IsDigit(expressionText[pos]))
            {
                value = checked(value * 10 + (uint)(expressionText[pos] - '0'));
                pos++;
            }

            index = value;
            return index > 0;
        }

        void ProcessString(ReadOnlySeStringSpan rosss2)
        {
            foreach (var payload in rosss2)
            {
                foreach (var expression in payload)
                {
                    ProcessExpression(expression);
                }
            }
        }

        void ProcessExpression(ReadOnlySeExpressionSpan expression)
        {
            _logger.Debug("Processing expression: {Expression}", expression.ToString());

            // Special case: expressions like <if(gnum8, ..., you)>.
            // Ensure the referenced local number (8) is set to 1 so the condition is true.
            // This makes evaluated log messages more readable ("you" instead of third-person name constructs).
            var exprText = expression.ToString();
            if (exprText.Contains(",you)", StringComparison.Ordinal) &&
                TryExtractGnumIndex(exprText, out var gnumIndex) &&
                !parameters.ContainsKey(gnumIndex))
            {
                parameters[gnumIndex] = new SeStringParameter(new ReadOnlySeString("1"));
            }

            if (expression.TryGetString(out ReadOnlySeStringSpan exprString))
            {
                ProcessString(exprString);
                return;
            }

            if (expression.TryGetBinaryExpression(out byte _, out ReadOnlySeExpressionSpan operand1, out ReadOnlySeExpressionSpan operand2))
            {
                ProcessExpression(operand1);
                ProcessExpression(operand2);
                return;
            }

            if (expression.TryGetParameterExpression(out var expressionType, out ReadOnlySeExpressionSpan operand))
            {
                if (!operand.TryGetUInt(out uint index))
                    return;

                if (parameters.ContainsKey(index))
                    return;

                if (expressionType == (int)ExpressionType.LocalNumber)
                {
                    parameters[index] = new SeStringParameter(0);
                    return;
                }

                if (expressionType == (int)ExpressionType.LocalString)
                {
                    parameters[index] = new SeStringParameter("");
                    return;
                }

                // For boolean-ish comparisons, seed a value that tends to produce deterministic output.
                if (expressionType == (int)ExpressionType.Equal)
                {
                    parameters[index] = new SeStringParameter(1);
                }
                else if (expressionType == (int)ExpressionType.NotEqual)
                {
                    parameters[index] = new SeStringParameter(0);
                }
                else if (expressionType == (int)ExpressionType.GreaterThan)
                {
                    parameters[index] = new SeStringParameter(0);
                }
                else if (expressionType == (int)ExpressionType.LessThan)
                {
                    parameters[index] = new SeStringParameter(1);
                }
                else if (expressionType == (int)ExpressionType.LessThanOrEqualTo)
                {
                    parameters[index] = new SeStringParameter(1);
                }
                else if (expressionType == (int)ExpressionType.GreaterThanOrEqualTo)
                {
                    parameters[index] = new SeStringParameter(1);
                }
            }
        }

        ProcessString(rosss);

        if (parameters.Count > 0)
        {
            var last = parameters.OrderBy(x => x.Key).Last();

            if (parameters.Count != last.Key)
            {
                // Fill missing local parameter slots, so we can go off the array index in SeStringContext.
                for (var i = 1u; i <= last.Key; i++)
                {
                    if (!parameters.ContainsKey(i))
                        parameters[i] = new SeStringParameter(0);
                }
            }
        }

        return parameters.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
    }
}
