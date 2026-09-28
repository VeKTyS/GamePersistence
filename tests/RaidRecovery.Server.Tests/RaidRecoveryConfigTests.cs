namespace RaidRecovery.Server.Tests;

public sealed class RaidRecoveryConfigTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public RaidRecoveryConfigTests()
    {
        Directory.CreateDirectory(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    private void WriteConfig(string content) => File.WriteAllText(Path.Combine(_temp.Path, "config.json"), content);

    [Fact]
    public void Missing_file_falls_back_to_defaults_with_a_warning()
    {
        var config = RaidRecoveryConfig.Load(_temp.Path, out var warning);

        Assert.Equal(TimeSpan.FromHours(24), config.MaxAge);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Valid_file_is_read_without_warning()
    {
        WriteConfig("""{ "maxAgeHours": 6 }""");

        var config = RaidRecoveryConfig.Load(_temp.Path, out var warning);

        Assert.Equal(TimeSpan.FromHours(6), config.MaxAge);
        Assert.Null(warning);
    }

    [Fact]
    public void Broken_file_falls_back_to_defaults_with_a_warning()
    {
        WriteConfig("{ maxAgeHours: ");

        var config = RaidRecoveryConfig.Load(_temp.Path, out var warning);

        Assert.Equal(TimeSpan.FromHours(24), config.MaxAge);
        Assert.NotNull(warning);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100000, 168)]
    public void Out_of_range_values_are_clamped(int configured, int expectedHours)
    {
        WriteConfig($$"""{ "maxAgeHours": {{configured}} }""");

        var config = RaidRecoveryConfig.Load(_temp.Path, out _);

        Assert.Equal(TimeSpan.FromHours(expectedHours), config.MaxAge);
    }
}
