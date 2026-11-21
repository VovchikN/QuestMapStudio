using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class QuestRepository(AdbClient device)
{
	public const string Data = "/sdcard/ModData/com.beatgames.beatsaber";

	public const string Mods = "/sdcard/ModData/com.beatgames.beatsaber/Mods";

	public const string Playlists = "/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists";

	public static readonly string[] DefaultRoots = new string[5] { "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels", "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongLoader/CustomLevels", "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomWIPLevels", "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongLoader/CustomWIPLevels", "/sdcard/ModData/com.beatgames.beatsaber/CustomLevels" };

	public async Task<List<MapEntry>> Scan(IEnumerable<string> roots, Action<string> progress, CancellationToken ct)
	{
		await device.CheckConnection();
		List<string> paths = new List<string>();
		foreach (string root in roots.Distinct())
		{
			if (!root.StartsWith("/sdcard/", StringComparison.Ordinal) || root.Contains("..") || root.Any(char.IsControl))
			{
				throw new InvalidDataException("Map folders must be under /sdcard");
			}
			ct.ThrowIfCancellationRequested();
			string[] array = await device.Find(root, "-iname info.dat");
			paths.AddRange(array.Where((string path) => !path.StartsWith("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/", StringComparison.Ordinal)));
			progress($"{root} — map files found: {array.Length}");
		}
		string[] infos = (from x in paths.Distinct()
			group x by x.Substring(0, x.LastIndexOf('/')) into g
			select g.OrderBy((string x) => (!x.EndsWith("/info.dat")) ? 1 : 0).First()).ToArray();
		List<MapEntry> maps = new List<MapEntry>();
		for (int i = 0; i < infos.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			string text = infos[i];
			string root = text.Substring(0, text.LastIndexOf('/'));
			progress($"Reading map {i + 1}/{infos.Length} — {Path.GetFileName(root)}");
			try
			{
				List<MapEntry> list = maps;
				string folder = root;
				string fileName = Path.GetFileName(text);
				list.Add(MapEntry.Parse(folder, fileName, await device.Read(text)));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				await device.CheckConnection();
				maps.Add(new MapEntry
				{
					Folder = root,
					Title = Path.GetFileName(root),
					Error = ex2.Message
				});
			}
		}
		await ReadMapDates(maps, ct);
		return maps.OrderBy((MapEntry x) => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public async Task DeleteMap(MapEntry map, IEnumerable<string> roots)
	{
		string folder = map.Folder;
		string text = (from r in roots
			select r.TrimEnd('/') into r
			where folder.StartsWith(r + "/", StringComparison.Ordinal)
			orderby r.Length descending
			select r).FirstOrDefault();
		if (text == null || !folder.StartsWith("/sdcard/", StringComparison.Ordinal) || folder.StartsWith("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/", StringComparison.Ordinal) || folder.Any(char.IsControl) || folder.Split('/').Skip(1).Any((string segment) => ((segment != null && segment.Length == 0) || segment == "." || segment == "..") ? true : false) || !string.Equals(map.InfoName, "info.dat", StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException("The map folder is outside the custom map library.");
		}
		string value = AdbClient.Q(folder);
		string value2 = AdbClient.Q(text);
		string value3 = AdbClient.Q(folder.Substring(text.Length));
		string value4 = AdbClient.Q(folder + "/" + map.InfoName);
		await device.Shell($"actual=$(readlink -f {value}) && base=$(readlink -f {value2}) && test -d {value} && test ! -L {value} && test \"$actual\" = \"$base\"{value3} && test -f {value4} && nested=$(find {value} -mindepth 2 -iname info.dat) && test -z \"$nested\" && rm -rf {value} && test ! -e {value}");
	}

	private async Task ReadMapDates(List<MapEntry> maps, CancellationToken ct)
	{
		int index = 0;
		while (index < maps.Count)
		{
			ct.ThrowIfCancellationRequested();
			StringBuilder stringBuilder = new StringBuilder();
			do
			{
				MapEntry mapEntry = maps[index];
				string value = AdbClient.Q(mapEntry.Folder + "/" + mapEntry.InfoName);
				string text = $"if stamp=$(stat -c %Y {value} 2>/dev/null); then printf '%s %s\\n' {index} \"$stamp\"; fi; ";
				if (stringBuilder.Length > 0 && stringBuilder.Length + text.Length > 12000)
				{
					break;
				}
				stringBuilder.Append(text);
				index++;
			}
			while (index < maps.Count);
			try
			{
				string[] array = (await device.Shell(stringBuilder.ToString())).Split('\n', StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					string[] array2 = array[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
					if (array2.Length == 2 && int.TryParse(array2[0], out var result) && result >= 0 && result < maps.Count && long.TryParse(array2[1], out var result2))
					{
						maps[result].ModifiedUnix = result2;
					}
				}
			}
			catch (IOException)
			{
				await device.CheckConnection();
			}
		}
	}

	public async Task<string> ResolveHash(MapEntry map)
	{
		if (map.Error != null)
		{
			throw new InvalidDataException(map.Error);
		}
		string text = ParseHash(await device.Shell("set -o pipefail; " + HashCommand(map)));
		map.GameHash = text ?? throw new IOException("Could not calculate game SHA-1: " + map.Title);
		return map.GameHash;
	}

	public async Task ResolveHashes(IEnumerable<MapEntry> maps, Action<string> progress, CancellationToken ct)
	{
		MapEntry[] pending = maps.Where((MapEntry m) => m.Error == null && m.GameHash == null).ToArray();
		int index = 0;
		while (index < pending.Length)
		{
			ct.ThrowIfCancellationRequested();
			StringBuilder stringBuilder = new StringBuilder("set -o pipefail; ");
			do
			{
				string text = $"if value=$({HashCommand(pending[index])}); then printf '%s %s\\n' {index} \"$value\"; fi; ";
				if (stringBuilder.Length > 18 && stringBuilder.Length + text.Length > 16000)
				{
					break;
				}
				if (text.Length > 16000)
				{
					progress("Map paths are too long: " + pending[index].Title);
					index++;
				}
				else
				{
					stringBuilder.Append(text);
					index++;
				}
			}
			while (index < pending.Length);
			string[] array = (await device.Shell(stringBuilder.ToString())).Split('\n', StringSplitOptions.RemoveEmptyEntries);
			for (int num = 0; num < array.Length; num++)
			{
				string[] array2 = array[num].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
				if (array2.Length >= 2 && int.TryParse(array2[0], out var result) && result >= 0 && result < pending.Length)
				{
					pending[result].GameHash = ParseHash(array2[1]);
				}
			}
			progress($"Indexing tracks: {index}/{pending.Length}");
		}
	}

	private static string HashCommand(MapEntry map)
	{
		foreach (string hashFile in map.HashFiles)
		{
			MapEntry.ValidateRelative(hashFile);
		}
		string[] source = map.HashFiles.Select((string x) => map.Folder + "/" + x).ToArray();
		string text = string.Join(" && ", source.Select((string p) => "test -f " + AdbClient.Q(p)));
		string text2 = string.Join("; ", source.Select((string p) => "cat " + AdbClient.Q(p) + " || exit 1"));
		return text + " && ( " + text2 + " ) | sha1sum";
	}

	private static string? ParseHash(string output)
	{
		string text = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		if (text == null || text.Length != 40 || !text.All(Uri.IsHexDigit))
		{
			return null;
		}
		return text.ToUpperInvariant();
	}

	public async Task<List<PlaylistDocument>> ReadPlaylists(Action<string> progress)
	{
		List<PlaylistDocument> result = new List<PlaylistDocument>();
		string[] array = await device.Find("/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists", "! -name '*.tmp'", topLevelOnly: true);
		progress($"{"/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists"} — playlist files found: {array.Length}");
		string[] array2 = array;
		foreach (string path in array2)
		{
			try
			{
				byte[] array3 = await device.Read(path, 8388608);
				PlaylistDocument playlistDocument = PlaylistDocument.Parse(Encoding.UTF8.GetString(array3));
				playlistDocument.RemotePath = path;
				playlistDocument.RemoteDigest = Digest(array3);
				playlistDocument.LocalId = Digest(Encoding.UTF8.GetBytes(path)).Substring(0, 24);
				result.Add(playlistDocument);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				await device.CheckConnection();
				progress("Skipped playlist: " + path + " — " + ex2.Message);
			}
		}
		return result;
	}

	public static string Digest(byte[] b)
	{
		return Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
	}
}
