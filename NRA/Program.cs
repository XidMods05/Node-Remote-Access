using System.Diagnostics;
using NRA.RpcProxy.Client;
using NRA.RpcProxy.Server;

namespace NRA;

public interface IInterface
{
    public void SayHello(string name, out string bye);
}

public class MyClass : IInterface
{
    public void SayHello(string name, out string bye)
    {
        Console.WriteLine("hello " + name);
        bye = "bye " + name;
    }
}

internal class Program
{
    private static void Main(string[] args)
    {
        byte[] key =
            [10, 90, 4, 4, 4, 9, 4, 0, 0, 0, 4, 9, 7, 2, 5, 9, 10, 90, 4, 4, 4, 9, 4, 0, 0, 0, 4, 9, 7, 2, 5, 9];
        byte[] nonce = [10, 90, 4, 4, 4, 9, 4, 5];

        var s = new NraProxyServer(false, "127.0.0.1", 9040);
        s.Init(key, nonce);

        // add proxy (can be added to DI container)
        s.AddProxyInterface<IInterface>(new MyClass());

        var c = new NraProxyClient(false, "127.0.0.1", 9040);
        c.Init(key, nonce);
        
        // get proxy (can be added to DI container)
        var t1 = c.GetProxyInterface<IInterface>();
        
        var st = new Stopwatch();
        
        st.Start();
        {
            for (var i = 0; i < 10_000; i++)
            {
                t1.SayHello("Anatoly " + i, out var bye);
                Console.WriteLine(bye + ". Oh no!");
            }
        }
        st.Stop();

        Console.WriteLine(st.ElapsedMilliseconds);
    }
}