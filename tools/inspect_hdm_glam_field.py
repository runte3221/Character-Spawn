import dnfile, os
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)
td = next(t for t in dn.net.mdtables.TypeDef if t.TypeName == 'GlamourerIpc')
for f in td.FieldList:
    fd = dn.net.mdtables.Field[f.row_index - 1]
    if fd.Name == '_applyState':
        print(fd.Name, hex(fd.Signature))
