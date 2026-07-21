namespace IntegrationTests;

// This class has no code, and is never created. Its purpose is simply to be the place to apply
// [CollectionDefinition] so every [Collection("scenarios")] test class shares one AppFixture
// (one Alba host, one Postgres connection) instead of booting the app per test class.
[CollectionDefinition("scenarios")]
public class ScenariosCollection : ICollectionFixture<AppFixture>;
