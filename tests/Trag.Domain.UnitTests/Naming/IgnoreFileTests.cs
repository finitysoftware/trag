using System.Text;
using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

// A line added to a .gitignore as it is: the bytes there stay, the line goes after them on a line of its own, ended as
// the file ends its lines, and a line the file already has is not written again, a byte order mark before it aside.
public sealed class IgnoreFileTests
{
    private static string? Addition(string existing) => IgnoreFile.Addition(Encoding.UTF8.GetBytes(existing), "/out.log");

    [Theory]
    [InlineData("", "/out.log\n")]
    [InlineData("*.tmp\n", "/out.log\n")]
    [InlineData("*.tmp\r\nbin/", "\r\n/out.log\r\n")]
    [InlineData("*.tmp", "\n/out.log\n")]
    public void WhenALineIsAdded_ShouldGoOnALineOfItsOwnEndedAsTheFileEndsItsLines(string existing, string addition) =>
        Assert.Equal(addition, Addition(existing));

    [Theory]
    [InlineData("/out.log\n")]
    [InlineData("*.tmp\r\n/out.log\r\n")]
    [InlineData("﻿/out.log\n")]
    public void WhenTheFileHasTheLineAlready_ShouldAddNothing(string existing) => Assert.Null(Addition(existing));
}
