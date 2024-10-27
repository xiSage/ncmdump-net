using System.Text.Json.Nodes;

namespace ncmdump_net.src
{
    internal class NeteaseCloudMusicMetadata
    {
        public string Album;
        public string[] Artist;
        public string Fromat;
        public string Name;
        public long Duration;
        public long Bitrate;

        public NeteaseCloudMusicMetadata(string meta)
        {
            Album = "";
            Artist = [];
            Fromat = "";
            Name = "";
            Duration = 0;
            Bitrate = 0;

            if (meta == null || meta.Length == 0) { return; }

            if (JsonObject.Parse(meta) is JsonObject json)
            {
                if (json["musicName"] is JsonValue musicName) { Name = musicName.GetValue<string>(); }
                if (json["album"] is JsonValue album) { Album = album.GetValue<string>(); }

                var artists = json["artist"]?.AsArray();
                if (artists != null && artists.Count > 0)
                {
                    Artist = new string[artists.Count];
                    for (int i = 0; i < artists.Count; i++)
                    {
                        if (artists[i] is JsonArray array)
                        {
                            Artist[i] = array[0]?.GetValue<string>()?? "";
                        }
                    }
                }

                if (json["bitrate"] is JsonValue bitrate) { Bitrate = bitrate.GetValue<int>(); }
                if (json["duration"] is JsonValue duration) { Duration = duration.GetValue<int>(); }
                if (json["format"] is JsonValue format) { Fromat = format.GetValue<string>(); }
            }

        }
    }
}
