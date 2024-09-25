using System.Collections.Concurrent;
using System.Text;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using NRA.Broker.AbsTcp;
using NRA.Broker.AbsTcp.IO;
using NRA.Broker.System.Application;
using WatsonTcp;

namespace NRA.RpcProxy.Server;

/// <summary>
///     Interceptor class for handling proxy interception and communication.
/// </summary>
/// <param name="useWatsonTcp">A boolean indicating whether to use WatsonTcp for communication.</param>
/// <param name="host">The host address for the communication.</param>
/// <param name="port">The port number for the communication.</param>
internal class Interceptor(bool useWatsonTcp, string host, int port) : IInterceptor
{
    /// <summary>
    ///     A concurrent dictionary to store saved proxies.
    /// </summary>
    internal readonly ConcurrentDictionary<string, Func<(string, object[]), (bool, object[], object)>> SavedProxies =
        new();

    private AbsTcpServer? _absTcpServer;

    private WatsonTcpShellApplication? _watsonTcpShellApplication;

    /// <summary>
    ///     Intercepts the method invocation.
    /// </summary>
    /// <param name="invocation">The invocation context.</param>
    public void Intercept(IInvocation invocation)
    {
        if (useWatsonTcp)
        {
            if (_watsonTcpShellApplication == null) throw new Exception("NraProxyClient must be initialized!");
        }
        else if (_absTcpServer == null)
        {
            throw new Exception("NraProxyClient must be initialized!");
        }

        invocation.Proceed();
    }

    /// <summary>
    ///     Initializes the communication server based on the provided parameters.
    /// </summary>
    /// <param name="key">The encryption key for the communication.</param>
    /// <param name="nonce">The nonce for the communication.</param>
    public void Init(byte[] key, byte[] nonce)
    {
        if (useWatsonTcp)
        {
            _watsonTcpShellApplication = new WatsonTcpShellApplication(host, port, true, request =>
            {
                var data = JsonConvert.DeserializeObject<object[]>(Encoding.UTF8.GetString(request.Data))!;

                if (!SavedProxies.TryGetValue(data[0].ToString()!, out var proxy))
                    return Task.FromResult(new SyncResponse(request,
                        JsonConvert.SerializeObject(new object[] { "fail Proxy not found!" })));

                var res = proxy.Invoke((data[1].ToString()!,
                    JsonConvert.DeserializeObject<object[]>(data[2].ToString()!))!);

                return Task.FromResult(new SyncResponse(request,
                    JsonConvert.SerializeObject(new[]
                        { res.Item1 ? "normal" : "fail unknown!", res.Item2, res.Item3 })));
            }, key, nonce);

            return;
        }

        _absTcpServer = new AbsTcpServer(host, port, request =>
        {
            var data = JsonConvert.DeserializeObject<object[]>(Encoding.UTF8.GetString(request.ReqData))!;

            if (!SavedProxies.TryGetValue(data[0].ToString()!, out var proxy))
                return new AbsTcpResponse
                {
                    ResGuid = request.ReqGuid!.Value,
                    ResData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new object[]
                        { "fail Proxy not found!" }))
                };

            var res = proxy.Invoke((data[1].ToString()!,
                JsonConvert.DeserializeObject<object[]>(data[2].ToString()!))!);

            return new AbsTcpResponse
            {
                ResGuid = request.ReqGuid!.Value,
                ResData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new[]
                    { res.Item1 ? "normal" : "fail", res.Item2, res.Item3 }))
            };
        }, key, nonce);
        _absTcpServer.Start();
    }
}

/// <summary>
///     Represents a server for creating proxy interfaces and handling communication.
/// </summary>
/// <param name="useWatsonTcp">A boolean indicating whether to use WatsonTcp for communication.</param>
/// <param name="host">The host address for the communication.</param>
/// <param name="port">The port number for the communication.</param>
public class NraProxyServer(bool useWatsonTcp, string host, int port)
{
    private readonly ProxyGenerator _generator = new();
    private readonly Interceptor _interceptor = new(useWatsonTcp, host, port);
    
    private readonly ConcurrentDictionary<string, object> _savedProxies = new();

    /// <summary>
    ///     Initializes the communication server with the provided encryption key and nonce.
    /// </summary>
    /// <param name="key">The encryption key (32 len) for the communication.</param>
    /// <param name="nonce">The nonce (8 len) for the communication.</param>
    public void Init(byte[] key, byte[] nonce)
    {
        if (key.Length != 32) throw new Exception("NraConfig.CipherKey lenght must be 32");
        if (nonce.Length != 8) throw new Exception("Nonce lenght must be 8");

        _interceptor.Init(key, nonce);
    }

    /// <summary>
    ///     Adds a proxy interface to the server for the specified target object.
    /// </summary>
    /// <typeparam name="T">The type of the interface.</typeparam>
    /// <param name="target">The target object to create the proxy for.</param>
    /// <returns>The proxy interface.</returns>
    public virtual T AddProxyInterface<T>(T target) where T : class
    {
        Thread.Yield();

        if (!typeof(T).IsInterface) throw new Exception("T must be interface!");

        var i = _generator.CreateInterfaceProxyWithTarget(target, _interceptor);

        _interceptor.SavedProxies.TryAdd(i.GetType().Name, n =>
        {
            var met = i.GetType().GetMethod(n.Item1);
            if (met == null) return (false, [], null!);

            var rt = met.Invoke(i, n.Item2);
            return (true, n.Item2, rt)!;
        });

        _savedProxies.TryAdd(typeof(T).Name, i);
        return i;
    }
    
    /// <summary>
    /// Gets the proxy from local.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public virtual T? GetProxyInterface<T>() where T : class
    {
        Thread.Yield();

        if (!typeof(T).IsInterface) throw new Exception("T must be interface!");
        return _savedProxies.GetValueOrDefault(typeof(T).Name) as T;
    }
}
