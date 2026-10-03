import dnfile, os, sys

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)

def decode_sig_type(data, pos):
    b = data[pos]
    pos += 1
    if b in (0x11, 0x12): # ELEMENT_TYPE_VALUETYPE or CLASS
        tok, pos = decode_compressed(data, pos)
        tbl_map = [0x02, 0x01, 0x1B, 0x00]
        table_id = tbl_map[tok & 3]
        rid = (tok >> 2) - 1
        name = f"tbl_{table_id:02x}_row_{rid}"
        if table_id == 0x01 and rid < len(dn.net.mdtables.TypeRef):
            name = str(dn.net.mdtables.TypeRef[rid].TypeName)
        elif table_id == 0x02 and rid < len(dn.net.mdtables.TypeDef):
            name = str(dn.net.mdtables.TypeDef[rid].TypeName)
        return name, pos
    return f'0x{b:02x}', pos

def decode_compressed(data, pos):
    b = data[pos]
    if (b & 0x80) == 0:
        return b, pos + 1
    elif (b & 0xC0) == 0x80:
        val = ((b & 0x3F) << 8) | data[pos+1]
        return val, pos + 2
    else:
        val = ((b & 0x1F) << 24) | (data[pos+1] << 16) | (data[pos+2] << 8) | data[pos+3]
        return val, pos + 4

for i, ms in enumerate(dn.net.mdtables.MethodSpec):
    tok = 0x2b000001 + i
    m_name = getattr(ms.Method.row, 'Name', '') if hasattr(ms.Method, 'row') else ''
    if m_name in ('GetExcelSheet', 'GetSubrowExcelSheet'):
        raw = bytes(ms.Instantiation.value) if ms.Instantiation else b''
        if len(raw) >= 2:
            count = raw[1]
            pos = 2
            args = []
            for _ in range(count):
                arg, pos = decode_sig_type(raw, pos)
                args.append(arg)
            types_str = ", ".join(args)
            print(f"{tok:08x}: {m_name}<{types_str}>")
