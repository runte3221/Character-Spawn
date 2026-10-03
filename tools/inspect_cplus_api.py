import dnfile, os

dll = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\CustomizePlus\2.2.1.3\CustomizePlus.dll')
dn = dnfile.dnPE(dll)

td = next(t for t in dn.net.mdtables.TypeDef if t.TypeName == 'CustomizePlusIpc')
for m in td.MethodList:
    md = dn.net.mdtables.MethodDef[m.row_index - 1]
    if 'PlayerCharacter' in md.Name:
        print(f"=== {md.Name} @ {hex(md.Rva)} ===")
