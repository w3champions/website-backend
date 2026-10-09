using System;
using NUnit.Framework;
using W3C.Domain;

/// <summary>
/// Assembly-wide fixture (global namespace, so NUnit applies it to every test): the clients have no
/// fallback for ADMIN_SECRET, so tests that assert on the outgoing header need it set explicitly.
/// </summary>
[SetUpFixture]
public class AdminSecretTestEnvironment
{
    public const string Secret = "unit-test-admin-secret";

    private string _previous;

    [OneTimeSetUp]
    public void SetAdminSecret()
    {
        _previous = Environment.GetEnvironmentVariable(AdminSecretProvider.VariableName);
        Environment.SetEnvironmentVariable(AdminSecretProvider.VariableName, Secret);
    }

    [OneTimeTearDown]
    public void RestoreAdminSecret()
    {
        Environment.SetEnvironmentVariable(AdminSecretProvider.VariableName, _previous);
    }
}
