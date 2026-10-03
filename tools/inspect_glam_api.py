import dnfile, os, sys, glob

sys.stdout.reconfigure(encoding='utf-8')
dlls = glob.glob(os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\Glamourer\*\Glamourer.dll'))
dn = dnfile.dnPE(dlls[0])

for td in dn.net.mdtables.TypeDef:
    if td.TypeName == 'StateApi':
        print(f"=== Type: {td.TypeName} ===")
        for m in td.MethodList:
            if 'ApplyState' in str(m.row.Name):
                print(f"  Method: {m.row.Name} RVA={m.row.Rva:x}")
                # check parameters
                sig = bytes(m.row.Signature.value)
                print(f"    Sig: {sig.hex()}")
                for p in m.ParamList:
                    print(f"    Param: {p.row.Name}")
