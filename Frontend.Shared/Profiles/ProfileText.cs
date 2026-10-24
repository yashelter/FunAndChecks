using System.Globalization;
namespace Frontend.Shared.Profiles;
public static class ProfileText
{
    public static string T(string ru, string en) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en;
    public static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0]));
}
