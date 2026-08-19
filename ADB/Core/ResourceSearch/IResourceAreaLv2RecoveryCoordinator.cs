using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceAreaLv2RecoveryCoordinator
    {
        void Clear(ResourceAreaLv2RecoveryRequest request);
        Task<ResourceAreaLv2RecoveryResult> RecoverAsync(
            ResourceAreaLv2RecoveryRequest request,
            CancellationToken cancellationToken);
    }
}
