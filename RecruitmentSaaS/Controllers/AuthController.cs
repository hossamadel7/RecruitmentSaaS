using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    public class AuthController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public AuthController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToDashboard(User.FindFirst(ClaimTypes.Role)?.Value);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var user = _context.Users
                .FirstOrDefault(u => u.Email == dto.Email && u.IsActive == true);

            if (user == null || user.PasswordHash != dto.Password)
            {
                ModelState.AddModelError("", "البريد الإلكتروني أو كلمة المرور غير صحيحة");
                return View(dto);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("BranchId", user.BranchId.ToString()),
                new Claim("TenantSchema", "demorecruitment")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });

            return RedirectToDashboard(user.Role.ToString());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied() => View();

        private IActionResult RedirectToDashboard(string? role)
        {
            return role switch
            {
                "1" => RedirectToAction("Index", "Admin"),
                "2" => RedirectToAction("Index", "Reception"),
                "3" => RedirectToAction("Index", "TeleSales"),
                "4" => RedirectToAction("Index", "Accountant"),
                "5" => RedirectToAction("Index", "Operations"),
                "6" => RedirectToAction("Index", "Sales"),
                _ => RedirectToAction("Login")
            };
        }
    }
}
