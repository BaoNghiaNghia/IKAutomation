using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Notifications
{
    public interface IAutomationFailureNotifier
    {
        bool IsConfigured { get; }
        Task<AutomationNotificationDeliveryResult> NotifyAsync(
            AutomationFailureNotification notification,
            CancellationToken cancellationToken);
    }
}
