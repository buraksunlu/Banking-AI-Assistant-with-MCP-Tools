using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Resolves a category code given by a caller (in the end, a model argument) to a defined category. Shared by every
/// service that filters by category, so "no such category" is always an error and never an empty result.
/// </summary>
internal static class CategoryLookup
{
    /// <summary>A defined category: its key, exact code and Turkish display name.</summary>
    public sealed record Category(short Id, string Code, string DisplayName);

    /// <summary>The category with exactly this code.</summary>
    /// <exception cref="ArgumentException">No category has this code; the field is <c>CategoryCode</c>.</exception>
    public static async Task<Category> FindAsync(BankingDbContext db, string code, CancellationToken cancellationToken) =>
        await db.TransactionCategories.AsNoTracking()
            .Where(c => c.Code == code)
            .Select(c => new Category(c.Id, c.Code, c.DisplayName))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new ArgumentException("Tanımlı bir kategori kodu olmalı.", nameof(TransactionSearchQuery.CategoryCode));
}
