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
        private readonly Dictionary<string, ReadingSpan[]> words = new Dictionary<string, ReadingSpan[]>(StringComparer.Ordinal);
        private int maxWordLength;
        private readonly string[] keys;
        private static readonly ReadingSpan[] Empty = new ReadingSpan[0];

        public ReadingIndex(TextReader reader) : this(reader, null) { }

        public ReadingIndex(TextReader reader, TextReader lexicon)
        {
            Load(reader, entries);
            if (lexicon != null) Load(lexicon, words);
            foreach (string word in words.Keys) maxWordLength = Math.Max(maxWordLength, word.Length);
            keys = new string[entries.Count];
            entries.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
        }

        private static void Load(TextReader reader, Dictionary<string, ReadingSpan[]> target)
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
                target.Add(text, spans.ToArray());
            }
        }

        // Menus, evidence, profiles and generated UI strings are complete text.
        // They must not use the dialogue-prefix ambiguity/early-reveal rules.
        public ReadingSpan[] ResolveText(string text)
        {
            if (string.IsNullOrEmpty(text)) return Empty;
            ReadingSpan[] exact;
            if (entries.TryGetValue(text, out exact)) return exact;
            List<ReadingSpan> result = new List<ReadingSpan>();
            int offset = 0;
            foreach (string line in text.Split('\n'))
            {
                if (entries.TryGetValue(line, out exact))
                    foreach (ReadingSpan span in exact)
                        result.Add(new ReadingSpan(offset + span.Start, span.Length, span.Reading));
                else
                    for (int i = 0; i < line.Length; i++)
                        for (int length = Math.Min(maxWordLength, line.Length - i); length > 0; length--)
                        {
                            ReadingSpan[] found;
                            if (!words.TryGetValue(line.Substring(i, length), out found)) continue;
                            foreach (ReadingSpan span in found)
                                result.Add(new ReadingSpan(offset + i + span.Start, span.Length, span.Reading));
                            i += length - 1;
                            break;
                        }
                offset += line.Length + 1;
            }
            return result.ToArray();
        }

        public static bool ContainsKanji(string text)
        {
            foreach (char c in text)
                if (c >= '\u3400' && c <= '\u9fff' || c >= '\uf900' && c <= '\ufaff' || "々〆ヶ".IndexOf(c) >= 0) return true;
            return false;
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
            if (!string.IsNullOrEmpty(fullLine) && fullLine.StartsWith(visible, StringComparison.Ordinal))
            {
                if (entries.TryGetValue(fullLine, out spans)) return spans;
                return ResolveText(fullLine);
            }
            if (entries.TryGetValue(visible, out spans)) return spans;
            // Restored dialogue and inserted text may have no script context. Only
            // use a prefix when every candidate agrees on the completed spans.
            int index = Array.BinarySearch(keys, visible, StringComparer.Ordinal);
            if (index < 0) index = ~index;
            if (index == keys.Length || !keys[index].StartsWith(visible, StringComparison.Ordinal)) return ResolveText(visible);
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
                if (text[i] == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end >= 0)
                    {
                        string tag = text.Substring(i + 1, end - i - 1).TrimStart('/');
                        int equal = tag.IndexOf('=');
                        if (equal >= 0) tag = tag.Substring(0, equal);
                        if (tag == "color" || tag == "size" || tag == "b" || tag == "i" || tag == "material")
                        { i = end; continue; }
                    }
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }
}
