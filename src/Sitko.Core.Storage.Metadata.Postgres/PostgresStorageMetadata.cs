﻿using Sitko.Core.App;

namespace Sitko.Core.Storage.Metadata.Postgres
{
    public class
        PostgresStorageMetadataModule<TStorageOptions> : BaseStorageMetadataModule<TStorageOptions,
            PostgresStorageMetadataProvider<TStorageOptions>, PostgresStorageMetadataProviderOptions>
        where TStorageOptions : StorageOptions
    {
        public PostgresStorageMetadataModule(Application application) : base(application)
        {
        }
    }
}
