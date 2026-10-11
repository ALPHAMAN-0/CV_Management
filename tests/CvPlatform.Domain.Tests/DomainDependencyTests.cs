using CvPlatform.Domain;

namespace CvPlatform.Domain.Tests;

public sealed class DomainDependencyTests
{
    // Domain holds pure logic only; a framework reference here would leak infrastructure into it.
    // netstandard is a runtime facade that the host loads alongside every .NET assembly — it is
    // not a real package reference, so we filter it explicitly to keep the assertion deterministic.
    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var referenced = typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("netstandard", StringComparison.Ordinal));

        Assert.Empty(referenced);
    }
}
