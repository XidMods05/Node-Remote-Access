using System.Text;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using NRA.Broker.AbsTcp;
using NRA.Broker.AbsTcp.IO;
using NRA.Broker.System.Application;
using NRA.Broker.System.IO;
using WatsonTcp;

namespace NRA.RpcProxy.Client;

/// <summary>
///     Interceptor class for proxying method calls to remote servers using either WatsonTcp or AbsTcp.
/// </summary>
/// <param name="useWatsonTcp">A boolean indicating whether to use WatsonTcp for communication.</param>
/// <param name="host">The host address of the remote server.</param>
/// <param name="port">The port number of the remote server.</param>
internal class Interceptor(bool useWatsonTcp, string host, int port) : IInterceptor
{
    private AbsTcpClient? _absTcpClient;
    private WatsonTcpShellApplication? _watsonTcpShellApplication;

    /// <summary>
    ///     Intercepts method calls and forwards them to the remote server.
    /// </summary>
    /// <param name="invocation">The details of the method call being intercepted.</param>
    public void Intercept(IInvocation invocation)
    {
        if (useWatsonTcp)
        {
            if (_watsonTcpShellApplication == null)
                throw new Exception("NraProxyClient must be initialized!");

            var req = new ShellRequest
            {
                Id = _watsonTcpShellApplication.Id,
                JsonData = JsonConvert.SerializeObject(new object[]
                    { invocation.Method.DeclaringType!.Name + "Proxy", invocation.Method.Name, invocation.Arguments })
            };

            _watsonTcpShellApplication.Shell.AddReq(req, response =>
            {
                var data = JsonConvert.DeserializeObject<object[]>(response.JsonData)!;

                var status = data[0].ToString()!;
                if (status.Contains("fail", StringComparison.CurrentCultureIgnoreCase)) throw new Exception(status);

                var args = JsonConvert.DeserializeObject<object[]>(data[1].ToString()!)!;
                for (var i = 0; i < invocation.Arguments.Length; i++)
                    invocation.Arguments[i] = args[i];

                invocation.ReturnValue = data[2];
            });

            return;
        }

        if (_absTcpClient == null)
            throw new Exception("NraProxyClient must be initialized!");

        var reqAbs = new AbsTcpRequest
        {
            ReqGuid = Guid.NewGuid(),
            ReqData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new object[]
                { invocation.Method.DeclaringType!.Name + "Proxy", invocation.Method.Name, invocation.Arguments }))
        };

        var resAbs = _absTcpClient.SendAndWait(reqAbs);
        if (resAbs == null) return;

        var data = JsonConvert.DeserializeObject<object[]>(Encoding.UTF8.GetString(resAbs.ResData))!;

        var status = data[0].ToString()!;
        if (status.Contains("fail", StringComparison.CurrentCultureIgnoreCase)) throw new Exception(status);

        var args = JsonConvert.DeserializeObject<object[]>(data[1].ToString()!)!;
        for (var i = 0; i < invocation.Arguments.Length; i++)
            invocation.Arguments[i] = args[i];

        invocation.ReturnValue = data[2];
    }

    /// <summary>
    ///     Initializes the Interceptor with the provided key and nonce.
    /// </summary>
    /// <param name="key">The encryption key for secure communication.</param>
    /// <param name="nonce">The nonce for secure communication.</param>
    public void Init(byte[] key, byte[] nonce)
    {
        if (useWatsonTcp)
        {
            _watsonTcpShellApplication = new WatsonTcpShellApplication(host, port, false,
                request => Task.FromResult(new SyncResponse(request, "")), key, nonce);
        }
        else
        {
            _absTcpClient = new AbsTcpClient(host, port, key, nonce);
            _absTcpClient.ConnectAsync();
        }
    }
}

/// <summary>
///     Represents a client for creating proxy interfaces to remote servers using either WatsonTcp or AbsTcp.
/// </summary>
/// <param name="useWatsonTcp">A boolean indicating whether to use WatsonTcp for communication.</param>
/// <param name="host">The host address of the remote server.</param>
/// <param name="port">The port number of the remote server.</param>
public class NraProxyClient(bool useWatsonTcp, string host, int port)
{
    private readonly ProxyGenerator _generator = new();
    private readonly Interceptor _interceptor = new(useWatsonTcp, host, port);

    /// <summary>
    ///     Initializes the NraProxyClient with the provided encryption key and nonce.
    /// </summary>
    /// <param name="key">The encryption key (32 len) for secure communication.</param>
    /// <param name="nonce">The nonce (8 len) for secure communication.</param>
    /// <exception cref="Exception">Thrown when the key length is not 32 or the nonce length is not 8.</exception>
    public void Init(byte[] key, byte[] nonce)
    {
        if (key.Length != 32) throw new Exception("NraConfig.CipherKey lenght must be 32");
        if (nonce.Length != 8) throw new Exception("Nonce lenght must be 8");

        _interceptor.Init(key, nonce);
    }

    /// <summary>
    ///     Creates a proxy interface for the specified type T, forwarding method calls to the remote server.
    /// </summary>
    /// <typeparam name="T">The interface type for which to create a proxy.</typeparam>
    /// <returns>An instance of the proxy interface.</returns>
    /// <exception cref="Exception">Thrown when T is not an interface.</exception>
    public virtual T GetProxyInterface<T>() where T : class
    {
        Thread.Yield();

        if (!typeof(T).IsInterface) throw new Exception("T must be interface!");
        return _generator.CreateInterfaceProxyWithoutTarget<T>(_interceptor);
    }
}