
namespace AutoPost.Api.DTOs
{
    public class CreatePostDto
    {
        public string Topic { get; set; } = string.Empty;
        public string Hashtags { get; set; } = string.Empty;
    }
}



//namespace AutoPost.Api.Dtos
//{
//    public class GeneratePostDto
//    {
//        public string Topic { get; set; }
//        public string Hashtags { get; set; }
//        //public string Prompt { get; set; }
//        //public IFormFile? JsonFile { get; set; }
//    }
//}