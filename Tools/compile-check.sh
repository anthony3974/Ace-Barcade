#!/usr/bin/env bash
# Compiles the project's C# without opening the Unity editor, using the compiler and
# reference assemblies that ship with Unity. Writes nothing into the project, so it is safe
# to run while the editor (or another agent) is working in the same folder.
#
# Needs Library/ to exist: open the project in Unity once first.
# Usage: Tools/compile-check.sh
# Exit code 0 means everything compiled. Errors are printed as file(line,col): error ...
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)"
UNITY="${UNITY_EDITOR_DIR:-$HOME/Unity/Hub/Editor/$VERSION/Editor}"
DOTNET="$UNITY/Data/NetCoreRuntime/dotnet"
CSC="$UNITY/Data/DotNetSdkRoslyn/csc.dll"
[[ -x "$DOTNET" && -f "$CSC" ]] || { echo "Unity $VERSION not found at $UNITY (set UNITY_EDITOR_DIR)" >&2; exit 2; }

RSP="$(ls -t Library/Bee/artifacts/*.dag/AceBarcade.rsp 2>/dev/null | head -1 || true)"
[[ -n "$RSP" ]] || { echo "No Library/ yet: open the project in Unity once first." >&2; exit 2; }

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

# Engine, editor and system references plus defines, as Unity uses them.
grep -E '^-r:|^-define:' "$RSP" | grep -v 'Library/Bee/artifacts' > "$OUT/common.rsp"

pkg() { for n in "$@"; do echo "-r:\"Library/ScriptAssemblies/$n.dll\""; done; }
own() { for n in "$@"; do echo "-r:\"$OUT/$n.dll\""; done; }

compile() { # name, sources file, extra rsp lines file
    local name="$1" sources="$2" extra="$3"
    [[ -s "$sources" ]] || { echo "--   $name (no files)"; return; }
    {
        echo "-target:library"
        echo "-out:\"$OUT/$name.dll\""
        echo "-langversion:9.0"
        echo "-nologo"
        echo "-nowarn:0169,0649,0282,1701,1702"
        cat "$OUT/common.rsp" "$extra"
        sed 's/.*/"&"/' "$sources"
    } > "$OUT/$name.rsp"
    if "$DOTNET" "$CSC" "@$OUT/$name.rsp" > "$OUT/$name.log" 2>&1; then
        local warnings
        warnings="$(grep -c ': warning ' "$OUT/$name.log" || true)"
        echo "OK   $name ($(wc -l < "$sources") files, $warnings warnings)"
        grep ': warning ' "$OUT/$name.log" | grep -v 'Library/' | sort -u | head -20 || true
    else
        echo "FAIL $name"
        grep -E ': error |error CS' "$OUT/$name.log" | sort -u
        exit 1
    fi
}

RUNTIME="$(pkg Unity.InputSystem UnityEngine.UI Unity.TextMeshPro \
    Unity.RenderPipelines.Core.Runtime Unity.RenderPipelines.Universal.Runtime \
    Unity.RenderPipelines.Universal.2D.Runtime)"
TEST="$(pkg UnityEngine.TestRunner UnityEditor.TestRunner)"

# Shared code (Assets/Scripts, minus tests)
find Assets/Scripts -name '*.cs' -not -path 'Assets/Scripts/Tests/*' | sort > "$OUT/core.src"
echo "$RUNTIME" > "$OUT/core.extra"
compile AceBarcade "$OUT/core.src" "$OUT/core.extra"

# Games (Assets/Games)
find Assets/Games -name '*.cs' | sort > "$OUT/games.src"
{ echo "$RUNTIME"; own AceBarcade; } > "$OUT/games.extra"
compile AceBarcade.Games "$OUT/games.src" "$OUT/games.extra"

# Editor tools (Assets/Editor)
find Assets/Editor -name '*.cs' | sort > "$OUT/editor.src"
{ echo "$RUNTIME"; own AceBarcade AceBarcade.Games; } > "$OUT/editor.extra"
compile AceBarcade.Editor "$OUT/editor.src" "$OUT/editor.extra"

# Edit mode tests (Assets/Scripts/Tests)
find Assets/Scripts/Tests -name '*.cs' | sort > "$OUT/tests.src"
{ echo "$RUNTIME"; echo "$TEST"; own AceBarcade AceBarcade.Games; } > "$OUT/tests.extra"
compile AceBarcade.Tests "$OUT/tests.src" "$OUT/tests.extra"
