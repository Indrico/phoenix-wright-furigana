using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PwaatFurigana
{
    public sealed class ReadingSpan
    {
        public int Start, Length;
        public string Reading;
        public ReadingSpan(int start, int length, string reading)
        { Start = start; Length = length; Reading = reading; }
    }

    public sealed class ReadingIndex
    {
        private readonly Dictionary<string, ReadingSpan[]> entries = new Dictionary<string, ReadingSpan[]>(StringComparer.Ordinal);
        private readonly string[] keys;
        private static readonly ReadingSpan[] Empty = new ReadingSpan[0];

        public ReadingIndex(TextReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab < 0) throw new InvalidDataException("Invalid furigana data");
                string text = line.Substring(0, tab);
                List<ReadingSpan> spans = new List<ReadingSpan>();
                foreach (string value in line.Substring(tab + 1).Split(';'))
                {
                    if (value.Length == 0) continue;
                    string[] fields = value.Split(',');
                    int start = int.Parse(fields[0]), length = int.Parse(fields[1]);
                    if (start < 0 || length <= 0 || start + length > text.Length || fields.Length != 3)
                        throw new InvalidDataException("Invalid furigana span");
                    spans.Add(new ReadingSpan(start, length, fields[2]));
                }
                entries.Add(text, spans.ToArray());
            }
            keys = new string[entries.Count];
            entries.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
        }

        public ReadingSpan[] Resolve(string visible, string fullLine)
        {
            if (string.IsNullOrEmpty(visible)) return Empty;
            List<ReadingSpan> visibleSpans = new List<ReadingSpan>();
            foreach (ReadingSpan span in ResolveAll(visible, fullLine))
                if (span.Start + span.Length <= visible.Length) visibleSpans.Add(span);
            return visibleSpans.ToArray();
        }

        private ReadingSpan[] ResolveAll(string visible, string fullLine)
        {
            ReadingSpan[] spans;
            if (!string.IsNullOrEmpty(fullLine) && fullLine.StartsWith(visible, StringComparison.Ordinal)
                && entries.TryGetValue(fullLine, out spans)) return spans;
            if (entries.TryGetValue(visible, out spans)) return spans;
            // Restored dialogue and inserted text may have no script context. Only
            // use a prefix when every candidate agrees on the completed spans.
            int index = Array.BinarySearch(keys, visible, StringComparer.Ordinal);
            if (index < 0) index = ~index;
            if (index == keys.Length || !keys[index].StartsWith(visible, StringComparison.Ordinal)) return Empty;
            List<ReadingSpan> common = new List<ReadingSpan>();
            foreach (ReadingSpan span in entries[keys[index]])
                if (span.Start + span.Length <= visible.Length) common.Add(span);
            int candidates = 0;
            while (++index < keys.Length && keys[index].StartsWith(visible, StringComparison.Ordinal))
            {
                if (++candidates > 64) return Empty;
                ReadingSpan[] other = entries[keys[index]];
                for (int i = common.Count - 1; i >= 0; i--)
                {
                    bool found = false;
                    foreach (ReadingSpan span in other)
                        if (span.Start == common[i].Start && span.Length == common[i].Length && span.Reading == common[i].Reading) found = true;
                    if (!found) common.RemoveAt(i);
                }
            }
            return common.ToArray();
        }

        public static string PlainText(string text)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<' && (text.IndexOf("<color=", i, StringComparison.Ordinal) == i
                    || text.IndexOf("</color>", i, StringComparison.Ordinal) == i))
                {
                    int end = text.IndexOf('>', i);
                    if (end >= 0) { i = end; continue; }
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }
}
