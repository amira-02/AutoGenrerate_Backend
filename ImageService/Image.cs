using AutoGenerate.Shared.Models;

namespace AutoGenerate.ImageService
{
    public class Image
    {
        public int Id { get; set; }

        public string Url { get; set; } = "";

        public string Type { get; set; } = "Generated"; // or Uploaded

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int PostId { get; set; }
        public Post? Post { get; set; }
    }
}
