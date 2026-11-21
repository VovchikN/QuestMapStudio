using System;

namespace QuestMapStudio.Core;

public sealed class Settings
{
	public string AdbPath { get; set; } = "";

	public string LastSerial { get; set; } = "";

	public string[] ExtraRoots { get; set; } = Array.Empty<string>();
}
