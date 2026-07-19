using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Notifications
{
    public interface IAutomationFailureNotifier
    {
        bool IsConfigured { get; }
        Task NotifyAsync(AutomationFailureNotification notification,
            CancellationToken cancellationToken);
    }
}
