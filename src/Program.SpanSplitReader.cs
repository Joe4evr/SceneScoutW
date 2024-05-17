using System;

namespace SceneScoutW;

sealed partial class Program
{
    private ref struct SpanSplitReader
    {
        private readonly char _splitter;
        private ReadOnlySpan<char> _span;

        public SpanSplitReader(ReadOnlySpan<char> span, char splitter)
        {
            _span = span;
            _splitter = splitter;
        }

        public readonly SpanSplitReader GetEnumerator() => this;

        public ReadOnlySpan<char> Current { readonly get; private set; }
        public bool MoveNext()
        {
            var span = _span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == _splitter)
                {
                    Current = span[..i];
                    _span = span[(i + 1)..];
                    return true;
                }
            }

            Current = [];
            return false;
        }
    }
}
