using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public sealed class TcmbArchiveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tcmb-archive-" + Guid.NewGuid().ToString("N"));

    public TcmbArchiveTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void File_name_is_the_iso_day() =>
        Assert.Equal("2025-10-10.xml", TcmbArchive.FileName(new DateOnly(2025, 10, 10)));

    [Fact]
    public void Weekdays_skip_saturday_and_sunday_and_include_both_ends()
    {
        // Fri 10 Oct 2025 → Mon 13 Oct 2025: the weekend in between is not a TCMB publication day.
        Assert.Equal(
            [new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 13)],
            TcmbArchive.Weekdays(new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 13)));
        Assert.Empty(TcmbArchive.Weekdays(new DateOnly(2025, 10, 11), new DateOnly(2025, 10, 12)));
    }

    [Fact]
    public void A_reversed_range_is_refused() =>
        Assert.Throws<ArgumentException>(() => TcmbArchive.Weekdays(new DateOnly(2025, 10, 13), new DateOnly(2025, 10, 10)));

    [Fact]
    public void Read_reports_missing_and_rejected_days_and_ignores_weekend_files()
    {
        Save(new DateOnly(2025, 10, 10), TcmbFixture.Bulletin());
        Save(new DateOnly(2025, 10, 11), TcmbFixture.Bulletin(tarih: "11.10.2025"));   // Saturday: never read
        Save(new DateOnly(2025, 10, 14), "<html>hata</html>");
        // 13 Oct has no file.

        var read = TcmbArchive.Read(_directory, new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 14));

        Assert.Equal(2, read.Rows.Count);
        Assert.All(read.Rows, r => Assert.Equal(new DateOnly(2025, 10, 10), r.PriceDate));
        Assert.Equal([new DateOnly(2025, 10, 13)], read.MissingDays);
        var rejected = Assert.Single(read.RejectedDays);
        Assert.Equal(new DateOnly(2025, 10, 14), rejected.Day);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
    }

    [Fact]
    public void A_file_holding_another_days_bulletin_is_rejected_for_its_own_day()
    {
        Save(new DateOnly(2025, 10, 13), TcmbFixture.Bulletin());   // content says 10.10.2025

        var read = TcmbArchive.Read(_directory, new DateOnly(2025, 10, 13), new DateOnly(2025, 10, 13));

        Assert.Empty(read.Rows);
        Assert.Equal(new DateOnly(2025, 10, 13), Assert.Single(read.RejectedDays).Day);
    }

    private void Save(DateOnly day, string content) =>
        File.WriteAllText(Path.Combine(_directory, TcmbArchive.FileName(day)), content);
}
