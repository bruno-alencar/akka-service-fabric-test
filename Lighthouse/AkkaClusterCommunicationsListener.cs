using System;
using System.Configuration;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using ConfigurationException = Akka.Configuration.ConfigurationException;

namespace Lighthouse
{
    internal class AkkaClusterCommunicationListener : ICommunicationListener
    {
        private const string EndpointName = "Akka.Cluster.Endpoint";

        private readonly StatelessServiceContext _context;

        public static ActorSystem System;

        public AkkaClusterCommunicationListener(StatelessServiceContext context)
        {
            _context = context;
        }

        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var systemName = "lighthouse";
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            var clusterConfig = section.AkkaConfig;

            var lighthouseConfig = clusterConfig.GetConfig("lighthouse");
            if (lighthouseConfig != null)
            {
                systemName = lighthouseConfig.GetString("actorsystem", systemName);
            }

            var endpoint = _context.CodePackageActivationContext.GetEndpoint(EndpointName);



            var remoteConfig = clusterConfig.GetConfig("akka.remote");
            var ipAddress = remoteConfig.GetString("helios.tcp.public-hostname") ??
                             _context.NodeContext.IPAddressOrFQDN ??
                            "127.0.0.1"; //localhost as a final default
            var port = remoteConfig.GetInt("helios.tcp.port");

            if (port == 0)
            {
                port = _context.CodePackageActivationContext.GetEndpoint("Akka.Cluster.Endpoint").Port;
            }


            //var injectedClusterConfigString = $@"akka.cluster.seed-nodes = [""akka.tcp://{systemName}@{ipAddress}:{port}""]";


            if (port == 0)
                throw new ConfigurationException(
                    "Need to specify an explicit port for Lighthouse. Found an undefined port or a port value of 0 in App.config.");

            var finalConfig = ConfigurationFactory.ParseString(
                    $@"akka.remote.helios.tcp.public-hostname = {ipAddress} 
akka.remote.helios.tcp.port = {port}")
                //.WithFallback(ConfigurationFactory.ParseString(injectedClusterConfigString))
                .WithFallback(clusterConfig);

            try
            {
                System = ActorSystem.Create(systemName, finalConfig);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(this._context, ex.Message);
                throw;
            }

            return Task.FromResult($"tcp://{ipAddress}:{port}");
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            System?.Terminate().Wait(cancellationToken);
            System?.Dispose();

            return Task.FromResult(0);
        }

        public void Abort()
        {
            System?.Dispose();
        }
    }
}
