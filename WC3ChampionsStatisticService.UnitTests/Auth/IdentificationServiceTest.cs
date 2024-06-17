using System;
using System.Net.Http;
using Moq;
using NUnit.Framework;
using W3C.Domain.IdentificationService;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class IdentificationServiceTest
{
    [Test]
    public void TestTokenCache()
    {
        Environment.SetEnvironmentVariable("WEBSITE_BACKEND_TO_ID_SERVICE_SECRET", "e72dbdaa8b0d89d8fa5eaa2620f31e75186081a555ea14df9202ad6a9f180653");
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(_ => _.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var client = new IdentificationServiceClient(mockFactory.Object);
        var token = client.GetToken();
        Assert.AreEqual(token, client.GetToken());
    }
    
    [Test]
    public void TestTokenNew()
    {
        Environment.SetEnvironmentVariable("WEBSITE_BACKEND_TO_ID_SERVICE_SECRET", "e72dbdaa8b0d89d8fa5eaa2620f31e75186081a555ea14df9202ad6a9f180653");
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(_ => _.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var client = new IdentificationServiceClient(mockFactory.Object, 0.001);
        
        var token = client.GetToken();
        var token2 = client.GetToken();
        
        Assert.AreNotEqual(token, token2);
    }
}
