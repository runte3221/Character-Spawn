import dnfile, os, sys

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)

for td in dn.net.mdtables.TypeDef:
    if 'Glam' in str(td.TypeName) or 'HumanGuise' in str(td.TypeName):
        print(f"=== Type: {td.TypeName} ===")
        for f in td.FieldList:
            print(f"  Field: {f.row.Name}")
        for m in td.MethodList:
            print(f"  Method: {m.row.Name}")
