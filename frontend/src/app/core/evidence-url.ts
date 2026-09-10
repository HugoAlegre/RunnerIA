/**
 * URL relativa limpia (sin token). Preferir descarga/informe vía HttpClient
 * (header X-Runner-Auth). No poner el PIN/sesión en query: queda en historial y logs.
 */
export function evidenceUrlWithAuth(path: string, cacheKey?: string): string {
  if (!path || path === '#') return path;
  const clean = path.split('?')[0];
  if (!cacheKey) return clean;
  const qs = new URLSearchParams();
  qs.set('t', String(cacheKey));
  return `${clean}?${qs.toString()}`;
}
