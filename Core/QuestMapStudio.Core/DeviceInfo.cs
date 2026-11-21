namespace QuestMapStudio.Core;

public sealed record DeviceInfo(string Serial, string State, string Description)
{
	public override string ToString()
	{
		return Serial + " — " + State + " " + Description;
	}
}
