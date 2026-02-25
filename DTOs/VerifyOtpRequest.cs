namespace AutoPost.Api.DTOs
{
    public class VerifyOtpDto
    {
        public string Email { get; set; } = null!;
        public string Code { get; set; } = null!;
    }
}
