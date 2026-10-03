import dnfile, os, sys

sys.stdout.reconfigure(encoding='utf-8')
dll_path = os.path.expandvars(r'%APPDATA%\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll')
dn = dnfile.dnPE(dll_path)

for i, ms in enumerate(dn.net.mdtables.MethodSpec):
    tok = 0x2b000001 + i
    m = ms.Method
    m_name = ""
    if hasattr(m, 'row'):
        m_name = str(getattr(m.row, 'Name', ''))
    sig = ms.Instantiation
    print(f"{tok:08x}: Method={m_name} sig={sig.hex() if sig else ''}")
