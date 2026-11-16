using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class AuthoringPipelineTests
{
    [Fact]
    public void Publisher_requires_field_binding_ports()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        services.AddHarborlineFormsAuthoringPublisher();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>());
    }
}
