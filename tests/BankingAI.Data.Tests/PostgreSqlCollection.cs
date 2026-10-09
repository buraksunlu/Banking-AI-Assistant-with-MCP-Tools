using Xunit;

namespace BankingAI.Data.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection
{
    public const string Name = "PostgreSQL integration tests";
}
