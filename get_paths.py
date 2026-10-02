with open(r'C:\Users\RYO\.gemini\antigravity\brain\4d997fbe-63ff-470b-aca5-4ca456f4cb12\.system_generated\steps\7524\content.md', 'r', encoding='utf-8') as f:
    text = f.read()
import re
paths = re.findall(r'\"path\":\"(Brio/[^\"]+)\"', text)
for p in sorted(set(paths)):
    print(p)
