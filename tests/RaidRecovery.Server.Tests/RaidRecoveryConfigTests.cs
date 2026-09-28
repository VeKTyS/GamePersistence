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

    [Fact]
    public void The_two_resume_rules_are_off_unless_asked_for()
    {
        WriteConfig("""{ "maxAgeHours": 6 }""");

        var config = RaidRecoveryConfig.Load(_temp.Path, out _);

        Assert.Equal(0, config.ResumeLimit);
        Assert.Equal(0, config.VitalHealthFloor);
    }

    [Theory]
    [InlineData(-3, 0, 0)]
    [InlineData(2, 2, 15)]
    [InlineData(5000, 100, 100)]
    public void The_resume_rules_are_read_and_clamped(int configured, int expectedLimit, int expectedFloor)
    {
        var floor = configured == 2 ? 15 : configured;
        WriteConfig($$"""{ "maxResumesPerRaid": {{configured}}, "blockResumeUnderVitalHealthPercent": {{floor}} }""");

        var config = RaidRecoveryConfig.Load(_temp.Path, out _);

        Assert.Equal(expectedLimit, config.ResumeLimit);
        Assert.Equal(expectedFloor, config.VitalHealthFloor);
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
