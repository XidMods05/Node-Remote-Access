using NRA.Broker.System.IO;

namespace NRA.Broker.System.Abstract;

public interface IShell
{
    public void AddReq(ShellRequest request, Action<ShellResponse> response);
}