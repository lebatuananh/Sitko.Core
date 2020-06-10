using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sitko.Core.Repository.EntityFrameworkCore
{
    public class EFRepositoryLock
    {
        private readonly EFRepositoryLockOptions _options;

        public EFRepositoryLock(EFRepositoryLockOptions options)
        {
            _options = options;
        }

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public async Task WaitAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            await _lock.WaitAsync(timeout ?? _options.Timeout, cancellationToken);
        }

        public void Wait(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            _lock.Wait(timeout ?? _options.Timeout, cancellationToken);
        }

        public void Release()
        {
            _lock.Release();
        }
    }

    public class EFRepositoryLockOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);
    }
}
