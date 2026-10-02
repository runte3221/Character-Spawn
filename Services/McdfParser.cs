using System;
using System.IO;
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
            // 1. Mare Content Delivery File (MCDF) 独自バイナリ形式の解析
            // フォーマット: 先頭付近に 'M','C','D','F' (4 bytes) + version (1 byte) + dataLength (4 bytes int) + JSON (UTF-8)
            using (var fs = File.OpenRead(filePath))
            {
                byte[] scanBuffer = new byte[Math.Min(2048, fs.Length)];
                int bytesRead = fs.Read(scanBuffer, 0, scanBuffer.Length);

                int mcdfOffset = -1;
                for (int i = 0; i <= bytesRead - 4; i++)
                {
                    if (scanBuffer[i] == (byte)'M' &&
                        scanBuffer[i + 1] == (byte)'C' &&
                        scanBuffer[i + 2] == (byte)'D' &&
                        scanBuffer[i + 3] == (byte)'F')
                    {
                        mcdfOffset = i;
                        break;
                    }
                }

                if (mcdfOffset >= 0 && mcdfOffset + 9 <= bytesRead)
                {
                    byte version = scanBuffer[mcdfOffset + 4];
                    int dataLength = BitConverter.ToInt32(scanBuffer, mcdfOffset + 5);

                    if (dataLength > 0 && dataLength < 100 * 1024 * 1024) // 100MB以下
                    {
                        fs.Seek(mcdfOffset + 9, SeekOrigin.Begin);
                        byte[] jsonBytes = new byte[dataLength];
                        int totalRead = 0;
                        while (totalRead < dataLength)
                        {
                            int read = fs.Read(jsonBytes, totalRead, dataLength - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead == dataLength)
                        {
                            string jsonStr = Encoding.UTF8.GetString(jsonBytes);
                            var jObj = JObject.Parse(jsonStr);

                            string? glamourerData = jObj["GlamourerData"]?.ToString();
                            string? desc = jObj["Description"]?.ToString();

                            if (!string.IsNullOrWhiteSpace(glamourerData))
                            {
                                log.Information($"Successfully parsed binary MCDF '{Path.GetFileName(filePath)}': GlamourerData length={glamourerData.Length}, Version={version}");
                                return new McdfData(glamourerData, null, desc);
                            }
                        }
                    }
                }
            }

            // 2. フォールバック: ZIPアーカイブ形式としての解析（古い形式や互換フォーマット）
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

                string? glamourerDesign = null;
                byte[]? customizeData = null;
                string? description = null;

                foreach (var entry in archive.Entries)
                {
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
                            else if (json["GlamourerData"] != null)
                            {
                                glamourerDesign = json["GlamourerData"]?.ToString();
                            }
                            else if (json["Designs"] != null || json["Design"] != null)
                            {
                                glamourerDesign = content;
                            }
                        }
                        catch
                        {
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

                if (!string.IsNullOrWhiteSpace(glamourerDesign))
                {
                    log.Information($"Parsed ZIP-based MCDF '{Path.GetFileName(filePath)}': GlamourerDesign length={glamourerDesign.Length}");
                    return new McdfData(glamourerDesign, customizeData, description);
                }
            }
            catch
            {
                // ZIPパース失敗時は無視
            }

            log.Warning($"No valid Glamourer data could be extracted from MCDF file: {filePath}");
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to parse MCDF file '{filePath}': {ex.Message}");
            return null;
        }
    }
}
