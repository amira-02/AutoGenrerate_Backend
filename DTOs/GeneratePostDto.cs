namespace AutoPost.Api.Dtos
{
    public class GeneratePostDto
    {
        public string Prompt { get; set; }
        public IFormFile JsonFile { get; set; }
    }
}