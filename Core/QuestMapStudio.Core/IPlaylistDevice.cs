using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public interface IPlaylistDevice
{
	Task Check();

	Task<string?> Digest(string path);

	Task Upload(string local, string remote);

	Task Shell(string command);

	Task Cleanup(string command)
	{
		return Shell(command);
	}
}
