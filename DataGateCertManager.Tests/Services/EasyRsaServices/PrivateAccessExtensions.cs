using System.Reflection;

namespace DataGateCertManager.Tests.Services.EasyRsaServices;

public static class PrivateAccessExtensions
{
    public static T Invoke<T>(this object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new MissingMethodException($"{target.GetType().Name} does not contain private method {methodName}");
        return (T)method.Invoke(target, args)!;
    }

    public static async Task<T> InvokeAsync<T>(this object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new MissingMethodException($"{target.GetType().Name} does not contain private async method {methodName}");
        var task = (Task<T>)method.Invoke(target, args)!;
        return await task;
    }
}