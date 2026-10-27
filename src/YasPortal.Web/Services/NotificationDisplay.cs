using YasPortal.Domain.Notifications;

namespace YasPortal.Web.Services;

/// <summary>How a notification kind is named and drawn, shared by the personal list and the all-notifications report.</summary>
public static class NotificationDisplay
{
    public static string KindLabel(NotificationKind kind) => kind switch
    {
        NotificationKind.StepReached => "رسیدن درخواست به مرحله شما",
        NotificationKind.ReturnedToStep => "بازگشت درخواست به مرحله شما",
        NotificationKind.RequestApproved => "تایید نهایی درخواست",
        NotificationKind.RequestRejected => "رد درخواست",
        NotificationKind.RequestReturned => "بازگشت درخواست به شما",
        NotificationKind.RequestForceClosed => "بسته‌شدن توسط مدیر",
        NotificationKind.RequestReopened => "بازگشایی درخواست",
        NotificationKind.DelegationReceived => "تفویض دریافتی",
        NotificationKind.RequestCreatedForYou => "ثبت درخواست توسط مدیر",
        NotificationKind.VacantPosition => "سمت خالی",
        NotificationKind.Manual => "پیام دستی",
        _ => kind.ToString(),
    };

    public static string IconFor(NotificationKind kind) => kind switch
    {
        NotificationKind.StepReached => Icons.Inbox,
        NotificationKind.ReturnedToStep => Icons.Returned,
        NotificationKind.RequestApproved => Icons.Approved,
        NotificationKind.RequestRejected => Icons.Rejected,
        NotificationKind.RequestReturned => Icons.Returned,
        NotificationKind.RequestForceClosed => Icons.ForceClose,
        NotificationKind.RequestCreatedForYou => Icons.NewRequestForOthers,
        NotificationKind.VacantPosition => Icons.PositionVacant,
        NotificationKind.Manual => Icons.Message,
        _ => Icons.Bell,
    };
}
