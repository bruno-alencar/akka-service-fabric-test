using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Akka.Cluster;
using Microsoft.ServiceFabric.Services.Client;
using Newtonsoft.Json.Linq;

namespace Lighthouse
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class Lighthouse : StatelessService
    {
        public static StatelessServiceContext LastContext;

        public Lighthouse(StatelessServiceContext context)
            : base(context)
        {
            LastContext = context;
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[] { new ServiceInstanceListener(ctx => new AkkaClusterCommunicationListener(ctx)) };
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            var resolver = ServicePartitionResolver.GetDefault();

            //You cannot try to resolve a partition in ServiceInstanceListener, as it's trying to retrieve services that aren't yet initialized, and creates a deadlock.
            var partition =
                await
                    resolver.ResolveAsync(Context.ServiceName, ServicePartitionKey.Singleton, cancellationToken);

            while (partition.Endpoints.Count < 2)
            {
                ServiceEventSource.Current.ServiceMessage(Context, "[Lighthouse] Waiting for more cluster nodes to start up.");
                await Task.Delay(5000, cancellationToken);

                partition = await
                    resolver.ResolveAsync(partition, cancellationToken);
            }


            var endpoints = partition.Endpoints.Select(c => new Uri(JObject.Parse(c.Address).Value<JToken>("Endpoints").Value<string>("")));
            var myEndpoint = Context.CodePackageActivationContext.GetEndpoint("Akka.Cluster.Endpoint");
            var cluster = Cluster.Get(AkkaClusterCommunicationListener.System);

            //Other endpoints I need to connect to
            var connectEndpoints =
                endpoints.Where(e => e.Port != myEndpoint.Port)
                    .Select(
                        c =>
                            new Address("akka.tcp", AkkaClusterCommunicationListener.System.Name,
                                c.Host,
                                c.Port))
                                .ToList();

            //Join locally
            connectEndpoints.Add(new Address("akka.tcp", AkkaClusterCommunicationListener.System.Name,
                Context.NodeContext.IPAddressOrFQDN,
                myEndpoint.Port));


            ServiceEventSource.Current.ServiceMessage(Context, "[Lighthouse] Joining cluster: {0}", string.Join(", ", connectEndpoints));
            
            cluster.JoinSeedNodes(connectEndpoints);
        }
    }
}
