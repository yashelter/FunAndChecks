using Xunit;

namespace FunAndChecks.Tests.Integration;

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<TestWebAppFactory>
{
}
