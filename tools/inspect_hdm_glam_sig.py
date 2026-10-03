import dnfile, os
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)
td = next(t for t in dn.net.mdtables.TypeDef if t.TypeName == 'GlamourerIpc')
md = next(dn.net.mdtables.MethodDef[m.row_index - 1] for m in td.MethodList if dn.net.mdtables.MethodDef[m.row_index - 1].Name == 'ApplyState')
print("RVA:", hex(md.Rva))

# Disassemble ApplyState in GlamourerIpc
import dump_human_guise
dump_human_guise.disasm('GlamourerIpc', 'ApplyState')
