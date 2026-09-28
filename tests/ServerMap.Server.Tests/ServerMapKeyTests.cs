namespace TCFModManager.ServerMap.Tests;

public class ServerMapKeyTests
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void A_generated_key_is_six_groups_of_four_from_the_unambiguous_alphabet()
    {
        var key = ServerMapKey.Generate();

        var groups = key.Split('-');
        Assert.Equal(6, groups.Length);
        Assert.All(groups, g => Assert.Equal(4, g.Length));
        Assert.All(key.Replace("-", ""), c => Assert.Contains(c, Alphabet));
    }

    [Fact]
    public void Generated_keys_differ() =>
        Assert.NotEqual(ServerMapKey.Generate(), ServerMapKey.Generate());

    [Theory]
    [InlineData("ABCD-EFGH-JKMN", "abcd efgh jkmn")]
    [InlineData("ABCD-EFGH-JKMN", "ABCDEFGHJKMN")]
    [InlineData("ABCD-EFGH-JKMN", "  abcd-efgh-jkmn\r\n")]
    public void Verify_ignores_case_dashes_and_spacing(string expected, string presented) =>
        Assert.True(ServerMapKey.Verify(expected, presented));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCD-EFGH-JKMP")]
    [InlineData("ABCD-EFGH")]
    public void Verify_refuses_anything_else(string? presented) =>
        Assert.False(ServerMapKey.Verify("ABCD-EFGH-JKMN", presented));

    [Fact]
    public void The_first_read_writes_the_key_file()
    {
        using var dir = new TempDirectory();

        var key = ServerMapKey.Current(dir.Path);

        Assert.Equal(key, File.ReadAllText(dir.File(ServerMapKey.FileName)).Trim());
    }

    [Fact]
    public void A_hand_edited_key_file_is_the_key()
    {
        using var dir = new TempDirectory();
        ServerMapKey.Current(dir.Path);

        File.WriteAllText(dir.File(ServerMapKey.FileName), "MY-OWN-KEY-1234\n");
        File.SetLastWriteTimeUtc(dir.File(ServerMapKey.FileName), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("MY-OWN-KEY-1234", ServerMapKey.Current(dir.Path));
    }

    [Fact]
    public void A_key_that_vanished_under_a_running_server_is_kept_not_reminted()
    {
        using var dir = new TempDirectory();
        var key = ServerMapKey.Current(dir.Path);

        File.Delete(dir.File(ServerMapKey.FileName));

        Assert.Equal(key, ServerMapKey.Current(dir.Path));
    }

    [Fact]
    public void Rotate_replaces_the_key_and_the_file()
    {
        using var dir = new TempDirectory();
        var before = ServerMapKey.Current(dir.Path);

        var after = ServerMapKey.Rotate(dir.Path);

        Assert.NotEqual(before, after);
        Assert.Equal(after, ServerMapKey.Current(dir.Path));
        Assert.Equal(after, File.ReadAllText(dir.File(ServerMapKey.FileName)).Trim());
    }

    // The in-memory key belongs to the folder it came from; a second folder gets its own file.
    [Fact]
    public void A_second_config_folder_gets_its_own_key_file()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        var a = ServerMapKey.Current(first.Path);
        var b = ServerMapKey.Current(second.Path);

        Assert.NotEqual(a, b);
        Assert.True(File.Exists(second.File(ServerMapKey.FileName)));
    }
}
