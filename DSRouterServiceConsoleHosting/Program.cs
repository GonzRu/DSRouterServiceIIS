using System;
using System.ServiceModel;
using System.ServiceModel.Description;

namespace DSRouterServiceConsoleHost
{
    class Program
    {
        private static string ServiceUrl;
        private static string MetaUrl;

        static void Main(string[] args)
        {
            ServiceUrl = "net.tcp://localhost:3332/DSRouter.DSRouterService";

            var host = new ServiceHost(typeof(DSRouterService.DSRouterService));

            NetTcpBinding tcpBinding = new NetTcpBinding();
            tcpBinding.TransactionFlow = false;
            tcpBinding.Security.Transport.ProtectionLevel = System.Net.Security.ProtectionLevel.EncryptAndSign;
            tcpBinding.Security.Transport.ClientCredentialType = TcpClientCredentialType.Windows;
            tcpBinding.Security.Mode = SecurityMode.None;

            tcpBinding.MaxReceivedMessageSize = int.MaxValue;// 150000000;
            tcpBinding.MaxBufferSize = int.MaxValue;//150000000;
            tcpBinding.ReaderQuotas.MaxArrayLength = int.MaxValue;// 150000000;

            // Add a endpoint
            host.AddServiceEndpoint(typeof(DSRouterService.IDSRouter/*.DSRouterService*/), tcpBinding, ServiceUrl);
            host.Opening += new EventHandler(host_Opening);
            host.Opened += new EventHandler(host_Opened);
            host.Closing += new EventHandler(host_Closing);
            host.Closed += new EventHandler(host_Closed);

            // A channel to describe the service. Used with the proxy scvutil.exe tool
            ServiceMetadataBehavior metadataBehavior;
            metadataBehavior = host.Description.Behaviors.Find<ServiceMetadataBehavior>();
            if (metadataBehavior == null)
            {
                metadataBehavior = new ServiceMetadataBehavior();
                metadataBehavior.HttpGetUrl = new Uri("http://localhost:3333/DSRouter.DSRouterService/mex");

                metadataBehavior.HttpGetEnabled = true;
                metadataBehavior.ToString();
                host.Description.Behaviors.Add(metadataBehavior);
                MetaUrl = metadataBehavior.HttpGetUrl.ToString();
            }

            host.Open();

            Console.WriteLine("Для завершения работы нажмите ВВОД");
            Console.ReadLine();

            if (host != null)
                host.Close();
        }

        static void host_Opening(object sender, EventArgs e)
        {
            Console.WriteLine("Service opening ... Stand by");
        }

        static void host_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("Service closed");
        }

        static void host_Closing(object sender, EventArgs e)
        {
            Console.WriteLine("Service closing ... stand by");
        }

        static void host_Opened(object sender, EventArgs e)
        {
            Console.WriteLine("Service opened.");
            Console.WriteLine("Service URL:\t" + ServiceUrl);
            Console.WriteLine("Meta URL:\t" + MetaUrl + " (Not that relevant)");
            Console.WriteLine("Waiting for clients...");
        }
    }
}
