using CvPlatform.Web.Infrastructure;

namespace CvPlatform.Web.Tests;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Uri_is_converted_to_key_value_form()
    {
        var result = PostgresConnectionString.Normalize("postgresql://cv:p%40ss@db.example.com/cvplatform");

        Assert.Equal("Host=db.example.com;Port=5432;Database=cvplatform;Username=cv;Password=p@ss", result);
    }

    [Fact]
    public void Key_value_form_is_left_untouched()
    {
        const string value = "Host=localhost;Database=cvplatform";

        Assert.Equal(value, PostgresConnectionString.Normalize(value));
    }
}
