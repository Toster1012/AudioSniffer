using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace AudioSniffer.Components.Middleware
{
    public class AdminAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _protector;
        private readonly ILogger<AdminAuthMiddleware> _logger;

        public AdminAuthMiddleware(RequestDelegate next, IConfiguration configuration, IDataProtectionProvider dataProtectionProvider, ILogger<AdminAuthMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _protector = dataProtectionProvider.CreateProtector("AdminAuthMiddleware");
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/admin") && !context.Request.Path.StartsWithSegments("/admin/auth"))
            {
                string adminKey = _configuration["Admin:AccessKey"] ?? "SECURE_ADMIN_KEY_123";
                string protectedCookieName = "admin_access_secure";
                string? cookieValue = context.Request.Cookies[protectedCookieName];
                string? authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

                bool isAuthorized = false;

                if (!string.IsNullOrEmpty(cookieValue))
                {
                    try
                    {
                        string unprotectedValue = _protector.Unprotect(cookieValue);
                        if (unprotectedValue == adminKey)
                        {
                            isAuthorized = true;
                        }
                    }
                    catch (CryptographicException)
                    {
                        _logger.LogWarning("Attempt to use tampered admin cookie from IP: {IpAddress}", context.Connection.RemoteIpAddress);
                    }
                }
                else if (!string.IsNullOrEmpty(authHeader))
                {
                    if (authHeader.StartsWith("Bearer ") && authHeader.Substring(7) == adminKey)
                    {
                        isAuthorized = true;
                    }
                    else if (authHeader == adminKey)
                    {
                        isAuthorized = true;
                    }
                }

                string attemptKey = $"admin_auth_attempt_{context.Connection.RemoteIpAddress}";
                int attempts = context.Session?.GetInt32(attemptKey) ?? 0;

                if (attempts >= 5)
                {
                    int lockoutTime = context.Session?.GetInt32($"{attemptKey}_lockout") ?? 0;
                    int currentTime = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

                    if (currentTime - lockoutTime < 300)
                    {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync($@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>429 Слишком много попыток</title>
                            <meta charset=""utf-8"">
                            <style>
                                body {{
                                    font-family: -apple-system, BlinkMacSystemFont, 'SF Pro Display', sans-serif;
                                    background: #f5f5f7;
                                    color: #1d1d1f;
                                    display: flex;
                                    justify-content: center;
                                    align-items: center;
                                    height: 100vh;
                                    margin: 0;
                                }}
                                .container {{
                                    text-align: center;
                                    max-width: 500px;
                                    padding: 24px;
                                }}
                                h1 {{
                                    font-size: 48px;
                                    font-weight: 700;
                                    margin-bottom: 16px;
                                    color: #ff3b30;
                                }}
                                p {{
                                    font-size: 18px;
                                    line-height: 1.4;
                                    margin-bottom: 32px;
                                    color: #86868b;
                                }}
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <h1>Слишком много попыток</h1>
                                <p>Вы временно заблокированы из-за слишком многих неудачных попыток авторизации. Пожалуйста, попробуйте снова через 5 минут.</p>
                            </div>
                        </body>
                        </html>
                    ");
                    return;
                    }
                    else
                    {
                        context.Session?.SetInt32(attemptKey, 0);
                    }
                }

                if (!isAuthorized)
                {
                    attempts++;
                    context.Session?.SetInt32(attemptKey, attempts);
                    if (attempts == 5)
                    {
                        context.Session?.SetInt32($"{attemptKey}_lockout", (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
                    }

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "text/html; charset=utf-8";

                    await context.Response.WriteAsync(@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>401 Доступ запрещен</title>
                            <meta charset=""utf-8"">
                            <style>
                                body {
                                    font-family: -apple-system, BlinkMacSystemFont, 'SF Pro Display', sans-serif;
                                    background: #f5f5f7;
                                    color: #1d1d1f;
                                    display: flex;
                                    flex-direction: column;
                                    justify-content: center;
                                    align-items: center;
                                    height: 100vh;
                                    margin: 0;
                                    padding: 24px;
                                }
                                .container {
                                    text-align: center;
                                    max-width: 500px;
                                }
                                h1 {
                                    font-size: 48px;
                                    font-weight: 700;
                                    margin-bottom: 16px;
                                    color: #ff3b30;
                                }
                                p {
                                    font-size: 18px;
                                    line-height: 1.4;
                                    margin-bottom: 32px;
                                    color: #86868b;
                                }
                                .login-form {
                                    background: white;
                                    border-radius: 12px;
                                    padding: 24px;
                                    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
                                    width: 100%;
                                    max-width: 400px;
                                }
                                .form-group {
                                    margin-bottom: 20px;
                                    text-align: left;
                                }
                                label {
                                    display: block;
                                    font-size: 16px;
                                    font-weight: 600;
                                    margin-bottom: 8px;
                                    color: #1d1d1f;
                                }
                                input {
                                    width: 100%;
                                    padding: 12px;
                                    border: 1px solid #d2d2d7;
                                    border-radius: 8px;
                                    font-size: 16px;
                                    box-sizing: border-box;
                                }
                                button {
                                    width: 100%;
                                    padding: 12px;
                                    background: #0071e3;
                                    color: white;
                                    border: none;
                                    border-radius: 8px;
                                    font-size: 16px;
                                    font-weight: 600;
                                    cursor: pointer;
                                    transition: background 0.2s;
                                }
                                button:hover {
                                    background: #0077ed;
                                }
                                .error-message {
                                    color: #ff3b30;
                                    font-size: 14px;
                                    margin-top: 8px;
                                    display: none;
                                }
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <h1>Доступ запрещен</h1>
                                <p>Эта страница требует прав администратора. Пожалуйста, авторизуйтесь для продолжения.</p>

                                <div class='login-form'>
                                    <div class='form-group'>
                                        <label for='adminKey'>Ключ доступа администратора</label>
                                        <input type='password' id='adminKey' placeholder='Введите ключ доступа' autocomplete='off'>
                                        <div class='error-message' id='errorMessage'>Неверный ключ доступа</div>
                                    </div>
                                    <button onclick='authenticate()'>Авторизоваться</button>
                                </div>
                            </div>

                            <script>
                                function authenticate() {
                                    const key = document.getElementById('adminKey').value;
                                    const errorMessage = document.getElementById('errorMessage');

                                    if (!key) {
                                        errorMessage.style.display = 'block';
                                        errorMessage.textContent = 'Пожалуйста, введите ключ доступа';
                                        return;
                                    }

                                    const csrfToken = getCookie('X-CSRF-TOKEN-COOKIE');

                                    fetch('/admin/auth', {
                                        method: 'POST',
                                        headers: {
                                            'Content-Type': 'application/json',
                                            'X-CSRF-TOKEN': csrfToken
                                        },
                                        body: JSON.stringify({ key: key })
                                    })
                                    .then(response => {
                                        if (response.ok) {
                                            window.location.reload();
                                        } else {
                                            errorMessage.style.display = 'block';
                                            errorMessage.textContent = 'Неверный ключ доступа';
                                        }
                                    })
                                    .catch(error => {
                                        errorMessage.style.display = 'block';
                                        errorMessage.textContent = 'Ошибка авторизации';
                                    });
                                }

                                function getCookie(name) {
                                    const value = `; ${document.cookie}`;
                                    const parts = value.split(`; ${name}=`);
                                    if (parts.length === 2) return parts.pop().split(';').shift();
                                }

                                document.getElementById('adminKey').addEventListener('keypress', function(e) {
                                    if (e.key === 'Enter') {
                                        authenticate();
                                    }
                                });
                            </script>
                        </body>
                        </html>
                    ");
                    return;
                }
            }

            await _next(context);
        }
    }
}