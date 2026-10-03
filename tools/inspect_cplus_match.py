import dnfile, os

dll = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\CustomizePlus\2.2.1.3\CustomizePlus.dll')
dn = dnfile.dnPE(dll)

# Look for methods in ProfileManager and ArmatureManager that get profiles for actor / character
for td in dn.net.mdtables.TypeDef:
    if td.TypeName in ('ProfileManager', 'ArmatureManager'):
        print(f"=== {td.TypeName} ===")
        for m in td.MethodList:
            md = dn.net.mdtables.MethodDef[m.row_index - 1]
            if any(k in str(md.Name) for k in ('GetProfile', 'GetActive', 'OnRender', 'Apply', 'Armature')):
                print(f"  {md.Name}")
