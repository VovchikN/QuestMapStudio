using System.Threading.Tasks;

namespace QuestMapStudio.Core;

public interface IMapDevice
{
	Task CheckConnection();

	Task<string> Shell(string command);

	Task Upload(string local, string remote);

	Task Cleanup(string command)
	{
		return Shell(command);
	}
}
