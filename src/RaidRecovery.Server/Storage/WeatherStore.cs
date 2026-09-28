using System.Text.Json;
using SPTarkov.Server.Core.Models.Spt.Weather;

namespace RaidRecovery.Server.Storage;

public interface IWeatherStore
{
    void Write(string profileId, GetLocalWeatherResponseData weather);

    /// <summary>null if nothing is stored or if the file is unreadable.</summary>
    GetLocalWeatherResponseData? Read(string profileId);

    void Delete(string profileId);
}

/// <summary>One file per profile next to the snapshot: &lt;profile&gt;.weather.json, written through SPT's serializer.</summary>
public sealed class WeatherStore(
    string directory,
    Func<GetLocalWeatherResponseData, string?> serialize,
    Func<string, GetLocalWeatherResponseData?> deserialize
) : IWeatherStore
{
    public void Write(string profileId, GetLocalWeatherResponseData weather)
    {
        var json = serialize(weather) ?? throw new InvalidOperationException("The weather could not be serialized");
        Directory.CreateDirectory(directory);
        var path = BuildPath(profileId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        // A crash in the middle of the write leaves the previous file intact
        File.Move(temp, path, overwrite: true);
    }

    public GetLocalWeatherResponseData? Read(string profileId)
    {
        var path = BuildPath(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public void Delete(string profileId)
    {
        File.Delete(BuildPath(profileId));
    }

    private string BuildPath(string profileId)
    {
        // The identifier ends up in a file path: we refuse anything that is not a profile id
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            throw new ArgumentException("Invalid profile identifier", nameof(profileId));
        }

        return Path.Combine(directory, profileId + ".weather.json");
    }
}
