import dnfile

dn_main = dnfile.dnPE(r"C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Glamourer.dll")

mr = dn_main.net.mdtables.MemberRef[0x04b0 - 1]
print("MR Name:", mr.Name)
print("MR Class:", mr.Class.row.TypeName if hasattr(mr.Class, 'row') and hasattr(mr.Class.row, 'TypeName') else mr.Class)
print("MR Sig bytes:", mr.Signature.value.hex())

# Let's check which Method in Penumbra.GameData.dll matches this signature
dn_pgd = dnfile.dnPE(r"C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Penumbra.GameData.dll")
for t in dn_pgd.net.mdtables.TypeDef:
    if t.TypeName == mr.Class.row.TypeName:
        print(f"TypeDef {t.TypeName}:")
        for m in t.MethodList:
            if m.row.Name == mr.Name:
                print(f"  Method {m.row.Name} Sig: {m.row.Signature.value.hex()} Rva: {hex(m.row.Rva)}")
