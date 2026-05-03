namespace AutoGenerate.AiService
{
    public class RecommendationsRequestDto
    {
        public int IgFollowers { get; set; }
        public string IgEngRate { get; set; } = "0";
        public int IgMediaCount { get; set; }
        public int FbFans { get; set; }
        public string? RecentCaptions { get; set; }
    }

}
