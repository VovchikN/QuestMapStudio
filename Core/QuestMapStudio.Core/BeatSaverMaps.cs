using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class BeatSaverMaps(HttpClient http)
{
	public static bool IsHash(string value)
	{
		if (value.Length == 40)
		{
			return value.All(Uri.IsHexDigit);
		}
		return false;
	}

	public static bool IsCode(string value)
	{
		int length = value.Length;
		if (length > 0 && length <= 12)
		{
			return value.All(Uri.IsHexDigit);
		}
		return false;
	}

	public async Task<BeatSaverMap?> Resolve(string hash, string code, CancellationToken ct)
	{
		bool byHash = IsHash(hash);
		if (!byHash && !IsCode(code))
		{
			return null;
		}
		string text = (byHash ? ("hash/" + hash) : ("id/" + code));
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(40L));
		using HttpResponseMessage response = await http.GetAsync("https://api.beatsaver.com/maps/" + text, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
		response.EnsureSuccessStatusCode();
		using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
		using MemoryStream data = new MemoryStream();
		await CopyLimited(body, data, 2097152L, timeout.Token);
		JsonNode jsonNode = JsonNode.Parse(data.ToArray());
		if (!(jsonNode is JsonObject) || !(jsonNode["versions"] is JsonArray jsonArray))
		{
			return null;
		}
		foreach (JsonNode item in jsonArray)
		{
			string text2 = MapEntry.S(item, "hash");
			if (IsHash(text2) && (!byHash || hash.Equals(text2, StringComparison.OrdinalIgnoreCase)) && (byHash || MapEntry.S(item, "state", "Published").Equals("Published", StringComparison.OrdinalIgnoreCase)) && Uri.TryCreate(MapEntry.S(item, "downloadURL"), UriKind.Absolute, out Uri result) && Allowed(result))
			{
				string text3 = MapEntry.S(jsonNode, "id");
				return new BeatSaverMap(text2.ToUpperInvariant(), IsCode(text3) ? text3 : null, result);
			}
		}
		return null;
	}

	public async Task Download(BeatSaverMap map, string path, CancellationToken ct)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromMinutes(5L));
		Uri url = map.Download;
		for (int hop = 0; hop < 4; hop++)
		{
			if (!Allowed(url))
			{
				throw new IOException("BeatSaver returned an unsupported download address.");
			}
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			int statusCode = (int)response.StatusCode;
			if (((uint)(statusCode - 301) > 2u && (uint)(statusCode - 307) > 1u) || 1 == 0)
			{
				goto IL_016a;
			}
			Uri location = response.Headers.Location;
			if ((object)location == null)
			{
				goto IL_016a;
			}
			url = new Uri(url, location);
			goto end_IL_0110;
			IL_016a:
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > 1073741824)
			{
				throw new InvalidDataException("Download exceeds 1 GiB.");
			}
			await using (Stream source = await response.Content.ReadAsStreamAsync(timeout.Token))
			{
				await using (FileStream destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
				{
					await CopyLimited(source, destination, 1073741824L, timeout.Token);
				}
				return;
			}
			end_IL_0110:;
		}
		throw new IOException("Too many BeatSaver download redirects.");
	}

	private static bool Allowed(Uri url)
	{
		if (url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0)
		{
			if (!(url.Host == "beatsaver.com"))
			{
				return url.Host.EndsWith(".beatsaver.com", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private static async Task CopyLimited(Stream source, Stream destination, long limit, CancellationToken ct)
	{
		byte[] buffer = new byte[81920];
		long total = 0L;
		while (true)
		{
			int num = await source.ReadAsync(buffer, ct);
			if (num != 0)
			{
				total += num;
				if (total > limit)
				{
					throw new InvalidDataException("The BeatSaver response exceeded its size limit.");
				}
				await destination.WriteAsync(buffer.AsMemory(0, num), ct);
				continue;
			}
			break;
		}
	}
}
