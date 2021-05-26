using System;
using System.Collections.Generic;
using Amazon;
using Amazon.S3;
using HealthChecks.Aws.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Sitko.Core.App;

namespace Sitko.Core.Storage.S3
{
    public class S3StorageModule<TS3StorageOptions> : StorageModule<S3Storage<TS3StorageOptions>, TS3StorageOptions>
        where TS3StorageOptions : S3StorageOptions, new()
    {
        public S3StorageModule(Application application) : base(application)
        {
        }

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration,
            IHostEnvironment environment)
        {
            base.ConfigureServices(services, configuration, environment);
            services.AddSingleton<S3ClientProvider<TS3StorageOptions>>();
            services.AddHealthChecks().Add(new HealthCheckRegistration(GetType().Name,
                (Func<IServiceProvider, IHealthCheck>)(sp =>
                {
                    var options = new S3BucketOptions
                    {
                        AccessKey = GetConfig().AccessKey,
                        BucketName = GetConfig().Bucket,
                        SecretKey = GetConfig().SecretKey,
                        S3Config = new AmazonS3Config
                        {
                            RegionEndpoint = GetConfig().Region,
                            ServiceURL = GetConfig().Server.ToString(),
                            ForcePathStyle = true
                        }
                    };
                    return new S3HealthCheck(options);
                }), null, null, null));
        }
    }

    public class S3StorageOptions : StorageOptions
    {
        public Uri? Server { get; set; }
        public string Bucket { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public RegionEndpoint Region { get; set; } = RegionEndpoint.USEast1;
        public override string Name { get; set; } = string.Empty;

        public override (bool isSuccess, IEnumerable<string> errors) CheckConfig()
        {
            var result = base.CheckConfig();
            if (result.isSuccess)
            {
                if (Server is null)
                {
                    return (false, new[] {"S3 server url is empty"});
                }

                if (string.IsNullOrEmpty(Bucket))
                {
                    return (false, new[] {"S3 bucketName is empty"});
                }

                if (string.IsNullOrEmpty(AccessKey))
                {
                    return (false, new[] {"S3 access key is empty"});
                }

                if (string.IsNullOrEmpty(SecretKey))
                {
                    return (false, new[] {"S3 secret key is empty"});
                }
            }

            return result;
        }
    }
}
