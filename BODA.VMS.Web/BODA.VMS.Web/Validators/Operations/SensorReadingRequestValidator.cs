using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Operations;

public sealed class SensorReadingRequestValidator : AbstractValidator<SensorReadingRequest>
{
    public SensorReadingRequestValidator()
    {
        RuleFor(x => x.ClientIndex).GreaterThanOrEqualTo(0);

        // 적어도 하나의 센서값은 제공돼야 함 (기존 inline 검증 이전).
        RuleFor(x => x)
            .Must(s => s.TemperatureC.HasValue || s.HumidityPct.HasValue
                      || s.VibrationRms.HasValue || s.PressurePsi.HasValue)
            .WithMessage("At least one sensor value (TemperatureC/HumidityPct/VibrationRms/PressurePsi) must be provided");

        // 물리적 범위 (값이 있을 때만)
        RuleFor(x => x.TemperatureC).InclusiveBetween(-50, 150).When(x => x.TemperatureC.HasValue);
        RuleFor(x => x.HumidityPct).InclusiveBetween(0, 100).When(x => x.HumidityPct.HasValue);
        RuleFor(x => x.VibrationRms).GreaterThanOrEqualTo(0).When(x => x.VibrationRms.HasValue);
        RuleFor(x => x.PressurePsi).GreaterThanOrEqualTo(0).When(x => x.PressurePsi.HasValue);
    }
}
