using BODA.VMS.Web.Endpoints;
using BODA.VMS.Web.Validators.Operations;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Operations;

public class HeartbeatRequestValidatorTests
{
    private readonly HeartbeatRequestValidator _validator = new();

    [Fact]
    public void Valid_minimal_request_passes()
    {
        _validator.TestValidate(new HeartbeatRequest(ClientIndex: 0))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Negative_client_index_fails()
    {
        _validator.TestValidate(new HeartbeatRequest(ClientIndex: -1))
            .ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }

    [Fact]
    public void Long_hostname_fails()
    {
        _validator.TestValidate(new HeartbeatRequest(ClientIndex: 0, HostName: new string('x', 101)))
            .ShouldHaveValidationErrorFor(x => x.HostName);
    }
}

public class DisconnectRequestValidatorTests
{
    private readonly DisconnectRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(new DisconnectRequest(ClientIndex: 0))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Negative_client_index_fails()
    {
        _validator.TestValidate(new DisconnectRequest(ClientIndex: -1))
            .ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }
}

public class ClientRegisterRequestValidatorTests
{
    private readonly ClientRegisterRequestValidator _validator = new();

    [Fact]
    public void Valid_minimal_request_passes()
    {
        _validator.TestValidate(new ClientRegisterRequest(ClientIndex: 0))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void ClientIndex_out_of_range_fails(int idx)
    {
        _validator.TestValidate(new ClientRegisterRequest(ClientIndex: idx))
            .ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }

    [Fact]
    public void Invalid_ip_format_fails()
    {
        _validator.TestValidate(new ClientRegisterRequest(ClientIndex: 0, IpAddress: "not-an-ip"))
            .ShouldHaveValidationErrorFor(x => x.IpAddress);
    }

    [Fact]
    public void Null_ip_passes()
    {
        // Optional field — null OK
        _validator.TestValidate(new ClientRegisterRequest(ClientIndex: 0, IpAddress: null))
            .ShouldNotHaveAnyValidationErrors();
    }
}
