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

    def test_opening_date_and_ui(self):
        overrides = json.loads((Path(__file__).resolve().parents[1] / "name-readings.json").read_text())
        tagger = Tagger()
        for text in ["８月３日　午前９時４７分", "地方裁判所　被告人第２控え室", "須々木マコ", "第一回　探　　偵"]:
            spans = annotate(text, tagger, overrides)
            covered = {i for start, length, _ in spans for i in range(start, start+length)}
            from generate_readings import KANJI
            self.assertTrue(all(i in covered for i,c in enumerate(text) if KANJI.search(c)), text)
        self.assertIn((2, 2, "みっか"), annotate("８月３日　午前９時４７分", tagger, overrides))
        self.assertEqual(annotate("探偵", tagger, overrides), [(0, 2, "たんてい")])


if __name__ == "__main__":
    unittest.main()
