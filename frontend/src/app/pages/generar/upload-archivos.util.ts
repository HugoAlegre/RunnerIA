/** Formatos y validación para la zona de carga en Generar. */

export const EXTENSIONES_GHERKIN = ['.feature'] as const;
export const EXTENSIONES_DOCS = ['.txt', '.md', '.csv', '.json', '.log', '.docx', '.pdf'] as const;
export const EXTENSIONES_IMAGEN = ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'] as const;

export const MAX_ARCHIVOS = 20;
export const MAX_BYTES_POR_ARCHIVO = 15 * 1024 * 1024; // 15 MB

export type TipoArchivoUpload = 'gherkin' | 'doc' | 'imagen' | 'rechazado';

const TODAS_PERMITIDAS = new Set<string>([
  ...EXTENSIONES_GHERKIN,
  ...EXTENSIONES_DOCS,
  ...EXTENSIONES_IMAGEN
]);

const RECHAZADAS_CON_MENSAJE: Record<string, string> = {
  '.doc': 'Word antiguo (.doc). Convertí a .docx.',
  '.xlsx': 'Excel no soportado. Exportá a PDF o pegá el texto.',
  '.xls': 'Excel no soportado. Exportá a PDF o pegá el texto.',
  '.zip': 'ZIP no soportado. Extraé el .feature o documentación.',
  '.rar': 'Archivo comprimido no soportado.',
  '.exe': 'Ejecutable no permitido.'
};

export function extensionDeArchivo(nombre: string): string {
  const i = (nombre || '').lastIndexOf('.');
  if (i < 0) return '';
  return nombre.slice(i).toLowerCase();
}

export function esExtensionPermitida(nombre: string): boolean {
  return TODAS_PERMITIDAS.has(extensionDeArchivo(nombre));
}

export function tipoArchivoUpload(nombre: string): TipoArchivoUpload {
  const ext = extensionDeArchivo(nombre);
  if (EXTENSIONES_GHERKIN.includes(ext as (typeof EXTENSIONES_GHERKIN)[number])) return 'gherkin';
  if (EXTENSIONES_DOCS.includes(ext as (typeof EXTENSIONES_DOCS)[number])) return 'doc';
  if (EXTENSIONES_IMAGEN.includes(ext as (typeof EXTENSIONES_IMAGEN)[number])) return 'imagen';
  return 'rechazado';
}

export function motivoRechazo(nombre: string): string {
  const ext = extensionDeArchivo(nombre);
  if (RECHAZADAS_CON_MENSAJE[ext]) return RECHAZADAS_CON_MENSAJE[ext];
  if (!ext) return 'Sin extensión reconocible.';
  return `Formato «${ext}» no soportado. Usá .feature, PDF, .docx, TXT, MD o imagen.`;
}

export function acceptAtributoInput(): string {
  return [
    ...EXTENSIONES_GHERKIN,
    ...EXTENSIONES_DOCS,
    ...EXTENSIONES_IMAGEN,
    'text/plain',
    'text/markdown',
    'application/json',
    'application/pdf',
    'image/*'
  ].join(',');
}

const RX_MOJIBAKE = /Ã.|â€|Â.|contraseÃ±a|operaciÃ³n|supervisiÃ³n/;

/** Feature / Característica (español e inglés). */
export const RX_GHERKIN_FEATURE = /^\s*(?:Feature|Caracter[ií]stica)\s*:/im;
/** Scenario / Escenario. */
export const RX_GHERKIN_SCENARIO =
  /^\s*(?:Scenario(?: Outline)?|Escenario(?: Outline)?|Esquema del escenario)\s*:/im;
/** Pasos Given/When/Then o Dado/Cuando/Entonces. */
export const RX_GHERKIN_STEP =
  /^\s*(?:Given|When|Then|And|But|Dado|Cuando|Entonces|Y|Pero)\s+\S+/im;

/** True si el texto parece un .feature ejecutable (Feature/Característica + Escenario + pasos). */
export function esGherkinListo(texto: string): boolean {
  const t = (texto || '').trim();
  if (!t) return false;
  return RX_GHERKIN_FEATURE.test(t) && RX_GHERKIN_SCENARIO.test(t) && RX_GHERKIN_STEP.test(t);
}

export function contarFeaturesGherkin(texto: string): number {
  const m = (texto || '').match(/^\s*(?:Feature|Caracter[ií]stica)\s*:/gim);
  return m ? m.length : 0;
}

export function extraerTituloFeature(gherkin: string): string {
  const m = (gherkin || '').match(/^\s*(?:Feature|Caracter[ií]stica)\s*:\s*(.+)$/im);
  return m ? m[1].trim() : '';
}

export function extraerEscenarioFeature(gherkin: string): string {
  const m = (gherkin || '').match(
    /^\s*(?:Scenario(?: Outline)?|Escenario(?: Outline)?)\s*:\s*(.+)$/im
  );
  return m ? m[1].trim() : '';
}

export function detectarMojibake(texto: string): boolean {
  return RX_MOJIBAKE.test(texto || '');
}

export function sugerirLanguageEs(texto: string): boolean {
  const t = texto || '';
  if (/^\s*#\s*language:\s*es\b/im.test(t)) return false;
  return /\b(Característica|Antecedentes|Escenario|Dado|Cuando|Entonces)\b/.test(t);
}

export function leerArchivoComoTexto(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error ?? new Error('No se pudo leer el archivo'));
    reader.readAsText(file, 'UTF-8');
  });
}

export interface ResultadoClasificarArchivos {
  gherkin: File[];
  docs: File[];
  rechazados: Array<{ nombre: string; motivo: string }>;
  demasiadoGrandes: string[];
  duplicados: string[];
}

export function clasificarArchivosEntrada(
  files: File[],
  docsActuales: File[],
  cupoDocs: number
): ResultadoClasificarArchivos {
  const gherkin: File[] = [];
  const docs: File[] = [];
  const rechazados: Array<{ nombre: string; motivo: string }> = [];
  const demasiadoGrandes: string[] = [];
  const duplicados: string[] = [];
  const vistos = new Set(docsActuales.map((d) => `${d.name}|${d.size}`));

  for (const f of files) {
    if (f.size > MAX_BYTES_POR_ARCHIVO) {
      demasiadoGrandes.push(f.name);
      continue;
    }
    const key = `${f.name}|${f.size}`;
    if (vistos.has(key)) {
      duplicados.push(f.name);
      continue;
    }
    vistos.add(key);

    const tipo = tipoArchivoUpload(f.name);
    if (tipo === 'rechazado') {
      rechazados.push({ nombre: f.name, motivo: motivoRechazo(f.name) });
      continue;
    }
    if (tipo === 'gherkin') gherkin.push(f);
    else docs.push(f);
  }

  const cupo = Math.max(0, cupoDocs);
  if (docs.length > cupo) {
    const fuera = docs.splice(cupo);
    for (const f of fuera) {
      rechazados.push({
        nombre: f.name,
        motivo: `Límite de ${MAX_ARCHIVOS} archivos de documentación alcanzado.`
      });
    }
  }

  return { gherkin, docs, rechazados, demasiadoGrandes, duplicados };
}

export function badgeTipoArchivo(nombre: string): string {
  const t = tipoArchivoUpload(nombre);
  if (t === 'gherkin') return 'gherkin';
  if (t === 'imagen') return 'imagen';
  return 'doc';
}
