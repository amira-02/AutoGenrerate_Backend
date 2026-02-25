using System;

namespace AutoPost.Api.Services
{
    public class EmailService
    {
        public void SendOtp(string email, string code)
        {
            Console.WriteLine($"OTP for {email} is {code}");
        }
    }
}
