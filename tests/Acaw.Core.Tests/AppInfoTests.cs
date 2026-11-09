using Acaw.Core;

namespace Acaw.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void La_version_vient_de_Directory_Build_props_sans_empreinte_de_commit()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Theory]
    [InlineData("0.4.0+8090b1e2f3", "0.4.0")]
    [InlineData("0.4.0", "0.4.0")]
    [InlineData("1.0.0-beta+abc", "1.0.0-beta")]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    public void L_empreinte_du_commit_est_retiree(string? raw, string expected)
    {
        Assert.Equal(expected, AppInfo.Clean(raw));
    }
}
