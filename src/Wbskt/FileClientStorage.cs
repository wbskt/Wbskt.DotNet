using System.Text;
using System.Text.Json;

namespace Wbskt;

/// <summary>
/// Keeps the client's credentials in a JSON file, so it registers once and signs in with them on
/// every later start. The default for <see cref="WbsktClient(string, string)"/>.
/// </summary>
/// <remarks>
/// The file is <c>{"clientRefId": ..., "secret": ...}</c>, the protocol's own names and the same file the
/// Python SDK writes at the same path, so a client moved from one SDK to the other keeps its credentials.
/// A file from before 0.9.0-beta.2 (<c>RefId</c>, <c>Secret</c>) is still read.
/// </remarks>
public sealed class FileClientStorage : IClientStorage
{
    private readonly string _filePath;
    private readonly string? _legacyPath;

    public FileClientStorage(string filePath)
        : this(filePath, null)
    {
    }

    private FileClientStorage(string filePath, string? legacyPath)
    {
        _filePath = filePath;
        _legacyPath = legacyPath;
    }

    /// <summary>
    /// The file for a client named <paramref name="clientName"/>: <c>wbskt/{name}.json</c> in the
    /// user's local application data folder (<c>~/.local/share</c> on Linux), or the working
    /// directory where there is none. Named per client, so several on one machine don't share one.
    /// </summary>
    /// <remarks>
    /// <c>\ / : * ? " &lt; &gt; |</c> and control characters become <c>_</c> on every platform, as the
    /// Python SDK does, so names differing only in those (<c>a:b</c>, <c>a*b</c>) share a file.
    /// </remarks>
    public static FileClientStorage ForClient(string clientName)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root))
        {
            root = Directory.GetCurrentDirectory();
        }

        return ForClient(clientName, root);
    }

    internal static FileClientStorage ForClient(string clientName, string root)
    {
        string directory = Path.Combine(root, "wbskt");
        string path = Path.Combine(directory, $"{SafeFileName(clientName)}.json");

        // Before 0.9.0-beta.2 only the platform's invalid characters were replaced.
        string legacy = Path.Combine(directory, $"{string.Concat(clientName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))}.json");
        return new FileClientStorage(path, legacy == path ? null : legacy);
    }

    /// <summary>
    /// The same replacement on every platform, matching the Python SDK, so both find one file for a name.
    /// </summary>
    internal static string SafeFileName(string clientName) =>
        string.Concat(clientName.Select(c => c < 0x20 || "\\/:*?\"<>|".Contains(c) ? '_' : c));

    public async Task SaveCredentialsAsync(Guid refId, string secret)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["clientRefId"] = refId.ToString("D"), ["secret"] = secret });

        // Written beside it and moved over it, so a crash never leaves half a file. Created readable by
        // this user only, so the secret is never on disk with wider permissions; named per save, so two
        // processes saving at once don't trip over each other.
        string temporary = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync()
    {
        string path = FindExisting();
        if (!File.Exists(path))
        {
            return (null, null);
        }

        // Older versions wrote it with the default mode, readable by other users.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var refId = (root.TryGetProperty("clientRefId", out var id) ? id : root.GetProperty("RefId")).GetGuid();
            var secret = (root.TryGetProperty("secret", out var s) ? s : root.GetProperty("Secret")).GetString();
            return (refId, secret);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    public Task ClearCredentialsAsync()
    {
        File.Delete(_filePath);
        if (_legacyPath is not null)
        {
            File.Delete(_legacyPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The file to read: this one, or a file an older version named differently, moved here. Where the
    /// move fails (another process got there first, a read-only folder) the old file is read in place.
    /// </summary>
    private string FindExisting()
    {
        if (_legacyPath is null || File.Exists(_filePath) || !File.Exists(_legacyPath))
        {
            return _filePath;
        }

        try
        {
            File.Move(_legacyPath, _filePath);
            return _filePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return File.Exists(_filePath) ? _filePath : _legacyPath;
        }
    }
}
