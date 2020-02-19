using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sitko.Core.Storage.Proxy.StaticFiles
{
    public class StorageMiddleware<TStorageOptions> where TStorageOptions : StorageOptions
    {
        private readonly RequestDelegate _next;
        private readonly IStorage<TStorageOptions> _storage;
        private readonly ILogger _logger;
        private readonly IContentTypeProvider _contentTypeProvider;
        private readonly StorageFileOptions _options;

        public StorageMiddleware(RequestDelegate next, IOptions<StorageFileOptions> options,
            IStorage<TStorageOptions> storage,
            ILogger<StorageMiddleware<TStorageOptions>> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _options = options.Value;
            _storage = storage;
            _contentTypeProvider = _options.ContentTypeProvider ?? new FileExtensionContentTypeProvider();
            _logger = logger;
        }

        public Task Invoke(HttpContext context)
        {
            if (!ValidateNoEndpoint(context))
            {
                _logger.LogError("No endpoint");
            }
            else if (!ValidateMethod(context))
            {
                _logger.LogError("Method {Method} is not supported", context.Request.Method);
            }
            else if (!ValidatePath(context, out var subPath))
            {
                _logger.LogError("Path mismatch");
            }
            else if (!LookupContentType(_contentTypeProvider, subPath, out var contentType))
            {
                _logger.LogError("File type is not supported");
            }
            else
            {
                // If we get here, we can try to serve the file
                return TryServeStaticFile(context, contentType, subPath);
            }

            return _next(context);
        }

        private bool ValidatePath(HttpContext context, out string path)
        {
            path = context.Request.Path;

            if (path.StartsWith("/"))
            {
                return true;
            }

            return false;
        }

        // Return true because we only want to run if there is no endpoint.
        private static bool ValidateNoEndpoint(HttpContext context) => context.GetEndpoint() == null;

        private static bool ValidateMethod(HttpContext context)
        {
            return context.Request.Method == "GET" || context.Request.Method == "HEAD";
        }


        private static bool LookupContentType(IContentTypeProvider contentTypeProvider,
            PathString subPath, out string contentType)
        {
            if (contentTypeProvider.TryGetContentType(subPath.Value, out contentType))
            {
                return true;
            }

            return false;
        }

        private async Task TryServeStaticFile(HttpContext context, string contentType, PathString subPath)
        {
            var fileContext =
                new StorageFileContext<TStorageOptions>(context, _options, _logger, _storage, contentType, subPath);
            var fileExists = await fileContext.LookupFileInfo();
            if (!fileExists)
            {
                _logger.LogError("File {File} not found", fileContext.SubPath);
            }
            else
            {
                // If we get here, we can try to serve the file
                await fileContext.ServeStaticFile(context, _next);
                return;
            }

            await _next(context);
        }
    }
}