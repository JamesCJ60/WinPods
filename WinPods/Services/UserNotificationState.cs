using System.Runtime.InteropServices;

namespace WinPods.Services;

public static class UserNotificationState
{
    public static bool ShouldSuppressAutomaticPopup()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QueryUserNotificationState.Busy
            or QueryUserNotificationState.RunningDirect3DFullScreen
            or QueryUserNotificationState.PresentationMode;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

    private enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }
}
