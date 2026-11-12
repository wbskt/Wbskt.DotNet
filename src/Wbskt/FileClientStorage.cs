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

    public FileClientStorage(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// The file for a client named <paramref name="clientName"/>: <c>wbskt/{name}.json</c> in the
    /// user's local application data folder (<c>~/.local/share</c> on Linux), or the working
    /// directory where there is none. Named per client, so several on one machine don't share one.
    /// </summary>
    public static FileClientStorage ForClient(string clientName)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root))
        {
            root = Directory.GetCurrentDirectory();
        }

        string directory = Path.Combine(root, "wbskt");
        string path = Path.Combine(directory, $"{SafeFileName(clientName)}.json");

        // Before 0.9.0-beta.2 only the platform's invalid characters were replaced; carry such a file over.
        string legacy = Path.Combine(directory, $"{string.Concat(clientName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))}.json");
        if (legacy != path && File.Exists(legacy) && !File.Exists(path))
        {
            File.Move(legacy, path);
        }

        return new FileClientStorage(path);
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
        // this user only, so the secret is never on disk with wider permissions.
        string temporary = _filePath + ".tmp";
        File.Delete(temporary);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(temporary, options))
        {
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json));
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, _filePath, overwrite: true);
    }

    public async Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync()
    {
        if (!File.Exists(_filePath))
        {
            return (null, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
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
        File.Delete(_filePath + ".tmp");

        return Task.CompletedTask;
    }
}
