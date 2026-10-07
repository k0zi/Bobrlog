using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace Bobrlog.Core.Tests;

/// <summary>Runs a test with a fixed UI culture (localized strings would otherwise follow the machine's LANG).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class UseCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    private CultureInfo? _originalCulture;
    private CultureInfo? _originalUiCulture;

    public override void Before(MethodInfo methodUnderTest)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    }

    public override void After(MethodInfo methodUnderTest)
    {
        CultureInfo.CurrentCulture = _originalCulture!;
        CultureInfo.CurrentUICulture = _originalUiCulture!;
    }
}
