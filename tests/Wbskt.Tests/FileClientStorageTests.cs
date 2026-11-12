namespace Wbskt.Tests;

public sealed class FileClientStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wbskt-storage-{Guid.NewGuid():N}");

    [Fact]
    public async Task Credentials_saved_are_loaded_back_and_cleared()
    {
        var storage = new FileClientStorage(Path.Combine(_dir, "nested", "client.json"));
        var refId = Guid.NewGuid();

        Assert.Equal((null, null), await storage.LoadCredentialsAsync());

        await storage.SaveCredentialsAsync(refId, "s3cret");
        Assert.Equal((refId, "s3cret"), await storage.LoadCredentialsAsync());

        await storage.ClearCredentialsAsync();
        Assert.Equal((null, null), await storage.LoadCredentialsAsync());
    }

    [Fact]
    public async Task The_file_uses_the_protocols_names_as_the_python_sdk_does()
    {
        string path = Path.Combine(_dir, "client.json");
        var refId = Guid.NewGuid();

        await new FileClientStorage(path).SaveCredentialsAsync(refId, "s3cret");

        Assert.Equal($$"""{"clientRefId":"{{refId}}","secret":"s3cret"}""", await File.ReadAllTextAsync(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public async Task A_file_the_python_sdk_or_an_older_version_wrote_is_read()
    {
        Directory.CreateDirectory(_dir);
        var refId = Guid.NewGuid();
        string python = Path.Combine(_dir, "python.json");
        string older = Path.Combine(_dir, "older.json");
        await File.WriteAllTextAsync(python, $$"""{"clientRefId": "{{refId}}", "secret": "from-python"}""");
        await File.WriteAllTextAsync(older, $$"""{"RefId":"{{refId}}","Secret":"from-0.9.0-beta.1"}""");

        Assert.Equal((refId, "from-python"), await new FileClientStorage(python).LoadCredentialsAsync());
        Assert.Equal((refId, "from-0.9.0-beta.1"), await new FileClientStorage(older).LoadCredentialsAsync());
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_no_credentials()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "client.json");
        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Equal((null, null), await new FileClientStorage(path).LoadCredentialsAsync());
    }

    [Fact]
    public async Task Each_client_name_gets_its_own_file()
    {
        string a = $"sensor-{Guid.NewGuid():N}";
        string b = $"sensor/{Guid.NewGuid():N}";
        var first = FileClientStorage.ForClient(a);
        var second = FileClientStorage.ForClient(b);
        var refId = Guid.NewGuid();

        try
        {
            await first.SaveCredentialsAsync(refId, "one");
            Assert.Equal((null, null), await second.LoadCredentialsAsync());
        }
        finally
        {
            await first.ClearCredentialsAsync();
            await second.ClearCredentialsAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void A_client_name_maps_to_the_file_name_the_Python_SDK_uses()
    {
        Assert.Equal("pi_kitchen__1_", FileClientStorage.SafeFileName("pi/kitchen:\"1\""));
        Assert.Equal("a_b_c_d_e_f_g", FileClientStorage.SafeFileName("a*b?c|d<e>f\\g"));
        Assert.Equal("tab_here", FileClientStorage.SafeFileName("tab\there"));
        Assert.Equal("greenhouse-sensor", FileClientStorage.SafeFileName("greenhouse-sensor"));
    }

    [Fact]
    public async Task A_temporary_file_left_by_an_interrupted_save_does_not_block_the_next()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wbskt-{Guid.NewGuid():N}.json");
        var storage = new FileClientStorage(path);
        var refId = Guid.NewGuid();

        try
        {
            await File.WriteAllTextAsync(path + ".tmp", "partial");
            await storage.SaveCredentialsAsync(refId, "secret");
            Assert.Equal((refId, "secret"), await storage.LoadCredentialsAsync());
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            await storage.ClearCredentialsAsync();
        }
    }
}
