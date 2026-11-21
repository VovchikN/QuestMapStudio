using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class MapRecovery(AdbClient device, IEnumerable<string> allowedRoots)
{
	public const string Root = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps";

	private readonly string[] roots = allowedRoots.Distinct().ToArray();

	public async Task<List<RecoverableMap>> List(Action<string> report)
	{
		List<RecoverableMap> result = new List<RecoverableMap>();
		string[] array = await device.Find("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps", "-name original.json");
		foreach (string text in array)
		{
			string parent = text.Substring(0, text.LastIndexOf('/'));
			if (!parent.StartsWith("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps/", StringComparison.Ordinal))
			{
				continue;
			}
			string id = parent.Substring("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps".Length + 1);
			if (!ValidId(id))
			{
				continue;
			}
			try
			{
				RecoverableMap item = JsonSerializer.Deserialize<RecoverableMap>(await device.Read(text, 65536)) ?? throw new InvalidDataException("Пустая запись восстановления.");
				if (item.Id != id)
				{
					throw new InvalidDataException("Не совпадает идентификатор карты.");
				}
				ValidateSource(item.Folder, item.InfoName);
				if ((await device.Shell("test -d " + Q(parent + "/map") + " && printf exists || true")).Trim() == "exists")
				{
					result.Add(item);
				}
			}
			catch (Exception ex) when ((ex is IOException || ex is ArgumentException || ex is JsonException) ? true : false)
			{
				await device.CheckConnection();
				report("Папка восстановления: " + id + " — " + ex.Message);
			}
		}
		return result;
	}

	public async Task Move(MapEntry map)
	{
		ValidateSource(map.Folder, map.InfoName);
		await CheckGameClosed();
		RecoverableMap recoverableMap = new RecoverableMap(Guid.NewGuid().ToString("N"), map.Folder, map.InfoName, map.Title);
		string text = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps/" + recoverableMap.Id;
		string value = text + "/map";
		string value2 = JsonSerializer.Serialize(recoverableMap);
		await device.Shell($"test ! -L {Q(map.Folder)} && test -f {Q(map.Folder + "/" + map.InfoName)} && mkdir -p {Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps")} && mkdir {Q(text)} && printf %s {Q(value2)} > {Q(text + "/original.json")} && test ! -e {Q(value)} && mv -n {Q(map.Folder)} {Q(value)} && test -d {Q(value)} && test ! -e {Q(map.Folder)}");
	}

	public async Task Restore(RecoverableMap item)
	{
		if (!ValidId(item.Id))
		{
			throw new IOException("Неверный идентификатор папки восстановления.");
		}
		ValidateSource(item.Folder, item.InfoName);
		await CheckGameClosed();
		string dir = "/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps/" + item.Id;
		string payload = dir + "/map";
		string parent = item.Folder.Substring(0, item.Folder.LastIndexOf('/'));
		if ((await device.Shell("test -e " + Q(item.Folder) + " && printf exists || true")).Trim() == "exists")
		{
			throw new IOException("На исходном месте уже есть карта. Восстановление не перезаписывает файлы.");
		}
		await device.Shell($"test -d {Q(parent)} && test ! -L {Q(dir)} && test ! -L {Q(payload)} && test -f {Q(payload + "/" + item.InfoName)} && test ! -e {Q(item.Folder)} && mv -n {Q(payload)} {Q(item.Folder)} && test ! -e {Q(payload)} && test -f {Q(item.Folder + "/" + item.InfoName)}");
		await device.Shell("rm -f " + Q(dir + "/original.json") + " && rmdir " + Q(dir));
	}

	public async Task Clear()
	{
		await CheckGameClosed();
		await device.Shell($"test ! -L {Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps")} && if [ -d {Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps")} ]; then for item in {Q("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/Recovery/Maps")}/*; do name=${{item##*/}}; " + "case \"$name\" in *[!0-9a-f]*) continue ;; esac; [ \"${#name}\" -eq 32 ] && [ -d \"$item\" ] && [ ! -L \"$item\" ] || continue; rm -rf \"$item\" || exit 1; done; fi");
	}

	private async Task CheckGameClosed()
	{
		await device.CheckConnection();
		if (!string.IsNullOrWhiteSpace(await device.Shell("pidof com.beatgames.beatsaber || true")))
		{
			throw new IOException("Закройте Beat Saber перед перемещением, восстановлением или очисткой карт.");
		}
	}

	private void ValidateSource(string folder, string info)
	{
		if (!string.Equals(info, "info.dat", StringComparison.OrdinalIgnoreCase) || !folder.StartsWith("/sdcard/", StringComparison.Ordinal) || folder.Any(char.IsControl) || folder.Split('/').Skip(1).Any((string p) =>
		{
			switch (p)
			{
			default:
				if (p.Length != 0)
				{
					break;
				}
				goto case ".";
			case ".":
			case "..":
				return true;
			case null:
				break;
			}
			return false;
		}) || roots.Any((string root) => string.Equals(root.TrimEnd('/'), folder, StringComparison.Ordinal)) || !roots.Any((string root) => folder.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)) || folder.StartsWith("/sdcard/ModData/com.beatgames.beatsaber/Mods/QuestMapStudio/", StringComparison.Ordinal))
		{
			throw new IOException("Карта находится вне каталогов карт или имеет некорректный путь.");
		}
	}

	private static bool ValidId(string id)
	{
		if (Guid.TryParseExact(id, "N", out var result))
		{
			return result.ToString("N") == id;
		}
		return false;
	}

	private static string Q(string value)
	{
		return AdbClient.Q(value);
	}
}
