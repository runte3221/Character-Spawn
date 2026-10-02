using Dalamud.Plugin.Services;

namespace CharacterSpawn.Managers;

public class LogManager
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public record LogEntry(DateTime Timestamp, LogLevel Level, string Message);

    private readonly IPluginLog pluginLog;
    private readonly List<LogEntry> entries = new();
    private readonly object lockObj = new();
    private const int MaxEntries = 1000;

    public LogManager(IPluginLog pluginLog)
    {
        this.pluginLog = pluginLog;
    }

    public void Add(LogLevel level, string message)
    {
        lock (lockObj)
        {
            if (entries.Count >= MaxEntries)
            {
                entries.RemoveAt(0);
            }
            entries.Add(new LogEntry(DateTime.Now, level, message));
        }

        // また PluginLog にも出力
        switch (level)
        {
            case LogLevel.Debug:
                pluginLog.Debug(message);
                break;
            case LogLevel.Info:
                pluginLog.Information(message);
                break;
            case LogLevel.Warning:
                pluginLog.Warning(message);
                break;
            case LogLevel.Error:
                pluginLog.Error(message);
                break;
        }
    }

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Warning(string message) => Add(LogLevel.Warning, message);
    public void Error(string message) => Add(LogLevel.Error, message);
    public void Debug(string message) => Add(LogLevel.Debug, message);

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (lockObj)
        {
            return entries.ToList();
        }
    }

    public void Clear()
    {
        lock (lockObj)
        {
            entries.Clear();
        }
    }
}
