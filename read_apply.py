import pefile

pe = pefile.PE(r'C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Glamourer.Api.dll')
# Find strings in dotnet metadata
with open(r'C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\Glamourer\1.7.1.3\Glamourer.Api.dll', 'rb') as f:
    raw = f.read()

# Search for ApplyFlagEx enum fields
import re
for m in re.finditer(rb'ApplyFlagEx', raw):
    idx = m.start()
    print("Found ApplyFlagEx at", idx)
    print(repr(raw[idx-50:idx+300]))
