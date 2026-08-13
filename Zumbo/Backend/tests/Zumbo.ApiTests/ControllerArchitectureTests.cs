using Microsoft.AspNetCore.Mvc;

namespace Zumbo.ApiTests;

public sealed class ControllerArchitectureTests
{
    [Fact]
    public void ConcreteApiControllers_FollowPresentationIntentRules()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.All(controllers, controller =>
        {
            Assert.EndsWith("Controller", controller.Name, StringComparison.Ordinal);
            Assert.NotNull(controller.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).SingleOrDefault());
        });
    }
}
