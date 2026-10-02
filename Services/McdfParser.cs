using System;
using System.Collections.Generic;
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

    public class McdfBundle
    {
        public string? GlamourerDesign { get; set; }
        public string? ManipulationData { get; set; }
        public string? CustomizePlusData { get; set; }
        public Dictionary<string, string> ModPaths { get; set; } = new(StringComparer.Ordinal);
        public string? Description { get; set; }
    }

    public McdfParser(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>
    /// AQR / Mare 準拠: MCDF から外見文字列および内包 Mod ファイル群を展開し、Penumbra 用のパス対応マップを生成する
    /// </summary>
    public McdfBundle? ExtractMcdfBundle(string filePath, string cacheDir)
    {
        if (!File.Exists(filePath))
        {
            log.Error($"MCDF file not found: {filePath}");
            return null;
        }

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);

            var headerChars = new string(reader.ReadChars(4));
            if (!string.Equals(headerChars, "MCDF", StringComparison.Ordinal))
            {
                log.Warning($"Invalid MCDF header in '{Path.GetFileName(filePath)}'");
                return null;
            }

            byte version = reader.ReadByte();
            int dataLength = reader.ReadInt32();
            if (dataLength <= 0 || dataLength > 100 * 1024 * 1024)
            {
                log.Warning($"Invalid MCDF data length {dataLength}");
                return null;
            }

            byte[] rawBytes = reader.ReadBytes(dataLength);
            string jsonStr = Encoding.UTF8.GetString(rawBytes);
            var jObj = JObject.Parse(jsonStr);

            var bundle = new McdfBundle
            {
                GlamourerDesign = jObj["GlamourerData"]?.ToString(),
                ManipulationData = jObj["ManipulationData"]?.ToString(),
                CustomizePlusData = jObj["CustomizePlusData"]?.ToString(),
                Description = jObj["Description"]?.ToString()
            };

            Directory.CreateDirectory(cacheDir);

            // 1. Files: ストリームに連続して格納されている実ファイルバイナリを展開
            var filesToken = jObj["Files"] as JArray;
            if (filesToken != null)
            {
                foreach (var fileItem in filesToken)
                {
                    var hash = fileItem["Hash"]?.ToString();
                    var length = fileItem["Length"]?.Value<int>() ?? 0;
                    var gamePaths = fileItem["GamePaths"]?.ToObject<System.Collections.Generic.List<string>>() ?? new();

                    if (length > 0)
                    {
                        var safeName = string.IsNullOrEmpty(hash) ? Guid.NewGuid().ToString("N") : hash;
                        var cachedFilePath = Path.Combine(cacheDir, safeName + ".tmp");

                        // ストリームから length バイト読み取り
                        byte[] fileBytes = reader.ReadBytes(length);
                        if (!File.Exists(cachedFilePath) || new FileInfo(cachedFilePath).Length != length)
                        {
                            File.WriteAllBytes(cachedFilePath, fileBytes);
                        }

                        foreach (var gp in gamePaths)
                        {
                            bundle.ModPaths[gp] = cachedFilePath;
                        }
                    }
                }
            }

            // 2. FileSwaps: ゲーム内パスの別名スワップ設定
            var swapsToken = jObj["FileSwaps"] as JArray;
            if (swapsToken != null)
            {
                foreach (var swapItem in swapsToken)
                {
                    var fileSwapPath = swapItem["FileSwapPath"]?.ToString();
                    var gamePaths = swapItem["GamePaths"]?.ToObject<System.Collections.Generic.List<string>>() ?? new();
                    if (!string.IsNullOrEmpty(fileSwapPath))
                    {
                        foreach (var gp in gamePaths)
                        {
                            bundle.ModPaths[gp] = fileSwapPath;
                        }
                    }
                }
            }

            log.Information($"Extracted MCDF '{Path.GetFileName(filePath)}': GlamourerLen={bundle.GlamourerDesign?.Length ?? 0}, ModFiles={bundle.ModPaths.Count}");
            return bundle;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to extract MCDF bundle from '{Path.GetFileName(filePath)}': {ex.Message}");
            return null;
        }
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
