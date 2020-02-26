using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sitko.Core.Auth.IdentityServer
{
    public abstract class IdentityServerModule<T> : AuthModule<T> where T : IdentityServerAuthOptions
    {
        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration,
            IHostEnvironment environment)
        {
            base.ConfigureServices(services, configuration, environment);
            services.AddHealthChecks().AddIdentityServer(new Uri(Config.OidcServerUrl));
        }

        protected override void CheckConfig()
        {
            base.CheckConfig();
            if (string.IsNullOrEmpty(Config.OidcServerUrl))
            {
                throw new ArgumentException("Oidc servder url can't be empty");
            }
        }
    }
}