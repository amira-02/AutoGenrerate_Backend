namespace AutoGenerate.PostService
{
    public class PostDto
    {
        public int Id { get; set; }
        public int TopicId { get; set; }
        public string TopicName { get; set; } = "";
        public string Hashtags { get; set; } = "";
        public string? Caption { get; set; }
        public string? Tone { get; set; }
        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? ScheduledAt { get; set; }
        public string Status { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public List<string> Platforms { get; set; } = new();
    }
}
