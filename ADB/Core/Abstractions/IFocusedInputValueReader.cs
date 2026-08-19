using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Abstractions
{
    public interface IFocusedInputValueReader
    {
        Task<int> ReadFocusedIntegerAsync(
            string deviceName,
            CancellationToken cancellationToken);
    }
}
