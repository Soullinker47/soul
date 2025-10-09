using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExocadDailyExporter.Models;
using ExocadDailyExporter.Services;
using Xunit;

namespace ExocadDailyExporter.Tests;

public class ImportServiceTests
{
    private static Config CreateConfig(string sourceRoot, string archiveRoot, int stabilizeSeconds = 1)
    {
        return new Config
        {
            SourceRoot = sourceRoot,
            ArchiveRoot = archiveRoot,
            StabilizeSeconds = stabilizeSeconds,
            DailyTime = "19:00",
            RunAtStartup = false,
            KeepLogDays = 7,
            ExcludePatterns = new List<string> { "crashreport*", "*preview*.stl", "*screenshot*.png" }
        };
    }

    [Fact]
    public void AllowedPatternsMatch()
    {
        var config = CreateConfig("C:/temp", "C:/out");
        var logger = new Logger(7);
        using var service = new ImportService(config, logger);

        Assert.True(service.IsAllowedFile(Path.Combine(config.SourceRoot, "case", "tooth_crown_cad.stl")));
        Assert.True(service.IsAllowedFile(Path.Combine(config.SourceRoot, "case", "test.constructionInfo")));
        Assert.False(service.IsAllowedFile(Path.Combine(config.SourceRoot, "case", "model.dentalProject")));
        Assert.False(service.IsAllowedFile(Path.Combine(config.SourceRoot, "case", "mesh_preview_cad.stl")));
    }

    [Fact]
    public void ExcludePatternsWin()
    {
        var config = CreateConfig("C:/temp", "C:/out");
        config.ExcludePatterns.Add("*_tmp.stl");
        var logger = new Logger(7);
        using var service = new ImportService(config, logger);

        Assert.False(service.IsAllowedFile(Path.Combine(config.SourceRoot, "case", "demo_tmp.stl")));
    }

    [Fact]
    public async Task ImportTodayCopiesFiles()
    {
        var source = CreateTempDirectory();
        var archive = CreateTempDirectory();
        try
        {
            var caseDir = Path.Combine(source, "Case001");
            Directory.CreateDirectory(caseDir);
            var file = Path.Combine(caseDir, "sample_crown_cad.stl");
            await File.WriteAllTextAsync(file, "hello");
            File.SetLastWriteTime(file, DateTime.Now.AddMinutes(-5));

            var config = CreateConfig(source, archive);
            var logger = new Logger(7);
            using var service = new ImportService(config, logger);

            var count = await service.ImportTodayAsync(CancellationToken.None);
            Assert.Equal(1, count);

            var today = DateTime.Today;
            var target = Path.Combine(archive, today.ToString("yyyy"), today.ToString("yyyy-MM-dd"), "Case001", "sample_crown_cad.stl");
            Assert.True(File.Exists(target));
        }
        finally
        {
            TryDelete(source);
            TryDelete(archive);
        }
    }

    [Fact]
    public async Task NewerTargetIsNotOverwritten()
    {
        var source = CreateTempDirectory();
        var archive = CreateTempDirectory();
        try
        {
            var caseDir = Path.Combine(source, "Case002");
            Directory.CreateDirectory(caseDir);
            var file = Path.Combine(caseDir, "bridge_cad.stl");
            await File.WriteAllTextAsync(file, "source v1");
            File.SetLastWriteTime(file, DateTime.Now.AddMinutes(-10));

            var config = CreateConfig(source, archive);
            var logger = new Logger(7);
            using var service = new ImportService(config, logger);

            await service.ImportCaseDirectoryAsync(caseDir, CancellationToken.None);

            var today = DateTime.Today;
            var target = Path.Combine(archive, today.ToString("yyyy"), today.ToString("yyyy-MM-dd"), "Case002", "bridge_cad.stl");
            Assert.True(File.Exists(target));

            await File.WriteAllTextAsync(target, "target v2");
            File.SetLastWriteTime(target, DateTime.Now);

            await service.ImportCaseDirectoryAsync(caseDir, CancellationToken.None);
            var content = await File.ReadAllTextAsync(target);
            Assert.Equal("target v2", content);
        }
        finally
        {
            TryDelete(source);
            TryDelete(archive);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ExocadDailyExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}
