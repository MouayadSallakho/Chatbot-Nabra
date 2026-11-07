using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Writes every report type, in every format and both languages, to a folder for
/// a person to look at (Phase 37.7 evidence).
/// </summary>
/// <remarks>
/// Does nothing unless <c>OMNIAI_REPORT_DUMP_DIR</c> names a folder, so an
/// ordinary test run writes no file anywhere.
/// </remarks>
public sealed class ReportFileDump
{
    [Fact]
    public async Task Writes_every_file_when_asked()
    {
        string? folder = Environment.GetEnvironmentVariable("OMNIAI_REPORT_DUMP_DIR");

        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        foreach (UserLanguage language in Enum.GetValues<UserLanguage>())
        {
            string code = UserLanguages.Code(language);
            Directory.CreateDirectory(Path.Combine(folder, code));

            foreach (ReportType type in Enum.GetValues<ReportType>())
            {
                ReportDocument document = await ReportFixtures.BuildAsync(type, language: language);

                foreach (ReportFormat format in Enum.GetValues<ReportFormat>())
                {
                    byte[] bytes = await ReportRenderingTests.Render(ReportFixtures.Renderer(format), document);

                    await File.WriteAllBytesAsync(
                        Path.Combine(folder, code, $"{ReportCatalog.For(type).Slug}.{ReportCatalog.Extension(format)}"),
                        bytes);
                }
            }
        }
    }
}
