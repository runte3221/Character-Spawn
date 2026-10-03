import dnfile

dn = dnfile.dnPE(r'C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Glamourer.Api.dll')
for td in dn.net.mdtables.TypeDef:
    if td.TypeName == 'ApplyFlag':
        print(f"ApplyFlag in Glamourer.Api:")
        for f in td.FieldList:
            val = None
            for c in dn.net.mdtables.Constant:
                if c.Parent.row_index == f.row_index:
                    val = int.from_bytes(c.Value, 'little')
            print(f"  {f.row.Name} = {val}")
