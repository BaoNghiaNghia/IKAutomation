using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public enum UiMessageKey
    {
        NoDeviceSelected, CheckingPreflight, PreflightFactoryReturnedNull,
        PreflightReturnedNoResult, PreflightEligibleTeams, PreflightNoReadyTeam,
        PreflightCancelled, WaitingForExecutionSlot, OneShotStarted,
        WorkflowFactoryReturnedNull, OneShotReturnedNoResult, OneShotCancelled,
        YieldedUntilScheduledCheck, ReadyTeamWaitTimeout, NoTeamReadyWithinWait,
        CheckingAllowedTeams, ReadyAllowedTeams, ConfirmingNoReadyTeam,
        DispatchedTeamsNoReadyRemaining, WaitingForNextTeamCheck,
        ReadinessWaitCancelled, OperationCancelled, OperationTimedOut,
        DataAccessFailed, AccessDenied, RequiredFileMissing, DeviceUnavailable,
        ScreenshotFailed, TemplateMissing, UnexpectedFailure
    }

    public interface IUserMessageLocalizer
    {
        string Get(UiMessageKey key);
        string Format(UiMessageKey key, params object[] args);
    }

    public sealed class VietnameseUserMessageLocalizer : IUserMessageLocalizer
    {
        private static readonly IReadOnlyDictionary<UiMessageKey, string> Messages =
            new Dictionary<UiMessageKey, string>
            {
                [UiMessageKey.NoDeviceSelected] = "Cần chọn ít nhất một thiết bị LDPlayer.",
                [UiMessageKey.CheckingPreflight] = "Đang kiểm tra Bản đồ Thế giới, ảnh màn hình, danh sách đội và các đội đủ điều kiện.",
                [UiMessageKey.PreflightFactoryReturnedNull] = "Không thể tạo dịch vụ kiểm tra ban đầu.",
                [UiMessageKey.PreflightReturnedNoResult] = "Kiểm tra ban đầu không trả về kết quả.",
                [UiMessageKey.PreflightEligibleTeams] = "Kiểm tra ban đầu hoàn tất; đội sẵn sàng đủ điều kiện: {0}.",
                [UiMessageKey.PreflightNoReadyTeam] = "Chưa có đội sẵn sàng; thiết bị đã nhường lượt.",
                [UiMessageKey.PreflightCancelled] = "Đã hủy kiểm tra ban đầu cho các thiết bị.",
                [UiMessageKey.WaitingForExecutionSlot] = "Đang chờ lượt xử lý.",
                [UiMessageKey.OneShotStarted] = "Đã bắt đầu lượt farm.",
                [UiMessageKey.WorkflowFactoryReturnedNull] = "Không thể tạo luồng farm.",
                [UiMessageKey.OneShotReturnedNoResult] = "Lượt farm không trả về kết quả.",
                [UiMessageKey.OneShotCancelled] = "Đã hủy lượt farm.",
                [UiMessageKey.YieldedUntilScheduledCheck] = "Chưa có đội sẵn sàng; thiết bị đã nhường lượt. Thiết bị sẽ được kiểm tra lại theo lịch.",
                [UiMessageKey.ReadyTeamWaitTimeout] = "Đã hết thời gian chờ đội sẵn sàng.",
                [UiMessageKey.NoTeamReadyWithinWait] = "Không có đội được phép nào sẵn sàng trong thời gian chờ.",
                [UiMessageKey.CheckingAllowedTeams] = "Đang kiểm tra các đội được phép (lần {0}).",
                [UiMessageKey.ReadyAllowedTeams] = "Đội được phép đã sẵn sàng: {0}.",
                [UiMessageKey.ConfirmingNoReadyTeam] = "Chưa phát hiện đội sẵn sàng; đang xác nhận lại ({0}/{1}).",
                [UiMessageKey.DispatchedTeamsNoReadyRemaining] = "Đã điều {0} đội; hiện không còn đội được phép nào sẵn sàng.",
                [UiMessageKey.WaitingForNextTeamCheck] = "Chưa có đội được phép nào sẵn sàng; đang chờ đến lần kiểm tra tiếp theo.",
                [UiMessageKey.ReadinessWaitCancelled] = "Đã hủy chờ đội sẵn sàng.",
                [UiMessageKey.OperationCancelled] = "Thao tác đã được hủy.",
                [UiMessageKey.OperationTimedOut] = "Thao tác đã hết thời gian chờ.",
                [UiMessageKey.DataAccessFailed] = "Không thể đọc hoặc ghi dữ liệu cần thiết.",
                [UiMessageKey.AccessDenied] = "Ứng dụng không có quyền truy cập tài nguyên cần thiết.",
                [UiMessageKey.RequiredFileMissing] = "Không tìm thấy tệp cần thiết để thực hiện thao tác.",
                [UiMessageKey.DeviceUnavailable] = "Không thể kết nối với thiết bị LDPlayer.",
                [UiMessageKey.ScreenshotFailed] = "Không thể chụp ảnh màn hình thiết bị.",
                [UiMessageKey.TemplateMissing] = "Thiếu mẫu nhận diện cần thiết.",
                [UiMessageKey.UnexpectedFailure] = "Đã xảy ra lỗi trong quá trình xử lý."
            };

        public string Get(UiMessageKey key)
        {
            if (!Messages.TryGetValue(key, out string message))
                throw new InvalidOperationException("Thiếu thông điệp giao diện cho khóa " + key + ".");
            return message;
        }

        public string Format(UiMessageKey key, params object[] args)
        {
            string template = Get(key);
            int requiredArgumentCount = RequiredArgumentCount(template);
            if ((args?.Length ?? 0) != requiredArgumentCount)
                throw new ArgumentException("Số đối số định dạng không khớp với " + key + ".",
                    nameof(args));
            return string.Format(CultureInfo.GetCultureInfo("vi-VN"), template, args);
        }

        public static void ValidateCatalog()
        {
            foreach (UiMessageKey key in (UiMessageKey[])Enum.GetValues(typeof(UiMessageKey)))
                _ = Default.Get(key);
        }

        private static int RequiredArgumentCount(string template)
        {
            int highest = -1;
            for (int index = 0; index < template.Length - 2; index++)
            {
                if (template[index] != '{' || !char.IsDigit(template[index + 1])) continue;
                int end = template.IndexOf('}', index + 1);
                if (end < 0) continue;
                if (int.TryParse(template.Substring(index + 1, end - index - 1),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int argument))
                    highest = Math.Max(highest, argument);
            }
            return highest + 1;
        }

        public static VietnameseUserMessageLocalizer Default { get; } =
            new VietnameseUserMessageLocalizer();
    }

    public static class VietnameseDisplayNames
    {
        public static string Stage(MultiDeviceOneShotFarmStage value)
        {
            switch (value)
            {
                case MultiDeviceOneShotFarmStage.Preflight: return "Kiểm tra ban đầu";
                case MultiDeviceOneShotFarmStage.PreflightFailed: return "Kiểm tra thất bại";
                case MultiDeviceOneShotFarmStage.Queued: return "Đang xếp hàng";
                case MultiDeviceOneShotFarmStage.ReadyForGameplay: return "Sẵn sàng thực hiện";
                case MultiDeviceOneShotFarmStage.DispatchingTeam: return "Đang điều đội";
                case MultiDeviceOneShotFarmStage.Requeued: return "Đã nhường lượt";
                case MultiDeviceOneShotFarmStage.Running: return "Đang chạy";
                case MultiDeviceOneShotFarmStage.WaitingForReadyTeam: return "Đang chờ đội";
                case MultiDeviceOneShotFarmStage.Completed: return "Hoàn tất";
                case MultiDeviceOneShotFarmStage.Failed: return "Thất bại";
                case MultiDeviceOneShotFarmStage.Cancelled: return "Đã hủy";
                default: return "Không xác định";
            }
        }

        public static string Stage(OneShotFarmProgressStage value)
        {
            switch (value)
            {
                case OneShotFarmProgressStage.PreparingFarm: return "Đang chuẩn bị";
                case OneShotFarmProgressStage.CheckingTeamAvailability: return "Đang kiểm tra đội";
                case OneShotFarmProgressStage.WaitingForReadyTeam: return "Đang chờ đội";
                case OneShotFarmProgressStage.ReadyTeamFound: return "Đã tìm thấy đội sẵn sàng";
                case OneShotFarmProgressStage.RunningFarmStep: return "Đang thực hiện";
                case OneShotFarmProgressStage.Completed: return "Hoàn tất";
                case OneShotFarmProgressStage.Failed: return "Thất bại";
                case OneShotFarmProgressStage.Cancelled: return "Đã hủy";
                default: return "Không xác định";
            }
        }

        public static string Outcome(OneShotFarmOutcome value)
        {
            switch (value)
            {
                case OneShotFarmOutcome.MarchStarted: return "Đã điều quân";
                case OneShotFarmOutcome.ResourceNotFound: return "Không tìm thấy tài nguyên";
                case OneShotFarmOutcome.ResourceLevelsExhausted: return "Đã thử hết các cấp tài nguyên";
                case OneShotFarmOutcome.Cancelled: return "Đã hủy";
                case OneShotFarmOutcome.NoEligibleTeam: return "Không có đội đủ điều kiện";
                case OneShotFarmOutcome.WorldMapUnavailable: return "Không mở được Bản đồ Thế giới";
                case OneShotFarmOutcome.SearchPanelUnavailable: return "Không mở được bảng tìm tài nguyên";
                case OneShotFarmOutcome.SearchConfigurationFailed: return "Không thể thiết lập tìm kiếm";
                case OneShotFarmOutcome.SearchExecutionFailed: return "Tìm kiếm tài nguyên thất bại";
                case OneShotFarmOutcome.ResourcePopupNotReady: return "Cửa sổ tài nguyên chưa sẵn sàng";
                case OneShotFarmOutcome.TeamSelectionFailed: return "Không thể mở chọn đội";
                case OneShotFarmOutcome.TeamSelectionNotReady: return "Màn hình chọn đội chưa sẵn sàng";
                case OneShotFarmOutcome.TeamDispatchFailed: return "Điều quân thất bại";
                case OneShotFarmOutcome.AllCandidateStoragesFull: return "Kho của các tài nguyên đã chọn đều đầy";
                case OneShotFarmOutcome.ResourcePlanExhausted: return "Đã thử hết kế hoạch tìm tài nguyên";
                case OneShotFarmOutcome.RecoveryFailed: return "Khôi phục thiết bị thất bại";
                case OneShotFarmOutcome.TeamAvailabilityCheckFailed: return "Kiểm tra đội thất bại";
                case OneShotFarmOutcome.TeamAvailabilityWaitTimeout: return "Hết thời gian chờ đội";
                case OneShotFarmOutcome.PreconditionFailed: return "Chưa đáp ứng điều kiện ban đầu";
                case OneShotFarmOutcome.Failed: return "Thất bại";
                default: return "Không xác định";
            }
        }

        public static string State(ContinuousFarmDeviceState value)
        {
            switch (value)
            {
                case ContinuousFarmDeviceState.Preflight: return "Kiểm tra ban đầu";
                case ContinuousFarmDeviceState.Ready: return "Sẵn sàng";
                case ContinuousFarmDeviceState.Running: return "Đang chạy";
                case ContinuousFarmDeviceState.Waiting: return "Đang chờ";
                case ContinuousFarmDeviceState.Recovering: return "Đang khôi phục";
                case ContinuousFarmDeviceState.Quarantined: return "Tạm cách ly";
                case ContinuousFarmDeviceState.Stopped: return "Đã dừng";
                default: return "Không xác định";
            }
        }

        public static string Resource(ResourceType value)
        {
            switch (value)
            {
                case ResourceType.Food: return "Lương thực";
                case ResourceType.Wood: return "Gỗ";
                case ResourceType.Stone: return "Đá";
                case ResourceType.Iron: return "Sắt";
                default: return "Không xác định";
            }
        }

        public static string Team(TeamNumber value) => "Đội " + ((int)value).ToString(
            CultureInfo.GetCultureInfo("vi-VN"));

        public static string Level(int value) => "Cấp " + value.ToString(
            CultureInfo.GetCultureInfo("vi-VN"));

        public static string Duration(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            return value.TotalHours >= 1
                ? string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0} giờ {1} phút {2} giây",
                    (int)value.TotalHours, value.Minutes, value.Seconds)
                : string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0} phút {1} giây",
                    value.Minutes, value.Seconds);
        }
    }

    public sealed class UserErrorPresentation
    {
        public string UserMessage { get; set; }
        public string TechnicalDetails { get; set; }
    }

    public static class UserErrorPresenter
    {
        public static UserErrorPresentation Present(Exception exception)
        {
            UiMessageKey key = exception is OperationCanceledException ? UiMessageKey.OperationCancelled
                : exception is TimeoutException ? UiMessageKey.OperationTimedOut
                : exception is UnauthorizedAccessException ? UiMessageKey.AccessDenied
                : exception is FileNotFoundException ? UiMessageKey.RequiredFileMissing
                : exception is IOException ? UiMessageKey.DataAccessFailed
                : UiMessageKey.UnexpectedFailure;
            return new UserErrorPresentation
            {
                UserMessage = VietnameseUserMessageLocalizer.Default.Get(key),
                TechnicalDetails = exception?.ToString()
            };
        }
    }
}
