using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Invoicing;

namespace Tahona.BakeryPOS.UnitTests.Invoicing;

public class FiscalDocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static FiscalDocument NewDoc() =>
        FiscalDocument.Create(Guid.NewGuid(), Guid.NewGuid(), FiscalDocumentKind.Invoice, "{}", 12_000m, FiscalSettings.DefaultConsumerFinal);

    [Fact]
    public void Idempotency_key_is_25_alphanumeric_chars_unique_per_document()
    {
        var a = NewDoc();
        var b = NewDoc();
        Assert.Equal(25, a.IdempotencyKey.Length);
        Assert.Matches("^[0-9a-z]+$", a.IdempotencyKey);
        Assert.NotEqual(a.IdempotencyKey, b.IdempotencyKey);
        Assert.Equal(FiscalDocument.Base36(a.Id), a.IdempotencyKey);
    }

    [Fact]
    public void Accepted_requires_cufe_and_sets_retention()
    {
        var doc = NewDoc();
        doc.StartSubmission();
        Assert.Throws<DomainException>(() => doc.Accept("", Now, 5));

        doc.Accept("cufe-123", Now, 5);
        Assert.Equal(FiscalDocumentStatus.Accepted, doc.Status);
        Assert.Equal(Now.AddYears(5), doc.RetainUntil);
        Assert.Throws<DomainException>(() => doc.StartSubmission());
    }

    [Fact]
    public void Rejected_is_not_resent_until_retried()
    {
        var doc = NewDoc();
        doc.StartSubmission();
        doc.Reject("Error DIAN");
        Assert.Throws<DomainException>(() => doc.StartSubmission());

        doc.Retry();
        Assert.Equal(FiscalDocumentStatus.Pending, doc.Status);
        doc.StartSubmission();
        Assert.Equal(2, doc.Attempts);
    }

    [Fact]
    public void Unknown_keeps_the_same_key_for_the_next_attempt()
    {
        var doc = NewDoc();
        var key = doc.IdempotencyKey;
        doc.StartSubmission();
        doc.MarkUnknown("timeout");
        doc.StartSubmission();
        Assert.Equal(key, doc.IdempotencyKey);
    }

    [Fact]
    public void Settings_cannot_activate_while_incomplete()
    {
        var settings = FiscalSettings.Create(Guid.NewGuid());
        Assert.Throws<DomainException>(() => settings.SetActive(true));
        settings.SetCredentials("u", "k");
        settings.Configure(1, null, 2, 3, FiscalSettings.DefaultConsumerFinal, 5);
        settings.SetActive(true);
        Assert.True(settings.IsActive);
    }
}
