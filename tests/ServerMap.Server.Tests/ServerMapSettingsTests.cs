namespace TCFModManager.ServerMap.Tests;

public class ServerMapSettingsTests
{
    private static void Write(TempDirectory dir, string json, int minutesAhead)
    {
        var path = dir.File(ServerMapSettings.FileName);
        File.WriteAllText(path, json);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(minutesAhead));
    }

    [Fact]
    public void No_file_means_the_defaults()
    {
        using var dir = new TempDirectory();

        Assert.False(ServerMapSettings.Current(dir.Path).LanOnly);
    }

    [Theory]
    [InlineData("{ \"lanOnly\": true }")]
    [InlineData("{ \"LanOnly\": true }")]
    [InlineData("{ \"LANONLY\": true, \"somethingElse\": 3 }")]
    public void LanOnly_is_read_whatever_the_case(string json)
    {
        using var dir = new TempDirectory();
        Write(dir, json, 1);

        Assert.True(ServerMapSettings.Current(dir.Path).LanOnly);
    }

    [Fact]
    public void A_change_is_picked_up_without_a_restart()
    {
        using var dir = new TempDirectory();
        Write(dir, "{ \"lanOnly\": false }", 1);
        Assert.False(ServerMapSettings.Current(dir.Path).LanOnly);

        Write(dir, "{ \"lanOnly\": true }", 2);

        Assert.True(ServerMapSettings.Current(dir.Path).LanOnly);
    }

    [Fact]
    public void A_file_broken_by_hand_keeps_the_last_good_read()
    {
        using var dir = new TempDirectory();
        Write(dir, "{ \"lanOnly\": true }", 1);
        Assert.True(ServerMapSettings.Current(dir.Path).LanOnly);

        Write(dir, "{ \"lanOnly\": tru", 2);

        Assert.True(ServerMapSettings.Current(dir.Path).LanOnly);
    }

    [Fact]
    public void A_file_never_readable_is_the_defaults()
    {
        using var dir = new TempDirectory();
        Write(dir, "not json", 1);

        Assert.False(ServerMapSettings.Current(dir.Path).LanOnly);
    }

    [Fact]
    public void Deleting_the_file_turns_it_back_off()
    {
        using var dir = new TempDirectory();
        Write(dir, "{ \"lanOnly\": true }", 1);
        Assert.True(ServerMapSettings.Current(dir.Path).LanOnly);

        File.Delete(dir.File(ServerMapSettings.FileName));

        Assert.False(ServerMapSettings.Current(dir.Path).LanOnly);
    }
}
