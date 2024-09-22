# Node Remote Access

NRA Project 2024 (v1.0.1) - Secure Remote Method Invocation for .NET
This project provides a secure and efficient mechanism for remote method invocation in .NET applications, similar to Java's RMI. It leverages TCP communication with custom encryption for secure data transfer. 

Key Features:

Secure Communication: Employs a custom cipher for symmetric encryption using a 32-byte key and an 8-byte nonce, ensuring secure data transmission over TCP. 
Flexible Transport Options: Supports two TCP implementations:
WatsonTcp: Provides maximum security but prioritizes secure packet delivery over speed.
AbsTcp: Offers a balance between security and speed, delivering packets at roughly twice the speed of WatsonTcp while consuming slightly more CPU resources on the client side. WatsonTcp consumes more RAM on the server side, while AbsTcp consumes more RAM on the client side.
Easy Integration: Provides a straightforward API for creating and using proxy objects for remote method calls.
Comprehensive Documentation: Includes clear and detailed documentation with examples to guide developers.

Example Usage:

```C#
// Example client code
class Program
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
```
```C#
// Example interface
interface IInterface 
{
    string SayHello(string name, out string goodbye);
}

// Example implementation
class MyClass : IInterface 
{
    public string SayHello(string name, out string goodbye) 
    {
        goodbye = "Hello";
        return "Hello " + name;
    }
}
```
Analogous Java Example using Java RMI:
```Java
// Example server code
public class Server {
    public static void main(String[] args) {
        try {
            // Create and bind a remote object
            MyRemoteImpl myImpl = new MyRemoteImpl();
            Naming.rebind("rmi://localhost/MyRemote", myImpl);
            System.out.println("Server started.");
        } catch (Exception e) {
            System.out.println("Server exception: " + e.getMessage());
        }
    }
}

// Example client code
public class Client {
    public static void main(String[] args) {
        try {
            // Look up the remote object
            MyRemote myRemote = (MyRemote) Naming.lookup("rmi://localhost/MyRemote");
            // Call a remote method
            String response = myRemote.sayHello("Anatoly");
            System.out.println("Response: " + response);
        } catch (Exception e) {
            System.out.println("Client exception: " + e.getMessage());
        }
    }
}

```
```Java
// Example interface
interface MyRemote extends Remote {
    String sayHello(String name) throws RemoteException;
}

// Example implementation
class MyRemoteImpl implements MyRemote {
    public String sayHello(String name) {
        return "Hello " + name;
    }
}
```
NRA Project 2024 provides a robust and secure solution for remote method invocation in .NET, offering developers a powerful alternative to traditional RPC mechanisms.

This project is under active development, and contributions are welcome. Feel free to fork the repository, contribute to the code, and provide feedback.

Contact me: nazardev@duck.com
