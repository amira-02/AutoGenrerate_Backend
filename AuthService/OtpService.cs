using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
    using AutoGenerate.Auth;

public class OtpService
{
    private readonly AppDbContext _context;

    public OtpService(AppDbContext context)
    {
        _context = context;
    }

    public string GenerateOtp(string email)
    {
        var code = new Random().Next(100000, 999999).ToString();

        var otp = new OtpCode
        {
            Email = email,
            Code = code,
            Expiration = DateTime.Now.AddMinutes(5)
        };

        _context.OtpCodes.Add(otp);
        _context.SaveChanges();

        return code;
    }

    public bool VerifyOtp(string email, string code)
    {
        var otp = _context.OtpCodes
            .FirstOrDefault(o => o.Email == email && o.Code == code);

        if (otp == null || otp.Expiration < DateTime.Now)
            return false;

        return true;
    }
}