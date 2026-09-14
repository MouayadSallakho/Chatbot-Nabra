using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Knowledge;

/// <summary>What a knowledge source records, and what it refuses.</summary>
public sealed class KnowledgeSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_source_records_which_file_a_document_came_from()
    {
        KnowledgeSource source = Source("refund-policy.pdf", KnowledgeFileType.Pdf);

        Assert.Equal("refund-policy.pdf", source.FileName.Value);
        Assert.Equal(KnowledgeFileType.Pdf, source.FileType);
        Assert.Equal(Now, source.ExtractedAt);
    }

    [Fact]
    public void A_source_has_nowhere_to_keep_a_file()
    {
        // The storage decision, asserted structurally rather than trusted from a
        // comment: the upload is read during its request and discarded, and a
        // future change of mind has to add a property here and answer for it.
        Assert.DoesNotContain(
            typeof(KnowledgeSource).GetProperties(),
            property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Bytes", StringComparison.OrdinalIgnoreCase)
                || property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void A_source_must_belong_to_a_workspace_and_a_document()
    {
        Assert.Throws<DomainValidationException>(() => KnowledgeSource.Create(
            default,
            KnowledgeDocumentId.New(),
            SourceFileName.Create("a.txt"),
            KnowledgeFileType.Text,
            10,
            Fingerprint(1),
            Now));
    }

    [Fact]
    public void An_empty_file_is_not_a_source()
    {
        Assert.Throws<DomainValidationException>(() => Source("a.txt", KnowledgeFileType.Text, bytes: 0));
    }

    [Fact]
    public void Replacing_with_a_different_file_records_the_change()
    {
        KnowledgeSource source = Source("v1.csv", KnowledgeFileType.Csv);

        bool changed = source.Replace(
            SourceFileName.Create("v2.csv"),
            KnowledgeFileType.Csv,
            200,
            Fingerprint(2),
            Now.AddHours(1));

        Assert.True(changed);
        Assert.Equal("v2.csv", source.FileName.Value);
        Assert.Equal(200, source.ByteLength);
        Assert.Equal(Now.AddHours(1), source.ExtractedAt);
    }

    [Fact]
    public void Replacing_with_the_identical_file_changes_nothing_and_says_so()
    {
        // <strong>Re-uploading the same file is not an edit.</strong> Reporting a
        // change would move the document's version and invalidate every entity tag
        // an administrator's open screen is holding, for no reason anybody could
        // see.
        KnowledgeSource source = Source("plans.csv", KnowledgeFileType.Csv);

        bool changed = source.Replace(
            SourceFileName.Create("plans.csv"),
            KnowledgeFileType.Csv,
            100,
            Fingerprint(1),
            Now.AddHours(5));

        Assert.False(changed);
        Assert.Equal(Now, source.ExtractedAt);
        Assert.Equal(Now, source.UpdatedAt);
    }

    [Fact]
    public void Renaming_the_same_bytes_is_still_a_change()
    {
        // The name is what an administrator recognises the document by, so a new
        // one is worth recording even when the content is identical.
        KnowledgeSource source = Source("draft.csv", KnowledgeFileType.Csv);

        Assert.True(source.Replace(
            SourceFileName.Create("final.csv"),
            KnowledgeFileType.Csv,
            100,
            Fingerprint(1),
            Now.AddHours(1)));
    }

    private static KnowledgeSource Source(
        string fileName,
        KnowledgeFileType type,
        long bytes = 100) =>
        KnowledgeSource.Create(
            TenantId.New(),
            KnowledgeDocumentId.New(),
            SourceFileName.Create(fileName),
            type,
            bytes,
            Fingerprint(1),
            Now);

    private static ContentFingerprint Fingerprint(byte seed)
    {
        byte[] digest = new byte[ContentFingerprint.Length];

        Array.Fill(digest, seed);

        return ContentFingerprint.From(digest);
    }
}

/// <summary>What an uploaded file's name becomes before it is stored.</summary>
public sealed class SourceFileNameTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config", "config")]
    [InlineData("C:\\Users\\someone\\Desktop\\report.pdf", "report.pdf")]
    [InlineData("/var/tmp/notes.txt", "notes.txt")]
    [InlineData("folder/sub/policy.docx", "policy.docx")]
    public void A_path_is_reduced_to_its_last_segment(string sent, string stored)
    {
        // Traversal collapses into an ordinary name rather than being refused.
        // Nothing here ever becomes a path -- the bytes are read from the request
        // and discarded -- so this is about what is stored and displayed.
        Assert.Equal(stored, SourceFileName.Create(sent).Value);
    }

    [Fact]
    public void Control_characters_are_removed()
    {
        // This string is rendered in the admin and written to the database. A name
        // carrying a line break or a terminal escape is a name designed to be
        // rendered somewhere it should not be.
        // The escape character itself goes; the printable characters after it
        // stay, because they are ordinary text and removing them would start
        // rewriting names rather than sanitising them.
        Assert.Equal("report[31m.pdf", SourceFileName.Create("report\r\n\u001b[31m.pdf").Value);
    }

    [Fact]
    public void An_alternate_data_stream_suffix_is_neutralised()
    {
        Assert.Equal("notes.txt_stream", SourceFileName.Create("notes.txt:stream").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("...")]
    public void A_name_with_nothing_usable_in_it_is_refused(string? sent)
    {
        Assert.Throws<DomainValidationException>(() => SourceFileName.Create(sent));
    }

    [Fact]
    public void A_name_longer_than_the_bound_is_refused()
    {
        Assert.Throws<DomainValidationException>(
            () => SourceFileName.Create(new string('a', SourceFileName.MaxLength + 1)));
    }

    [Theory]
    [InlineData("Refund policy.pdf", "Refund policy")]
    [InlineData("tariffs.2026.csv", "tariffs.2026")]
    [InlineData("README", "README")]
    public void A_title_can_be_derived_from_the_name(string fileName, string title)
    {
        Assert.Equal(title, SourceFileName.Create(fileName).WithoutExtension());
    }
}
