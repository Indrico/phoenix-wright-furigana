"""Build offline, contextual readings from this installation's Japanese scripts.

Build dependencies: fugashi, unidic-lite, cryptography (see requirements.txt).
No dictionary or Python installation is needed when playing the patched game.
"""
import hashlib
import json
import re
import struct
import argparse
from collections import Counter, defaultdict
from pathlib import Path

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.padding import PKCS7
from fugashi import Tagger

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "furigana-mod"
ARGS = [
    0,0,0,1,1,2,2,0,2,3,1,1,1,0,1,2,1,0,3,1,
    0,0,0,1,1,2,4,1,1,1,3,0,1,0,2,2,0,1,1,2,
    1,1,3,0,1,0,0,2,1,2,2,5,1,2,1,2,1,1,3,2,
    1,1,1,0,0,0,1,1,1,0,1,2,2,0,1,1,0,2,1,7,
    1,2,1,0,2,1,2,1,0,1,1,2,3,0,0,3,4,3,0,0,
    1,2,3,0,0,4,1,3,0,1,1,1,3,3,0,0,2,4,2,2,
    1,0,1,2,0,1,1,1,0,
]
BREAKS = {0,1,2,7,8,9,10,13,21,42,45,46,69}
KANJI = re.compile(r"[\u3400-\u9fff\uf900-\ufaff々〆ヶ]")
RUNS = re.compile(r"[\u3400-\u9fff\uf900-\ufaff々〆ヶ]+|[^\u3400-\u9fff\uf900-\ufaff々〆ヶ]+")
CHAR_READINGS = {'偵': 'てい', '简': 'かん'}


def hiragana(text):
    return "".join(chr(ord(c)-0x60) if "ァ" <= c <= "ヶ" else c for c in text)


def split_reading(surface, reading, offset):
    """Align kana in inflected words, e.g. 取り戻す -> 取/と, 戻/もど."""
    runs = list(RUNS.finditer(surface))
    pattern = "^" + "".join("(.+?)" if KANJI.search(r[0]) else re.escape(hiragana(r[0])) for r in runs) + "$"
    match = re.match(pattern, reading)
    if not match:
        return [(offset, len(surface), reading)]
    groups = iter(match.groups())
    return [(offset+r.start(), len(r[0]), next(groups)) for r in runs if KANJI.search(r[0])]


def annotate(text, tagger, overrides, lexicon=None):
    names = re.compile("|".join(map(re.escape, sorted(overrides, key=len, reverse=True))))
    spans = []

    def tokenize(part, offset):
        cursor = 0
        for token in tagger(part):
            pos = part.index(token.surface, cursor)
            cursor = pos + len(token.surface)
            reading = token.feature.kana
            if KANJI.search(token.surface) and reading and reading != "*":
                reading = hiragana(reading)
                spans.extend(split_reading(token.surface, reading, offset+pos))
                if lexicon is not None:
                    lexicon[token.surface][reading] += 1

    start = 0
    for match in names.finditer(text):
        tokenize(text[start:match.start()], start)
        spans.extend(split_reading(match[0], overrides[match[0]], match.start()))
        start = match.end()
    tokenize(text[start:], start)
    # UniDic splits the date's number from 日, losing readings such as みっか.
    days = {1:'ついたち',2:'ふつか',3:'みっか',4:'よっか',5:'いつか',6:'むいか',7:'なのか',
            8:'ようか',9:'ここのか',10:'とおか',14:'じゅうよっか',20:'はつか',24:'にじゅうよっか'}
    for date in re.finditer(r'[０-９0-9]+月\s*([０-９0-9]+日)', text):
        day = int(date[1][:-1])
        if day in days:
            start, end = date.span(1)
            spans = [(s,n,r) for s,n,r in spans if s+n <= start or s >= end]
            spans.append((start, end-start, days[day]))
    # Unknown names must still receive a reading for every supported kanji.
    # Full contextual words always take precedence over this character fallback.
    covered = {i for s, n, r in spans for i in range(s, s+n)}
    for i, char in enumerate(text):
        if i not in covered and KANJI.search(char):
            if char in CHAR_READINGS:
                spans.append((i, 1, CHAR_READINGS[char]))
                continue
            for token in tagger(char):
                reading = token.feature.kana
                if reading and reading != "*" and not KANJI.search(reading):
                    spans.append((i, 1, hiragana(reading)))
                    break
    spans.sort()
    return spans


def decrypt(path, key):
    decryptor = Cipher(algorithms.AES(key[:16]), modes.CBC(key[16:])).decryptor()
    decoded = decryptor.update(path.read_bytes()) + decryptor.finalize()
    unpadder = PKCS7(128).unpadder()
    decoded = unpadder.update(decoded) + unpadder.finalize()
    return decoded


def text_lines(path, key):
    """ConvertTextData / ConvertLineData / ChoData's id + UTF-16 records."""
    data = decrypt(path, key)
    cursor = 0
    while cursor + 2 <= len(data):
        cursor += 2  # ushort record id
        start = cursor
        while cursor + 2 <= len(data) and data[cursor:cursor+2] != b'\0\0':
            cursor += 2
        text = data[start:cursor].decode('utf-16-le').replace('φ', ' ')
        yield from re.split(r'[,\r\n]', text)
        cursor += 2


def script_lines(path, key):
    decoded = decrypt(path, key)
    count = struct.unpack_from("<H", decoded)[0]
    start = 4 + count*4
    data = struct.unpack("<%dH" % ((len(decoded)-start)//2), decoded[start:])
    offsets = struct.unpack_from(f"<{count}I", decoded, 4)
    starts = sorted({(v-start)//2 for v in offsets if start <= v < len(decoded) and (v-start)%2 == 0})
    # Resynchronize at each real message; label entries encode references instead
    # of file offsets and are outside the data region.
    for begin, end in zip(starts, starts[1:]+[len(data)]):
        i, text = begin, []
        while i < end:
            code = data[i]
            i += 1
            if code > 128:
                char = chr(code-128)
                n = ord(char)
                valid = (0x20 <= n <= 0x7e or 0x370 <= n <= 0x3ff or 0x2000 <= n <= 0x30ff
                         or 0x3400 <= n <= 0x9fff or 0xf900 <= n <= 0xfaff or 0xff00 <= n <= 0xffef)
                if valid:
                    text.append(char)
                elif text:
                    yield "".join(text)
                    text = []
            else:
                if code in BREAKS and text:
                    yield "".join(text)
                    text = []
                i += ARGS[code]
        if text:
            yield "".join(text)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-dir', type=Path, default=ROOT)
    args = parser.parse_args()
    root = args.game_dir.resolve()
    key = hashlib.pbkdf2_hmac("sha1", b"u8DurGE2", b"6BBGizHE", 1000, 32)
    tagger = Tagger()
    overrides = json.loads((MOD / "name-readings.json").read_text())
    lines, files, counts = set(), [], {}
    for title in ["GS1", "GS2", "GS3"]:
        scenario = root / "PWAAT_Data/StreamingAssets" / title / "scenario"
        paths = sorted(scenario.glob("*.mdt"))
        paths = [p for p in paths if not re.search(r"_[ufgkstbl]\.mdt$", p.name)]
        title_lines = set()
        for path in paths:
            title_lines.update(script_lines(path, key))
            files.append({"path": str(path.relative_to(root)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        counts[title] = {"files": len(paths), "unique_lines": len(title_lines)}
        lines.update(title_lines)
    extras = sorted((root/'PWAAT_Data/StreamingAssets/menu/text').glob('*.bin'))
    for title in ['GS1', 'GS2', 'GS3']:
        scenario = root/'PWAAT_Data/StreamingAssets'/title/'scenario'
        extras.extend(sorted(scenario.glob('*.bin')))
        extras.extend(sorted(scenario.glob('*.cho')))
    extras = [p for p in extras if not re.search(r'_[ufgkstbl]\.(?:bin|cho)$', p.name)]
    for path in extras:
        lines.update(text_lines(path, key))
        files.append({'path': str(path.relative_to(root)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    out = root / "PWAAT_Data/StreamingAssets/Furigana"
    out.mkdir(exist_ok=True)
    total = 0
    lexicon = defaultdict(Counter)
    uncovered = Counter()
    with (out / "readings.tsv").open("w", encoding="utf-8", newline="\n") as f:
        for line in sorted(lines):
            if not KANJI.search(line):
                continue
            if any(c in line for c in "\t\r\n"):
                raise ValueError("Unexpected delimiter in script line")
            spans = annotate(line, tagger, overrides, lexicon)
            covered = {i for s, n, r in spans for i in range(s, s+n)}
            uncovered.update(c for i,c in enumerate(line) if i not in covered and KANJI.search(c))
            f.write(line + "\t" + ";".join(f"{s},{n},{r}" for s,n,r in spans) + "\n")
            total += 1
    for word, reading in overrides.items():
        lexicon[word] = Counter({reading: 1000000})
    # Composite UI and inspector defaults can contain kanji absent from the
    # scenario corpus. Include every single-kanji reading known to UniDic.
    characters = set(KANJI.findall(''.join(lines)))
    characters.update(chr(i) for i in range(0x3400, 0xa000))
    characters.update(chr(i) for i in range(0xf900, 0xfb00))
    for char in sorted(characters):
        if char in CHAR_READINGS:
            lexicon[char][CHAR_READINGS[char]] += 1
            continue
        for token in tagger(char):
            reading = token.feature.kana
            if reading and reading != '*' and not KANJI.search(reading):
                lexicon[char][hiragana(reading)] += 1
                break
    with (out/'lexicon.tsv').open('w', encoding='utf-8', newline='\n') as f:
        for word in sorted(lexicon):
            reading = lexicon[word].most_common(1)[0][0]
            spans = split_reading(word, reading, 0)
            f.write(word+'\t'+';'.join(f'{s},{n},{r}' for s,n,r in spans)+'\n')
    report = {"titles": counts, "lines_with_kanji": total, "sources": files,
              "extra_text_files": len(extras), "lexicon_words": len(lexicon), 'uncovered_characters': dict(uncovered),
              "readings_sha256": hashlib.sha256((out/"readings.tsv").read_bytes()).hexdigest(),
              "lexicon_sha256": hashlib.sha256((out/"lexicon.tsv").read_bytes()).hexdigest()}
    evidence = root/'furigana-mod/evidence'
    evidence.mkdir(exist_ok=True)
    (evidence/"readings-build.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")
    print(json.dumps({k:v for k,v in report.items() if k != "sources"}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
