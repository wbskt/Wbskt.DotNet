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
        var first = FileClientStorage.ForClient(a, _dir);
        var second = FileClientStorage.ForClient(b, _dir);
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

    [Fact]
    public void A_client_name_maps_to_the_file_name_the_Python_SDK_uses()
    {
        Assert.Equal("pi_kitchen__1_", FileClientStorage.SafeFileName("pi/kitchen:\"1\""));
        Assert.Equal("a_b_c_d_e_f_g", FileClientStorage.SafeFileName("a*b?c|d<e>f\\g"));
        Assert.Equal("tab_here", FileClientStorage.SafeFileName("tab\there"));
        Assert.Equal("greenhouse-sensor", FileClientStorage.SafeFileName("greenhouse-sensor"));
    }

    [Fact]
    public async Task Saves_leave_no_temporary_file_behind()
    {
        string path = Path.Combine(_dir, "client.json");
        var storage = new FileClientStorage(path);
        var refId = Guid.NewGuid();

        await Task.WhenAll(storage.SaveCredentialsAsync(refId, "one"), storage.SaveCredentialsAsync(refId, "two"));

        Assert.Equal(refId, (await storage.LoadCredentialsAsync()).RefId);
        Assert.Equal(["client.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_file_an_older_version_named_differently_is_moved_and_made_private()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = Path.Combine(_dir, "wbskt");
        Directory.CreateDirectory(folder);
        string legacy = Path.Combine(folder, "pi:kitchen.json");
        var refId = Guid.NewGuid();
        await File.WriteAllTextAsync(legacy, $$"""{"RefId":"{{refId}}","Secret":"old"}""");
        File.SetUnixFileMode(legacy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var storage = FileClientStorage.ForClient("pi:kitchen", _dir);

        Assert.Equal((refId, "old"), await storage.LoadCredentialsAsync());
        string moved = Path.Combine(folder, "pi_kitchen.json");
        Assert.False(File.Exists(legacy));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(moved));
    }

    [Fact]
    public async Task A_file_under_the_new_name_wins_over_an_older_one()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = Path.Combine(_dir, "wbskt");
        Directory.CreateDirectory(folder);
        var refId = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(folder, "pi:kitchen.json"), $$"""{"RefId":"{{Guid.NewGuid()}}","Secret":"old"}""");
        await File.WriteAllTextAsync(Path.Combine(folder, "pi_kitchen.json"), $$"""{"clientRefId":"{{refId}}","secret":"new"}""");

        Assert.Equal((refId, "new"), await FileClientStorage.ForClient("pi:kitchen", _dir).LoadCredentialsAsync());
        Assert.True(File.Exists(Path.Combine(folder, "pi:kitchen.json")));
    }

    [Fact]
    public async Task Constructing_storage_touches_no_file()
    {
        string folder = Path.Combine(_dir, "wbskt");
        Directory.CreateDirectory(folder);
        string legacy = Path.Combine(folder, "pi:kitchen.json");
        await File.WriteAllTextAsync(legacy, "{}");

        FileClientStorage.ForClient("pi:kitchen", _dir);

        Assert.True(File.Exists(legacy));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
