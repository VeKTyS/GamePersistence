using RaidRecovery.Server.Services;
using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Eft.Weather;
using SPTarkov.Server.Core.Models.Spt.Weather;

namespace RaidRecovery.Server.Tests;

public class WeatherReplayServiceTests
{
    private sealed class MemoryWeatherStore : IWeatherStore
    {
        private readonly Dictionary<string, GetLocalWeatherResponseData> _weather = [];

        public void Write(string profileId, GetLocalWeatherResponseData weather) => _weather[profileId] = weather;

        public GetLocalWeatherResponseData? Read(string profileId) => _weather.GetValueOrDefault(profileId);

        public void Delete(string profileId) => _weather.Remove(profileId);
    }

    private readonly MemoryWeatherStore _store = new();
    private readonly WeatherReplayService _service;

    public WeatherReplayServiceTests()
    {
        _service = new WeatherReplayService(_store);
    }

    private static GetLocalWeatherResponseData Forecast(double rain)
    {
        return new GetLocalWeatherResponseData { Weather = [new Weather { Rain = rain }] };
    }

    [Fact]
    public void A_normal_raid_keeps_the_weather_of_the_server()
    {
        var kept = _service.OnGenerated(Samples.ProfileId, Forecast(1));

        Assert.Null(kept);
    }

    [Fact]
    public void A_resumed_raid_gets_the_weather_of_the_interrupted_raid()
    {
        _service.OnGenerated(Samples.ProfileId, Forecast(1));
        _service.Arm(Samples.ProfileId);

        var kept = _service.OnGenerated(Samples.ProfileId, Forecast(5));

        Assert.Equal(1, kept!.Weather!.Single().Rain);
    }

    [Fact]
    public void Only_the_first_request_after_a_recovery_is_replayed()
    {
        _service.OnGenerated(Samples.ProfileId, Forecast(1));
        _service.Arm(Samples.ProfileId);
        _service.OnGenerated(Samples.ProfileId, Forecast(5));

        // The game asks again later in the raid: it gets the server's weather
        var kept = _service.OnGenerated(Samples.ProfileId, Forecast(7));

        Assert.Null(kept);
    }

    [Fact]
    public void A_recovery_without_kept_weather_falls_back_on_the_server()
    {
        _service.Arm(Samples.ProfileId);

        var kept = _service.OnGenerated(Samples.ProfileId, Forecast(5));

        Assert.Null(kept);
    }

    [Fact]
    public void Forgetting_a_raid_cancels_the_replay()
    {
        _service.OnGenerated(Samples.ProfileId, Forecast(1));
        _service.Arm(Samples.ProfileId);

        _service.Forget(Samples.ProfileId);

        Assert.Null(_service.OnGenerated(Samples.ProfileId, Forecast(5)));
    }

    [Fact]
    public void Profiles_do_not_share_their_weather()
    {
        _service.OnGenerated(Samples.ProfileId, Forecast(1));
        _service.Arm(Samples.ProfileId);

        Assert.Null(_service.OnGenerated(Samples.OtherProfileId, Forecast(5)));
    }

    [Fact]
    public void An_invalid_profile_identifier_is_ignored()
    {
        _service.Arm("../../evil");

        Assert.Null(_service.OnGenerated("../../evil", Forecast(5)));
    }
}
