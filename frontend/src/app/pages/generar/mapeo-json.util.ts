/** JSON exportado desde Mapeo UI → borrador Gherkin en Generar. */

import { esGherkinListo } from './upload-archivos.util';

export interface MapeoUiJsonExport {
  formato?: string;
  gherkin?: string;
  sesionId?: string;
  urlInicio?: string;
  eventos?: Array<Record<string, unknown>>;
}

export function parseMapeoUiJson(texto: string): MapeoUiJsonExport | null {
  const raw = (texto || '').trim();
  if (!raw.startsWith('{')) return null;
  try {
    const o = JSON.parse(raw) as MapeoUiJsonExport;
    if (o.formato === 'mapeo-ui-v1') return o;
    if (Array.isArray(o.eventos)) return o;
    return null;
  } catch {
    return null;
  }
}

/** Devuelve Gherkin listo para vista previa en Generar, o null si no es mapeo. */
export function extraerGherkinDeMapeoJson(texto: string): string | null {
  const o = parseMapeoUiJson(texto);
  if (!o) return null;
  const gh = (o.gherkin || '').trim();
  if (gh && esGherkinListo(gh)) return gh;
  if (gh) return gh;
  return null;
}

export function mensajeMapeoSinGherkin(nombreArchivo: string): string {
  return (
    `«${nombreArchivo}» es export de Mapeo UI pero no trae Gherkin. ` +
    'Volvé a Mapeo UI, grabá clics y exportá de nuevo (JSON actualizado).'
  );
}

export function esArchivoMapeoUi(texto: string): boolean {
  return parseMapeoUiJson(texto) !== null;
}
