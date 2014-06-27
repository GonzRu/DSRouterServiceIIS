using System;
using System.ServiceModel;
using System.ServiceModel.Description;

namespace DSRouterServiceConsoleHost
{
    class Program
    {
        private static string ServiceUrl;
        private static string MetaUrl;
        private static ServiceHost host;

        static void Main(string[] args)
        {
            CreateHost();

            OpenHost();

            Console.WriteLine("Для завершения работы нажмите ВВОД");
            Console.ReadLine();

            CloseHost();
        }

        #region Private-metods

        private static void CreateHost()
        {
            ServiceUrl = "net.tcp://localhost:3332/DSRouter.DSRouterService/DSRouterService.svc";

            host = new ServiceHost(typeof(DSRouterServiceIIS.DSRouterService));

            NetTcpBinding tcpBinding = new NetTcpBinding();
            tcpBinding.Security.Mode = SecurityMode.None;

            tcpBinding.MaxReceivedMessageSize = int.MaxValue;
            tcpBinding.MaxBufferSize = int.MaxValue;
            tcpBinding.ReaderQuotas.MaxArrayLength = int.MaxValue;

            // Add a endpoint
            host.AddServiceEndpoint(typeof(DSRouterServiceIIS.IDSRouter), tcpBinding, ServiceUrl);
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
                host.Description.Behaviors.Add(metadataBehavior);
                MetaUrl = metadataBehavior.HttpGetUrl.ToString();
            }
        }

        private static void OpenHost()
        {
            try
            {
                host.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void CloseHost()
        {
            if (host != null)
                host.Close();
        }

        #endregion

        #region Handlers

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

        #endregion

    }
}
