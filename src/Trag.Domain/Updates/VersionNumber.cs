namespace Trag.Domain.Updates;

// The build stamps its version with "+" and the commit it was built from; the version is what comes before it.
public static class VersionNumber
{
    public static string Of(string informationalVersion) => informationalVersion.Split('+')[0];
}
