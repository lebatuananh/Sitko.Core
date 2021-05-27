using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Sinks.Elasticsearch;
using Sitko.Core.App;

namespace Sitko.Core.ElasticStack
{
    public class ElasticStackModuleConfig : BaseModuleConfig
    {
        public bool LoggingEnabled { get; private set; }
        public bool ApmEnabled { get; private set; }
        public List<Uri>? ElasticSearchUrls { get; protected set; }
        public double ApmTransactionSampleRate { get; set; } = 1.0;
        public int ApmTransactionMaxSpans { get; set; } = 500;
        public bool ApmCentralConfig { get; set; } = true;
        public List<string>? ApmSanitizeFieldNames { get; set; }
        public readonly Dictionary<string, string> ApmGlobalLabels = new();
        public List<Uri>? ApmServerUrls { get; private set; }
        public string? ApmSecretToken { get; set; }
        public string? ApmApiKey { get; set; }
        public bool ApmVerifyServerCert { get; set; } = true;
        public TimeSpan ApmFlushInterval { get; set; } = TimeSpan.FromSeconds(10);
        public int ApmMaxBatchEventCount { get; set; } = 10;
        public int ApmMaxQueueEventCount { get; set; } = 1000;
        public TimeSpan ApmMetricsInterval { get; set; } = TimeSpan.FromSeconds(30);
        public List<string>? ApmDisableMetrics { get; set; }
        public string ApmCaptureBody { get; set; } = "off";
        public List<string>? ApmCaptureBodyContentTypes { get; set; }
        public bool ApmCaptureHeaders { get; set; } = true;
        public bool ApmUseElasticTraceparentHeader { get; set; } = true;
        public int ApmStackTraceLimit { get; set; } = 50;
        public TimeSpan ApmSpanFramesMinDuration { get; set; } = TimeSpan.FromSeconds(0.5);
        public string ApmLogLevel { get; set; } = "Error";
        public string? LoggingIndexFormat { get; set; }
        public AutoRegisterTemplateVersion? LoggingTemplateVersion { get; set; }
        public int? LoggingNumberOfReplicas { get; set; }
        public string? LoggingLifeCycleName { get; set; }
        public string? LoggingLiferRolloverAlias { get; set; }


        public ElasticStackModuleConfig EnableLogging(Uri elasticSearchUri)
        {
            return EnableLogging(new[] {elasticSearchUri});
        }

        public ElasticStackModuleConfig EnableLogging(IEnumerable<Uri> elasticSearchUrls)
        {
            LoggingEnabled = true;
            ElasticSearchUrls = elasticSearchUrls.ToList();
            return this;
        }

        public ElasticStackModuleConfig EnableApm(Uri apmUri)
        {
            return EnableApm(new[] {apmUri});
        }

        public ElasticStackModuleConfig EnableApm(IEnumerable<Uri> apmUrls)
        {
            ApmEnabled = true;
            ApmServerUrls = apmUrls.ToList();
            return this;
        }

        public override (bool isSuccess, IEnumerable<string> errors) CheckConfig()
        {
            var result = base.CheckConfig();
            if (result.isSuccess)
            {
                if (ApmEnabled)
                {
                    if (ApmServerUrls == null || !ApmServerUrls.Any())
                    {
                        return (false, new[] {"ApmServerUrls can't be empty"});
                    }
                }

                if (LoggingEnabled)
                {
                    if (ElasticSearchUrls == null || !ElasticSearchUrls.Any())
                    {
                        return (false, new[] {"ElasticSearchUrls can't be empty"});
                    }
                }
            }

            return result;
        }
    }
}
