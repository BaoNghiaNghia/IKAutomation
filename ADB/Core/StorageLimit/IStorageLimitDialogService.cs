using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.StorageLimit
{
    public interface IStorageLimitDialogService
    {
        Task<StorageLimitDialogResult> HandleAsync(string deviceName,
            StorageLimitPolicy policy, CancellationToken cancellationToken);
    }
}
