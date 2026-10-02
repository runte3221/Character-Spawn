import dnfile

dn = dnfile.dnPE(r'C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Glamourer.Api.dll')
for t in dn.net.mdtables.TypeDef:
    if 'ApplyFlag' in t.TypeName:
        print(f"Type: {t.TypeNamespace}.{t.TypeName}")
        for f in dn.net.mdtables.Field:
            # Check fields
            pass

# Also dump all enum fields in Glamourer.Api.Enums
for row in dn.net.mdtables.TypeDef:
    name = row.TypeName
    ns = row.TypeNamespace
    if 'Enum' in ns or 'Apply' in name or 'Flag' in name:
        print(f"TypeDef: {ns}.{name}")

