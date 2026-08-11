using System;

namespace VibeDeck.Host.WindowsNotifications
{
    public sealed class WindowsNotificationStatusResponse
    {
        public bool Supported { get; set; }
        public bool Packaged { get; set; }
        public bool CompanionRequired { get; set; }
        public bool CompanionConnected { get; set; }
        public string ActivationUri { get; set; }
        public bool Enabled { get; set; }
        public bool Listening { get; set; }
        public string AccessStatus { get; set; }
        public string Message { get; set; }
        public string SourceKey { get; set; }
        public string CardId { get; set; }
        public string LastCapturedAt { get; set; }
        public int CapturedCount { get; set; }
        public string CompanionSeenAt { get; set; }
    }

    public sealed class WindowsNotificationCompanionHeartbeat
    {
        public string AccessStatus { get; set; }
        public string Message { get; set; }
    }

    public sealed class WindowsNotificationCompanionEvent
    {
        public string Id { get; set; }
        public string From { get; set; }
        public string Text { get; set; }
        public string Timestamp { get; set; }
    }
}
