namespace AutoGenerate.PostService.DTOs
{
    
        public class CreatePostDto
        {
            public string Topic { get; set; } = "";
            public string Hashtags { get; set; } = "";

            public string ToneOfVoice { get; set; } = "";
            public string CaptionLength { get; set; } = "";
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