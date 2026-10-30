// Installer helper. Compatible with Windows PowerShell 5.1's C# compiler.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public static class PwaatReleasePatch
{
    private static readonly int[] Args = {
        0,0,0,1,1,2,2,0,2,3,1,1,1,0,1,2,1,0,3,1,
        0,0,0,1,1,2,4,1,1,1,3,0,1,0,2,2,0,1,1,2,
        1,1,3,0,1,0,0,2,1,2,2,5,1,2,1,2,1,1,3,2,
        1,1,1,0,0,0,1,1,1,0,1,2,2,0,1,1,0,2,1,7,
        1,2,1,0,2,1,2,1,0,1,1,2,3,0,0,3,4,3,0,0,
        1,2,3,0,0,4,1,3,0,1,1,1,3,3,0,0,2,4,2,2,
        1,0,1,2,0,1,1,1,0
    };

    private static bool Breaks(int code)
    {
        return code == 0 || code == 1 || code == 2 || code == 7 || code == 8 || code == 9
            || code == 10 || code == 13 || code == 21 || code == 42 || code == 45 || code == 46 || code == 69;
    }

    private static IEnumerable<string> Lines(string path)
    {
        byte[] data;
        using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes("u8DurGE2", Encoding.UTF8.GetBytes("6BBGizHE"), 1000))
        using (Aes aes = Aes.Create())
        {
            aes.Key = derive.GetBytes(16);
            aes.IV = derive.GetBytes(16);
            using (ICryptoTransform decryptor = aes.CreateDecryptor())
            {
                byte[] encrypted = File.ReadAllBytes(path);
                data = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            }
        }
        if (!path.EndsWith(".mdt", StringComparison.OrdinalIgnoreCase))
        {
            int cursor = 0;
            while (cursor + 2 <= data.Length)
            {
                cursor += 2; // ushort text id, followed by a UTF-16 NUL record
                int begin = cursor;
                while (cursor + 2 <= data.Length && BitConverter.ToUInt16(data, cursor) != 0) cursor += 2;
                string value = Encoding.Unicode.GetString(data, begin, cursor - begin).Replace('\u03c6', ' ');
                foreach (string line in value.Split(new char[] { ',', '\r', '\n' })) yield return line;
                cursor += 2;
            }
            yield break;
        }
        int count = BitConverter.ToUInt16(data, 0), start = 4 + count * 4;
        int units = (data.Length - start) / 2;
        SortedSet<int> offsets = new SortedSet<int>();
        for (int i = 0; i < count; i++)
        {
            uint offset = BitConverter.ToUInt32(data, 4 + i * 4);
            if (offset >= start && offset < data.Length && (offset - start) % 2 == 0)
                offsets.Add(((int)offset - start) / 2);
        }
        List<int> boundaries = new List<int>(offsets);
        boundaries.Add(units);
        for (int message = 0; message < boundaries.Count - 1; message++)
        {
            StringBuilder text = new StringBuilder();
            int i = boundaries[message], end = boundaries[message + 1];
            while (i < end)
            {
                int code = BitConverter.ToUInt16(data, start + i++ * 2);
                if (code > 128)
                {
                    int n = code - 128;
                    bool valid = n >= 0x20 && n <= 0x7e || n >= 0x370 && n <= 0x3ff || n >= 0x2000 && n <= 0x30ff
                        || n >= 0x3400 && n <= 0x9fff || n >= 0xf900 && n <= 0xfaff || n >= 0xff00 && n <= 0xffef;
                    if (valid) text.Append((char)n);
                    else if (text.Length > 0) { yield return text.ToString(); text.Length = 0; }
                }
                else
                {
                    if (Breaks(code) && text.Length > 0) { yield return text.ToString(); text.Length = 0; }
                    i += Args[code];
                }
            }
            if (text.Length > 0) yield return text.ToString();
        }
    }

    public static int RebuildReadings(string[] assetFiles, string cachePath, string outputPath)
    {
        Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        using (Stream input = File.OpenRead(cachePath))
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
        using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
        {
            string row;
            while ((row = reader.ReadLine()) != null)
            {
                int tab = row.IndexOf('\t');
                if (tab != 64) throw new InvalidDataException("Invalid reading cache key");
                cache.Add(row.Substring(0, tab), row.Substring(tab + 1));
            }
        }
        SortedDictionary<string, string> output = new SortedDictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        using (SHA256 hash = SHA256.Create())
            foreach (string path in assetFiles)
                foreach (string text in Lines(path))
                {
                    if (!seen.Add(text)) continue;
                    string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
                    string spans;
                    if (cache.TryGetValue(key, out spans)) output.Add(text, spans);
                }
        if (output.Count != cache.Count) throw new InvalidDataException("Game scripts do not match the reading cache");
        using (StreamWriter writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            foreach (KeyValuePair<string, string> entry in output)
                writer.WriteLine(entry.Key + "\t" + entry.Value);
        }
        return output.Count;
    }
}
