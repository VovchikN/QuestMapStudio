using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class MapInstaller
{
	public const string Destination = "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels";

	private const string StagingRoot = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/MapImport";

	private readonly IMapDevice _device;

	public string? CleanupWarning { get; private set; }

	public static string InstalledFolder(MapArchive map)
	{
		return "/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels/QMS-" + map.ContentHash;
	}

	public MapInstaller(IMapDevice device)
	{
		_device = device;
	}

	public async Task<string> Install(string zip, MapArchive map, Action<string> progress, CancellationToken cancel)
	{
		CleanupWarning = null;
		await _device.CheckConnection();
		if (!string.IsNullOrWhiteSpace(await _device.Shell("pidof com.beatgames.beatsaber || true")))
		{
			throw new InvalidOperationException("Close Beat Saber on Quest before installing.");
		}
		string final = InstalledFolder(map);
		if ((await _device.Shell("if [ -d " + Q(final) + " ]; then echo exists; fi")).Trim() == "exists")
		{
			return "Already installed: " + map.Title;
		}
		string stage = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/MapImport/" + Guid.NewGuid().ToString("N");
		bool stageCreated = false;
		string text = Path.Combine(Path.GetTempPath(), "QuestMapStudio", "PreparedMaps");
		Directory.CreateDirectory(text);
		string prepared = Path.Combine(text, Guid.NewGuid().ToString("N") + ".zip");
		string result2;
		try
		{
			cancel.ThrowIfCancellationRequested();
			progress("Preparing ZIP for transfer…");
			await Task.Run(() =>
			{
				MapArchive.PrepareForDevice(zip, prepared, map, cancel);
			}, cancel);
			await _device.Shell("mkdir -p " + Q(stage) + " " + Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels"));
			stageCreated = true;
			string[] array = (await _device.Shell("df -k " + Q(stage))).Trim().Split('\n').Last()
				.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (array.Length < 4 || !long.TryParse(array[3], out var result))
			{
				throw new IOException("Could not check free space on Quest.");
			}
			if (result * 1024 < map.ExpandedBytes + new FileInfo(prepared).Length + 33554432)
			{
				throw new IOException("There is not enough free space on Quest.");
			}
			progress("Transferring ZIP to Quest…");
			await _device.Upload(prepared, stage + "/map.zip");
			cancel.ThrowIfCancellationRequested();
			using FileStream f = File.OpenRead(prepared);
			using SHA256 sha = SHA256.Create();
			string digest = Convert.ToHexString(await sha.ComputeHashAsync(f, cancel)).ToLowerInvariant();
			string b = (await _device.Shell("sha256sum " + Q(stage + "/map.zip"))).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
			if (!string.Equals(digest, b, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("The transferred ZIP failed its checksum.");
			}
			progress("Extracting ZIP on Quest…");
			await _device.Shell("unzip -q " + Q(stage + "/map.zip") + " -d " + Q(stage + "/files"));
			cancel.ThrowIfCancellationRequested();
			string s = stage + "/files";
			await _device.Shell($"test ! -e {Q(final)} && mv -n {Q(s)} {Q(final)} && test -d {Q(final)}");
			result2 = "Installed: " + map.Title + ". Restart Beat Saber to refresh the list.";
		}
		finally
		{
			try
			{
				if (File.Exists(prepared))
				{
					File.Delete(prepared);
				}
			}
			catch (Exception ex)
			{
				CleanupWarning = "Could not remove temporary ZIP on the computer: " + prepared + ". " + ex.Message;
			}
			if (stageCreated)
			{
				try
				{
					await _device.Cleanup("rm -rf " + Q(stage));
				}
				catch (Exception ex2)
				{
					CleanupWarning = "Quest disconnected. Temporary folder remains: " + stage + ". " + ex2.Message;
					progress(CleanupWarning);
				}
			}
		}
		return result2;
	}

	private static string Q(string s)
	{
		return "'" + s.Replace("'", "'\\''") + "'";
	}
}
