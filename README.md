# Phoenix Wright: Ace Attorney Trilogy — Japanese Furigana

An unofficial offline furigana toggle for the Windows Unity/Mono version of
Phoenix Wright: Ace Attorney Trilogy. It adds readings above kanji in dialogue,
choices, menus, profiles, and evidence text across all three games.

**v0.2.0-alpha.1 fixes invisible furigana:** the previous runtime put its labels
on Unity's Default layer, which the game's text cameras exclude. Labels now
inherit each source text's layer. This version also discovers all active UI
texts and includes the menu/evidence text tables and an offline word fallback.

**Preview release:** the reading lookup and installer are tested, but the
Windows game has not been launched in the development environment. In-game
layout, mouse interaction, F8 input, and saved preferences still need a Windows
play test. Automatically generated readings may misinterpret unusual names,
wordplay, and ambiguous kanji.

## Download and install

Download the Windows ZIP from [Releases](https://github.com/Indrico/phoenix-wright-furigana/releases).
Windows 10/11 with Windows PowerShell 5.1 is the intended installer environment.
No Python, .NET SDK, or mod loader is required to install or play.

1. Close the game.
2. Extract the ZIP and place its `furigana-mod` folder beside `PWAAT.exe`.
3. Open `furigana-mod` and double-click **Install.cmd**.
4. Launch the game normally and select **Japanese**.
5. In **Options**, use **Furigana: ON/OFF [F8]**, or press **F8** during play.

Readings start enabled and the preference is saved between launches. The button
and readings are hidden in other languages. Readings appear only after the
complete word is visible in dialogue. Complete UI text uses full-text readings
with a word/character fallback for strings assembled by the game. Text baked
into images, logos, and backgrounds is outside the mod's scope.

To upgrade from v0.1.0-alpha.1, close the game, replace the `furigana-mod` files
with the new ZIP, keep `furigana-mod/backups`, and run **Install.cmd** again.

The ZIP contains an assembly patcher, the mod's own DLL, and reading annotations keyed
by hashes. Installation reconstructs the patched assembly and reading table from
your local game files. Original game assemblies, scripts, images, and audio are
not bundled. All work is offline.

## Supported build and uninstall

The original `PWAAT_Data/Managed/Assembly-CSharp.dll` must have SHA-256:

```text
8c419d19c86812497cabf9e29b2c87651156a2d705176dadbfec9b066747d15f
```

The installer also checks 106 Japanese scenario files and 27 UI/choice text
files used to build 81,010 reading entries. The fallback lexicon contains
11,007 words/characters. Other game builds and unexpectedly modified assemblies are
rejected before installation.

To uninstall, close the game and run **furigana-mod/Uninstall.cmd**. The original
assembly is backed up in `furigana-mod/backups/Assembly-CSharp.dll` and retained
after uninstalling. Existing saves are not edited.

## Source and validation

- [`furigana-mod/src`](furigana-mod/src): Unity overlay and reading lookup.
- [`furigana-mod/tools`](furigana-mod/tools): data generation and C# assembly patcher.
- [`furigana-mod/tests`](furigana-mod/tests): reading and generation checks.
- [`furigana-mod/evidence`](furigana-mod/evidence): original build identity, hashes,
  and the initial validation record.
- [`release`](release): Windows installer and reproducible release packaging.

The reading validation covers 81,010 lines and 151,351 reading spans, including
the opening date/location, composite/multiline strings, and every kanji in 3,582
extracted UI text samples. Exactly two
method bodies are hooked; 9,445 method bodies remain unchanged.
See [developer instructions](furigana-mod/README.md) for source builds. Place that
`furigana-mod` folder inside your game directory before running its build tools.

Build a release from an already generated and verified local installation:

```sh
python3 release/build_release.py --game-dir /path/to/game --version v0.2.0-alpha.1
```

The packaging script verifies the source hashes. Test
the extracted installer against a disposable copy of the supported game assets
before publishing. `dist/` and local game backups are excluded from Git.

```sh
python3 release/test_release.py --game-dir /path/to/game \
  --archive dist/phoenix-wright-furigana-v0.2.0-alpha.1-windows.zip --pwsh pwsh
```

The installer is written for Windows PowerShell 5.1 and also supports PowerShell
7. Installer integration checks run with PowerShell 7 on Linux; Windows
PowerShell 5.1 has not yet been exercised. Both C# installer helpers use syntax
and framework APIs available to Windows PowerShell 5.1.

The reverse engineering followed the supplied [REA guide](https://github.com/morluto/rea).
Readings use fugashi/UniDic and curated corrections in
[`name-readings.json`](furigana-mod/name-readings.json). Third-party notices are in
[`furigana-mod/licenses`](furigana-mod/licenses).
