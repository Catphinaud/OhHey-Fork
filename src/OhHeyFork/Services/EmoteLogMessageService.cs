// Copyright (c) 2025 MeiHasCrashed
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dalamud.Game;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Text.Expressions;
using Lumina.Text.ReadOnly;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OhHeyFork.Services;

public sealed class EmoteLogMessageService : IEmoteLogMessageService
{
    private readonly IPluginLog _logger;
    private readonly IDataManager _dataManager;
    private readonly ISeStringEvaluator _seStringEvaluator;
    private readonly IPlayerState _playerState;

    private readonly object _gate = new();
    private Dictionary<uint, string>? _targetedEnglishCache;
    private Dictionary<uint, EmoteTargetedPayloadPreview>? _targetedNamePreviewCache;

    public EmoteLogMessageService(IPluginLog logger, IDataManager dataManager, ISeStringEvaluator seStringEvaluator, IPlayerState playerState)
    {
        _logger = logger;
        _dataManager = dataManager;
        _seStringEvaluator = seStringEvaluator;
        _playerState = playerState;
    }

    public IReadOnlyDictionary<uint, string> GetTargetedLogMessagesEnglish()
    {
        EnsureCachesBuilt();
        return _targetedEnglishCache!;
    }

    public bool TryGetTargetedLogMessageEnglish(uint emoteRowId, out string message)
    {
        EnsureCachesBuilt();
        return _targetedEnglishCache!.TryGetValue(emoteRowId, out message!);
    }

    public void InvalidateCache()
    {
        lock (_gate) {
            _targetedEnglishCache = null;
            _targetedNamePreviewCache = null;
        }
    }

    public IReadOnlyDictionary<uint, EmoteTargetedPayloadPreview> GetTargetedPayloadPreviewNameCache()
    {
        EnsureCachesBuilt();
        return _targetedNamePreviewCache!;
    }

    public bool TryGetTargetedPayloadPreviewNameCache(uint emoteRowId, out EmoteTargetedPayloadPreview preview)
    {
        EnsureCachesBuilt();
        return _targetedNamePreviewCache!.TryGetValue(emoteRowId, out preview);
    }

    public bool TryGetTargetedLogMessageRawPayload(uint emoteRowId, out string rawPayload)
    {
        rawPayload = string.Empty;

        var emote = _dataManager.GetExcelSheet<Emote>().GetRowOrDefault(emoteRowId);
        if (emote is null)
            return false;

        if (emote.Value.LogMessageTargeted.RowId == 0)
            return false;

        rawPayload = GetMacroString(emote.Value.LogMessageTargeted.Value.Text);
        return !string.IsNullOrWhiteSpace(rawPayload);
    }

    public bool TryRenderTargetedPayloadPreview(string rawPayload, string targetName, out EmoteTargetedPayloadPreview preview)
    {
        preview = default;
        if (string.IsNullOrWhiteSpace(rawPayload))
            return false;

        var normalizedName = string.IsNullOrWhiteSpace(targetName) ? "{Name}" : targetName.Trim();
        var engine = new TargetedPayloadRenderEngine(rawPayload.Trim());

        var selfToTarget = new TargetedPayloadContext(
            Gstr1: "you",
            Gstr2: "you",
            Gstr3: normalizedName,
            Gnum7: 0,
            Gnum8: 0,
            TargetName: normalizedName);

        var targetToSelf = new TargetedPayloadContext(
            Gstr1: "you",
            Gstr2: normalizedName,
            Gstr3: "you",
            Gnum7: 0,
            Gnum8: 0,
            TargetName: normalizedName);

        var youToName = engine.Render(selfToTarget);
        var nameToYou = engine.Render(targetToSelf);

        if (LooksIncomplete(youToName, normalizedName) || LooksIncomplete(nameToYou, normalizedName))
        {
            youToName = RenderSimpleTargetedFallback(rawPayload, normalizedName, selfToTarget);
            nameToYou = RenderSimpleTargetedFallback(rawPayload, normalizedName, targetToSelf);
        }

        preview = new EmoteTargetedPayloadPreview(
            RawPayload: rawPayload,
            YouToName: youToName,
            NameToYou: nameToYou);

        return true;
    }

    private void EnsureCachesBuilt()
    {
        lock (_gate) {
            if (_targetedEnglishCache is not null && _targetedNamePreviewCache is not null)
                return;

            var englishCache = new Dictionary<uint, string>();
            var namePreviewCache = new Dictionary<uint, EmoteTargetedPayloadPreview>();

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
                    englishCache.TryAdd(emoteRowId, text);

                    var rawPayload = GetMacroString(emote.LogMessageTargeted.Value.Text);
                    if (TryRenderTargetedPayloadPreview(rawPayload, "{Name}", out var preview))
                    {
                        namePreviewCache.TryAdd(emoteRowId, preview);
                    }
                } catch (Exception ex) {
                    _logger.Debug(ex,
                        "Failed to evaluate targeted LogMessage {LogMessageId} for Emote {EmoteRowId}.",
                        logMessageId,
                        emoteRowId);
                }
            }

            _logger.Debug("Built Emote targeted log message cache: {Count} entries.", englishCache.Count);
            _logger.Debug("Built Emote targeted preview cache ({Name}): {Count} entries.", namePreviewCache.Count);
            _targetedEnglishCache = englishCache;
            _targetedNamePreviewCache = namePreviewCache;
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

    private sealed class TargetedPayloadRenderEngine
    {
        private readonly string _payload;

        public TargetedPayloadRenderEngine(string payload)
        {
            _payload = payload;
        }

        public string Render(TargetedPayloadContext context)
        {
            var rendered = RenderText(_payload, context);
            return NormalizeText(rendered);
        }

        private static string RenderText(string text, TargetedPayloadContext context)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var output = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != '<')
                {
                    output.Append(text[i]);
                    i++;
                    continue;
                }

                if (!TryFindMatchingAngle(text, i, out var closeIndex))
                {
                    output.Append(text[i]);
                    i++;
                    continue;
                }

                var expressionBody = text[(i + 1)..closeIndex];
                output.Append(EvaluateExpression(expressionBody, context));
                i = closeIndex + 1;
            }

            return ReplaceKnownTokens(output.ToString(), context);
        }

        private static string EvaluateExpression(string expression, TargetedPayloadContext context)
        {
            var expr = expression.Trim();
            if (expr.Length == 0)
                return string.Empty;

            if (TryGetFunction(expr, out var name, out var argsText))
            {
                if (name.Equals("if", StringComparison.OrdinalIgnoreCase))
                {
                    var args = SplitTopLevelArguments(argsText);
                    if (args.Count < 3)
                        return expr;

                    return EvaluateCondition(args[0], context)
                        ? RenderText(args[1], context)
                        : RenderText(args[2], context);
                }

                if (name.Equals("head", StringComparison.OrdinalIgnoreCase))
                {
                    var args = SplitTopLevelArguments(argsText);
                    if (args.Count == 0)
                        return string.Empty;

                    var value = RenderText(args[0], context);
                    return CapitalizeFirstLetter(value);
                }

                if (name.Equals("ennoun", StringComparison.OrdinalIgnoreCase))
                {
                    return context.TargetName;
                }
            }

            return ReplaceKnownTokens(expr, context);
        }

        private static bool EvaluateCondition(string condition, TargetedPayloadContext context)
        {
            var cond = condition.Trim();
            if (cond.Length == 0)
                return false;

            if (cond.StartsWith("[", StringComparison.Ordinal) &&
                cond.EndsWith("]", StringComparison.Ordinal) &&
                cond.Length >= 2)
            {
                cond = cond[1..^1].Trim();
            }

            var equalsIndex = cond.IndexOf("==", StringComparison.Ordinal);
            if (equalsIndex >= 0)
            {
                var left = cond[..equalsIndex].Trim();
                var right = cond[(equalsIndex + 2)..].Trim();
                var leftValue = ReplaceKnownTokens(left, context);
                var rightValue = ReplaceKnownTokens(right, context);
                return string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
            }

            if (cond.Equals("gnum7", StringComparison.OrdinalIgnoreCase))
                return context.Gnum7 != 0;

            if (cond.Equals("gnum8", StringComparison.OrdinalIgnoreCase))
                return context.Gnum8 != 0;

            if (bool.TryParse(cond, out var boolValue))
                return boolValue;

            return !string.IsNullOrWhiteSpace(ReplaceKnownTokens(cond, context));
        }

        private static string ReplaceKnownTokens(string value, TargetedPayloadContext context)
        {
            return value
                .Replace("gstr1", context.Gstr1, StringComparison.OrdinalIgnoreCase)
                .Replace("gstr2", context.Gstr2, StringComparison.OrdinalIgnoreCase)
                .Replace("gstr3", context.Gstr3, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindMatchingAngle(string text, int openIndex, out int closeIndex)
        {
            closeIndex = -1;
            var depth = 0;
            for (var i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '<')
                    depth++;
                else if (text[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetFunction(string expression, out string name, out string args)
        {
            name = string.Empty;
            args = string.Empty;

            var openParen = expression.IndexOf('(');
            if (openParen <= 0 || !expression.EndsWith(")", StringComparison.Ordinal))
                return false;

            name = expression[..openParen].Trim();
            args = expression[(openParen + 1)..^1];
            return name.Length > 0;
        }

        private static List<string> SplitTopLevelArguments(string arguments)
        {
            var result = new List<string>();
            var start = 0;
            var parenDepth = 0;
            var angleDepth = 0;
            var bracketDepth = 0;

            for (var i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        parenDepth--;
                        break;
                    case '<':
                        angleDepth++;
                        break;
                    case '>':
                        angleDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth--;
                        break;
                    case ',':
                        if (parenDepth == 0 && angleDepth == 0 && bracketDepth == 0)
                        {
                            result.Add(arguments[start..i].Trim());
                            start = i + 1;
                        }
                        break;
                }
            }

            if (start <= arguments.Length)
            {
                result.Add(arguments[start..].Trim());
            }

            return result;
        }

        private static string CapitalizeFirstLetter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetter(chars[i]))
                    continue;

                chars[i] = char.ToUpperInvariant(chars[i]);
                break;
            }

            return new string(chars);
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var output = new StringBuilder(value.Length);
            var previousWasWhitespace = false;
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsWhiteSpace(current))
                {
                    if (!previousWasWhitespace)
                    {
                        output.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                previousWasWhitespace = false;
                output.Append(current);
            }

            var compact = output.ToString().Trim();
            compact = compact.Replace(" .", ".", StringComparison.Ordinal)
                .Replace(" ,", ",", StringComparison.Ordinal)
                .Replace(" !", "!", StringComparison.Ordinal)
                .Replace(" ?", "?", StringComparison.Ordinal)
                .Replace(" ;", ";", StringComparison.Ordinal)
                .Replace(" :", ":", StringComparison.Ordinal);
            return compact;
        }
    }

    private readonly record struct TargetedPayloadContext(
        string Gstr1,
        string Gstr2,
        string Gstr3,
        int Gnum7,
        int Gnum8,
        string TargetName);

    private static bool LooksIncomplete(string text, string targetName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = text.Trim();
        if (normalized.Length < 6)
            return true;

        var hasPronoun = normalized.Contains("you", StringComparison.OrdinalIgnoreCase);
        var hasTarget = normalized.Contains(targetName, StringComparison.OrdinalIgnoreCase);
        return !hasPronoun && !hasTarget;
    }

    private static string RenderSimpleTargetedFallback(string rawPayload, string normalizedName, TargetedPayloadContext context)
    {
        var fallback = rawPayload;

        // Pick the simple verb branch based on [gstr1==gstr2] if-patterns, for example pat/pats.
        fallback = Regex.Replace(
            fallback,
            @"<if\(\[gstr1==gstr2\],\s*([A-Za-z']+)\s*,\s*([A-Za-z']+)\s*\)>",
            match => string.Equals(context.Gstr1, context.Gstr2, StringComparison.OrdinalIgnoreCase)
                ? match.Groups[1].Value
                : match.Groups[2].Value,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Replace the frequent player-name expression with the provided target placeholder.
        fallback = Regex.Replace(
            fallback,
            @"<ennoun\([^>]*\)>",
            normalizedName,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Handle the two other if patterns in this targeted payload template.
        fallback = Regex.Replace(
            fallback,
            @"<if\(\[gstr1==gstr2\],\s*you\s*,\s*<if\(gnum7,\s*" + Regex.Escape(normalizedName) + @"\s*,\s*gstr2\)\s*\)\s*>",
            string.Equals(context.Gstr1, context.Gstr2, StringComparison.OrdinalIgnoreCase) ? "you" : context.Gstr2,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        fallback = Regex.Replace(
            fallback,
            @"<if\(\[gstr1==gstr3\],\s*<if\(gnum8,\s*" + Regex.Escape(normalizedName) + @"\s*,\s*you\)\s*,\s*<if\(gnum8,\s*" + Regex.Escape(normalizedName) + @"\s*,\s*gstr3\)\s*\)\s*>",
            string.Equals(context.Gstr1, context.Gstr3, StringComparison.OrdinalIgnoreCase) ? "you" : context.Gstr3,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        fallback = fallback.Replace("<head(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(")>", string.Empty, StringComparison.OrdinalIgnoreCase);
        fallback = fallback.Replace("gstr1", context.Gstr1, StringComparison.OrdinalIgnoreCase)
            .Replace("gstr2", context.Gstr2, StringComparison.OrdinalIgnoreCase)
            .Replace("gstr3", context.Gstr3, StringComparison.OrdinalIgnoreCase);

        var compact = Regex.Replace(fallback, @"<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        compact = Regex.Replace(compact, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        if (compact.Length == 0)
            return compact;

        compact = char.ToUpperInvariant(compact[0]) + compact[1..];
        compact = compact.Replace(" .", ".", StringComparison.Ordinal);
        return compact;
    }

    private static string GetMacroString(ReadOnlySeString seString)
    {
        // Docs note ToString() strips payloads, so prefer macro-string conversion.
        var toMacroStringMethod = seString.GetType().GetMethod(
            "ToMacroString",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (toMacroStringMethod is not null && toMacroStringMethod.ReturnType == typeof(string))
        {
            if (toMacroStringMethod.Invoke(seString, null) is string macro && !string.IsNullOrWhiteSpace(macro))
                return macro;
        }

        // Fallback: rebuild from payload spans to preserve expression text as much as possible.
        var sb = new StringBuilder();
        foreach (var payload in seString.AsSpan())
        {
            sb.Append(payload.ToString());
        }

        return sb.ToString();
    }
}
