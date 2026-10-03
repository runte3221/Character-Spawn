import dump_human_guise, dnfile, os
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)
td = next(r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == 'HumanGuise')
md = next(m for m in td.MethodList if str(m.row.Name) == 'TryApplyOnce')
offset = dn.get_offset_from_rva(md.row.Rva)
hb = dn.__data__[offset]
flags = int.from_bytes(dn.__data__[offset:offset+2], 'little')
code_size = int.from_bytes(dn.__data__[offset+4:offset+8], 'little') if (hb & 3) != 2 else (hb >> 2)
header_size = (flags >> 12) * 4 if (hb & 3) != 2 else 1
code = dn.__data__[offset+header_size:offset+header_size+code_size]

us = dn.net.metadata.streams.get(b'#US')
OPCODES = dump_human_guise.OPCODES
resolve_token = dump_human_guise.resolve_token

i = 0x0500
while i < 0x06e0 and i < len(code):
    pos = i
    b = code[i]
    i += 1
    if b == 0xFE:
        b2 = code[i]
        i += 1
        print(f"  {pos:04x}: FE {b2:02x}")
        continue
    opname, oplen = OPCODES.get(b, (f"op_{b:02x}", 0))
    if oplen == 0:
        print(f"  {pos:04x}: {opname}")
    elif oplen == 1:
        val = code[i]
        i += 1
        print(f"  {pos:04x}: {opname} 0x{val:02x}")
    elif oplen == 4:
        val = int.from_bytes(code[i:i+4], 'little')
        i += 4
        if opname == 'ldstr':
            s = us.get(val & 0xFFFFFF) if us else ''
            print(f'  {pos:04x}: {opname} "{s}"')
        elif opname in ('call', 'callvirt', 'newobj', 'ldfld', 'stfld', 'ldsfld', 'stsfld', 'ldtoken'):
            print(f"  {pos:04x}: {opname} {resolve_token(val)}")
        else:
            print(f"  {pos:04x}: {opname} 0x{val:08x}")
