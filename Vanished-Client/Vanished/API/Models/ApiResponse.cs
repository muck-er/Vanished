namespace Vanished.API.Models
{
    public class ApiResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public string token { get; set; } = string.Empty;
        public string access_token { get; set; } = string.Empty;
        public string refresh_token { get; set; } = string.Empty;
        public bool requires_mfa { get; set; }
        public string step_up_token { get; set; } = string.Empty;
        public string email_verification_token { get; set; } = string.Empty;
        public int expires_in_seconds { get; set; }
        public int resend_available_in_seconds { get; set; }
        public int cooldown_seconds { get; set; }
        public bool available { get; set; }
        public string username { get; set; } = string.Empty;
    }
}
