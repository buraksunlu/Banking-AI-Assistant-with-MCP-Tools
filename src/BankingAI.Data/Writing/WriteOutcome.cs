namespace BankingAI.Data.Writing;

/// <summary>
/// What a writer did with a transaction: wrote it now, or found the same event already written (a retry) and
/// changed nothing, so no balance moved a second time.
/// </summary>
public enum WriteOutcome
{
    Created = 1,
    AlreadyExisted = 2
}
