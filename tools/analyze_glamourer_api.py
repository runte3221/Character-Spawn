import dnfile, os, sys

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\Glamourer.Api.dll')
dn = dnfile.dnPE(dll_path)
us = dn.net.metadata.streams.get(b'#US')

def inspect_type(name):
    td = next(r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == name)
    print(f"=== TypeDef: {td.TypeNamespace}.{td.TypeName} ===")
    for md in td.MethodList:
        print(f"  Method: {md.row.Name}")
        if md.row.Rva:
            offset = dn.get_offset_from_rva(md.row.Rva)
            hb = dn.__data__[offset]
            code_size = (hb >> 2) if (hb & 3) == 2 else int.from_bytes(dn.__data__[offset+4:offset+8], 'little')
            header_size = 1 if (hb & 3) == 2 else (int.from_bytes(dn.__data__[offset:offset+2], 'little') >> 12) * 4
            code = dn.__data__[offset+header_size:offset+header_size+code_size]
            i = 0
            while i < len(code):
                op = code[i]
                i += 1
                if op == 0x72:
                    tok = int.from_bytes(code[i:i+4], 'little')
                    i += 4
                    print(f"    ldstr: {us.get(tok & 0xFFFFFF)}")
                elif op in (0x28, 0x6f, 0x73, 0x7b, 0x7e, 0x7d, 0x80, 0x20, 0x25, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44, 0x14, 0x15, 0x16):
                    i += 4
                elif op in (0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x1f, 0x0e, 0x10):
                    i += 1
                elif op in (0x11, 0x12, 0x13):
                    i += 2

def print_method_sig(md):
    # parse signature blob
    blob = md.row.Signature.value
    print(f"    Signature raw: {list(blob)}")

for name in ('GetState', 'ApplyState', 'RevertState'):
    td = next(r for r in dn.net.mdtables.TypeDef if str(r.TypeName) == name)
    print(f"=== {name} Methods ===")
    for md in td.MethodList:
        print(f"  {md.row.Name}:")
        print_method_sig(md)

