using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace QuestMapStudio.Core;

public sealed record MapArchive(string Title, string Prefix, string ContentHash, long ExpandedBytes, int FileCount)
{
	public const long MaxZipBytes = 1073741824L;

	public const long MaxExpandedBytes = 2147483648L;

	public static void PrepareForDevice(string original, string output, MapArchive expected, CancellationToken cancellation)
	{
		string prefix = ((expected.Prefix.Length == 0) ? "" : (expected.Prefix + "/"));
		using (ZipArchive zipArchive = ZipFile.OpenRead(original))
		{
			using FileStream stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			using ZipArchive zipArchive2 = new ZipArchive(stream, ZipArchiveMode.Create);
			if (zipArchive.Entries.Count > 20000)
			{
				throw new InvalidDataException("Too many ZIP entries.");
			}
			byte[] array = new byte[81920];
			long num = 0L;
			foreach (ZipArchiveEntry item in zipArchive.Entries.Where((ZipArchiveEntry e) => !e.FullName.EndsWith('/') && e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
			{
				cancellation.ThrowIfCancellationRequested();
				string text = item.FullName.Substring(prefix.Length);
				if (text.StartsWith('/') || text.Contains("//") || text.Contains('\\') || text.Contains(':') || text.Any(char.IsControl) || text.Split('/').Any((string x) => (x == "." || x == "..") ? true : false))
				{
					throw new InvalidDataException("Unsafe ZIP filename.");
				}
				ZipArchiveEntry zipArchiveEntry = zipArchive2.CreateEntry(text, CompressionLevel.Optimal);
				using Stream stream2 = item.Open();
				using Stream stream3 = zipArchiveEntry.Open();
				int num2;
				while ((num2 = stream2.Read(array, 0, array.Length)) != 0)
				{
					cancellation.ThrowIfCancellationRequested();
					num += num2;
					if (num > 2147483648u)
					{
						throw new InvalidDataException("Expanded map exceeds 2 GiB.");
					}
					stream3.Write(array, 0, num2);
				}
			}
		}
		if (Inspect(output, cancellation).ContentHash != expected.ContentHash)
		{
			throw new InvalidDataException("ZIP contents changed during preparation.");
		}
	}

	public string GameHash(string file, CancellationToken ct)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(file);
		string text = ((Prefix.Length == 0) ? "" : (Prefix + "/"));
		ZipArchiveEntry zipArchiveEntry = zipArchive.GetEntry(text + "Info.dat") ?? zipArchive.GetEntry(text + "info.dat") ?? throw new InvalidDataException("Missing Info.dat.");
		using MemoryStream memoryStream = new MemoryStream();
		using (Stream stream = zipArchiveEntry.Open())
		{
			stream.CopyTo(memoryStream);
		}
		MapEntry mapEntry = MapEntry.Parse("", zipArchiveEntry.Name, memoryStream.ToArray());
		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
		byte[] array = new byte[81920];
		foreach (string hashFile in mapEntry.HashFiles)
		{
			using Stream stream2 = (zipArchive.GetEntry(text + hashFile) ?? throw new InvalidDataException("Missing map file: " + hashFile)).Open();
			int count;
			while ((count = stream2.Read(array)) != 0)
			{
				ct.ThrowIfCancellationRequested();
				incrementalHash.AppendData(array, 0, count);
			}
		}
		return Convert.ToHexString(incrementalHash.GetHashAndReset());
	}

	public static MapArchive Inspect(string file, CancellationToken cancellation = default(CancellationToken))
	{
		if (new FileInfo(file).Length > 1073741824)
		{
			throw new InvalidDataException("ZIP exceeds 1 GiB.");
		}
		Dictionary<string, ZipArchiveEntry> files;
		string prefix;
		int audio;
		int difficulties;
		using (ZipArchive zipArchive = ZipFile.OpenRead(file))
		{
			if (zipArchive.Entries.Count > 20000)
			{
				throw new InvalidDataException("Too many ZIP entries.");
			}
			files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
			long num = 0L;
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				cancellation.ThrowIfCancellationRequested();
				string fullName = entry.FullName;
				if (string.IsNullOrEmpty(fullName) || fullName.StartsWith('/') || fullName.Contains("//") || fullName.Contains('\\') || fullName.Contains(':') || fullName.Any(char.IsControl) || fullName.Split('/').Any((string x) => x == ".." || x == "."))
				{
					throw new InvalidDataException("Unsafe archive path: " + fullName);
				}
				int num2 = (entry.ExternalAttributes >> 16) & 0xF000;
				if (num2 != 0 && num2 != 32768 && num2 != 16384)
				{
					throw new InvalidDataException("Links and special files are not accepted.");
				}
				if ((num2 == 16384 && !fullName.EndsWith('/')) || (num2 == 32768 && fullName.EndsWith('/')))
				{
					throw new InvalidDataException("Inconsistent directory entry.");
				}
				num = checked(num + entry.Length);
				if (num > 2147483648u)
				{
					throw new InvalidDataException("Expanded map exceeds 2 GiB.");
				}
				if (!fullName.EndsWith('/') && !files.TryAdd(fullName, entry))
				{
					throw new InvalidDataException("Duplicate archive filename: " + fullName);
				}
			}
			KeyValuePair<string, ZipArchiveEntry>[] array = files.Where((KeyValuePair<string, ZipArchiveEntry> x) => x.Key.Split('/').Last().Equals("Info.dat", StringComparison.OrdinalIgnoreCase)).ToArray();
			if (array.Length != 1)
			{
				throw new InvalidDataException("Expected exactly one map with Info.dat in this ZIP.");
			}
			KeyValuePair<string, ZipArchiveEntry> keyValuePair = array[0];
			string text = keyValuePair.Key.Split('/').Last();
			if ((!(text == "Info.dat") && !(text == "info.dat")) || 1 == 0)
			{
				throw new InvalidDataException("Use Info.dat or info.dat with exact filename case.");
			}
			if (keyValuePair.Value.Length > 4194304)
			{
				throw new InvalidDataException("Info.dat is too large.");
			}
			prefix = keyValuePair.Key.Substring(0, keyValuePair.Key.Length - keyValuePair.Key.Split('/').Last().Length);
			using Stream utf8Json = keyValuePair.Value.Open();
			using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			});
			JsonElement rootElement = jsonDocument.RootElement;
			string text2;
			JsonElement value2;
			JsonElement value3;
			if (rootElement.TryGetProperty("_songName", out var value))
			{
				text2 = value.GetString();
			}
			else if (rootElement.TryGetProperty("song", out value2) && value2.TryGetProperty("title", out value3))
			{
				text2 = value3.GetString();
			}
			else
			{
				text2 = (rootElement.TryGetProperty("songName", out var value4) ? value4.GetString() : "Custom map");
			}
			audio = 0;
			difficulties = 0;
			Visit(rootElement);
			if (audio != 1 || difficulties == 0)
			{
				throw new InvalidDataException("Info.dat must reference audio and at least one difficulty.");
			}
			using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			byte[] array2 = new byte[81920];
			int num3 = 0;
			foreach (KeyValuePair<string, ZipArchiveEntry> item in files.Where((KeyValuePair<string, ZipArchiveEntry> x) => x.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy((KeyValuePair<string, ZipArchiveEntry> x) => x.Key, StringComparer.Ordinal))
			{
				cancellation.ThrowIfCancellationRequested();
				incrementalHash.AppendData(Encoding.UTF8.GetBytes(item.Key.Substring(prefix.Length) + "\0" + item.Value.Length + "\0"));
				using Stream stream = item.Value.Open();
				int count;
				while ((count = stream.Read(array2, 0, array2.Length)) != 0)
				{
					cancellation.ThrowIfCancellationRequested();
					incrementalHash.AppendData(array2, 0, count);
				}
				num3++;
			}
			return new MapArchive(text2 ?? "Custom map", prefix.TrimEnd('/'), Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant(), num, num3);
		}
		void Visit(JsonElement node)
		{
			if (node.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item2 in node.EnumerateArray())
				{
					Visit(item2);
				}
				return;
			}
			if (node.ValueKind != JsonValueKind.Object)
			{
				return;
			}
			foreach (JsonProperty item3 in node.EnumerateObject())
			{
				string text3 = item3.Name.TrimStart('_');
				bool flag = text3 == "songFilename";
				bool flag2 = ((text3 == "beatmapFilename" || text3 == "beatmapDataFilename") ? true : false);
				bool flag3 = flag2;
				flag2 = flag | flag3;
				if (!flag2)
				{
					bool flag4 = ((text3 == "audioDataFilename" || text3 == "lightshowDataFilename") ? true : false);
					flag2 = flag4;
				}
				if (flag2)
				{
					if (item3.Value.ValueKind != JsonValueKind.String)
					{
						throw new InvalidDataException("Invalid file reference: " + item3.Name);
					}
					string path = item3.Value.GetString();
					if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Split('/').Any((string x) => (x == "." || x == "..") ? true : false) || !files.ContainsKey(prefix + path))
					{
						throw new InvalidDataException("Missing or invalid referenced file: " + path);
					}
					if (!files.Keys.Any((string x) => string.Equals(x, prefix + path, StringComparison.Ordinal)))
					{
						throw new InvalidDataException("Filename case does not match Info.dat: " + path);
					}
					if (flag)
					{
						audio++;
					}
					if (flag3)
					{
						difficulties++;
					}
				}
				else
				{
					Visit(item3.Value);
				}
			}
		}
	}
}
