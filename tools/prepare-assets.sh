#!/bin/zsh
set -eu
TASK_ROOT=${0:A:h:h}
cd "$TASK_ROOT"
if [[ ! -d assets/windows/Zenith ]]; then
  if [[ ! -f Zenith.7z ]]; then print -u2 'Place the original Zenith.7z in the project directory.'; exit 1; fi
  7z x Zenith.7z -oassets/windows -y
fi
mkdir -p assets/windows/Zenith/Previews
python3 - <<'PY'
from pathlib import Path
import shutil
root=Path('assets/windows/Zenith')
for module,key in [('ClassicRender','classic'),('FlatRender','flat'),('PFARender','pfa'),('MidiTrailRender','miditrail'),('TexturedRender','textured'),('NoteCountRender','notecounter'),('ScriptedRenderer','scripted')]:
    path=Path('upstream')/module/'preview.png'
    if not path.exists(): path=Path('upstream')/module/'Resources/preview.png'
    if path.exists(): shutil.copy2(path,root/'Previews'/f'{key}.png')
shutil.copy2('upstream/Black-Midi-Render/icon.png',root/'icon.png')
PY
