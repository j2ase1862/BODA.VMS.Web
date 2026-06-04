using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BODA.VMS.Web.Middleware;

/// <summary>
/// 컬렉션 (List/IEnumerable&lt;T&gt;) 본문 endpoint 용 검증 필터. ValidationEndpointFilter
/// 는 단건 T 만 인식 — 배치 endpoint 는 본 필터를 사용해 각 항목을 검증한다.
///
/// 사용:
///   app.MapPost("/api/parameters/batch", ...)
///      .AddEndpointFilter&lt;CollectionValidationEndpointFilter&lt;RecipeParameterDto&gt;&gt;();
///
/// 동작:
/// - 인수 중 IEnumerable&lt;T&gt; 첫번째 발견
/// - null/빈 컬렉션은 next() 로 통과 — 비즈니스 로직이 처리
/// - 각 항목을 ValidateAsync 후 모든 에러를 항목 인덱스 prefix 와 함께 집계
///   (예: `[2].Description`) → ValidationProblemDetails 반환
/// </summary>
public sealed class CollectionValidationEndpointFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T>? _validator;

    public CollectionValidationEndpointFilter(IValidator<T>? validator = null)
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

        var collection = context.Arguments
            .OfType<IEnumerable<T>>()
            .FirstOrDefault();
        if (collection is null)
        {
            return await next(context);
        }

        var items = collection.ToList();
        if (items.Count == 0)
        {
            return await next(context);
        }

        var errorsByKey = new Dictionary<string, List<string>>();
        for (var i = 0; i < items.Count; i++)
        {
            var result = await _validator.ValidateAsync(items[i]);
            if (result.IsValid) continue;

            foreach (var err in result.Errors)
            {
                var key = $"[{i}].{err.PropertyName}";
                if (!errorsByKey.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    errorsByKey[key] = list;
                }
                list.Add(err.ErrorMessage);
            }
        }

        if (errorsByKey.Count == 0)
        {
            return await next(context);
        }

        var problem = new ValidationProblemDetails(
            errorsByKey.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()))
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
}
