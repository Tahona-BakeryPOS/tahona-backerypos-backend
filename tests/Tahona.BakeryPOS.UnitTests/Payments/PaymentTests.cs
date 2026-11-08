using System.Security.Cryptography;
using System.Text;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Payments;

namespace Tahona.BakeryPOS.UnitTests.Payments;

public class PaymentTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Company = Guid.NewGuid();

    private static Payment NewGatewayPayment(decimal amount = 15_000m)
    {
        var method = PaymentMethod.Create(Company, "BOLD_NEQUI", "Nequi", PaymentMethodKind.Gateway, BoldMethods.Nequi, false, 0);
        return Payment.StartGateway(Company, PaymentPurpose.Test, null, Guid.NewGuid(), method, amount);
    }

    [Fact]
    public void Signature_is_hmac_sha256_hex_of_the_base64_body()
    {
        const string body = """{"id":"1","type":"SALE_APPROVED"}""";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secreta"), Encoding.UTF8.GetBytes(base64))).ToLowerInvariant();

        Assert.Equal(expected, BoldSignature.Compute(body, "secreta"));
        Assert.True(BoldSignature.IsValid(body, "secreta", expected.ToUpperInvariant()));
        Assert.False(BoldSignature.IsValid(body, "otra", expected));
        Assert.False(BoldSignature.IsValid(body + " ", "secreta", expected));
        Assert.False(BoldSignature.IsValid(body, "secreta", null));
    }

    [Fact]
    public void Sandbox_signs_with_an_empty_key()
    {
        const string body = """{"id":"2"}""";
        Assert.Equal(BoldSignature.Compute(body, ""), BoldSignature.Compute(body, null));
    }

    [Fact]
    public void Gateway_payment_starts_pending_and_resolves_once()
    {
        var payment = NewGatewayPayment();
        Assert.True(payment.IsPending);

        payment.ApproveByGateway(Now, "123456");
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.False(payment.ResolvedManually);
        Assert.Throws<DomainException>(() => payment.DeclineByGateway(Now));
    }

    [Fact]
    public void Manual_approval_needs_the_approval_number_and_a_note()
    {
        var payment = NewGatewayPayment();
        Assert.Throws<DomainException>(() => payment.ResolveManually(PaymentStatus.Approved, Guid.NewGuid(), Now, null, "sin webhook"));
        Assert.Throws<DomainException>(() => payment.ResolveManually(PaymentStatus.Approved, Guid.NewGuid(), Now, "123", " "));

        payment.ResolveManually(PaymentStatus.Approved, Guid.NewGuid(), Now, "123", "El webhook no llegó; recibo del datáfono");
        Assert.True(payment.ResolvedManually);
        Assert.Equal("123", payment.Reference);
    }

    [Fact]
    public void Attempt_reference_is_its_id_and_closed_attempts_do_not_change()
    {
        var payment = NewGatewayPayment();
        var attempt = PaymentAttempt.Create(payment, GatewayEnvironment.Sandbox, BoldMethods.Nequi, "N860W000000");
        Assert.Equal(attempt.Id.ToString("N"), attempt.Reference);

        attempt.MarkSent("int-1");
        attempt.ApplyResult(AttemptStatus.Approved, "SALE_APPROVED", "bold-1", Now);
        Assert.False(attempt.IsOpen);
        Assert.Throws<DomainException>(() => attempt.ApplyResult(AttemptStatus.Declined, "SALE_REJECTED", "bold-1", Now));
    }

    [Fact]
    public void Gateway_methods_are_limited_to_bold_values_and_cash_cannot_be_disabled()
    {
        Assert.Throws<DomainException>(() => PaymentMethod.Create(Company, "X", "X", PaymentMethodKind.Gateway, "CARD", false, 0));
        var cash = PaymentMethod.Create(Company, PaymentMethod.CashCode, "Efectivo", PaymentMethodKind.Cash, null, false, 0);
        Assert.Throws<DomainException>(() => cash.Update("Efectivo", false, 0, isActive: false));
        var transfer = PaymentMethod.Create(Company, "TRANSFERENCIA", "Transferencia", PaymentMethodKind.TransferManual, null, false, 1);
        Assert.True(transfer.RequiresReference);
    }
}
