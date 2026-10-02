using System.IO.Compression;
using System.Text;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

namespace CharacterSpawn.Services;

public class McdfParser
{
    private readonly IPluginLog log;

    public record McdfData(string? GlamourerDesign, byte[]? CustomizeData, string? Description);

    public McdfParser(IPluginLog log)
    {
        this.log = log;
    }

    public McdfData? ParseMcdf(string filePath)
    {
        if (!File.Exists(filePath))
        {
            log.Error($"MCDF file not found: {filePath}");
            return null;
        }

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            string? glamourerDesign = null;
            byte[]? customizeData = null;
            string? description = null;

            foreach (var entry in archive.Entries)
            {
                // Mare chara data usually contains glamourer.json or manifest
                if (entry.Name.Equals("glamourer.json", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, Encoding.UTF8);
                    var content = reader.ReadToEnd();

                    try
                    {
                        var json = JToken.Parse(content);
                        if (json["CustomizationCode"] != null)
                        {
                            glamourerDesign = json["CustomizationCode"]?.ToString();
                        }
                        else if (json["Designs"] != null || json["Design"] != null)
                        {
                            glamourerDesign = content;
                        }
                    }
                    catch
                    {
                        // Fallback to raw string
                        glamourerDesign = content;
                    }
                }
                else if (entry.Name.Equals("chara.data", StringComparison.OrdinalIgnoreCase) ||
                         entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream();
                    using var entryStream = entry.Open();
                    entryStream.CopyTo(ms);
                    customizeData = ms.ToArray();
                }
                else if (entry.Name.Equals("meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, Encoding.UTF8);
                    description = reader.ReadToEnd();
                }
            }

            return new McdfData(glamourerDesign, customizeData, description);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to parse MCDF archive: {ex.Message}");
            return null;
        }
    }
}
