﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using DaxnetBlog.Web.Security;
using DaxnetBlog.Web.Models;
using System.Text;
using DaxnetBlog.Common;
using DaxnetBlog.Common.IntegrationServices;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace DaxnetBlog.Web.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private static readonly Crypto crypto = Crypto.Create(CryptoTypes.EncAes);
        private readonly UserManager<User> userManager;
        private readonly SignInManager<User> signInManager;
        private readonly IEmailService emailService;

        public AccountController(UserManager<User> userManager,
            SignInManager<User> signInManager,
            IEmailService emailService)
        {
            this.userManager = userManager;
            this.signInManager = signInManager;
            this.emailService = emailService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        //
        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (ModelState.IsValid)
            {
                var captchaString = this.Request.Form["__captcha_image"];
                var encryptedString = crypto.Encrypt(model.Captcha.ToLower(), "*trak");
                if (captchaString != encryptedString)
                {
                    ModelState.AddModelError("", "验证码不正确。");
                    return View(nameof(Login));
                }
                // This doesn't count login failures towards account lockout
                // To enable password failures to trigger account lockout, set lockoutOnFailure: true
                var result = await signInManager.PasswordSignInAsync(model.UserName, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    return RedirectToLocal(returnUrl);
                }
                else if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "登录失败，该帐号已被锁定。");
                    return View(model);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "登录失败，请检查用户名或密码。");
                    return View(model);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogOff()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult PreRegister()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register(string returnUrl = null)
        {
            ViewData["ShowMessage"] = false;
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (ModelState.IsValid)
            {
                var captchaString = this.Request.Form["__captcha_image"];
                var encryptedString = crypto.Encrypt(model.Captcha.ToLower(), "*trak");
                if (captchaString != encryptedString)
                {
                    ModelState.AddModelError("", "验证码不正确。");
                    return View(nameof(Register));
                }

                var user = new User
                {
                    EmailAddress = model.Email,
                    UserName = model.UserName,
                    NickName = model.NickName
                };

                var registerResult = await userManager.CreateAsync(user, model.Password);

                if (registerResult.Succeeded)
                {
                    var verificationCode = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    var callbackUrl = Url.Action(nameof(ConfirmEmail), "Account", new { userName = Convert.ToBase64String(Encoding.ASCII.GetBytes(user.UserName)), code = verificationCode }, protocol: HttpContext.Request.Scheme);
                    await emailService.SendEmailAsync(user.NickName, user.EmailAddress,
                        "站点认证信息（daxnet.me）",
                        $@"感谢您注册成为daxnet.me站点的会员，请<a href=""{callbackUrl}"">【点击此处】</a>完成帐号验证。谢谢！");
                    ViewData["ShowMessage"] = true;
                    ViewData["MessageTitle"] = "注册成功";
                    ViewData["MessageBody"] = @"验证码已发送至注册邮箱，请点击邮件中链接激活帐号。如在一定时间内仍未收到激活链接，请发邮件至daxnet@live.com与站长联系。";
                }
                else
                {
                    foreach (var error in registerResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> Manage(string category="Profile")
        {
            var accountModel = await this.userManager.FindByNameAsync(User.Identity.Name);
            return View(new { category, accountModel }.ToExpando());
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(AccountProfileViewModel model)
        {
            var identityResult = await userManager.UpdateAsync(new User
            {
                Id = model.Id,
                NickName = model.NickName,
                EmailAddress = model.EmailAddress
            });

            return Ok(identityResult.Succeeded);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = new User { Id = model.Id };
            var identityResult = await userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);

            return Ok(identityResult.Succeeded);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userName, string code)
        {
            if (userName == null || code == null)
            {
                return View(nameof(ConfirmEmail), IdentityResult.Failed(new IdentityError { Description = "待验证的数据不正确。" }));
            }
            var userNameDecoded = Encoding.ASCII.GetString(Convert.FromBase64String(userName));

            var user = await userManager.FindByNameAsync(userNameDecoded);
            if (user == null)
            {
                return View(nameof(ConfirmEmail), IdentityResult.Failed(new IdentityError { Description = "待验证的用户不存在。" }));
            }

            var result = await userManager.ConfirmEmailAsync(user, code);
            return View(nameof(ConfirmEmail), result);
        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }
    }
}
