using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class MapEntry
{
	public string Folder { get; set; } = "";

	public string InfoName { get; set; } = "Info.dat";

	public string RawInfo { get; set; } = "";

	[JsonIgnore]
	public string CoverFile => ReadCoverFile();

	public string Title { get; set; } = "";

	public string Artist { get; set; } = "";

	public string Mapper { get; set; } = "";

	public List<Difficulty> Difficulties { get; set; } = new List<Difficulty>();

	public List<string> HashFiles { get; set; } = new List<string>();

	public double? Bpm { get; set; }

	public string? AudioFile { get; set; }

	public double? Duration { get; set; }

	public string? MapCode { get; set; }

	public long? ModifiedUnix { get; set; }

	public string? GameHash { get; set; }

	public string? Error { get; set; }

	public string DifficultyText => string.Join(", ", Difficulties.Select((Difficulty x) => x.Characteristic + ": " + x.Name));

	[JsonIgnore]
	public string CompactDifficulty => string.Join(" · ", Difficulties.Select((Difficulty d) =>
	{
		switch (d.Name)
		{
		case "Easy":
			return "E";
		case "Normal":
			return "N";
		case "Hard":
			return "H";
		case "Expert":
			return "Ex";
		case "ExpertPlus":
		case "Expert+":
			return "Ex+";
		default:
			return d.Name;
		}
	}).Distinct());

	public string RequiredText => Join(Difficulties.SelectMany((Difficulty x) => x.Requirements));

	public string SuggestedText => Join(Difficulties.SelectMany((Difficulty x) => x.Suggestions));

	private string ReadCoverFile()
	{
		if (RawInfo.Length == 0)
		{
			return "";
		}
		try
		{
			JsonNode n = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(RawInfo)).TrimStart('\ufeff'));
			string text = S(n, "coverImageFilename", S(n, "_coverImageFilename"));
			ValidateRelative(text);
			return text;
		}
		catch (Exception ex) when ((ex is FormatException || ex is JsonException || ex is InvalidDataException) ? true : false)
		{
			return "";
		}
	}

	private static string Join(IEnumerable<string> values)
	{
		string[] array = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length != 0)
		{
			return string.Join(", ", array);
		}
		return "None listed";
	}

	public override string ToString()
	{
		return Title + " — " + Artist + " | " + Mapper + ((Error == null) ? "" : (" ⚠ " + Error));
	}

	public static MapEntry Parse(string folder, string infoName, byte[] raw)
	{
		MapEntry mapEntry = new MapEntry
		{
			Folder = folder,
			InfoName = infoName,
			RawInfo = Convert.ToBase64String(raw)
		};
		JsonObject jsonObject = (JsonNode.Parse(Encoding.UTF8.GetString(raw).TrimStart('\ufeff')) as JsonObject) ?? throw new InvalidDataException("Info.dat is not a JSON object");
		string text = S(jsonObject, "version", S(jsonObject, "_version"));
		bool flag = text.StartsWith("4.", StringComparison.Ordinal);
		if (!flag && !text.StartsWith("2.") && !text.StartsWith("3."))
		{
			throw new InvalidDataException("Unsupported Info.dat version: " + text);
		}
		JsonObject jsonObject2 = jsonObject["audio"] as JsonObject;
		JsonObject n = (jsonObject["customData"] ?? jsonObject["_customData"]) as JsonObject;
		mapEntry.Bpm = Positive(jsonObject2?["bpm"] ?? jsonObject["bpm"] ?? jsonObject["_beatsPerMinute"]);
		mapEntry.Duration = Positive(jsonObject2?["songDuration"] ?? jsonObject["songDuration"] ?? jsonObject["_songDuration"]);
		string text2 = S(jsonObject2, "songFilename", S(jsonObject, "_songFilename"));
		if (text2.Length > 0)
		{
			try
			{
				ValidateRelative(text2);
				mapEntry.AudioFile = text2;
			}
			catch (InvalidDataException)
			{
			}
		}
		mapEntry.MapCode = TryMapCode(S(n, "beatSaverKey", S(n, "_beatSaverKey", S(n, "beatsaverKey", S(n, "_beatsaverKey", S(jsonObject, "beatSaverKey"))))));
		MapEntry mapEntry2 = mapEntry;
		if (mapEntry2.MapCode == null)
		{
			string text3 = (mapEntry2.MapCode = MapCodeFromName(Path.GetFileName(folder)));
		}
		mapEntry.Title = (flag ? S(jsonObject["song"], "title") : S(jsonObject, "_songName"));
		mapEntry.Artist = (flag ? S(jsonObject["song"], "author") : S(jsonObject, "_songAuthorName"));
		mapEntry.Mapper = (flag ? "" : S(jsonObject, "_levelAuthorName"));
		mapEntry.HashFiles.Add(infoName);
		if (flag)
		{
			mapEntry.HashFiles.Add(S(jsonObject["audio"], "audioDataFilename"));
			foreach (JsonNode item in jsonObject["difficultyBeatmaps"]?.AsArray() ?? new JsonArray())
			{
				mapEntry.HashFiles.Add(S(item, "beatmapDataFilename"));
				mapEntry.HashFiles.Add(S(item, "lightshowDataFilename"));
				string mapper = string.Join(", ", Strings(item?["beatmapAuthors"]?["mappers"]));
				mapEntry.Difficulties.Add(new Difficulty(S(item, "characteristic"), S(item, "difficulty"), mapper, Strings(item?["customData"]?["requirements"]), Strings(item?["customData"]?["suggestions"])));
			}
			mapEntry.Mapper = string.Join(", ", (from x in mapEntry.Difficulties
				select x.Mapper into x
				where x.Length > 0
				select x).Distinct());
		}
		else
		{
			foreach (JsonNode item2 in jsonObject["_difficultyBeatmapSets"]?.AsArray() ?? new JsonArray())
			{
				foreach (JsonNode item3 in item2?["_difficultyBeatmaps"]?.AsArray() ?? new JsonArray())
				{
					mapEntry.HashFiles.Add(S(item3, "_beatmapFilename"));
					mapEntry.Difficulties.Add(new Difficulty(S(item2, "_beatmapCharacteristicName"), S(item3, "_difficulty"), mapEntry.Mapper, Strings(item3?["_customData"]?["_requirements"]), Strings(item3?["_customData"]?["_suggestions"])));
				}
			}
		}
		if (mapEntry.HashFiles.Count < 2)
		{
			throw new InvalidDataException("No difficulties found");
		}
		foreach (string hashFile in mapEntry.HashFiles)
		{
			ValidateRelative(hashFile);
		}
		if (string.IsNullOrWhiteSpace(mapEntry.Title))
		{
			mapEntry.Title = Path.GetFileName(folder);
		}
		return mapEntry;
	}

	private static double? Positive(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue) || !jsonValue.TryGetValue<double>(out var value) || !double.IsFinite(value) || !(value > 0.0))
		{
			return null;
		}
		return value;
	}

	private static string? TryMapCode(string value)
	{
		int length = value.Length;
		if (length < 1 || length > 8 || !value.All(Uri.IsHexDigit))
		{
			return null;
		}
		return value.ToLowerInvariant();
	}

	public static string? MapCodeFromName(string name)
	{
		int num = name.IndexOf(" (", StringComparison.Ordinal);
		if (num <= 0)
		{
			return null;
		}
		return TryMapCode(name.Substring(0, num));
	}

	public static string S(JsonNode? n, string key, string fallback = "")
	{
		if (!(n?[key] is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value))
		{
			return fallback;
		}
		return value;
	}

	private static string[] Strings(JsonNode? n)
	{
		if (!(n is JsonArray source))
		{
			return Array.Empty<string>();
		}
		return (from x in source.OfType<JsonValue>()
			select (!x.TryGetValue<string>(out string value)) ? "" : value into x
			where x.Length > 0
			select x).ToArray();
	}

	public static void ValidateRelative(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) || path.Split('/').Any((string x) => ((x != null && x.Length == 0) || x == "." || x == "..") ? true : false))
		{
			throw new InvalidDataException("Invalid file reference in Info.dat: " + path);
		}
	}

	public async Task<string> ComputeHash(Func<string, Task<byte[]>> read)
	{
		using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
		foreach (string hashFile in HashFiles)
		{
			IncrementalHash incrementalHash = sha;
			incrementalHash.AppendData(await read(hashFile));
		}
		return Convert.ToHexString(sha.GetHashAndReset());
	}
}
