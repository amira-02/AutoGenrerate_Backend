namespace AutoGenerate.SocialAccounts
{
    public class SaveAccountDto
    {
        public int Id { get; set; }          // 0 = create new, >0 = update specific account
        public int ClientId { get; set; }    // FK to Clients table
        public int PlatformId { get; set; }  // FK to Platforms table
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public string? AccountId { get; set; }
        public string? Username { get; set; }
    }
}
