using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuestMapStudio.Core;

public sealed class PlaylistDocument : INotifyPropertyChanged
{
	public JsonObject Root { get; private set; } = new JsonObject();

	public bool Pending { get; set; }

	public bool PendingDelete { get; set; }

	public string? RemotePath { get; set; }

	public string? RemoteDigest { get; set; }

	public string LocalId { get; set; } = Guid.NewGuid().ToString("N");

	public string Title
	{
		get
		{
			return MapEntry.S(Root, "playlistTitle", "New playlist");
		}
		set
		{
			Root["playlistTitle"] = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Display"));
		}
	}

	public string Author
	{
		get
		{
			return MapEntry.S(Root, "playlistAuthor");
		}
		set
		{
			Root["playlistAuthor"] = value;
		}
	}

	public string Description
	{
		get
		{
			return MapEntry.S(Root, "playlistDescription");
		}
		set
		{
			Root["playlistDescription"] = value;
		}
	}

	public JsonArray Songs => Root["songs"].AsArray();

	public string Display => Title + " (" + Songs.Count + ")";

	public event PropertyChangedEventHandler? PropertyChanged;

	public static PlaylistDocument New()
	{
		return Parse("{\"playlistTitle\":\"New playlist\",\"playlistAuthor\":\"\",\"playlistDescription\":\"\",\"songs\":[]}");
	}

	public static PlaylistDocument Parse(string text)
	{
		if (Encoding.UTF8.GetByteCount(text) > 8388608)
		{
			throw new InvalidDataException("Playlist exceeds 8 MiB");
		}
		JsonObject jsonObject = (JsonNode.Parse(text.TrimStart('\ufeff')) as JsonObject) ?? throw new InvalidDataException("Invalid playlist");
		if (!(jsonObject["songs"] is JsonArray source) || source.Any((JsonNode n) => !(n is JsonObject)))
		{
			throw new InvalidDataException("The playlist must contain a songs array of objects");
		}
		return new PlaylistDocument
		{
			Root = jsonObject
		};
	}

	public byte[] Bytes()
	{
		return Encoding.UTF8.GetBytes(Root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public bool Add(MapEntry map, string hash)
	{
		if (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("Invalid game SHA-1");
		}
		if (Songs.Any((JsonNode n) => string.Equals(MapEntry.S(n, "hash"), hash, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		Songs.Add(new JsonObject
		{
			["hash"] = hash.ToLowerInvariant(),
			["songName"] = map.Title
		});
		return true;
	}

	public void Move(int from, int to)
	{
		if (from >= 0 && to >= 0 && from < Songs.Count && to < Songs.Count && from != to)
		{
			JsonNode item = Songs[from];
			Songs.RemoveAt(from);
			Songs.Insert(to, item);
		}
	}

	public void SetCover(byte[] image, string mime)
	{
		if (image.Length > 2097152)
		{
			throw new InvalidDataException("Cover exceeds 2 MiB");
		}
		bool flag = image.Length > 8 && ((ReadOnlySpan<byte>)image.AsSpan(0, 8)).SequenceEqual((ReadOnlySpan<byte>)new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 });
		bool flag2 = image.Length > 3 && image[0] == byte.MaxValue && image[1] == 216 && image[2] == byte.MaxValue;
		if (!flag && !flag2)
		{
			throw new InvalidDataException("Select a PNG or JPEG cover");
		}
		string text = Convert.ToBase64String(image);
		Root["imageString"] = text;
		Root["image"] = "data:image/" + (flag ? "png" : "jpeg") + ";base64," + text;
	}
}
