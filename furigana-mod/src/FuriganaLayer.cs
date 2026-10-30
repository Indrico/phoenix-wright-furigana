using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PwaatFurigana
{
    public sealed class FuriganaLayer : MonoBehaviour
    {
        private const string Preference = "PWAAT.JapaneseFurigana.Enabled";
        private static FuriganaLayer instance;
        private ReadingIndex readings;
        private bool showReadings;
        private bool failed;
        private float statusUntil;
        private readonly List<Line> lines = new List<Line>();

        private sealed class Line
        {
            public Text Source;
            public string FullText, LastText;
            public Font LastFont;
            public readonly List<Text> Labels = new List<Text>();
            public readonly TextGenerator Generator = new TextGenerator();
            public List<UIVertex> Mesh = new List<UIVertex>();
            public TextEffect.BaseMessageTextEffect[] Effects;
            public ReadingSpan[] Spans = new ReadingSpan[0];
        }

        public static void Install()
        {
            if (instance != null) return;
            GameObject obj = new GameObject("Japanese Furigana");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<FuriganaLayer>();
        }

        private void Awake()
        {
            showReadings = PlayerPrefs.GetInt(Preference, 1) != 0;
            try
            {
                using (StreamReader reader = new StreamReader(Path.Combine(Application.streamingAssetsPath, "Furigana/readings.tsv"), Encoding.UTF8))
                    readings = new ReadingIndex(reader);
            }
            catch (Exception error)
            {
                failed = true;
                Debug.LogError("[Furigana] Cannot load readings: " + error.Message);
            }
        }

        private static bool IsJapanese()
        {
            return GSStatic.global_work_ != null && GSStatic.global_work_.language == Language.JAPAN;
        }

        private void Toggle()
        {
            showReadings = !showReadings;
            PlayerPrefs.SetInt(Preference, showReadings ? 1 : 0);
            PlayerPrefs.Save();
            statusUntil = Time.unscaledTime + 2f;
        }

        private void Update()
        {
            if (!failed && IsJapanese() && Input.GetKeyDown(KeyCode.F8)) Toggle();
        }

        private void OnGUI()
        {
            if (!IsJapanese()) return;
            float scale = Mathf.Max(0.5f, Screen.height / 1080f);
            Rect button = new Rect(Screen.width - 240f * scale, 12f * scale, 228f * scale, 40f * scale);
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.fontSize = Mathf.Max(12, Mathf.RoundToInt(20f * scale));
            int oldDepth = GUI.depth;
            GUI.depth = -100;
            string caption = failed ? "Furigana data missing" : "Furigana: " + (showReadings ? "ON" : "OFF") + "  [F8]";
            if (optionCtrl.instance != null && optionCtrl.instance.is_open)
            {
                if (GUI.Button(button, caption, style) && !failed) Toggle();
            }
            else if (Time.unscaledTime < statusUntil) GUI.Box(button, caption, style);
            GUI.depth = oldDepth;
        }

        private Line GetLine(Text source)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].Source == null) lines.RemoveAt(i);
                else if (lines[i].Source == source) return lines[i];
            }
            Line line = new Line();
            line.Source = source;
            line.Effects = source.GetComponents<TextEffect.BaseMessageTextEffect>();
            lines.Add(line);
            return line;
        }

        // Called immediately before the game's SetCharacter(work, character).
        // Read ahead only; the scenario cursor and original dialogue stay intact.
        public static void BeginLine(MessageWork work)
        {
            if (instance == null || instance.failed || !IsJapanese() || work.message_text.character_count != 0) return;
            messageBoardCtrl board = messageBoardCtrl.instance;
            if (board == null || work.message_line >= board.line_list.Count) return;
            Line line = instance.GetLine(board.line_list[work.message_line]);
            line.FullText = null;
            line.LastText = null;
            try
            {
                StringBuilder text = new StringBuilder();
                uint cursor = work.mdt_index;
                // Match the original AdjustCentering/GetCharacterCountInLine scan.
                for (int limit = 0; limit < 512; limit++)
                {
                    ushort code = work.mdt_data.GetMessage(cursor++);
                    if (code > 128) { text.Append((char)(code - 128)); continue; }
                    if (code == 0 || code == 1 || code == 2 || code == 7 || code == 8 || code == 9 || code == 10
                        || code == 13 || code == 21 || code == 42 || code == 45 || code == 46 || code == 69) break;
                    cursor += MessageSystem.code_proc_arg_count_table[code];
                }
                line.FullText = text.ToString();
            }
            catch (IndexOutOfRangeException)
            {
                // Some inserted characters are not backed by a scenario line.
                // The index can still resolve the visible text conservatively.
                line.FullText = null;
            }
        }

        private void LateUpdate()
        {
            bool active = !failed && showReadings && IsJapanese();
            messageBoardCtrl board = messageBoardCtrl.instance;
            if (active && board != null)
                foreach (Text source in board.line_list)
                    if (source != null) GetLine(source);
            foreach (Line line in lines)
            {
                if (line.Source == null) continue;
                bool visible = active && board != null && board.line_list.Contains(line.Source)
                    && line.Source.isActiveAndEnabled && line.Source.color.a > 0f;
                if (visible) Render(line);
                else foreach (Text label in line.Labels) label.gameObject.SetActive(false);
            }
        }

        private void Render(Line line)
        {
            Text source = line.Source;
            string plain = ReadingIndex.PlainText(source.text);
            if (plain.Length == 0)
            {
                foreach (Text label in line.Labels) label.gameObject.SetActive(false);
                line.LastText = null;
                return;
            }
            if (line.LastText != source.text || line.LastFont != source.font)
            {
                line.Spans = readings.Resolve(plain, line.FullText);
                line.LastText = source.text;
                line.LastFont = source.font;
            }
            TextGenerationSettings settings = source.GetGenerationSettings(source.rectTransform.rect.size);
            if (!line.Generator.Populate(source.text, settings))
            {
                foreach (Text label in line.Labels) label.gameObject.SetActive(false);
                return;
            }
            IList<UICharInfo> characters = line.Generator.characters;
            IList<UIVertex> vertices = line.Generator.verts;
            float unit = 1f / source.pixelsPerUnit;
            line.Mesh.Clear();
            // Apply the game's quotation spacing to a copy of its text vertices.
            // The original renderer and its vertices are never modified.
            int[] triangle = new int[] { 0, 1, 2, 2, 3, 0 };
            for (int i = 0; i < vertices.Count - 4; i += 4)
                foreach (int corner in triangle)
                {
                    UIVertex vertex = vertices[i + corner];
                    vertex.position *= unit;
                    line.Mesh.Add(vertex);
                }
            foreach (TextEffect.BaseMessageTextEffect effect in line.Effects)
                if (effect != null && effect.isActiveAndEnabled) effect.MessageModifyMesh(ref line.Mesh);
            int used = 0;
            foreach (ReadingSpan span in line.Spans)
            {
                int end = span.Start + span.Length;
                // Wait for the whole word to appear; do not reveal upcoming text.
                if (end > plain.Length || end > characters.Count || end * 4 > vertices.Count - 4) continue;
                float left = characters[span.Start].cursorPos.x * unit;
                float right = (characters[end - 1].cursorPos.x + characters[end - 1].charWidth) * unit;
                left += line.Mesh[span.Start * 6].position.x - vertices[span.Start * 4].position.x * unit;
                right += line.Mesh[(end - 1) * 6].position.x - vertices[(end - 1) * 4].position.x * unit;
                float top = float.MinValue;
                for (int i = span.Start * 6; i < end * 6; i++) top = Mathf.Max(top, line.Mesh[i].position.y);
                if (right <= left) continue;
                Text label;
                if (used < line.Labels.Count) label = line.Labels[used];
                else
                {
                    GameObject obj = new GameObject("Furigana", typeof(RectTransform));
                    obj.transform.SetParent(source.transform, false);
                    label = obj.AddComponent<Text>();
                    label.raycastTarget = false;
                    label.supportRichText = false;
                    label.alignment = TextAnchor.LowerCenter;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    line.Labels.Add(label);
                }
                used++;
                label.font = source.font;
                label.fontStyle = source.fontStyle;
                label.fontSize = Mathf.Max(12, Mathf.RoundToInt(source.fontSize * 0.30f));
                label.text = span.Reading;
                label.color = line.Mesh[span.Start * 6].color;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = source.rectTransform.pivot;
                label.rectTransform.pivot = new Vector2(0.5f, 0f);
                float available = right - left + source.fontSize * 0.10f;
                float width = Mathf.Max(available, label.preferredWidth);
                label.rectTransform.sizeDelta = new Vector2(width, source.fontSize * 0.34f);
                label.rectTransform.anchoredPosition = new Vector2((left + right) / 2f, top + 1f);
                label.rectTransform.localScale = new Vector3(Mathf.Min(1f, available / width), 1f, 1f);
                label.gameObject.SetActive(true);
            }
            for (int i = used; i < line.Labels.Count; i++) line.Labels[i].gameObject.SetActive(false);
        }
    }
}
