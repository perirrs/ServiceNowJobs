namespace SNHub.Notifications.Domain.Enums;
public enum NotificationType
{
    JobMatch         = 1,
    ApplicationStatusChanged = 2,
    NewMessage       = 3,
    CommunityMention = 4,
    SystemAlert      = 5,
    WeeklyDigest     = 6,
    ProfileView      = 7
}
public enum NotificationChannel { InApp = 1, Email = 2 }
