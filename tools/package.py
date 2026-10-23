#!/usr/bin/env python3
import pathlib, shutil, hashlib, zipfile, re
root = pathlib.Path(__file__).resolve().parent.parent
version = re.search(r'<Version>([^<]+)</Version>', (root/'CityPerformance.csproj').read_text()).group(1)
source_version = re.search(r'Version = "([^"]+)"', (root/'src/CityPerformance.cs').read_text()).group(1)
if version != source_version: raise SystemExit(f'Version mismatch: csproj {version}, source {source_version}')
out = root/'build'/'CityPerformance'
if out.exists(): shutil.rmtree(out)
files = {
 'CityPerformance.dll': root/'bin/Release/netstandard2.1/CityPerformance.dll',
 'Dependencies/0Harmony.dll': root/'build/deps/0Harmony.dll',
 'Dependencies/Harmony-LICENSE.txt': root/'build/deps/Harmony-LICENSE.txt',
 'Locales/en.json': root/'Locales/en.json',
 'README.md': root/'README.md',
 'CHANGELOG.md': root/'CHANGELOG.md',
}
# The Mod Creator uses the first image in the mod folder's root as the Workshop thumbnail.
preview = root/'workshop'/'preview.png'
if not preview.is_file(): raise SystemExit('Missing workshop/preview.png')
files['preview.png'] = preview
for name, src in files.items():
 dest=out/name; dest.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src,dest)
manifest=''.join(hashlib.sha256((out/name).read_bytes()).hexdigest()+'  '+name+'\n' for name in sorted(files))
(root/'build/CityPerformance.sha256').write_text(manifest)
archive = root/'build'/f'CityPerformance-{version}.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 for name in sorted(files): z.write(out/name,'CityPerformance/'+name)
print('Packaged',out,'and',archive)

(root/'build'/f'CityPerformance-{version}.zip.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n')
