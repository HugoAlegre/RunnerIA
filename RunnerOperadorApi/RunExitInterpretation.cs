using System.Text.RegularExpressions;

/// <summary>
/// <c>dotnet test</c> / SpecFlow: 0 = todo OK, 1 = escenarios fallidos (corrida válida),
/// otros códigos = error de build, host o script.
/// Exit 1 SIN resumen de tests (ParserError de PowerShell) se trata como error de proceso,
/// no como falla de escenario (evita falso positivo).
/// </summary>
internal static class RunExitInterpretation
{
    private static readonly Regex RxFailedPassed = new(
        @"Failed:\s*(\d+)\D+Passed:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxTotalFailed = new(
        @"Total tests:\s*(\d+).*?Failed:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex RxTotalTestsZero = new(
        @"Total tests:\s*0\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxNoTestMatches = new(
        @"No test matches",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxPassedSkipped = new(
        @"Passed:\s*(\d+)\D+Skipped:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxTotalPassedSkipped = new(
        @"Total tests:\s*(\d+).*?Passed:\s*(\d+).*?Skipped:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex RxScriptError = new(
        @"ParserError|TerminatorExpectedAtEndOfString|Falta la cadena en el terminador|is not recognized as|no se reconoce como nombre de un cmdlet|FullyQualifiedErrorId",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool LooksLikeScriptError(IReadOnlyList<string> logLines)
    {
        foreach (var line in logLines)
        {
            if (RxScriptError.IsMatch(line))
                return true;
        }
        return false;
    }

    public static bool HasDotnetTestSummary(IReadOnlyList<string> logLines)
    {
        foreach (var line in logLines)
        {
            if (RxFailedPassed.IsMatch(line)
                || RxTotalFailed.IsMatch(line)
                || RxTotalTestsZero.IsMatch(line)
                || RxNoTestMatches.IsMatch(line)
                || RxTotalPassedSkipped.IsMatch(line))
                return true;
        }
        return false;
    }

    public static bool IsTestFailureExitCode(int? exitCode, IReadOnlyList<string>? logLines = null)
    {
        if (exitCode != 1) return false;
        if (logLines is { Count: > 0 })
        {
            if (LooksLikeScriptError(logLines)) return false;
            if (!HasDotnetTestSummary(logLines)) return false;
        }
        return true;
    }

    public static bool IsProcessErrorExitCode(int? exitCode, IReadOnlyList<string>? logLines = null)
    {
        if (exitCode is not int c) return false;
        if (c != 0 && c != 1) return true;
        if (c == 1 && logLines is { Count: > 0 })
        {
            if (LooksLikeScriptError(logLines)) return true;
            if (!HasDotnetTestSummary(logLines)) return true;
        }
        return false;
    }

    public static bool IsRunOk(int? exitCode, bool corridaVacia = false) =>
        exitCode == 0 && !corridaVacia;

    /// <summary>
    /// Exit 0 pero sin tests ejecutados (filtro vacío, solo skipped, o script sin features).
    /// </summary>
    public static bool IsEmptyRun(IReadOnlyList<string> logLines, out string? reason)
    {
        reason = null;
        if (logLines.Count == 0) return false;

        foreach (var line in logLines)
        {
            if (RxNoTestMatches.IsMatch(line))
            {
                reason = "No se ejecuto ningun escenario — el filtro no coincide con ningun test (revisar tag/Category).";
                return true;
            }

            if (RxTotalTestsZero.IsMatch(line))
            {
                reason = "No se ejecuto ningun escenario — Total tests: 0 (revisar filtro/tag).";
                return true;
            }

            if (Regex.IsMatch(line, @"No hay escenarios generados", RegexOptions.IgnoreCase))
            {
                reason = "No hay escenarios en Features/_pruebas para este lote (revisar @Prueba / Release 9).";
                return true;
            }
        }

        for (var i = logLines.Count - 1; i >= 0; i--)
        {
            var line = logLines[i];

            var mAllSkipped = RxTotalPassedSkipped.Match(line);
            if (mAllSkipped.Success)
            {
                var total = int.Parse(mAllSkipped.Groups[1].Value);
                var passed = int.Parse(mAllSkipped.Groups[2].Value);
                var skipped = int.Parse(mAllSkipped.Groups[3].Value);
                if (total > 0 && passed == 0 && skipped == total)
                {
                    reason =
                        $"Todos los escenarios quedaron Omitidos ({skipped}) — revisar login UTF-8, stubs o prep pendiente.";
                    return true;
                }
            }

            var mPs = RxPassedSkipped.Match(line);
            if (mPs.Success)
            {
                var passed = int.Parse(mPs.Groups[1].Value);
                var skipped = int.Parse(mPs.Groups[2].Value);
                if (passed == 0 && skipped > 0)
                {
                    var mTotal = RxTotalFailed.Match(line);
                    if (!mTotal.Success || int.Parse(mTotal.Groups[1].Value) == skipped)
                    {
                        reason =
                            $"Sin pruebas ejecutadas — {skipped} Omitido(s). Revisar filtro, tags o Assert.Ignore.";
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static string? InterpretError(
        int exitCode,
        string? evidenciaCarpeta,
        string? evidenciaInforme,
        IReadOnlyList<string> logLines)
    {
        if (exitCode == 0) return null;

        if (LooksLikeScriptError(logLines))
            return "Error del script PowerShell (parseo/encoding). El lote no arranco — revisar comillas y caracteres especiales en el .ps1.";

        if (exitCode == 1 && !HasDotnetTestSummary(logLines))
            return $"Error del proceso (codigo {exitCode}, sin resumen de tests). El script no llego a dotnet test.";

        if (exitCode == 1)
        {
            var summary = ExtractTestSummary(logLines);
            var tieneEvidencia =
                !string.IsNullOrWhiteSpace(evidenciaCarpeta) ||
                !string.IsNullOrWhiteSpace(evidenciaInforme);

            if (tieneEvidencia)
                return summary ?? "Escenarios con fallas (ver informe).";

            return summary ?? "La corrida termino con fallas (sin evidencia generada).";
        }

        return $"Error del proceso (codigo {exitCode}).";
    }

    public static string? ExtractTestSummary(IReadOnlyList<string> logLines)
    {
        if (logLines.Count == 0) return null;

        for (var i = logLines.Count - 1; i >= 0; i--)
        {
            var line = logLines[i];
            var m1 = RxFailedPassed.Match(line);
            if (m1.Success)
            {
                var failed = m1.Groups[1].Value;
                var passed = m1.Groups[2].Value;
                return $"Escenarios: {passed} OK, {failed} fallidos (ver informe).";
            }

            var m2 = RxTotalFailed.Match(line);
            if (m2.Success)
            {
                var total = m2.Groups[1].Value;
                var failed = m2.Groups[2].Value;
                return $"Pruebas: {total} total, {failed} fallidas (ver informe).";
            }
        }

        return null;
    }
}
