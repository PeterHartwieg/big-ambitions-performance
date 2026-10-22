#!/usr/bin/env python3
import pathlib, shutil, hashlib, zipfile
root = pathlib.Path(__file__).resolve().parent.parent
out = root/'build'/'CityPerformance'
if out.exists(): shutil.rmtree(out)
files = {
 'CityPerformance.dll': root/'bin/Release/netstandard2.1/CityPerformance.dll',
 'Dependencies/0Harmony.dll': root/'build/deps/0Harmony.dll',
 'Dependencies/Harmony-LICENSE.txt': root/'build/deps/Harmony-LICENSE.txt',
 'Locales/en.json': root/'Locales/en.json',
 'README.md': root/'README.md',
}
for name, src in files.items():
 dest=out/name; dest.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src,dest)
manifest=''.join(hashlib.sha256((out/name).read_bytes()).hexdigest()+'  '+name+'\n' for name in sorted(files))
(root/'build/CityPerformance.sha256').write_text(manifest)
with zipfile.ZipFile(root/'build/CityPerformance-0.4.4.zip','w',zipfile.ZIP_DEFLATED) as z:
 for name in sorted(files): z.write(out/name,'CityPerformance/'+name)
print('Packaged',out)
