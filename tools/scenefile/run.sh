#!/bin/bash
# Builds and runs the .aplat container and undo-history tests with mono:   tools/scenefile/run.sh
set -e
cd "$(dirname "$0")"
OUT="${TMPDIR:-/tmp}/aplat-scenefile"
mkdir -p "$OUT"
REFS="-r:System.Drawing.dll -r:System.Core.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Xml.dll"
SRC="Stubs.cs ../../IO/SceneFileManager.cs ../../IO/BlobPacker.cs ../../IO/UndoHistory.cs \
    ../../Models/Scene.cs ../../Models/SceneObject.cs ../../Models/Mesh.cs ../../Models/Vec3.cs \
    ../../Models/Material.cs ../../Models/PaintTiles.cs ../../Models/InkJournal.cs ../../Models/InkOps.cs ../../Controls/LiveItem.cs"
mcs -nologo -langversion:latest $REFS -out:"$OUT/scenefile.exe" SceneFileTests.cs $SRC
mcs -nologo -langversion:latest $REFS -out:"$OUT/history.exe" HistoryTests.cs $SRC
mono "$OUT/scenefile.exe"
mono "$OUT/history.exe"
