#!/usr/bin/env bash
set -euo pipefail
mod_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
game_dir=$(cd -- "$mod_dir/.." && pwd)
dotnet_bin=${DOTNET_BIN:-dotnet}
sdk_dir=$("$dotnet_bin" --list-sdks | tail -1 | sed -E 's/^([^ ]+) \[(.*)\]$/\2\/\1/')
managed_dir="$game_dir/PWAAT_Data/Managed"
test -s "$game_dir/PWAAT_Data/StreamingAssets/Furigana/readings.tsv" || { echo "Generate readings.tsv before building." >&2; exit 1; }
test -s "$game_dir/PWAAT_Data/StreamingAssets/Furigana/lexicon.tsv" || { echo "Generate lexicon.tsv before building." >&2; exit 1; }
references=()
for assembly in mscorlib System System.Core UnityEngine UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.InputModule UnityEngine.UIModule UnityEngine.TextRenderingModule UnityEngine.UI Assembly-CSharp; do
    references+=("-reference:$managed_dir/$assembly.dll")
done
"$dotnet_bin" "$sdk_dir/Roslyn/bincore/csc.dll" -nologo -noconfig -nostdlib+ -langversion:3 -target:library -optimize+ -deterministic+ \
    "${references[@]}" "-out:$managed_dir/PwaatFurigana.dll" "$mod_dir/src/ReadingIndex.cs" "$mod_dir/src/FuriganaLayer.cs"
"$dotnet_bin" run --project "$mod_dir/tools/Patcher" -- apply "$game_dir"
"$dotnet_bin" run --no-build --project "$mod_dir/tools/Patcher" -- verify "$game_dir"
