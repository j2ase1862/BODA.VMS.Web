using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class ClientDtoValidator : AbstractValidator<ClientDto>
{
    public ClientDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Client name is required")
            .MaximumLength(50);

        RuleFor(x => x.IpAddress)
            .NotEmpty().WithMessage("IP address is required")
            .MaximumLength(45)
            .Matches(@"^(\d{1,3}\.){3}\d{1,3}$|^[0-9a-fA-F:]+$")
            .WithMessage("IP address must be valid IPv4 or IPv6");

        RuleFor(x => x.ClientIndex)
            .InclusiveBetween(0, 99).WithMessage("Client index must be 0-99");
    }
}
