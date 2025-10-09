using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExocadDailyExporter.Models;

namespace ExocadDailyExporter.Services;

public class ImportService : IDisposable
{
    private static readonly string[] AllowedPatterns =
    {
        "*_crown_cad.stl",
        "*_fullcontour_cad.stl",
        "*_reduced_cad.stl",
        "*_coping_cad.stl",
        "*_veneer_cad.stl",
        "*_inlay_cad.stl",
        "*_onlay_cad.stl",
        "*_overlay_cad.stl",
        "*bridge*_cad.stl",
        "*_pontic_cad.stl",
        "*_abutment_cad.stl",
        "*_meso_cad.stl",
        "*_crown_implant_cad.stl",
        "*_sleeve_cad.stl",
        "*_tibase_cad.stl",
        "*_bar_cad.stl",
        "*_provisional_cad.stl",
        "*_temp_cad.stl",
        "*_longterm_temp_cad.stl",
        "*_splint_cad.stl",
        "*_bite_splint_cad.stl",
        "*_setup_cad.stl",
        "*_tryin_cad.stl",
        "*_base_cad.stl",
        "*_denture_cad.stl",
        "*_gingiva_cad.stl",
        "*_primary_telescopic_cad.stl",
        "*_secondary_telescopic_cad.stl",
        "*_post_cad.stl",
        "*_core_cad.stl",
        "*_rpd_cad.stl",
        "*_framework_cad.stl",
        "*_ramp_cad.stl",
        "*.constructionInfo"
    };

    private readonly Config _config;
    private readonly Logger _logger;
    private readonly TimeSpan _stabilizeDuration;
    private readonly HashSet<string> _activeCases = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _caseLock = new();
    private readonly CancellationTokenSource _cts = new();

    public ImportService(Config config, Logger logger)
    {
        _config = config;
        _logger = logger;
        _stabilizeDuration = TimeSpan.FromSeconds(Math.Max(config.StabilizeSeconds, 5));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public CancellationToken Token => _cts.Token;

    public bool IsAllowedFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (IsExcluded(fileName))
        {
            return false;
        }

        return AllowedPatterns.Any(pattern => WildcardMatch(pattern, fileName));
    }

    public bool IsExcluded(string fileName)
    {
        foreach (var pattern in _config.ExcludePatterns)
        {
            if (WildcardMatch(pattern, fileName))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<int> ImportTodayAsync(CancellationToken cancellationToken)
    {
        var todayStart = DateTime.Today;
        var processed = 0;
        if (!Directory.Exists(_config.SourceRoot))
        {
            _logger.Warn($"源目录不存在：{_config.SourceRoot}");
            return 0;
        }

        var caseDirectories = GetCaseDirectories(_config.SourceRoot);
        foreach (var caseDir in caseDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CaseHasDueFiles(caseDir, todayStart))
            {
                await ImportCaseDirectoryAsync(caseDir, cancellationToken).ConfigureAwait(false);
                processed++;
            }
        }

        return processed;
    }

    public Task ImportCaseFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        var caseDirectory = GetCaseDirectoryForFile(filePath);
        if (caseDirectory == null)
        {
            _logger.Warn($"无法确定病例目录：{filePath}");
            return Task.CompletedTask;
        }

        return ImportCaseDirectoryAsync(caseDirectory, cancellationToken);
    }

    public async Task ImportCaseDirectoryAsync(string caseDirectory, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(caseDirectory);
        if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath))
        {
            _logger.Warn($"病例目录不存在：{caseDirectory}");
            return;
        }

        lock (_caseLock)
        {
            if (!_activeCases.Add(fullPath))
            {
                return;
            }
        }

        try
        {
            await Task.Run(() => ImportCaseCore(fullPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_caseLock)
            {
                _activeCases.Remove(fullPath);
            }
        }
    }

    private void ImportCaseCore(string caseDirectory, CancellationToken cancellationToken)
    {
        var todayStart = DateTime.Today;
        var files = EnumerateAllowedFiles(caseDirectory)
            .Where(file => ShouldImportFile(file, todayStart));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryCopyFile(file);
        }
    }

    private IEnumerable<string> EnumerateAllowedFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error(ex, $"枚举文件失败：{root}");
            yield break;
        }

        foreach (var file in files)
        {
            if (IsAllowedFile(file))
            {
                yield return file;
            }
        }
    }

    private bool CaseHasDueFiles(string caseDirectory, DateTime todayStart)
    {
        foreach (var file in EnumerateAllowedFiles(caseDirectory))
        {
            if (ShouldImportFile(file, todayStart))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldImportFile(string filePath, DateTime todayStart)
    {
        try
        {
            var info = new FileInfo(filePath);
            var lastWrite = info.LastWriteTime;
            if (lastWrite < todayStart)
            {
                return false;
            }

            var diff = DateTime.Now - lastWrite;
            return diff >= _stabilizeDuration;
        }
        catch (IOException ex)
        {
            _logger.Error(ex, $"读取文件时间失败：{filePath}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error(ex, $"读取文件时间失败：{filePath}");
            return false;
        }
    }

    private void TryCopyFile(string sourcePath)
    {
        var relativePath = Path.GetRelativePath(_config.SourceRoot, sourcePath);
        if (relativePath.StartsWith(".."))
        {
            _logger.Warn($"忽略越界文件：{sourcePath}");
            return;
        }
        var today = DateTime.Today;
        var targetRoot = Path.Combine(_config.ArchiveRoot, today.ToString("yyyy"), today.ToString("yyyy-MM-dd"));
        var targetPath = Path.Combine(targetRoot, relativePath);

        var sourceInfo = new FileInfo(sourcePath);
        var targetInfo = new FileInfo(targetPath);
        if (targetInfo.Exists && targetInfo.LastWriteTime >= sourceInfo.LastWriteTime)
        {
            return;
        }

        var directory = targetInfo.DirectoryName;
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        const int maxAttempts = 3;
        var attempt = 0;
        var delay = TimeSpan.FromSeconds(1);
        while (attempt < maxAttempts)
        {
            attempt++;
            try
            {
                using var sourceStream = new FileStream(NormalizeLongPath(sourcePath), FileMode.Open, FileAccess.Read, FileShare.Read);
                using var targetStream = new FileStream(NormalizeLongPath(targetPath), FileMode.Create, FileAccess.Write, FileShare.None);
                sourceStream.CopyTo(targetStream);
                targetStream.Flush(true);
                File.SetLastWriteTime(targetPath, sourceInfo.LastWriteTime);
                File.SetCreationTime(targetPath, sourceInfo.CreationTime);
                _logger.Info($"已复制：{relativePath}");
                return;
            }
            catch (IOException ex)
            {
                _logger.Warn($"复制失败（{attempt}/{maxAttempts}）：{relativePath}，原因：{ex.Message}");
                if (attempt >= maxAttempts)
                {
                    _logger.Error(ex, $"复制文件失败：{sourcePath}");
                    return;
                }

                Thread.Sleep(delay);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Error(ex, $"无权限复制：{sourcePath}");
                return;
            }
        }
    }

    private static string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private string? GetCaseDirectoryForFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(_config.SourceRoot))
        {
            return null;
        }

        try
        {
            var fullSourceRoot = Path.GetFullPath(_config.SourceRoot);
            var fullFile = Path.GetFullPath(filePath);
            if (!fullFile.StartsWith(fullSourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = Path.GetRelativePath(fullSourceRoot, fullFile);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Length == 0)
            {
                return null;
            }

            var caseFolder = segments[0];
            return Path.Combine(fullSourceRoot, caseFolder);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> GetCaseDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            yield return directory;
        }
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        if (pattern == null)
        {
            return false;
        }

        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string NormalizeLongPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return path;
        }

        if (path.StartsWith("\\\\?\\"))
        {
            return path;
        }

        if (path.Length >= 260)
        {
            if (Path.IsPathRooted(path))
            {
                return "\\\\?\\" + path;
            }
        }

        return path;
    }
}
