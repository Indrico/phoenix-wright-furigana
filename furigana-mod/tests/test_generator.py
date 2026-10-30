import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from generate_readings import annotate, hiragana, split_reading
from fugashi import Tagger


class ReadingTests(unittest.TestCase):
    def test_okurigana(self):
        self.assertEqual(split_reading("食べる", "たべる", 0), [(0, 1, "た")])
        self.assertEqual(split_reading("取り戻す", "とりもどす", 2), [(2, 1, "と"), (4, 1, "もど")])

    def test_kana_and_names(self):
        overrides = json.loads((Path(__file__).resolve().parents[1] / "name-readings.json").read_text())
        tagger = Tagger()
        self.assertEqual(annotate("こんにちは！", tagger, overrides), [])
        self.assertEqual(annotate("成歩堂さん。", tagger, overrides), [(0, 3, "なるほどう")])
        self.assertEqual(annotate("真犯人", tagger, overrides), [(0, 3, "しんはんにん")])
        self.assertEqual(hiragana("キョウ"), "きょう")


if __name__ == "__main__":
    unittest.main()
