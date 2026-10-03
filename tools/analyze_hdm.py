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
            return f"MemberRef::{mr.Name}"
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
    elif table_id == 0x1B: # TypeSpec
        return f"TypeSpec_{tok:08x}"
    elif table_id == 0x2B: # MethodSpec
        return f"MethodSpec_{tok:08x}"
    return f"token_{tok:08x}"

def dump_method(td_name, md_name):
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
        op = code[i]
        pos = i
        i += 1
        if op == 0x72: # ldstr
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            str_off = tok & 0x00FFFFFF
            s = us.get(str_off) if us else ''
            print(f'  {pos:04x}: ldstr "{s}"')
        elif op in (0x28, 0x6f): # call, callvirt
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            call_type = 'call' if op == 0x28 else 'callvirt'
            print(f'  {pos:04x}: {call_type} {resolve_token(tok)}')
        elif op in (0x7b, 0x7e, 0x7d, 0x80): # ldfld, ldsfld, stfld, stsfld
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            fname = resolve_token(tok)
            fop = {0x7b:'ldfld', 0x7e:'ldsfld', 0x7d:'stfld', 0x80:'stsfld'}[op]
            print(f'  {pos:04x}: {fop} {fname}')
        elif op == 0xfe:
            op2 = code[i]
            i += 1
            if op2 in (0x06, 0x07, 0x09, 0x0a, 0x0c, 0x0d):
                i += 2
            elif op2 in (0x13, 0x14):
                i += 4
        elif op in (0x20, 0x25, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44):
            i += 4
        elif op in (0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37):
            i += 1
        elif op in (0x11, 0x12, 0x13):
            i += 2
        elif op in (0x14, 0x15, 0x16):
            i += 4
        elif op in (0x1f, 0x0e, 0x10):
            i += 1

# Opcode map for common IL opcodes
import dis

us = dn.net.metadata.streams.get(b'#US')

def disasm_full(td_name, md_name):
    td = next(r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == td_name)
    md = next(m for m in td.MethodList if str(m.row.Name) == md_name)
    offset = dn.get_offset_from_rva(md.row.Rva)
    hb = dn.__data__[offset]
    flags = int.from_bytes(dn.__data__[offset:offset+2], 'little')
    code_size = int.from_bytes(dn.__data__[offset+4:offset+8], 'little') if (hb & 3) != 2 else (hb >> 2)
    header_size = (flags >> 12) * 4 if (hb & 3) != 2 else 1
    code = dn.__data__[offset+header_size:offset+header_size+code_size]
    print(f"=== FULL DISASM: {td_name}::{md_name} ({code_size} bytes) ===")
    
    i = 0
    while i < len(code):
        pos = i
        b = code[i]
        i += 1
        if b == 0xfe:
            b2 = code[i]
            i += 1
            print(f"  {pos:04x}: FE {b2:02x}")
        elif b == 0x72: # ldstr
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            s = us.get(tok & 0xFFFFFF) if us else ''
            print(f'  {pos:04x}: ldstr "{s}"')
        elif b in (0x28, 0x6f): # call, callvirt
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            print(f"  {pos:04x}: {'call' if b == 0x28 else 'callvirt'} {resolve_token(tok)}")
        elif b in (0x7b, 0x7e, 0x7d, 0x80):
            tok = int.from_bytes(code[i:i+4], 'little')
            i += 4
            print(f"  {pos:04x}: fld {resolve_token(tok)}")
        elif b in (0x20, 0x14, 0x19): # ldc.i4, ldarg, etc
            val = int.from_bytes(code[i:i+4], 'little', signed=True)
            i += 4
            print(f"  {pos:04x}: op_{b:02x} {val} (0x{val:x})")
        elif b in (0x1f, 0x10, 0x0e, 0x2c, 0x2d, 0x2e, 0x2b): # 1 byte operand
            val = code[i]
            i += 1
            print(f"  {pos:04x}: op_{b:02x} {val} (0x{val:x})")
        else:
            print(f"  {pos:04x}: op_{b:02x}")

td = next(r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == 'HumanGuise')
md = next(m for m in td.MethodList if str(m.row.Name) == '.cctor')
offset = dn.get_offset_from_rva(md.row.Rva)
hb = dn.__data__[offset]
flags = int.from_bytes(dn.__data__[offset:offset+2], 'little')
code_size = int.from_bytes(dn.__data__[offset+4:offset+8], 'little') if (hb & 3) != 2 else (hb >> 2)
header_size = (flags >> 12) * 4 if (hb & 3) != 2 else 1
code = dn.__data__[offset+header_size:offset+header_size+code_size]

entries = []
i = 0
cur_str = None
cur_idx = None
cur_mask = None
while i < len(code):
    b = code[i]
    i += 1
    if b == 0x72:
        tok = int.from_bytes(code[i:i+4], 'little')
        i += 4
        cur_str = str(us.get(tok & 0xFFFFFF))
    elif b in (0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e):
        val = b - 0x16
        if cur_idx is None and cur_str is not None:
            cur_idx = val
        elif cur_idx is not None:
            cur_mask = val
    elif b == 0x1f:
        val = code[i]
        i += 1
        if cur_idx is None and cur_str is not None:
            cur_idx = val
        elif cur_idx is not None:
            cur_mask = val
    elif b == 0x20:
        val = int.from_bytes(code[i:i+4], 'little')
        i += 4
        cur_mask = val
    elif b == 0x73:
        tok = int.from_bytes(code[i:i+4], 'little')
        i += 4
        if cur_str and cur_idx is not None:
            entries.append((cur_str, cur_idx, cur_mask))
            cur_str, cur_idx, cur_mask = None, None, None

print("=== HDM CustomizeMap ===")
for e in entries[10:]:
    print(f'("{e[0]}", {e[1]}, 0x{e[2]:02X}),')



