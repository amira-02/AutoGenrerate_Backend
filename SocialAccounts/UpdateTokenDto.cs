namespace AutoGenerate.SocialAccounts
{
    public class UpdateTokenDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
    }
}
