/// <summary>Detección de placeholders de secretos enmascarados en la UI.</summary>
static class RunnerSecrets
{
    public static bool EsMascara(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return false;
        var v = valor.Trim();
        return v is "***" or "****" or "********" or "[SECRETO]" or "[SECRET]" or "[OCULTO]" or "[MASKED]";
    }
}
