using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BODA.VMS.Web.Middleware;

/// <summary>
/// Minimal API endpoint filter — Tin 자리의 인수 중 IValidator&lt;T&gt; 가 등록된 타입을
/// 자동 발견해 ValidateAsync 수행. 실패시 RFC 7807 ValidationProblemDetails 반환.
///
/// 사용:
///   app.MapPost("/auth/login", ...).AddEndpointFilter&lt;ValidationEndpointFilter&lt;LoginRequest&gt;&gt;();
///
/// 등록 가능한 Validator 가 없으면 fall-through (검증 skip).
/// </summary>
public sealed class ValidationEndpointFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T>? _validator;

    public ValidationEndpointFilter(IValidator<T>? validator = null)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (_validator is null)
        {
            return await next(context);
        }

        var target = context.Arguments.OfType<T>().FirstOrDefault();
        if (target is null)
        {
            return await next(context);
        }

        var result = await _validator.ValidateAsync(target);
        if (!result.IsValid)
        {
            var problem = new ValidationProblemDetails(
                result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            };
            return Results.Problem(
                detail: problem.Detail,
                instance: problem.Instance,
                statusCode: problem.Status,
                title: problem.Title,
                type: problem.Type,
                extensions: problem.Extensions
                    .Concat(new[] { new KeyValuePair<string, object?>("errors", problem.Errors) })
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        return await next(context);
    }
}
