import urllib.request, json, time
url = "https://api.github.com/repos/runte3221/Character-Spawn/actions/runs?per_page=1"
req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
for i in range(25):
    try:
        data = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
        run = data["workflow_runs"][0]
        print(f"[{i+1}] ID: {run['id']}, Status: {run['status']}, Conclusion: {run['conclusion']}")
        if run["status"] == "completed":
            break
    except Exception as e:
        print("Error:", e)
    time.sleep(8)
