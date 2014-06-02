using System;
using System.ServiceModel;
using System.ServiceModel.Description;

namespace DSRouterServiceConsoleHost
{
    class Program
    {
        static void Main(string[] args)
        {
            string urlService = "net.tcp://localhost:3332/DSRouter.DSRouterService/";

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
            host.AddServiceEndpoint(typeof(DSRouterService.IDSRouter/*.DSRouterService*/), tcpBinding, urlService);

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
            }

            host.Open();

            Console.WriteLine("Для закрытия нажмите клавишу");
            Console.ReadKey();
        }
    }
}
