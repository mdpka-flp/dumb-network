using DumbNetwork.Services;

namespace DumbNetwork.Tests;

public class BackendLayoutTests
{
    [Fact]
    public void Bundled_backend_is_discoverable()
    {
        var layout = BackendLayout.Locate();

        Assert.True(File.Exists(layout.ExecutablePath), layout.ExecutablePath);
        Assert.True(Directory.Exists(layout.FakeDirectory), layout.FakeDirectory);
        Assert.StartsWith("v", layout.Version, StringComparison.Ordinal);
        Assert.Contains(layout.RuntimeIdentifier, layout.DisplayName, StringComparison.Ordinal);
    }
}
