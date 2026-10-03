import dnfile, os, sys

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)

def resolve_token(tok):
    table_id = (tok >> 24) & 0xFF
    row_id = (tok & 0x00FFFFFF) - 1
    if table_id == 0x0A: # MemberRef
        if 0 <= row_id < len(dn.net.mdtables.MemberRef):
            mr = dn.net.mdtables.MemberRef[row_id]
            # also get class
            cls = resolve_token(mr.Class.row_index if hasattr(mr.Class, 'row_index') else 0)
            return f"MemberRef::{mr.Name} (on {mr.Class})"
    elif table_id == 0x06: # MethodDef
        if 0 <= row_id < len(dn.net.mdtables.MethodDef):
            md = dn.net.mdtables.MethodDef[row_id]
            return f"MethodDef::{md.Name}"
    elif table_id == 0x04: # Field
        if 0 <= row_id < len(dn.net.mdtables.Field):
            f = dn.net.mdtables.Field[row_id]
            return f"Field::{f.Name}"
    elif table_id == 0x01: # TypeRef
        if 0 <= row_id < len(dn.net.mdtables.TypeRef):
            tr = dn.net.mdtables.TypeRef[row_id]
            return f"TypeRef::{tr.TypeName}"
    elif table_id == 0x02: # TypeDef
        if 0 <= row_id < len(dn.net.mdtables.TypeDef):
            td = dn.net.mdtables.TypeDef[row_id]
            return f"TypeDef::{td.TypeName}"
    elif table_id == 0x70:
        us = dn.net.metadata.streams.get(b'#US')
        return f'ldstr "{us.get(tok & 0xFFFFFF)}"'
    return f"token_{tok:08x}"

OPCODES = {
    0x00: ("nop", 0), 0x01: ("break", 0), 0x02: ("ldarg.0", 0), 0x03: ("ldarg.1", 0),
    0x04: ("ldarg.2", 0), 0x05: ("ldarg.3", 0), 0x06: ("ldloc.0", 0), 0x07: ("ldloc.1", 0),
    0x08: ("ldloc.2", 0), 0x09: ("ldloc.3", 0), 0x0A: ("stloc.0", 0), 0x0B: ("stloc.1", 0),
    0x0C: ("stloc.2", 0), 0x0D: ("stloc.3", 0), 0x0E: ("ldarg.s", 1), 0x0F: ("ldarga.s", 1),
    0x10: ("starg.s", 1), 0x11: ("ldloc.s", 1), 0x12: ("ldloca.s", 1), 0x13: ("stloc.s", 1),
    0x14: ("ldnull", 0), 0x15: ("ldc.i4.m1", 0), 0x16: ("ldc.i4.0", 0), 0x17: ("ldc.i4.1", 0),
    0x18: ("ldc.i4.2", 0), 0x19: ("ldc.i4.3", 0), 0x1A: ("ldc.i4.4", 0), 0x1B: ("ldc.i4.5", 0),
    0x1C: ("ldc.i4.6", 0), 0x1D: ("ldc.i4.7", 0), 0x1E: ("ldc.i4.8", 0), 0x1F: ("ldc.i4.s", 1),
    0x20: ("ldc.i4", 4), 0x25: ("dup", 0), 0x26: ("pop", 0), 0x28: ("call", 4),
    0x2A: ("ret", 0), 0x2B: ("br.s", 1), 0x2C: ("brfalse.s", 1), 0x2D: ("brtrue.s", 1),
    0x2E: ("beq.s", 1), 0x2F: ("bge.s", 1), 0x30: ("bgt.s", 1), 0x31: ("ble.s", 1),
    0x32: ("blt.s", 1), 0x33: ("bne.un.s", 1), 0x38: ("br", 4), 0x39: ("brfalse", 4),
    0x3A: ("brtrue", 4), 0x3B: ("beq", 4), 0x6F: ("callvirt", 4), 0x72: ("ldstr", 4),
    0x73: ("newobj", 4), 0x7B: ("ldfld", 4), 0x7C: ("ldflda", 4), 0x7D: ("stfld", 4),
    0x7E: ("ldsfld", 4), 0x80: ("stsfld", 4), 0x8D: ("newarr", 4), 0x8E: ("ldlen", 0),
    0x8F: ("ldelem.i4", 0), 0x90: ("ldelem.u1", 0), 0x9C: ("stelem.i1", 0), 0xA2: ("stelem.ref", 0),
    0xD0: ("ldtoken", 4), 0xD2: ("conv.u1", 0), 0xDE: ("leave", 4), 0xDC: ("endfinally", 0),
}

def disasm(td_name, md_name):
    td = next((r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == td_name), None)
    if not td: return
    md = next((m for m in td.MethodList if str(m.row.Name) == md_name), None)
    if not md or not md.row.Rva: return
    offset = dn.get_offset_from_rva(md.row.Rva)
    hb = dn.__data__[offset]
    if (hb & 3) == 2:
        code_size = hb >> 2
        code_offset = offset + 1
    else:
        flags = int.from_bytes(dn.__data__[offset:offset+2], 'little')
        code_size = int.from_bytes(dn.__data__[offset+4:offset+8], 'little')
        header_size = (flags >> 12) * 4
        code_offset = offset + header_size
    code = dn.__data__[code_offset:code_offset+code_size]
    print(f'=== {td_name}::{md_name} (size: {code_size}) ===')
    us = dn.net.metadata.streams.get(b'#US')
    
    i = 0
    while i < len(code):
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

disasm('EventNpcIndex', '.ctor')
disasm('NpcData', 'TryGetEventCustomize')
