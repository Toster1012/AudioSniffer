using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace AudioSniffer.Components.Middleware
{
    public class RequestValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RequestValidationMiddleware> _logger;

        public RequestValidationMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<RequestValidationMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext http_context)
        {
            // Проверка размера запроса
            int max_request_size = _configuration.GetValue<int>("Security:MaxRequestSizeBytes", 10485760); // 10MB по умолчанию

            if (http_context.Request.ContentLength.HasValue && http_context.Request.ContentLength.Value > max_request_size)
            {
                _logger.LogWarning("Request size limit exceeded from IP: {IpAddress}. Size: {RequestSize} bytes",
                    http_context.Connection.RemoteIpAddress, http_context.Request.ContentLength.Value);

                http_context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                http_context.Response.ContentType = "application/json";
                await http_context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "error",
                    code = "request_too_large",
                    message = $"Request size exceeds the maximum allowed size of {max_request_size} bytes"
                }));
                return;
            }

            // Проверка типа содержимого
            string? content_type = http_context.Request.ContentType;
            if (!string.IsNullOrEmpty(content_type))
            {
                if (content_type.Contains("application/json"))
                {
                    // Проверка глубины JSON
                    int max_json_depth = _configuration.GetValue<int>("Security:MaxJsonDepth", 32);

                    if (http_context.Request.ContentLength.HasValue && http_context.Request.ContentLength.Value > 0)
                    {
                        try
                        {
                            http_context.Request.EnableBuffering();
                            byte[] request_buffer = new byte[http_context.Request.ContentLength.Value];
                            await http_context.Request.Body.ReadAsync(request_buffer, 0, request_buffer.Length);
                            string request_body = System.Text.Encoding.UTF8.GetString(request_buffer);
                            http_context.Request.Body.Position = 0;

                            // Проверка глубины JSON
                            JsonDocumentOptions json_options = new JsonDocumentOptions
                            {
                                MaxDepth = max_json_depth,
                                CommentHandling = JsonCommentHandling.Skip
                            };

                            using (JsonDocument.Parse(request_body, json_options))
                            {
                                // JSON валиден и глубина в пределах нормы
                            }
                        }
                        catch (JsonException json_exception) when (json_exception.Message.Contains("depth"))
                        {
                            _logger.LogWarning("JSON depth limit exceeded from IP: {IpAddress}. Error: {ErrorMessage}",
                                http_context.Connection.RemoteIpAddress, json_exception.Message);

                            http_context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            http_context.Response.ContentType = "application/json";
                            await http_context.Response.WriteAsync(JsonSerializer.Serialize(new
                            {
                                status = "error",
                                code = "json_depth_exceeded",
                                message = $"JSON depth exceeds the maximum allowed depth of {max_json_depth}"
                            }));
                            return;
                        }
                        catch (Exception processing_exception)
                        {
                            _logger.LogWarning("Invalid JSON from IP: {IpAddress}. Error: {ErrorMessage}",
                                http_context.Connection.RemoteIpAddress, processing_exception.Message);

                            http_context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            http_context.Response.ContentType = "application/json";
                            await http_context.Response.WriteAsync(JsonSerializer.Serialize(new
                            {
                                status = "error",
                                code = "invalid_json",
                                message = "Invalid JSON format"
                            }));
                            return;
                        }
                    }
                }
                else if (!content_type.StartsWith("application/") &&
                         !content_type.StartsWith("text/") &&
                         !content_type.StartsWith("multipart/") &&
                         !content_type.StartsWith("image/") &&
                         !content_type.StartsWith("video/") &&
                         !content_type.StartsWith("audio/"))
                {
                    _logger.LogWarning("Invalid content type from IP: {IpAddress}. ContentType: {ContentType}",
                        http_context.Connection.RemoteIpAddress, content_type);

                    http_context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    http_context.Response.ContentType = "application/json";
                    await http_context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        status = "error",
                        code = "unsupported_media_type",
                        message = "Unsupported media type"
                    }));
                    return;
                }
            }

            await _next(http_context);
        }
    }
}