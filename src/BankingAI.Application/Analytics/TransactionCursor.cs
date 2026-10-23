using System.Buffers.Binary;
using System.Buffers.Text;

namespace BankingAI.Application.Analytics;

/// <summary>
/// The position after the last row of a page in the newest-first order (API-17, decision K4-A): that row's moment
/// and id, which together are unique, so a page edge between two rows of the same moment loses neither.
/// </summary>
/// <remarks>
/// Opaque to the client: a version byte, the UTC ticks and the id, in base64url. It carries no customer and no
/// filter; every page is still scoped and filtered by the query, so a forged cursor only moves within the
/// customer's own rows.
/// </remarks>
public sealed record TransactionCursor(DateTimeOffset OccurredAtUtc, Guid Id)
{
    private const byte Version = 1;
    private const int Length = 1 + sizeof(long) + 16;

    /// <summary>The cursor that continues after <paramref name="item"/>.</summary>
    public static string After(TransactionItem item)
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..], item.OccurredAtUtc.UtcTicks);
        item.Id.TryWriteBytes(bytes[(1 + sizeof(long))..]);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>Reads a cursor written by <see cref="After"/>.</summary>
    /// <exception cref="ArgumentException">The text is not a cursor of this version.</exception>
    public static TransactionCursor Parse(string cursor, string field)
    {
        Span<byte> bytes = stackalloc byte[Length + 2];
        if (!Base64Url.TryDecodeFromChars(cursor, bytes, out var written) || written != Length || bytes[0] != Version)
            throw Invalid(field);

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes[1..]);
        var id = new Guid(bytes[(1 + sizeof(long))..Length]);
        if (ticks is < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks || id == Guid.Empty)
            throw Invalid(field);
        return new TransactionCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    private static ArgumentException Invalid(string field) => new("Geçersiz sayfa imleci.", field);
}
