import dnfile, os, sys
import dncil.clr.il

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)

def resolve(tok):
    if not isinstance(tok, int): return str(tok)
    tid = (tok >> 24) & 0xFF
    rid = (tok & 0x00FFFFFF) - 1
    if tid == 0x0A and 0 <= rid < len(dn.net.mdtables.MemberRef):
        return f'MemberRef::{dn.net.mdtables.MemberRef[rid].Name}'
    if tid == 0x06 and 0 <= rid < len(dn.net.mdtables.MethodDef):
        return f'MethodDef::{dn.net.mdtables.MethodDef[rid].Name}'
    if tid == 0x04 and 0 <= rid < len(dn.net.mdtables.Field):
        return f'Field::{dn.net.mdtables.Field[rid].Name}'
    if tid == 0x01 and 0 <= rid < len(dn.net.mdtables.TypeRef):
        return f'TypeRef::{dn.net.mdtables.TypeRef[rid].TypeName}'
    if tid == 0x70:
        us = dn.net.user_strings.get_us(tok & 0x00FFFFFF)
        return f'ldstr "{us.value}"' if us else 'ldstr'
    return f'0x{tok:08x}'

for t in dn.net.mdtables.TypeDef:
    if t.TypeName in ('EventNpcIndex', 'NpcData'):
        print(f"=== Type: {t.TypeName} ===")
        for m in t.MethodList:
            print(f"  Method: {m.Name}")
            if m.Rva in dn.net.clr.MethodBodies:
                body = dn.net.clr.MethodBodies[m.Rva]
                try:
                    for ins in dncil.clr.il.parse_instructions(body):
                        print(f"    {ins.offset:04x}: {ins.opcode.name:15} {resolve(ins.operand)}")
                except Exception as e:
                    print(f"    Error: {e}")
