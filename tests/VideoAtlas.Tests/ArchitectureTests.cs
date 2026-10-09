using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using VideoAtlas.Web;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace VideoAtlas.Tests;

public sealed class ArchitectureTests
{
    [Fact] public void DomainAndApplicationHaveOnlyInwardDependencies()
    {
        var architecture = new ArchLoader().LoadAssemblies(typeof(Video).Assembly, typeof(VideoChat).Assembly,
            typeof(AppDbContext).Assembly, typeof(HomeController).Assembly).Build();
        Types().That().ResideInNamespace("VideoAtlas.Domain").Should().NotDependOnAny(
            Types().That().ResideInNamespace("VideoAtlas.Application").Or().ResideInNamespace("VideoAtlas.Infrastructure").Or().ResideInNamespace("VideoAtlas.Web")).Check(architecture);
        Types().That().ResideInNamespace("VideoAtlas.Application").Should().NotDependOnAny(
            Types().That().ResideInNamespace("VideoAtlas.Infrastructure").Or().ResideInNamespace("VideoAtlas.Web")).Check(architecture);
        Assert.DoesNotContain(typeof(Video).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.", StringComparison.Ordinal));
    }
    [Fact] public void EveryControllerActionExplicitlyDeclaresAuthorization()
    {
        var controllers = typeof(HomeController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t));
        var count = 0;
        foreach (var type in controllers)
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            count++;
            Assert.True(method.IsDefined(typeof(AuthorizeAttribute)) || method.IsDefined(typeof(AllowAnonymousAttribute)) ||
                type.IsDefined(typeof(AuthorizeAttribute)) || type.IsDefined(typeof(AllowAnonymousAttribute)), $"{type.Name}.{method.Name} lacks authorization metadata");
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(Video));
        }
        Assert.True(count >= 20);
    }
}
