using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using log4net;


namespace ChatServiceHost
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static ILog appLog;
        private static ILog AppLog
        {
            get
            {
                if (appLog == null)
                {
                    appLog = LogManager.GetLogger(new StackFrame().GetMethod().DeclaringType);
                }
                return appLog;
            }
        }

        ServiceHost host;
        const string ServiceHost = "ChatServiceHost";
        const string chatServiceName = "ChatService";
        const string ServiceSectionName = "system.serviceModel/services";
        const int transferFileSize = (int)67108864;         // 64M 
        const int maxActiveConnections = 200;

        // the following timeout variables are specified in term of seconds
        const int sessionTimeOut = 20 * 60 * 60;            // 20 hours
        const int OpenTimeout = 20;                         // 20 seconds
        const int CloseTimeout = 20;                        // 20 seconds
        const int SendTimeout = 2 * 60;                     // 2 minutes
        const int ReceiveTimeout = sessionTimeOut + 5;      // same as the sessionTimeout

        public MainWindow()
        {
            InitializeComponent();

            log4net.Config.XmlConfigurator.Configure();
            InitScreen();
        }

        private void InitScreen()
        {
            IList<NetworkAddress> networkAddresses = GetBaseAddressFromConfigFile();
            if (networkAddresses == null)
            {
                AppLog.ErrorFormat("{0}(): No network address defined in app.config", new StackFrame().GetMethod().Name);
            }

            NetworkAddress defaultNetworkAddress = networkAddresses.First();
            textBoxIP.Text = defaultNetworkAddress.Server;
            textBoxPort.Text = defaultNetworkAddress.Port;

            StartChatService();
        }

        private void buttonStart_Click(object sender, RoutedEventArgs e)
        {
            StartChatService();
        }

        private void StartChatService()
        {
            string methodName = new StackFrame().GetMethod().Name;
            AppLog.InfoFormat("{0}(): Starting WCF service", methodName);

            buttonStart.IsEnabled = false;

            IList<NetworkAddress> networkAddresses = GetBaseAddressFromConfigFile();
            //Define base addresses so all endPoints can go under it
            Uri[] baseAddresses = new Uri[networkAddresses.Count];
            for (int i = 0; i < networkAddresses.Count; i++)
            {
                baseAddresses[i] = new Uri(networkAddresses[i].BaseAddress);
            }
            foreach (Uri uri in baseAddresses)
            {
                AppLog.InfoFormat("{0}(): ChatServiceHost WCF binding address: {1}", methodName, uri.ToString());
            }

            host = new ServiceHost(typeof(ServiceAssembly.ChatService), baseAddresses);
            InitTcpBinding(host);
            InitMetaEndpoint(host, networkAddresses);

            try
            {
                host.Open();
            }
            catch (Exception ex)
            {
                labelStatus.Content = ex.Message;
                AppLog.ErrorFormat("{0}(): Failed to open WCF host. Exception: {1}", methodName, ex);
            }
            finally
            {
                if (host.State == CommunicationState.Opened)
                {
                    string msg = "Server is ready to accept connections";
                    labelStatus.Content = msg;
                    AppLog.InfoFormat("{0}(): {1}", methodName, msg);

                    buttonStop.IsEnabled = true;
                }
            }
        }

        private void InitTcpBinding(ServiceHost serviceHost)
        {
            NetTcpBinding tcpBinding = new NetTcpBinding(SecurityMode.None, true);
            
            //Updated: to enable file transefer of 64 MB
            tcpBinding.MaxBufferPoolSize = transferFileSize;
            tcpBinding.MaxBufferSize = transferFileSize;
            tcpBinding.MaxReceivedMessageSize = transferFileSize;
            tcpBinding.TransferMode = TransferMode.Buffered;
            tcpBinding.ReaderQuotas.MaxArrayLength = transferFileSize;
            tcpBinding.ReaderQuotas.MaxBytesPerRead = transferFileSize;
            tcpBinding.ReaderQuotas.MaxStringContentLength = transferFileSize;

            tcpBinding.CloseTimeout = TimeSpan.FromSeconds(OpenTimeout);
            tcpBinding.OpenTimeout = TimeSpan.FromSeconds(CloseTimeout);
            tcpBinding.SendTimeout = TimeSpan.FromSeconds(SendTimeout);
            tcpBinding.ReceiveTimeout = TimeSpan.FromSeconds(ReceiveTimeout);

            tcpBinding.MaxConnections = maxActiveConnections;
            //To maxmize MaxConnections you have to assign another port for mex endpoint and configure ServiceThrottling as well
            ServiceThrottlingBehavior throttle;
            throttle = serviceHost.Description.Behaviors.Find<ServiceThrottlingBehavior>();
            if (throttle == null)
            {
                throttle = new ServiceThrottlingBehavior();
                throttle.MaxConcurrentCalls = maxActiveConnections;
                throttle.MaxConcurrentSessions = maxActiveConnections;
                serviceHost.Description.Behaviors.Add(throttle);
            }
            
            //Enable reliable session and keep the connection alive for 20 hours.
            tcpBinding.ReliableSession.Enabled = true;
            tcpBinding.ReliableSession.InactivityTimeout = TimeSpan.FromSeconds(sessionTimeOut);
            
            serviceHost.AddServiceEndpoint(typeof(ServiceAssembly.IChat), tcpBinding, "tcp");
            ReportTcpInfoToAppLog(tcpBinding);
        }

        private void InitMetaEndpoint(ServiceHost serviceHost, IList<NetworkAddress> networkAddresses)
        {
            int medaDataPortDifference = -1;

            //Define Metadata endPoint, So we can publish information about the service
            ServiceMetadataBehavior mBehave = new ServiceMetadataBehavior();
            serviceHost.Description.Behaviors.Add(mBehave);

            NetworkAddress mexAddress = (from n in networkAddresses
                                         where n.Transport == "net.tcp"
                                         select n).FirstOrDefault();
            if (mexAddress == null)
            {
                AppLog.ErrorFormat("{0}(): tcp address not found", new StackFrame().GetMethod().Name);
                return;
            }

            string metaDataAddress = string.Format("{0}:{1}:{2}{3}{4}", mexAddress.Transport, mexAddress.Server,
                                                    (int.Parse(mexAddress.Port) + medaDataPortDifference).ToString(),
                                                    mexAddress.Subfolder, "mex");
            serviceHost.AddServiceEndpoint(typeof(IMetadataExchange),
                                    MetadataExchangeBindings.CreateMexTcpBinding(),
                                    metaDataAddress);
            AppLog.InfoFormat("{0}(): metaData Endpoint added. {1}", new StackFrame().GetMethod().Name, metaDataAddress);
        }

        private void ReportTcpInfoToAppLog(NetTcpBinding tcpBinding)
        {
            string methodName = new StackFrame().GetMethod().Name;

            try
            {
                string tcpAddressInfo1 = string.Format("{0}(): tcp binding info. MaxBufferPoolSize: {1}, MaxBufferSize: {2}, MaxConnections: {3}, " +
                                                        "MaxReceivedMessageSize: {4}, CloseTimeout: {5}, OpenTimeout: {6}, ReceiveTimeout: {7}, " +
                                                        "SendTimeout: {8}",
                                                        new StackFrame().GetMethod().Name, tcpBinding.MaxBufferPoolSize, tcpBinding.MaxBufferSize,
                                                        tcpBinding.MaxConnections, tcpBinding.MaxReceivedMessageSize, tcpBinding.CloseTimeout,
                                                        tcpBinding.OpenTimeout, tcpBinding.ReceiveTimeout, tcpBinding.SendTimeout);
                AppLog.Info(tcpAddressInfo1);
            }
            catch (Exception ex)
            {
                AppLog.ErrorFormat("{0}(): Cannot report tcp info line 1. Exception {1}", methodName, ex);
            }

            try
            {
                string tcpAddressInfo2 = string.Format("{0}(): tcp binding info. ReliableSession.Enable: {1}, ReliableSession.InactivityTimeout: {2}, " +
                                                        "Scheme: {3}, Security.Message: {4}, transferMode: {5}",
                                                        new StackFrame().GetMethod().Name, tcpBinding.ReliableSession.Enabled, 
                                                        tcpBinding.ReliableSession.InactivityTimeout, tcpBinding.Scheme, 
                                                        tcpBinding.Security.Message.ToString(), tcpBinding.TransferMode);
                AppLog.Info(tcpAddressInfo2);
            }
            catch (Exception ex)
            {
                AppLog.ErrorFormat("{0}(): Cannot report tcp info line 2. Exception {1}", methodName, ex);
            }
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            string msg = string.Empty;
            string methodName = new StackFrame().GetMethod().Name;

            if (host != null)
            {
                try
                {
                    host.Close();
                }
                catch (Exception ex)
                {
                    labelStatus.Content = ex.Message;
                    AppLog.ErrorFormat("{0}(): Failed to close host server. Exception: {1}", methodName, ex);
                }
                finally
                {
                    if (host.State == CommunicationState.Closed)
                    {
                        msg = "Server connection closed";
                        labelStatus.Content = msg;
                        AppLog.InfoFormat("{0}(): {1}", methodName, msg);

                        buttonStart.IsEnabled = true;
                        buttonStop.IsEnabled = false;
                    }
                }
            }
        }

        private IList<NetworkAddress> GetBaseAddressFromConfigFile()
        {
            string folderSeperator = @"/";
            ServicesSection servicesSection = null;
            ServiceModelConfigurationElementCollection<ServiceElement> serviceElements = null;
            ServiceElement chatService = null;
            IList<NetworkAddress> networkAddresses = new List<NetworkAddress>();
            string methodName = new StackFrame().GetMethod().Name;

            // Automagically find all endpoints defined in app.config
            try
            {
                servicesSection = ConfigurationManager.GetSection(ServiceSectionName) as ServicesSection;
                if (servicesSection == null)
                {
                    AppLog.ErrorFormat("{0}(): Section [{1}] not found", methodName, ServiceSectionName);
                    return null;
                }

                serviceElements = servicesSection.ElementInformation.Properties[string.Empty].Value as ServiceModelConfigurationElementCollection<ServiceElement>;
                if (serviceElements == null)
                {
                    AppLog.ErrorFormat("{0}(): Cannot retrieve serviceElements collection", methodName);
                    return null;
                }

                chatService = (from ServiceElement s in serviceElements
                               where String.Compare(s.Name, chatServiceName, true) == 0
                               select s).FirstOrDefault();
                if (chatService == null)
                {
                    AppLog.ErrorFormat("{0}(): Service [{1}] not found", methodName, chatServiceName);
                    return null;
                }

                foreach (BaseAddressElement baseAddressElement in chatService.Host.BaseAddresses)
                {
                    string matchPattern = @"(?<transport>.*?):(?<server>//.*?):(?<port>.*?)(?<subfolder>/.*)";
                    Regex regex = new Regex(matchPattern, RegexOptions.Compiled);
                    MatchCollection matchCollection = regex.Matches(baseAddressElement.BaseAddress);
                    if (matchCollection.Count == 0)
                    {
                        AppLog.ErrorFormat("{0}(): Invalid format in wcf base address. {1}", methodName, baseAddressElement.BaseAddress);
                        return null;
                    }

                    foreach (Match match in matchCollection)
                    {
                        string transport = match.Groups["transport"].Value;         // net.tcp
                        string server = match.Groups["server"].Value;               // "//localhost"
                        string port = match.Groups["port"].Value;                   // port
                        string subfolder = match.Groups["subfolder"].Value;         // subfolder

                        if (!subfolder.EndsWith(folderSeperator))
                        {
                            subfolder += folderSeperator;
                        }

                        NetworkAddress networkAddress = new NetworkAddress(baseAddressElement.BaseAddress, transport, server, port, subfolder);
                        networkAddresses.Add(networkAddress);
                    }
                }

                /*
                string defaultProtocol = "net.tcp";
                BaseAddressElement tcpAddress = (from BaseAddressElement b in chatService.Host.BaseAddresses
                                                 where b.BaseAddress.StartsWith(defaultProtocol, StringComparison.OrdinalIgnoreCase)
                                                 select b).FirstOrDefault();
                */
            }
            catch (Exception ex)
            {
                StringBuilder variables = new StringBuilder();
                if (servicesSection == null)
                {
                    variables.Append("ServicesSection is null. ");
                }
                if (serviceElements == null)
                {
                    variables.Append("servicesElements Collection is null. ");
                }
                if (chatService == null)
                {
                    variables.Append("ChatService is null. ");
                }

                AppLog.ErrorFormat("{0}(): Cannot retrieve endpoint from config file. {1}. Exception {2}", methodName, variables.ToString(), ex);
                return null;
            }

            return networkAddresses;
        }

    }
}

