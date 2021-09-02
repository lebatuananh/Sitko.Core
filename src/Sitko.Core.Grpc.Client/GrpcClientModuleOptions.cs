using Grpc.Core;

namespace Sitko.Core.Grpc.Client
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using App;
    using global::Grpc.Core.Interceptors;
    using global::Grpc.Net.Client;
    using JetBrains.Annotations;

    [PublicAPI]
    public class GrpcClientModuleOptions<TClient> : BaseModuleOptions where TClient : ClientBase<TClient>
    {
        public bool EnableHttp2UnencryptedSupport { get; set; }
        public bool DisableCertificatesValidation { get; set; }
        [JsonIgnore] public Action<GrpcChannelOptions>? ConfigureChannelOptions { get; set; }

        internal HashSet<Type> Interceptors { get; } = new();

        public GrpcClientModuleOptions<TClient> AddInterceptor<TInterceptor>() where TInterceptor : Interceptor
        {
            Interceptors.Add(typeof(TInterceptor));
            return this;
        }
    }
}
