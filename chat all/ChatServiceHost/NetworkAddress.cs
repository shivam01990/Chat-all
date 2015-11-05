
namespace ChatServiceHost
{
    public class NetworkAddress
    {
        public string BaseAddress { get; set; }
        public string Transport { get; set; }
        public string Server { get; set; }
        public string Port { get; set; }
        public string Subfolder { get; set; }

        public NetworkAddress() { }

        public NetworkAddress(string baseAddress, string transport, string server, string port, string subfolder)
        {
            BaseAddress = baseAddress;
            Transport = transport;
            Server = server;
            Port = port;
            Subfolder = subfolder;
        }
    }
}
