using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Sitko.Core.App.Logging;

namespace Sitko.Core.App
{
    public abstract class Application : IApplication, IAsyncDisposable
    {
        private readonly bool _check;
        private readonly LoggerConfiguration _loggerConfiguration = new LoggerConfiguration();
        private readonly LogLevelSwitcher _logLevelSwitcher = new LogLevelSwitcher();
        private readonly HashSet<Type> _registeredModules = new HashSet<Type>();
        private readonly Dictionary<string, object> _store = new Dictionary<string, object>();
        protected readonly IConfiguration Configuration;
        protected readonly IHostEnvironment Environment;

        protected readonly IHostBuilder HostBuilder;

        protected readonly Dictionary<string, LogEventLevel> LogEventLevels = new Dictionary<string, LogEventLevel>();
        protected readonly ILogger<Application> Logger;
        protected readonly List<IApplicationModule> Modules = new List<IApplicationModule>();

        private IHost? _appHost;

        protected Application(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length > 0 && args[0] == "check")
            {
                _check = true;
            }

            var tmpHost = Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(builder => { builder.AddEnvironmentVariables("ASPNETCORE_"); })
                .ConfigureLogging(builder => { builder.SetMinimumLevel(LogLevel.Information); }).Build();

            Configuration = tmpHost.Services.GetService<IConfiguration>();
            Environment = tmpHost.Services.GetService<IHostEnvironment>();
            Logger = tmpHost.Services.GetService<ILogger<Application>>();
            LoggingEnableConsole = Environment.IsDevelopment();

            Name = Environment.ApplicationName;

            _loggerConfiguration.MinimumLevel.ControlledBy(_logLevelSwitcher.Switch);
            _loggerConfiguration.Enrich.FromLogContext()
                .Enrich.WithProperty("App", Name)
                .Enrich.WithProperty("AppVersion", Version);

            HostBuilder = Host.CreateDefaultBuilder(args)
                .UseDefaultServiceProvider(options =>
                {
                    options.ValidateOnBuild = true;
                    options.ValidateScopes = true;
                });
            HostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton(_logLevelSwitcher);
                services.AddSingleton<ILoggerFactory>(_ => new SerilogLoggerFactory());
            });
        }

        protected LogEventLevel LoggingLevel { get; set; } = LogEventLevel.Information;
        protected bool LoggingEnableConsole { get; set; }
        protected Action<LoggerConfiguration, LogLevelSwitcher>? LoggingConfigure { get; set; }

        public string Name { get; private set; }
        public string Version { get; private set; } = "dev";

        public virtual ValueTask DisposeAsync()
        {
            _appHost?.Dispose();
            return new ValueTask();
        }

        public async Task RunAsync()
        {
            await InitAsync();

            await GetAppHost().RunAsync();
        }

        public async Task StartAsync()
        {
            await InitAsync();

            await GetAppHost().StartAsync();
        }

        public async Task StopAsync()
        {
            await GetAppHost().StopAsync();
        }

        public async Task ExecuteAsync(Func<IServiceProvider, Task> command)
        {
            GetHostBuilder().UseConsoleLifetime();
            var host = GetAppHost();

            await InitAsync();

            var serviceProvider = host.Services;

            try
            {
                using var scope = serviceProvider.CreateScope();
                await command(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetService<ILogger<Application>>();
                logger.LogError(ex, ex.ToString());
            }
        }

        public IServiceProvider GetServices()
        {
            return GetAppHost().Services;
        }

        protected IHost GetAppHost()
        {
            if (_appHost == null)
            {
                try
                {
                    Init();
                    _appHost = HostBuilder.Build();
                    Logger.LogInformation("Check required modules");
                    foreach (var module in Modules)
                    {
                        CheckRequiredModules(module);
                    }

                    if (_check)
                    {
                        Console.WriteLine("Check run is successful");
                        System.Environment.Exit(0);
                    }

                    Log.Logger = _loggerConfiguration.CreateLogger();
                }
                catch (Exception e)
                {
                    Logger.LogError($"Host build error: {e}");
                    System.Environment.Exit(255);
                }
            }

            return _appHost!;
        }

        public IHostBuilder GetHostBuilder()
        {
            return HostBuilder;
        }

        protected virtual void ConfigureLogging()
        {
            _logLevelSwitcher.Switch.MinimumLevel = LoggingLevel;
            _logLevelSwitcher.MsMessagesSwitch.MinimumLevel = LogEventLevel.Warning;
            _loggerConfiguration.MinimumLevel.Override("Microsoft", _logLevelSwitcher.MsMessagesSwitch);

            if (LoggingEnableConsole)
            {
                _loggerConfiguration
                    .WriteTo.Console(
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3} {SourceContext}] {Message:lj}{NewLine}{Exception}",
                        levelSwitch: _logLevelSwitcher.Switch);
            }

            LoggingConfigure?.Invoke(_loggerConfiguration, _logLevelSwitcher);
            foreach (var entry in LogEventLevels)
            {
                _loggerConfiguration.MinimumLevel.Override(entry.Key, entry.Value);
            }
        }

        protected void RegisterModule<TModule, TModuleConfig>(
            Action<IConfiguration, IHostEnvironment, TModuleConfig>? configure = null)
            where TModule : IApplicationModule<TModuleConfig> where TModuleConfig : class, new()
        {
            if (_registeredModules.Contains(typeof(TModule)))
            {
                throw new Exception($"Module {typeof(TModule)} already registered");
            }

            _registeredModules.Add(typeof(TModule));
            HostBuilder.ConfigureServices((context, services) =>
            {
                var config = new TModuleConfig();
                if (!_check)
                {
                    configure?.Invoke(context.Configuration, context.HostingEnvironment, config);
                }

                var module = (TModule)Activator.CreateInstance(typeof(TModule), config, this);
                if (!_check)
                {
                    module.CheckConfig();
                }

                module.ConfigureLogging(_loggerConfiguration, _logLevelSwitcher,
                    context.Configuration, context.HostingEnvironment);
                module.ConfigureServices(services, context.Configuration, context.HostingEnvironment);
                AddModule(module);
            });
        }

        private bool _initComplete;

        private void Init()
        {
            if (!_initComplete)
            {
                var name = GetName();
                if (!string.IsNullOrEmpty(name))
                {
                    Name = name;
                }

                var version = GetVersion();
                if (!string.IsNullOrEmpty(version))
                {
                    Version = version;
                }

                InitApplication();
                ConfigureLogging();
                _initComplete = true;
            }
        }

        protected virtual string? GetName()
        {
            return null;
        }

        protected virtual string? GetVersion()
        {
            return null;
        }

        protected virtual void InitApplication()
        {
        }

        protected virtual void AddModule(IApplicationModule module)
        {
            Modules.Add(module);
        }

        public async Task InitAsync()
        {
            var host = GetAppHost();

            using var scope = host.Services.CreateScope();
            Logger.LogInformation("Init modules");
            foreach (var module in Modules)
            {
                Logger.LogInformation("Init module {Module}", module);
                await module.InitAsync(scope.ServiceProvider,
                    scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                    scope.ServiceProvider.GetRequiredService<IHostEnvironment>());
            }
        }

        private void CheckRequiredModules(IApplicationModule module)
        {
            var requiredModules = module.GetRequiredModules();
            foreach (var requiredModule in requiredModules.Where(requiredModule =>
                Modules.All(m => !requiredModule.IsInstanceOfType(m))))
            {
                throw new Exception($"Module {module} require module {requiredModule} to be included");
            }
        }

        public void OnStarted(IConfiguration configuration, IHostEnvironment environment,
            IServiceProvider serviceProvider)
        {
            foreach (var module in Modules)
            {
                module.ApplicationStarted(configuration, environment, serviceProvider);
            }
        }

        public void OnStopping(IConfiguration configuration, IHostEnvironment environment,
            IServiceProvider serviceProvider)
        {
            foreach (var module in Modules)
            {
                module.ApplicationStopping(configuration, environment, serviceProvider);
            }
        }

        public void OnStopped(IConfiguration configuration, IHostEnvironment environment,
            IServiceProvider serviceProvider)
        {
            foreach (var module in Modules)
            {
                module.ApplicationStopped(configuration, environment, serviceProvider);
            }
        }

        public bool HasModule<TModule>() where TModule : IApplicationModule
        {
            return Modules.OfType<TModule>().Any();
        }


        public void Set(string key, object value)
        {
            _store[key] = value;
        }

        public T Get<T>(string key)
        {
            if (_store.ContainsKey(key))
            {
                return (T)_store[key];
            }

#pragma warning disable 8603
            return default;
#pragma warning restore 8603
        }

        protected void LogModuleRegistrationFailed<T>() where T : IApplicationModule
        {
            Logger.LogError("Can't register module {Module}: empty configuration", typeof(T));
        }
    }

    public class Application<T> : Application where T : Application<T>
    {
        public Application(string[] args) : base(args)
        {
            ConfigureServices((context, services) =>
            {
                services.AddSingleton(typeof(IApplication), this);
                services.AddSingleton(typeof(Application<T>), this);
                services.AddSingleton(typeof(T), this);
                services.AddHostedService<ApplicationLifetimeService<T>>();
            });
        }

        public T ConfigureServices(Action<IServiceCollection> configure)
        {
            HostBuilder.ConfigureServices(configure);
            return (T)this;
        }

        public T ConfigureServices(Action<HostBuilderContext, IServiceCollection> configure)
        {
            HostBuilder.ConfigureServices(configure);
            return (T)this;
        }

        public T ConfigureLogLevel(string source, LogEventLevel level)
        {
            LogEventLevels.Add(source, level);
            return (T)this;
        }

        public T AddModule<TModule, TModuleConfig>(
            Action<IConfiguration, IHostEnvironment, TModuleConfig>? configure = null)
            where TModule : IApplicationModule<TModuleConfig> where TModuleConfig : class, new()
        {
            RegisterModule<TModule, TModuleConfig>(configure);
            return (T)this;
        }

        public T AddModule<TModule>() where TModule : BaseApplicationModule
        {
            AddModule<TModule, BaseApplicationModuleConfig>();
            return (T)this;
        }


        public T ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> action)
        {
            HostBuilder.ConfigureAppConfiguration(action);
            return (T)this;
        }
    }
}
