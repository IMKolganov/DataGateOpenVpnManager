using System.Reflection;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DataGateOpenVpnManager.Tests.Security;

public sealed class ApiContractPackageTests
{
    [Fact]
    public void ControllerRequestAndResponseContracts_ComeFromSharedModelsPackage()
    {
        var assembly = typeof(DataGateOpenVpnManager.Controllers.ServiceClientsController).Assembly;
        var shared = typeof(ApiResponse<>).Assembly;
        var inspected = 0;
        foreach (var controller in assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!method.GetCustomAttributes<HttpMethodAttribute>().Any())
                continue;
            inspected++;
            foreach (var parameter in method.GetParameters().Where(p => p.GetCustomAttribute<FromServicesAttribute>() is null))
                Check(parameter.ParameterType, controller.Name + "." + method.Name);
            Check(method.ReturnType, controller.Name + "." + method.Name);
            foreach (var response in method.GetCustomAttributes<ProducesResponseTypeAttribute>())
                Check(response.Type, controller.Name + "." + method.Name);
        }
        Assert.True(inspected > 20);

        void Check(Type type, string action)
        {
            if (type.IsArray)
            {
                Check(type.GetElementType()!, action);
                return;
            }
            foreach (var argument in type.GetGenericArguments())
                Check(argument, action);
            if (type.Namespace?.StartsWith("DataGate", StringComparison.Ordinal) == true)
                Assert.True(type.Assembly == shared, $"{action}: {type.FullName} must come from the published SharedModels package.");
        }
    }
}
