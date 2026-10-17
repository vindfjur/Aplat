#!/bin/bash
# Builds and runs the object-journal tests with mono (System.Drawing via libgdiplus):
#   tools/inktest/run.sh
# The journal classes are compiled alone, without WinForms / OpenTK.
set -e
cd "$(dirname "$0")"
OUT="${TMPDIR:-/tmp}/paint3d-inktest"
mkdir -p "$OUT"
REFS="-r:System.Drawing.dll -r:System.Core.dll -r:System.IO.Compression.dll -r:System.Xml.dll"
SRC="../../Models/InkJournal.cs ../../Models/InkOps.cs ../../Controls/LiveItem.cs Stubs.cs"
mcs -nologo -langversion:latest $REFS -out:"$OUT/journal.exe" JournalTests.cs $SRC
mcs -nologo -langversion:latest $REFS -out:"$OUT/material.exe" MaterialInkTests.cs ../../Models/Material.cs ../../Models/PaintTiles.cs $SRC
mcs -nologo -langversion:latest $REFS -out:"$OUT/group.exe" GroupTests.cs $SRC
mono "$OUT/journal.exe"
mono "$OUT/material.exe"
mono "$OUT/group.exe"
