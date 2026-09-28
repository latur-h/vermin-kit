namespace VerminKit;

sealed record NoteSpan(int Start, int Length, bool Closed, string Weapon, string[] Properties, string Trait);

sealed record NoteToken(int Start, int Length, string Part, string Query, NoteSpan Span);

static class NoteMarkup
{
    public static IReadOnlyList<NoteSpan> Find(string text)
    {
        var spans = new List<NoteSpan>();
        var index = 0;
        while (index < text.Length)
        {
            var open = text.IndexOf('[', index);
            if (open < 0)
                break;

            var close = text.IndexOfAny([']', '\n', '['], open + 1);
            var closed = close >= 0 && text[close] == ']';
            var end = close < 0 ? text.Length : close;
            var inner = text[(open + 1)..end];
            Split(inner, out var weapon, out var properties, out var trait);
            var length = (closed ? close + 1 : end) - open;
            if (length < 1)
                length = 1;
            spans.Add(new NoteSpan(open, length, closed, weapon.Trim(), properties, trait.Trim()));
            index = open + length;
        }

        return spans;
    }

    public static NoteSpan? SpanAt(string text, int index)
    {
        foreach (var span in Find(text))
        {
            if (index >= span.Start && index < span.Start + span.Length)
                return span;
        }

        return null;
    }

    public static bool TryVisibleName(string text, NoteSpan span, out int start, out int length)
    {
        start = span.Start + 1;
        var close = span.Start + span.Length - (span.Closed ? 1 : 0);
        if (close < start || start > text.Length)
        {
            length = 0;
            return false;
        }

        var colon = text.IndexOf(':', start, Math.Min(text.Length, close) - start);
        var nameEnd = colon < 0 ? close : colon;
        while (start < nameEnd && char.IsWhiteSpace(text[start]))
            start++;
        while (nameEnd > start && char.IsWhiteSpace(text[nameEnd - 1]))
            nameEnd--;
        length = nameEnd - start;
        return length > 0;
    }

    public static NoteToken? TokenAt(string text, int caret)
    {
        if (caret < 0 || caret > text.Length)
            return null;

        foreach (var span in Find(text))
        {
            var innerStart = span.Start + 1;
            var innerEnd = span.Start + span.Length - (span.Closed ? 1 : 0);
            if (caret < innerStart || caret > innerEnd)
                continue;

            var inner = text[innerStart..innerEnd];
            var colon = inner.IndexOf(':');
            var semi = colon < 0 ? -1 : inner.IndexOf(';', colon + 1);
            var caretInner = Math.Clamp(caret - innerStart, 0, inner.Length);

            if (colon < 0 || caretInner <= colon)
            {
                var tokenEnd = colon < 0 ? inner.Length : colon;
                return Trim(text, innerStart, innerStart + tokenEnd, "weapon", span);
            }

            if (semi < 0 || caretInner <= semi)
            {
                var propStart = colon + 1;
                var propEnd = semi < 0 ? inner.Length : semi;
                var local = Math.Clamp(caretInner, propStart, propEnd);
                var look = Math.Min(inner.Length - 1, local - 1);
                var comma = look >= propStart ? inner.LastIndexOf(',', look) : -1;
                var start = comma < propStart ? propStart : comma + 1;
                var from = Math.Max(start, local);
                var next = from >= inner.Length ? -1 : inner.IndexOf(',', from);
                var end = next < 0 || next > propEnd ? propEnd : next;
                return Trim(text, innerStart + start, innerStart + end, "property", span);
            }

            return Trim(text, innerStart + semi + 1, innerEnd, "trait", span);
        }

        return null;
    }

    static NoteToken Trim(string text, int rawStart, int rawEnd, string part, NoteSpan span)
    {
        var start = Math.Clamp(rawStart, 0, text.Length);
        var end = Math.Clamp(rawEnd, 0, text.Length);
        if (end < start)
            end = start;
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;

        return new NoteToken(start, end - start, part, text[start..end], span);
    }

    static void Split(string inner, out string weapon, out string[] properties, out string trait)
    {
        var colon = inner.IndexOf(':');
        weapon = colon < 0 ? inner : inner[..colon];
        var rest = colon < 0 ? "" : inner[(colon + 1)..];
        var semi = rest.IndexOf(';');
        var propText = semi < 0 ? rest : rest[..semi];
        trait = semi < 0 ? "" : rest[(semi + 1)..];
        properties = propText.Length == 0
            ? []
            : propText.Split(',').Select(static part => part.Trim()).Where(static part => part.Length > 0).ToArray();
    }
}
