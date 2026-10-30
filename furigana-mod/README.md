# Japanese furigana toggle

This folder contains the source-build tools. For the ready-to-install Windows
package, see the [repository README](../README.md). For a source build, place
this `furigana-mod` folder inside the game directory and follow Rebuilding below.

After building, launch the Windows game as usual,
select Japanese, and open **Options**. Click **Furigana: ON/OFF [F8]** in the
top-right corner. **F8** also toggles readings during gameplay, with a brief
status message. The preference is remembered between launches and starts on.
The button and readings are hidden in other languages.

Readings appear above kanji in dialogue and all active Unity UI text: menus,
choices, evidence descriptions, and profiles across all three games. Dialogue
readings appear after their complete word is visible. The overlay follows the
existing font, color, quotation spacing, and camera layer without changing the
original text or scenario cursor. Text baked into images is outside this patch's
scope; the game's speaker-name sprites already use kana.

The offline data contains 81,010 lines and 151,351 reading spans from 106 Japanese
MDT files and 27 menu/choice/evidence text files. A fallback lexicon contains
11,007 words and individual kanji for composite UI strings. Readings were generated with fugashi/UniDic, plus the character-name
and vocabulary corrections in `name-readings.json`. Automated readings can still
misinterpret unusual names, wordplay, and ambiguous kanji. No network connection,
Python, or .NET SDK is needed to play.

**Validation:** the shipped runtime DLL's lookup was tested against every data
entry, including partial words, ambiguous prefixes, restored text, rich-text
offsets, and character-name overrides. Static verification confirms exactly two
changed method bodies and 9,445 unchanged bodies; the original backup's hash,
runtime CLR compatibility, dependency files, and data hashes are checked.
Restoring, reinstalling, and upgrading from the previous alpha were also
exercised. A Wine 8.0/Xvfb attempt could not start the player because Steam was
unavailable. **The in-game layout, clicks, hotkey, and saved preference still
need a Windows play test.** Every kanji in 3,582 extracted UI text samples was
covered by the actual runtime DLL's reading lookup.

## Files and restoring

- `../PWAAT_Data/Managed/Assembly-CSharp.dll`: patched game assembly.
- `../PWAAT_Data/Managed/PwaatFurigana.dll`: added overlay and lookup code.
- `../PWAAT_Data/StreamingAssets/Furigana/readings.tsv`: contextual readings.
- `../PWAAT_Data/StreamingAssets/Furigana/lexicon.tsv`: composite UI fallback.
- `backups/Assembly-CSharp.dll`: untouched original game assembly, created locally
  during patching and excluded from this repository.
- `evidence/`: original-build identification, patch and data hashes.

To move the modification to Windows, copy these four installed files to the
same relative locations in the **same game build**, including `lexicon.tsv`.
Keep the original backup. For distribution, use the release installer instead.
This patch supports original Assembly-CSharp.dll SHA-256
`8c419d19c86812497cabf9e29b2c87651156a2d705176dadbfec9b066747d15f`.

Close the game and double-click `Restore original game.cmd` to remove the hooks.
Alternatively, copy `backups/Assembly-CSharp.dll` over the installed assembly.
The extra DLL and reading data are inactive once the original is restored.

## Rebuilding

Use a .NET 8 SDK and Python 3.12 for build-time tools. The runtime is compiled
against this game's own .NET 3.5/Unity references with C# 3 syntax.

```sh
python3 -m venv .venv
.venv/bin/pip install -r furigana-mod/tools/requirements.txt
.venv/bin/python furigana-mod/tools/generate_readings.py
bash furigana-mod/tools/build.sh
dotnet run --project furigana-mod/tests/Readings -- PWAAT_Data/StreamingAssets/Furigana/readings.tsv
.venv/bin/python -m unittest discover -s furigana-mod/tests -v
```

Commands above run from the game folder. Set `DOTNET_BIN` if `dotnet` is not on
PATH. `build.sh` is a Bash script; on Windows it can run through WSL.
The patcher refuses unsupported or unexpectedly modified assemblies, preserves
the original backup, and always rebuilds the two hooks from that backup.

The patcher's restore command is:

```sh
dotnet run --project furigana-mod/tools/Patcher -- restore /absolute/path/to/game
```

## Reverse engineering evidence

The investigation followed the supplied
[REA guide](https://github.com/morluto/rea) and its
[managed-code workflow](https://github.com/morluto/rea/blob/main/docs/managed-code-analysis.md).
REA 6.8.0 identified a Unity Mono/CIL assembly with complete metadata coverage.
ILSpy 9.1 recovered the relevant C# behavior; Mono.Cecil 0.11.6 applied the hooks.

`mainCtrl.Awake()` starts the overlay. The two-argument
`MessageSystem.SetCharacter(MessageWork, char)` captures the full line before
its first character. `MdtData` stores encoded UTF-16 characters plus control
instructions; opcode 128 is a timing command, not a printable NUL. The overlay
uses `messageBoardCtrl.line_list` and discovers other active `UnityEngine.UI.Text`
components, while preserving color markup. Labels inherit the source's layer:
dialogue uses layer 8, and its camera's culling mask is 256 (Default is excluded).
The original Japanese renderer has no furigana path in these inspected types.
Detailed type tokens, method identities, and source hashes are recorded under
`evidence/`. Tokens identify this particular original build.

UniDic/unidic-lite notices are included under `licenses/`.
