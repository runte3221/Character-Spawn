with open(r'C:\Users\RYO\AppData\Roaming\XIVLauncher\installedPlugins\HDM\1.0.3.0\HDM.dll', 'rb') as f:
    data = f.read()

# Search for calls to DeleteObjectByIndex
import re
idx = 0
while True:
    idx = data.find(b'DeleteObjectByIndex', idx)
    if idx == -1:
        break
    print(f"Found at {idx}")
    print(repr(data[max(0, idx-100):min(len(data), idx+100)]))
    idx += 1
