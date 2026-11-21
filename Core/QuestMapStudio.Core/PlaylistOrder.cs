using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class PlaylistOrder(AdbClient adb)
{
	public const string ConfigDirectory = "/sdcard/ModData/com.beatgames.beatsaber/Configs";

	public const string ConfigPath = "/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json";

	private readonly PlaylistDevice device = new PlaylistDevice(adb);

	public async Task<string[]> Read()
	{
		return Paths((await ReadConfig()).Item1);
	}

	private async Task<(JsonObject Document, string? Digest)> ReadConfig()
	{
		if (await device.Digest("/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json") == null)
		{
			return (Document: new JsonObject(), Digest: null);
		}
		byte[] array = await adb.Read("/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json");
		return (Document: (JsonNode.Parse(array) as JsonObject) ?? throw new IOException("Invalid PlaylistCore config."), Digest: QuestRepository.Digest(array));
	}

	private static string[] Paths(JsonObject config)
	{
		if (!config.ContainsKey("order"))
		{
			return Array.Empty<string>();
		}
		return ((config["order"] as JsonArray) ?? throw new IOException("Invalid PlaylistCore order.")).Select((JsonNode node) =>
		{
			if (!(node is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value) || value == null)
			{
				throw new IOException("Invalid path in PlaylistCore order.");
			}
			return value;
		}).ToArray();
	}

	public async Task<string[]> Save(string[] baseline, string[] orderedPaths, CancellationToken ct)
	{
		await device.Check();
		(JsonObject, string) tuple = await ReadConfig();
		JsonObject item = tuple.Item1;
		string digest = tuple.Item2;
		string[] array = Paths(item);
		string[] array2 = orderedPaths.Distinct(StringComparer.Ordinal).ToArray();
		HashSet<string> hashSet = array2.ToHashSet(StringComparer.Ordinal);
		List<string> desired = new List<string>(array2.Length + array.Length);
		int num = 0;
		string[] array3 = array;
		foreach (string item2 in array3)
		{
			if (hashSet.Contains(item2))
			{
				if (num < array2.Length)
				{
					desired.Add(array2[num++]);
				}
			}
			else
			{
				desired.Add(item2);
			}
		}
		desired.AddRange(array2.Skip(num));
		if (array.SequenceEqual(desired))
		{
			return desired.ToArray();
		}
		if (!array.SequenceEqual(baseline))
		{
			throw new IOException("Playlist order changed on Quest. Revert the staged order, refresh, then reorder again.");
		}
		JsonArray jsonArray = new JsonArray();
		foreach (string item3 in desired)
		{
			jsonArray.Add(item3);
		}
		item["order"] = jsonArray;
		byte[] bytes = Encoding.UTF8.GetBytes(item.ToJsonString());
		string expected = QuestRepository.Digest(bytes);
		string stage = "/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json." + Guid.NewGuid().ToString("N") + ".tmp";
		string temp = Path.GetTempFileName();
		string[] result;
		try
		{
			await File.WriteAllBytesAsync(temp, bytes, ct);
			await device.Shell("mkdir -p " + AdbClient.Q("/sdcard/ModData/com.beatgames.beatsaber/Configs"));
			await device.Upload(temp, stage);
			if (await device.Digest(stage) != expected)
			{
				throw new IOException("The staged playlist order failed its checksum.");
			}
			await device.Check();
			ct.ThrowIfCancellationRequested();
			if (await device.Digest("/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json") != digest)
			{
				throw new IOException("PlaylistCore settings changed during Apply. Retry to keep the new settings.");
			}
			await device.Shell("mv -f " + AdbClient.Q(stage) + " " + AdbClient.Q("/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json"));
			if (await device.Digest("/sdcard/ModData/com.beatgames.beatsaber/Configs/PlaylistCore.json") != expected)
			{
				throw new IOException("Playlist order verification failed on Quest.");
			}
			result = desired.ToArray();
		}
		finally
		{
			File.Delete(temp);
			try
			{
				await device.Cleanup("rm -f " + AdbClient.Q(stage));
			}
			catch (IOException)
			{
			}
		}
		return result;
	}
}
