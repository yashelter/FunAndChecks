using FluentAssertions;
using FunAndChecks.Application.Attachments;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AttachmentContentTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "a.png", "image/png", true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "photo.JPG", "image/jpeg", true)]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "report.pdf", "application/pdf", false)]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0 }, "lab.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    public void Detect_UsesSignature_NotDeclaredType(byte[] data, string name, string expected, bool image)
    {
        AttachmentContent.Detect(data, name).Should().Be(new DetectedContent(expected, image));
    }

    [Fact]
    public void Detect_AllowsUtf8SourceCode_ButRejectsHtmlAndBinaryText()
    {
        AttachmentContent.Detect("int main() { return 0; } // привет"u8, "main.cpp")!.ContentType.Should().StartWith("text/plain");
        AttachmentContent.Detect("<html><script>alert(1)</script>"u8, "page.html").Should().BeNull();
        AttachmentContent.Detect("<svg onload=alert(1)>"u8, "image.png").Should().BeNull();
        AttachmentContent.Detect(new byte[] { 0x41, 0, 0x42 }, "notes.txt").Should().BeNull();
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\temp\lab""1"".zip", "lab1.zip")]
    [InlineData("", "file")]
    public void CleanFileName_DropsPathsAndQuotes(string input, string expected) =>
        AttachmentContent.CleanFileName(input).Should().Be(expected);
}
