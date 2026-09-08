// ProtocolVersionTests.cs
//
// What this file does: it fails the build when the code's idea of the message
// contract version drifts away from the document's.
//
// Why this is worth a test at all: docs/protocol.md is the single source of truth,
// but nothing enforces a document. Somebody edits the contract, forgets the constant,
// and from then on the repository contains two different answers to "which version
// are we speaking". This test reads the document from disk at test time, so the
// answer cannot be stale.
//
// This is also the smallest possible proof that the test project is wired up and
// that the runner in scripts/test.sh actually runs something.

using System.Text.RegularExpressions;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class ProtocolVersionTests
{
    [Fact]
    public void CodeVersionMatchesProtocolDocument()
    {
        var repoRoot = FindRepositoryRoot();
        var doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "protocol.md"));

        // The document declares its version on a line like: "Sürüm: `ATMSIM/0.1`"
        var match = Regex.Match(doc, @"S[uü]r[uü]m:\s*`([^`]+)`");

        Assert.True(match.Success, "docs/protocol.md içinde 'Sürüm: `...`' satırı bulunamadı.");
        Assert.Equal(match.Groups[1].Value, ProtocolVersion.Current);
    }

    /// <summary>
    /// Walks upward from the test binary until it finds the folder holding ATMSIM.slnx.
    /// Tests must not hard-code an absolute path: the repository sits in a different
    /// place on the Mac than it does in the build container.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ATMSIM.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
