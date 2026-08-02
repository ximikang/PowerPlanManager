using Microsoft.Windows.ApplicationModel.Resources;

namespace PowerManager.App.Services;

public sealed class StringLocalizer : IStringLocalizer
{
    private readonly ResourceLoader _loader = new();

    public string Get(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }
}
