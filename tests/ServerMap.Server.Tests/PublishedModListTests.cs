namespace TCFModManager.ServerMap.Tests;

public class PublishedModListTests
{
    private const string Minimal = "{ \"list\": { \"name\": \"Mine\", \"revision\": 3, \"entries\": [ {}, {} ] } }";

    [Fact]
    public void The_config_folder_is_Data_ServerMap_beside_the_ServerMap_folder()
    {
        using var root = new TempDirectory();
        var payload = Path.Combine(root.Path, "TCFModManager", "ServerMap", "payload");
        Directory.CreateDirectory(payload);

        Assert.Equal(Path.Combine(root.Path, "TCFModManager", "Data", "ServerMap"), PublishedModList.ConfigDirectory(payload));
    }

    [Fact]
    public void An_install_still_on_the_old_config_folder_keeps_using_it()
    {
        using var root = new TempDirectory();
        var payload = Path.Combine(root.Path, "TCFModManager", "ServerMap", "payload");
        var legacy = Path.Combine(root.Path, "TCFModManager", "ServerMap", "config");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(legacy);

        Assert.Equal(legacy, PublishedModList.ConfigDirectory(payload));
    }

    [Fact]
    public void Nothing_published_is_null()
    {
        using var dir = new TempDirectory();

        Assert.Null(PublishedModList.Current(dir.Path));
        Assert.Null(PublishedModList.Current(Path.Combine(dir.Path, "missing")));
    }

    [Fact]
    public void The_preferred_name_wins_over_other_lists()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("other.tcfmodlist"), Minimal);
        File.WriteAllText(dir.File(PublishedModList.PreferredFileName), Minimal);

        Assert.Equal(dir.File(PublishedModList.PreferredFileName), PublishedModList.Find(dir.Path));
    }

    [Fact]
    public void A_single_list_under_any_name_is_published()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("Zero to Hero.tcfmodlist"), Minimal);

        Assert.Equal(dir.File("Zero to Hero.tcfmodlist"), PublishedModList.Find(dir.Path));
    }

    [Fact]
    public void Two_lists_and_no_preferred_name_publishes_neither()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("a.tcfmodlist"), Minimal);
        File.WriteAllText(dir.File("b.tcfmodlist"), Minimal);

        Assert.Null(PublishedModList.Find(dir.Path));
    }

    [Fact]
    public void The_file_is_kept_byte_for_byte()
    {
        using var dir = new TempDirectory();
        var path = dir.File(PublishedModList.PreferredFileName);
        File.WriteAllText(path, Minimal);

        var read = PublishedModList.ReadFrom(path)!;

        Assert.Equal(Minimal, read.Json);
        Assert.Equal("Mine", read.Name);
        Assert.Equal(3, read.Revision);
        Assert.Equal(2, read.EntryCount);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ }")]
    [InlineData("{ \"list\": [] }")]
    [InlineData("{ \"list\": { \"entries\": [] } }")]
    [InlineData("{ \"list\": { \"name\": ")]
    public void Anything_that_is_not_a_list_is_not_published(string json)
    {
        using var dir = new TempDirectory();
        var path = dir.File(PublishedModList.PreferredFileName);
        File.WriteAllText(path, json);

        Assert.Null(PublishedModList.ReadFrom(path));
    }

    [Fact]
    public void A_list_with_no_revision_is_revision_one()
    {
        using var dir = new TempDirectory();
        var path = dir.File(PublishedModList.PreferredFileName);
        File.WriteAllText(path, "{ \"list\": { \"name\": \"Mine\" } }");

        Assert.Equal(1, PublishedModList.ReadFrom(path)!.Revision);
    }

    [Fact]
    public void A_republished_list_is_read_again()
    {
        using var dir = new TempDirectory();
        var path = dir.File(PublishedModList.PreferredFileName);
        File.WriteAllText(path, Minimal);
        Assert.Equal(3, PublishedModList.Current(dir.Path)!.Revision);

        File.WriteAllText(path, Minimal.Replace("\"revision\": 3", "\"revision\": 4"));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(4, PublishedModList.Current(dir.Path)!.Revision);
    }

    [Fact]
    public void Unpublishing_is_seen_straight_away()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File(PublishedModList.PreferredFileName), Minimal);
        Assert.NotNull(PublishedModList.Current(dir.Path));

        File.Delete(dir.File(PublishedModList.PreferredFileName));

        Assert.Null(PublishedModList.Current(dir.Path));
    }
}
