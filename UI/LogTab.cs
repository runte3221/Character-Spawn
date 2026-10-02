using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Managers;

namespace CharacterSpawn.UI;

public class LogTab
{
    private readonly LogManager logManager;
    private bool autoScroll = true;
    private string filterQuery = string.Empty;

    public LogTab(LogManager logManager)
    {
        this.logManager = logManager;
    }

    public void Draw()
    {
        ImGui.BeginGroup();

        // Control bar: [ Clear ] [ Copy All ] [x] Auto-scroll  Search: [...]
        if (ImGui.Button("Clear"))
        {
            logManager.Clear();
        }

        ImGui.SameLine();

        if (ImGui.Button("Copy All"))
        {
            var sb = new StringBuilder();
            foreach (var entry in logManager.GetEntries())
            {
                sb.AppendLine($"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
            }
            ImGui.SetClipboardText(sb.ToString());
        }

        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref autoScroll);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##LogFilter", "Filter logs...", ref filterQuery, 64);

        ImGui.EndGroup();

        ImGui.Separator();

        // Log Entries Scroll Area
        if (ImGui.BeginChild("LogScrollRegion", new Vector2(-1, -1), true))
        {
            var entries = logManager.GetEntries();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(filterQuery) &&
                    !entry.Message.Contains(filterQuery, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Level.ToString().Contains(filterQuery, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var color = entry.Level switch
                {
                    LogManager.LogLevel.Error => new Vector4(1.0f, 0.4f, 0.4f, 1.0f),
                    LogManager.LogLevel.Warning => new Vector4(1.0f, 0.9f, 0.3f, 1.0f),
                    LogManager.LogLevel.Debug => new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    _ => new Vector4(0.9f, 0.9f, 0.9f, 1.0f)
                };

                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"[{entry.Timestamp:HH:mm:ss.fff}]");
                ImGui.SameLine();
                ImGui.TextColored(color, $"[{entry.Level,-5}] {entry.Message}");
            }

            if (autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }

            ImGui.EndChild();
        }
    }
}
