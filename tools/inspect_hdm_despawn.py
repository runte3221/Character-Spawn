import dnfile, os
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)
for td in dn.net.mdtables.TypeDef:
    for m in td.MethodList:
        md = dn.net.mdtables.MethodDef[m.row_index - 1]
        if any(w in md.Name.lower() for w in ('despawn', 'destroy', 'delete', 'removepuppet')):
            print(f"{td.TypeName}::{md.Name}")
