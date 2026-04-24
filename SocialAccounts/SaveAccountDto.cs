namespace AutoGenerate.SocialAccounts
{
    public class SaveAccountDto
    {
        public string Platform { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string? AccountId { get; set; }
        public string? Username { get; set; }
    }
}
