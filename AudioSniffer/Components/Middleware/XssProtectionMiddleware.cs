using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace AudioSniffer.Components.Middleware
{
    public class XssProtectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<XssProtectionMiddleware> _logger;

        public XssProtectionMiddleware(RequestDelegate next, ILogger<XssProtectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Method == "GET")
            {
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> queryParam in context.Request.Query)
                {
                    if (ContainsXssPattern(queryParam.Value.ToString()))
                    {
                        _logger.LogWarning("Potential XSS attack detected in query parameter: {ParamName} from IP: {IpAddress}",
                            queryParam.Key, context.Connection.RemoteIpAddress);

                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            status = "error",
                            code = "xss_detected",
                            message = "Potential XSS attack detected"
                        }));
                        return;
                    }
                }
            }
            else if (context.Request.Method == "POST" || context.Request.Method == "PUT")
            {
                if (context.Request.ContentType?.Contains("application/json") == true)
                {
                    context.Request.EnableBuffering();
                    byte[] buffer = new byte[Convert.ToInt32(context.Request.ContentLength)];
                    await context.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                    string requestBody = System.Text.Encoding.UTF8.GetString(buffer);
                    context.Request.Body.Position = 0;

                    if (ContainsXssPattern(requestBody))
                    {
                        _logger.LogWarning("Potential XSS attack detected in request body from IP: {IpAddress}",
                            context.Connection.RemoteIpAddress);

                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            status = "error",
                            code = "xss_detected",
                            message = "Potential XSS attack detected"
                        }));
                        return;
                    }
                }

                if (context.Request.HasFormContentType)
                {
                    foreach (string formKey in context.Request.Form.Keys)
                    {
                        Microsoft.Extensions.Primitives.StringValues formValue = context.Request.Form[formKey];
                        string? formValueString = formValue.ToString();
                        if (!string.IsNullOrEmpty(formValueString) && ContainsXssPattern(formValueString))
                        {
                            _logger.LogWarning("Potential XSS attack detected in form data from IP: {IpAddress}",
                                context.Connection.RemoteIpAddress);

                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                            {
                                status = "error",
                                code = "xss_detected",
                                message = "Potential XSS attack detected"
                            }));
                            return;
                        }
                    }
                }
            }

            await _next(context);
        }

        private bool ContainsXssPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            string[] xssPatterns = new[]
            {
                "<script.*?>.*?</script>",
                "javascript:",
                "onerror\\s*=",
                "onclick\\s*=",
                "onload\\s*=",
                "onmouseover\\s*=",
                "eval\\(",
                "document\\.cookie",
                "window\\.location",
                "alert\\(",
                "&#x.*?;",
                "&#.*?;",
                "<.*?javascript:.*?>",
                "<.*?on.*?=.*?>",
                "expression\\(",
                "vbscript:",
                "fromCharCode"
            };

            foreach (string pattern in xssPatterns)
            {
                if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}