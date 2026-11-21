using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public sealed class AdbClient(string executable, string? serial = null, CancellationToken cancellation = default(CancellationToken)) : IMapDevice
{
	public string? Serial => serial;

	public async Task<byte[]> Run(IEnumerable<string> arguments, int maxBytes = 16777216, CancellationToken? token = null)
	{
		CancellationToken ct = token ?? cancellation;
		ProcessStartInfo processStartInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		if (serial != null)
		{
			processStartInfo.ArgumentList.Add("-s");
			processStartInfo.ArgumentList.Add(serial);
		}
		foreach (string argument in arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		Process p = Process.Start(processStartInfo) ?? throw new IOException("Could not start ADB");
		try
		{
			using (ct.Register(() =>
			{
				try
				{
					p.Kill(entireProcessTree: true);
				}
				catch
				{
				}
			}))
			{
				Task<string> error = p.StandardError.ReadToEndAsync(ct);
				using MemoryStream output = new MemoryStream();
				byte[] buffer = new byte[65536];
				try
				{
					int num;
					while ((num = await p.StandardOutput.BaseStream.ReadAsync(buffer, ct)) > 0)
					{
						if (output.Length + num > maxBytes)
						{
							throw new IOException("ADB response exceeded its size limit");
						}
						output.Write(buffer, 0, num);
					}
					await p.WaitForExitAsync(ct);
					string text = await error;
					ct.ThrowIfCancellationRequested();
					if (p.ExitCode != 0)
					{
						throw new IOException("ADB: " + text.Trim() + " (code " + p.ExitCode + ")");
					}
					return output.ToArray();
				}
				catch
				{
					try
					{
						if (!p.HasExited)
						{
							p.Kill(entireProcessTree: true);
						}
					}
					catch
					{
					}
					throw;
				}
			}
		}
		finally
		{
			if (p != null)
			{
				((IDisposable)p).Dispose();
			}
		}
	}

	public async Task<List<DeviceInfo>> Devices()
	{
		Encoding uTF = Encoding.UTF8;
		string text = uTF.GetString(await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "devices", "-l" })));
		List<DeviceInfo> list = new List<DeviceInfo>();
		string[] array = text.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			bool flag = array2.Length >= 2;
			if (flag)
			{
				bool flag2;
				switch (array2[1])
				{
				case "device":
				case "offline":
				case "unauthorized":
				case "recovery":
				case "sideload":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				list.Add(new DeviceInfo(array2[0], array2[1], string.Join(" ", array2.Skip(2))));
			}
		}
		return list;
	}

	public async Task CheckConnection()
	{
		if (serial == null)
		{
			throw new InvalidOperationException("Select a connected Quest");
		}
		Encoding uTF = Encoding.UTF8;
		if (uTF.GetString(await Run(new _003C_003Ez__ReadOnlySingleElementList<string>("get-state"))).Trim() != "device")
		{
			throw new IOException("Quest is disconnected or unauthorized. Connect USB and allow debugging in the headset.");
		}
	}

	public async Task<string> Shell(string command)
	{
		Encoding uTF = Encoding.UTF8;
		return uTF.GetString(await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "shell", command })));
	}

	public async Task Cleanup(string command)
	{
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20L));
		await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "shell", command }), 16777216, timeout.Token);
	}

	public Task Upload(string local, string remote)
	{
		return Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "push", local, remote }), 1048576);
	}

	public async Task PullFile(string remote, string local, long limit)
	{
		if (!long.TryParse((await Shell("stat -c %s " + Q(remote))).Trim(), out var length) || length <= 0 || length > limit)
		{
			throw new IOException("The cover image exceeds the file size limit.");
		}
		await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "pull", remote, local }), 1048576);
		if (new FileInfo(local).Length != length)
		{
			throw new IOException("The cover image size changed during transfer.");
		}
	}

	public async Task<byte[]> Read(string path, int limit = 4194304)
	{
		return await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3]
		{
			"shell",
			"-T",
			"cat " + Q(path)
		}), limit);
	}

	public async Task<string[]> Find(string root, string expression, bool topLevelOnly = false)
	{
		string text = "if [ -d " + Q(root) + " ]; then find " + Q(root) + (topLevelOnly ? " -maxdepth 1" : "") + " -type f " + expression + " -print0; fi";
		byte[] array = await Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "shell", "-T", text }), 33554432);
		if (array.Length != 0 && array[^1] != 0)
		{
			throw new IOException("Invalid Quest file list: " + root);
		}
		return Encoding.UTF8.GetString(array).Split('\0', StringSplitOptions.RemoveEmptyEntries);
	}

	public static string Q(string s)
	{
		return "'" + s.Replace("'", "'\\''") + "'";
	}
}
