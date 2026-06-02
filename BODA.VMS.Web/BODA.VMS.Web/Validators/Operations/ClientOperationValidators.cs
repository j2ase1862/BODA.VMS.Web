using BODA.VMS.Web.Endpoints;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Operations;

// HeartbeatRequest / DisconnectRequest / ClientRegisterRequest 는 ClientEndpoints.cs
// 내부 public record. 같은 파일에 검증 규칙 묶음.

public sealed class HeartbeatRequestValidator : AbstractValidator<HeartbeatRequest>
{
    public HeartbeatRequestValidator()
    {
        RuleFor(x => x.ClientIndex).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HostName).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.HostName));
        RuleFor(x => x.SwName).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.SwName));
    }
}

public sealed class DisconnectRequestValidator : AbstractValidator<DisconnectRequest>
{
    public DisconnectRequestValidator()
    {
        RuleFor(x => x.ClientIndex).GreaterThanOrEqualTo(0);
    }
}

public sealed class ClientRegisterRequestValidator : AbstractValidator<ClientRegisterRequest>
{
    public ClientRegisterRequestValidator()
    {
        RuleFor(x => x.ClientIndex)
            .InclusiveBetween(0, 99).WithMessage("ClientIndex must be 0-99");

        RuleFor(x => x.Name)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Name));

        RuleFor(x => x.IpAddress)
            .Matches(@"^(\d{1,3}\.){3}\d{1,3}$|^[0-9a-fA-F:]+$")
            .When(x => !string.IsNullOrEmpty(x.IpAddress))
            .WithMessage("IP address must be valid IPv4 or IPv6");
    }
}
