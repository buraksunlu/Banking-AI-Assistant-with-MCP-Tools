using Xunit;

namespace BankingAI.Application.Tests;

// API-12. Pure: the id a key stands for. No database.
public sealed class IdempotentIdTests
{
    private static readonly Guid Customer = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCustomer = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public void The_same_customer_resource_and_key_always_give_the_same_id()
    {
        Assert.Equal(IdempotentId.For(IdempotentId.Transaction, Customer, "7c2f-a91e"),
            IdempotentId.For(IdempotentId.Transaction, Customer, "7c2f-a91e"));
    }

    [Fact]
    public void A_different_customer_resource_or_key_gives_a_different_id()
    {
        var id = IdempotentId.For(IdempotentId.Transaction, Customer, "key-1");

        Assert.NotEqual(id, IdempotentId.For(IdempotentId.Transaction, OtherCustomer, "key-1"));
        Assert.NotEqual(id, IdempotentId.For(IdempotentId.Account, Customer, "key-1"));
        Assert.NotEqual(id, IdempotentId.For(IdempotentId.Transaction, Customer, "key-2"));
        Assert.NotEqual(id, IdempotentId.For(IdempotentId.Transaction, Customer, "KEY-1")); // keys are exact
    }

    [Fact]
    public void Ids_are_marked_as_derived_uuids()
    {
        // RFC 9562 version 8 ("custom") and the RFC variant, so they never pass for a random v4 GUID.
        var id = IdempotentId.For(IdempotentId.Transaction, Customer, "key-1").ToString();

        Assert.Equal('8', id[14]);
        Assert.Contains(id[19], "89ab");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("iki kelime")]   // spaces are not allowed
    [InlineData("anahtar\t1")]
    [InlineData("şifre")]         // visible ASCII only
    public void Keys_must_be_visible_ascii(string key)
    {
        var error = Assert.Throws<ArgumentException>(() => IdempotentId.For(IdempotentId.Transaction, Customer, key));
        Assert.Equal("idempotencyKey", error.ParamName);
    }

    [Fact]
    public void Keys_are_at_most_100_characters()
    {
        IdempotentId.For(IdempotentId.Transaction, Customer, new string('a', 100));
        Assert.Throws<ArgumentException>(() => IdempotentId.For(IdempotentId.Transaction, Customer, new string('a', 101)));
    }
}
