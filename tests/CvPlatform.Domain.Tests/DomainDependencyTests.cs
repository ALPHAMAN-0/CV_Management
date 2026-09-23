using CvPlatform.Domain;

namespace CvPlatform.Domain.Tests;

public sealed class DomainDependencyTests
{
    // Domain holds pure logic only; a framework reference here would leak infrastructure into it.
    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var referenced = typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal));

        Assert.Empty(referenced);
    }
}
