using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class PlaylistStore(IPlaylistDevice device)
{
	private const string BackupRoot = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Backups/Playlists";

	public static string DefaultPath(PlaylistDocument playlist)
	{
		return "/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists/QMS-" + playlist.LocalId + ".bplist";
	}

	public async Task<string> Save(PlaylistDocument doc, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		await device.Check();
		if (!doc.LocalId.All(char.IsAsciiLetterOrDigit) || doc.LocalId.Length == 0)
		{
			throw new IOException("Invalid playlist ID.");
		}
		string path = doc.RemotePath ?? DefaultPath(doc);
		ValidatePath(path);
		byte[] bytes = doc.Bytes();
		if (bytes.Length > 8388608)
		{
			throw new IOException("Playlist exceeds 8 MiB.");
		}
		string expected = QuestRepository.Digest(bytes);
		if (await device.Digest(path) == expected)
		{
			ct.ThrowIfCancellationRequested();
			doc.RemotePath = path;
			doc.RemoteDigest = expected;
			return "Playlist already saved.";
		}
		await CheckVersion(doc, path);
		string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		string temp = Path.GetTempFileName();
		string backup = null;
		string result;
		try
		{
			await File.WriteAllBytesAsync(temp, bytes, ct);
			await device.Shell("mkdir -p " + Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists") + " " + Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Backups/Playlists"));
			await device.Upload(temp, stage);
			if (await device.Digest(stage) != expected)
			{
				throw new IOException("The uploaded playlist failed its checksum");
			}
			ct.ThrowIfCancellationRequested();
			await device.Check();
			await CheckVersion(doc, path);
			if (doc.RemoteDigest != null)
			{
				backup = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Backups/Playlists/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bplist";
				await device.Shell("cp " + Q(path) + " " + Q(backup));
				if (await device.Digest(backup) != doc.RemoteDigest)
				{
					throw new IOException("The backup failed its checksum");
				}
			}
			await device.Shell("mv -f " + Q(stage) + " " + Q(path));
			if (await device.Digest(path) != expected)
			{
				throw new IOException("Playlist verification failed. Backup: " + backup);
			}
			doc.RemotePath = path;
			doc.RemoteDigest = expected;
			result = backup ?? "Playlist created";
		}
		finally
		{
			File.Delete(temp);
			try
			{
				await device.Cleanup("rm -f " + Q(stage));
			}
			catch
			{
			}
		}
		return result;
	}

	public async Task<string> Delete(PlaylistDocument doc)
	{
		if (doc.RemotePath == null)
		{
			return "Local draft";
		}
		ValidatePath(doc.RemotePath);
		await device.Check();
		if (await device.Digest(doc.RemotePath) == null)
		{
			doc.RemotePath = null;
			doc.RemoteDigest = null;
			return "Playlist already removed.";
		}
		await CheckVersion(doc, doc.RemotePath);
		string backup = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Backups/Playlists/deleted-" + Guid.NewGuid().ToString("N") + ".bplist";
		await device.Shell("mkdir -p " + Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Backups/Playlists") + " && cp " + Q(doc.RemotePath) + " " + Q(backup));
		if (await device.Digest(backup) != doc.RemoteDigest)
		{
			throw new IOException("Could not verify the playlist backup");
		}
		await device.Shell("rm " + Q(doc.RemotePath));
		doc.RemotePath = null;
		doc.RemoteDigest = null;
		return backup;
	}

	private async Task CheckVersion(PlaylistDocument d, string path)
	{
		if (await device.Digest(path) != d.RemoteDigest)
		{
			throw new IOException("The playlist changed on Quest. Export a copy of this draft, then reload the playlist from Quest before retrying.");
		}
	}

	private static string Q(string x)
	{
		return AdbClient.Q(x);
	}

	private static void ValidatePath(string p)
	{
		if (!p.StartsWith("/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists/", StringComparison.Ordinal) || p.Substring("/sdcard/ModData/com.beatgames.beatsaber/Mods/PlaylistManager/Playlists".Length + 1).Contains('/') || p.Contains("/../") || p.Any(char.IsControl))
		{
			throw new IOException("Invalid playlist path");
		}
	}
}
