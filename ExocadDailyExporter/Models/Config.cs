using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExocadDailyExporter.Models;

public class Config
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true
    };

    public string SourceRoot { get; set; } = "D:/Exocad-Allinone-3.0/CAD/exocad-DentalCAD3.0-2021-03-25/CAD-Data";

    public string ArchiveRoot { get; set; } = string.Empty;

    public int StabilizeSeconds { get; set; } = 30;

    public string DailyTime { get; set; } = "19:00";

    public bool RunAtStartup { get; set; } = true;

    public int KeepLogDays { get; set; } = 30;

    public List<string> ExcludePatterns { get; set; } = new()
    {
        "crashreport*",
        "*preview*.stl",
        "*screenshot*.png"
    };

    [JsonIgnore]
    public static string ConfigFileName => "appsettings.json";

    [JsonIgnore]
    public static string AppDirectory => AppContext.BaseDirectory;

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(AppDirectory, ConfigFileName);

    public static Config Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            var config = new Config();
            config.Save();
            return config;
        }

        using var stream = File.OpenRead(path);
        var configFromFile = JsonSerializer.Deserialize<Config>(stream, SerializerOptions) ?? new Config();
        configFromFile.Normalize();
        return configFromFile;
    }

    public void Save()
    {
        Normalize();
        var path = ConfigPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
    }

    public TimeSpan GetDailyTimeOfDay()
    {
        if (TimeSpan.TryParse(DailyTime, out var time))
        {
            return time;
        }

        return new TimeSpan(19, 0, 0);
    }

    private void Normalize()
    {
        SourceRoot = NormalizePath(SourceRoot);
        ArchiveRoot = NormalizePath(ArchiveRoot);
        if (StabilizeSeconds < 5)
        {
            StabilizeSeconds = 5;
        }

        if (KeepLogDays < 1)
        {
            KeepLogDays = 1;
        }

        DailyTime = GetDailyTimeOfDay().ToString("HH\:mm");
        ExcludePatterns = ExcludePatterns?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList() ?? new List<string>();
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
