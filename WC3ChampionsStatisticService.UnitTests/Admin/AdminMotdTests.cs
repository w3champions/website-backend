using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminMotdTests
{
    [TestCase(401)]
    [TestCase(2000)]
    public async Task SetMotd_AcceptsMessagesUpTo2000Characters(int length)
    {
        var message = new MessageOfTheDay { motd = new string('a', length) };
        var repository = new Mock<IInformationMessagesRepository>();
        repository.Setup(r => r.SetMotd(message)).ReturnsAsync(HttpStatusCode.OK);
        var controller = new AdminController(null, null, null, repository.Object, null, null, null);

        var result = await controller.SetMotd(message);

        Assert.That(result, Is.InstanceOf<OkResult>());
        repository.Verify(r => r.SetMotd(message), Times.Once);
    }

    [Test]
    public async Task SetMotd_RejectsMessagesOver2000Characters()
    {
        var repository = new Mock<IInformationMessagesRepository>();
        var controller = new AdminController(null, null, null, repository.Object, null, null, null);

        var result = await controller.SetMotd(new MessageOfTheDay { motd = new string('a', 2001) });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        repository.Verify(r => r.SetMotd(It.IsAny<MessageOfTheDay>()), Times.Never);
    }
}
