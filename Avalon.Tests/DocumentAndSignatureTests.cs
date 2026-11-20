using System;
using Avalon.Core;
using Xunit;

namespace Avalon.Tests;

/// <summary>
/// Дополнительные тесты: маска номера документа PR23-KTZ-{Год}-{Номер:D5},
/// подпись SHA256 и хэш пароля.
/// </summary>
public class DocumentAndSignatureTests
{
    [Fact]
    public void DocumentNumber_FormatAndParse()
    {
        string doc = DocumentNumber.Format(2026, 42);
        Assert.Equal("PR23-KTZ-2026-00042", doc);

        Assert.True(DocumentNumber.TryParse(doc, out int year, out int seq));
        Assert.Equal(2026, year);
        Assert.Equal(42, seq);
    }

    [Fact]
    public void DocumentNumber_RejectsWrongMask()
    {
        Assert.False(DocumentNumber.IsValid("PR23-КТЗ-2026-00042")); // кириллица вместо KTZ
        Assert.False(DocumentNumber.IsValid("PR23-KTZ-2026-42"));    // не D5
        Assert.False(DocumentNumber.IsValid(""));
        Assert.False(DocumentNumber.IsValid(null));
    }

    [Fact]
    public void Signature_IsDeterministicAndVerifiable()
    {
        var date = new DateTime(2026, 11, 20, 12, 0, 0);
        string sig1 = TicketSignature.Compute("PR23-KTZ-2026-00042", date, "Иванов Иван", 7150m, 3);
        string sig2 = TicketSignature.Compute("PR23-KTZ-2026-00042", date, "Иванов Иван", 7150m, 3);

        Assert.Equal(sig1, sig2);
        Assert.Equal(64, sig1.Length); // SHA256 hex
        Assert.True(TicketSignature.Verify("PR23-KTZ-2026-00042", date, "Иванов Иван", 7150m, 3, sig1));
        Assert.False(TicketSignature.Verify("PR23-KTZ-2026-00042", date, "Иванов Иван", 9999m, 3, sig1));
    }

    [Fact]
    public void PasswordHasher_HashAndVerify()
    {
        string hash = PasswordHasher.Hash("123456");
        Assert.Equal("8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92", hash);
        Assert.True(PasswordHasher.Verify("123456", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
        // Обратная совместимость со старой БД (открытый пароль).
        Assert.True(PasswordHasher.Verify("123456", "123456"));
    }
}
