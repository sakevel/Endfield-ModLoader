"""Developer-only Qt 5.15 header/import-library setup; never packages Qt binaries."""
import argparse, re, subprocess
from pathlib import Path
import pefile
p=argparse.ArgumentParser()
p.add_argument('--source',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--native',type=Path,required=True)
p.add_argument('--perl',required=True)
a=p.parse_args();a.source=a.source.resolve();a.output=a.output.resolve()
commit=subprocess.check_output(['git','-C',str(a.source),'rev-parse','HEAD'],text=True).strip()
if commit!='4ee4fc18b4067b90efa46ca9baba74f53b54d9ec':raise RuntimeError('Qt header revision mismatch')
subprocess.run(['git','-C',str(a.source),'diff','--exit-code','HEAD','--','src/corelib','src/gui','src/widgets','bin/syncqt.pl'],check=True,stdout=subprocess.DEVNULL)
subprocess.run([a.perl,str(a.source/'bin/syncqt.pl'),str(a.source),'-outdir',str(a.output),'-version','5.15.8','-module','QtCore','-module','QtGui','-module','QtWidgets','-quiet'],check=True)
# This adapter uses the Windows shared 5.15 public widget ABI only. Enable header
# declarations; no Qt sources/private headers are compiled, and missing imports fail.
features=set()
for root in ['corelib','gui','widgets']:
    for f in (a.source/'src'/root).rglob('*.h'):
        features.update(re.findall(r'QT_(?:CONFIG|REQUIRE_CONFIG)\((\w+)\)',f.read_text(encoding='utf8',errors='strict')))
config='#pragma once\n#define QT_VERSION_MAJOR 5\n#define QT_VERSION_MINOR 15\n#define QT_VERSION_PATCH 8\n#define QT_VERSION_STR "5.15.8"\n#define QT_SHARED\n'
config+=''.join(f'#define QT_FEATURE_{x} 1\n' for x in sorted(features))
for module,name in [('QtCore','qtcore'),('QtGui','qtgui'),('QtWidgets','qtwidgets')]:
    (a.output/'include'/module/(name+'-config.h')).write_text(config,encoding='utf8')
(a.output/'include/QtCore/qconfig.h').write_text(config,encoding='utf8')
for dll in ['Qt5Core','Qt5Gui','Qt5Widgets']:
    image=pefile.PE(str(a.native/(dll+'.dll')))
    exports=[]
    for e in image.DIRECTORY_ENTRY_EXPORT.symbols:
        if e.name:
            section=image.get_section_by_rva(e.address)
            exports.append(e.name.decode('ascii')+(' DATA' if not section.Characteristics&0x20000000 else ''))
    (a.output/(dll+'.def')).write_text('LIBRARY '+dll+'.dll\nEXPORTS\n'+'\n'.join(exports),encoding='ascii')
print('Pinned Qt headers and import definitions prepared; no native binaries copied')
