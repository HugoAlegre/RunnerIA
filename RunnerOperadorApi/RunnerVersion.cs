/// <summary>Versión del Runner (API): número, canal beta y build.</summary>
public static class RunnerVersion
{
    public const string Version = "0.0.10";
    public const string Channel = "beta";
    public const string Build = "20260910";

    public static string Label => $"{Version} {Channel} build {Build}";
}
