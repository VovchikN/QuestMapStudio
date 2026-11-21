using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QuestMapStudio.Core;

public sealed class LocalData
{
	private record Draft(string Id, string? Path, string? Digest, string Json, bool? Pending = null, bool PendingDelete = false);

	public string Root { get; }

	public Settings Settings { get; private set; } = new Settings();

	public LocalData(string? root = null)
	{
		Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuestMapStudio");
		Directory.CreateDirectory(Root);
	}

	public void LoadSettings()
	{
		string path = Path.Combine(Root, "settings.json");
		if (File.Exists(path))
		{
			Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
		}
	}

	public void SaveSettings()
	{
		Atomic(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(Settings));
	}

	private string DeviceDir(string serial)
	{
		if (string.IsNullOrWhiteSpace(serial))
		{
			throw new IOException("No Quest profile selected");
		}
		string text = Path.Combine(Root, "Devices", QuestRepository.Digest(Encoding.UTF8.GetBytes(serial)).Substring(0, 24));
		Directory.CreateDirectory(text);
		return text;
	}

	public string[] LoadQueue()
	{
		string path = Path.Combine(Root, "import-queue.json");
		if (!File.Exists(path))
		{
			return Array.Empty<string>();
		}
		return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>();
	}

	public void SaveQueue(IEnumerable<string> paths)
	{
		Atomic(Path.Combine(Root, "import-queue.json"), JsonSerializer.Serialize(paths));
	}

	public PlaylistOrderDraft? LoadOrderDraft(string serial)
	{
		string path = Path.Combine(DeviceDir(serial), "playlist-order-draft.json");
		if (!File.Exists(path))
		{
			return null;
		}
		return JsonSerializer.Deserialize<PlaylistOrderDraft>(File.ReadAllText(path));
	}

	public void SaveOrderDraft(string serial, PlaylistOrderDraft? order)
	{
		string path = Path.Combine(DeviceDir(serial), "playlist-order-draft.json");
		if (order == null)
		{
			File.Delete(path);
		}
		else
		{
			Atomic(path, JsonSerializer.Serialize(order));
		}
	}

	public List<MapEntry> LoadMaps(string serial)
	{
		string path = Path.Combine(DeviceDir(serial), "catalogue.json");
		if (!File.Exists(path))
		{
			return new List<MapEntry>();
		}
		return JsonSerializer.Deserialize<List<MapEntry>>(File.ReadAllText(path)) ?? new List<MapEntry>();
	}

	public void SaveMaps(string serial, List<MapEntry> maps)
	{
		Atomic(Path.Combine(DeviceDir(serial), "catalogue.json"), JsonSerializer.Serialize(maps));
	}

	public List<ImportBatch> LoadImports(string serial)
	{
		string path = Path.Combine(DeviceDir(serial), "imports.json");
		if (!File.Exists(path))
		{
			return new List<ImportBatch>();
		}
		return JsonSerializer.Deserialize<List<ImportBatch>>(File.ReadAllText(path)) ?? new List<ImportBatch>();
	}

	public void SaveImports(string serial, IEnumerable<ImportBatch> batches)
	{
		Atomic(Path.Combine(DeviceDir(serial), "imports.json"), JsonSerializer.Serialize(batches));
	}

	public string CoverPath(string serial, string path)
	{
		return Path.Combine(DeviceDir(serial), "Covers-v2", QuestRepository.Digest(Encoding.UTF8.GetBytes(path)) + ".png");
	}

	public List<PlaylistDocument> LoadDrafts(string serial)
	{
		List<PlaylistDocument> list = new List<PlaylistDocument>();
		string path = Path.Combine(DeviceDir(serial), "Drafts");
		if (!Directory.Exists(path))
		{
			return list;
		}
		foreach (string item in Directory.EnumerateFiles(path, "*.json"))
		{
			Draft draft = JsonSerializer.Deserialize<Draft>(File.ReadAllText(item)) ?? throw new IOException("Invalid draft: " + item);
			PlaylistDocument playlistDocument = PlaylistDocument.Parse(draft.Json);
			playlistDocument.LocalId = draft.Id;
			playlistDocument.RemotePath = draft.Path;
			playlistDocument.RemoteDigest = draft.Digest;
			playlistDocument.Pending = draft.Pending ?? (draft.Path == null);
			playlistDocument.PendingDelete = draft.PendingDelete;
			list.Add(playlistDocument);
		}
		return list;
	}

	public void SaveDraft(string serial, PlaylistDocument d)
	{
		if (!d.LocalId.All(char.IsAsciiLetterOrDigit))
		{
			throw new IOException("Invalid draft ID");
		}
		string text = Path.Combine(DeviceDir(serial), "Drafts");
		Directory.CreateDirectory(text);
		Atomic(Path.Combine(text, d.LocalId + ".json"), JsonSerializer.Serialize(new Draft(d.LocalId, d.RemotePath, d.RemoteDigest, Encoding.UTF8.GetString(d.Bytes()), d.Pending, d.PendingDelete)));
	}

	public void DeleteDraft(string serial, PlaylistDocument d)
	{
		if (!d.LocalId.All(char.IsAsciiLetterOrDigit))
		{
			throw new IOException("Invalid ID");
		}
		string text = Path.Combine(DeviceDir(serial), "Drafts", d.LocalId + ".json");
		if (File.Exists(text))
		{
			string text2 = Path.Combine(Root, "Backups");
			Directory.CreateDirectory(text2);
			File.Copy(text, Path.Combine(text2, "draft-" + Guid.NewGuid().ToString("N") + ".json"));
			File.Delete(text);
		}
	}

	public static void Atomic(string path, string text)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text2 = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text2, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text2, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
	}
}
