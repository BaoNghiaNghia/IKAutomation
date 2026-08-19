using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.StorageLimit
{
    public interface IStorageLimitDialogService
    {
        Task<StorageLimitDialogResult> HandleAsync(string deviceName,
            StorageLimitPolicy policy, CancellationToken cancellationToken);
        Task<StorageLimitDialogResult> HandleResourceExpiryAsync(string deviceName,
            CancellationToken cancellationToken);
    }
}
