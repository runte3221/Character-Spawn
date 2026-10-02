using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Dalamud.Plugin.Services;
using LZ4;
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

        // 1. AQR & Mare 黄金パターン: LZ4 圧縮ストリームを解凍して MCDF ヘッダと JSON データを読み取る
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);

            var headerChars = new string(reader.ReadChars(4));
            if (string.Equals(headerChars, "MCDF", StringComparison.Ordinal))
            {
                byte version = reader.ReadByte();
                if (version == 1)
                {
                    int dataLength = reader.ReadInt32();
                    if (dataLength > 0 && dataLength < 100 * 1024 * 1024)
                    {
                        byte[] rawBytes = reader.ReadBytes(dataLength);
                        string jsonStr = Encoding.UTF8.GetString(rawBytes);
                        var jObj = JObject.Parse(jsonStr);

                        string? glamourerData = jObj["GlamourerData"]?.ToString();
                        string? desc = jObj["Description"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(glamourerData))
                        {
                            log.Information($"Successfully parsed LZ4-compressed MCDF '{Path.GetFileName(filePath)}': GlamourerData length={glamourerData.Length}, Version={version}");
                            return new McdfData(glamourerData, null, desc);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug($"LZ4 decompression failed for '{Path.GetFileName(filePath)}': {ex.Message}. Trying uncompressed binary fallback.");
        }

        // 2. 非圧縮 MCDF バイナリフォールバック（ファイル先頭に "MCDF" が平文で存在する場合）
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var reader = new BinaryReader(fileStream);

            var headerChars = new string(reader.ReadChars(4));
            if (string.Equals(headerChars, "MCDF", StringComparison.Ordinal))
            {
                byte version = reader.ReadByte();
                if (version == 1)
                {
                    int dataLength = reader.ReadInt32();
                    if (dataLength > 0 && dataLength < 100 * 1024 * 1024)
                    {
                        byte[] rawBytes = reader.ReadBytes(dataLength);
                        string jsonStr = Encoding.UTF8.GetString(rawBytes);
                        var jObj = JObject.Parse(jsonStr);

                        string? glamourerData = jObj["GlamourerData"]?.ToString();
                        string? desc = jObj["Description"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(glamourerData))
                        {
                            log.Information($"Successfully parsed uncompressed MCDF '{Path.GetFileName(filePath)}': GlamourerData length={glamourerData.Length}");
                            return new McdfData(glamourerData, null, desc);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug($"Uncompressed binary fallback failed for '{Path.GetFileName(filePath)}': {ex.Message}");
        }

        // 3. ZIP アーカイブフォールバック
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Equals("glamourer.json", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string json = reader.ReadToEnd();
                    var jObj = JObject.Parse(json);
                    string? glamourerData = jObj["GlamourerData"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(glamourerData))
                    {
                        log.Information($"Successfully parsed ZIP-based MCDF '{Path.GetFileName(filePath)}'");
                        return new McdfData(glamourerData, null, null);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"All parsing attempts failed for MCDF '{Path.GetFileName(filePath)}': {ex.Message}");
        }

        return null;
    }
}
