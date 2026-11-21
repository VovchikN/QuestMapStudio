using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class PlaylistDevice(AdbClient client) : IPlaylistDevice
{
	public async Task Check()
	{
		await client.CheckConnection();
		if (!string.IsNullOrWhiteSpace(await client.Shell("pidof com.beatgames.beatsaber || true")))
		{
			throw new IOException("Close Beat Saber on Quest before applying changes.");
		}
	}

	public async Task<string?> Digest(string path)
	{
		string text = (await client.Shell("if [ -f " + AdbClient.Q(path) + " ]; then sha256sum " + AdbClient.Q(path) + "; fi")).Trim();
		if (text.Length == 0)
		{
			return null;
		}
		string text2 = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
		if (text2.Length != 64 || !text2.All(Uri.IsHexDigit))
		{
			throw new IOException("Playlist checksum could not be read");
		}
		return text2.ToLowerInvariant();
	}

	public Task Upload(string local, string remote)
	{
		return client.Upload(local, remote);
	}

	public Task Cleanup(string command)
	{
		return client.Cleanup(command);
	}

	public async Task Shell(string command)
	{
		await client.Shell(command);
	}
}
