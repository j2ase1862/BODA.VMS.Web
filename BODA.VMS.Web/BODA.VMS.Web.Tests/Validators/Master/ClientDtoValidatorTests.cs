using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class ClientDtoValidatorTests
{
    private readonly ClientDtoValidator _validator = new();

    private static ClientDto Valid() => new()
    {
        Name = "Line A",
        IpAddress = "192.168.1.10",
        ClientIndex = 0
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("192.168.1.1")]    // IPv4
    [InlineData("10.0.0.1")]
    [InlineData("fe80::1")]         // IPv6
    [InlineData("::1")]
    public void Valid_ip_formats_pass(string ip)
    {
        var dto = Valid();
        dto.IpAddress = ip;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("192.168.1")]      // 불완전 IPv4
    [InlineData("hello world")]
    public void Invalid_ip_formats_fail(string ip)
    {
        var dto = Valid();
        dto.IpAddress = ip;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.IpAddress);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void ClientIndex_out_of_range_fails(int idx)
    {
        var dto = Valid();
        dto.ClientIndex = idx;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }
}
