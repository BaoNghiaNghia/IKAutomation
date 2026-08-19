using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Abstractions
{
    public interface IFocusedInputValueReader
    {
        Task<int> ReadFocusedIntegerAsync(
            string deviceName,
            CancellationToken cancellationToken);
    }
}
