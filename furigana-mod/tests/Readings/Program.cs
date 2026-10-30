using PwaatFurigana;
using System.Text;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
// Context chooses the right reading before the typewriter finishes the line.
var sample = new ReadingIndex(new StringReader(
    "生物です\t0,2,せいぶつ\n生き物です\t0,1,い;2,1,もの\n御剣検事\t0,2,みつるぎ;2,2,けんじ\n"));
Check(sample.Resolve("生", "生物です").Length == 0, "Incomplete words must not reveal future readings");
Check(sample.Resolve("生物", "生物です").Single().Reading == "せいぶつ", "Complete compound reading");
Check(sample.Resolve("生", "生き物です").Single().Reading == "い", "Context-sensitive reading");
Check(sample.Resolve("生", null).Length == 0, "Ambiguous prefixes must not invent a reading");
Check(sample.Resolve("御剣", null).Single().Reading == "みつるぎ", "Unique restored-text prefix");
Check(sample.Resolve("", null).Length == 0, "Empty text");
Check(sample.Resolve("Not in dictionary", null).Length == 0, "Unknown text");
Check(ReadingIndex.PlainText("<color=#68c0f0>御剣</color>検事") == "御剣検事", "Color-tag offsets");
Check(ReadingIndex.PlainText("＜証拠＞ <literal>") == "＜証拠＞ <literal>", "Literal angle brackets");

string file = Path.GetFullPath(args[0]);
using var reader = new StreamReader(file, Encoding.UTF8);
var corpus = new ReadingIndex(reader);
int count = 0, spanCount = 0;
foreach (string line in File.ReadLines(file))
{
    string[] parts = line.Split('\t');
    string text = parts[0];
    Check(!text.Any(char.IsControl), "Control character in dialogue key");
    var spans = corpus.Resolve(text, text);
    int expected = parts[1].Length == 0 ? 0 : parts[1].Split(';').Length;
    Check(spans.Length == expected, "Lost spans for " + text);
    foreach (var span in spans)
    {
        Check(span.Start >= 0 && span.Length > 0 && span.Start + span.Length <= text.Length, "Span outside dialogue");
        Check(span.Reading.Length > 0, "Empty reading");
        Check(text.Substring(span.Start, span.Length).Any(c => c >= 0x3400 && c <= 0x9fff || c >= 0xf900 && c <= 0xfaff || "々〆ヶ".Contains(c)), "Furigana on kana-only text");
    }
    spanCount += spans.Length;
    count++;
}
Check(count > 70000, "Trilogy corpus unexpectedly small");
string[] names = { "成歩堂", "御剣", "真宵", "千尋", "春美" };
string[] readings = { "なるほどう", "みつるぎ", "まよい", "ちひろ", "はるみ" };
for (int i = 0; i < names.Length; i++)
{
    // Test real script lines rather than invented dialogue.
    var real = File.ReadLines(file).First(l => l.Split('\t')[0].Contains(names[i]));
    var text = real.Split('\t')[0];
    Check(corpus.Resolve(text, text).Any(s => text.Substring(s.Start, s.Length) == names[i] && s.Reading == readings[i]), "Character-name override: " + names[i]);
}
Console.WriteLine($"PASS: contextual lookup, no early readings, restored text, rich-text tags, name overrides; {count:N0} lines / {spanCount:N0} spans validated.");
