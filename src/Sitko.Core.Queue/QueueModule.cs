using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sitko.Core.App;
using Sitko.Core.MediatR;
using Sitko.Core.Queue.Internal;

namespace Sitko.Core.Queue
{
    public interface IQueueModule : IApplicationModule
    {
    }

    public abstract class QueueModule<TQueue, TConfig> : BaseApplicationModule<TConfig>, IQueueModule
        where TQueue : class, IQueue
        where TConfig : QueueModuleConfig, new()
    {
        public override void ConfigureServices(ApplicationContext context, IServiceCollection services,
            TConfig startupConfig)
        {
            base.ConfigureServices(context, services, startupConfig);
            services.AddSingleton<IQueue, TQueue>();
            services.AddSingleton<QueueContext>();

            if (startupConfig.HealthChecksEnabled)
            {
                services.AddHealthChecks().AddCheck<QueueHealthCheck>("Queue health check");
            }

            if (startupConfig.Middlewares.Any())
            {
                services.Scan(selector =>
                    selector.AddTypes(startupConfig.Middlewares).AsSelfWithInterfaces().WithSingletonLifetime());
            }

            foreach (var options in startupConfig.Options)
            {
                services.AddSingleton(typeof(IQueueMessageOptions), options.Value);
            }

            if (startupConfig.ProcessorEntries.Any())
            {
                var types = startupConfig.ProcessorEntries.Select(e => e.Type).Distinct().ToArray();
                services.Scan(selector => selector.AddTypes(types).AsSelfWithInterfaces().WithScopedLifetime());
                var messageTypes = startupConfig.ProcessorEntries.SelectMany(e => e.MessageTypes).Distinct().ToArray();
                foreach (var messageType in messageTypes)
                {
                    var host = typeof(QueueProcessorHost<>).MakeGenericType(messageType);
                    services.AddSingleton(typeof(IHostedService), host);
                }
            }

            foreach ((Type serviceType, Type implementationType) in startupConfig.TranslateMediatRTypes)
            {
                services.AddTransient(serviceType, implementationType);
            }
        }

        public override IEnumerable<Type> GetRequiredModules(ApplicationContext context, TConfig config)
        {
            var modules = new List<Type>(base.GetRequiredModules(context, config));

            if (config.TranslateMediatRTypes.Any())
            {
                modules.Add(typeof(IMediatRModule));
            }

            return modules;
        }
    }
}
