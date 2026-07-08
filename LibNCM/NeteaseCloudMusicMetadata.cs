using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibNCM;

public class NeteaseCloudMusicMetadata
{
    public string Album { get; set; } = "";
    public List<string> Artist { get; } = new(5);
    public string Format { get; set; } = "";
    public string Name { get; set; } = "";
    public long Duration { get; set; }
    public long Bitrate { get; set; }
    public string Description { get; set; } = "";

    public NeteaseCloudMusicMetadata(string meta)
    {
        if (string.IsNullOrEmpty(meta)) return;

        JsonObject? json;
        try
        {
            json = JsonNode.Parse(meta) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new NcmMetadataException("Failed to parse metadata JSON", e);
        }

        if (json is null) return;

        if (json["musicName"] is JsonValue musicName) { Name = musicName.GetValue<string>(); }
        if (json["album"] is JsonValue album) { Album = album.GetValue<string>(); }

        var artists = json["artist"]?.AsArray();
        if (artists is { Count: > 0 })
        {
            for (int i = 0; i < artists.Count; i++)
            {
                if (artists[i] is JsonArray array)
                {
                    Artist.Add(array[0]?.GetValue<string>() ?? "");
                }
            }
        }

        if (json["bitrate"] is JsonValue bitrate) { Bitrate = bitrate.GetValue<long>(); }
        if (json["duration"] is JsonValue duration) { Duration = duration.GetValue<long>(); }
        if (json["format"] is JsonValue format) { Format = format.GetValue<string>(); }
    }
}
