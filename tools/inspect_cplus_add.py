import dnfile, os

dll = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\CustomizePlus\2.2.1.3\CustomizePlus.dll')
dn = dnfile.dnPE(dll)
td = next(t for t in dn.net.mdtables.TypeDef if t.TypeName == 'CustomizePlusIpc')
md = next(dn.net.mdtables.MethodDef[m.row_index - 1] for m in td.MethodList if str(dn.net.mdtables.MethodDef[m.row_index - 1].Name) == 'AddPlayerCharacterToProfile')

offset = dn.get_offset_from_rva(md.Rva)
hb = dn.__data__[offset]
flags = int.from_bytes(dn.__data__[offset:offset+2], 'little')
code_size = int.from_bytes(dn.__data__[offset+4:offset+8], 'little') if (hb & 3) != 2 else (hb >> 2)
header_size = (flags >> 12) * 4 if (hb & 3) != 2 else 1
code = dn.__data__[offset+header_size:offset+header_size+code_size]

us = dn.net.metadata.streams.get(b'#US')
def resolve_token(tok):
    table_id = (tok >> 24) & 0xFF
    row_id = (tok & 0x00FFFFFF) - 1
    if table_id == 0x0A:
        if 0 <= row_id < len(dn.net.mdtables.MemberRef):
            return f"MemberRef::{dn.net.mdtables.MemberRef[row_id].Name}"
    elif table_id == 0x06:
        if 0 <= row_id < len(dn.net.mdtables.MethodDef):
            return f"MethodDef::{dn.net.mdtables.MethodDef[row_id].Name}"
    elif table_id == 0x04:
        if 0 <= row_id < len(dn.net.mdtables.Field):
            return f"Field::{dn.net.mdtables.Field[row_id].Name}"
    elif table_id == 0x01:
        if 0 <= row_id < len(dn.net.mdtables.TypeRef):
            return f"TypeRef::{dn.net.mdtables.TypeRef[row_id].TypeName}"
    elif table_id == 0x02:
        if 0 <= row_id < len(dn.net.mdtables.TypeDef):
            return f"TypeDef::{dn.net.mdtables.TypeDef[row_id].TypeName}"
    return f"tok_{tok:08x}"

i = 0
while i < len(code):
    pos = i
    b = code[i]
    i += 1
    if b == 0x72:
        tok = int.from_bytes(code[i:i+4], 'little')
        i += 4
        s = us.get(tok & 0xFFFFFF) if us else ''
        print(f"  {pos:04x}: ldstr \"{s}\"")
    elif b in (0x28, 0x6f):
        tok = int.from_bytes(code[i:i+4], 'little')
        i += 4
        print(f"  {pos:04x}: call {resolve_token(tok)}")
    elif b in (0x7b, 0x7e, 0x7d, 0x80):
        tok = int.from_bytes(code[i:i+4], 'little')
        i += 4
        print(f"  {pos:04x}: fld {resolve_token(tok)}")
    elif b in (0x20, 0x14, 0x19):
        val = int.from_bytes(code[i:i+4], 'little', signed=True)
        i += 4
        print(f"  {pos:04x}: op_{b:02x} {val}")
    elif b in (0x1f, 0x10, 0x0e, 0x2c, 0x2d, 0x2e, 0x2b):
        val = code[i]
        i += 1
        print(f"  {pos:04x}: op_{b:02x} {val}")
    else:
        print(f"  {pos:04x}: op_{b:02x}")
